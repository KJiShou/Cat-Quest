param([Parameter(Mandatory=$true)][string]$Executable)
$ErrorActionPreference='Stop'
$exe=(Resolve-Path -LiteralPath $Executable).Path
$identity=Get-Content -LiteralPath (Join-Path ([IO.Path]::GetDirectoryName($exe)) 'catquest-build.json') -Raw | ConvertFrom-Json
if(!$identity.development -or $identity.productName -notlike 'CatQuest SQLite Verification*'){throw 'Isolated Development build required.'}
$folder=Join-Path (Get-Location) ('Temp/SQLiteMigrationVerification/lifecycle-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $folder | Out-Null
Set-Content -LiteralPath (Join-Path $folder 'lifecycle-authorized.txt') 'Authorized isolated two-main-process and shutdown verification'
$children=[Collections.Generic.List[Diagnostics.Process]]::new()
function Start-Probe([string]$mode){
    $arguments=@('--focus-smoke','--sqlite-lifecycle','--lifecycle-mode',$mode,'--smoke-dir',('"'+$folder+'"'),'-screen-fullscreen','0','-screen-width','1280','-screen-height','720','-logFile',('"'+(Join-Path $folder ($mode+'.log'))+'"'))
    # This is an interactive native-dialog verification: the owner window and
    # its retry notice must be visible for the requested rendered review.
    $style=if($mode -eq 'owner'){'Normal'}else{'Hidden'}
    $process=Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory ([IO.Path]::GetDirectoryName($exe)) -WindowStyle $style -PassThru
    $null=$process.Handle;$children.Add($process);return $process
}
function Wait-Text([string]$mode,[string]$pattern,[int]$seconds){
    $path=Join-Path $folder ($mode+'-lifecycle.txt');$until=[DateTime]::UtcNow.AddSeconds($seconds)
    while([DateTime]::UtcNow -lt $until){
        if(Test-Path -LiteralPath $path){$text=Get-Content -LiteralPath $path -Raw;if($text -match 'FAIL'){throw $text};if($text -match $pattern){return $text}}
        Start-Sleep -Milliseconds 250
    }
    throw "Timed out waiting for $mode $pattern"
}
try {
    $owner=Start-Probe 'owner';$null=Wait-Text 'owner' 'READY' 45
    $contender=Start-Probe 'contender';$null=Wait-Text 'contender' 'PASS second' 45
    if(!$contender.WaitForExit(15000)){throw 'Rejected second instance did not close cleanly.'}
    if($contender.ExitCode -ne 0){throw 'Rejected second instance returned an error exit code.'}
    @{directory=$folder;ownerPid=$owner.Id;contenderPid=$contender.Id;utc=[DateTime]::UtcNow.ToString('O')} | ConvertTo-Json | Set-Content Temp/sqlite-lifecycle-current.json
    Set-Content -LiteralPath (Join-Path $folder 'begin-close.request') ''
    $null=Wait-Text 'owner' 'COMMIT held' 15
    Write-Output ('Waiting for the native retry notice; inspect and acknowledge it in this isolated Player. Evidence: '+$folder)
    $null=Wait-Text 'owner' 'PASS timeout' 90
    if(!$owner.WaitForExit(15000)){throw 'Owner retry did not close cleanly.'}
    if($owner.ExitCode -ne 0){throw 'Owner returned an error exit code.'}
    @{passed=$true;utc=[DateTime]::UtcNow.ToString('O');executable=$exe;checks=@('second real main process rejects shared save','pending command survives ten-second quit timeout','rendered frames continue while waiting','native retry notice','command acceptance resumes','retry closes cleanly')} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $folder 'result.json')
    Write-Output ('PASS '+$folder)
} finally {
    foreach($child in $children){if(!$child.HasExited){$child.Kill();$null=$child.WaitForExit(5000)};$child.Dispose()}
}
