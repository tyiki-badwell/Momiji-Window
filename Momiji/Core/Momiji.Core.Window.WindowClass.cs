using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Momiji.Internal.Log;
using Momiji.Internal.Util;
using Kernel32 = Momiji.Interop.Kernel32.NativeMethods;
using User32 = Momiji.Interop.User32.NativeMethods;

namespace Momiji.Core.Window;

internal sealed class WindowClass : IDisposable
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private bool _disposed;

    private readonly string _className;
    private readonly StringToHGlobalUni _lpszClassName;
    private User32.WNDCLASSEX _windowClass;
    private readonly nint _originalWndProc;

    internal nint ClassName => _windowClass.lpszClassName;

    internal nint HInstance => _windowClass.hInstance;

    internal WindowClass(
        ILoggerFactory loggerFactory,
        nint wndProcFunctionPointer,
        User32.WNDCLASSEX.CS classStyle,
        string className
    )
    {
        _loggerFactory = loggerFactory;
        _logger = _loggerFactory.CreateLogger<WindowClass>();

        _className = nameof(WindowClass) + className + Guid.NewGuid().ToString();
        _lpszClassName = new(_className, _logger);

        //TODO csの排他設定チェック

        if (className == "")
        {
            _windowClass = new User32.WNDCLASSEX
            {
                cbSize = Marshal.SizeOf<User32.WNDCLASSEX>(),
                lpfnWndProc = wndProcFunctionPointer,
                hInstance = Kernel32.GetModuleHandleW(default),
                hbrBackground = 5, //COLOR_WINDOW
                lpszClassName = _lpszClassName.Handle
            };
        }
        else
        {
            var windowClass = new User32.WNDCLASSEX
            {
                cbSize = Marshal.SizeOf<User32.WNDCLASSEX>()
            };

            using var lpszClassName = new StringToHGlobalUniRAII(className, _logger);

            {
                var result = User32.GetClassInfoExW(
                    nint.Zero,
                    lpszClassName.Handle,
                    ref windowClass
                );
                var error = new Win32Exception();
                _logger.LogWithError(LogLevel.Information, $"GetClassInfoExW [windowClass:{windowClass}][className:{className}]", error.ToString(), Environment.CurrentManagedThreadId);
                if (!result)
                {
                    throw error;
                }
            }

            _originalWndProc = windowClass.lpfnWndProc;

            //スーパークラス化する
            _windowClass = windowClass with
            {
                lpfnWndProc = wndProcFunctionPointer,
                hInstance = Kernel32.GetModuleHandleW(default),
                lpszClassName = _lpszClassName.Handle
            };

            _windowClass.style &= ~User32.WNDCLASSEX.CS.GLOBALCLASS;
        }

        if (classStyle != User32.WNDCLASSEX.CS.NONE)
        {
            _windowClass.style |= classStyle;
        }

        {
            var atom = User32.RegisterClassExW(ref _windowClass);
            var error = new Win32Exception();
            _logger.LogWithError(LogLevel.Information, $"RegisterClass [windowClass:{_windowClass}][className:{_className}][atom:{atom:X}]", error.ToString(), Environment.CurrentManagedThreadId);
            if (atom == 0)
            {
                throw error;
            }
        }
    }

    ~WindowClass()
    {
        Dispose(false);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _logger.LogWithLine(LogLevel.Information, $"disposing [{_className}]", Environment.CurrentManagedThreadId);
        }

        {
            //クローズしていないウインドウが残っていると失敗する
            var result = User32.UnregisterClassW(_windowClass.lpszClassName, _windowClass.hInstance);
            var error = new Win32Exception();
            _logger.LogWithError(LogLevel.Information, $"UnregisterClass [windowClass:{_windowClass}] [result:{result}]", error.ToString(), Environment.CurrentManagedThreadId);
        }

        if (_lpszClassName != default)
        {
            _lpszClassName.Dispose();
        }

        _disposed = true;
    }

    internal void CallOriginalWindowProc(User32.HWND hwnd, IUIThread.IMessage message)
    {
        var isWindowUnicode = (hwnd.Handle != User32.HWND.None.Handle) && User32.IsWindowUnicode(hwnd);

        if (_originalWndProc == default)
        {
            message.Result = isWindowUnicode
                ? User32.DefWindowProcW(hwnd, (uint)message.Msg, message.WParam, message.LParam)
                : User32.DefWindowProcA(hwnd, (uint)message.Msg, message.WParam, message.LParam)
                ;
            var error = new Win32Exception();
            _logger.LogWithMsgAndError(LogLevel.Trace, "DefWindowProc result", hwnd, message, error.ToString(), Environment.CurrentManagedThreadId);
        }
        else
        {
            _logger.LogWithMsg(LogLevel.Trace, "CallWindowProc", hwnd, message, Environment.CurrentManagedThreadId);

            message.Result = isWindowUnicode
                ? User32.CallWindowProcW(_originalWndProc, hwnd, (uint)message.Msg, message.WParam, message.LParam)
                : User32.CallWindowProcA(_originalWndProc, hwnd, (uint)message.Msg, message.WParam, message.LParam)
                ;
            var error = new Win32Exception();
            _logger.LogWithMsgAndError(LogLevel.Trace, "CallWindowProc result", hwnd, message, error.ToString(), Environment.CurrentManagedThreadId);
        }
    }
}
