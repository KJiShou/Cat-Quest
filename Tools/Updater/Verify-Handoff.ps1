$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$folder = Join-Path $repo ('Library/UpdaterVerification/handoff-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
$installed=Join-Path $folder 'installed'
$release=Join-Path $folder 'release'
$work=Join-Path $folder 'work'
foreach($directory in @($installed,$release,$work)){[IO.Directory]::CreateDirectory($directory)|Out-Null}
& (Join-Path $PSScriptRoot 'Build-Updater.ps1') -OutputDirectory $installed
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
& $compiler /nologo /target:winexe /main:HandoffFixture "/out:$installed/CatQuest.exe" /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /r:System.Windows.Forms.dll (Join-Path $repo 'Assets/Scripts/Updates/UpdateManifest.cs') (Join-Path $PSScriptRoot 'Program.cs') (Join-Path $PSScriptRoot 'HandoffFixture.cs')
if($LASTEXITCODE -ne 0){throw 'Handoff fixture compilation failed.'}
[IO.File]::WriteAllText((Join-Path $installed 'UnityPlayer.dll'),'fixture')
[IO.File]::WriteAllText((Join-Path $installed 'catquest-build.json'),'{"version":"1.2","development":true}')
[IO.File]::WriteAllText((Join-Path $folder 'notes.txt'),'Isolated updater handoff test')
& (Join-Path $repo 'Tools/Publish-CatQuestPackage.ps1') -BuildDirectory $installed -OutputDirectory (Join-Path $folder 'baseline') -NotesFile (Join-Path $folder 'notes.txt') -AllowDevelopmentBuild
Copy-Item -Path "$installed/*" -Destination $release
[IO.File]::WriteAllText((Join-Path $release 'catquest-build.json'),'{"version":"1.2.1","development":true}')
& (Join-Path $repo 'Tools/Publish-CatQuestPackage.ps1') -BuildDirectory $release -OutputDirectory (Join-Path $folder 'published') -NotesFile (Join-Path $folder 'notes.txt') -AllowDevelopmentBuild
Copy-Item -LiteralPath (Join-Path $folder 'published/CatQuest-1.2.1-win-x64.zip') -Destination (Join-Path $work 'package.zip')
Copy-Item -LiteralPath (Join-Path $folder 'published/catquest-update.json') -Destination (Join-Path $work 'release.json')
Copy-Item -LiteralPath (Join-Path $installed 'CatQuestUpdater.exe') -Destination $work
[IO.File]::WriteAllText((Join-Path $installed 'personal.txt'),'must stay')
$parent=Start-Process -FilePath (Join-Path $installed 'CatQuest.exe') -ArgumentList "`"$work`"" -WindowStyle Hidden -PassThru
try {
    $deadline=[DateTime]::UtcNow.AddSeconds(40)
    while([DateTime]::UtcNow -lt $deadline -and !(Test-Path -LiteralPath (Join-Path $installed 'restarted.txt'))){Start-Sleep -Milliseconds 200}
    if(!(Test-Path -LiteralPath (Join-Path $installed 'restarted.txt'))){throw 'Updater did not restart the fixture.'}
    $inventory=Get-Content -LiteralPath (Join-Path $installed 'catquest-package.json') -Raw | ConvertFrom-Json
    if($inventory.version -ne '1.2.1' -or (Get-Content -LiteralPath (Join-Path $installed 'personal.txt')) -ne 'must stay'){throw 'Updated version or unrelated file preservation failed.'}
    $originalHash=(Get-FileHash -LiteralPath (Join-Path $installed 'CatQuest.exe')).Hash
    $bad=Get-Content -LiteralPath (Join-Path $work 'request.json') -Raw | ConvertFrom-Json
    $bad.parentPid=$PID;$bad.parentStartedUtcTicks=0
    [IO.File]::WriteAllText((Join-Path $work 'request.json'),($bad|ConvertTo-Json))
    [IO.File]::Delete((Join-Path $work 'result.txt'))
    $rejected=Start-Process -FilePath (Join-Path $work 'CatQuestUpdater.exe') -ArgumentList ('"'+(Join-Path $work 'request.json')+'"') -WindowStyle Hidden -PassThru
    try {
        $deadline=[DateTime]::UtcNow.AddSeconds(10)
        while([DateTime]::UtcNow -lt $deadline -and !(Test-Path -LiteralPath (Join-Path $work 'result.txt'))){Start-Sleep -Milliseconds 100}
        if((Get-Content -LiteralPath (Join-Path $work 'result.txt') -Raw) -notmatch 'owner does not match'){throw 'Wrong/stale owner was not rejected.'}
        if((Get-FileHash -LiteralPath (Join-Path $installed 'CatQuest.exe')).Hash -ne $originalHash){throw 'Rejected request mutated installation.'}
    } finally {if(!$rejected.HasExited){$rejected.Kill()};$rejected.Dispose()}
    [IO.File]::WriteAllText((Join-Path $folder 'result.txt'),([DateTime]::UtcNow.ToString('O')+' PASS: release packaging, owner PID/start-time handshake, wrong/stale owner rejection without mutation, parent exit, standalone replacement, helper self-update, restart, unrelated file preserved. Disposable process fixture, not Unity.'))
    Get-Content -LiteralPath (Join-Path $folder 'result.txt')
} finally {if(!$parent.HasExited){$parent.Kill()};$parent.Dispose()}
