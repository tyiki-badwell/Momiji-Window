using System.Runtime.InteropServices;

namespace Momiji.Interop.User32;

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
    internal static partial uint GetQueueStatus(
        uint flags
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
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PostThreadMessageW(
        uint idThread,
        uint nMsg,
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
