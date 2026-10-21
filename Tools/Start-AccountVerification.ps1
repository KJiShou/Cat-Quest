param([Parameter(Mandatory=$true)][string]$Executable,[Parameter(Mandatory=$true)][string]$Directory,[ValidateRange(640,3840)][int]$Width=1280,[ValidateRange(480,2160)][int]$Height=720)
$ErrorActionPreference='Stop'
# Interactive player smoke test: launch visibly from the first frame. Hidden startup
# followed by ShowWindow produced PresentFrame timing errors on Unity 6000.3.10f1.
# Do not hide logs or disable the Development Console to conceal those errors.
$directory=[IO.Path]::GetFullPath($Directory)
if(-not (Test-Path (Join-Path $directory 'account-test.marker'))){throw 'Isolated test marker missing'}
$registryPath='Software\DefaultCompany\Cat Quest'
$backupPath=Join-Path $directory 'playerprefs-before.clixml'
if(-not (Test-Path $backupPath)){
 $registry=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($registryPath);$values=@()
 if($registry){try{foreach($name in $registry.GetValueNames()){$values+=@{name=$name;kind=$registry.GetValueKind($name).ToString();value=$registry.GetValue($name,$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)}}}finally{$registry.Dispose()}}
 @{existed=($null -ne $registry);values=$values}|Export-Clixml $backupPath
 $live=Join-Path $env:USERPROFILE 'AppData/LocalLow/DefaultCompany/Cat Quest/catquest-data.json'
 @{exists=(Test-Path $live);hash=$(if(Test-Path $live){(Get-FileHash -LiteralPath $live -Algorithm SHA256).Hash}else{''});path=$live}|ConvertTo-Json|Set-Content (Join-Path $directory 'live-save-before.json')
}
$arguments='--account-smoke --account-test-dir "'+$directory+'" -logFile "'+(Join-Path $directory ('Player-'+[DateTime]::UtcNow.ToString('HHmmss')+'.log'))+'" -screen-fullscreen 0 -screen-width '+$Width+' -screen-height '+$Height
$process=Start-Process -FilePath $Executable -ArgumentList $arguments -WorkingDirectory ([IO.Path]::GetDirectoryName($Executable)) -WindowStyle Normal -PassThru
$source=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Verify-SettingsNotifications.ps1'))
if($source -match "(?s)Add-Type -TypeDefinition @'\r?\n(.*?)\r?\n'@"){Add-Type -TypeDefinition $matches[1]}
$deadline=[DateTime]::UtcNow.AddSeconds(40);$window=$null
 do {
  $process.Refresh();if($process.HasExited){throw 'Test player exited during startup'}
  $window=[SettingsSmokeNative]::Windows()|Where-Object {$_.ProcessId -eq $process.Id -and $_.ClassName -eq 'UnityWndClass'}|Select-Object -First 1
  if($window){break};Start-Sleep -Milliseconds 200
 } while([DateTime]::UtcNow -lt $deadline)
if(-not $window){throw 'Unity test window not found'}

[SettingsSmokeNative]::SetForegroundWindow([IntPtr]$window.Handle)|Out-Null
@{pid=$process.Id;executable=$Executable;directory=$directory;utc=[DateTime]::UtcNow.ToString('o')}|ConvertTo-Json|Set-Content (Join-Path $directory 'process.json')
@{pid=$process.Id;directory=$directory}|ConvertTo-Json
