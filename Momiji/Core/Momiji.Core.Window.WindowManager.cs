using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Momiji.Internal.Debug;
using Momiji.Internal.Log;
using Momiji.Internal.Util;
using Kernel32 = Momiji.Interop.Kernel32.NativeMethods;
using User32 = Momiji.Interop.User32.NativeMethods;

namespace Momiji.Core.Window;

internal sealed class WindowManager : IWindowManager, IDisposable
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private bool _disposed;

    private IUIThreadChecker UIThreadChecker { get; }

    private IDispatcherQueue DispatcherQueue { get; }

    internal IWindowClassManager WindowClassManager { get; }
    internal IWindowProcedure WindowProcedure { get; }
    private WindowManagerSynchronizationContext WindowContextSynchronizationContext { get; }

    public bool IsEmpty => _windowMap.IsEmpty;

    private readonly ConcurrentDictionary<User32.HWND, NativeWindowBase> _windowMap = [];
    private readonly Stack<NativeWindowBase> _windowStack = [];

    private readonly ConcurrentDictionary<int, WeakReference<NativeWindowBase>> _childIdMap = [];
    private int childId = 0;

    private readonly IUIThreadFactory.Param _param;

    internal WindowManager(
        ILoggerFactory loggerFactory,
        IConfiguration configuration,
        IUIThreadChecker uiThreadChecker,
        IDispatcherQueue dispatcherQueue
    )
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _loggerFactory = loggerFactory;
        _logger = _loggerFactory.CreateLogger<WindowManager>();

        UIThreadChecker = uiThreadChecker;
        UIThreadChecker.OnInactivated += OnInactivated;

        DispatcherQueue = dispatcherQueue;
        WindowContextSynchronizationContext = new(_loggerFactory, DispatcherQueue);

        WindowProcedure = new WindowProcedure(_loggerFactory, uiThreadChecker, OnMessage, OnThreadMessage);

        WindowClassManager = new WindowClassManager(_loggerFactory, WindowProcedure);

        _param = new IUIThreadFactory.Param();
        configuration.GetSection($"{typeof(UIThread).FullName}").Bind(_param);
    }

    ~WindowManager()
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

            ((WindowClassManager)WindowClassManager).Dispose();

            ((WindowProcedure)WindowProcedure).Dispose();
        }

        _disposed = true;
    }

    internal readonly ref struct SwitchThreadDpiAwarenessContextPerMonitorAwareV2RAII
    {
        private readonly ILogger _logger;
        private readonly NativeWindowBase _window;

        private readonly User32.DPI_AWARENESS_CONTEXT _oldContext;

        public SwitchThreadDpiAwarenessContextPerMonitorAwareV2RAII(
            NativeWindowBase window,
            ILogger logger
        )
        {
            _window = window;
            _logger = logger;

            _oldContext = User32.SetThreadDpiAwarenessContext(User32.DPI_AWARENESS_CONTEXT.PER_MONITOR_AWARE_V2);

            var error = new Win32Exception();
            _logger.LogWithHWndAndError(LogLevel.Trace, $"ON SetThreadDpiAwarenessContext [{_oldContext:X} -> PER_MONITOR_AWARE_V2]", _window.HWND, error.ToString(), Environment.CurrentManagedThreadId);
        }

        public void Dispose()
        {
            if (_oldContext.Handle != 0)
            {
                var oldContext = User32.SetThreadDpiAwarenessContext(_oldContext);

                var error = new Win32Exception();
                _logger.LogWithHWndAndError(LogLevel.Trace, $"OFF SetThreadDpiAwarenessContext [{oldContext} -> {_oldContext}]", _window.HWND, error.ToString(), Environment.CurrentManagedThreadId);
            }
        }
    }

    private TResult InvokeWithContext<TResult>(Func<IWindow, TResult> func, NativeWindowBase window)
    {
        //TODO スレッドセーフになっているか要確認(再入しても問題ないなら気にしない)
        //TODO 何かのコンテキストに移す
        _logger.LogWithLine(LogLevel.Trace, $"Push [handle:{window.Handle:X}] [stack:{_windowStack.Count}] [{SynchronizationContext.Current}]", Environment.CurrentManagedThreadId);
        _windowStack.Push(window);

        try
        {
            //TODO CreateWindowだけ囲えば良さそうだが、一旦全部囲ってしまう
            using var switchContext = new SwitchThreadDpiAwarenessContextPerMonitorAwareV2RAII(window, _logger);

            _logger.LogWithLine(LogLevel.Trace, "Invoke start", Environment.CurrentManagedThreadId);
            var result = func(window);
            _logger.LogWithLine(LogLevel.Trace, "Invoke end", Environment.CurrentManagedThreadId);
            return result;
        }
        finally
        {
            var result = _windowStack.Pop();
            _logger.LogWithLine(LogLevel.Trace, $"Pop [handle:{window.Handle:X}] [stack:{_windowStack.Count}] [{SynchronizationContext.Current}]", Environment.CurrentManagedThreadId);
            Debug.Assert(result == window);
        }
    }

    internal int GenerateChildId(NativeWindow window)
    {
        var id = Interlocked.Increment(ref childId);
        _childIdMap.TryAdd(id, new(window));
        return id;
    }

    public void CloseAll()
    {
        //TODO SendMessageに行くのでUIスレッドで呼び出す必要は無いが、統一すべき？
        foreach (var item in _windowMap)
        {
            if (!_windowMap.ContainsKey(item.Key))
            {
                //ループ中に削除されていた場合はスキップ
                continue;
            }

            try
            {
                //TODO 親ウインドウにだけ実行する(順序に依っては子ウインドウのcloseで1400になる)
                item.Value.Close();
            }
            catch (Exception e)
            {
                _logger.LogWithLine(LogLevel.Error, e, "close failed.", Environment.CurrentManagedThreadId);
            }
        }
    }

    private void OnInactivated()
    {
        _logger.LogWithLine(LogLevel.Trace, "OnInactivated", Environment.CurrentManagedThreadId);
        ForceClose();
    }

    private void ForceClose()
    {
        if (_windowMap.IsEmpty)
        {
            return;
        }

        _logger.LogWithLine(LogLevel.Warning, $"window left {_windowMap.Count}", Environment.CurrentManagedThreadId);
        PrintWindowMap();

        foreach (var item in _windowMap)
        {
            //TODO UIThreadで実行しないとエラーになってる
            item.Value.Dispose();
        }

        //TODO mapが空になるのを待つ？
        _logger.LogWithLine(LogLevel.Warning, $"window left {_windowMap.Count}", Environment.CurrentManagedThreadId);

        _windowMap.Clear();
    }

    public IWindow CreateWindow(
        string windowTitle,
        IWindow? parent,
        string className,
        IWindowManager.OnMessage? onMessage,
        IWindowManager.OnMessage? onMessageAfter
    )
    {
        //UIスレッドで呼び出す必要アリ
        UIThreadChecker.ThrowIfCalledFromOtherThread();

        var classStyle = (className == string.Empty)
                ? (User32.WNDCLASSEX.CS)_param.CS
                : User32.WNDCLASSEX.CS.NONE
                ;

        var window =
            new NativeWindow(
                _loggerFactory,
                this,
                className,
                classStyle,
                windowTitle,
                (parent as NativeWindow),
                onMessage,
                onMessageAfter
            );

        return window;
    }

    internal async ValueTask<TResult> DispatchAsync<TResult>(Func<IWindow, TResult> item, NativeWindowBase window)
    {
        _logger.LogWithLine(LogLevel.Trace, "DispatchAsync", Environment.CurrentManagedThreadId);

        TResult func()
        {
            return InvokeWithContext(item, window);
        }

        return await DispatcherQueue.DispatchAsync(func);
    }

    private void OnThreadMessage(IWindowManager.IMessage message)
    {
        _logger.LogWithMsg(LogLevel.Trace, "OnThreadMessage", User32.HWND.None, message, Environment.CurrentManagedThreadId);

        using var switchContext = new SwitchSynchronizationContextRAII(WindowContextSynchronizationContext, _logger);

        //TODO スレッドメッセージ用の処理

    }

    private void OnMessage(User32.HWND hwnd, IWindowManager.IMessage message)
    {
        _logger.LogWithMsg(LogLevel.Trace, "OnMessage", hwnd, message, Environment.CurrentManagedThreadId);

        using var switchContext = new SwitchSynchronizationContextRAII(WindowContextSynchronizationContext, _logger);

        try
        {
            WindowDebug.CheckDpiAwarenessContext(_logger, hwnd);

            WndProcBefore(hwnd, message);
            if (message.Handled)
            {
                return;
            }

            if (TryGetWindow(hwnd, out var window))
            {
                //ウインドウに流す
                window.OnMessage(message);

                WndProcAfter(window, message);
            }
            else
            {
                _logger.LogWithMsg(LogLevel.Trace, "unkown window handle", hwnd, message, Environment.CurrentManagedThreadId);
                CallOriginalWindowProc(hwnd, message);
            }
        }
        catch (Exception)
        {
            //msgによってreturnを分ける
            WndProcError(hwnd, message);
            throw;
        }
    }

    private void WndProcBefore(User32.HWND hwnd, IWindowManager.IMessage message)
    {
        switch (message.Msg)
        {
            case 0x0081://WM_NCCREATE
                _logger.LogWithHWnd(LogLevel.Trace, "WM_NCCREATE Before", hwnd, Environment.CurrentManagedThreadId);
                OnWM_NCCREATE_Before(hwnd);
                break;

            case 0x0210://WM_PARENTNOTIFY
                _logger.LogWithHWnd(LogLevel.Trace, "WM_PARENTNOTIFY Before", hwnd, Environment.CurrentManagedThreadId);
                OnWM_PARENTNOTIFY_Before(hwnd, message.WParam, message.LParam);
                break;
        }
    }

    /*
    private void OnWM_COMMAND(User32.HWND hwnd, nint wParam, nint lParam)
    {
        //WParam 上位ワード　0：メニュー／1：アクセラレータ／その他：ボタン識別子　下位ワード　識別子
        //LParam ウインドウハンドル
        var wParamHi = (wParam & 0xFFFF0000) >> 16;
        var childId = (int)(wParam & 0xFFFF);
        var childHWnd = (User32.HWND)lParam;
        _logger.LogWithHWnd(LogLevel.Trace, $"WM_COMMAND [wParamHi:{wParamHi:X}][childId:{childId}] [childHWnd:{childHWnd}]", hwnd, Environment.CurrentManagedThreadId);

        if (_childIdMap.TryGetValue(childId, out var window))
        {
            //管理下にあるコントロール

            if (_windowMap.TryGetValue(childHWnd, out var window2))
            {
                //既に管理対象のwindowだった
                _logger.LogWithHWnd(LogLevel.Trace, $"is managed window childHWnd:[{childHWnd}][{window2.Handle:X}]", hwnd, Environment.CurrentManagedThreadId);
            }
            else
            {
                //TODO windowに、HWNDのアサインとWndProcの差し替えを設ける

                //最速でHWNDを受け取る
                window._hWindow = childHWnd;
                if (_windowMap.TryAdd(childHWnd, window))
                {
                    _logger.LogWithHWnd(LogLevel.Information, "add window map", childHWnd, Environment.CurrentManagedThreadId);
                }
                else
                {
                    throw new WindowException($"failed add window map hwnd:[{childHWnd}]");
                }

                BindWndProc(hwnd, childHWnd);
            }
        }

    }
    */

    private void OnWM_PARENTNOTIFY_Before(User32.HWND hwnd, nint wParam, nint lParam)
    {
        //WParam 子のウインドウメッセージ
        //LParam 下位ワード　X座標／上位ワード　Y座標

        var msg = wParam & 0xFFFF;
        var childId = (wParam & 0xFFFF0000) >> 16;
        _logger.LogWithHWnd(LogLevel.Trace, $"WM_PARENTNOTIFY [msg:{msg:X}][childId:{childId}] {wParam:X}", hwnd, Environment.CurrentManagedThreadId);

        switch (msg)
        {
            case 0x0001: //WM_CREATE
                {
                    _logger.LogWithHWnd(LogLevel.Trace, $"WM_PARENTNOTIFY WM_CREATE {wParam:X}", hwnd, Environment.CurrentManagedThreadId);
                    var childHWnd = (User32.HWND)lParam;

                    {
                        var currentThreadId = Kernel32.GetCurrentThreadId();
                        var childWindowThreadId = User32.GetWindowThreadProcessId(childHWnd, out var processId);
                        _logger.LogWithHWnd(LogLevel.Trace, $"GetWindowThreadProcessId [childHWnd:{childHWnd}] [childWindowThreadId:{childWindowThreadId:X}] [processId:{processId:X}] / current thread id:[{currentThreadId:X}]", hwnd, Environment.CurrentManagedThreadId);

                        if (currentThreadId != childWindowThreadId)
                        {
                            _logger.LogWithHWnd(LogLevel.Trace, "made by other thread", hwnd, Environment.CurrentManagedThreadId);

                            //TODO 異なるスレッドで作成したウインドウが子ウインドウになった場合の制御
                            // UIThread経由で作ったWindow
                            // 　→ wndprocを差し替えても、windowを積んだstackは元のスレッドにあるので括り付けられない
                            // 　→ 元のスレッドのwndprocが動くわけでもないので、括り付けられないのは変わらない
                            // _windowMapに入れることにするか？

                        }
                    }

                    if (FindWindow(childHWnd, out var childWindow))
                    {
                        //既に管理対象のwindowだった
                        _logger.LogWithHWnd(LogLevel.Trace, $"is managed window childHWnd:[{childHWnd}][{childWindow.Handle:X}]", hwnd, Environment.CurrentManagedThreadId);
                    }
                    else
                    {
                        Add(new SubClassNativeWindow(_loggerFactory, this, childHWnd));
                    }

                    break;
                }
            case 0x0002: //WM_DESTROY
                {
                    _logger.LogWithHWnd(LogLevel.Trace, $"WM_PARENTNOTIFY WM_DESTROY {wParam:X}", hwnd, Environment.CurrentManagedThreadId);
                    var childHWnd = (User32.HWND)lParam;

                    if (FindWindow(childHWnd, out var childWindow))
                    {
                        //既に管理対象のwindowだった
                        _logger.LogWithHWnd(LogLevel.Trace, $"is managed window childHWnd:[{childHWnd}][{childWindow.Handle:X}]", hwnd, Environment.CurrentManagedThreadId);
                        if (childWindow is SubClassNativeWindow subclass)
                        {
                            subclass.Dispose();
                            Remove(subclass);
                        }
                    }
                    else
                    {
                        //UnbindWndProc(hwnd, childHWnd);
                    }
                    break;
                }
        }
    }

    private void OnWM_NCCREATE_Before(User32.HWND hwnd)
    {
        //TODO トップレベルウインドウだったときのみ呼び出す
        if (!User32.EnableNonClientDpiScaling(hwnd))
        {
            var error = new Win32Exception();
            _logger.LogWithHWndAndError(LogLevel.Error, "EnableNonClientDpiScaling failed", hwnd, error.ToString(), Environment.CurrentManagedThreadId);
        }
    }

    private void WndProcError(User32.HWND hwnd, IWindowManager.IMessage message)
    {
        _logger.LogWithHWnd(LogLevel.Trace, $"WndProcError [{SynchronizationContext.Current}]", hwnd, Environment.CurrentManagedThreadId);

        switch (message.Msg)
        {
            case 0x0001://WM_CREATE
                _logger.LogWithHWnd(LogLevel.Error, "WM_CREATE error", hwnd, Environment.CurrentManagedThreadId);
                message.Result = -1;
                message.Handled = true;
                break;

            case 0x0081://WM_NCCREATE
                _logger.LogWithHWnd(LogLevel.Error, "WM_NCCREATE error", hwnd, Environment.CurrentManagedThreadId);
                message.Result = -1;
                message.Handled = true;
                break;
        }
    }

    private void WndProcAfter(NativeWindowBase window, IWindowManager.IMessage message)
    {
        _logger.LogWithHWnd(LogLevel.Trace, $"WndProcAfter [{SynchronizationContext.Current}]", window.HWND, Environment.CurrentManagedThreadId);

        switch (message.Msg)
        {
            case 0x0081://WM_NCCREATE
                _logger.LogWithHWnd(LogLevel.Trace, "WM_NCCREATE After", window.HWND, Environment.CurrentManagedThreadId);
                OnWM_NCCREATE_After(window);
                break;

            case 0x0082://WM_NCDESTROY
                _logger.LogWithHWnd(LogLevel.Trace, "WM_NCDESTROY After", window.HWND, Environment.CurrentManagedThreadId);
                OnWM_NCDESTROY_After(window);
                break;
        }
    }

    private void OnWM_NCCREATE_After(NativeWindowBase window)
    {
        //作成に成功してからWindowMapに入れる
        Add(window);
    }

    private void OnWM_NCDESTROY_After(NativeWindowBase window)
    {
        Remove(window);
    }

    private void CallOriginalWindowProc(User32.HWND hwnd, IWindowManager.IMessage message)
    {
        var isWindowUnicode = User32.IsWindowUnicode(hwnd);

        _logger.LogWithMsg(LogLevel.Trace, "DefWindowProc", hwnd, message, Environment.CurrentManagedThreadId);
        message.Result = isWindowUnicode
            ? User32.DefWindowProcW(hwnd, (uint)message.Msg, message.WParam, message.LParam)
            : User32.DefWindowProcA(hwnd, (uint)message.Msg, message.WParam, message.LParam)
            ;
        message.Handled = true;
        var error = new Win32Exception();
        _logger.LogWithMsgAndError(LogLevel.Trace, "DefWindowProc result", hwnd, message, error.ToString(), Environment.CurrentManagedThreadId);
    }

    [Conditional("DEBUG")]
    private void PrintWindowStack()
    {
        _logger.LogTrace("================================= PrintWindowStack start");
        _logger.LogWithLine(LogLevel.Trace, $"window stack {string.Join("<-", _windowStack.Select((window, idx) => $"[hash:{window.GetHashCode():X}][window:{window}]"))}", Environment.CurrentManagedThreadId);
        _logger.LogTrace("================================= PrintWindowStack end");
    }

    [Conditional("DEBUG")]
    private void PrintWindowMap()
    {
        _logger.LogTrace("================================= PrintWindowMap start");

        foreach (var item in _windowMap)
        {
            _logger.LogWithHWnd(LogLevel.Trace, $"window map [window:{item.Value}]", item.Key, Environment.CurrentManagedThreadId);
        }

        _logger.LogTrace("================================= PrintWindowMap end");
    }

    private void Add(NativeWindowBase window)
    {
        if (_windowMap.TryAdd(window.HWND, window))
        {
            _logger.LogWithHWnd(LogLevel.Information, "add window map", window.HWND, Environment.CurrentManagedThreadId);
        }
        else
        {
            //TODO handleが再利用されている？
            throw new WindowException($"failed add window map hwnd:[{window.HWND}]");
        }
    }

    private void Remove(NativeWindowBase window)
    {
        if (_windowMap.TryRemove(window.HWND, out var window_))
        {
            _logger.LogWithHWnd(LogLevel.Information, "remove window map", window_.HWND, Environment.CurrentManagedThreadId);
            window_.HWND = User32.HWND.None;
        }
        else
        {
            throw new WindowException($"failed remove window map hwnd:[{window.HWND}]");
        }
    }

    private bool FindWindow(User32.HWND hwnd, [MaybeNullWhen(false)] out NativeWindowBase window)
    {
        if (_windowMap.TryGetValue(hwnd, out window))
        {
            _logger.LogWithHWnd(LogLevel.Trace, $"in window map [hash:{window.GetHashCode():X}][{window}]", hwnd, Environment.CurrentManagedThreadId);
            return true;
        }
        return false;
    }

    private bool TryGetWindow(User32.HWND hwnd, [MaybeNullWhen(false)] out NativeWindowBase window)
    {
        PrintWindowStack();
        PrintWindowMap();

        if (FindWindow(hwnd, out window))
        {
            return true;
        }

        if (_windowStack.TryPeek(out window))
        {
            if (window.HWND.Handle == hwnd.Handle)
            {
                _logger.LogWithHWnd(LogLevel.Trace, $"in window stack [hash:{window.GetHashCode():X}][{window}]", hwnd, Environment.CurrentManagedThreadId);
                return true;
            }

            if (window.HWND.Handle == User32.HWND.None.Handle)
            {
                _logger.LogWithHWnd(LogLevel.Trace, $"in window stack (until create process) [hash:{window.GetHashCode():X}][{window}]", hwnd, Environment.CurrentManagedThreadId);
                //最速でHWNDを受け取る
                window.HWND = hwnd;
                return true;
            }

            _logger.LogWithHWnd(LogLevel.Trace, $"in window stack, but other window [hash:{window.GetHashCode():X}][{window}]", hwnd, Environment.CurrentManagedThreadId);
            return false;
        }
        else
        {
            _logger.LogWithHWnd(LogLevel.Trace, "stack is empty", hwnd, Environment.CurrentManagedThreadId);
            return false;
        }
    }
}

//TODO 実験中
internal sealed class WindowManagerSynchronizationContext : SynchronizationContext
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;

    private readonly IDispatcherQueue _dispatcherQueue;

    internal WindowManagerSynchronizationContext(
        ILoggerFactory loggerFactory,
        IDispatcherQueue dispatcherQueue
    )
    {
        _loggerFactory = loggerFactory;
        _logger = _loggerFactory.CreateLogger<WindowManagerSynchronizationContext>();
        _dispatcherQueue = dispatcherQueue;
    }

    public override void Send(SendOrPostCallback d, object? state)
    {
        _logger.LogWithLine(LogLevel.Trace, "Send", Environment.CurrentManagedThreadId);
        throw new NotSupportedException("Send");
    }

    public override void Post(SendOrPostCallback d, object? state)
    {
        _logger.LogWithLine(LogLevel.Trace, "Post", Environment.CurrentManagedThreadId);
        _dispatcherQueue.Dispatch(d, state);
    }

    public override SynchronizationContext CreateCopy()
    {
        _logger.LogWithLine(LogLevel.Trace, "CreateCopy", Environment.CurrentManagedThreadId);
        return this;
    }

    public override void OperationStarted()
    {
        _logger.LogWithLine(LogLevel.Trace, "OperationStarted", Environment.CurrentManagedThreadId);
    }

    public override void OperationCompleted()
    {
        _logger.LogWithLine(LogLevel.Trace, "OperationCompleted", Environment.CurrentManagedThreadId);
    }
}
