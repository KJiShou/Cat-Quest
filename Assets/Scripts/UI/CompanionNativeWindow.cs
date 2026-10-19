using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.EventSystems;

// All native operations are confined to the companion's own process window.
public static class CompanionNativeWindow
{
    public static bool IsPinned { get; private set; } = true;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
    static bool minimizedToTaskbar;
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window,int command);
    delegate bool EnumProc(IntPtr window, IntPtr data);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback,IntPtr data);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr window,int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr window,int index,int value);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [StructLayout(LayoutKind.Sequential)] struct Rect { public int left,top,right,bottom; }
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr window,out Rect rect);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window,out Rect rect);
    [StructLayout(LayoutKind.Sequential)] struct Point { public int x,y; }
    [DllImport("user32.dll")] static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] static extern IntPtr SetCapture(IntPtr window);
    static Rect resizeRect; static Point resizeStart; static int activeEdge;
    [DllImport("user32.dll")] static extern IntPtr LoadCursor(IntPtr instance,IntPtr name);
    [DllImport("user32.dll")] static extern IntPtr SetCursor(IntPtr cursor);
    [StructLayout(LayoutKind.Sequential)] struct MonitorInfo { public int size;public Rect monitor,work;public uint flags; }
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr window,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Auto)] static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window,uint message,IntPtr wParam,IntPtr lParam);
    [DllImport("gdi32.dll")] static extern IntPtr CreateRoundRectRgn(int left,int top,int right,int bottom,int ellipseWidth,int ellipseHeight);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("user32.dll")] static extern int SetWindowRgn(IntPtr window,IntPtr region,bool redraw);
    static int roundedWidth,roundedHeight;
    static IntPtr handle;
    static IntPtr Handle
    {
        get {
            if(handle!=IntPtr.Zero) return handle;
            uint own=(uint)System.Diagnostics.Process.GetCurrentProcess().Id;
            EnumWindows((w,d)=> { GetWindowThreadProcessId(w,out uint id); if(id!=own || !IsWindowVisible(w)) return true; handle=w; return false; },IntPtr.Zero);
            return handle;
        }
    }
#endif
    public static bool Configure()
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        var window=Handle; if(window==IntPtr.Zero) return false;
        int style=GetWindowLong(window,-16);
        SetWindowLong(window,-16,(style & ~0x00C40000)); // Remove caption and sizing frame; custom handles below.
        SetWindowLong(window,-20,(GetWindowLong(window,-20)|0x80) & ~0x40000); // Tool window, no taskbar entry.
        var monitor=new MonitorInfo{size=Marshal.SizeOf<MonitorInfo>()};
        GetMonitorInfo(MonitorFromWindow(window,2),ref monitor);
        SetWindowPos(window,new IntPtr(IsPinned ? -1 : -2),Math.Max(monitor.work.left,monitor.work.right-440),Math.Max(monitor.work.top,monitor.work.bottom-320),420,300,0x0020|0x0010);
#endif
        return true;
    }
    public static bool SetPinned(bool pinned)
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        if (Handle == IntPtr.Zero || !SetWindowPos(Handle,new IntPtr(pinned ? -1 : -2),0,0,0,0,0x0010|0x0002|0x0001)) return false;
#endif
        IsPinned = pinned;
        return true;
    }
    public static void Minimize()
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        var window = Handle;
        if (window == IntPtr.Zero) return;
        // Expose a taskbar button so the hidden tool window can be restored.
        ShowWindow(window,0);
        SetWindowLong(window,-20,(GetWindowLong(window,-20) & ~0x80)|0x40000);
        SetWindowPos(window,IntPtr.Zero,0,0,0,0,0x0037);
        minimizedToTaskbar = true;
        ShowWindow(window,2);
#endif
    }
    public static void Hide()
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        ShowWindow(Handle,0);
#endif
    }
    public static void SetSize(int width,int height)
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        SetWindowPos(Handle,new IntPtr(IsPinned ? -1 : -2),0,0,width,height,0x0010|0x0002);
#else
        Screen.SetResolution(width,height,FullScreenMode.Windowed);
#endif
    }
    public static void Expand()
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        SetWindowPos(Handle,new IntPtr(IsPinned ? -1 : -2),0,0,420,300,0x0010|0x0002);
#else
        Screen.SetResolution(420,300,FullScreenMode.Windowed);
#endif
    }
    public static void BeginDrag(bool resize)
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        if(resize) BeginResize(8);
        else { ReleaseCapture(); SendMessage(Handle,0x00A1,new IntPtr(2),IntPtr.Zero); }
#endif
    }
    public static void BeginResize(int edge)
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        activeEdge=edge;GetWindowRect(Handle,out resizeRect);GetCursorPos(out resizeStart);SetCapture(Handle);
#endif
    }
    public static void ResizeCursor(int edge)
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        int cursor=edge==0?32512:(edge==1||edge==2)?32644:(edge==3||edge==6)?32645:(edge==4||edge==8)?32642:32643;
        SetCursor(LoadCursor(IntPtr.Zero,new IntPtr(cursor)));
#endif
    }
    public static bool UpdateResize()
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        if((GetAsyncKeyState(1)&0x8000)==0) { ReleaseCapture();return false; }
        GetCursorPos(out var point);
        int dx=point.x-resizeStart.x,dy=point.y-resizeStart.y;
        int left=resizeRect.left,right=resizeRect.right,top=resizeRect.top,bottom=resizeRect.bottom;
        if(activeEdge==1 || activeEdge==4 || activeEdge==7)left=Math.Min(right-240,left+dx);
        if(activeEdge==2 || activeEdge==5 || activeEdge==8)right=Math.Max(left+240,right+dx);
        if(activeEdge==3 || activeEdge==4 || activeEdge==5)top=Math.Min(bottom-180,top+dy);
        if(activeEdge==6 || activeEdge==7 || activeEdge==8)bottom=Math.Max(top+180,bottom+dy);
        SetWindowPos(Handle,new IntPtr(IsPinned ? -1 : -2),left,top,right-left,bottom-top,0x0010);
        ResizeCursor(activeEdge);
        return true;
#else
        return false;
#endif
    }
    public static void UpdateRoundedRegion()
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        GetWindowRect(Handle,out var rect);int width=rect.right-rect.left,height=rect.bottom-rect.top;
        if(width<=0||height<=0||(roundedWidth==width&&roundedHeight==height))return;
        var region=CreateRoundRectRgn(0,0,width+1,height+1,32,32);
        if(region==IntPtr.Zero)return;
        if(SetWindowRgn(Handle,region,true)==0){DeleteObject(region);return;}
        roundedWidth=width;roundedHeight=height; // Windows owns the successful region.
#endif
    }
    public static void EnforceMinimum()
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        if (IsIconic(Handle)) return;
        if (minimizedToTaskbar)
        {
            minimizedToTaskbar = false;
            SetWindowLong(Handle,-20,(GetWindowLong(Handle,-20)|0x80) & ~0x40000);
            SetWindowPos(Handle,new IntPtr(IsPinned ? -1 : -2),0,0,0,0,0x0033);
        }
        UpdateRoundedRegion();
        GetClientRect(Handle,out var rect);
        int width=rect.right-rect.left,height=rect.bottom-rect.top;
        if(width>0 && height>0 && (width<240 || height<180)) SetWindowPos(Handle,new IntPtr(IsPinned ? -1 : -2),0,0,Math.Max(240,width),Math.Max(180,height),0x0010|0x0002);
#endif
    }
}
