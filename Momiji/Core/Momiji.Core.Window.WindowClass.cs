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
        User32.WNDCLASSEX.CS cs = User32.WNDCLASSEX.CS.NONE
    )
    {
        _loggerFactory = loggerFactory;
        _logger = _loggerFactory.CreateLogger<WindowClass>();

        _windowClass = new User32.WNDCLASSEX
        {
            cbSize = Marshal.SizeOf<User32.WNDCLASSEX>(),
            style = cs,
            lpfnWndProc = wndProc.FunctionPointer,
            hInstance = Kernel32.GetModuleHandleW(default),
            lpszClassName = Marshal.StringToHGlobalUni(nameof(WindowClass) + Guid.NewGuid().ToString())
        };

        var atom = User32.RegisterClassExW(ref _windowClass);
        var error = Marshal.GetLastPInvokeError();
        _logger.LogWithLine(LogLevel.Information, $"RegisterClass {_windowClass} {atom} {error}", Environment.CurrentManagedThreadId);
        if (atom == 0)
        {
            throw new WindowException($"RegisterClass failed [{error} {Marshal.GetPInvokeErrorMessage(error)}]");
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
        var error = Marshal.GetLastPInvokeError();
        _logger.LogWithErrorId(LogLevel.Information, $"UnregisterClass {_windowClass.lpszClassName} {result}", error, Marshal.GetPInvokeErrorMessage(error), Environment.CurrentManagedThreadId);

        Marshal.FreeHGlobal(_windowClass.lpszClassName);

        _disposed = true;
    }
}
