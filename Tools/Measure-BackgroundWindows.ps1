param(
    [Parameter(Mandatory=$true)][string]$Executable,
    [ValidateRange(5,30)][int]$SampleSeconds = 12,
    [ValidateRange(1,3)][int]$Repeats = 2,
    [ValidateRange(1,10)][int]$WarmupSeconds = 3,
    [switch]$VerifyPreviewsOnly
)
$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$exe = if ([IO.Path]::IsPathRooted($Executable)) { [IO.Path]::GetFullPath($Executable) } else { [IO.Path]::GetFullPath((Join-Path $workspace $Executable)) }
if (!(Test-Path -LiteralPath $exe)) { throw 'Build the current Windows Development Player first.' }
if (Get-Process CatQuest -ErrorAction SilentlyContinue) { throw 'Close existing CatQuest players before this isolated measurement.' }
$evidenceRoot = if ($VerifyPreviewsOnly) { 'Library/PreviewWindowsVerification/native-' } else { 'Library/BackgroundPerformance/native-' }
$folder = Join-Path $workspace ($evidenceRoot + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
[IO.Directory]::CreateDirectory($folder) | Out-Null
$utf8 = [Text.UTF8Encoding]::new($false)
$checks = [Collections.Generic.List[string]]::new()
$metrics = [Collections.Generic.List[object]]::new()
$watch = [Diagnostics.Stopwatch]::StartNew()
$main = $null; $child = $null; $control = $null; $failure = $null; $viewPath = $null
$window = [IntPtr]::Zero; $childWindow = [IntPtr]::Zero
$registryPath = 'Software\DefaultCompany\Cat Quest'
$backup = @(); $existed = $false; $prefsCaptured = $false; $prefsRestored = $false; $foregroundRestored = $false
Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class BackgroundNative {
 public delegate bool Callback(IntPtr h,IntPtr p);
 [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left,Top,Right,Bottom; }
 [StructLayout(LayoutKind.Sequential)] public struct Io { public ulong ReadOperations,WriteOperations,OtherOperations,ReadBytes,WriteBytes,OtherBytes; }
 [DllImport("user32.dll")] static extern bool EnumWindows(Callback callback,IntPtr state);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h,out uint process);
 [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h,out Rect rect);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h,System.Text.StringBuilder value,int count);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h,System.Text.StringBuilder value,int count);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
 [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h,int command);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] static extern void mouse_event(uint flags,uint x,uint y,uint data,UIntPtr extra);
 [DllImport("user32.dll")] static extern bool AttachThreadInput(uint a,uint b,bool attach);
 [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
 [DllImport("kernel32.dll")] static extern bool GetProcessIoCounters(IntPtr process,out Io counters);
 public static Io ReadIo(IntPtr process) { Io result; if(!GetProcessIoCounters(process,out result))throw new Exception("GetProcessIoCounters failed"); return result; }
 public static IntPtr Window(int id) { IntPtr result=IntPtr.Zero;long area=0;EnumWindows((h,p)=>{uint pid;GetWindowThreadProcessId(h,out pid);Rect rect;GetWindowRect(h,out rect);long candidate=(long)(rect.Right-rect.Left)*(rect.Bottom-rect.Top);if(pid==id&&candidate>area){area=candidate;result=h;}return true;},IntPtr.Zero);return result; }
 public static string Describe(int id) { var result=new System.Text.StringBuilder();EnumWindows((h,p)=>{uint pid;GetWindowThreadProcessId(h,out pid);if(pid==id||h==GetForegroundWindow()){var cls=new System.Text.StringBuilder(256);var title=new System.Text.StringBuilder(256);GetClassName(h,cls,256);GetWindowText(h,title,256);Rect r;GetWindowRect(h,out r);result.AppendFormat("hwnd={0} pid={1} visible={2} class={3} title={4} rect={5},{6},{7},{8}; ",h,pid,IsWindowVisible(h),cls,title,r.Left,r.Top,r.Right,r.Bottom);}return true;},IntPtr.Zero);return result.ToString(); }
 public static bool ClickTitle(IntPtr h) { Rect r;if(!GetWindowRect(h,out r)||!SetCursorPos(r.Left+(r.Right-r.Left)/2,r.Top+14))return false;mouse_event(2,0,0,0,UIntPtr.Zero);mouse_event(4,0,0,0,UIntPtr.Zero);return true; }
 public static bool Foreground(IntPtr target) { uint pid;uint other=GetWindowThreadProcessId(GetForegroundWindow(),out pid),own=GetCurrentThreadId();bool attached=own!=other&&AttachThreadInput(own,other,true);try{SetForegroundWindow(target);return GetForegroundWindow()==target;}finally{if(attached)AttachThreadInput(own,other,false);} }
}
'@
[BackgroundNative]::SetProcessDPIAware() | Out-Null
$previousForeground = [BackgroundNative]::GetForegroundWindow()
$previousCursor = [Windows.Forms.Cursor]::Position
function WriteJson($path,$value) { [IO.File]::WriteAllText($path,($value | ConvertTo-Json -Depth 20),$utf8) }
function ReadJson($path) {
    try {
        $stream = [IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::Read,([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
        $reader = [IO.StreamReader]::new($stream)
        try { return $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
    } catch { return $null }
}
function Check($value,$name) { if (!$value) { throw $name }; $checks.Add($name); Write-Output "PASS: $name" }
function CleanupFailure([string]$name,$errorRecord) {
    $message = "Cleanup ${name}: $errorRecord"
    $script:failure = if ($failure) { $failure + [Environment]::NewLine + $message } else { $message }
    Write-Output $message
}
function Pump([int]$milliseconds=50) { [Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds $milliseconds }
function ActivateWindow([IntPtr]$target) {
    # Unity's window activation completes asynchronously after ShowWindow.
    $deadline = [DateTime]::UtcNow.AddSeconds(3)
    do {
        [BackgroundNative]::Foreground($target) | Out-Null
        Pump 100
        if ([BackgroundNative]::GetForegroundWindow() -eq $target) { return $true }
    } while ([DateTime]::UtcNow -lt $deadline)
    # Use the same real native pointer path as the existing Windows input smoke.
    for ($activationAttempt=0;$activationAttempt -lt 3;$activationAttempt++) {
        [BackgroundNative]::ShowWindow($target,9) | Out-Null
        [BackgroundNative]::Foreground($target) | Out-Null
        if (![BackgroundNative]::ClickTitle($target)) { break }
        Pump 700
        if ([BackgroundNative]::GetForegroundWindow() -eq $target) { return $true }
    }
    if ($main) { [IO.File]::AppendAllText((Join-Path $folder 'activation-failure.txt'),('target='+$target+' '+[BackgroundNative]::Describe($main.Id)+[Environment]::NewLine),$utf8) }
    return $false
}
function WaitFor($path,[scriptblock]$predicate,$name,[int]$seconds=20) {
    $deadline = [DateTime]::UtcNow.AddSeconds($seconds)
    do {
        if ($watch.Elapsed.TotalSeconds -gt 600) { throw 'Measurement exceeded its ten-minute bound.' }
        if ($main -and $main.HasExited) { throw "Main player exited: $($main.ExitCode)" }
        $errorPath = Join-Path $folder 'smoke-error.txt'
        if (Test-Path -LiteralPath $errorPath) { throw [IO.File]::ReadAllText($errorPath) }
        $value = ReadJson $path
        if ($value -and (& $predicate $value)) { return $value }
        Pump
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Timed out: $name"
}
function Command($value) {
    $path = Join-Path $folder 'smoke-command.json'
    if (Test-Path -LiteralPath $path) { throw 'Previous smoke command is still pending.' }
    $id = [Guid]::NewGuid().ToString('N'); $value.requestId = $id
    WriteJson ($path+'.tmp') $value; [IO.File]::Move($path+'.tmp',$path)
    return WaitFor $resultPath { param($v) $v.requestId -eq $id } ('command '+$value.action) 50
}
function EnsureCompanion {
    if ($child -and !$child.HasExited) { return }
    $state = Command @{ action='widget' }
    $script:viewPath = Join-Path $state.companionDirectory 'companion-window.json'
    $null = WaitFor $viewPath { param($v) $v.connected -and $v.noPlayerStore -and $v.training -and ![string]::IsNullOrEmpty($v.ninjaSprite) -and ![string]::IsNullOrEmpty($v.acornSprite) } 'companion handshake and loaded training scene' 40
    $process = @(Get-CimInstance Win32_Process | Where-Object { $_.ParentProcessId -eq $main.Id -and $_.ExecutablePath -eq $exe })
    if ($process.Count -ne 1) { throw 'Expected exactly one owned companion process.' }
    $script:child = Get-Process -Id $process[0].ProcessId
    $script:childWindow = [BackgroundNative]::Window($child.Id)
    if ($childWindow -eq [IntPtr]::Zero) { throw 'Companion has no native window.' }
    $null = Command @{ action='reduce-motion'; enabled=$true }
    $null = WaitFor $viewPath { param($v) $v.reduceMotion } 'reduce motion reaches actual companion'
    $null = Command @{ action='reduce-motion'; enabled=$false }
    $null = WaitFor $viewPath { param($v) !$v.reduceMotion } 'normal motion reaches actual companion'
    $checks.Add('Reduce Motion on/off synchronizes to the actual isolated companion process')
}
function CloseCompanion {
    if (!$child -or $child.HasExited) { return }
    $null = Command @{ action='close-widget' }
    if (!$child.WaitForExit(6000)) { throw 'Companion did not close.' }
    $script:child = $null; $script:viewPath = $null; $script:childWindow = [IntPtr]::Zero
}
function ApplyCase([string]$name) {
    if ($name -like '*companion*') { EnsureCompanion } else { CloseCompanion }
    if ($name -eq 'foreground-main') {
        $control.Hide()
        [BackgroundNative]::ShowWindow($window,9) | Out-Null
        if (!(ActivateWindow $window)) { throw ('Interactive desktop refused foreground activation. target='+$window+' '+[BackgroundNative]::Describe($main.Id)) }
    } elseif ($name -eq 'unfocused-visible-main') {
        [BackgroundNative]::ShowWindow($window,9) | Out-Null
        $control.Show(); if (!(ActivateWindow $control.Handle)) { throw 'Cannot activate measurement control window.' }
    } else {
        [BackgroundNative]::ShowWindow($window,6) | Out-Null
        if ($name -eq 'minimized-main-companion-visible') {
            $control.Hide()
            [BackgroundNative]::ShowWindow($childWindow,9) | Out-Null
            if (!(ActivateWindow $childWindow)) { throw 'Cannot activate companion.' }
        } else {
            if ($child) { [BackgroundNative]::ShowWindow($childWindow,6) | Out-Null }
            $control.Show(); if (!(ActivateWindow $control.Handle)) { throw 'Cannot activate measurement control window.' }
        }
    }
    $expectedFocused = $name -eq 'foreground-main'
    $null = WaitFor $resultPath { param($v) $v.focused -eq $expectedFocused -and !$v.verificationQuiet } 'focus state updated'
    $until = [DateTime]::UtcNow.AddSeconds($WarmupSeconds); while ([DateTime]::UtcNow -lt $until) { Pump }
    $expectedMinimized = $name -like 'minimized*'
    if ([BackgroundNative]::IsIconic($window) -ne $expectedMinimized -or ![BackgroundNative]::IsWindowVisible($window)) { throw 'Main native visibility does not match measurement case.' }
    if ($child -and [BackgroundNative]::IsIconic($childWindow) -ne ($name -eq 'minimized-main-companion-minimized')) { throw 'Companion native visibility does not match measurement case.' }
}
function ProcessSnapshot($process) {
    $process.Refresh()
    return @{ cpu=$process.TotalProcessorTime.TotalSeconds; working=$process.WorkingSet64; private=$process.PrivateMemorySize64; io=[BackgroundNative]::ReadIo($process.Handle) }
}
function TimerSeconds([string]$value) {
    $parts = $value.Split(':'); if ($parts.Count -eq 2) { return [double]$parts[0]*60+[double]$parts[1] }
    if ($parts.Count -eq 3) { return [double]$parts[0]*3600+[double]$parts[1]*60+[double]$parts[2] }
    throw "Unexpected timer format: $value"
}
function PipeCommand($state,[string]$action,[string]$owner,[string]$session,[string]$request) {
    $path = Join-Path $state.companionDirectory 'pipe-command.request'
    $prior = ReadJson $viewPath; $timer = [Diagnostics.Stopwatch]::StartNew()
    WriteJson ($path+'.tmp') @{ ownerId=$owner; sessionId=$session; requestId=$request; action=$action }; [IO.File]::Move($path+'.tmp',$path)
    $ack = WaitFor $viewPath { param($v) $v.lastAck -eq $request -and $v.ackCount -gt $prior.ackCount } 'real pipe acknowledgement'
    return @{ state=$ack; milliseconds=$timer.Elapsed.TotalMilliseconds }
}
function MeasureCase([string]$name,[int]$repeat) {
    ApplyCase $name
    $quietSeconds = $SampleSeconds+3
    $childBefore = if ($child) { ReadJson $viewPath } else { $null }
    $childMeasureBefore = $null
    if ($child) {
        $request = Join-Path ([IO.Path]::GetDirectoryName($viewPath)) 'companion-ui.request'
        if (Test-Path -LiteralPath $request) { throw 'Previous companion UI command is still pending.' }
        [IO.File]::WriteAllText($request+'.tmp',"performance-quiet:$quietSeconds",$utf8); [IO.File]::Move($request+'.tmp',$request)
        $until = [DateTime]::UtcNow.AddSeconds(5)
        while (Test-Path -LiteralPath $request) { if ([DateTime]::UtcNow -gt $until) { throw 'Companion quiet command not consumed.' }; Pump }
        $childMeasureBefore = ReadJson $viewPath
    }
    $before = Command @{ action='performance-quiet'; seconds=$quietSeconds }
    if (!$before.verificationQuiet) { throw 'Main quiet command was not applied.' }
    if ($null -eq $before.cameraRenders -or !$before.cameras) { throw 'Main camera attribution counters are unavailable; rebuild the current Development Player.' }
    if ($child -and ($null -eq $childMeasureBefore.cameraRenders -or !$childMeasureBefore.cameras)) { throw 'Companion camera attribution counters are unavailable.' }
    $mainStamp = [IO.File]::GetLastWriteTimeUtc($resultPath)
    $childStamp = if ($child) { [IO.File]::GetLastWriteTimeUtc($viewPath) } else { $null }
    $startMain = ProcessSnapshot $main; $startChild = if ($child) { ProcessSnapshot $child } else { $null }
    $sample = [Diagnostics.Stopwatch]::StartNew()
    while ($sample.Elapsed.TotalSeconds -lt $SampleSeconds) { Pump 100 }
    $wall = $sample.Elapsed.TotalSeconds
    $endMain = ProcessSnapshot $main; $endChild = if ($child) { ProcessSnapshot $child } else { $null }
    Check (([BackgroundNative]::GetForegroundWindow() -eq $window) -eq ($name -eq 'foreground-main') -and [BackgroundNative]::IsIconic($window) -eq ($name -like 'minimized*')) "$name native window state remains correct at sample end"
    if ($child) { Check ([BackgroundNative]::IsIconic($childWindow) -eq ($name -eq 'minimized-main-companion-minimized')) "$name companion visibility remains correct at sample end" }
    Check ([IO.File]::GetLastWriteTimeUtc($resultPath) -eq $mainStamp) "$name probe writes are silent during CPU sample"
    if ($child) { Check ([IO.File]::GetLastWriteTimeUtc($viewPath) -eq $childStamp) "$name companion probe writes are silent" }
    $after = WaitFor $resultPath { param($v) !$v.verificationQuiet -and $v.utc -ne $before.utc } 'automatic probe resumption' 10
    $clockWall = ([DateTimeOffset]::Parse($after.utc)-[DateTimeOffset]::Parse($before.utc)).TotalSeconds
    $drift = ($after.elapsed-$before.elapsed)-$clockWall
    Check ($after.phase -eq 'Focusing' -and $after.sessionId -eq $before.sessionId -and [Math]::Abs($drift) -lt 1) "$name Focus follows real time without restarting or settling"
    Check ($after.focused -eq ($name -eq 'foreground-main')) "$name Unity focus state remains correct"
    $latency = $null; $view=$null; $childRenderWall=$null
    if ($child) {
        $view = WaitFor $viewPath { param($v) $v.connected -and $v.timer -ne $childBefore.timer -and [IO.File]::GetLastWriteTimeUtc($viewPath) -gt $childStamp } 'companion resumes probe'
        Check ($view.connections -eq $childBefore.connections -and [Math]::Abs((TimerSeconds $view.timer)-$after.elapsed) -le 2) "$name real IPC stays connected and displayed timer agrees"
        $childRenderWall=([DateTimeOffset]::Parse($view.utc)-[DateTimeOffset]::Parse($childMeasureBefore.utc)).TotalSeconds
        $ack = PipeCommand $after 'start' $after.ownerId $after.sessionId ([Guid]::NewGuid().ToString('N'))
        Check (!$ack.state.lastAccepted) "$name already-running start is acknowledged without duplicate session"
        $latency = $ack.milliseconds
    }
    WriteJson (Join-Path $folder ($name+'-'+$repeat+'-render.json')) @{ mainBefore=$before; mainAfter=$after; companionBefore=$childMeasureBefore; companionAfter=$view }
    $row = [pscustomobject]@{
        case=$name; repeat=$repeat; sampleSeconds=$wall; mainOneCoreCpuPercent=100*($endMain.cpu-$startMain.cpu)/$wall;
        mainMachineCpuPercent=100*($endMain.cpu-$startMain.cpu)/$wall/[Environment]::ProcessorCount;
        mainWorkingSetMiB=$endMain.working/1MB; mainPrivateMiB=$endMain.private/1MB; mainWorkingSetDeltaMiB=($endMain.working-$startMain.working)/1MB;
        mainReadBytes=$endMain.io.ReadBytes-$startMain.io.ReadBytes; mainWriteBytes=$endMain.io.WriteBytes-$startMain.io.WriteBytes;
        companionOneCoreCpuPercent=if($child){100*($endChild.cpu-$startChild.cpu)/$wall}else{$null};
        companionWorkingSetMiB=if($child){$endChild.working/1MB}else{$null}; companionPrivateMiB=if($child){$endChild.private/1MB}else{$null};
        companionReadBytes=if($child){$endChild.io.ReadBytes-$startChild.io.ReadBytes}else{$null}; companionWriteBytes=if($child){$endChild.io.WriteBytes-$startChild.io.WriteBytes}else{$null};
        focusClockDriftSeconds=$drift; observedAckMilliseconds=$latency; targetFrameRate=$after.targetFrameRate
        mainRenderIntervalSeconds=$clockWall; mainFrameHz=($after.frameCount-$before.frameCount)/$clockWall;
        mainCameraRenderHz=($after.cameraRenders-$before.cameraRenders)/$clockWall; mainTextureCameraRenderHz=($after.textureCameraRenders-$before.textureCameraRenders)/$clockWall;
        companionRenderIntervalSeconds=$childRenderWall;
        companionFrameHz=if($child){($view.frameCount-$childMeasureBefore.frameCount)/$childRenderWall}else{$null};
        companionCameraRenderHz=if($child){($view.cameraRenders-$childMeasureBefore.cameraRenders)/$childRenderWall}else{$null};
        companionTextureCameraRenderHz=if($child){($view.textureCameraRenders-$childMeasureBefore.textureCameraRenders)/$childRenderWall}else{$null}
    }
    $metrics.Add($row); $metrics | Export-Csv -LiteralPath (Join-Path $folder 'metrics.csv') -NoTypeInformation
    Write-Output ("MEASURE {0} repeat {1}: main {2:F2}% of one core, companion {3}, private {4:F1} MiB" -f $name,$repeat,$row.mainOneCoreCpuPercent,$row.companionOneCoreCpuPercent,$row.mainPrivateMiB)
    Write-Output ("RENDER {0}: main frames {1:F2}/s, all cameras {2:F2}/s, RT cameras {3:F2}/s; child frames {4}, RT cameras {5}" -f $name,$row.mainFrameHz,$row.mainCameraRenderHz,$row.mainTextureCameraRenderHz,$row.companionFrameHz,$row.companionTextureCameraRenderHz)
}
function ActivePreview($state,[string]$kind) {
    return @($state.previews | Where-Object { $_.active -and $_.kind -eq $kind })
}
function PreviewReady($state,[string]$kind,[bool]$cameraEnabled) {
    $active = @(ActivePreview $state $kind)
    return $active.Count -eq 1 -and $active[0].cameraEnabled -eq $cameraEnabled -and $active[0].targetCreated -and $active[0].rawBound -and $active[0].stageActive
}
function CameraRenders($state,[int]$cameraId) {
    $entry = @($state.cameras | Where-Object { $_.instanceId -eq $cameraId })
    if ($entry.Count -ne 1) { throw "Missing measured camera $cameraId" }
    return [long]$entry[0].renders
}
function VerifyPreviews {
    $control.Hide(); [BackgroundNative]::ShowWindow($window,9) | Out-Null
    Check (ActivateWindow $window) 'Preview verification owns the actual foreground window'
    $null=Command @{action='configure';mode=0}; $started=Command @{action='start'}
    $session=$started.sessionId
    foreach ($previewPageName in @('Dashboard','CatHome','Focus','Boss')) {
        $previewKind = if($previewPageName -in @('Dashboard','CatHome')){'Home'}else{$previewPageName}
        $null=Command @{action='preview-page';page=$previewPageName}
        $before=WaitFor $resultPath {param($v) $v.previewPage -eq $previewPageName -and (PreviewReady $v $previewKind $true)} ('ready '+$previewPageName) 45
        $selected=@(ActivePreview $before $previewKind)[0]
        $beforeRenders=CameraRenders $before $selected.cameraId
        $null=WaitFor $resultPath {param($v) (CameraRenders $v $selected.cameraId) -gt $beforeRenders} ('render '+$previewPageName)
        [BackgroundNative]::ShowWindow($window,6) | Out-Null
        $minimized=WaitFor $resultPath {param($v) $v.previewPage -eq $previewPageName -and (PreviewReady $v $previewKind $false)} ('minimized '+$previewPageName)
        Check ([BackgroundNative]::IsIconic($window)) "$previewPageName is actually minimized"
        $until=[DateTime]::UtcNow.AddSeconds(3);while([DateTime]::UtcNow -lt $until){Pump}
        $steady=ReadJson $resultPath
        Check ((CameraRenders $steady $selected.cameraId) -eq (CameraRenders $minimized $selected.cameraId)) "$previewPageName preview stops rendering while minimized"
        Check ($steady.phase -eq 'Focusing' -and $steady.sessionId -eq $session -and $steady.elapsed - $minimized.elapsed -gt 2) "$previewPageName minimization preserves real Focus timing"
        [BackgroundNative]::ShowWindow($window,9) | Out-Null
        Check (ActivateWindow $window) "$previewPageName restores native foreground"
        $restored=WaitFor $resultPath {param($v) $v.previewPage -eq $previewPageName -and (PreviewReady $v $previewKind $true) -and (CameraRenders $v $selected.cameraId) -gt (CameraRenders $steady $selected.cameraId)} ('restored '+$previewPageName)
        $restoredPreview=@(ActivePreview $restored $previewKind)[0]
        Check ($restoredPreview.cameraId -eq $selected.cameraId -and $restoredPreview.targetId -eq $selected.targetId -and $restoredPreview.stageId -eq $selected.stageId) "$previewPageName reuses its stage camera and RenderTexture after restore"
        if($previewPageName -in @('Focus','Boss')) { Check (@($restored.previews | Where-Object {$_.kind -eq 'Home' -and $_.cameraEnabled}).Count -eq 0) "$previewPageName leaves hidden Home cameras disabled" }
        WriteJson (Join-Path $folder ($previewPageName+'-states.json')) @{before=$before;minimized=$minimized;steady=$steady;restored=$restored}
        $captureName=$previewPageName+'-restored.png';$null=Command @{action='screenshot';screenshot=$captureName}
        $until=[DateTime]::UtcNow.AddSeconds(5);while(!(Test-Path -LiteralPath (Join-Path $folder $captureName))){if([DateTime]::UtcNow -gt $until){throw 'Preview screenshot did not appear'};Pump}
    }
    $entry=Command @{action='boss-start'}
    Check ($entry.battleCount -eq 1 -and $entry.tickets -eq 2 -and $entry.bossActive -and $entry.expectedBattleOutcome -in @('Victory','Defeat')) 'Boss entry consumes exactly one isolated ticket'
    $duplicate=Command @{action='boss-confirm-again'}
    Check ($duplicate.battleCount -eq 1 -and $duplicate.battleId -eq $entry.battleId -and $duplicate.tickets -eq 2) 'Duplicate confirmation does not create a second entry'
    [BackgroundNative]::ShowWindow($window,6) | Out-Null
    $minimized=WaitFor $resultPath {param($v) (PreviewReady $v 'Boss' $false)} 'active Boss minimized'
    $completed=WaitFor $resultPath {param($v) $v.battleId -eq $entry.battleId -and $v.battleOutcome -eq $entry.expectedBattleOutcome} 'Boss continues to deterministic outcome while minimized' 90
    Check ([BackgroundNative]::IsIconic($window) -and $completed.battleCount -eq 1 -and $completed.tickets -eq 2) 'Minimized Boss settles one valid entry without stopped/error recovery'
    Check ($completed.simulationElapsed -gt $minimized.simulationElapsed -and $completed.exp -eq 10000+$completed.battleExperience -and $completed.coins -eq $completed.battleCoins) 'Boss simulation and committed rewards continue with its camera disabled'
    [BackgroundNative]::ShowWindow($window,9) | Out-Null;Check (ActivateWindow $window) 'Boss result restores foreground'
    $restored=WaitFor $resultPath {param($v) PreviewReady $v 'Boss' $true} 'Boss result camera restored'
    $duplicate=Command @{action='boss-confirm-again'}
    Check ($duplicate.battleId -eq $completed.battleId -and $duplicate.exp -eq $completed.exp -and $duplicate.coins -eq $completed.coins -and $duplicate.tickets -eq 2) 'Restored result and repeated confirmation cannot settle twice'
    # Victory commits before its existing death/result presentation finishes.
    $until=[DateTime]::UtcNow.AddSeconds(3);while([DateTime]::UtcNow -lt $until){Pump}
    $resultView=WaitFor $resultPath {param($v) [DateTimeOffset]::Parse($v.utc).UtcDateTime -ge $until -and $v.battleOutcome -eq $completed.battleOutcome} 'fresh rendered Boss result after its presentation'
    $null=Command @{action='screenshot';screenshot='Boss-result-restored.png'}
    $until=[DateTime]::UtcNow.AddSeconds(5);while(!(Test-Path -LiteralPath (Join-Path $folder 'Boss-result-restored.png'))){if([DateTime]::UtcNow -gt $until){throw 'Boss result screenshot did not appear'};Pump}
    WriteJson (Join-Path $folder 'boss-continuity.json') @{entry=$entry;minimized=$minimized;completed=$completed;restored=$restored;duplicate=$duplicate;resultView=$resultView}
    $null=Command @{action='preview-page';page='Focus'}
    $null=Command @{action='cancel'};$final=Command @{action='confirm-cancel'}
    Check ($final.phase -eq 'Interrupted' -and $final.exp -eq $completed.exp -and $final.coins -eq $completed.coins -and $final.tickets -eq 2) 'Preview verification cancels Focus without extra rewards'
    WriteJson (Join-Path $folder 'final-state.json') $final
}
try {
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($registryPath)
    if ($key) { $existed=$true; try { foreach ($name in $key.GetValueNames()) { $backup += @{ name=$name; kind=$key.GetValueKind($name); value=$key.GetValue($name,$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames) } } } finally { $key.Dispose() } }
    $prefsCaptured=$true; $backup | Export-Clixml -LiteralPath (Join-Path $folder 'playerprefs-backup.xml')
    $managed = Join-Path ([IO.Path]::GetDirectoryName($exe)) ([IO.Path]::GetFileNameWithoutExtension($exe)+'_Data/Managed/Assembly-CSharp.dll')
    WriteJson (Join-Path $folder 'context.json') @{ utc=[DateTime]::UtcNow.ToString('O'); executable=$exe; sha256=(Get-FileHash -LiteralPath $exe).Hash; managedAssemblySha256=if(Test-Path -LiteralPath $managed){(Get-FileHash -LiteralPath $managed).Hash}else{$null}; logicalProcessors=[Environment]::ProcessorCount; sampleSeconds=$SampleSeconds; repeats=$Repeats; warmupSeconds=$WarmupSeconds; previewVerification=[bool]$VerifyPreviewsOnly; fixtureExperience=if($VerifyPreviewsOnly){10000}else{100}; backend='D3D11'; resolution='1280x720'; isolated=$true; os=[Environment]::OSVersion.ToString() }
    [IO.File]::WriteAllText((Join-Path $folder 'measurement-notes.txt'),'Development Player, D3D11, 1280x720, real monotonic Focus with no artificial advance. Probe file polling/writes are boundedly silenced during each sample; real rendering and named pipes remain active. CPU uses process TotalProcessorTime delta; 100% one-core means one logical core, machine percent divides by logical cores. Working/private memory are process resident/committed measures, NOT GPU memory or allocation rate. IPC latency includes smoke-frame and 50ms polling overhead. Render/frame rates use cumulative counters across the full bounded quiet interval (about sampleSeconds+3); CPU uses the inner sampleSeconds interval, so these intervals are reported separately. Per-camera names/types/counts are retained in each render JSON. GPU time, power draw and battery life are not measured. Repeat 2 reverses case order. No runtime optimization is justified solely by a nonzero minimized CPU value.',$utf8)
    WriteJson (Join-Path $folder 'test-save.json') @{ version=5; username='Background measurement'; experience=if($VerifyPreviewsOnly){10000}else{100}; tickets=if($VerifyPreviewsOnly){3}else{0}; tagsInitialized=$true; tutorial=@{version=1;dismissed=$true;taskStep=4;announcedLevel=99}; settings=@{notificationsEnabled=$false;soundVolume=0;musicVolume=0;reduceMotion=$false} }
    $control = [Windows.Forms.Form]::new(); $control.Text='Cat Quest measurement control'; $control.Width=280; $control.Height=90; $control.StartPosition='Manual'; $control.Left=0; $control.Top=0; $control.Show()
    $arguments='--focus-smoke --smoke-dir "'+$folder+'" -force-d3d11 -logFile "'+$folder+'/Player.log" -screen-fullscreen 0 -screen-width 1280 -screen-height 720'
    $main = Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory ([IO.Path]::GetDirectoryName($exe)) -WindowStyle Hidden -PassThru
    $resultPath = Join-Path $folder 'smoke-result.json'
    $state = WaitFor $resultPath { param($v) $v.controlsBound -and $v.phase -eq 'Ready' } 'isolated startup' 60
    Check $state.developmentBuild 'Current executable is a Development Player'
    $window = [BackgroundNative]::Window($main.Id); Check ($window -ne [IntPtr]::Zero) 'Real main native window exists'
    $null = Command @{action='close-widget'}
    if ($VerifyPreviewsOnly) { VerifyPreviews } else {
    $null = Command @{action='configure';mode=1}; $state = Command @{action='start'}
    Check ($state.phase -eq 'Focusing') 'Isolated stopwatch starts without artificial time advance'
    $null = WaitFor $resultPath { param($v) $v.training -and ![string]::IsNullOrEmpty($v.ninjaSprite) -and ![string]::IsNullOrEmpty($v.acornSprite) } 'main training scene ready before sampling' 40
    $cases = @('foreground-main','unfocused-visible-main','minimized-main','minimized-main-companion-visible','minimized-main-companion-minimized')
    for ($repeat=1; $repeat -le $Repeats; $repeat++) {
        $order = @($cases); if ($repeat%2 -eq 0) { [Array]::Reverse($order) }
        foreach ($caseName in $order) { MeasureCase $caseName $repeat }
    }
    EnsureCompanion
    $state = ReadJson $resultPath
    $ack = PipeCommand $state 'end' 'wrong-owner' $state.sessionId ([Guid]::NewGuid().ToString('N')); Check (!$ack.state.lastAccepted) 'Wrong-owner command rejected by actual host'
    $ack = PipeCommand $state 'end' $state.ownerId 'stale-session' ([Guid]::NewGuid().ToString('N')); Check (!$ack.state.lastAccepted) 'Stale-session command rejected by actual host'
    $request = [Guid]::NewGuid().ToString('N')
    $ack = PipeCommand $state 'cancel' $state.ownerId $state.sessionId $request; Check $ack.state.lastAccepted 'Valid cancellation crosses actual pipe after background sampling'
    $final = WaitFor $resultPath { param($v) $v.phase -eq 'Interrupted' } 'cancelled session'
    $ack = PipeCommand $state 'cancel' $state.ownerId $state.sessionId $request; Check (!$ack.state.lastAccepted) 'Duplicate cancellation request rejected'
    Check ($final.exp -eq 100 -and $final.coins -eq 0 -and $final.tickets -eq 0) 'Measurement cancellation grants no rewards'
    WriteJson (Join-Path $folder 'final-state.json') $final
    }
} catch { $failure=$_.ToString(); Write-Output "FAIL: $failure" }
finally {
    # A failed initial handshake may have launched a child before EnsureCompanion
    # acquired its handle. Capture only this parent's exact executable children.
    $ownedChildren = @(); if ($child) { $ownedChildren += $child }
    try {
        if ($main) {
            foreach ($entry in @(Get-CimInstance Win32_Process | Where-Object { $_.ParentProcessId -eq $main.Id -and $_.ExecutablePath -eq $exe })) {
                if ($entry.ProcessId -notin $ownedChildren.Id) {
                    $owned = Get-Process -Id $entry.ProcessId -ErrorAction SilentlyContinue
                    if ($owned) { $ownedChildren += $owned }
                }
            }
        }
    } catch { CleanupFailure 'enumerating owned companion' $_ }
    try {
        if ($main -and !$main.HasExited) {
            try { $null=Command @{action='quit'} } catch {}
            if (!$main.WaitForExit(8000)) { $main.Kill(); if (!$main.WaitForExit(3000)) { throw 'Owned main process did not exit.' } }
        }
    } catch { CleanupFailure 'main process' $_ }
    foreach ($owned in $ownedChildren) {
        try { if (!$owned.HasExited -and !$owned.WaitForExit(6000)) { $owned.Kill(); if (!$owned.WaitForExit(3000)) { throw 'Owned companion did not exit.' } } }
        catch { CleanupFailure ('companion '+$owned.Id) $_ }
    }
    if ($prefsCaptured) {
        try {
            $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($registryPath)
            try {
                foreach ($name in $key.GetValueNames()) { if ($name -notin $backup.name) { $key.DeleteValue($name,$false) } }
                foreach ($value in $backup) { $key.SetValue($value.name,$value.value,$value.kind) }
                if (@($key.GetValueNames()).Count -ne $backup.Count) { throw 'Restored PlayerPrefs key count differs.' }
                foreach ($value in $backup) {
                    $actual = $key.GetValue($value.name,$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames) | ConvertTo-Json -Compress
                    $expected = $value.value | ConvertTo-Json -Compress
                    if ($key.GetValueKind($value.name) -ne $value.kind -or $actual -ne $expected) { throw 'Restored PlayerPrefs value/type differs.' }
                }
            } finally { $key.Dispose() }
            if (!$existed) { [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKey($registryPath,$false) }
            $prefsRestored=$true
        } catch { CleanupFailure 'PlayerPrefs restore' $_ }
    }
    try { if ($control) { $control.Close(); $control.Dispose() } } catch { CleanupFailure 'measurement window' $_ }
    try { if ($previousForeground -ne [IntPtr]::Zero) { $foregroundRestored=[BackgroundNative]::Foreground($previousForeground) } } catch { CleanupFailure 'foreground restore' $_ }
    try { [BackgroundNative]::SetCursorPos($previousCursor.X,$previousCursor.Y) | Out-Null } catch { CleanupFailure 'cursor restore' $_ }
    WriteJson (Join-Path $folder 'result.json') @{ utc=[DateTime]::UtcNow.ToString('O'); passed=($null -eq $failure); checks=$checks; failure=$failure; restoredPlayerPrefs=$prefsRestored; restoredForeground=$foregroundRestored; metrics=$metrics }
    Write-Output "Evidence: $folder"
}
if ($failure) { exit 1 }
