﻿using System.ComponentModel;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Logging;
using Momiji.Core.Buffer;
using Momiji.Internal.Log;
using Momiji.Internal.Util;
using User32 = Momiji.Interop.User32.NativeMethods;

namespace Momiji.Core.Window;

internal interface IWindowProcedure : IDisposable
{
    nint FunctionPointer { get; }

    void PostMessage(
        User32.HWND hwnd,
        uint nMsg,
        nint wParam,
        nint lParam
    );

    nint SendMessage(
        User32.HWND hwnd,
        uint nMsg,
        nint wParam,
        nint lParam
    );

    User32.HWND CreateWindow(
        User32.WS_EX dwExStyle,
        nint lpszClassName,
        string windowTitle,
        User32.WS style,
        int x,
        int y,
        int width,
        int height,
        User32.HWND hwndParent,
        nint hMenu,
        nint hInst
    );

    void DispatchMessage();

}

internal sealed partial class WindowProcedure : IWindowProcedure
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private bool _disposed;

    private IUIThreadChecker UIThreadChecker { get; }

    private readonly PinnedDelegate<User32.WNDPROC> _wndProc;
    public nint FunctionPointer => _wndProc.FunctionPointer;

    internal delegate void OnWindowMessageEventHandler(User32.HWND hwnd, IWindowManager.IMessage message);
    private readonly OnWindowMessageEventHandler? _onWindowMessage;

    internal delegate void OnThreadMessageEventHandler(IWindowManager.IMessage message);
    private readonly OnThreadMessageEventHandler? _onThreadMessage;

    private readonly Stack<Exception> _wndProcExceptionStack = [];

    internal WindowProcedure(
        ILoggerFactory loggerFactory,
        IUIThreadChecker uiThreadChecker,
        OnWindowMessageEventHandler? onWindowMessage,
        OnThreadMessageEventHandler? onThreadMessage
    )
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _loggerFactory = loggerFactory;
        _logger = _loggerFactory.CreateLogger<WindowProcedure>();
        UIThreadChecker = uiThreadChecker;
        UIThreadChecker.OnInactivated += OnInactivated;

        _wndProc = new(WndProc);

        _onWindowMessage = onWindowMessage;
        _onThreadMessage = onThreadMessage;
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
            UIThreadChecker.OnInactivated -= OnInactivated;
            _wndProc.Dispose();
        }

        _disposed = true;
    }

    private void OnInactivated()
    {
        _logger.LogWithLine(LogLevel.Trace, "OnInactivated", Environment.CurrentManagedThreadId);
        //TODO メッセージキューを吐き出しきる
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

            if (nMsg == 0x0010 && error.NativeErrorCode == 1400)
            {
                //直接呼出しで、WM_CLOSEで1400が返ってくるのはOKにする
                //TODO 直接呼出ししたことの判定方法　CLOSEのキャンセルもある　そもそもココで判定したくない
            }
            else
            {
                throw error;
            }
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
           &&   (!UIThreadChecker.IsActivatedThread)
        )
        {
            _logger.LogWithWndProcParam(LogLevel.Trace, "PostThreadMessageW", hwnd, nMsg, wParam, lParam, Environment.CurrentManagedThreadId);
            result =
                User32.PostThreadMessageW(
                    UIThreadChecker.NativeThreadId,
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

    internal readonly ref struct SwitchThreadDpiHostingBehaviorMixedRAII
    {
        private readonly ILogger _logger;
        private readonly User32.DPI_HOSTING_BEHAVIOR _oldBehavior;

        public SwitchThreadDpiHostingBehaviorMixedRAII(
            ILogger logger
        )
        {
            _logger = logger;
            _oldBehavior = User32.SetThreadDpiHostingBehavior(User32.DPI_HOSTING_BEHAVIOR.MIXED);
            _logger.LogWithLine(LogLevel.Trace, $"ON SetThreadDpiHostingBehavior [{_oldBehavior} -> MIXED]", Environment.CurrentManagedThreadId);
        }

        public void Dispose()
        {
            if (_oldBehavior != User32.DPI_HOSTING_BEHAVIOR.INVALID)
            {
                var oldBehavior = User32.SetThreadDpiHostingBehavior(_oldBehavior);
                _logger.LogWithLine(LogLevel.Trace, $"OFF SetThreadDpiHostingBehavior [{oldBehavior} -> {_oldBehavior}]", Environment.CurrentManagedThreadId);
            }
        }
    }

    public User32.HWND CreateWindow(
        User32.WS_EX dwExStyle,
        nint lpszClassName,
        string windowTitle,
        User32.WS style,
        int x,
        int y,
        int width,
        int height,
        User32.HWND hwndParent,
        nint hMenu,
        nint hInst
    )
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        //UIスレッドで呼び出す必要アリ
        UIThreadChecker.ThrowIfCalledFromOtherThread();

        _logger.LogWithLine(LogLevel.Trace, $"CreateWindowEx {dwExStyle} {style}", Environment.CurrentManagedThreadId);

        //DPI awareness 
        using var switchBehavior = new SwitchThreadDpiHostingBehaviorMixedRAII(_logger);

        //ウインドウタイトル
        using var lpszWindowName = new StringToHGlobalUniRAII(windowTitle, _logger);

        var hWindow =
            User32.CreateWindowExW(
                dwExStyle,
                lpszClassName,
                lpszWindowName.Handle,
                style,
                x,
                y,
                width,
                height,
                hwndParent,
                hMenu,
                hInst,
                nint.Zero
            );
        var error = new Win32Exception();
        _logger.LogWithHWndAndError(LogLevel.Information, "CreateWindowEx result", hWindow, error.ToString(), Environment.CurrentManagedThreadId);
        if (hWindow.Handle == User32.HWND.None.Handle)
        {
            ThrowIfOccurredInWndProc();
            throw error;
        }

        var behavior = User32.GetWindowDpiHostingBehavior(hWindow);
        _logger.LogWithLine(LogLevel.Trace, $"GetWindowDpiHostingBehavior {behavior}", Environment.CurrentManagedThreadId);

        return hWindow;
    }

    public void DispatchMessage()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        //UIスレッドで呼び出す必要アリ
        UIThreadChecker.ThrowIfCalledFromOtherThread();

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
                    _onThreadMessage?.Invoke(message);
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
                var error = new Win32Exception();
                _logger.LogWithHWndAndError(LogLevel.Trace, $"TranslateMessage [{result:X}]", msg.hwnd, error.ToString(), Environment.CurrentManagedThreadId);
            }

            {
                var isWindowUnicode = User32.IsWindowUnicode(msg.hwnd);
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
            _onWindowMessage?.Invoke(hwnd, message);
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

    internal void ThrowIfOccurredInWndProc()
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
