using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Momiji.Core.Buffer;
using Momiji.Internal.Window;
using Comctl32 = Momiji.Interop.Comctl32.NativeMethods;
using User32 = Momiji.Interop.User32.NativeMethods;

namespace Momiji.Core.Window;

internal interface IWindowProcedure : IDisposable
{
    nint FunctionPointer { get; }
    nint SubFunctionPointer { get; }

    void DispatchMessage();

    void ThrowIfOccurredInWndProc();

    internal delegate void WindowMessageEventHandler(User32.HWND hwnd, IWindowManager.IMessage message);
    event WindowMessageEventHandler? OnWindowMessage;

    internal delegate void OnThreadMessageEventHandler(IWindowManager.IMessage message);
    event OnThreadMessageEventHandler? OnThreadMessage;
}

internal sealed partial class WindowProcedure : IWindowProcedure
{
    private readonly ILogger<WindowProcedure> _logger;
    private bool _disposed;

    private readonly IUIThreadActivator _uiThreadActivator;

    private readonly PinnedDelegate<User32.WNDPROC> _wndProc;
    public nint FunctionPointer => _wndProc.FunctionPointer;

    private readonly PinnedDelegate<Comctl32.SUBCLASSPROC> _subWndProc;
    public nint SubFunctionPointer => _subWndProc.FunctionPointer;

    public event IWindowProcedure.WindowMessageEventHandler? OnWindowMessage;

    public event IWindowProcedure.OnThreadMessageEventHandler? OnThreadMessage;


    private readonly Stack<Exception> _wndProcExceptionStack = [];

    public WindowProcedure(
        ILogger<WindowProcedure> logger,
        IUIThreadActivator uiThreadActivator
    )
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(uiThreadActivator);

        _logger = logger;
        _uiThreadActivator = uiThreadActivator;
        _uiThreadActivator.OnInactivated += OnInactivated;
        
        _wndProc = new(WndProc);
        _subWndProc = new(SubWndProc);
    }

    ~WindowProcedure()
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
            _logger.LogWithLine(LogLevel.Information, "disposing", Environment.CurrentManagedThreadId);
            _uiThreadActivator.OnInactivated -= OnInactivated;
            _wndProc.Dispose();
            _subWndProc.Dispose();
        }
    }

    private void OnInactivated()
    {
        _logger.LogWithLine(LogLevel.Trace, "OnInactivated", Environment.CurrentManagedThreadId);
        //TODO メッセージキューを吐き出しきる
    }

    public void DispatchMessage()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        //UIスレッドで呼び出す必要アリ
        _uiThreadActivator.ThrowIfCalledFromOtherThread();

        var msg = new User32.MSG();

        while (true)
        {
            _logger.LogWithLine(LogLevel.Trace, "PeekMessage", Environment.CurrentManagedThreadId);
            if (!User32.PeekMessageW(
                    ref msg,
                    User32.HWND.None,
                    0,
                    0,
                    0x0001 // PM_REMOVE
            ))
            {
                _logger.LogWithLine(LogLevel.Trace, "PeekMessage NONE", Environment.CurrentManagedThreadId);
                return;
            }
            if (_logger.IsEnabled(LogLevel.Trace))
            {
                _logger.LogWithHWnd(LogLevel.Trace, $"MSG {msg}", msg.hwnd, Environment.CurrentManagedThreadId);
            }

            //TODO フックがあればココで流す


            if (msg.hwnd.Handle == User32.HWND.None.Handle)
            {
                //msg.hwnd がnullのとき(= thread宛メッセージ)は、↓以降はバイパスしてthread宛メッセージを処理する
                _logger.LogWithHWnd(LogLevel.Trace, "hwnd is none", msg.hwnd, Environment.CurrentManagedThreadId);

                var message = new Message()
                {
                    Msg = (int)msg.message,
                    WParam = msg.wParam,
                    LParam = msg.lParam
                };

                try
                {
                    OnThreadMessage?.Invoke(message);
                }
                catch (Exception e)
                {
                    var error = new WndProcException("error occurred at thread message", User32.HWND.None, message.Msg, message.WParam, message.LParam, e);
                    _logger.LogWithLine(LogLevel.Trace, error, "error occurred at thread message", Environment.CurrentManagedThreadId);
                    _wndProcExceptionStack.Push(error);
                }

                continue;
            }


            //TODO Windowに直接配信するのもアリ？


            {
                _logger.LogWithHWnd(LogLevel.Trace, "TranslateMessage", msg.hwnd, Environment.CurrentManagedThreadId);
                var result = User32.TranslateMessage(ref msg);
                var lastError = Marshal.GetLastPInvokeError();
                if (_logger.IsEnabled(LogLevel.Trace))
                {
                    _logger.LogWithHWndAndError(LogLevel.Trace, $"TranslateMessage [{result:X}]", msg.hwnd, lastError, Environment.CurrentManagedThreadId);
                }
            }

            {
                var isWindowUnicode = User32.IsWindowUnicode(msg.hwnd);
                if (_logger.IsEnabled(LogLevel.Trace))
                {
                    _logger.LogWithHWnd(LogLevel.Trace, $"IsWindowUnicode [{isWindowUnicode}]", msg.hwnd, Environment.CurrentManagedThreadId);
                }

                _logger.LogWithHWnd(LogLevel.Trace, "DispatchMessage", msg.hwnd, Environment.CurrentManagedThreadId);
                var result = isWindowUnicode
                            ? User32.DispatchMessageW(ref msg)
                            : User32.DispatchMessageA(ref msg)
                            ;
                var lastError = Marshal.GetLastPInvokeError();
                if (_logger.IsEnabled(LogLevel.Trace))
                {
                    _logger.LogWithHWndAndError(LogLevel.Trace, $"DispatchMessage [{result:X}]", msg.hwnd, lastError, Environment.CurrentManagedThreadId);
                }
            }
        }
    }

    private sealed record class Message : IWindowManager.IMessage
    {
        public required int Msg
        {
            get; init;
        }
        public required nint WParam
        {
            get; init;
        }
        public required nint LParam
        {
            get; init;
        }

        private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;

        public int OwnerThreadId => _ownerThreadId;

        private nint _result;

        public nint Result
        {
            get => _result;
            set
            {
                if (OwnerThreadId != Environment.CurrentManagedThreadId)
                {
                    throw new InvalidOperationException("called from other thread");
                }

                _result = value;
            }
        }

        public bool Handled
        {
            get; set;
        }
        public override string ToString()
        {
            return $"[Msg:{Msg:X}][WParam:{WParam:X}][LParam:{LParam:X}][OwnerThreadId:{OwnerThreadId:X}][Result:{Result:X}][Handled:{Handled}]";
        }
    }

    private nint WndProc(
        User32.HWND hwnd, 
        uint msg, 
        nint wParam, 
        nint lParam
    )
    {
        var message = new Message()
        {
            Msg = (int)msg,
            WParam = wParam,
            LParam = lParam
        };

        try
        {
            OnWindowMessage?.Invoke(hwnd, message);
        }
        catch (Exception e)
        {
            var error = new WndProcException("error occurred in WndProc", hwnd, message.Msg, message.WParam, message.LParam, e);
            _logger.LogWithLine(LogLevel.Trace, error, "error occurred in WndProc", Environment.CurrentManagedThreadId);
            _wndProcExceptionStack.Push(error);
        }

        if (message.Handled)
        {
            return message.Result;
        }
 
        return User32.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private nint SubWndProc(
        User32.HWND hwnd,
        uint msg,
        nint wParam,
        nint lParam,
        nuint uIdSubclass,
        nint dwRefData
    )
    {
        var message = new Message()
        {
            Msg = (int)msg,
            WParam = wParam,
            LParam = lParam
        };

        try
        {
            OnWindowMessage?.Invoke(hwnd, message);
        }
        catch (Exception e)
        {
            var error = new WndProcException("error occurred in WndProc", hwnd, message.Msg, message.WParam, message.LParam, e);
            _logger.LogWithLine(LogLevel.Trace, error, "error occurred in WndProc", Environment.CurrentManagedThreadId);
            _wndProcExceptionStack.Push(error);
        }
        return message.Result;
    }

    private sealed class WndProcException(
        string message,
        User32.HWND hwnd,
        int msg,
        nint wParam,
        nint lParam,
        Exception innerException
    ) : Exception(message, innerException)
    {
        public readonly User32.HWND Hwnd = hwnd;
        public readonly int Msg = msg;
        public readonly nint WParam = wParam;
        public readonly nint LParam = lParam;
    }

    public void ThrowIfOccurredInWndProc()
    {
        //TODO 何かのコンテキストに移す
        if (_logger.IsEnabled(LogLevel.Trace))
        {
            _logger.LogWithLine(LogLevel.Trace, $"ThrowIfOccurredInWndProc [count:{_wndProcExceptionStack.Count}]", Environment.CurrentManagedThreadId);
        }
        lock (_wndProcExceptionStack)
        {
            if (_wndProcExceptionStack.Count == 1)
            {
                ExceptionDispatchInfo.Throw(_wndProcExceptionStack.Pop());
            }
            else if (_wndProcExceptionStack.Count > 1)
            {
                var aggregateException = new AggregateException(_wndProcExceptionStack);
                _wndProcExceptionStack.Clear();
                throw aggregateException;
            }
        }
    }
}
