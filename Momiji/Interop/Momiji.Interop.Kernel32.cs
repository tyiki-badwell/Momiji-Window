using System.Runtime.InteropServices;

namespace Momiji.Interop.Kernel32;

internal static class Libraries
{
    public const string Kernel32 = "kernel32.dll";
}

internal static partial class NativeMethods
{
    [LibraryImport(Libraries.Kernel32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial nint GetModuleHandleW(
        [MarshalAs(UnmanagedType.LPWStr)] string? lpModuleName
    );
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.Kernel32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(
        nint hObject
    );
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.Kernel32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int GetCurrentThreadId();
}
internal static partial class NativeMethods
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct STARTUPINFOW
    {
        [Flags]
        public enum STARTF : uint
        {
            USESHOWWINDOW = 0x00000001,
            USESIZE = 0x00000002,
            USEPOSITION = 0x00000004,
            USECOUNTCHARS = 0x00000008,

            USEFILLATTRIBUTE = 0x00000010,
            RUNFULLSCREEN = 0x00000020,
            FORCEONFEEDBACK = 0x00000040,
            FORCEOFFFEEDBACK = 0x00000080,

            USESTDHANDLES = 0x00000100,
            USEHOTKEY = 0x00000200,
            TITLEISLINKNAME = 0x00000800,

            TITLEISAPPID = 0x00001000,
            PREVENTPINNING = 0x00002000,
            UNTRUSTEDSOURCE = 0x00008000,
        }

        public int cb;
        public nint lpReserved;
        public nint lpDesktop;
        public nint lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public STARTF dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public nint lpReserved2;
        public nint hStdInput;
        public nint hStdOutput;
        public nint hStdError;
    }
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.Kernel32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial void GetStartupInfoW(ref STARTUPINFOW lpStartupInfo);
}
internal static partial class NativeMethods
{
    public enum PROCESS_INFORMATION_CLASS : uint
    {
        ProcessMemoryPriority,
        ProcessMemoryExhaustionInfo,
        ProcessAppMemoryInfo,
        ProcessInPrivateInfo,
        ProcessPowerThrottling,
        ProcessReservedValue1,
        ProcessTelemetryCoverageInfo,
        ProcessProtectionLevelInfo,
        ProcessLeapSecondInfo,
        ProcessMachineTypeInfo,
        ProcessInformationClassMax
    };
}
internal static partial class NativeMethods
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public class MEMORY_PRIORITY_INFORMATION
    {
        public enum MEMORY_PRIORITY : uint
        {
            UNKNOWN = 0,
            VERY_LOW,
            LOW,
            MEDIUM,
            BELOW_NORMAL,
            NORMAL
        };

        public MEMORY_PRIORITY MemoryPriority;
    }
}
internal static partial class NativeMethods
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public class PROCESS_POWER_THROTTLING_STATE
    {
        [Flags]
        public enum PROCESS_POWER_THROTTLING : uint
        {
            UNKNOWN = 0,
            EXECUTION_SPEED = 0x1,
            IGNORE_TIMER_RESOLUTION = 0x4
        };

        public uint Version;
        public PROCESS_POWER_THROTTLING ControlMask;
        public PROCESS_POWER_THROTTLING StateMask;
    }
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.Kernel32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetProcessInformation(
        nint hProcess,
        PROCESS_INFORMATION_CLASS ProcessInformationClass,
        nint ProcessInformation,
        int ProcessInformationSize
    );
}
