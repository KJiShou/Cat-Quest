param([Parameter(Mandatory=$true)][string]$Executable, [Parameter(Mandatory=$true)][string]$Fixture,
 [ValidateRange(640,3840)][int]$Width=1280, [ValidateRange(480,2160)][int]$Height=720)
$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$exe = [IO.Path]::GetFullPath($Executable)
$fixturePath = [IO.Path]::GetFullPath($Fixture)
if (!(Test-Path -LiteralPath $exe) -or !(Test-Path -LiteralPath $fixturePath)) { throw 'Build and explicit isolated fixture are required.' }
if (Get-Process CatQuest -ErrorAction SilentlyContinue) { throw 'Close the existing CatQuest player before isolated native verification.' }
$folder = Join-Path $workspace ('Library/SavePlayerVerification/native-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
[IO.Directory]::CreateDirectory($folder) | Out-Null
$utf8 = [Text.UTF8Encoding]::new($false)
$registryPath = 'Software\DefaultCompany\Cat Quest'
$backup = @(); $existed = $false; $player = $null; $failure = $null
Add-Type @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class SaveNative {
 public delegate bool Callback(IntPtr h,IntPtr p);
 [DllImport("user32.dll")] static extern bool EnumWindows(Callback c,IntPtr p);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h,out uint id);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h,StringBuilder value,int count);
 [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h,int mode);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 public static IntPtr Window(int id) { IntPtr result=IntPtr.Zero; EnumWindows((h,p)=>{uint pid;GetWindowThreadProcessId(h,out pid);var name=new StringBuilder(64);GetClassName(h,name,64);if(pid==id&&name.ToString()=="UnityWndClass")result=h;return true;},IntPtr.Zero);return result; }
}
'@
$foreground = [SaveNative]::GetForegroundWindow()
function ReadReport {
 try { return [IO.File]::ReadAllText((Join-Path $folder 'save-player-result.json')) | ConvertFrom-Json } catch { return $null }
}
try {
 $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($registryPath)
 if ($key) { $existed=$true; try { foreach($name in $key.GetValueNames()) { $backup+=@{name=$name;kind=$key.GetValueKind($name);value=$key.GetValue($name)} } } finally { $key.Dispose() } }
 @{existed=$existed;values=$backup} | Export-Clixml (Join-Path $folder 'playerprefs-before.clixml')
 Copy-Item -LiteralPath $fixturePath -Destination (Join-Path $folder 'test-save.json')
 [IO.File]::WriteAllText((Join-Path $folder 'save-probe-authorized.txt'),'Explicit isolated native save verification',$utf8)
 foreach($phase in @('measure','recovery')) {
  if($phase -eq 'recovery') { Move-Item -LiteralPath (Join-Path $folder 'save-player-result.json') -Destination (Join-Path $folder 'measurement.json') }
  $argsList = @('--focus-smoke','--save-smoke','--smoke-dir',('"'+$folder+'"'),'-screen-fullscreen','0','-screen-width',"$Width",'-screen-height',"$Height",'-logFile',('"'+(Join-Path $folder ($phase+'-player.log'))+'"'))
  if($phase -eq 'recovery') { $argsList += '--save-recovery' }
  # This is the visible interactive Player under test. Hidden startup followed by
  # ShowWindow triggers Unity 6000.3.10f1 D3D11 presentation errors on this machine.
  $player = Start-Process -FilePath $exe -ArgumentList $argsList -WorkingDirectory ([IO.Path]::GetDirectoryName($exe)) -WindowStyle Normal -PassThru
  $deadline = [DateTime]::UtcNow.AddSeconds(210); $started=$false
  while(!$player.HasExited -and [DateTime]::UtcNow -lt $deadline) {
   $window = [SaveNative]::Window($player.Id)
   if($window -ne [IntPtr]::Zero -and !$started) {
    [SaveNative]::SetForegroundWindow($window) | Out-Null
    $shell = New-Object -ComObject WScript.Shell; $shell.AppActivate($player.Id) | Out-Null
   }
   $state = ReadReport
   if($phase -eq 'measure' -and $state.phase -eq 'READY' -and !$started) {
    Start-Sleep -Milliseconds 500
    [IO.File]::WriteAllText((Join-Path $folder 'go.request'),'go',$utf8); $started=$true
   }
   Start-Sleep -Milliseconds 150; $player.Refresh()
  }
  if(!$player.HasExited) { throw "Native $phase timed out." }
  $state = ReadReport
  $expected = if($phase -eq 'measure'){'RESTART-REQUIRED'}else{'RECOVERY-PASS'}
  if($player.ExitCode -ne 0 -or !$state.passed -or $state.phase -ne $expected) { throw "Native $phase failed: exit=$($player.ExitCode), $($state.failure)" }
  $player.Dispose(); $player=$null
 }
} catch { $failure=$_.ToString(); Write-Warning $failure }
finally {
 if($player) { if(!$player.HasExited) { $player.Kill(); $player.WaitForExit(10000) | Out-Null }; $player.Dispose() }
 $key=[Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($registryPath)
 try { foreach($name in $key.GetValueNames()) { if($name -notin $backup.name){$key.DeleteValue($name,$false)} }; foreach($item in $backup){$key.SetValue($item.name,$item.value,$item.kind)} } finally { $key.Dispose() }
 if(!$existed){[Microsoft.Win32.Registry]::CurrentUser.DeleteSubKey($registryPath,$false)}
 [SaveNative]::SetForegroundWindow($foreground) | Out-Null
 @{utc=[DateTime]::UtcNow.ToString('O');passed=($null -eq $failure);failure=$failure;restoredPlayerPrefs=$true;executable=$exe;fixture=$fixturePath} | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $folder 'driver-result.json') -Encoding UTF8
 Write-Output "Evidence: $folder"
}
if($failure){exit 1}
