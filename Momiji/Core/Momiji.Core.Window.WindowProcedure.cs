using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Momiji.Core.Buffer;
using Momiji.Internal.Debug;
using Momiji.Internal.Log;
using Kernel32 = Momiji.Interop.Kernel32.NativeMethods;
using User32 = Momiji.Interop.User32.NativeMethods;

namespace Momiji.Core.Window;

internal sealed class WindowProcedure : IDisposable
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private bool _disposed;

    private readonly int _uiThreadId;
    private readonly uint _nativeThreadId;

    private readonly PinnedDelegate<User32.WNDPROC> _wndProc;
    internal nint FunctionPointer => _wndProc.FunctionPointer;

    internal delegate void OnMessage(User32.HWND hwnd, IUIThread.IMessage message);
    private readonly OnMessage _onMessage;

    internal delegate void OnThreadMessage(IUIThread.IMessage message);
    private readonly OnThreadMessage _onThreadMessage;

    private readonly Stack<Exception> _wndProcExceptionStack = [];

    internal WindowProcedure(
        ILoggerFactory loggerFactory,
        OnMessage onMessage,
        OnThreadMessage onThreadMessage
    )
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _loggerFactory = loggerFactory;
        _logger = _loggerFactory.CreateLogger<WindowContext>();
        _uiThreadId = Environment.CurrentManagedThreadId;
        _nativeThreadId = Kernel32.GetCurrentThreadId();

        _onMessage = onMessage;
        _onThreadMessage = onThreadMessage;
        _wndProc = new(WndProc);

        Setup();
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
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _logger.LogWithLine(LogLevel.Information, "disposing", Environment.CurrentManagedThreadId);

            _wndProc.Dispose();
        }

        _disposed = true;
    }

    private void Setup()
    {
        WindowDebug.CheckIntegrityLevel(_loggerFactory);
        WindowDebug.CheckDesktop(_loggerFactory);
        WindowDebug.CheckGetProcessInformation(_loggerFactory);

        {
            var result = User32.IsGUIThread(true);
            if (!result)
            {
                var error = new Win32Exception();
                throw error;
            }
        }

        { //メッセージキューが無ければ作られるハズ
            var result =
                User32.GetQueueStatus(
                    0x04FF //QS_ALLINPUT
                );
            var error = new Win32Exception();
            _logger.LogWithError(LogLevel.Information, $"GetQueueStatus {result}", error.ToString(), Environment.CurrentManagedThreadId);
        }

        {
            var si = new Kernel32.STARTUPINFOW()
            {
                cb = Marshal.SizeOf<Kernel32.STARTUPINFOW>()
            };
            Kernel32.GetStartupInfoW(ref si);
            var error = new Win32Exception();
            _logger.LogWithError(LogLevel.Information, $"GetStartupInfoW [{si.dwFlags}][{si.wShowWindow}]", error.ToString(), Environment.CurrentManagedThreadId);
        }
    }

    private void ThrowIfCalledFromOtherThread()
    {
        if (_uiThreadId != Environment.CurrentManagedThreadId)
        {
            throw new InvalidOperationException($"called from invalid thread id [construct:{_uiThreadId:X}] [current:{Environment.CurrentManagedThreadId:X}]");
        }
    }

    public nint SendMessage(
        User32.HWND hwnd,
        uint nMsg,
        nint wParam,
        nint lParam
    )
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        //TODO SendMessage/SendNotifyMessage/SendMessageCallback/SendMessageTimeout の使い分け？
        _logger.LogWithWndProcParam(LogLevel.Trace, "SendMessageW", hwnd, nMsg, wParam, lParam, Environment.CurrentManagedThreadId);
        var result =
            User32.SendMessageW(
                hwnd,
                nMsg,
                wParam,
                lParam
            );
        var error = new Win32Exception();
        _logger.LogWithHWndAndError(LogLevel.Trace, $"SendMessageW result:[{result}]", hwnd, error.ToString(), Environment.CurrentManagedThreadId);

        if (error.NativeErrorCode != 0)
        {
            //UIPIに引っかかると5が返ってくる
            throw error;
        }
        return result;
    }

    public void PostMessage(
        User32.HWND hwnd,
        uint nMsg,
        nint wParam,
        nint lParam
    )
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        bool result;

        if ( 
                (hwnd.Handle == User32.HWND.None.Handle)
           &&   (_uiThreadId != Environment.CurrentManagedThreadId)
        )
        {
            _logger.LogWithWndProcParam(LogLevel.Trace, "PostThreadMessageW", hwnd, nMsg, wParam, lParam, Environment.CurrentManagedThreadId);
            result =
                User32.PostThreadMessageW(
                    _nativeThreadId,
                    nMsg,
                    wParam,
                    lParam
                );
        }
        else
        {
            _logger.LogWithWndProcParam(LogLevel.Trace, "PostMessageW", hwnd, nMsg, wParam, lParam, Environment.CurrentManagedThreadId);
            result =
                User32.PostMessageW(
                    hwnd,
                    nMsg,
                    wParam,
                    lParam
                );
        }

        var error = new Win32Exception();
        _logger.LogWithHWndAndError(LogLevel.Trace, $"PostMessageW result:[{result}]", hwnd, error.ToString(), Environment.CurrentManagedThreadId);

        if (!result)
        {
            if (error.NativeErrorCode != 0)
            {
                throw error;
            }
        }
    }

    public void DispatchMessage()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        //UIスレッドで呼び出す必要アリ
        ThrowIfCalledFromOtherThread();

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
            _logger.LogWithHWnd(LogLevel.Trace, $"MSG {msg}", msg.hwnd, Environment.CurrentManagedThreadId);

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
                    _onThreadMessage(message);
                }
                catch (Exception e)
                {
                    var error = new WndProcException("error occurred at thread message", User32.HWND.None, message.Msg, message.WParam, message.LParam, e);
                    _logger.LogWithLine(LogLevel.Trace, error, "error occurred at thread message", Environment.CurrentManagedThreadId);
                    _wndProcExceptionStack.Push(error);
                }

                continue;
            }

            {
                _logger.LogWithHWnd(LogLevel.Trace, "TranslateMessage", msg.hwnd, Environment.CurrentManagedThreadId);
                var result = User32.TranslateMessage(ref msg);
                var error = new Win32Exception();
                _logger.LogWithHWndAndError(LogLevel.Trace, $"TranslateMessage [{result:X}]", msg.hwnd, error.ToString(), Environment.CurrentManagedThreadId);
            }

            {
                var isWindowUnicode = (msg.hwnd.Handle != User32.HWND.None.Handle) && User32.IsWindowUnicode(msg.hwnd);
                _logger.LogWithHWnd(LogLevel.Trace, $"IsWindowUnicode [{isWindowUnicode}]", msg.hwnd, Environment.CurrentManagedThreadId);

                _logger.LogWithHWnd(LogLevel.Trace, "DispatchMessage", msg.hwnd, Environment.CurrentManagedThreadId);
                var result = isWindowUnicode
                            ? User32.DispatchMessageW(ref msg)
                            : User32.DispatchMessageA(ref msg)
                            ;
                var error = new Win32Exception();
                _logger.LogWithHWndAndError(LogLevel.Trace, $"DispatchMessage [{result:X}]", msg.hwnd, error.ToString(), Environment.CurrentManagedThreadId);
            }
        }
    }

    private sealed record class Message : IUIThread.IMessage
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

    private nint WndProc(User32.HWND hwnd, uint msg, nint wParam, nint lParam)
    {
        var message = new Message()
        {
            Msg = (int)msg,
            WParam = wParam,
            LParam = lParam
        };

        try
        {
            _onMessage(hwnd, message);

            if (message.Handled)
            {
                return message.Result;
            }
        }
        catch (Exception e)
        {
            var error = new WndProcException("error occurred in WndProc", hwnd, message.Msg, message.WParam, message.LParam, e);
            _logger.LogWithLine(LogLevel.Trace, error, "error occurred in WndProc", Environment.CurrentManagedThreadId);
            _wndProcExceptionStack.Push(error);

            if (message.Handled)
            {
                return message.Result;
            }
        }

        return User32.DefWindowProcW(hwnd, msg, wParam, lParam);
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
        _logger.LogWithLine(LogLevel.Trace, $"ThrowIfOccurredInWndProc [count:{_wndProcExceptionStack.Count}]", Environment.CurrentManagedThreadId);
        lock (_wndProcExceptionStack)
        {
            if (_wndProcExceptionStack.Count == 1)
            {
                ExceptionDispatchInfo.Throw(_wndProcExceptionStack.Pop());
            }
            else if (_wndProcExceptionStack.Count > 1)
            {
                var aggregateException = new AggregateException(_wndProcExceptionStack.AsEnumerable());
                _wndProcExceptionStack.Clear();
                throw aggregateException;
            }
        }
    }
}
