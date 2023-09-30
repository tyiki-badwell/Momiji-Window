using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Momiji.Interop.User32;

internal static class Libraries
{
    public const string User32 = "user32.dll";
}

internal sealed class HWindowStation : SafeHandleZeroOrMinusOneIsInvalid
{
    public HWindowStation() : base(true)
    {
    }

    protected override bool ReleaseHandle()
    {
        return NativeMethods.CloseWindowStation(handle);
    }
}

internal sealed class HDesktop : SafeHandleZeroOrMinusOneIsInvalid
{
    public HDesktop() : base(true)
    {
    }

    protected override bool ReleaseHandle()
    {
        return NativeMethods.CloseDesktop(handle);
    }
}

internal static partial class NativeMethods
{
    internal enum CWF : uint
    {
        NONE = 0,
        CREATE_ONLY = 0x0001
    }
}
internal static partial class NativeMethods
{
    internal enum DF : uint
    {
        NONE = 0,
        ALLOWOTHERACCOUNTHOOK = 0x0001
    }
}
internal static partial class NativeMethods
{
    [Flags]
    internal enum WINSTA_ACCESS_MASK : int
    {
        DELETE = 0x00010000,
        READ_CONTROL = 0x00020000,
        WRITE_DAC = 0x00040000,
        WRITE_OWNER = 0x00080000,
        SYNCHRONIZE = 0x00100000,

        STANDARD_RIGHTS_REQUIRED = DELETE | READ_CONTROL | WRITE_DAC | WRITE_OWNER,

        STANDARD_RIGHTS_READ = READ_CONTROL,
        STANDARD_RIGHTS_WRITE = READ_CONTROL,
        STANDARD_RIGHTS_EXECUTE = READ_CONTROL,

        STANDARD_RIGHTS_ALL = DELETE | READ_CONTROL | WRITE_DAC | WRITE_OWNER | SYNCHRONIZE,

        SPECIFIC_RIGHTS_ALL = 0x0000FFFF,

        ACCESS_SYSTEM_SECURITY = 0x01000000,

        MAXIMUM_ALLOWED = 0x02000000,

        GENERIC_READ = unchecked((int)0x80000000),
        GENERIC_WRITE = 0x40000000,
        GENERIC_EXECUTE = 0x20000000,
        GENERIC_ALL = 0x10000000,

        WINSTA_ENUMDESKTOPS = 0x00000001,
        WINSTA_READATTRIBUTES = 0x00000002,
        WINSTA_ACCESSCLIPBOARD = 0x00000004,
        WINSTA_CREATEDESKTOP = 0x00000008,
        WINSTA_WRITEATTRIBUTES = 0x00000010,
        WINSTA_ACCESSGLOBALATOMS = 0x00000020,
        WINSTA_EXITWINDOWS = 0x00000040,
        WINSTA_ENUMERATE = 0x00000100,
        WINSTA_READSCREEN = 0x00000200,

        WINSTA_ALL_ACCESS = 0x0000037F
    }
}
internal static partial class NativeMethods
{
    [Flags]
    internal enum DESKTOP_ACCESS_MASK : int
    {
        DELETE = 0x00010000,
        READ_CONTROL = 0x00020000,
        WRITE_DAC = 0x00040000,
        WRITE_OWNER = 0x00080000,
        SYNCHRONIZE = 0x00100000,

        STANDARD_RIGHTS_REQUIRED = DELETE | READ_CONTROL | WRITE_DAC | WRITE_OWNER,

        STANDARD_RIGHTS_READ = READ_CONTROL,
        STANDARD_RIGHTS_WRITE = READ_CONTROL,
        STANDARD_RIGHTS_EXECUTE = READ_CONTROL,

        STANDARD_RIGHTS_ALL = DELETE | READ_CONTROL | WRITE_DAC | WRITE_OWNER | SYNCHRONIZE,

        SPECIFIC_RIGHTS_ALL = 0x0000FFFF,

        ACCESS_SYSTEM_SECURITY = 0x01000000,

        MAXIMUM_ALLOWED = 0x02000000,

        GENERIC_READ = unchecked((int)0x80000000),
        GENERIC_WRITE = 0x40000000,
        GENERIC_EXECUTE = 0x20000000,
        GENERIC_ALL = 0x10000000,

        DESKTOP_READOBJECTS = 0x00000001,
        DESKTOP_CREATEWINDOW = 0x00000002,
        DESKTOP_CREATEMENU = 0x00000004,
        DESKTOP_HOOKCONTROL = 0x00000008,
        DESKTOP_JOURNALRECORD = 0x00000010,
        DESKTOP_JOURNALPLAYBACK = 0x00000020,
        DESKTOP_ENUMERATE = 0x00000040,
        DESKTOP_WRITEOBJECTS = 0x00000080,
        DESKTOP_SWITCHDESKTOP = 0x00000100,
    }
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.User32, SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static partial HWindowStation CreateWindowStationW(
        string? lpwinsta,
        CWF dwFlags,
        WINSTA_ACCESS_MASK dwDesiredAccess, 
        nint /*ref Advapi32.NativeMethods.SecurityAttributes*/ lpsa
    );
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.User32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseWindowStation(
        nint hWinSta
    );
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.User32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetProcessWindowStation(
        HWindowStation hWinSta
    );
}
internal static partial class NativeMethodsExtensions
{
    internal static bool SetProcessWindowStation(
        this HWindowStation hWinSta
    )
    {
        return NativeMethods.SetProcessWindowStation(hWinSta);
    }
}

internal static partial class NativeMethods
{
    [LibraryImport(Libraries.User32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static partial HWindowStation GetProcessWindowStation();
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.User32, SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial HDesktop CreateDesktopW(
        string? lpszDesktop,
        nint lpszDevice, /* NULL */
        nint pDevmode, /* NULL */
        DF dwFlags,
        DESKTOP_ACCESS_MASK dwDesiredAccess,
        nint /*ref Advapi32.NativeMethods.SecurityAttributes*/ lpsa
    );
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.User32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseDesktop(
        nint hDesktop
    );
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.User32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetThreadDesktop(
        HDesktop hDesktop
    );
}
internal static partial class NativeMethodsExtensions
{
    internal static bool SetThreadDesktop(
        this HDesktop hDesktop
    )
    {
        return NativeMethods.SetThreadDesktop(hDesktop);
    }
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.User32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial HDesktop GetThreadDesktop(
        int dwThreadId
    );
}
internal static partial class NativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct WNDCLASSEX
    {
        [Flags]
        internal enum CS : uint
        {
            NONE    = 0,
            VREDRAW = 0x00000001,
            HREDRAW = 0x00000002,
            DBLCLKS = 0x00000008,
            OWNDC = 0x00000020,
            CLASSDC = 0x00000040,
            PARENTDC = 0x00000080,
            NOCLOSE = 0x00000200,
            SAVEBITS = 0x00000800,
            BYTEALIGNCLIENT = 0x00001000,
            BYTEALIGNWINDOW = 0x00002000,
            GLOBALCLASS = 0x00004000,
            IME = 0x00010000,
            DROPSHADOW = 0x00020000
        }

        public int cbSize;
        public CS style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public nint lpszMenuName;
        public nint lpszClassName;
        public nint hIconSm;

        public readonly override string ToString()
        {
            return
                $"cbSize[{cbSize}] style[{style}] lpfnWndProc[{lpfnWndProc:X}] cbClsExtra[{cbClsExtra}] cbWndExtra[{cbWndExtra}] hInstance[{hInstance:X}] hIcon[{hIcon:X}] hCursor[{hCursor:X}] hbrBackground[{hbrBackground:X}] lpszMenuName[{lpszMenuName:X}] lpszClassName[{lpszClassName:X}] hIconSm[{hIconSm:X}]";
        }
    }
}
internal static partial class NativeMethods
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Ansi, SetLastError = false)]
    internal delegate nint WNDPROC(HWND hWnd, uint msg, nint wParam, nint lParam);
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.User32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial ushort RegisterClassExW(
        ref WNDCLASSEX lpWndClass
    );
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.User32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UnregisterClassW(
        nint lpClassName,
        nint hInstance
    );
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.User32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsGUIThread(
        [MarshalAs(UnmanagedType.Bool)] bool bConvert
    );
}
internal static partial class NativeMethods
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal readonly struct HWND
    {
        private readonly nint handle;
        internal nint Handle => handle;

        internal static HWND None => default;
        internal static HWND BROADCAST => new(0xFFFF);

        private HWND(nint i)
        {
            handle = i;
        }

        public static explicit operator HWND(nint i)
        {
            return new HWND(i);
        }
        public readonly override string ToString()
        {
            return
                $"{handle:X}";
        }
    }
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.User32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial nint DefWindowProcA(
        HWND hWnd,
        uint msg,
        nint wParam,
        nint lParam
    );
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.User32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial nint DefWindowProcW(
        HWND hWnd,
        uint msg,
        nint wParam,
        nint lParam
    );
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.User32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial HWND CreateWindowExW(
        int dwExStyle,
        nint lpszClassName,
        nint lpszWindowName,
        int style,
        int x,
        int y,
        int width,
        int height,
        HWND hwndParent,
        nint hMenu,
        nint hInst,
        nint pvParam
    );
}
internal static partial class NativeMethods
{
    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    internal struct CREATESTRUCT
    {
        public nint lpCreateParams;
        public nint hInstance;
        public nint hMenu;
        public HWND hwndParent;
        public int cy;
        public int cx;
        public int y;
        public int x;
        public long style;
        public nint lpszName;
        public nint lpszClass;
        public int dwExStyle;
        public readonly override string ToString()
        {
            return
                $"lpCreateParams[{lpCreateParams:X}] hInstance[{hInstance:X}] hMenu[{hMenu:X}] hwndParent[{hwndParent}] cy[{cy}] cx[{cx}] y[{y}] x[{x}] style[{style:X}] lpszName[{lpszName:X}] lpszClass[{lpszClass:X}] dwExStyle[{dwExStyle:X}]";
        }
    }
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.User32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyWindow(
        HWND hwnd
    );
}
internal static partial class NativeMethods
{
    [StructLayout(LayoutKind.Sequential, Pack = 0)]
    internal struct MSG
    {
        public HWND hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public POINT pt;

        public readonly override string ToString()
        {
            return
                $"hwnd[{hwnd}] message[{message:X}] wParam[{wParam:X}] lParam[{lParam:X}] time[{time}] pt[{pt}]";
        }
    }
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.User32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindowUnicode(
        HWND hwnd
    );
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.User32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint GetQueueStatus(
        uint flags
    );
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.User32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint MsgWaitForMultipleObjectsEx(
        uint nCount,
        nint pHandles,
        uint dwMilliseconds,
        uint dwWakeMask,
        uint dwFlags
    );
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.User32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int GetMessageA(
        ref MSG msg,
        HWND hwnd,
        int nMsgFilterMin,
        int nMsgFilterMax
    );
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.User32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int GetMessageW(
        ref MSG msg,
        HWND hwnd,
        int nMsgFilterMin,
        int nMsgFilterMax
    );
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.User32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PeekMessageW(
        ref MSG msg,
        HWND hwnd,
        int nMsgFilterMin,
        int nMsgFilterMax,
        int wRemoveMsg
    );
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.User32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool TranslateMessage(
        ref MSG msg
    );
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.User32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial nint DispatchMessageA(
        ref MSG msg
    );
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.User32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial nint DispatchMessageW(
        ref MSG msg
    );
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.User32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SendNotifyMessageA(
        HWND hWnd,
        uint nMsg,
        nint wParam,
        nint lParam
    );
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.User32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SendNotifyMessageW(
        HWND hWnd,
        uint nMsg,
        nint wParam,
        nint lParam
    );
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.User32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial nint SendMessageW(
        HWND hWnd,
        uint nMsg,
        nint wParam,
        nint lParam
    );
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.User32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PostMessageW(
        HWND hWnd,
        uint nMsg,
        nint wParam,
        nint lParam
    );
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.User32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial nint PostThreadMessageW(
        int idThread,
        uint nMsg,
        nint wParam,
        nint lParam
    );
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.User32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial nint SetWindowLongPtrA(
        HWND hWnd,
        int nIndex,
        nint dwNewLong
    );
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.User32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial nint SetWindowLongPtrW(
        HWND hWnd,
        int nIndex,
        nint dwNewLong
    );
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.User32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial nint SetWindowLongA(
        HWND hWnd,
        int nIndex,
        nint dwNewLong
    );
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.User32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial nint SetWindowLongW(
        HWND hWnd,
        int nIndex,
        nint dwNewLong
    );
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.User32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial nint CallWindowProcA(
        nint lpPrevWndFunc,
        HWND hWnd,
        uint Msg,
        nint wParam,
        nint lParam
    );
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.User32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial nint CallWindowProcW(
        nint lpPrevWndFunc,
        HWND hWnd,
        uint Msg,
        nint wParam,
        nint lParam
    );
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.User32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial void PostQuitMessage(
        int nExitCode
    );
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.User32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool MoveWindow(
        HWND hWnd,
        int X,
        int Y,
        int nWidth,
        int nHeight,
        [MarshalAs(UnmanagedType.Bool)] bool bRepaint
    );
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.User32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetWindowPos(
      HWND hWnd,
      HWND hWndInsertAfter,
      int X,
      int Y,
      int cx,
      int cy,
      uint uFlags
    );
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.User32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ShowWindow(
        HWND hWnd,
        int nCmdShow
    );
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.User32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ShowWindowAsync(
        HWND hWnd,
        int nCmdShow
    );
}
internal static partial class NativeMethods
{
    [StructLayout(LayoutKind.Sequential, Pack = 0)]
    internal struct POINT
    {
        public long x;
        public long y;
        public readonly override string ToString()
        {
            return
                $"x[{x}] y[{y}]";
        }
    }
}
internal static partial class NativeMethods
{
    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    internal struct WINDOWPLACEMENT
    {
        [Flags]
        internal enum FLAG : uint
        {
            WPF_SETMINPOSITION = 0x0001,
            WPF_RESTORETOMAXIMIZED = 0x0002,
            WPF_ASYNCWINDOWPLACEMENT = 0x0004
        }
        public int length;
        public FLAG flags;
        public uint showCmd;
        public POINT ptMinPosition;
        public POINT ptMaxPosition;
        public RECT rcNormalPosition;
        public RECT rcDevice;
        public readonly override string ToString()
        {
            return
                $"formatType[{flags:F}] showCmd[{showCmd:X}] ptMinPosition[{ptMinPosition}] ptMaxPosition[{ptMaxPosition}] rcNormalPosition[{rcNormalPosition}] rcDevice[{rcDevice}]";
        }
    }
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.User32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetWindowPlacement(
        HWND hWnd,
        ref WINDOWPLACEMENT lpwndpl
    );
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.User32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int InSendMessageEx(
        nint lpReserved
    );
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.User32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ReplyMessage(
        nint lResult
    );
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.User32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PrintWindow(
        HWND hWnd,
        nint hDC,
        int flags
    );
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.User32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial nint GetDC(
        HWND hWnd
    );
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.User32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int ReleaseDC(
        HWND hWnd,
        nint hDC
    );
}
internal static partial class NativeMethods
{
    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    internal struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
        public readonly override string ToString()
        {
            return
                $"left[{left}] top[{top}] right[{right}] bottom[{bottom}]";
        }
    };
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.User32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetClientRect(
        HWND hWnd,
        ref RECT lpRect
    );
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.User32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool AdjustWindowRect(
        ref RECT lpRect,
        int dwStyle,
        [MarshalAs(UnmanagedType.Bool)] bool bMenu
    );
}
