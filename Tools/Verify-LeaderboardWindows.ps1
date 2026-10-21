param([Parameter(Mandatory=$true)][string]$Executable,[switch]$Run)
$ErrorActionPreference='Stop'
if(-not $Run){'Add -Run to launch the interactive, isolated Windows verification.';return}
$workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$exe=[IO.Path]::GetFullPath($Executable)
if(-not(Test-Path -LiteralPath $exe)){throw 'Build not found'}
$out=Join-Path $workspace ('Temp/LeaderboardWindows/'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $out -Force|Out-Null
[IO.File]::WriteAllText((Join-Path $out 'account-test.marker'),'Isolated Windows rendering verification; no Firebase writes.')
$checks=[Collections.Generic.List[string]]::new();$failure=$null;$player=$null
function State { $p=Join-Path $out 'account-result.json';if(Test-Path $p){try{return [IO.File]::ReadAllText($p)|ConvertFrom-Json}catch{}};return $null }
function Check($value,[string]$message){if(-not $value){throw $message};$checks.Add($message)}
function Command([string]$action,[string]$name=''){
 $p=Join-Path $out 'account-command.json';Check (-not(Test-Path $p)) 'No pending command'
 [IO.File]::WriteAllText($p,(@{action=$action;name=$name}|ConvertTo-Json -Compress))
 $end=[DateTime]::UtcNow.AddSeconds(10)
 while(Test-Path $p){if([DateTime]::UtcNow -gt $end){throw ('Command timed out: '+$action)};Start-Sleep -Milliseconds 100}
}
try{
 foreach($size in @(@(1920,1080),@(1280,720))){
  $suffix=($size[0].ToString()+'x'+$size[1]);$statePath=Join-Path $out 'account-result.json'
  if(Test-Path $statePath){Remove-Item -LiteralPath $statePath}
  & (Join-Path $PSScriptRoot 'Start-AccountVerification.ps1') -Executable $exe -Directory $out -Width $size[0] -Height $size[1] | Out-Null
  $info=Get-Content (Join-Path $out 'process.json') -Raw|ConvertFrom-Json;$player=Get-Process -Id $info.pid
  $end=[DateTime]::UtcNow.AddSeconds(40)
  do{$state=State;if($state -and $state.ready){break};if([DateTime]::UtcNow -gt $end){throw 'Player did not become ready'};Start-Sleep -Milliseconds 200}while($true)
  foreach($kind in @('BossStage','TasksCompleted','FocusMinutes','Power')){
   Command 'preview' $kind;Start-Sleep -Seconds 1;Command 'capture';Start-Sleep -Seconds 1
   $state=State;Check ($state.presentationWarnings -eq 0) "$suffix ${kind}: no presentation warnings"
   Check (-not $state.developmentConsoleVisible) "$suffix ${kind}: Development Console remains closed"
   $capture=Join-Path $out 'presentation-capture.png';Check (Test-Path $capture) "$suffix ${kind}: screenshot captured"
   Copy-Item -LiteralPath $capture -Destination (Join-Path $out ($kind+'-'+$suffix+'.png'))
  }
  $w=[SettingsSmokeNative]::Windows()|Where-Object {$_.ProcessId -eq $player.Id -and $_.ClassName -eq 'UnityWndClass'}|Select-Object -First 1
  Check ($null -ne $w) 'Own Unity window found'
  [SettingsSmokeNative]::ShowWindow([IntPtr]$w.Handle,6)|Out-Null;Start-Sleep -Seconds 1
  Check ([SettingsSmokeNative]::IsIconic([IntPtr]$w.Handle)) "$suffix minimized"
  [SettingsSmokeNative]::ShowWindow([IntPtr]$w.Handle,9)|Out-Null;[SettingsSmokeNative]::SetForegroundWindow([IntPtr]$w.Handle)|Out-Null;Start-Sleep -Seconds 2
  $state=State;Check ($state.presentationWarnings -eq 0 -and -not $state.developmentConsoleVisible) "$suffix restore: no presentation warnings or console"
  Copy-Item -LiteralPath $statePath -Destination (Join-Path $out ('state-'+$suffix+'.json'))
  Command 'quit';if(-not $player.WaitForExit(10000)){throw 'Player did not quit'};$player=$null
 }
 $logs=Get-ChildItem -LiteralPath $out -Filter 'Player-*.log'
 Check (@(Select-String -Path $logs.FullName -Pattern 'PresentFrame|WaitForLastPresentation|NullReferenceException|ArgumentException').Count -eq 0) 'Complete player logs contain no presentation warnings or managed exceptions'
}catch{$failure=$_.Exception.Message}
finally{
 if($player -and -not $player.HasExited){$player.CloseMainWindow()|Out-Null;if(-not $player.WaitForExit(5000)){$player.Kill();$player.WaitForExit()}}
 $backup=Join-Path $out 'playerprefs-before.clixml'
 if(Test-Path $backup){
  $before=Import-Clixml $backup;$registryPath='Software\DefaultCompany\Cat Quest';$key=[Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($registryPath)
  try{foreach($name in $key.GetValueNames()){if($name -notin $before.values.name){$key.DeleteValue($name,$false)}};foreach($saved in $before.values){$kind=[Microsoft.Win32.RegistryValueKind]::$($saved.kind);$value=$saved.value;if($kind -eq 'Binary'){$value=[byte[]]$value};if($kind -eq 'MultiString'){$value=[string[]]$value};$key.SetValue($saved.name,$value,$kind)}}finally{$key.Dispose()}
  if(-not $before.existed){[Microsoft.Win32.Registry]::CurrentUser.DeleteSubKey($registryPath,$false)}
  $live=Get-Content (Join-Path $out 'live-save-before.json') -Raw|ConvertFrom-Json
  $same=if($live.exists){(Get-FileHash -LiteralPath $live.path).Hash -eq $live.hash}else{-not(Test-Path -LiteralPath $live.path)}
  if(-not $same){$failure='Live save changed during verification'}else{$checks.Add('Live save hash unchanged; PlayerPrefs restored')}
 }
 @{utc=[DateTime]::UtcNow.ToString('O');executable=$exe;passed=($null -eq $failure);failure=$failure;checks=$checks.ToArray();fixture='Local UI preview only; no Google login or Firebase writes'}|ConvertTo-Json -Depth 5|Set-Content (Join-Path $out 'result.json')
 [IO.File]::WriteAllText((Join-Path $workspace 'Temp/LeaderboardWindows/latest-run.txt'),$out)
}
if($failure){throw $failure}
'PASS '+$out
