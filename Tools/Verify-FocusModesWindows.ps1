param([string]$Executable = 'Builds/FocusModes/CatQuest.exe')
$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$exe = [IO.Path]::GetFullPath((Join-Path $workspace $Executable))
if (!(Test-Path -LiteralPath $exe)) { throw 'Build the Windows development player first.' }
if (Get-Process CatQuest -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe }) { throw 'This verification player is already running.' }
$folder = Join-Path $workspace ('Library/FocusModesVerification/native-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
[IO.Directory]::CreateDirectory($folder) | Out-Null
$utf8 = [Text.UTF8Encoding]::new($false)
$checks = [Collections.Generic.List[string]]::new()
$notVerified = [Collections.Generic.List[string]]::new()
$watch = [Diagnostics.Stopwatch]::StartNew()
$main = $null; $child = $null; $saveLock = $null; $suspended = $false; $failure = $null
$registryPath = 'Software\DefaultCompany\Cat Quest'
$backup = @(); $existed = $false
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class FocusNative {
 [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left,Top,Right,Bottom; }
 public delegate bool Callback(IntPtr h,IntPtr p);
 [DllImport("user32.dll")] static extern bool EnumWindows(Callback c,IntPtr p);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h,out uint id);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h,out Rect rect);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int w,int height,uint flags);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] public static extern void mouse_event(uint flags,uint x,uint y,uint data,UIntPtr extra);
 [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h,int mode);
 [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
 [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
 [DllImport("ntdll.dll")] public static extern int NtSuspendProcess(IntPtr h);
 [DllImport("ntdll.dll")] public static extern int NtResumeProcess(IntPtr h);
 public static IntPtr Window(int id) { IntPtr result=IntPtr.Zero; long area=0; EnumWindows((h,p)=> { uint pid; GetWindowThreadProcessId(h,out pid); Rect r; GetWindowRect(h,out r); long a=(long)(r.Right-r.Left)*(r.Bottom-r.Top); if(pid==id && IsWindowVisible(h) && a>area){area=a;result=h;} return true; },IntPtr.Zero);return result; }
}
'@
[FocusNative]::SetProcessDPIAware() | Out-Null
$cursor = [Windows.Forms.Cursor]::Position
$canMovePointer = [FocusNative]::SetCursorPos($cursor.X,$cursor.Y)
if(!$canMovePointer){$notVerified.Add('Windows rejected SetCursorPos in this execution desktop: actual hover, pin/unpin and pointer clicks are not verified.')}
function WriteJson($path,$value) { [IO.File]::WriteAllText($path,($value | ConvertTo-Json -Depth 20),$utf8) }
function ReadJson($path) {
 try {
  $stream=[IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::Read,([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
  $reader=[IO.StreamReader]::new($stream)
  try { return $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
 } catch {} return $null
}
function Check($value,$name) { if(!$value){throw $name}; $checks.Add($name); Write-Output "PASS: $name" }
function WaitFor($file,[scriptblock]$predicate,$name,[int]$seconds=15) {
 $until=[DateTime]::UtcNow.AddSeconds($seconds)
 do {
  if($watch.Elapsed.TotalSeconds -gt 240){throw 'Native smoke deadline exceeded'}
  if($main -and $main.HasExited){throw "Main player exited ($($main.ExitCode))"}
  if(Test-Path (Join-Path $folder 'smoke-error.txt')){throw [IO.File]::ReadAllText((Join-Path $folder 'smoke-error.txt'))}
  $v=ReadJson $file; if($v -and (& $predicate $v)){return $v}; Start-Sleep -Milliseconds 150
 } while([DateTime]::UtcNow -lt $until)
 throw "Timed out: $name"
}
function Command($value) {
 $path=Join-Path $folder 'smoke-command.json';$tmp=$path+'.tmp'
 if(Test-Path $path){throw 'Previous command pending'}
 WriteJson $tmp $value;[IO.File]::Move($tmp,$path)
 $until=[DateTime]::UtcNow.AddSeconds(10)
 while(Test-Path $path){if([DateTime]::UtcNow -gt $until){throw 'Command not consumed'};Start-Sleep -Milliseconds 100}
 Start-Sleep -Milliseconds 300
 return WaitFor (Join-Path $folder 'smoke-result.json') {param($v)$true} 'command result'
}
function HostCommand($state,$action,$owner=$state.ownerId,$session=$state.sessionId,$request=[Guid]::NewGuid().ToString('N')) {
 $path=Join-Path $state.companionDirectory ('command-'+[Guid]::NewGuid().ToString('N')+'.json')
 WriteJson ($path+'.tmp') @{ownerId=$owner;sessionId=$session;requestId=$request;action=$action};[IO.File]::Move($path+'.tmp',$path)
 Start-Sleep -Milliseconds 500
}
function ClickAt($window,[int]$x,[int]$y) {
 $rect=[FocusNative+Rect]::new();[FocusNative]::GetWindowRect($window,[ref]$rect)|Out-Null
 [FocusNative]::SetForegroundWindow($window)|Out-Null;[FocusNative]::SetCursorPos($rect.Left+$x,$rect.Top+$y)|Out-Null
 Start-Sleep -Milliseconds 350
 [FocusNative]::mouse_event(2,0,0,0,[UIntPtr]::Zero);Start-Sleep -Milliseconds 80;[FocusNative]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
 Start-Sleep -Milliseconds 500
}
function Capture($window,$name) {
 $r=[FocusNative+Rect]::new();[FocusNative]::GetWindowRect($window,[ref]$r)|Out-Null
 $bitmap=[Drawing.Bitmap]::new($r.Right-$r.Left,$r.Bottom-$r.Top);$graphics=[Drawing.Graphics]::FromImage($bitmap)
 try {$graphics.CopyFromScreen($r.Left,$r.Top,0,0,$bitmap.Size);$bitmap.Save((Join-Path $folder ($name+'.png')))}finally{$graphics.Dispose();$bitmap.Dispose()}
}
function ResizeWindow($window,[int]$width,[int]$height) {
 for($attempt=0;$attempt -lt 5;$attempt++) {
  [FocusNative]::SetWindowPos($window,[IntPtr]::Zero,100,100,$width,$height,0x14)|Out-Null
  Start-Sleep -Milliseconds 700
  $r=[FocusNative+Rect]::new();[FocusNative]::GetWindowRect($window,[ref]$r)|Out-Null
  if(($r.Right-$r.Left) -eq $width -and ($r.Bottom-$r.Top) -eq $height){return}
 }
 throw "Native resize did not settle at ${width}x${height}; actual $($r.Right-$r.Left)x$($r.Bottom-$r.Top)"
}
try {
 $key=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($registryPath)
 if($key){$existed=$true;try{foreach($name in $key.GetValueNames()){$backup+=@{name=$name;kind=$key.GetValueKind($name);value=$key.GetValue($name)}}}finally{$key.Dispose()}}
 WriteJson (Join-Path $folder 'context.json') @{utc=[DateTime]::UtcNow.ToString('O');executable=$exe;sha256=(Get-FileHash $exe).Hash;isolated=$true}
 WriteJson (Join-Path $folder 'test-save.json') @{version=5;username='Focus Test';settings=@{notificationsEnabled=$false;soundVolume=0;musicVolume=0}}
 $arguments='--focus-smoke --smoke-dir "'+$folder+'" -force-d3d11 -logFile "'+$folder+'/Player.log" -screen-fullscreen 0 -screen-width 1280 -screen-height 720'
 $main=Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory ([IO.Path]::GetDirectoryName($exe)) -WindowStyle Hidden -PassThru
 $resultPath=Join-Path $folder 'smoke-result.json'
 $r=WaitFor $resultPath {param($v)$v.controlsBound -and $v.phase -eq 'Ready'} 'startup' 45
 $r=Command @{action='configure';mode=1};$r=Command @{action='start'};$r=Command @{action='widget'}
 $viewPath=Join-Path $r.companionDirectory 'companion-window.json'
 $view=WaitFor $viewPath {param($v)$v.connected -and $v.noPlayerStore} 'companion connected' 30
 $children=@(Get-CimInstance Win32_Process | Where-Object {$_.ParentProcessId -eq $main.Id -and $_.ExecutablePath -eq $exe})
 $child=Get-Process -Id $children[0].ProcessId
 $window=[FocusNative]::Window($child.Id)
 Check ($window -ne [IntPtr]::Zero) 'actual companion window'
 $r=Command @{action='advance';seconds=14400}
 $view=WaitFor $viewPath {param($v)$v.timer -like '4:00:*'} 'four-hour count-up'
 Check ($r.phase -eq 'Focusing' -and $r.exp -eq 0 -and $r.fish -eq 72) 'four-hour stopwatch remains active with capped preview'
 ResizeWindow $window 420 300
 $view=WaitFor $viewPath {param($v)$v.width -eq 420 -and $v.height -eq 300} 'normal size'
 if($canMovePointer){[FocusNative]::SetCursorPos(250,250)|Out-Null};Start-Sleep -Milliseconds 700;Capture $window 'companion-normal'
 if($canMovePointer){
  [FocusNative]::SetCursorPos(20,20)|Out-Null;Start-Sleep -Milliseconds 700;Capture $window 'companion-normal-idle'
  ClickAt $window 327 20
  $view=WaitFor $viewPath {param($v)!$v.pinned} 'unpin'
 }
 ResizeWindow $window 240 180
 $view=WaitFor $viewPath {param($v)$v.width -eq 240 -and $v.height -eq 180} 'minimum size'
 Check ($view.pinned -eq !$canMovePointer) 'native resize preserves pin state'
 if($canMovePointer){[FocusNative]::SetCursorPos(220,200)|Out-Null};Start-Sleep -Milliseconds 700;Capture $window 'companion-minimum'
 if($canMovePointer){ClickAt $window 181 20}else{[FocusNative]::ShowWindow($window,2)|Out-Null;Start-Sleep -Milliseconds 500}
 Check ([FocusNative]::IsIconic($window)) 'native minimize'
 Start-Sleep -Milliseconds 500;[FocusNative]::ShowWindow($window,9)|Out-Null
 $r=ReadJson $resultPath;HostCommand $r end 'wrong-owner';HostCommand $r end $r.ownerId 'stale-session'
 Check ((ReadJson $resultPath).phase -eq 'Focusing') 'wrong-owner and stale session rejected'
 $saveLock=[IO.File]::Open((Join-Path $folder 'test-save.json.tmp'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
 HostCommand $r end
 $r=WaitFor $resultPath {param($v)$v.phase -eq 'Saving'} 'failed save'
 Check ($r.exp -eq 0 -and $r.coins -eq 0) 'save failure grants nothing'
 Command @{action='screenshot';screenshot='main-save-failure.png'}|Out-Null
 $saveLock.Dispose();$saveLock=$null
 $request=[Guid]::NewGuid().ToString('N');HostCommand $r retry $r.ownerId $r.sessionId $request
 $r=WaitFor $resultPath {param($v)$v.phase -eq 'Finished'} 'retry finish'
 Check ($r.exp -eq 180 -and $r.coins -eq 72 -and $r.tickets -eq 4) 'stopwatch retry pays exactly three hours without Rest'
 HostCommand $r retry $r.ownerId $r.sessionId $request;HostCommand $r end
 Check ((ReadJson $resultPath).exp -eq 180) 'duplicate retry/end cannot pay twice'
 $r=Command @{action='configure';mode=2;focusMinutes=180};$r=Command @{action='start'};$r=Command @{action='advance';seconds=10800}
 Check ($r.phase -eq 'Finished' -and $r.exp -eq 360 -and $r.coins -eq 144 -and $r.tickets -eq 4) 'countdown shares six-hour daily cap and skips Rest'
 $r=Command @{action='configure';mode=1};$r=Command @{action='start'};$r=Command @{action='advance';seconds=60}
 $request=[Guid]::NewGuid().ToString('N');HostCommand $r start $r.ownerId $r.sessionId $request;HostCommand $r start $r.ownerId $r.sessionId $request
 Check ((ReadJson $resultPath).sessionId -eq $r.sessionId) 'duplicate start cannot replace active session'
 [FocusNative]::NtSuspendProcess($main.Handle)|Out-Null;$suspended=$true
 Start-Sleep -Milliseconds 3700
 $view=WaitFor $viewPath {param($v)!$v.connected} 'heartbeat loss';$timer=$view.timer
 Start-Sleep -Milliseconds 500;$view=ReadJson $viewPath
 Check ($view.timer -eq $timer -and !$view.training) 'heartbeat loss freezes timer and training'
 Capture $window 'companion-disconnected'
 [FocusNative]::NtResumeProcess($main.Handle)|Out-Null;$suspended=$false
 $view=WaitFor $viewPath {param($v)$v.connected} 'heartbeat reconnect'
 if($canMovePointer){ClickAt $window 215 18}else{[IO.File]::WriteAllText((Join-Path $r.companionDirectory 'close.request'),$r.ownerId)}
 Check ($child.WaitForExit(6000)) 'native close exits companion'
 $r=ReadJson $resultPath;Check ($r.phase -eq 'Focusing') 'closing companion preserves main Focus'
 $r=Command @{action='end'}
 Check ($r.phase -eq 'Finished' -and $r.exp -eq 360 -and $r.coins -eq 144) 'third session remains usable without extra rewards'
 WriteJson (Join-Path $folder 'final-state.json') $r
} catch { $failure=$_.ToString(); Write-Output "FAIL: $failure" }
finally {
 if($saveLock){$saveLock.Dispose()}
 if($suspended -and $main){[FocusNative]::NtResumeProcess($main.Handle)|Out-Null}
 if($main -and !$main.HasExited){try{Command @{action='quit'}|Out-Null}catch{};if(!$main.WaitForExit(8000)){$main.Kill();$main.WaitForExit(3000)|Out-Null}}
 if($child -and !$child.HasExited){if(!$child.WaitForExit(5000)){$child.Kill();$child.WaitForExit(3000)|Out-Null}}
 $key=[Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($registryPath)
 try{foreach($name in $key.GetValueNames()){if($name -notin $backup.name){$key.DeleteValue($name,$false)}};foreach($v in $backup){$key.SetValue($v.name,$v.value,$v.kind)}}finally{$key.Dispose()}
 if(!$existed){[Microsoft.Win32.Registry]::CurrentUser.DeleteSubKey($registryPath,$false)}
 [FocusNative]::SetCursorPos($cursor.X,$cursor.Y)|Out-Null
 WriteJson (Join-Path $folder 'result.json') @{utc=[DateTime]::UtcNow.ToString('O');passed=($null -eq $failure);checks=$checks;notVerified=$notVerified;failure=$failure;restoredPlayerPrefs=$true}
 Write-Output "Evidence: $folder"
}
if($failure){exit 1}
