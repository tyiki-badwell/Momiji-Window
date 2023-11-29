using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Momiji.Core.Buffer;
using Momiji.Internal.Log;
using Kernel32 = Momiji.Interop.Kernel32.NativeMethods;
using User32 = Momiji.Interop.User32.NativeMethods;

namespace Momiji.Core.Window;

internal class WindowClass : IDisposable
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private bool _disposed;

    private User32.WNDCLASSEX _windowClass;

    internal nint ClassName => _windowClass.lpszClassName;

    internal nint HInstance => _windowClass.hInstance;

    internal WindowClass(
        ILoggerFactory loggerFactory,
        PinnedDelegate<User32.WNDPROC> wndProc,
        int cs
    )
    {
        _loggerFactory = loggerFactory;
        _logger = _loggerFactory.CreateLogger<WindowClass>();

        var className = nameof(WindowClass) + Guid.NewGuid().ToString();

        _windowClass = new User32.WNDCLASSEX
        {
            cbSize = Marshal.SizeOf<User32.WNDCLASSEX>(),
            style = cs,
            lpfnWndProc = wndProc.FunctionPointer,
            hInstance = Kernel32.GetModuleHandleW(default),
            hbrBackground = 5, //COLOR_WINDOW
            lpszClassName = Marshal.StringToHGlobalUni(className)
        };

        var atom = User32.RegisterClassExW(ref _windowClass);
        var error = new Win32Exception();
        _logger.LogWithError(LogLevel.Information, $"RegisterClass [windowClass:{_windowClass}][className:{className}][atom:{atom}]", error.ToString(), Environment.CurrentManagedThreadId);
        if (atom == 0)
        {
            throw new WindowException("RegisterClass failed", error);
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

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _logger.LogWithLine(LogLevel.Information, "disposing", Environment.CurrentManagedThreadId);
        }

        //クローズしていないウインドウが残っていると失敗する
        var result = User32.UnregisterClassW(_windowClass.lpszClassName, _windowClass.hInstance);
        var error = new Win32Exception();
        _logger.LogWithError(LogLevel.Information, $"UnregisterClass {_windowClass.lpszClassName} {result}", error.ToString(), Environment.CurrentManagedThreadId);

        Marshal.FreeHGlobal(_windowClass.lpszClassName);

        _disposed = true;
    }
}
