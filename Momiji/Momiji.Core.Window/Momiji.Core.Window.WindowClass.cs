using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Momiji.Internal.Window;
using Kernel32 = Momiji.Interop.Kernel32.NativeMethods;
using User32 = Momiji.Interop.User32.NativeMethods;

namespace Momiji.Core.Window;

internal interface IWindowClass : IDisposable
{
    nint ClassName { get; }

    nint HInstance { get; }

    void Initialize(
        User32.WNDCLASSEX.CS classStyle,
        string className
    );

    void CallOriginalWindowProc(User32.HWND hwnd, IWindowManager.IMessage message);
}

internal sealed partial class WindowClass : IWindowClass
{
    private bool _disposed;

    private readonly ILogger<WindowClass> _logger;
    private readonly IWindowProcedure _windowProcedure;

    private User32.WNDCLASSEX.CS _classStyle;
    private string? _className;
    private string? _classNameRegist;
    private HGlobalUnicodeString? _lpszClassNameRegist;
    private User32.WNDCLASSEX _windowClass;
    private nint _originalWndProc;

    private nint _atom;

    public nint ClassName => _windowClass.lpszClassName;

    public nint HInstance => _windowClass.hInstance;

    public WindowClass(
        ILogger<WindowClass> logger,
        IWindowProcedure windowProcedure
    )
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(windowProcedure);

        _logger = logger;
        _windowProcedure = windowProcedure;
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
        if (Interlocked.CompareExchange(ref _disposed, true, false) == true)
        {
            _logger.LogWithLine(LogLevel.Information, "already disposed", Environment.CurrentManagedThreadId);
            return;
        }

        if (disposing)
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogWithLine(LogLevel.Information, $"disposing [{_className}]", Environment.CurrentManagedThreadId);
            }
        }

        if (_atom != 0)
        {
            //TODO RegisterClassExWが失敗している状況に対応できていない
            //クローズしていないウインドウが残っていると失敗する
            var result = User32.UnregisterClassW(_atom, _windowClass.hInstance);

            if (result)
            {
                _atom = 0;
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogWithLine(
                        LogLevel.Information,
                        $"UnregisterClass [windowClass:{_windowClass}] [result:{result}]",
                        Environment.CurrentManagedThreadId);
                }
            }
            else
            {
                var error = new Win32Exception();
                _logger.LogWithError(
                    LogLevel.Error,
                    $"UnregisterClass [windowClass:{_windowClass}] [result:{result}]",
                    error.ToString(),
                    Environment.CurrentManagedThreadId);
            }
        }

        if (_lpszClassNameRegist != default)
        {
            _lpszClassNameRegist.Dispose();
        }
    }

    public void Initialize(
        User32.WNDCLASSEX.CS classStyle,
        string className
    )
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        //TODO ２回呼び出ししたらエラーにするデザインがよいか？

        _classStyle = classStyle;
        _className = className;
        _classNameRegist = $"{nameof(WindowClass)}{className}-{_classStyle:X}:{Guid.NewGuid()}";
        _lpszClassNameRegist = new(_classNameRegist, _logger);

        if (string.IsNullOrEmpty(_className))
        {
            _windowClass = new User32.WNDCLASSEX
            {
                cbSize = Marshal.SizeOf<User32.WNDCLASSEX>(),
                lpfnWndProc = _windowProcedure.FunctionPointer,
                hInstance = Kernel32.GetModuleHandleW(default),
                hbrBackground = 5 + 1, //COLOR_WINDOW
                lpszClassName = _lpszClassNameRegist.Handle
            };
        }
        else
        {
            var windowClass = new User32.WNDCLASSEX
            {
                cbSize = Marshal.SizeOf<User32.WNDCLASSEX>()
            };

            using var lpszClassName = new HGlobalUnicodeStringScope(_className, _logger);

            {
                var result = User32.GetClassInfoExW(
                    nint.Zero,
                    lpszClassName.Handle,
                    ref windowClass
                );
                if (result)
                {
                    if (_logger.IsEnabled(LogLevel.Information))
                    {
                        _logger.LogWithLine(LogLevel.Information, $"GetClassInfoExW [windowClass:{windowClass}][className:{_className}]", Environment.CurrentManagedThreadId);
                    }
                }
                else
                {
                    var error = new Win32Exception();
                    if (_logger.IsEnabled(LogLevel.Information))
                    {
                        _logger.LogWithError(LogLevel.Information, $"GetClassInfoExW [windowClass:{windowClass}][className:{_className}]", error.ToString(), Environment.CurrentManagedThreadId);
                    }
                    throw error;
                }
            }

            _originalWndProc = windowClass.lpfnWndProc;

            //スーパークラス化する
            _windowClass = windowClass with
            {
                lpfnWndProc = _windowProcedure.FunctionPointer,
                hInstance = Kernel32.GetModuleHandleW(default),
                lpszClassName = _lpszClassNameRegist.Handle
            };

            _windowClass.style &= ~User32.WNDCLASSEX.CS.GLOBALCLASS;
        }

        if (_classStyle != User32.WNDCLASSEX.CS.NONE)
        {
            //TODO csの排他設定チェック
            _windowClass.style |= _classStyle;
        }

        {
            _atom = User32.RegisterClassExW(ref _windowClass);
            if (_atom != 0)
            {
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogWithLine(LogLevel.Information, $"RegisterClass [windowClass:{_windowClass}][className:{_classNameRegist}][atom:{_atom:X}]", Environment.CurrentManagedThreadId);
                }
            }
            else
            {
                var error = new Win32Exception();
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogWithError(LogLevel.Information, $"RegisterClass [windowClass:{_windowClass}][className:{_classNameRegist}][atom:{_atom:X}]", error.ToString(), Environment.CurrentManagedThreadId);
                }
                throw error;
            }
        }
    }

    public void CallOriginalWindowProc(User32.HWND hwnd, IWindowManager.IMessage message)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var isWindowUnicode = (hwnd.Handle != User32.HWND.None.Handle) && User32.IsWindowUnicode(hwnd);

        if (_originalWndProc == default)
        {
            message.Result = isWindowUnicode
                ? User32.DefWindowProcW(hwnd, (uint)message.Msg, message.WParam, message.LParam)
                : User32.DefWindowProcA(hwnd, (uint)message.Msg, message.WParam, message.LParam)
                ;
            var lastError = Marshal.GetLastPInvokeError();
            _logger.LogWithMsgAndError(LogLevel.Trace, "DefWindowProc result", hwnd, message, lastError, Environment.CurrentManagedThreadId);
        }
        else
        {
            //スーパークラス化している場合は、オリジナルのプロシージャを実行する
            message.Result = isWindowUnicode
                ? User32.CallWindowProcW(_originalWndProc, hwnd, (uint)message.Msg, message.WParam, message.LParam)
                : User32.CallWindowProcA(_originalWndProc, hwnd, (uint)message.Msg, message.WParam, message.LParam)
                ;
            var lastError = Marshal.GetLastPInvokeError();
            _logger.LogWithMsgAndError(LogLevel.Trace, "CallWindowProc result", hwnd, message, lastError, Environment.CurrentManagedThreadId);
        }

        message.Handled = true;
    }
}
