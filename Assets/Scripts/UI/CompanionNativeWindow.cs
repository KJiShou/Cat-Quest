using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.EventSystems;

// All native operations are confined to the companion's own process window.
public static class CompanionNativeWindow
{
    public const int MinimumWidth=240, MinimumHeight=180;
    public static int ResizeStarts { get; private set; }
    public static int ResizeFrames { get; private set; }
    public static int LastResizeEdge { get; private set; }
    public static bool NativeResizeInstalled { get; private set; }
    public static bool SuppressPointer { get; private set; }
    public static bool IsPinned { get; private set; } = true;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern IntPtr GetFocus();
    [DllImport("user32.dll")] static extern IntPtr SetFocus(IntPtr window);
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
    [DllImport("user32.dll")] static extern uint GetMessagePos();
    [DllImport("user32.dll")] static extern IntPtr GetCapture();
    delegate IntPtr SubclassProc(IntPtr window,uint message,IntPtr wParam,IntPtr lParam,UIntPtr id,UIntPtr data);
    [DllImport("comctl32.dll")] static extern bool SetWindowSubclass(IntPtr window,SubclassProc callback,UIntPtr id,UIntPtr data);
    [DllImport("comctl32.dll")] static extern bool RemoveWindowSubclass(IntPtr window,SubclassProc callback,UIntPtr id);
    [DllImport("comctl32.dll")] static extern IntPtr DefSubclassProc(IntPtr window,uint message,IntPtr wParam,IntPtr lParam);
    static readonly SubclassProc ResizeProcedure=WindowProcedure; // Root the delegate for the entire native registration.
    static readonly UIntPtr ResizeSubclassId=new UIntPtr(1);
    static Point MessagePoint() { uint p=GetMessagePos();return new Point{x=unchecked((short)(p&0xffff)),y=unchecked((short)(p>>16))}; }
    static int EdgeAt(Point point)
    {
        if(!GetWindowRect(handle,out var r))return 0;
        int x=point.x-r.left,y=point.y-r.top,w=r.right-r.left,h=r.bottom-r.top;
        if(x<0||y<0||x>=w||y>=h)return 0;
        if(x<12 && y<12)return 4;
        if(x>=w-12 && y<12)return 5;
        if(x<12 && y>=h-12)return 7;
        if(x>=w-12 && y>=h-12)return 8;
        return x<6?1:x>=w-6?2:y<6?3:y>=h-6?6:0;
    }
    // Consume edge gestures before Unity's per-frame input processing can coalesce
    // a quick press/move/release. Message coordinates retain the actual press origin.
    static IntPtr WindowProcedure(IntPtr window,uint message,IntPtr wParam,IntPtr lParam,UIntPtr id,UIntPtr data)
    {
        if(message==0x0201 || message==0x0203) // WM_LBUTTONDOWN / WM_LBUTTONDBLCLK
        {
            var point=MessagePoint();int edge=EdgeAt(point);
            if(edge!=0){StartResize(edge,point);return IntPtr.Zero;}
        }
        if(activeEdge!=0 && (message==0x0200 || message==0x0202)) // MOVE / UP
        {
            ApplyResize(MessagePoint());
            if(message==0x0202)CancelResize();
            return IntPtr.Zero;
        }
        if(message==0x0215 || message==0x001F || (message==0x001C && wParam==IntPtr.Zero)) // capture/cancel/deactivate
            CancelResize();
        if(activeEdge!=0 && message==0x0100 && wParam.ToInt64()==27) // Escape restores this drag's starting bounds.
        {
            SetWindowPos(window,IntPtr.Zero,resizeRect.left,resizeRect.top,resizeRect.right-resizeRect.left,resizeRect.bottom-resizeRect.top,0x0010|0x0004);
            CancelResize();return IntPtr.Zero;
        }
        if(message==0x0020) // WM_SETCURSOR
        {
            GetCursorPos(out var point);int edge=activeEdge!=0?activeEdge:EdgeAt(point);
            if(edge!=0){ResizeCursor(edge);return new IntPtr(1);}
        }
        if(message==0x0082)Shutdown(); // WM_NCDESTROY
        return DefSubclassProc(window,message,wParam,lParam);
    }
    static void StartResize(int edge,Point point)
    {
        if(!GetWindowRect(handle,out resizeRect))return;
        resizeStart=point;activeEdge=edge;LastResizeEdge=edge;ResizeStarts++;SuppressPointer=true;
        SetCapture(handle);
    }
    static void ApplyResize(Point point)
    {
        int dx=point.x-resizeStart.x,dy=point.y-resizeStart.y;
        int left=resizeRect.left,right=resizeRect.right,top=resizeRect.top,bottom=resizeRect.bottom;
        if(activeEdge==1||activeEdge==4||activeEdge==7)left=Math.Min(right-MinimumWidth,left+dx);
        if(activeEdge==2||activeEdge==5||activeEdge==8)right=Math.Max(left+MinimumWidth,right+dx);
        if(activeEdge==3||activeEdge==4||activeEdge==5)top=Math.Min(bottom-MinimumHeight,top+dy);
        if(activeEdge==6||activeEdge==7||activeEdge==8)bottom=Math.Max(top+MinimumHeight,bottom+dy);
        SetWindowPos(handle,IntPtr.Zero,left,top,right-left,bottom-top,0x0010|0x0004);
        ResizeFrames++;ResizeCursor(activeEdge);
    }
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
    public static bool IsVisible
    {
        get {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            return Handle!=IntPtr.Zero && IsWindowVisible(Handle);
#else
            return Application.isFocused;
#endif
        }
    }
    public static bool IsForeground
    {
        get {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            return Handle!=IntPtr.Zero && !IsIconic(Handle) && GetForegroundWindow()==Handle;
#else
            return Application.isFocused;
#endif
        }
    }
    public static bool Configure(bool visible = true)
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        var window=Handle; if(window==IntPtr.Zero) return false;
        if(!NativeResizeInstalled)NativeResizeInstalled=SetWindowSubclass(window,ResizeProcedure,ResizeSubclassId,UIntPtr.Zero);
        if(!NativeResizeInstalled)return false;
        // Refresh shell registration without activating the companion.
        ShowWindow(window,0);
        int style=GetWindowLong(window,-16);
        SetWindowLong(window,-16,(style & ~0x00C40000)); // Remove caption and sizing frame; custom handles below.
        SetWindowLong(window,-20,(GetWindowLong(window,-20) & ~0x80)|0x40000); // WS_EX_APPWINDOW, not WS_EX_TOOLWINDOW: keep a taskbar entry.
        var monitor=new MonitorInfo{size=Marshal.SizeOf<MonitorInfo>()};
        GetMonitorInfo(MonitorFromWindow(window,2),ref monitor);
        SetWindowPos(window,new IntPtr(IsPinned ? -1 : -2),Math.Max(monitor.work.left,monitor.work.right-440),Math.Max(monitor.work.top,monitor.work.bottom-320),420,300,0x0020|0x0010);
        if (visible) ShowWindow(window,4); // SW_SHOWNOACTIVATE
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
        CancelResize();ShowWindow(window,2);
#endif
    }
    public static void Show()
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        if (Handle != IntPtr.Zero) ShowWindow(Handle,4);
#endif
    }
    public static void Hide()
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        CancelResize();ShowWindow(Handle,0);
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
        GetCursorPos(out var point);StartResize(edge,point);
#endif
    }
    public static void ResizeCursor(int edge)
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        int cursor=edge==0?32512:(edge==1||edge==2)?32644:(edge==3||edge==6)?32645:(edge==4||edge==8)?32642:32643;
        SetCursor(LoadCursor(IntPtr.Zero,new IntPtr(cursor)));
#endif
    }
    public static void CancelResize()
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        if(activeEdge==0)return; // Do not release capture owned by an ordinary Unity control.
        activeEdge=0;
        if(GetCapture()==handle)ReleaseCapture();
#endif
    }
    public static void CompleteInputFrame()
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        if(activeEdge==0)SuppressPointer=false;
#endif
    }
    public static void Shutdown()
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        CancelResize();
        if(NativeResizeInstalled)RemoveWindowSubclass(handle,ResizeProcedure,ResizeSubclassId);
        NativeResizeInstalled=false;
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
        // A borderless window shown without activation can become foreground with no
        // keyboard focus. Windows then sends WM_SYSKEYDOWN (Enter can toggle fullscreen).
        // Repair only our own already-foreground window; never activate another app.
        if (GetForegroundWindow()==Handle && GetFocus()==IntPtr.Zero) SetFocus(Handle);
        UpdateRoundedRegion();
        GetClientRect(Handle,out var rect);
        int width=rect.right-rect.left,height=rect.bottom-rect.top;
        if(width>0 && height>0 && (width<MinimumWidth || height<MinimumHeight)) SetWindowPos(Handle,new IntPtr(IsPinned ? -1 : -2),0,0,Math.Max(MinimumWidth,width),Math.Max(MinimumHeight,height),0x0010|0x0002);
#endif
    }
}
