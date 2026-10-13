using System.Runtime.InteropServices;

namespace Momiji.Interop.User32;

internal static partial class NativeMethods
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal readonly struct DPI_AWARENESS_CONTEXT
    {
        private readonly nint handle;
        internal nint Handle => handle;

        internal static DPI_AWARENESS_CONTEXT UNAWARE => new(-1);
        internal static DPI_AWARENESS_CONTEXT SYSTEM_AWARE => new(-2);
        internal static DPI_AWARENESS_CONTEXT PER_MONITOR_AWARE => new(-3);
        internal static DPI_AWARENESS_CONTEXT PER_MONITOR_AWARE_V2 => new(-4);
        internal static DPI_AWARENESS_CONTEXT UNAWARE_GDISCALED => new(-5);

        private DPI_AWARENESS_CONTEXT(nint i)
        {
            handle = i;
        }

        public static explicit operator DPI_AWARENESS_CONTEXT(nint i)
        {
            return new DPI_AWARENESS_CONTEXT(i);
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
    internal static partial DPI_AWARENESS_CONTEXT SetThreadDpiAwarenessContext(
        DPI_AWARENESS_CONTEXT dpiContext
    );
}

internal static partial class NativeMethods
{
    [LibraryImport(Libraries.User32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial DPI_AWARENESS_CONTEXT GetThreadDpiAwarenessContext();
}

internal static partial class NativeMethods
{
    [LibraryImport(Libraries.User32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial DPI_AWARENESS_CONTEXT GetWindowDpiAwarenessContext(
        HWND hWnd
    );
}

internal static partial class NativeMethods
{
    internal enum DPI_AWARENESS : int
    {
        INVALID = -1,
        UNAWARE = 0,
        SYSTEM_AWARE = 1,
        PER_MONITOR_AWARE = 2
    }

    [LibraryImport(Libraries.User32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial DPI_AWARENESS GetAwarenessFromDpiAwarenessContext(
        DPI_AWARENESS_CONTEXT value
    );
}

internal static partial class NativeMethods
{
    [LibraryImport(Libraries.User32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint GetDpiFromDpiAwarenessContext(
        DPI_AWARENESS_CONTEXT value
    );
}

internal static partial class NativeMethods
{
    [LibraryImport(Libraries.User32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool AreDpiAwarenessContextsEqual(
        DPI_AWARENESS_CONTEXT dpiContextA,
        DPI_AWARENESS_CONTEXT dpiContextB
    );
}

internal static partial class NativeMethods
{
    [LibraryImport(Libraries.User32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsValidDpiAwarenessContext(
        DPI_AWARENESS_CONTEXT value
    );
}

internal static partial class NativeMethods
{
    [LibraryImport(Libraries.User32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint GetDpiForWindow(
        HWND hWnd
    );
}

internal static partial class NativeMethods
{
    [LibraryImport(Libraries.User32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint GetDpiForSystem();
}

internal static partial class NativeMethods
{
    [LibraryImport(Libraries.User32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint GetSystemDpiForProcess(
        nint hProcess
    );
}

internal static partial class NativeMethods
{
    [LibraryImport(Libraries.User32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EnableNonClientDpiScaling(
        HWND hWnd
    );
}

internal static partial class NativeMethods
{
    [LibraryImport(Libraries.User32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool InheritWindowMonitor(
        HWND hWnd,
        HWND hwndInherit
    );
}

internal static partial class NativeMethods
{
    [LibraryImport(Libraries.User32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetProcessDpiAwarenessContext(
        DPI_AWARENESS_CONTEXT dpiContext
    );
}

internal static partial class NativeMethods
{
    [LibraryImport(Libraries.User32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial DPI_AWARENESS_CONTEXT GetDpiAwarenessContextForProcess(
        nint hProcess
    );
}

internal static partial class NativeMethods
{
    internal enum DPI_HOSTING_BEHAVIOR : int
    {
        INVALID = -1,
        DEFAULT = 0,
        MIXED = 1
    }

    [LibraryImport(Libraries.User32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial DPI_HOSTING_BEHAVIOR SetThreadDpiHostingBehavior(
        DPI_HOSTING_BEHAVIOR value
    );
}

internal static partial class NativeMethods
{
    [LibraryImport(Libraries.User32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial DPI_HOSTING_BEHAVIOR GetThreadDpiHostingBehavior();
}

internal static partial class NativeMethods
{
    [LibraryImport(Libraries.User32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial DPI_HOSTING_BEHAVIOR GetWindowDpiHostingBehavior(
        HWND hWnd
    );
}

internal static partial class NativeMethods
{
    [LibraryImport(Libraries.User32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool AdjustWindowRectExForDpi(
        ref RECT lpRect,
        int dwStyle,
        [MarshalAs(UnmanagedType.Bool)] bool bMenu,
        int dwExStyle,
        uint dpi
    );
}
