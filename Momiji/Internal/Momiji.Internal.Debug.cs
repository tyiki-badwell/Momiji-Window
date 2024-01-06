using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Extensions.Logging;
using Momiji.Core.Buffer;
using Momiji.Core.Window;
using Momiji.Internal.Log;
using Momiji.Interop.User32;
using Advapi32 = Momiji.Interop.Advapi32.NativeMethods;
using Kernel32 = Momiji.Interop.Kernel32.NativeMethods;
using User32 = Momiji.Interop.User32.NativeMethods;

namespace Momiji.Internal.Debug;

public class WindowDebug
{
    public static void CheckIntegrityLevel(
        ILoggerFactory loggerFactory
    )
    {
        var logger = loggerFactory.CreateLogger<WindowDebug>();

        if (!Advapi32.OpenProcessToken(
                Process.GetCurrentProcess().Handle,
                Advapi32.DesiredAccess.TOKEN_QUERY,
                out var token
        ))
        {
            var error = new Win32Exception();
            logger.LogWithError(LogLevel.Error, "[window debug] OpenProcessToken failed", error.ToString(), Environment.CurrentManagedThreadId);
            return;
        }

        try
        {
            if (!Advapi32.GetTokenInformation(
                    token,
                    Advapi32.TOKEN_INFORMATION_CLASS.TokenIntegrityLevel,
                    nint.Zero,
                    0,
                    out var dwLengthNeeded
            ))
            {
                var error = new Win32Exception();
                if (error.NativeErrorCode != 122) //ERROR_INSUFFICIENT_BUFFER ではない
                {
                    logger.LogWithError(LogLevel.Error, "[window debug] GetTokenInformation failed", error.ToString(), Environment.CurrentManagedThreadId);
                    return;
                }
            }
            else
            { //ここでは必ずエラーになるハズなので、正常系がエラー扱い
                return;
            }

            using var tiBuf = new PinnedBuffer<byte[]>(new byte[dwLengthNeeded]);
            var ti = tiBuf.AddrOfPinnedObject;

            if (!Advapi32.GetTokenInformation(
                    token,
                    Advapi32.TOKEN_INFORMATION_CLASS.TokenIntegrityLevel,
                    ti,
                    dwLengthNeeded,
                    out var _
            ))
            {
                var error = new Win32Exception();
                logger.LogWithError(LogLevel.Error, "[window debug] GetTokenInformation failed", error.ToString(), Environment.CurrentManagedThreadId);
                return;
            }

            var p = Marshal.ReadIntPtr(ti);
            var sid = new SecurityIdentifier(p);
            logger.Log(LogLevel.Information, $"[window debug] SecurityInfo token:({sid})");
            PrintAccountFromSid(logger, p);
        }
        finally
        {
            token.Dispose();
        }
    }

    private static void PrintDesktopACL(
        ILogger logger,
        HDesktop desktop
    )
    {
        try
        {
            var windowSecurity = new WindowSecurity(desktop);
            logger.Log(LogLevel.Information, $"[window debug] SecurityInfo owner:{windowSecurity.GetOwner(typeof(NTAccount))}({windowSecurity.GetOwner(typeof(SecurityIdentifier))})");
            logger.Log(LogLevel.Information, $"[window debug] SecurityInfo group:{windowSecurity.GetGroup(typeof(NTAccount))}({windowSecurity.GetGroup(typeof(SecurityIdentifier))})");

            logger.Log(LogLevel.Information, "---------------------------------");
            foreach (AccessRule<User32.DESKTOP_ACCESS_MASK> rule in windowSecurity.GetAccessRules(true, true, typeof(NTAccount)))
            {
                logger.Log(LogLevel.Information, $"[window debug] AccessRule:{rule.IdentityReference} {rule.Rights}");
            }
            logger.Log(LogLevel.Information, "---------------------------------");
            foreach (AccessRule<User32.DESKTOP_ACCESS_MASK> rule in windowSecurity.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            {
                logger.Log(LogLevel.Information, $"[window debug] AccessRule:{rule.IdentityReference} {rule.Rights}");
            }
            logger.Log(LogLevel.Information, "---------------------------------");
        }
        catch (Exception e)
        {
            logger.Log(LogLevel.Error,e, "[window debug] WindowSecurity error");
        }
    }
    private static void PrintAccountFromSid(
    ILogger logger,
        nint sid
    )
    {
        using var szNameBuf = new PinnedBuffer<int[]>(new int[1]);
        var szName = szNameBuf.AddrOfPinnedObject;

        using var szDomainNameBuf = new PinnedBuffer<int[]>(new int[1]);
        var szDomainName = szDomainNameBuf.AddrOfPinnedObject;

        if (!Advapi32.LookupAccountSidW(nint.Zero, sid, nint.Zero, szName, nint.Zero, szDomainName, out var use))
        {
            var error = new Win32Exception();
            if (error.NativeErrorCode != 122) //ERROR_INSUFFICIENT_BUFFER ではない
            {
                logger.LogWithError(LogLevel.Error, "LookupAccountSidW failed", error.ToString(), Environment.CurrentManagedThreadId);
                return;
            }
        }
        else
        { //ここでは必ずエラーになるハズなので、正常系がエラー扱い
            return;
        }

        using var nameBuf = new PinnedBuffer<char[]>(new char[szNameBuf.Target[0]]);
        var name = nameBuf.AddrOfPinnedObject;

        using var domainNameBuf = new PinnedBuffer<char[]>(new char[szDomainNameBuf.Target[0]]);
        var domainName = domainNameBuf.AddrOfPinnedObject;
        if (!Advapi32.LookupAccountSidW(nint.Zero, sid, name, szName, domainName, szDomainName, out var _))
        {
            var error = new Win32Exception();
            logger.LogWithError(LogLevel.Error, "GetTokenInformation failed", error.ToString(), Environment.CurrentManagedThreadId);
            return;
        }
        logger.Log(LogLevel.Information, $"account [{Marshal.PtrToStringUni(name)}] [{Marshal.PtrToStringUni(domainName)}] {use}");
    }

    public static void CheckDesktop(
        ILoggerFactory loggerFactory
    )
    {
        var logger = loggerFactory.CreateLogger<WindowDebug>();

        {
            using var desktop = User32.GetThreadDesktop(Kernel32.GetCurrentThreadId());
            logger.Log(LogLevel.Information, $"[window debug] GetThreadDesktop now:{desktop.DangerousGetHandle():X}");

            if (desktop.IsInvalid)
            {
                var error = new Win32Exception();
                logger.LogWithError(LogLevel.Error, "[window debug] GetThreadDesktop failed", error.ToString(), Environment.CurrentManagedThreadId);
            }
            else
            {
                PrintDesktopACL(logger, desktop);
            }
        }

        {
            var sd =
                new CommonSecurityDescriptor(
                    false,
                    false,
                    ControlFlags.None,
                    new SecurityIdentifier(WellKnownSidType.AnonymousSid, null),
                    null,
                    null,
                    null
                );

            var length = sd.BinaryLength;
            var buffer = new byte[length];

            sd.GetBinaryForm(buffer, 0);

            using var aa = new PinnedBuffer<byte[]>(buffer);

            var sa = new Advapi32.SecurityAttributes()
            {
                nLength = Marshal.SizeOf<Advapi32.SecurityAttributes>(),
                lpSecurityDescriptor = aa.AddrOfPinnedObject,
                bInheritHandle = false
            };

            //TODO error 1307
            using var desktop =
                User32.CreateDesktopW(
                    "test",
                    nint.Zero,
                    nint.Zero,
                    User32.DF.NONE,
                    User32.DESKTOP_ACCESS_MASK.GENERIC_ALL,
                    nint.Zero //ref sa
                );
            logger.Log(LogLevel.Information, $"[window debug] CreateDesktopW new:{desktop.DangerousGetHandle():X}");

            if (desktop.IsInvalid)
            {
                var error = new Win32Exception();
                logger.LogWithError(LogLevel.Error, "[window debug] CreateDesktopW failed", error.ToString(), Environment.CurrentManagedThreadId);
            }
            else
            {
                PrintDesktopACL(logger, desktop);
            }
        }

        /*
        {
            using var sdBuf = new PinnedBuffer<byte[]>(new byte[(int)Advapi32.SECURITY_DESCRIPTOR_CONST.MIN_LENGTH * 10]);
            var sd = sdBuf.AddrOfPinnedObject;

            {
                var result =
                    Advapi32.InitializeSecurityDescriptor(
                        sd,
                        Advapi32.SECURITY_DESCRIPTOR_CONST.REVISION
                    );
                if (!result)
                {
                    var error = new Win32Exception();
                    logger.LogWithError(LogLevel.Error, "[window debug] InitializeSecurityDescriptor failed", error.ToString(), Environment.CurrentManagedThreadId);
                }
            }

            {
                using var eaBuf = new PinnedBuffer<Advapi32.ExplicitAccess>(new()
                {
                    grfAccessPermissions = 0,
                    grfAccessMode = Advapi32.ACCESS_MODE.GRANT_ACCESS,
                    grfInheritance = Advapi32.ACE.NO_INHERITANCE,
                    trustee = new Advapi32.Trustee()
                    {
                        trusteeForm = 0,
                        trusteeType = 0,
                        pSid = nint.Zero
                    }
                });

                //TODO error 87 
                var error =
                    Advapi32.SetEntriesInAclW(
                        1,
                        eaBuf.AddrOfPinnedObject,
                        nint.Zero,
                        out var newAcl
                    );
                if (error != 0)
                {
                    logger.Log(LogLevel.Error,$"[window debug] SetEntriesInAclW failed [{error} {Marshal.GetPInvokeErrorMessage(error)}]");
                }
                else
                {
                    var result =
                        Advapi32.SetSecurityDescriptorDacl(
                            sd,
                            true,
                            newAcl,
                            false
                        );
                    if (!result)
                    {
                        var error2 = new Win32Exception();
                        logger.LogWithError(LogLevel.Error, "[window debug] SetSecurityDescriptorDacl failed", error2.ToString(), Environment.CurrentManagedThreadId);
                    }
                }
                Marshal.FreeHGlobal(newAcl);
            }

            var sa = new Advapi32.SecurityAttributes()
            {
                nLength = Marshal.SizeOf<Advapi32.SecurityAttributes>(),
                lpSecurityDescriptor = sd,
                bInheritHandle = false
            };

            using var desktop =
                User32.CreateDesktopW(
                    "test",
                    nint.Zero,
                    nint.Zero,
                    User32.DF.NONE,
                    User32.DESKTOP_ACCESS_MASK.GENERIC_ALL,
                    ref sa
                );
            logger.Log(LogLevel.Information, $"[window debug] CreateDesktopW new:{desktop.DangerousGetHandle():X}");

            if (desktop.IsInvalid)
            {
                var error = new Win32Exception();
                logger.LogWithError(LogLevel.Error, "[window debug] CreateDesktopW failed", error2.ToString(), Environment.CurrentManagedThreadId);
            }
            else
            {
                PrintDesktopACL(logger, desktop);
            }
        }
            */
    }

    public static void CheckGetProcessInformation(
        ILoggerFactory loggerFactory
    )
    {
        var logger = loggerFactory.CreateLogger<WindowDebug>();

        {
            using var buf = new PinnedBuffer<Kernel32.MEMORY_PRIORITY_INFORMATION>(new());
            var result =
                Kernel32.GetProcessInformation(
                    Process.GetCurrentProcess().Handle,
                    Kernel32.PROCESS_INFORMATION_CLASS.ProcessMemoryPriority,
                    buf.AddrOfPinnedObject,
                    buf.SizeOf
                );
            var error = new Win32Exception();
            logger.LogWithError(LogLevel.Information, $"[window debug] GetProcessInformation ProcessMemoryPriority:{result} {buf.SizeOf} {buf.Target.MemoryPriority:F}", error.ToString(), Environment.CurrentManagedThreadId);
        }

        {
            using var buf = new PinnedBuffer<Kernel32.PROCESS_POWER_THROTTLING_STATE>(new()
            {
                Version = 1
            });

            //TODO error 87 
            var result =
                Kernel32.GetProcessInformation(
                    Process.GetCurrentProcess().Handle,
                    Kernel32.PROCESS_INFORMATION_CLASS.ProcessPowerThrottling,
                    buf.AddrOfPinnedObject,
                    buf.SizeOf
                );
            var error = new Win32Exception();
            logger.LogWithError(LogLevel.Information, $"[window debug] GetProcessInformation ProcessPowerThrottling:{result} {buf.SizeOf} {buf.Target.Version} {buf.Target.ControlMask} {buf.Target.StateMask}", error.ToString(), Environment.CurrentManagedThreadId);
        }
    }

    internal static void CheckDpiAwarenessContext(
        ILoggerFactory loggerFactory,
        User32.HWND hwnd
    )
    {
        var logger = loggerFactory.CreateLogger<WindowDebug>();

        {
            var context = User32.GetThreadDpiAwarenessContext();
            var awareness = User32.GetAwarenessFromDpiAwarenessContext(context);
            var dpi = User32.GetDpiFromDpiAwarenessContext(context);

            logger.LogWithHWnd(LogLevel.Trace, $"GetThreadDpiAwarenessContext [context:{context}][awareness:{awareness}][dpi:{dpi}]", hwnd, Environment.CurrentManagedThreadId);
        }

        {
            var context = User32.GetWindowDpiAwarenessContext(hwnd);
            var awareness = User32.GetAwarenessFromDpiAwarenessContext(context);
            var dpi = User32.GetDpiFromDpiAwarenessContext(context);
            var dpiForWindow = User32.GetDpiForWindow(hwnd);

            logger.LogWithHWnd(LogLevel.Trace, $"GetWindowDpiAwarenessContext [context:{context}][awareness:{awareness}][dpi:{dpi}][dpiForWindow:{dpiForWindow}]", hwnd, Environment.CurrentManagedThreadId);
        }
    }
}
