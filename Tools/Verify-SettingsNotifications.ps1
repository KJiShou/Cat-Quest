[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Executable,
    [string]$OutputDirectory,
    [switch]$Run,
    [switch]$AttemptBannerClick,
    [int]$MaximumSeconds = 180
)

$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$allowedOutput = [IO.Path]::GetFullPath((Join-Path $workspace 'Temp/SettingsVerification'))
$exePath = [IO.Path]::GetFullPath($Executable)
if (-not $Run) {
    Write-Output 'Prepared only. Add -Run after the Windows development build is ready. No player was started.'
    return
}
if (-not (Test-Path -LiteralPath $exePath -PathType Leaf)) { throw "Player not found: $exePath" }
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $allowedOutput ('native-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')) }
$runDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (-not $runDirectory.StartsWith($allowedOutput + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Output must be a new child directory inside Temp/SettingsVerification.' }
if (Test-Path -LiteralPath $runDirectory) { throw 'Use a fresh output directory; existing evidence is never overwritten.' }
if (Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -and [string]::Equals($_.ExecutablePath, $exePath, [StringComparison]::OrdinalIgnoreCase) }) { throw 'This player executable is already running. Do not overlap smoke runs or a live player.' }
[IO.Directory]::CreateDirectory($runDirectory) | Out-Null
$utf8 = [Text.UTF8Encoding]::new($false)
$checks = [Collections.Generic.List[object]]::new()
$notes = [Collections.Generic.List[string]]::new()
$watch = [Diagnostics.Stopwatch]::StartNew()
$mainProcess = $null
$knownCompanionIds = [Collections.Generic.HashSet[int]]::new()
$saveLock = $null
$failure = $null
$lastResult = $null
$commandSequence = 0
$mainWindow = [IntPtr]::Zero
$registryPath = 'Software\DefaultCompany\Cat Quest'
$registryBackup = @()
$registryExisted = $false

# Run in a fresh PowerShell process. Native input is scoped to the verified app toast.
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class SettingsSmokeNative {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    public class Window { public long Handle; public uint ProcessId; public string Title, ClassName; public int Left, Top, Right, Bottom; public bool Visible; }
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left,Top,Right,Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X,Y; }
    public delegate bool EnumProc(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback, IntPtr parameter);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr window,StringBuilder text,int count);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr window,StringBuilder text,int count);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window,out Rect rect);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr window,int command);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(Point p);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h,uint flags);
    [StructLayout(LayoutKind.Sequential)] struct MouseInput { public int x,y; public uint data,flags,time; public UIntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] struct Input { public uint type; public MouseInput mouse; }
    [DllImport("user32.dll",SetLastError=true)] static extern uint SendInput(uint count,Input[] inputs,int size);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
    public static void Click(int x,int y) {
        // Send position/down/up together; cursor movement by another input source must
        // not redirect the click between SetCursorPos and separate mouse_event calls.
        int ax=(int)Math.Round((x-GetSystemMetrics(76)+0.5)*65536/GetSystemMetrics(78));
        int ay=(int)Math.Round((y-GetSystemMetrics(77)+0.5)*65536/GetSystemMetrics(79));
        var inputs=new Input[3];
        for(int i=0;i<inputs.Length;i++) inputs[i].mouse=new MouseInput{x=ax,y=ay,flags=0xC001u|(i==1?2u:i==2?4u:0u)};
        if(SendInput((uint)inputs.Length,inputs,Marshal.SizeOf(typeof(Input)))!=inputs.Length)
            throw new InvalidOperationException("Native click injection failed: "+Marshal.GetLastWin32Error());
    }
    public static Window Describe(IntPtr h) {
        uint process; GetWindowThreadProcessId(h,out process); var title=new StringBuilder(512);var cls=new StringBuilder(256);Rect rect;
        GetWindowText(h,title,title.Capacity);GetClassName(h,cls,cls.Capacity);GetWindowRect(h,out rect);
        return new Window{Handle=h.ToInt64(),ProcessId=process,Title=title.ToString(),ClassName=cls.ToString(),Left=rect.Left,Top=rect.Top,Right=rect.Right,Bottom=rect.Bottom,Visible=IsWindowVisible(h)};
    }
    public static Window[] Windows() {
        var result=new List<Window>();
        EnumWindows((h,p)=> { uint process; GetWindowThreadProcessId(h,out process); var title=new StringBuilder(512);var cls=new StringBuilder(256);Rect rect;
            GetWindowText(h,title,title.Capacity);GetClassName(h,cls,cls.Capacity);GetWindowRect(h,out rect);
            result.Add(new Window{Handle=h.ToInt64(),ProcessId=process,Title=title.ToString(),ClassName=cls.ToString(),Left=rect.Left,Top=rect.Top,Right=rect.Right,Bottom=rect.Bottom,Visible=IsWindowVisible(h)});return true;
        },IntPtr.Zero);return result.ToArray();
    }
}
'@
$previousForeground = [SettingsSmokeNative]::GetForegroundWindow()
[SettingsSmokeNative]::SetProcessDPIAware() | Out-Null
$previousPointer = [SettingsSmokeNative+Point]::new()
[SettingsSmokeNative]::GetCursorPos([ref]$previousPointer) | Out-Null

function Write-JsonFile([string]$Path, $Value) { [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 20), $utf8) }
function Read-JsonFile([string]$Path) {
    if (-not [IO.File]::Exists($Path)) { return $null }
    try {
        $stream = [IO.FileStream]::new($Path,[IO.FileMode]::Open,[IO.FileAccess]::Read,([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
        # Release the frequently replaced receipt before PowerShell parses it.
        # Keeping a deleted file handle open across multiple replacements can
        # prevent Mono's next File.Replace even with FileShare.Delete enabled.
        try { $reader = [IO.StreamReader]::new($stream); try { $json = $reader.ReadToEnd() } finally { $reader.Dispose() } } finally { $stream.Dispose() }
        return ($json | ConvertFrom-Json)
    } catch [IO.IOException] { return $null } catch [ArgumentException] { return $null }
}
function Progress([string]$Stage) { Write-JsonFile (Join-Path $runDirectory 'progress.json') @{ stage=$Stage; utc=[DateTime]::UtcNow.ToString('o'); elapsedSeconds=[Math]::Round($watch.Elapsed.TotalSeconds,1) } }
function Check([bool]$Condition,[string]$Name,$Evidence=$null) {
    $checks.Add([ordered]@{ name=$Name; passed=$Condition; utc=[DateTime]::UtcNow.ToString('o'); evidence=$Evidence })
    if (-not $Condition) { throw $Name }
}
function Assert-Alive {
    if ($watch.Elapsed.TotalSeconds -gt $MaximumSeconds) { throw "Smoke deadline exceeded ($MaximumSeconds seconds)." }
    if ($mainProcess -and $mainProcess.HasExited) { throw "Main player exited unexpectedly: $($mainProcess.ExitCode)" }
    $errorPath = Join-Path $runDirectory 'smoke-error.txt'
    if (Test-Path -LiteralPath $errorPath) { throw ('Player smoke harness error: ' + [IO.File]::ReadAllText($errorPath)) }
}
function Wait-Result([scriptblock]$Predicate,[string]$Name,[int]$TimeoutSeconds=12,[DateTime]$After=[DateTime]::MinValue) {
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        Assert-Alive
        $path = Join-Path $runDirectory 'smoke-result.json'
        $value = Read-JsonFile $path
        if ($value -and [IO.File]::GetLastWriteTimeUtc($path) -gt $After -and (& $Predicate $value)) { $script:lastResult=$value; return $value }
        Start-Sleep -Milliseconds 150
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Timed out: $Name"
}
function Send-Command([hashtable]$Command,[scriptblock]$Predicate={param($r) $true},[string]$Name='command',[int]$TimeoutSeconds=12) {
    Progress $Name
    $path = Join-Path $runDirectory 'smoke-command.json'
    if (Test-Path -LiteralPath $path) { throw 'A prior smoke command was not consumed.' }
    $sent=[DateTime]::UtcNow
    $temp=Join-Path $runDirectory ('command-' + [Guid]::NewGuid().ToString('N') + '.tmp')
    Write-JsonFile $temp $Command
    [IO.File]::Move($temp,$path)
    $consume=[DateTime]::UtcNow.AddSeconds(8)
    while(Test-Path -LiteralPath $path) { Assert-Alive; if([DateTime]::UtcNow -gt $consume){throw "Command not consumed: $Name"};Start-Sleep -Milliseconds 100 }
    $r=Wait-Result $Predicate $Name $TimeoutSeconds $sent
    $script:commandSequence++
    Write-JsonFile (Join-Path $runDirectory ('{0:D2}-{1}.json' -f $commandSequence,($Name -replace '[^a-zA-Z0-9-]','-'))) $r
    return $r
}
function Stable-Count([int]$Expected,[string]$Name) {
    $until=[DateTime]::UtcNow.AddSeconds(1.1)
    do { $r=Wait-Result {param($r) $true} $Name; Check ($r.notificationAttempts -eq $Expected) $Name $r.notificationAttempts; Start-Sleep -Milliseconds 250 } while([DateTime]::UtcNow -lt $until)
    return $r
}
function Capture-Region([string]$Name,[int]$X,[int]$Y,[int]$Width,[int]$Height) {
    if($Width -lt 1 -or $Height -lt 1){return}
    $bitmap=[Drawing.Bitmap]::new($Width,$Height)
    try { $graphics=[Drawing.Graphics]::FromImage($bitmap);try {$graphics.CopyFromScreen($X,$Y,0,0,$bitmap.Size)}finally{$graphics.Dispose()};$bitmap.Save((Join-Path $runDirectory $Name),[Drawing.Imaging.ImageFormat]::Png) } finally {$bitmap.Dispose()}
}
function Capture-ToastCorner([string]$Name) {
    $area=[Windows.Forms.Screen]::PrimaryScreen.WorkingArea
    $w=[Math]::Min(660,$area.Width);$h=[Math]::Min(430,$area.Height)
    Capture-Region $Name ($area.Right-$w) ($area.Bottom-$h) $w $h
}
function Try-Banner([string]$Title,[string]$CaptureName,[bool]$Click) {
    $deadline=[DateTime]::UtcNow.AddSeconds(8)
    $found=$null; $top=$null; $point=[SettingsSmokeNative+Point]::new()
    do {
        Assert-Alive
        # Windows 11 can omit the toast host from both EnumWindows and UIA desktop
        # children. Hit-test the notification area without clicking, then inspect
        # only shell-owned hosts for this executable and the exact reminder title.
        $shellIds=@(Get-Process -Name ShellExperienceHost,ShellHost,explorer -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id)
        $hosts=[Collections.Generic.HashSet[long]]::new()
        foreach($screen in [Windows.Forms.Screen]::AllScreens){
            foreach($insetX in @(80,220,400,600)){
                foreach($insetY in @(80,180,300,450,600)){
                    $probe=[SettingsSmokeNative+Point]::new()
                    $probe.X=$screen.WorkingArea.Right-$insetX; $probe.Y=$screen.WorkingArea.Bottom-$insetY
                    $hostWindow=[SettingsSmokeNative]::GetAncestor([SettingsSmokeNative]::WindowFromPoint($probe),2)
                    if($hostWindow -ne [IntPtr]::Zero){[void]$hosts.Add($hostWindow.ToInt64())}
                }
            }
        }
        foreach($handle in $hosts) {
            try {
                $native=[SettingsSmokeNative]::Describe([IntPtr]$handle)
                if($native.ProcessId -notin $shellIds -or -not $native.Visible){continue}
                $element=[Windows.Automation.AutomationElement]::FromHandle([IntPtr]$handle)
                if($element.Current.ProcessId -notin $shellIds -or $element.Current.IsOffscreen){continue}
                $senderCondition=[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty,'SenderName')
                $sender=$element.FindFirst([Windows.Automation.TreeScope]::Descendants,$senderCondition)
                if(-not $sender -or $sender.Current.Name -ne [IO.Path]::GetFileName($exePath)){continue}
                $condition=[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,$Title)
                $candidate=$element.FindFirst([Windows.Automation.TreeScope]::Descendants,$condition)
                if(-not $candidate -or $candidate.Current.IsOffscreen){continue}
                $rect=$candidate.Current.BoundingRectangle
                if($rect.Width -lt 1 -or $rect.Height -lt 1){continue}
                $point.X=[int]($rect.X+$rect.Width/2); $point.Y=[int]($rect.Y+$rect.Height/2)
                $hostWindow=[IntPtr]$element.Current.NativeWindowHandle
                $hit=[SettingsSmokeNative]::GetAncestor([SettingsSmokeNative]::WindowFromPoint($point),2)
                if($hostWindow -eq [IntPtr]::Zero -or $hit -ne $hostWindow){continue}
                # Wait for the shell's slide-in animation to settle before clicking.
                Start-Sleep -Milliseconds 200
                $settled=$candidate.Current.BoundingRectangle
                if([Math]::Abs($settled.X-$rect.X) -gt 1 -or [Math]::Abs($settled.Y-$rect.Y) -gt 1){continue}
                if([SettingsSmokeNative]::GetAncestor([SettingsSmokeNative]::WindowFromPoint($point),2) -ne $hostWindow){continue}
                $found=$candidate; $top=[SettingsSmokeNative]::Describe($hostWindow); break
            } catch [Windows.Automation.ElementNotAvailableException] { }
        }
        if(-not $found){Start-Sleep -Milliseconds 100}
    } while(-not $found -and [DateTime]::UtcNow -lt $deadline)
    if(-not $found){
        Capture-ToastCorner $CaptureName
        if($Click){throw "Could not identify a clickable Cat Quest '$Title' banner; no other window was clicked."}
        $notes.Add("Actual '$Title' banner could not be identified; visual verification is incomplete."); return $false
    }
    Write-JsonFile (Join-Path $runDirectory ($CaptureName+'.uia.json')) @{title=$Title;window=$top;bounds=$found.Current.BoundingRectangle.ToString();click=$point}
    Capture-Region $CaptureName $top.Left $top.Top ($top.Right-$top.Left) ($top.Bottom-$top.Top)
    if($Click){
        Check ([SettingsSmokeNative]::IsIconic($mainWindow)) "Main window is minimized before '$Title' click"
        $before=(Wait-Result {param($r)$true} 'before-banner-click').notificationActivations
        [SettingsSmokeNative]::Click($point.X,$point.Y)
        $after=Wait-Result {param($r)$r.notificationActivations -eq $before+1} 'actual-banner-click' 8
        $restoreDeadline=[DateTime]::UtcNow.AddSeconds(3)
        while(([SettingsSmokeNative]::IsIconic($mainWindow) -or [SettingsSmokeNative]::GetForegroundWindow() -ne $mainWindow) -and [DateTime]::UtcNow -lt $restoreDeadline){Start-Sleep -Milliseconds 100}
        Check (-not [SettingsSmokeNative]::IsIconic($mainWindow)) "'$Title' click restores minimized main window" $after.notificationActivations
        Check ([SettingsSmokeNative]::GetForegroundWindow() -eq $mainWindow) "'$Title' click foregrounds main window" ([SettingsSmokeNative]::GetForegroundWindow().ToInt64())
        # Allow the toast to dismiss, then capture only the app window (not the desktop).
        Start-Sleep -Milliseconds 400
        $restored=[SettingsSmokeNative]::Describe($mainWindow)
        Capture-Region ($CaptureName -replace '.png$','-restored.png') $restored.Left $restored.Top ($restored.Right-$restored.Left) ($restored.Bottom-$restored.Top)
        Write-JsonFile (Join-Path $runDirectory ($CaptureName+'.restored.json')) @{window=$restored;state=$after;foreground=[SettingsSmokeNative]::GetForegroundWindow().ToInt64()}
    }
    return $true
}
function Notification-Windows { return @([SettingsSmokeNative]::Windows() | Where-Object {$_.ClassName -like 'CatQuest.FocusNotifications.*'}) }
function Discover-Companions {
    $children=@(Get-CimInstance Win32_Process | Where-Object { $_.ParentProcessId -eq $mainProcess.Id -and $_.CommandLine -like '*--companion*' -and $_.ExecutablePath -eq $exePath })
    foreach($child in $children){[void]$knownCompanionIds.Add([int]$child.ProcessId)}
    return $children
}

try {
    $registry=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($registryPath)
    if($registry){$registryExisted=$true;try{foreach($name in $registry.GetValueNames()){$registryBackup+=@{name=$name;kind=$registry.GetValueKind($name).ToString();value=$registry.GetValue($name,$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)}}}finally{$registry.Dispose()}}
    Write-JsonFile (Join-Path $runDirectory 'playerprefs-before.json') @{existed=$registryExisted;values=$registryBackup}
    Write-JsonFile (Join-Path $runDirectory 'context.json') @{utc=[DateTime]::UtcNow.ToString('o');executable=$exePath;sha256=(Get-FileHash -LiteralPath $exePath -Algorithm SHA256).Hash;acceleratedSeconds='FocusBuildSmoke calls FocusController.Advance(1), never an invalid >10s gap';liveSave='never used';output=$runDirectory}
    Write-JsonFile (Join-Path $runDirectory 'test-save.json') @{version=5;username='Notification Test';settings=@{notificationsEnabled=$true;soundVolume=0;musicVolume=0}}
    Progress 'launch-isolated-player'
    $arguments='--focus-smoke --smoke-dir "'+$runDirectory+'" -logFile "'+(Join-Path $runDirectory 'Player.log')+'" -screen-fullscreen 0 -screen-width 1280 -screen-height 720'
    $mainProcess=Start-Process -FilePath $exePath -ArgumentList $arguments -WorkingDirectory ([IO.Path]::GetDirectoryName($exePath)) -WindowStyle Normal -PassThru
    $r=Wait-Result {param($r)$r.controlsBound -and $r.phase -eq 'Ready'} 'isolated-startup' 35
    Check ($r.width -eq 1280 -and $r.height -eq 720) 'Windows player starts at 1280x720' @{width=$r.width;height=$r.height}
    $window=@([SettingsSmokeNative]::Windows()|Where-Object {$_.ProcessId -eq $mainProcess.Id -and $_.ClassName -eq 'UnityWndClass'}|Sort-Object {($_.Right-$_.Left)*($_.Bottom-$_.Top)} -Descending)[0]
    if(-not $window){throw 'Main player window not found.'};$mainWindow=[IntPtr]$window.Handle
    # Keep the visible startup presentation sequence; minimize/restore is tested below.
    $r=Send-Command @{action='close-widget'} {param($r)-not $r.widgetOpen} 'initial-widget-closed'
    $r=Send-Command @{action='notifications';enabled=$true} {param($r)$r.notificationsEnabled -and $r.notificationsRegistered} 'notifications-on'
    Check ($r.notificationAttempts -eq 0) 'Fresh isolated run has no reminder attempts'
    $r=Send-Command @{action='configure';focusMinutes=1;restMinutes=1} {param($r)$r.focusMinutes -eq 1 -and $r.restMinutes -eq 1} 'minimum-one-minute-durations'
    Check ($r.focusMinutes -eq 1 -and $r.restMinutes -eq 1) 'Actual Focus/Rest minimum is one minute'
    $r=Send-Command @{action='start'} {param($r)$r.phase -eq 'Focusing'} 'normal-focus-start'
    [SettingsSmokeNative]::ShowWindow($mainWindow,6)|Out-Null
    $r=Send-Command @{action='advance';seconds=60} {param($r)$r.phase -eq 'Resting' -and $r.notificationAttempts -eq 1} 'normal-focus-completed-minimized'
    Check ($r.notificationsAccepted -eq 1 -and $r.notificationKind -eq 'FocusCompleted') 'Normal committed Focus submits exactly one native reminder' $r
    $shown=Wait-Result {param($r)$r.notificationsShown -ge 1} 'shell-balloon-show-callback' 8
    Check ($shown.notificationsShown -ge 1) 'Windows reports the Focus banner shown' $shown.notificationsShown
    $visible=Try-Banner 'Focus complete' '01-focus-banner.png' $AttemptBannerClick.IsPresent
    $r=Send-Command @{action='retry'} {param($r)$r.phase -eq 'Resting'} 'retry-after-settlement'
    $r=Stable-Count 1 'Retry after successful settlement does not duplicate notification'
    [SettingsSmokeNative]::ShowWindow($mainWindow,6)|Out-Null
    $r=Send-Command @{action='advance';seconds=60} {param($r)$r.phase -eq 'Finished' -and $r.notificationAttempts -eq 2} 'normal-rest-finished'
    Check ($r.notificationKind -eq 'RestCompleted') 'Natural Rest completion submits one reminder' $r
    $restVisible=Try-Banner 'Rest complete' '02-rest-banner.png' $AttemptBannerClick.IsPresent
    $visible=$visible -and $restVisible

    $r=Send-Command @{action='start'} {param($r)$r.phase -eq 'Focusing'} 'early-focus-start'
    $r=Send-Command @{action='advance';seconds=10} {param($r)$r.phase -eq 'Focusing'} 'early-focus-ten-seconds'
    $r=Send-Command @{action='end'} {param($r)$r.phase -eq 'Resting'} 'manual-end-early'
    $r=Stable-Count 2 'Manual End Early emits no immediate notification'
    $r=Send-Command @{action='advance';seconds=60} {param($r)$r.phase -eq 'Finished' -and $r.notificationAttempts -eq 3} 'natural-rest-after-early'
    Check ($r.notificationKind -eq 'RestCompleted') 'Natural Rest after End Early still notifies'
    $r=Send-Command @{action='start'} {param($r)$r.phase -eq 'Focusing'} 'manual-rest-session-start'
    $r=Send-Command @{action='end'} {param($r)$r.phase -eq 'Resting'} 'manual-rest-session-early'
    $r=Send-Command @{action='rest'} {param($r)$r.phase -eq 'Finished'} 'manual-end-rest'
    $r=Stable-Count 3 'Manual End Rest emits no notification'

    $r=Send-Command @{action='notifications';enabled=$false} {param($r)-not $r.notificationsEnabled -and -not $r.notificationsRegistered} 'notifications-off'
    $r=Send-Command @{action='start'} {param($r)$r.phase -eq 'Focusing'} 'disabled-session-start'
    $r=Send-Command @{action='advance';seconds=120} {param($r)$r.phase -eq 'Finished'} 'disabled-focus-and-rest-complete'
    $r=Stable-Count 3 'Disabled Focus and Rest do not notify'
    $r=Send-Command @{action='notifications';enabled=$true} {param($r)$r.notificationsRegistered} 'notifications-reenabled'
    $r=Stable-Count 3 'Re-enabling notifications does not backfill old events'

    $r=Send-Command @{action='start'} {param($r)$r.phase -eq 'Focusing'} 'save-retry-session-start'
    $saveLock=[IO.File]::Open((Join-Path $runDirectory 'test-save.json.tmp'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
    $r=Send-Command @{action='advance';seconds=60} {param($r)$r.phase -eq 'Saving'} 'failed-settlement-no-notification'
    Check ($r.notificationAttempts -eq 3) 'Failed settlement does not notify'
    $saveLock.Dispose();$saveLock=$null
    $r=Send-Command @{action='retry'} {param($r)$r.phase -eq 'Resting' -and $r.notificationAttempts -eq 4} 'successful-save-retry'
    $r=Send-Command @{action='retry'} {param($r)$r.phase -eq 'Resting'} 'duplicate-save-retry'
    $r=Stable-Count 4 'Successful retry notifies once; repeated retry is silent'
    $r=Send-Command @{action='rest'} {param($r)$r.phase -eq 'Finished'} 'retry-session-manual-rest'

    $r=Send-Command @{action='widget'} {param($r)$r.widgetOpen} 'launch-real-companion' 20
    $companionDirectory=$r.companionDirectory
    $deadline=[DateTime]::UtcNow.AddSeconds(15);$view=$null
    do {Assert-Alive;$view=Read-JsonFile (Join-Path $companionDirectory 'companion-window.json');if($view -and $view.connected){break};Start-Sleep -Milliseconds 200}while([DateTime]::UtcNow -lt $deadline)
    Check ($view -and $view.connected -and $view.noPlayerStore) 'Real Companion is connected and owns no player store' $view
    $children=@(Discover-Companions);Check ($children.Count -eq 1) 'Exactly one Companion process launched' $children.ProcessId
    $windows=@(Notification-Windows);Write-JsonFile (Join-Path $runDirectory 'notification-windows-with-companion.json') $windows
    Check (@($windows|Where-Object{$_.ProcessId -eq $mainProcess.Id}).Count -eq 1) 'Main process owns exactly one notification window'
    Check (@($windows|Where-Object{$knownCompanionIds.Contains([int]$_.ProcessId)}).Count -eq 0) 'Companion creates no second notification sender'
    $r=Send-Command @{action='start'} {param($r)$r.phase -eq 'Focusing'} 'companion-visible-focus-start'
    $r=Send-Command @{action='advance';seconds=60} {param($r)$r.phase -eq 'Resting' -and $r.notificationAttempts -eq 5} 'companion-visible-normal-completion'
    $r=Stable-Count 5 'Companion display does not duplicate main notification'
    $r=Send-Command @{action='rest'} {param($r)$r.phase -eq 'Finished'} 'companion-session-manual-rest'
    $r=Send-Command @{action='close-widget'} {param($r)-not $r.widgetOpen} 'close-companion' 15
    Write-JsonFile (Join-Path $runDirectory 'final-player-state.json') $r
    if(-not $visible){$notes.Add('Behavior counters passed, but actual Windows banner visual/click review remains incomplete.')}
    if(-not $AttemptBannerClick){$notes.Add('Actual banner click was intentionally not attempted; rerun with -AttemptBannerClick for that interaction.')}
} catch {
    $failure=$_.Exception.ToString()
    $notes.Add($failure)
} finally {
    if($saveLock){$saveLock.Dispose()}
    if($mainProcess -and -not $mainProcess.HasExited){
        try {
            [void](Discover-Companions)
            $command=Join-Path $runDirectory 'smoke-command.json'
            if(-not (Test-Path -LiteralPath $command)){Write-JsonFile $command @{action='quit'}}
            if(-not $mainProcess.WaitForExit(8000)){$mainProcess.Kill();$mainProcess.WaitForExit(5000)|Out-Null;$notes.Add('Main player needed bounded forced cleanup after quit.')}
        } catch {$notes.Add('Main cleanup: '+$_.Exception.Message)}
    }
    foreach($childId in $knownCompanionIds){
        $child=Get-Process -Id $childId -ErrorAction SilentlyContinue
        if($child){try{if(-not $child.WaitForExit(5000)){$child.Kill();$child.WaitForExit(5000)|Out-Null;$notes.Add("Companion $childId needed bounded forced cleanup.")}}catch{$notes.Add('Companion cleanup: '+$_.Exception.Message)}}
    }
    try {
        $key=[Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($registryPath)
        try {
            foreach($name in $key.GetValueNames()){if($name -notin $registryBackup.name){$key.DeleteValue($name,$false)}}
            foreach($saved in $registryBackup){$kind=[Microsoft.Win32.RegistryValueKind]::$($saved.kind);$value=$saved.value;if($kind -eq 'Binary'){$value=[byte[]]$value};if($kind -eq 'MultiString'){$value=[string[]]$value};$key.SetValue($saved.name,$value,$kind)}
        } finally {$key.Dispose()}
        if(-not $registryExisted){[Microsoft.Win32.Registry]::CurrentUser.DeleteSubKey($registryPath,$false)}
    } catch {$notes.Add('PlayerPrefs restore failed: '+$_.Exception.Message);if(-not $failure){$failure='PlayerPrefs restore failed'}}
    [SettingsSmokeNative]::SetCursorPos($previousPointer.X,$previousPointer.Y)|Out-Null
    if([SettingsSmokeNative]::IsWindow($previousForeground)){[SettingsSmokeNative]::SetForegroundWindow($previousForeground)|Out-Null}
    $ownedWindows=@(Notification-Windows|Where-Object{$_.ProcessId -eq $mainProcess.Id -or $knownCompanionIds.Contains([int]$_.ProcessId)})
    $cleanupOk=$ownedWindows.Count -eq 0
    $checks.Add(@{name='No test notification windows remain after cleanup';passed=$cleanupOk;evidence=$ownedWindows})
    if(-not $cleanupOk -and -not $failure){$failure='Native notification window leaked after process cleanup'}
    $report=[ordered]@{utc=[DateTime]::UtcNow.ToString('o');elapsedSeconds=[Math]::Round($watch.Elapsed.TotalSeconds,1);passed=($null -eq $failure);failure=$failure;checks=$checks;notes=$notes;lastState=$lastResult;output=$runDirectory;liveSaveUsed=$false}
    Write-JsonFile (Join-Path $runDirectory 'result.json') $report
    Progress $(if($failure){'failed'}else{'complete'})
}
Write-Output (Join-Path $runDirectory 'result.json')
if($failure){exit 1}
