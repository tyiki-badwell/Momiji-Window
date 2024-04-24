using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using static Momiji.Interop.Advapi32.NativeMethods;

namespace Momiji.Interop.Advapi32;

internal static class Libraries
{
    public const string Advapi32 = "Advapi32.dll";
}

internal static partial class NativeMethods
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 0)]
    internal struct Trustee
    {
        public nint pMultipleTrustee;
        public int multipleTrusteeOperation;
        public int trusteeForm;
        public int trusteeType;
        public nint pSid;
    };
}
internal static partial class NativeMethods
{
    [StructLayout(LayoutKind.Sequential, Pack = 0)]
    internal struct SecurityAttributes
    {
        public int nLength;
        public nint lpSecurityDescriptor;
        public bool bInheritHandle;
    };
}
internal static partial class NativeMethods
{
    internal enum SECURITY_DESCRIPTOR_CONST : int
    {
        REVISION = 1,
        MIN_LENGTH = 20
    }
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.Advapi32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool InitializeSecurityDescriptor(
        nint pSecurityDescriptor,
        SECURITY_DESCRIPTOR_CONST dwRevision
    );
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.Advapi32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetSecurityDescriptorDacl(
        nint pSecurityDescriptor,
        [MarshalAs(UnmanagedType.Bool)] bool bDaclPresent,
        nint pDacl,
        [MarshalAs(UnmanagedType.Bool)] bool bDaclDefaulted
    );
}
internal static partial class NativeMethods
{
    internal enum ACCESS_MODE: int
    {
        NOT_USED_ACCESS,
        GRANT_ACCESS,
        SET_ACCESS,
        DENY_ACCESS,
        REVOKE_ACCESS,
        SET_AUDIT_SUCCESS,
        SET_AUDIT_FAILURE
    }
}
internal static partial class NativeMethods
{
    [Flags]
    internal enum ACE : int
    {
        NO_INHERITANCE = 0x0,
        OBJECT_INHERIT_ACE = 0x1,
        CONTAINER_INHERIT_ACE = 0x2,
        NO_PROPAGATE_INHERIT_ACE = 0x4,
        INHERIT_ONLY_ACE = 0x8
    }
}
internal static partial class NativeMethods
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 0)]
    internal struct ExplicitAccess
    {
        public int grfAccessPermissions;
        public ACCESS_MODE grfAccessMode;
        public ACE grfInheritance;
        public Trustee trustee;
    };
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.Advapi32, SetLastError = false)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial int SetEntriesInAclW(
        ulong cCountOfExplicitEntries,
        nint pListOfExplicitEntries,
        nint oldAcl,
        out nint newAcl
    );
}
internal static partial class NativeMethods
{
    internal enum DesiredAccess : uint
    {
        STANDARD_RIGHTS_REQUIRED = 0x000F0000,
        STANDARD_RIGHTS_READ = 0x00020000,
        TOKEN_ASSIGN_PRIMARY = 0x0001,
        TOKEN_DUPLICATE = 0x0002,
        TOKEN_IMPERSONATE = 0x0004,
        TOKEN_QUERY = 0x0008,
        TOKEN_QUERY_SOURCE = 0x0010,
        TOKEN_ADJUST_PRIVILEGES = 0x0020,
        TOKEN_ADJUST_GROUPS = 0x0040,
        TOKEN_ADJUST_DEFAULT = 0x0080,
        TOKEN_ADJUST_SESSIONID = 0x0100,
        TOKEN_READ = 
            STANDARD_RIGHTS_READ
            | TOKEN_QUERY,
        TOKEN_ALL_ACCESS = 
            STANDARD_RIGHTS_REQUIRED
            | TOKEN_ASSIGN_PRIMARY 
            | TOKEN_DUPLICATE 
            | TOKEN_IMPERSONATE 
            | TOKEN_QUERY 
            | TOKEN_QUERY_SOURCE 
            | TOKEN_ADJUST_PRIVILEGES 
            | TOKEN_ADJUST_GROUPS 
            | TOKEN_ADJUST_DEFAULT 
            | TOKEN_ADJUST_SESSIONID
    }
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.Advapi32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool OpenProcessToken(
        nint ProcessHandle,
        DesiredAccess DesiredAccess,
        out HToken TokenHandle
    );
}
internal static partial class NativeMethods
{
    internal enum TOKEN_INFORMATION_CLASS : int
    {
        TokenUser = 1,
        TokenGroups,
        TokenPrivileges,
        TokenOwner,
        TokenPrimaryGroup,
        TokenDefaultDacl,
        TokenSource,
        TokenType,
        TokenImpersonationLevel,
        TokenStatistics,
        TokenRestrictedSids,
        TokenSessionId,
        TokenGroupsAndPrivileges,
        TokenSessionReference,
        TokenSandBoxInert,
        TokenAuditPolicy,
        TokenOrigin,
        TokenElevationType,
        TokenLinkedToken,
        TokenElevation,
        TokenHasRestrictions,
        TokenAccessInformation,
        TokenVirtualizationAllowed,
        TokenVirtualizationEnabled,
        TokenIntegrityLevel,
        TokenUIAccess,
        TokenMandatoryPolicy,
        TokenLogonSid,
        TokenIsAppContainer,
        TokenCapabilities,
        TokenAppContainerSid,
        TokenAppContainerNumber,
        TokenUserClaimAttributes,
        TokenDeviceClaimAttributes,
        TokenRestrictedUserClaimAttributes,
        TokenRestrictedDeviceClaimAttributes,
        TokenDeviceGroups,
        TokenRestrictedDeviceGroups,
        TokenSecurityAttributes,
        TokenIsRestricted,
        TokenProcessTrustLevel,
        TokenPrivateNameSpace,
        TokenSingletonAttributes,
        TokenBnoIsolation,
        TokenChildProcessFlags,
        TokenIsLessPrivilegedAppContainer,
        TokenIsSandboxed,
        TokenIsAppSilo,
        MaxTokenInfoClass
    }
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.Advapi32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetTokenInformation(
        HToken TokenHandle,
        TOKEN_INFORMATION_CLASS TokenInformationClass,
        nint TokenInformation,
        int TokenInformationLength,
        out int ReturnLength
    );
}
internal static partial class NativeMethodsExtensions
{
    internal static bool GetTokenInformation(
        this HToken TokenHandle,
        TOKEN_INFORMATION_CLASS TokenInformationClass,
        nint TokenInformation,
        int TokenInformationLength,
        out int ReturnLength
    )
    {
        return NativeMethods.GetTokenInformation(TokenHandle, TokenInformationClass, TokenInformation, TokenInformationLength, out ReturnLength);
    }
}

internal static partial class NativeMethods
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 0)]
    internal struct SidAndAttributes
    {
        public int Sid;
        public int Attributes;
    };
}
internal static partial class NativeMethods
{
    [LibraryImport(Libraries.Advapi32, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool LookupAccountSidW(
        nint lpSystemName,
        nint Sid,
        nint Name,
        nint cchName,
        nint ReferencedDomainName,
        nint cchReferencedDomainName,
        out int peUse
    );
}

internal sealed partial class HToken : SafeHandleZeroOrMinusOneIsInvalid
{
    public HToken() : base(true)
    {
    }

    protected override bool ReleaseHandle()
    {
        return Kernel32.NativeMethods.CloseHandle(handle);
    }
}
