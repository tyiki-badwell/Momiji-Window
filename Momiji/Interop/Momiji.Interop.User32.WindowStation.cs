using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Momiji.Interop.User32;

internal sealed partial class HWindowStation : SafeHandleZeroOrMinusOneIsInvalid
{
    public HWindowStation() : base(true)
    {
    }

    protected override bool ReleaseHandle()
    {
        return NativeMethods.CloseWindowStation(handle);
    }
}

internal sealed partial class HDesktop : SafeHandleZeroOrMinusOneIsInvalid
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
        uint dwThreadId
    );
}
