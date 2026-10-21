using System;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
using System.Diagnostics;
using System.Runtime.InteropServices;
#endif

// A small Win32 adapter for running-app reminders. It never opens another process.
public sealed class WindowsFocusNotificationSink : IFocusNotificationSink, IDisposable
{
    public string LastError { get; private set; }
    public int Shown { get; private set; }
    public int Activations { get; private set; }
    public bool Registered { get; private set; }
    bool enabled, disposed;

    public void SetEnabled(bool value)
    {
        if (disposed || enabled == value) return;
        enabled = value;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        if (enabled && !CompanionProtocol.HasArgument("--companion"))
        {
            try { if (Initialize()) AddIcon(); }
            catch (Exception) { LastError = "Windows notifications are unavailable."; ReleaseNative(); }
        }
        else ReleaseNative();
#endif
    }

    public bool TryShow(FocusNotificationKind kind)
    {
        if (disposed || !enabled) return false;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        if (CompanionProtocol.HasArgument("--companion") || window == IntPtr.Zero) return false;
        if (!Registered && !AddIcon()) return false;
        var data = IconData();
        data.flags = NifInfo | NifRealtime;
        data.infoFlags = NiifInfo | NiifRespectQuietTime;
        data.infoTitle = kind == FocusNotificationKind.FocusCompleted ? "Focus complete" : "Rest complete";
        data.info = kind == FocusNotificationKind.FocusCompleted
            ? "Your Focus session is saved. Time for a break."
            : "Your break is over. Ready for another Focus?";
        bool accepted = Shell_NotifyIconW(NimModify, ref data);
        LastError = accepted ? null : "Windows could not display the reminder.";
        return accepted;
#else
        return false;
#endif
    }

    public void Tick()
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        if (disposed || window == IntPtr.Zero) return;
        // Unity normally pumps these messages; also drain this window only, with a bound.
        for (int i = 0; i < 32 && PeekMessageW(out var message, window, 0, 0, 1); i++)
            DispatchMessageW(ref message);
        if (recreateIcon)
        {
            recreateIcon = false; Registered = false;
            if (enabled) AddIcon();
        }
        if (restoreRequested)
        {
            restoreRequested = false;
            var target = FindMainWindow();
            if (target != IntPtr.Zero)
            {
                if (IsIconic(target)) ShowWindow(target, 9);
                SetForegroundWindow(target);
            }
        }
#endif
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true; enabled = false;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        ReleaseNative();
#endif
    }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
    const uint NimAdd = 0, NimModify = 1, NimDelete = 2, NimSetVersion = 4;
    const uint NifMessage = 1, NifIcon = 2, NifTip = 4, NifInfo = 0x10, NifRealtime = 0x40, NifShowTip = 0x80;
    const uint NiifInfo = 1, NiifRespectQuietTime = 0x80, CallbackMessage = 0x8000 + 71;
    const int NinSelect = 0x400, NinKeySelect = 0x401, NinBalloonShow = 0x402, NinBalloonUserClick = 0x405;
    static WindowsFocusNotificationSink owner;
    static readonly WindowProcedure Procedure = WindowMessage;
    IntPtr window, instance, icon, mainWindow;
    string className;
    ushort classAtom;
    uint taskbarCreated;
    bool ownsIcon, recreateIcon, restoreRequested;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct NotifyIconData
    {
        public uint size;
        public IntPtr window;
        public uint id, flags, callback;
        public IntPtr icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string tip;
        public uint state, stateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string info;
        public uint version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string infoTitle;
        public uint infoFlags;
        public Guid guid;
        public IntPtr balloonIcon;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WindowClass
    {
        public uint size, style;
        public IntPtr procedure;
        public int classExtra, windowExtra;
        public IntPtr instance, icon, cursor, background;
        public string menuName, className;
        public IntPtr smallIcon;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct NativeMessage
    {
        public IntPtr window;
        public uint message;
        public UIntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int x, y;
        public uint privateData;
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    delegate IntPtr WindowProcedure(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam);
    delegate bool EnumWindowProcedure(IntPtr window, IntPtr state);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern bool Shell_NotifyIconW(uint message, ref NotifyIconData data);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern uint ExtractIconExW(string file, int index, out IntPtr large, out IntPtr small, uint count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern ushort RegisterClassExW(ref WindowClass windowClass);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool UnregisterClassW(string className, IntPtr instance);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateWindowExW(uint extendedStyle, string className, string title, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr DefWindowProcW(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool PeekMessageW(out NativeMessage message, IntPtr window, uint min, uint max, uint remove);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr DispatchMessageW(ref NativeMessage message);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessageW(string message);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr LoadIconW(IntPtr instance, IntPtr name);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr window);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowProcedure callback, IntPtr state);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandleW(string name);

    bool Initialize()
    {
        if (window != IntPtr.Zero) return true;
        if (owner != null && owner != this) { LastError = "A notification window already exists."; return false; }
        owner = this;
        using (var process = Process.GetCurrentProcess())
        {
            className = "CatQuest.FocusNotifications." + process.Id;
            ExtractIconExW(process.MainModule.FileName, 0, out var large, out var small, 1);
            icon = small != IntPtr.Zero ? small : large;
            ownsIcon = icon != IntPtr.Zero;
            if (large != IntPtr.Zero && large != icon) DestroyIcon(large);
        }
        if (icon == IntPtr.Zero) icon = LoadIconW(IntPtr.Zero, new IntPtr(32512));
        instance = GetModuleHandleW(null);
        var definition = new WindowClass
        {
            size = (uint)Marshal.SizeOf<WindowClass>(), instance = instance,
            className = className, procedure = Marshal.GetFunctionPointerForDelegate(Procedure)
        };
        classAtom = RegisterClassExW(ref definition);
        if (classAtom != 0)
            window = CreateWindowExW(0x80, className, "Cat Quest notifications", 0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
        if (window == IntPtr.Zero)
        {
            LastError = "Windows could not create the notification window.";
            ReleaseNative(); return false;
        }
        taskbarCreated = RegisterWindowMessageW("TaskbarCreated");
        return true;
    }

    NotifyIconData IconData() => new NotifyIconData
    {
        size = (uint)Marshal.SizeOf<NotifyIconData>(), window = window, id = 1,
        icon = icon, callback = CallbackMessage, tip = "Cat Quest", info = "", infoTitle = ""
    };
    bool AddIcon()
    {
        if (window == IntPtr.Zero || !enabled) return false;
        var data = IconData(); data.flags = NifMessage | NifIcon | NifTip | NifShowTip;
        Registered = Shell_NotifyIconW(NimAdd, ref data);
        if (!Registered) { LastError = "Windows could not register the reminder icon."; return false; }
        data.version = 4;
        if (!Shell_NotifyIconW(NimSetVersion, ref data))
        {
            LastError = "Windows could not configure reminder interaction.";
            Shell_NotifyIconW(NimDelete, ref data); Registered = false; return false;
        }
        LastError = null; return true;
    }

    [AOT.MonoPInvokeCallback(typeof(WindowProcedure))]
    static IntPtr WindowMessage(IntPtr target, uint message, UIntPtr wParam, IntPtr lParam)
    {
        // No Unity work or managed exceptions may cross the native callback boundary.
        var active = owner;
        if (active != null && !active.disposed)
        {
            if (message == active.taskbarCreated && active.taskbarCreated != 0)
            {
                active.recreateIcon = true; return IntPtr.Zero;
            }
            if (message == CallbackMessage)
            {
                int notification = (int)(lParam.ToInt64() & 0xffff);
                if (notification == NinBalloonShow) active.Shown++;
                else if (notification == NinSelect || notification == NinKeySelect || notification == NinBalloonUserClick)
                {
                    active.restoreRequested = true; active.Activations++;
                }
                return IntPtr.Zero;
            }
        }
        return DefWindowProcW(target, message, wParam, lParam);
    }

    IntPtr FindMainWindow()
    {
        uint processId;
        using (var process = Process.GetCurrentProcess()) processId = (uint)process.Id;
        if (mainWindow != IntPtr.Zero && IsWindow(mainWindow))
        {
            GetWindowThreadProcessId(mainWindow, out uint ownerId);
            if (ownerId == processId && IsWindowVisible(mainWindow)) return mainWindow;
        }
        mainWindow = IntPtr.Zero;
        EnumWindows((candidate, state) =>
        {
            GetWindowThreadProcessId(candidate, out uint candidateProcess);
            if (candidateProcess != processId || candidate == window || !IsWindowVisible(candidate)) return true;
            mainWindow = candidate; return false;
        }, IntPtr.Zero);
        return mainWindow;
    }

    void ReleaseNative()
    {
        if (Registered && window != IntPtr.Zero)
        {
            var data = IconData(); Shell_NotifyIconW(NimDelete, ref data);
        }
        Registered = false;
        if (window != IntPtr.Zero) { DestroyWindow(window); window = IntPtr.Zero; }
        if (classAtom != 0) { UnregisterClassW(className, instance); classAtom = 0; }
        if (ownsIcon && icon != IntPtr.Zero) DestroyIcon(icon);
        icon = IntPtr.Zero; ownsIcon = false; mainWindow = IntPtr.Zero;
        recreateIcon = restoreRequested = false;
        if (owner == this) owner = null;
    }
#endif
}
