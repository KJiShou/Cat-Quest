# Capture the next incident of a running Cat Quest main process. Run in elevated
# Windows PowerShell with -TargetId <PID>. The existing dump watcher can run alongside it.
[CmdletBinding()]
param([Parameter(Mandatory)][int]$TargetId, [switch]$CaptureFlagOnly, [switch]$Passive)

$ErrorActionPreference = 'Stop'
$target = Get-Process -Id $TargetId
$info = Get-CimInstance Win32_Process -Filter "ProcessId = $TargetId"
if ($target.ProcessName -ne 'CatQuest' -or !$info.CommandLine -or
    $info.CommandLine -match '(?:^|\s)--companion(?:\s|$)') { throw 'Select the Cat Quest main process.' }
$session = "CatQuestScheduler-$TargetId"
$root = Join-Path $env:LOCALAPPDATA 'CatQuest\HangDiagnostics'
$dir = Join-Path $root ("scheduler-{0}-pid{1}" -f (Get-Date -Format yyyyMMdd-HHmmssfff), $TargetId)
$null = New-Item -ItemType Directory -Path $dir
$wpr = Join-Path $env:SystemRoot 'System32\wpr.exe'
$trace = Join-Path $dir 'scheduler.etl'
$samples = Join-Path $dir 'messages.csv'
$ring = New-Object 'System.Collections.Generic.Queue[string]'
$header = 'startUtc,endUtc,stopwatchTicks,hwnd,threadId,foreground,minimized,windowsHung,replied,elapsedMs,win32Error'

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class CQMessageProbe {
    [DllImport("user32")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint process);
    [DllImport("user32")] public static extern bool IsHungAppWindow(IntPtr h);
    [DllImport("user32")] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32")] public static extern short GetAsyncKeyState(int key);
    [DllImport("user32", SetLastError=true)] public static extern IntPtr SendMessageTimeoutW(IntPtr h, uint message,
        UIntPtr wParam, IntPtr lParam, uint flags, uint timeout, out UIntPtr result);
}
'@

function Save-State($status) {
    [ordered]@{ status=$status; timeUtc=[DateTime]::UtcNow.ToString('o'); processId=$TargetId
        executable=$info.ExecutablePath; processStarted=$targetStart; watcherPid=$PID
        stopwatchFrequency=[Diagnostics.Stopwatch]::Frequency; detectedUtc=$detected
        traceResult=$traceResult; session=$session; passive=[bool]$Passive; trigger=$reason } | ConvertTo-Json | Set-Content (Join-Path $dir 'status.json')
}

$targetStart = $target.StartTime.ToUniversalTime().ToString('o')
$detected = $null
$traceResult = 'pending'
$ownsTrace = $false
$export = $null
$writer = $null
$failSince = $null
$nextHeartbeat = 0
$triggerMs = $null
$recovered = $false
$reason = $null
$clock = [Diagnostics.Stopwatch]::StartNew()
try {
    $profiles = @('-start','CPU')
    if ($Passive) { $profiles += @('-start','GPU') }
    & $wpr @profiles -instancename $session *> (Join-Path $dir 'start.log')
    if ($LASTEXITCODE -ne 0) { throw 'WPR start failed. See start.log.' }
    $ownsTrace = $true
    Save-State 'armed'
    while (!$target.HasExited) {
        if (Test-Path -LiteralPath (Join-Path $dir 'stop.request')) { break }
        $target.Refresh()
        $h = $target.MainWindowHandle
        [uint32]$owner = 0
        $tid = [CQMessageProbe]::GetWindowThreadProcessId($h,[ref]$owner)
        if ($owner -eq $TargetId -and $tid) {
            $hung = [CQMessageProbe]::IsHungAppWindow($h)
            $fg = [CQMessageProbe]::GetForegroundWindow() -eq $h
            $mini = [CQMessageProbe]::IsIconic($h)
            $utc = [DateTime]::UtcNow.ToString('o')
            $ticks = [Diagnostics.Stopwatch]::GetTimestamp()
            [UIntPtr]$reply = [UIntPtr]::Zero
            # Read only the marker chord; skip ordinary WM_NULL probes in passive mode.
            $marked = $Passive -and [CQMessageProbe]::GetAsyncKeyState(0x77) -lt 0 -and
                [CQMessageProbe]::GetAsyncKeyState(0x11) -lt 0 -and [CQMessageProbe]::GetAsyncKeyState(0x10) -lt 0
            $ok = $null
            $ms = $null
            $err = $null
            if (!$Passive -or $marked -or $hung -or $detected -or $null -ne $failSince) {
                $ok = [CQMessageProbe]::SendMessageTimeoutW($h,0,[UIntPtr]::Zero,[IntPtr]::Zero,3,400,[ref]$reply) -ne [IntPtr]::Zero
                $err = if ($ok) { 0 } else { [Runtime.InteropServices.Marshal]::GetLastWin32Error() }
                $ms = [int](([Diagnostics.Stopwatch]::GetTimestamp()-$ticks)*1000.0/[Diagnostics.Stopwatch]::Frequency)
            }
            $line = "$utc,$([DateTime]::UtcNow.ToString('o')),$ticks,$($h.ToInt64()),$tid,$fg,$mini,$hung,$ok,$ms,$err"
            if ($writer) { $writer.WriteLine($line) } else {
                $ring.Enqueue($line)
                if ($ring.Count -gt 240) { $null = $ring.Dequeue() }
            }
            if ($ok) { $failSince = $null } elseif ($ok -eq $false -and $null -eq $failSince) { $failSince = $clock.ElapsedMilliseconds }
            if (!$detected -and $clock.ElapsedMilliseconds -ge $nextHeartbeat) {
                $line | Set-Content (Join-Path $dir 'latest-sample.csv')
                $nextHeartbeat = $clock.ElapsedMilliseconds + 5000
            }
            if (!$detected -and ($marked -or ($CaptureFlagOnly -and $hung) -or ($null -ne $failSince -and $clock.ElapsedMilliseconds-$failSince -ge 2000))) {
                $reason = if ($marked) { 'userMarker' } elseif ($CaptureFlagOnly -and $hung) { 'windowsFlag' } else { 'messageTimeouts' }
                $detected = $utc
                $triggerMs = $clock.ElapsedMilliseconds
                $writer = [IO.StreamWriter]::new($samples)
                $writer.AutoFlush = $true
                $writer.WriteLine($header)
                foreach ($row in $ring) { $writer.WriteLine($row) }
                Save-State 'detected'
            } elseif ($detected -and !$recovered -and $ok -and !$hung -and ($reason -ne 'userMarker' -or $clock.ElapsedMilliseconds-$triggerMs -ge 3000)) {
                $recovered = $true
                [IO.File]::WriteAllText((Join-Path $dir 'recovered-utc.txt'),[DateTime]::UtcNow.ToString('o'))
            }
        }
        if ($detected -and !$export -and ($recovered -or $clock.ElapsedMilliseconds-$triggerMs -ge 3000)) {
            $psi = [Diagnostics.ProcessStartInfo]::new($wpr, "-stop `"$trace`" -instancename $session")
            $psi.UseShellExecute = $false
            $psi.CreateNoWindow = $true
            $export = [Diagnostics.Process]::Start($psi)
        }
        if ($recovered) { break }
        Start-Sleep -Milliseconds 250
    }
} catch {
    $_ | Out-String | Set-Content (Join-Path $dir 'error.txt')
    throw
} finally {
    if ($writer) { $writer.Dispose() }
    if ($ownsTrace -and $detected -and !$export) {
        & $wpr -stop $trace -instancename $session *> (Join-Path $dir 'export.log')
        if ($LASTEXITCODE -eq 0 -and (Test-Path $trace)) { $ownsTrace = $false; $traceResult = 'saved' }
        else { $traceResult = 'exportFailed' }
    } elseif ($export) {
        $export.WaitForExit()
        if ($export.ExitCode -eq 0 -and (Test-Path $trace) -and (Get-Item $trace).Length -gt 0) {
            $ownsTrace = $false
            $traceResult = 'saved'
        } else { $traceResult = 'exportFailed' }
    }
    if ($ownsTrace) {
        $ErrorActionPreference = 'Continue'
        & $wpr -cancel -instancename $session *> (Join-Path $dir 'cancel.log')
    }
    Save-State 'stopped'
    $target.Dispose()
}
