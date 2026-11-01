# Focused native-input regression; uses an isolated unlocked save and only owned processes.
param([string]$Executable = 'Builds/Windows/CatQuest.exe')
$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$exe = [IO.Path]::GetFullPath((Join-Path $workspace $Executable))
if (!(Test-Path -LiteralPath $exe)) { throw 'Build the Windows development player first.' }
if (Get-Process CatQuest -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe }) { throw 'This verification player is already running.' }
$folder = Join-Path $workspace ('Library/CompanionInputVerification/native-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
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
 [StructLayout(LayoutKind.Sequential)] public struct GUIINFO {public int size;public uint flags;public IntPtr active,focus,capture,menu,movesize,caret;public Rect rect;}
 [DllImport("user32.dll")] public static extern bool GetGUIThreadInfo(uint id,ref GUIINFO info);
 public static GUIINFO Gui(IntPtr h){uint pid;uint tid=GetWindowThreadProcessId(h,out pid);var g=new GUIINFO{size=Marshal.SizeOf<GUIINFO>()};GetGUIThreadInfo(tid,ref g);return g;}

 [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public UNION data; }
 [StructLayout(LayoutKind.Explicit,Size=32)] struct UNION { [FieldOffset(0)] public KEYBOARD keyboard; }
 [StructLayout(LayoutKind.Sequential)] struct KEYBOARD { public ushort vk,scan;public uint flags,time;public UIntPtr extra; }
 [DllImport("user32.dll",SetLastError=true)] static extern uint SendInput(uint count,INPUT[] inputs,int size);
 public static void Key(byte key,bool up){var input=new INPUT{type=1,data=new UNION{keyboard=new KEYBOARD{scan=(ushort)MapVirtualKey(key,0),flags=(uint)(8|(up?2:0)|((key>=33&&key<=46)?1:0))}}};if(SendInput(1,new[]{input},Marshal.SizeOf<INPUT>())!=1)throw new Exception("SendInput rejected");}

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
 [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h,int index);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] public static extern uint MapVirtualKey(uint key,uint kind);
 [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
 [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
 [DllImport("ntdll.dll")] public static extern int NtSuspendProcess(IntPtr h);
 [DllImport("ntdll.dll")] public static extern int NtResumeProcess(IntPtr h);
 public static IntPtr Window(int id) { IntPtr result=IntPtr.Zero; long area=0; EnumWindows((h,p)=> { uint pid; GetWindowThreadProcessId(h,out pid); Rect r; GetWindowRect(h,out r); long a=(long)(r.Right-r.Left)*(r.Bottom-r.Top); if(pid==id && IsWindowVisible(h) && a>area){area=a;result=h;} return true; },IntPtr.Zero);return result; }
}
'@
[FocusNative]::SetProcessDPIAware() | Out-Null
$previousForeground=[FocusNative]::GetForegroundWindow()
$cursor = [Windows.Forms.Cursor]::Position
$canMovePointer = [FocusNative]::SetCursorPos($cursor.X,$cursor.Y)
if(!$canMovePointer){throw 'Native input checks require an available interactive Windows desktop.'}
function WriteJson($path,$value) { [IO.File]::WriteAllText($path,($value | ConvertTo-Json -Depth 20),$utf8) }
function ReadJson($path) {
 for($readAttempt=0;$readAttempt -lt 6;$readAttempt++){
 try {
  $stream=[IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::Read,([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
  $reader=[IO.StreamReader]::new($stream)
  try { return $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
 } catch {Start-Sleep -Milliseconds 50}
 } return $null
}
function Check($value,$name) { if(!$value){throw $name}; $checks.Add($name); Write-Output "PASS: $name" }
function WaitFor($file,[scriptblock]$predicate,$name,[int]$seconds=15) {
 $until=[DateTime]::UtcNow.AddSeconds($seconds)
 do {
  if($watch.Elapsed.TotalSeconds -gt 360){throw 'Native smoke deadline exceeded'}
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
function CheckTaskbar($state) {
 $style=[FocusNative]::GetWindowLong($window,-20)
 Check (($style -band 0x40000) -ne 0 -and ($style -band 0x80) -eq 0) ("APPWINDOW without TOOLWINDOW: "+$state)
}

function Tap([byte]$code,[int]$delay=350) {
 [FocusNative]::Key($code,$false);Start-Sleep -Milliseconds 60;[FocusNative]::Key($code,$true);Start-Sleep -Milliseconds $delay
}
function SelectControl($name) {
 Tap 27
 for($i=0;$i -lt 8;$i++){Tap 9;$v=ReadJson $viewPath;if($v.selected -eq $name){return}}
 throw "Cannot keyboard-select $name"
}
function ResumeMain { if($suspended){[FocusNative]::NtResumeProcess($main.Handle)|Out-Null;$script:suspended=$false} }
function BurstSubmit($name) {
 SelectControl $name
 $before=@(Get-ChildItem $r.companionDirectory -Filter 'command-*.json').Count
 [FocusNative]::NtSuspendProcess($main.Handle)|Out-Null;$script:suspended=$true
 for($i=0;$i -lt 5;$i++){Tap 13 60}
 $after=@(Get-ChildItem $r.companionDirectory -Filter 'command-*.json').Count
 Check (($after-$before) -eq 1) ("Five Enter presses queue one "+$name+" request")
 ResumeMain
}
function VerifyReset($label) {
 Tap 27;[FocusNative]::SetCursorPos(20,20)|Out-Null;Start-Sleep -Milliseconds 700
 $v=ReadJson $viewPath
 foreach($f in $v.feedback){Check ([Math]::Abs($f.scaleX-1) -lt .01 -and [Math]::Abs($f.scaleY-1) -lt .01 -and [Math]::Abs($f.offset) -lt .05) ($label+" resets "+$f.name)}
 WriteJson (Join-Path $folder ($label+'-feedback.json')) $v
}
function RestoreFromTaskbar {
 Add-Type -AssemblyName UIAutomationClient;Add-Type -AssemblyName UIAutomationTypes
 $taskbar=[System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ClassNameProperty,'Shell_TrayWnd'))
 $elements=@($taskbar.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition))
 $dump=@($elements|ForEach-Object{@{name=$_.Current.Name;id=$_.Current.AutomationId;class=$_.Current.ClassName;bounds=$_.Current.BoundingRectangle.ToString()}})
 WriteJson (Join-Path $folder 'taskbar-elements.json') $dump
 $candidates=@($elements|Where-Object {$_.Current.ClassName -eq 'Taskbar.TaskListButtonAutomationPeer' -and ($_.Current.AutomationId -match 'CatQuest|Cat.Quest')})
 Check ($candidates.Count -eq 1) 'unique player taskbar group'
 $rect=$candidates[0].Current.BoundingRectangle
 [FocusNative]::SetCursorPos([int]($rect.X+$rect.Width/2),[int]($rect.Y+$rect.Height/2))|Out-Null
 Start-Sleep -Milliseconds 200;[FocusNative]::mouse_event(2,0,0,0,[UIntPtr]::Zero);Start-Sleep -Milliseconds 80;[FocusNative]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
 Start-Sleep -Milliseconds 1000
 $root=[System.Windows.Automation.AutomationElement]::RootElement
 $popups=@($root.FindAll([System.Windows.Automation.TreeScope]::Children,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ClassNameProperty,'TaskListThumbnailWnd')))
 $dump=@($popups|ForEach-Object {$_.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition)}|ForEach-Object{@{name=$_.Current.Name;id=$_.Current.AutomationId;class=$_.Current.ClassName;bounds=$_.Current.BoundingRectangle.ToString()}})
 WriteJson (Join-Path $folder 'taskbar-thumbnails.json') $dump
 Check (![FocusNative]::IsIconic($window)) 'physical taskbar click restores companion'
}
try {
 $key=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($registryPath)
 if($key){$existed=$true;try{foreach($name in $key.GetValueNames()){$backup+=@{name=$name;kind=$key.GetValueKind($name);value=$key.GetValue($name)}}}finally{$key.Dispose()}}
 WriteJson (Join-Path $folder 'context.json') @{utc=[DateTime]::UtcNow.ToString('O');executable=$exe;sha256=(Get-FileHash $exe).Hash;isolated=$true}
 WriteJson (Join-Path $folder 'test-save.json') @{version=5;username='Focus Test';experience=10000;tutorial=@{version=1;dismissed=$true;taskStep=4};settings=@{notificationsEnabled=$false;soundVolume=0;musicVolume=0}}
 $arguments='--focus-smoke --smoke-dir "'+$folder+'" -force-d3d11 -logFile "'+$folder+'/Player.log" -screen-fullscreen 0 -screen-width 1280 -screen-height 720'
 $main=Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory ([IO.Path]::GetDirectoryName($exe)) -WindowStyle Hidden -PassThru
 $resultPath=Join-Path $folder 'smoke-result.json'
 $r=WaitFor $resultPath {param($v)$v.controlsBound -and $v.phase -eq 'Ready'} 'startup' 45
 $r=Command @{action='configure';mode=1};$r=Command @{action='start'};Check ($r.phase -eq 'Focusing' -and $r.sessionId) 'isolated Focus session started';$r=Command @{action='widget'}
 $viewPath=Join-Path $r.companionDirectory 'companion-window.json'
 $view=WaitFor $viewPath {param($v)$v.connected -and $v.noPlayerStore} 'companion connected' 30
 $children=@(Get-CimInstance Win32_Process | Where-Object {$_.ParentProcessId -eq $main.Id -and $_.ExecutablePath -eq $exe})
 $child=Get-Process -Id $children[0].ProcessId
 $window=[FocusNative]::Window($child.Id)
 Check ($window -ne [IntPtr]::Zero) 'actual companion window'
 Start-Sleep -Milliseconds 1000
 CheckTaskbar 'opened'
 ResizeWindow $window 420 300
 $view=WaitFor $viewPath {param($v)$v.width -eq 420 -and $v.height -eq 300} 'normal client size'
 ClickAt $window 200 170;Check ([FocusNative]::GetForegroundWindow() -eq $window) 'native keyboard focus'
 [FocusNative]::SetCursorPos(20,20)|Out-Null
 foreach($size in @(@(420,300),@(240,180))) {
  ResizeWindow $window $size[0] $size[1]
  $view=WaitFor $viewPath {param($v)$v.width -eq $size[0] -and $v.height -eq $size[1]} 'client resize'
  Check ([FocusNative]::Gui($window).focus -eq $window) 'real Win32 keyboard focus'
  SelectControl 'PinWindow';$before=ReadJson $viewPath
  Tap 13;$after=ReadJson $viewPath
  Check ($after.pinned -ne $before.pinned) 'keyboard Pin toggles'

  Check ($after.width -eq $size[0] -and $after.height -eq $size[1]) 'keyboard submit preserves window size'
  Check ($after.keyboard -and $after.controls) 'keyboard reveals action outside pointer'
  Tap 39;$null=WaitFor $viewPath {param($v)$v.selected -eq 'MinimizeWindow'} 'Right arrow focus' 3;Check $true 'Right arrow advances focus'
  Tap 37;$null=WaitFor $viewPath {param($v)$v.selected -eq 'PinWindow'} 'Left arrow focus' 3;Check $true 'Left arrow returns focus'
  Tap 9;Check ((ReadJson $viewPath).selected -eq 'MinimizeWindow') 'Tab advances focus'
  [FocusNative]::Key(16,$false);Tap 9;[FocusNative]::Key(16,$true)
  Check ((ReadJson $viewPath).selected -eq 'PinWindow') 'Shift Tab reverses focus'
  $baseline=ReadJson $viewPath
  $pinX=$size[0]-93
  $rect=[FocusNative+Rect]::new();[FocusNative]::GetWindowRect($window,[ref]$rect)|Out-Null
  [FocusNative]::SetCursorPos($rect.Left+$pinX,$rect.Top+20)|Out-Null;Start-Sleep -Milliseconds 250
  for($i=0;$i -lt 5;$i++){
   [FocusNative]::mouse_event(2,0,0,0,[UIntPtr]::Zero);Start-Sleep -Milliseconds 50
   [FocusNative]::mouse_event(4,0,0,0,[UIntPtr]::Zero);Start-Sleep -Milliseconds 60
  }
  Start-Sleep -Milliseconds 450
  $after=ReadJson $viewPath
  Check ($after.pinned -ne $baseline.pinned) 'five pointer clicks toggle Pin five times'
  for($i=0;$i -lt $after.feedback.Count;$i++){
   Check ($after.feedback[$i].x -eq $baseline.feedback[$i].x -and $after.feedback[$i].y -eq $baseline.feedback[$i].y -and $after.feedback[$i].width -eq $baseline.feedback[$i].width) ('stable hit area '+$after.feedback[$i].name)
  }
  SelectControl 'End';Capture $window ('keyboard-'+$size[0])
  VerifyReset ('reset-'+$size[0])
  SelectControl 'MinimizeWindow';Tap 13
  Check ([FocusNative]::IsIconic($window)) 'keyboard Minimize'
  Start-Sleep -Milliseconds 400
  $lost=ReadJson $viewPath
  Check (!$lost.keyboard -and !$lost.selected) 'minimize clears keyboard focus'
  foreach($f in $lost.feedback){Check ([Math]::Abs($f.scaleY-1) -lt .01) ('focus loss resets '+$f.name)}
  RestoreFromTaskbar
  $view=WaitFor $viewPath {param($v)$v.width -eq $size[0] -and $v.height -eq $size[1]} 'restored size'
  CheckTaskbar 'restored'
 }
 # Failed persistence exposes Retry without granting or duplicating rewards.
 $r=Command @{action='advance';seconds=65}
 $saveLock=[IO.File]::Open((Join-Path $folder 'test-save.json.tmp'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
 BurstSubmit 'End'
 $r=WaitFor $resultPath {param($v)$v.phase -eq 'Saving'} 'failed save phase'
 Check ($r.exp -eq 10000 -and $r.coins -eq 0) 'failed save grants no rewards'
 $view=WaitFor $viewPath {param($v)$v.phase -eq 'Saving'} 'Retry visible'
 Start-Sleep -Milliseconds 3200
 BurstSubmit 'Retry'
 Start-Sleep -Milliseconds 500
 Check ((ReadJson $resultPath).phase -eq 'Saving') 'failed retry remains retryable'
 $saveLock.Dispose();$saveLock=$null
 Start-Sleep -Milliseconds 3200
 BurstSubmit 'Retry'
 $r=WaitFor $resultPath {param($v)$v.phase -eq 'Finished'} 'retry settled once'
 $saved=ReadJson (Join-Path $folder 'test-save.json')
 Check (@($saved.focusSessions).Count -eq 1) 'retry keeps one session receipt'
 $paidExp=$r.exp;$paidCoins=$r.coins;Start-Sleep -Milliseconds 400
 $stable=ReadJson $resultPath;Check ($stable.exp -eq $paidExp -and $stable.coins -eq $paidCoins) 'settlement remains stable after rapid retry'
 VerifyReset 'after-retry'
 # A short Pomodoro exercises Start, Finish Rest and Cancel confirmation.
 $r=Command @{action='configure';mode=0;focusMinutes=1;restMinutes=1}
 $view=WaitFor $viewPath {param($v)$v.phase -eq 'Ready'} 'ready'
 BurstSubmit 'Start'
 $r=WaitFor $resultPath {param($v)$v.phase -eq 'Focusing'} 'start'
 $r=Command @{action='advance';seconds=60}
 $r=WaitFor $resultPath {param($v)$v.phase -eq 'Resting'} 'rest'
 $view=WaitFor $viewPath {param($v)$v.phase -eq 'Resting'} 'Finish Rest visible'
 BurstSubmit 'Rest'
 $r=WaitFor $resultPath {param($v)$v.phase -eq 'Finished'} 'finish rest'
 $r=Command @{action='configure';mode=0;focusMinutes=1;restMinutes=1}
 $view=WaitFor $viewPath {param($v)$v.phase -eq 'Ready'} 'ready again'
 BurstSubmit 'Start';$r=WaitFor $resultPath {param($v)$v.phase -eq 'Focusing'} 'second start'
 SelectControl 'End';Tap 13
 $view=WaitFor $viewPath {param($v)$v.cancelPrompt} 'cancel prompt'
 Tap 9;Check ((ReadJson $viewPath).cancelPrompt) 'Tab stays in confirmation'
 Tap 27;$view=WaitFor $viewPath {param($v)!$v.cancelPrompt} 'Escape dismisses prompt'
 Check ((ReadJson $resultPath).phase -eq 'Focusing') 'Escape keeps Focus running'
 VerifyReset 'after-prompt'
 SelectControl 'CloseWidget';Tap 13
 Check ($child.WaitForExit(6000)) 'keyboard Close exits companion'
 Check ((ReadJson $resultPath).phase -eq 'Focusing') 'keyboard Close preserves main Focus'
 WriteJson (Join-Path $folder 'final-state.json') $r
} catch { $failure=$_.ToString()+"
"+$_.ScriptStackTrace; Write-Output "FAIL: $failure" }
finally {
 if($saveLock){$saveLock.Dispose()}
 if($suspended -and $main){[FocusNative]::NtResumeProcess($main.Handle)|Out-Null}
 if($main -and !$main.HasExited){try{Command @{action='quit'}|Out-Null}catch{};if(!$main.WaitForExit(8000)){$main.Kill();$main.WaitForExit(3000)|Out-Null}}
 if($child -and !$child.HasExited){if(!$child.WaitForExit(5000)){$child.Kill();$child.WaitForExit(3000)|Out-Null}}
 $key=[Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($registryPath)
 try{foreach($name in $key.GetValueNames()){if($name -notin $backup.name){$key.DeleteValue($name,$false)}};foreach($v in $backup){$key.SetValue($v.name,$v.value,$v.kind)}}finally{$key.Dispose()}
 if(!$existed){[Microsoft.Win32.Registry]::CurrentUser.DeleteSubKey($registryPath,$false)}
 [FocusNative]::SetCursorPos($cursor.X,$cursor.Y)|Out-Null
 if($previousForeground -ne [IntPtr]::Zero){[FocusNative]::SetForegroundWindow($previousForeground)|Out-Null}
 WriteJson (Join-Path $folder 'result.json') @{utc=[DateTime]::UtcNow.ToString('O');passed=($null -eq $failure);checks=$checks;notVerified=$notVerified;failure=$failure;restoredPlayerPrefs=$true}
 Write-Output "Evidence: $folder"
}
if($failure){exit 1}
