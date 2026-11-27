using UnityEngine;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
using System;
using System.Runtime.InteropServices;
using System.Text;
#endif

// Read-only visibility and client-area pointer coordinates for this player.
// Focus and IPC keep running while preview cameras are suspended.
public static class WindowsAppWindow
{
    public static bool IsMinimized
    {
        get
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (checkedFrame == Time.frameCount) return minimized;
            checkedFrame = Time.frameCount;
            if (window != IntPtr.Zero)
            {
                GetWindowThreadProcessId(window, out uint owner);
                if (!IsWindow(window) || owner != GetCurrentProcessId()) window = IntPtr.Zero;
            }
            if (window == IntPtr.Zero && Time.realtimeSinceStartupAsDouble >= nextSearch)
            {
                nextSearch = Time.realtimeSinceStartupAsDouble + 1;
                EnumWindows(FindOwnWindow, IntPtr.Zero);
            }
            // Missing or recreated windows fail open, so visible UI never stays blank.
            minimized = window != IntPtr.Zero && IsIconic(window);
            return minimized;
#else
            return false;
#endif
        }
    }

    public static bool TryGetClientPointer(out Vector2 position, out Vector2 size)
    {
        position = size = Vector2.zero;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        if (IsMinimized || window == IntPtr.Zero || !GetCursorPos(out var point)
            || !ScreenToClient(window, ref point) || !GetClientRect(window, out var rect)) return false;
        position = new Vector2(point.x, rect.bottom - 1 - point.y);
        size = new Vector2(rect.right, rect.bottom);
        return true;
#else
        return false;
#endif
    }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
    [StructLayout(LayoutKind.Sequential)] struct Point { public int x, y; }
    [StructLayout(LayoutKind.Sequential)] struct Rect { public int left, top, right, bottom; }
    delegate bool WindowVisitor(IntPtr handle, IntPtr state);
    static readonly WindowVisitor FindOwnWindow = VisitWindow;
    static IntPtr window;
    static int checkedFrame = -1;
    static double nextSearch;
    static bool minimized;
    static bool VisitWindow(IntPtr candidate, IntPtr state)
    {
        GetWindowThreadProcessId(candidate, out uint owner);
        if (owner != GetCurrentProcessId()) return true;
        var name = new StringBuilder(64);
        GetClassName(candidate, name, name.Capacity);
        if (name.ToString() != "UnityWndClass") return true;
        window = candidate;
        return false;
    }
    [DllImport("user32.dll")] static extern bool EnumWindows(WindowVisitor callback, IntPtr state);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr handle, out uint process);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr handle, StringBuilder name, int capacity);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr handle);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr handle);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] static extern bool ScreenToClient(IntPtr handle, ref Point point);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr handle, out Rect rect);
    [DllImport("kernel32.dll")] static extern uint GetCurrentProcessId();
#endif
}
