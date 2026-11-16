param([Parameter(Mandatory=$true)][string]$Executable, [Parameter(Mandatory=$true)][string]$Fixture,
 [ValidateRange(640,3840)][int]$Width=1280, [ValidateRange(480,2160)][int]$Height=720)
$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$exe = [IO.Path]::GetFullPath($Executable)
$fixturePath = [IO.Path]::GetFullPath($Fixture)
if (!(Test-Path -LiteralPath $exe) -or !(Test-Path -LiteralPath $fixturePath)) { throw 'Build and explicit isolated fixture are required.' }
$identityPath = Join-Path ([IO.Path]::GetDirectoryName($exe)) 'catquest-build.json'
$identity = [IO.File]::ReadAllText($identityPath) | ConvertFrom-Json
if (!$identity.development -or $identity.productName -notmatch '^CatQuest SQLite Verification( |$)') { throw 'Use an isolated Development build with verificationProductName CatQuest SQLite Verification.' }
foreach ($existing in @(Get-CimInstance Win32_Process -Filter "Name='CatQuest.exe'")) {
 if ($existing.ExecutablePath -and [string]::Equals([IO.Path]::GetFullPath($existing.ExecutablePath),$exe,[StringComparison]::OrdinalIgnoreCase)) { throw 'This exact verification executable is already running. Use a fresh isolated build.' }
}
$folder = Join-Path $workspace ('Temp/SQLiteMigrationVerification/native-'  + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmssfff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
if (Test-Path -LiteralPath $folder) { throw 'Verification directory must be new.' }
[IO.Directory]::CreateDirectory($folder) | Out-Null
$utf8 = [Text.UTF8Encoding]::new($false)
$player = $null; $failure = $null
function ReadReport {
 try { return [IO.File]::ReadAllText((Join-Path $folder 'save-player-result.json')) | ConvertFrom-Json } catch { return $null }
}
try {
 Copy-Item -LiteralPath $fixturePath -Destination (Join-Path $folder 'test-save.json')
 [IO.File]::WriteAllText((Join-Path $folder 'save-probe-authorized.txt'),'Explicit isolated native save verification',$utf8)
 foreach($phase in @('measure','restart')) {
  if($phase -eq 'restart') { Move-Item -LiteralPath (Join-Path $folder 'save-player-result.json') -Destination (Join-Path $folder 'measurement.json') }
  $argsList = @('--focus-smoke','--save-smoke','--smoke-dir',('"'+$folder+'"'),'-screen-fullscreen','0','-screen-width',"$Width",'-screen-height',"$Height",'-logFile',('"'+(Join-Path $folder ($phase+'-player.log'))+'"'))
  if($phase -eq 'restart') { $argsList += '--save-restart' }
  # This is the visible interactive Player under test. Hidden startup followed by
  # ShowWindow triggers Unity 6000.3.10f1 D3D11 presentation errors on this machine.
  $player = Start-Process -FilePath $exe -ArgumentList $argsList -WorkingDirectory ([IO.Path]::GetDirectoryName($exe)) -WindowStyle Normal -PassThru
  # Retain the native handle while alive, as in the updater driver. Refresh after
  # process exit must not lose the handle needed to obtain the real exit code.
  $null=$player.Handle
  $deadline = [DateTime]::UtcNow.AddSeconds(210); $started=$false
  while(!$player.HasExited -and [DateTime]::UtcNow -lt $deadline) {
   $state = ReadReport
   if($phase -eq 'measure' -and $state.phase -eq 'READY' -and !$started) {
    Start-Sleep -Milliseconds 500
    [IO.File]::WriteAllText((Join-Path $folder 'go.request'),'go',$utf8); $started=$true
   }
   Start-Sleep -Milliseconds 150; $player.Refresh()
  }
  if(!$player.HasExited) { throw "Native $phase timed out." }
  $player.WaitForExit()
  $exitCode=$player.ExitCode
  $state = ReadReport
  $expected = if($phase -eq 'measure'){'RESTART-REQUIRED'}else{'RESTART-PASS'}
  if($null -eq $exitCode -or $exitCode -ne 0 -or !$state.passed -or $state.phase -ne $expected) { throw "Native $phase failed: exit=$exitCode, $($state.failure)" }
  $player.Dispose(); $player=$null
 }
} catch { $failure=$_.ToString(); Write-Warning $failure }
finally {
 if($player) { if(!$player.HasExited) { $player.Kill(); $player.WaitForExit(10000) | Out-Null }; $player.Dispose() }
 @{utc=[DateTime]::UtcNow.ToString('O');passed=($null -eq $failure);failure=$failure;livePlayerPreferencesIsolatedByProductName=$true;executable=$exe;fixture=$fixturePath} | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $folder 'driver-result.json') -Encoding UTF8
 Write-Output "Evidence: $folder"
}
if($failure){exit 1}
