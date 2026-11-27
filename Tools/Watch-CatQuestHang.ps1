<#
.SYNOPSIS
Watches the specified Windows executable for unresponsive main windows and saves local diagnostic dumps.
.DESCRIPTION
Cat Quest Companion processes are excluded. The watcher follows process restarts while this console is
open. Each hang produces incident.json, a minidump when possible, and outcome.json if the window
recovers or the process exits. Dumps can contain app data. The watcher neither uploads files nor
closes the target. Stop the watcher with Ctrl+C.
.EXAMPLE
.\Tools\Watch-CatQuestHang.ps1 -Executable 'D:\Apps\Cat Quest\CatQuest.exe'
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Executable,
    [string]$OutputDirectory = (Join-Path $env:LOCALAPPDATA 'CatQuest\HangDiagnostics'),
    [ValidateRange(100, 2000)][int]$PollMilliseconds = 250,
    [ValidateRange(0, 1000)][int]$MaxDumps = 20
)

$ErrorActionPreference = 'Stop'
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT -or ![Environment]::Is64BitProcess) {
    throw 'Run this script in 64-bit Windows PowerShell.'
}
$exe = Get-Item -LiteralPath $Executable
if ($exe.PSIsContainer -or $exe.Extension -ne '.exe') {
    throw 'Executable must be the path to a Windows .exe file.'
}
$null = New-Item -ItemType Directory -Path $OutputDirectory -Force
$outputRoot = (Get-Item -LiteralPath $OutputDirectory).FullName

if (-not ('CatQuestHangNative' -as [type])) {
    Add-Type -TypeDefinition @"
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using FILETIME = System.Runtime.InteropServices.ComTypes.FILETIME;
using Microsoft.Win32.SafeHandles;

public static class CatQuestHangNative
{
    [DllImport("user32.dll")]
    public static extern bool IsHungAppWindow(IntPtr window);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr OpenProcess(uint access, bool inherit, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetProcessTimes(IntPtr process, out FILETIME creation, out FILETIME exit,
        out FILETIME kernel, out FILETIME user);

    [DllImport("kernel32.dll")]
    static extern bool CloseHandle(IntPtr handle);

    [DllImport("Dbghelp.dll", SetLastError = true)]
    static extern bool MiniDumpWriteDump(IntPtr process, int processId, SafeFileHandle file,
        uint dumpType, IntPtr exception, IntPtr userStream, IntPtr callback);

    public static void WriteDump(int processId, long expectedCreationTime, string path)
    {
        const uint ProcessVmRead = 0x10;
        const uint ProcessQueryInformation = 0x400;
        const uint MiniDumpWithUnloadedModules = 0x20;
        const uint MiniDumpWithThreadInfo = 0x1000;

        IntPtr process = OpenProcess(ProcessVmRead | ProcessQueryInformation, false, processId);
        if (process == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            FILETIME creation, exit, kernel, user;
            if (!GetProcessTimes(process, out creation, out exit, out kernel, out user))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            long actualCreationTime = ((long)creation.dwHighDateTime << 32) | (uint)creation.dwLowDateTime;
            if (actualCreationTime != expectedCreationTime)
                throw new InvalidOperationException("The original process exited and its PID was reused.");

            using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                if (!MiniDumpWriteDump(process, processId, file.SafeFileHandle,
                    MiniDumpWithUnloadedModules | MiniDumpWithThreadInfo,
                    IntPtr.Zero, IntPtr.Zero, IntPtr.Zero))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }
        finally
        {
            CloseHandle(process);
        }
    }
}
"@
}

function Write-Record($Path, $Data) {
    $Data | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $Path -Encoding UTF8
}

function Save-Outcome($State, $Status) {
    $now = [DateTimeOffset]::Now
    Write-Record (Join-Path $State.Incident 'outcome.json') @{
        status = $Status
        observedAt = $now.ToString('o')
        secondsSinceDetection = [Math]::Round(($now - $State.DetectedAt).TotalSeconds, 3)
    }
    Write-Host "PID $($State.Process.Id): $Status"
    $State.Incident = $null
}

$tracked = @{}
$excluded = @{}
$scan = [Diagnostics.Stopwatch]::StartNew()
$nextScan = 0
$dumpCount = 0
Write-Host "Watching $($exe.FullName)"
Write-Host "Evidence directory: $outputRoot"
Write-Host 'Press Ctrl+C to stop the watcher.'

try {
    while ($true) {
        if ($scan.ElapsedMilliseconds -ge $nextScan) {
            $nextScan = $scan.ElapsedMilliseconds + 1500
            $candidates = @(Get-Process -Name $exe.BaseName -ErrorAction SilentlyContinue)
            $currentIds = @{}
            foreach ($candidate in $candidates) {
                $id = $candidate.Id
                $currentIds[$id] = $true
                try {
                    if ($tracked.ContainsKey($id) -or $excluded.ContainsKey($id)) { continue }
                    if (![string]::Equals($candidate.Path, $exe.FullName, [StringComparison]::OrdinalIgnoreCase)) {
                        continue
                    }
                    $info = Get-CimInstance Win32_Process -Filter "ProcessId = $id"
                    if (!$info.CommandLine) { continue }
                    if ($info.CommandLine -match '(?:^|\s)--companion(?=\s|$)') {
                        $excluded[$id] = $true
                        continue
                    }
                    $tracked[$id] = @{
                        Process = $candidate
                        StartedAt = $candidate.StartTime
                        Incident = $null
                        DetectedAt = $null
                    }
                    Write-Host "Tracking main process PID $id"
                } catch {
                    Write-Warning "Could not identify PID ${id}: $($_.Exception.Message)"
                } finally {
                    if (!$tracked.ContainsKey($id) -or ![object]::ReferenceEquals($tracked[$id].Process, $candidate)) {
                        $candidate.Dispose()
                    }
                }
            }
            foreach ($id in @($excluded.Keys)) {
                if (!$currentIds.ContainsKey($id)) { $excluded.Remove($id) }
            }
        }

        foreach ($id in @($tracked.Keys)) {
            $state = $tracked[$id]
            $process = $state.Process
            $process.Refresh()
            if ($process.HasExited) {
                if ($state.Incident) { Save-Outcome $state 'exited' }
                $process.Dispose()
                $tracked.Remove($id)
                continue
            }

            $window = $process.MainWindowHandle
            if ($window -eq [IntPtr]::Zero) { continue }
            [uint32]$owner = 0
            $threadId = [CatQuestHangNative]::GetWindowThreadProcessId($window, [ref]$owner)
            if ($owner -ne $id -or !$threadId) { continue }
            if (![CatQuestHangNative]::IsHungAppWindow($window)) {
                if ($state.Incident) { Save-Outcome $state 'recovered' }
                continue
            }
            if ($state.Incident) { continue }

            $detected = [DateTimeOffset]::Now
            $suffix = [Guid]::NewGuid().ToString('N').Substring(0, 8)
            $folder = Join-Path $outputRoot ("{0}-pid{1}-{2}" -f $detected.ToString('yyyyMMdd-HHmmssfff'), $id, $suffix)
            $null = New-Item -ItemType Directory -Path $folder
            $state.Incident = $folder
            $state.DetectedAt = $detected
            $record = [ordered]@{
                detectedAt = $detected.ToString('o')
                processId = $id
                processStartedAt = $state.StartedAt.ToString('o')
                executable = $exe.FullName
                windowHandle = ('0x{0:X}' -f $window.ToInt64())
                windowThreadId = $threadId
                windowsVersion = [Environment]::OSVersion.Version.ToString()
                appVersion = $null
                processCpuSeconds = $null
                workingSetBytes = $null
                dumpStatus = 'pending'
                dumpError = $null
            }
            try {
                $record.processCpuSeconds = $process.TotalProcessorTime.TotalSeconds
                $record.workingSetBytes = $process.WorkingSet64
            } catch { Write-Verbose "Process counters unavailable for PID $id." }
            Write-Record (Join-Path $folder 'incident.json') $record

            if ($MaxDumps -eq 0 -or $dumpCount -lt $MaxDumps) {
                $tempDump = Join-Path $folder 'main.dmp.partial'
                try {
                    [CatQuestHangNative]::WriteDump($id, $state.StartedAt.ToFileTimeUtc(), $tempDump)
                    Move-Item -LiteralPath $tempDump -Destination (Join-Path $folder 'main.dmp')
                    $record.dumpStatus = 'captured'
                    $dumpCount++
                } catch {
                    $record.dumpStatus = 'failed'
                    $record.dumpError = $_.Exception.GetBaseException().Message
                    if (Test-Path -LiteralPath $tempDump) { Remove-Item -LiteralPath $tempDump }
                    Write-Warning "Dump for PID ${id} failed: $($record.dumpError)"
                }
            } else {
                $record.dumpStatus = 'limitReached'
            }
            try {
                $manifest = Join-Path $exe.DirectoryName 'catquest-build.json'
                if (Test-Path -LiteralPath $manifest -PathType Leaf) {
                    $record.appVersion = (Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json).version
                }
            } catch { Write-Verbose 'Cat Quest build metadata was unavailable.' }
            $record.captureFinishedAt = [DateTimeOffset]::Now.ToString('o')
            Write-Record (Join-Path $folder 'incident.json') $record
            Write-Host "PID ${id}: not responding. Dump $($record.dumpStatus). Evidence: $folder"
        }
        Start-Sleep -Milliseconds $PollMilliseconds
    }
} finally {
    foreach ($state in $tracked.Values) { $state.Process.Dispose() }
}
