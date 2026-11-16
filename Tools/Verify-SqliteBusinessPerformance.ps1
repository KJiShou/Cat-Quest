param(
    [Parameter(Mandatory=$true)][string]$Executable,
    [ValidateSet('SQLite','JSON')][string]$Engine='SQLite',
    [string]$OutputRoot
)
$ErrorActionPreference='Stop'
$exe=(Resolve-Path -LiteralPath $Executable).Path
$identity=Get-Content -LiteralPath (Join-Path ([IO.Path]::GetDirectoryName($exe)) 'catquest-build.json') -Raw | ConvertFrom-Json
if(!$identity.development -or $identity.productName -notlike 'CatQuest SQLite Verification*'){throw 'Isolated Development build required.'}
$root=if([string]::IsNullOrWhiteSpace($OutputRoot)){Join-Path (Get-Location) 'Temp/SQLiteMigrationVerification'}else{
    if(![IO.Path]::IsPathRooted($OutputRoot)){throw 'An absolute verification output root is required.'}
    [IO.Path]::GetFullPath($OutputRoot)
}
$folder=Join-Path $root ('business-'+$Engine+'-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $folder | Out-Null
Set-Content -LiteralPath (Join-Path $folder 'sqlite-performance-authorized.txt') 'Explicit isolated business command benchmark'
$player=$null
try {
    $arguments=@('--focus-smoke','--sqlite-performance','--sqlite-business-performance','--benchmark-engine',$Engine,'--smoke-dir',('"'+$folder+'"'),'-screen-fullscreen','0','-screen-width','1280','-screen-height','720','-logFile',('"'+(Join-Path $folder 'Player.log')+'"'))
    $player=Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory ([IO.Path]::GetDirectoryName($exe)) -WindowStyle Hidden -PassThru
    $null=$player.Handle
    @{executable=$exe;engine=$Engine;utc=[DateTime]::UtcNow.ToString('O');pid=$player.Id;directory=$folder} | ConvertTo-Json | Set-Content Temp/sqlite-business-performance-current.json
    $deadline=[DateTime]::UtcNow.AddHours(2)
    while(!$player.HasExited -and [DateTime]::UtcNow -lt $deadline){Start-Sleep -Seconds 1}
    if(!$player.HasExited){throw 'Isolated business performance run exceeded two hours.'}
    $player.WaitForExit()
    $output=(Get-Content -LiteralPath (Join-Path $folder 'sqlite-performance-run.txt') -Raw).Trim()
    $result=Get-Content -LiteralPath (Join-Path $output 'result.txt') -Raw
    Write-Output $result
    Write-Output ('Evidence: '+$output)
    if($player.ExitCode -ne 0 -or $result -notmatch 'PASS'){throw 'Native business performance verification failed.'}
} finally {
    if($player){if(!$player.HasExited){$player.Kill();$null=$player.WaitForExit(5000)};$player.Dispose()}
}
