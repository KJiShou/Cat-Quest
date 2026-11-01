param([Parameter(Mandatory=$true)][string]$BuildRoot,[string]$PreparedPackages)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$build=[IO.Path]::GetFullPath($BuildRoot)
if(!$build.StartsWith((Join-Path $repo 'Builds/UpdateVerification/').Replace('/','\'),[StringComparison]::OrdinalIgnoreCase)){throw 'Use only the isolated UpdateVerification build directory.'}
$exe=Join-Path $build '1.2/CatQuest.exe'
$folder=Join-Path $repo ('Library/UpdaterVerification/windows-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
[IO.Directory]::CreateDirectory((Join-Path $folder 'update'))|Out-Null
$utf8=New-Object Text.UTF8Encoding($false)
$checks=[Collections.Generic.List[string]]::new()
$registryPath='Software\DefaultCompany\Cat Quest';$backup=@();$existed=$false;$saveLock=$null;$failure=$null
function Json($path,$value){[IO.File]::WriteAllText($path,($value|ConvertTo-Json -Depth 30),$utf8)}
function Read($path){for($attempt=0;$attempt -lt 8;$attempt++){try{$value=Get-Content -LiteralPath $path -Raw | ConvertFrom-Json;if($null -ne $value){return $value}}catch{};Start-Sleep -Milliseconds 25};return $null}
function Check($condition,$message){if(!$condition){throw $message};$checks.Add($message);Write-Output "PASS: $message"}
function Wait([scriptblock]$condition,[int]$seconds=40){$until=[DateTime]::UtcNow.AddSeconds($seconds);do{if(&$condition){return};Start-Sleep -Milliseconds 200}while([DateTime]::UtcNow -lt $until);throw 'Timed out waiting for native update check.'}
$statePath=Join-Path $folder 'smoke-result.json'
function Command($command){$path=Join-Path $folder 'smoke-command.json';Json $path $command;Wait {!(Test-Path -LiteralPath $path)};$consumed=[DateTime]::UtcNow;Wait {(Get-Item -LiteralPath $statePath).LastWriteTimeUtc -gt $consumed};return Read $statePath}
try {
    $key=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($registryPath)
    if($key){$existed=$true;try{foreach($name in $key.GetValueNames()){$backup+=@{name=$name;kind=$key.GetValueKind($name);value=$key.GetValue($name)}}}finally{$key.Dispose()}}
    [IO.File]::WriteAllText((Join-Path $folder 'notes.txt'),'Isolated Windows update test')
    if(!$PreparedPackages){
        & (Join-Path $PSScriptRoot 'Publish-CatQuestPackage.ps1') -BuildDirectory (Join-Path $build '1.2') -OutputDirectory (Join-Path $folder 'baseline') -NotesFile (Join-Path $folder 'notes.txt') -AllowDevelopmentBuild
        $PreparedPackages=Join-Path $folder 'published'
        & (Join-Path $PSScriptRoot 'Publish-CatQuestPackage.ps1') -BuildDirectory (Join-Path $build '1.2.1') -OutputDirectory $PreparedPackages -NotesFile (Join-Path $folder 'notes.txt') -AllowDevelopmentBuild
    }
    Copy-Item -LiteralPath (Join-Path $PreparedPackages 'CatQuest-1.2.1-win-x64.zip') -Destination (Join-Path $folder 'update/package.zip')
    Copy-Item -LiteralPath (Join-Path $PreparedPackages 'catquest-update.json') -Destination (Join-Path $folder 'update/release.json')
    Json (Join-Path $folder 'test-save.json') @{version=5;username='Update Test';experience=3000;tickets=3;fishCoins=47;settings=@{notificationsEnabled=$false;soundVolume=0;musicVolume=0}}
    [IO.File]::WriteAllText((Join-Path $build '1.2/personal.txt'),'preserve')
    $arguments='--focus-smoke --smoke-dir "'+$folder+'" -force-d3d11 -logFile "'+$folder+'/Player.log" -screen-fullscreen 0 -screen-width 1280 -screen-height 720'
    $main=Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory ([IO.Path]::GetDirectoryName($exe)) -WindowStyle Hidden -PassThru
    Wait {(Read $statePath).controlsBound}
    $r=Command @{action='updates-prepare'};Check ($r.updateState -eq 'Ready') 'Verified full package ready in real Windows player'
    $r=Command @{action='configure';mode=2};$r=Command @{action='start'}
    $r=Command @{action='updates-install'};Check ($r.phase -eq 'Focusing' -and $r.updateState -eq 'Ready' -and !$main.HasExited) 'Active Focus blocks restart without cancelling or awarding'
    $r=Command @{action='advance';seconds=120};$r=Command @{action='end'}
    Check ($r.phase -eq 'Finished' -and $r.exp -gt 3000) 'Countdown End Early keeps its existing rewards and finishes normally'
    $r=Command @{action='configure';mode=0;focusMinutes=1;restMinutes=1};$r=Command @{action='start'};$r=Command @{action='advance';seconds=60}
    Check ($r.phase -eq 'Resting') 'Completed Pomodoro enters Rest under existing rules'
    $r=Command @{action='updates-install'};Check ($r.phase -eq 'Resting' -and $r.updateState -eq 'Ready') 'Rest blocks restart without changing settlement'
    $r=Command @{action='rest'}
    $saveLock=[IO.File]::Open((Join-Path $folder 'test-save.json'),[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
    $r=Command @{action='updates-install'};Check (!$main.HasExited -and $r.updateState -eq 'Ready' -and $r.updateMessage -match 'Could not restart') 'Save failure prevents shutdown and supports retry'
    $saveLock.Dispose();$saveLock=$null
    $r=Command @{action='widget'}
    $viewPath=Join-Path $r.companionDirectory 'companion-window.json'
    Wait {(Read $viewPath).connected}
    $child=@(Get-CimInstance Win32_Process | Where-Object {$_.ParentProcessId -eq $main.Id -and $_.ExecutablePath -eq $exe})
    Check ($child.Count -eq 1) 'Owned Companion process connected before update'
    $before=Read (Join-Path $folder 'test-save.json')
    $r=Command @{action='updates-install'}
    Wait {$main.HasExited} 30
    Wait {(Test-Path -LiteralPath (Join-Path $folder 'update/result.txt'))} 60
    Check ((Get-Content -LiteralPath (Join-Path $folder 'update/result.txt') -Raw) -match '^Installed 1.2.1') 'Standalone helper installed full Unity release'
    Wait {(Read $statePath).appVersion -eq '1.2.1'} 60
    Check (!(Get-Process -Id $child[0].ProcessId -ErrorAction SilentlyContinue)) 'Owned Companion exited before file replacement'
    $after=Read (Join-Path $folder 'test-save.json')
    Check ($after.username -eq $before.username -and $after.experience -eq $before.experience -and $after.fishCoins -eq $before.fishCoins -and $after.tickets -eq $before.tickets -and $after.tasks.Count -eq $before.tasks.Count) 'Tasks, identity, rewards and progression survived restart'
    Check ((Get-Content -LiteralPath (Join-Path $build '1.2/personal.txt')) -eq 'preserve') 'Unrelated installation file preserved'
    $r=Command @{action='widget'};$viewPath=Join-Path $r.companionDirectory 'companion-window.json';Wait {(Read $viewPath).connected}
    Check ((Read $statePath).widgetOpen) 'Companion opens after update'
} catch {$failure=$_.ToString();Write-Output "FAIL: $failure"}
finally {
    if($saveLock){$saveLock.Dispose()}
    foreach($helper in @(Get-CimInstance Win32_Process | Where-Object {$_.ExecutablePath -eq (Join-Path $folder 'update/CatQuestUpdater.exe')})) {try{Stop-Process -Id $helper.ProcessId -ErrorAction Stop}catch{}}
    foreach($owned in @(Get-CimInstance Win32_Process | Where-Object {$_.ExecutablePath -eq $exe})) {try{Stop-Process -Id $owned.ProcessId -ErrorAction Stop}catch{}}
    $key=[Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($registryPath)
    try{foreach($name in $key.GetValueNames()){if($name -notin $backup.name){$key.DeleteValue($name,$false)}};foreach($v in $backup){$key.SetValue($v.name,$v.value,$v.kind)}}finally{$key.Dispose()}
    if(!$existed){[Microsoft.Win32.Registry]::CurrentUser.DeleteSubKey($registryPath,$false)}
    Json (Join-Path $folder 'result.json') @{utc=[DateTime]::UtcNow.ToString('O');passed=($null -eq $failure);checks=$checks;failure=$failure;isolatedSave=$true;restoredPlayerPrefs=$true;buildRoot=$build}
    Write-Output "Evidence: $folder"
}
if($failure){exit 1}
