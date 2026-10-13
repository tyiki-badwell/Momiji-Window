using System.Runtime.InteropServices;
using static Momiji.Interop.User32.NativeMethods;

namespace Momiji.Interop.Comctl32;

internal static class Libraries
{
    public const string Comctl32 = "comctl32.dll";
}
internal static partial class NativeMethods
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode, SetLastError = false)]
    internal delegate nint SUBCLASSPROC(
        HWND hWnd,
        uint uMsg,
        nint wParam,
        nint lParam,
        nuint uIdSubclass,
        nint dwRefData
    );
}

internal static partial class NativeMethods
{
    [LibraryImport(Libraries.Comctl32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetWindowSubclass(
        HWND hWnd,
        nint pfnSubclass,
        nuint uIdSubclass,
        nint dwRefData
    );
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.Comctl32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool RemoveWindowSubclass(
        HWND hWnd,
        nint pfnSubclass,
        nuint uIdSubclass
    );
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.Comctl32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial nint DefSubclassProc(
        HWND hWnd,
        uint uMsg,
        nint wParam,
        nint lParam
    );
}
