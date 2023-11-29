using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Momiji.Core.Buffer;
using Momiji.Internal.Debug;
using Momiji.Internal.Log;
using Kernel32 = Momiji.Interop.Kernel32.NativeMethods;
using User32 = Momiji.Interop.User32.NativeMethods;

namespace Momiji.Core.Window;

public class WindowManager : IWindowManager
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private bool _disposed;

    private readonly object _sync = new();
    private CancellationTokenSource? _processCancel;
    private Task? _processTask;
    private int _uiThreadId;

    private readonly PinnedDelegate<User32.WNDPROC> _wndProc;
    private readonly WindowClass _windowClass;

    private readonly ConcurrentQueue<Action> _queue = new();
    private readonly ManualResetEventSlim _queueEvent = new();

    private readonly ConcurrentDictionary<User32.HWND, NativeWindow> _windowMap = new();
    private readonly Stack<NativeWindow> _windowStack = new();

    private readonly Stack<Exception> _wndProcExceptionStack = new();

    private readonly ConcurrentDictionary<User32.HWND, nint> _oldWndProcMap = new();

    public WindowManager(
        IConfiguration configuration,
        ILoggerFactory loggerFactory
    )
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        //TODO windowとthreadが1:1のモード
        var param = new IWindowManager.Param();
        configuration.GetSection($"{typeof(WindowManager).FullName}").Bind(param);

        _loggerFactory = loggerFactory;
        _logger = _loggerFactory.CreateLogger<WindowManager>();

        _wndProc = new PinnedDelegate<User32.WNDPROC>(new(WndProc));
        _windowClass =
            new WindowClass(
                _loggerFactory,
                _wndProc,
                param.CS
            );
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

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _logger.LogWithLine(LogLevel.Information, "disposing", Environment.CurrentManagedThreadId);
            DisposeAsyncCore().AsTask().Wait();
        }

        _disposed = true;
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore().ConfigureAwait(false);

        Dispose(false);

        GC.SuppressFinalize(this);
    }

    protected async virtual ValueTask DisposeAsyncCore()
    {
        _logger.LogWithLine(LogLevel.Trace, "DisposeAsync start", Environment.CurrentManagedThreadId);
        try
        {
            await CancelAsync().ConfigureAwait(false);
        }
        finally
        {
            //クローズしていないウインドウが残っていると失敗する
            _windowClass.Dispose();

            _wndProc.Dispose();

            //_desktop?.Close();
            //_windowStation?.Close();
        }
        _logger.LogWithLine(LogLevel.Trace, "DisposeAsync end", Environment.CurrentManagedThreadId);
    }

    public async Task CancelAsync()
    {
        var processCancel = _processCancel;
        if (processCancel == null)
        {
            _logger.LogWithLine(LogLevel.Information, "already stopped.", Environment.CurrentManagedThreadId);
            return;
        }

        var task = _processTask;
        try
        {
            processCancel.Cancel();
            if (task != default)
            {
                await task;
            }
        }
        catch (Exception e)
        {
            _logger.LogWithLine(LogLevel.Error, e, "failed.", Environment.CurrentManagedThreadId);
        }
    }

    internal class PerMonitorAwareV2ThreadDpiAwarenessContextSetter : IDisposable
    {
        private readonly ILogger _logger;
        private readonly NativeWindow _window;

        private bool _disposed;
        private readonly User32.DPI_AWARENESS_CONTEXT _oldContext;

        public PerMonitorAwareV2ThreadDpiAwarenessContextSetter(
            ILoggerFactory loggerFactory,
            NativeWindow window
        )
        {
            _logger = loggerFactory.CreateLogger<PerMonitorAwareV2ThreadDpiAwarenessContextSetter>();
            _window = window;

            _oldContext = User32.SetThreadDpiAwarenessContext(User32.DPI_AWARENESS_CONTEXT.PER_MONITOR_AWARE_V2);

            var error = new Win32Exception();
            _logger.LogWithHWndAndError(LogLevel.Trace, $"ON SetThreadDpiAwarenessContext [{_oldContext:X} -> PER_MONITOR_AWARE_V2]", _window._hWindow, error.ToString(), Environment.CurrentManagedThreadId);
        }

        ~PerMonitorAwareV2ThreadDpiAwarenessContextSetter()
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
            }

            if (_oldContext.Handle != 0)
            {
                var oldContext = User32.SetThreadDpiAwarenessContext(_oldContext);

                var error = new Win32Exception();
                _logger.LogWithHWndAndError(LogLevel.Trace, $"OFF SetThreadDpiAwarenessContext [{oldContext} -> {_oldContext}]", _window._hWindow, error.ToString(), Environment.CurrentManagedThreadId);
            }

            _disposed = true;
        }
    }

    internal Task<T> DispatchAsync<T>(NativeWindow window, Func<T> item)
    {
        T func()
        {
            //TODO スレッドセーフになっているか要確認(再入しても問題ないなら気にしない)
            _windowStack.Push(window);

            try
            {
                //TODO CreateWindowだけ囲えば良さそうだが、一旦全部囲ってしまう
                using var context = new PerMonitorAwareV2ThreadDpiAwarenessContextSetter(_loggerFactory, window);

                var result = item.Invoke();
                return result;
            }
            finally
            {
                var result = _windowStack.Pop();
                Debug.Assert(result == window);
            }
        }

        if (_uiThreadId == Environment.CurrentManagedThreadId)
        {
            _logger.LogWithLine(LogLevel.Trace, "Dispatch called from same thread id then immidiate mode", Environment.CurrentManagedThreadId);
            return Task.FromResult(func());
        }

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.AttachedToParent);
        Dispatch(() => {
            try
            {
                //TODO キャンセルできるようにする？
                tcs.SetResult(func());
            }
            catch (Exception e)
            {
                tcs.SetException(e);
            }
        });

        return tcs.Task;
    }


    private void Dispatch(Action item)
    {
        _logger.LogWithLine(LogLevel.Information, "Dispatch", Environment.CurrentManagedThreadId);
        if (_processCancel == default)
        {
            throw new WindowException("message loop is not exists.");
        }

        _queue.Enqueue(item);
        _queueEvent.Set();
    }

    public IWindow CreateWindow(
        IWindowManager.OnMessage? onMessage = default
    )
    {
        var window =
            new NativeWindow(
                _loggerFactory,
                this,
                onMessage
            );

        window.CreateWindow(_windowClass);

        return window;
    }

    public IWindow CreateWindow(
        IWindow parent,
        IWindowManager.OnMessage? onMessage = default
    )
    {
        var window =
            new NativeWindow(
                _loggerFactory,
                this,
                onMessage
            );

        window.CreateWindow(_windowClass, (parent as NativeWindow)!);

        return window;
    }

    public IWindow CreateChildWindow(
        IWindow parent,
        string className,
        IWindowManager.OnMessage? onMessage = default
    )
    {
        var window =
            new NativeWindow(
                _loggerFactory,
                this,
                onMessage
            );

        window.CreateWindow((parent as NativeWindow)!, className);

        return window;
    }

    private nint ChildWindowSetWindowLong(
        User32.HWND childHWnd,
        int nIndex,
        nint dwNewLong
    )
    {
        Marshal.SetLastPInvokeError(0);
        _logger.LogWithHWnd(LogLevel.Information, $"child SetWindowLong nIndex:[{nIndex:X}] dwNewLong:[{dwNewLong:X}]", childHWnd, Environment.CurrentManagedThreadId);

        var isChildWindowUnicode = (childHWnd.Handle != User32.HWND.None.Handle) && User32.IsWindowUnicode(childHWnd);

        //SetWindowLong～のエラー判定のために、エラーコードをリセットする
        Marshal.SetLastPInvokeError(0);
        var result = isChildWindowUnicode
                        ? Environment.Is64BitProcess
                            ? User32.SetWindowLongPtrW(childHWnd, nIndex, dwNewLong)
                            : User32.SetWindowLongW(childHWnd, nIndex, dwNewLong)
                        : Environment.Is64BitProcess
                            ? User32.SetWindowLongPtrA(childHWnd, nIndex, dwNewLong)
                            : User32.SetWindowLongA(childHWnd, nIndex, dwNewLong)
                        ;
        var error = new Win32Exception();
        _logger.LogWithHWndAndError(LogLevel.Information, $"SetWindowLong result:[{result:X}]", childHWnd, error.ToString(), Environment.CurrentManagedThreadId);
        if (result == 0 && error.NativeErrorCode != 0)
        {
            throw new WindowException("SetWindowLong failed", error);
        }

        return result;
    }

    private void OnWM_PARENTNOTIFY(User32.HWND hwnd, nint wParam, nint lParam)
    {
        var GWLP_WNDPROC = -4;

        switch (wParam & 0xFFFF)
        {
            case 0x0001: //WM_CREATE
                {
                    _logger.LogWithHWnd(LogLevel.Trace, $"WM_PARENTNOTIFY WM_CREATE {wParam:X}", hwnd, Environment.CurrentManagedThreadId);
                    var childHWnd = (User32.HWND)lParam;

                    //TODO 異なるスレッドで作成したwindowのwndprocは差し替えても流れていかない

                    if (_oldWndProcMap.TryAdd(childHWnd, nint.Zero))
                    {
                        var oldWndProc = ChildWindowSetWindowLong(childHWnd, GWLP_WNDPROC, _wndProc.FunctionPointer);

                        if (oldWndProc == _wndProc.FunctionPointer)
                        {
                            //変更前・変更後のWndProcが同じだった＝WindowManager経由で作ったWindow　→　ここで管理する必要なし
                            //TODO SetWindowLongする前にバイパスした方がよい
                            _logger.LogWithHWnd(LogLevel.Information, $"IGNORE hwnd:[{childHWnd}]", hwnd, Environment.CurrentManagedThreadId);
                            _oldWndProcMap.TryRemove(childHWnd, out var _);
                        }
                        else
                        {
                            _logger.LogWithHWnd(LogLevel.Information, $"add to old wndproc map hwnd:[{childHWnd}]", hwnd, Environment.CurrentManagedThreadId);
                            if (!_oldWndProcMap.TryUpdate(childHWnd, oldWndProc, nint.Zero))
                            {
                                //更新できなかった
                                _logger.LogWithHWnd(LogLevel.Error, $"failed add to old wndproc map hwnd:[{childHWnd}]", hwnd, Environment.CurrentManagedThreadId);
                            }
                        }
                    }
                    else
                    {
                        //すでに登録されているのは異常事態
                        _logger.LogWithHWnd(LogLevel.Warning, $"found in old wndproc map hwnd:[{childHWnd}]", hwnd, Environment.CurrentManagedThreadId);
                    }

                    break;
                }
            case 0x0002: //WM_DESTROY
                {
                    _logger.LogWithHWnd(LogLevel.Trace, $"WM_PARENTNOTIFY WM_DESTROY {wParam:X}", hwnd, Environment.CurrentManagedThreadId);
                    var childHWnd = (User32.HWND)lParam;

                    if (_oldWndProcMap.TryRemove(childHWnd, out var oldWndProc))
                    {
                        _logger.LogWithHWnd(LogLevel.Information, $"remove old wndproc map hwnd:[{childHWnd}]", hwnd, Environment.CurrentManagedThreadId);
                        var _ = ChildWindowSetWindowLong(childHWnd, GWLP_WNDPROC, oldWndProc);

                        //WM_NCDESTROYが発生する前にWndProcを元に戻したので、ここで呼び出しする
                        //TODO 親ウインドウのWM_PARENTNOTIFYが発生するより先に子ウインドウのWM_NCDESTROYが発生した場合は、ココを通らない
                        OnWM_NCDESTROY(childHWnd);
                    }
                    else
                    {
                        _logger.LogWithHWnd(LogLevel.Warning, $"not found in old wndproc map hwnd:[{childHWnd}]", hwnd, Environment.CurrentManagedThreadId);
                    }
                    break;
                }
        }
    }

    private void OnWM_NCCREATE(User32.HWND hwnd)
    {
        //TODO トップレベルウインドウだったときのみ呼び出す
        if (!User32.EnableNonClientDpiScaling(hwnd))
        {
            var error = new Win32Exception();
            _logger.LogWithHWndAndError(LogLevel.Error, "EnableNonClientDpiScaling failed", hwnd, error.ToString(), Environment.CurrentManagedThreadId);
        }
    }

    private void OnWM_NCDESTROY(User32.HWND hwnd)
    {
        if (_windowMap.TryRemove(hwnd, out var _))
        {
            _logger.LogWithHWnd(LogLevel.Information, "remove window map", hwnd, Environment.CurrentManagedThreadId);

            //TODO メインウインドウなら、自身を終わらせる動作を入れるか？
        }
        else
        {
            _logger.LogWithHWnd(LogLevel.Warning, "failed. remove window map", hwnd, Environment.CurrentManagedThreadId);
        }
    }

    public void CloseAll()
    {
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

    public async Task StartAsync(CancellationToken stoppingToken)
    {
        lock (_sync)
        {
            if (_processCancel != null)
            {
                _logger.LogWithLine(LogLevel.Information, "already started.", Environment.CurrentManagedThreadId);
                return;
            }
            _processCancel = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        }

        _processTask = Run();

        try
        {
            await _processTask.ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _logger.LogWithLine(LogLevel.Error, e, "process task failed.", Environment.CurrentManagedThreadId);
        }

        _logger.LogWithLine(LogLevel.Information, "process task end", Environment.CurrentManagedThreadId);

        await CancelAsync().ConfigureAwait(false);
        _logger.LogWithLine(LogLevel.Trace, "cancel end", Environment.CurrentManagedThreadId);

        _processTask = default;

        _processCancel?.Dispose();
        _processCancel = default;

        //TODO スレッドが終わったことの通知は要る？

        _logger.LogWithLine(LogLevel.Information, "stopped.", Environment.CurrentManagedThreadId);
    }

    private Task Run()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.AttachedToParent);
        var thread = new Thread(() =>
        {
            _logger.LogWithLine(LogLevel.Information, "*** thread start ***", Environment.CurrentManagedThreadId);
            Exception? exception = default; 
            try
            {
                SetupMessageLoopThread();
                RunMessageLoop();
                _logger.LogWithLine(LogLevel.Information, "message loop normal end", Environment.CurrentManagedThreadId);
            }
            catch (Exception e)
            {
                exception = e;
                _logger.LogWithLine(LogLevel.Error, e, "message loop exception", Environment.CurrentManagedThreadId);
                _processCancel?.Cancel();
            }

            //TODO ここでcloseのチェックをするのは妥当か？ processCancelした直後なのでwindowが残ってそう。
            //但し、CreateWindowしたスレッドでないとDestroyWindowを呼べないので、この中で行うしかない？

            try
            {
                if (!_windowMap.IsEmpty)
                {
                    //クローズできていないwindowが残っているのは異常事態
                    _logger.LogWithLine(LogLevel.Warning, $"window left {_windowMap.Count}", Environment.CurrentManagedThreadId);

                    foreach (var item in _windowMap)
                    {
                        //TODO closeした通知を流す必要あり
                        var hwnd = item.Key;
                        _logger.LogWithLine(LogLevel.Warning, $"DestroyWindow {hwnd:X}", Environment.CurrentManagedThreadId);
                        if (!User32.DestroyWindow(hwnd))
                        {
                            var error = new Win32Exception();
                            _logger.LogWithHWndAndError(LogLevel.Error, "DestroyWindow failed", hwnd, error.ToString(), Environment.CurrentManagedThreadId);
                        }
                    }

                    _windowMap.Clear();
                }
            }
            catch (Exception e)
            {
                exception = e;
                _logger.LogWithLine(LogLevel.Error, e, "window clean up exception", Environment.CurrentManagedThreadId);
            }

            if (exception != default)
            {
                tcs.SetException(new WindowException("message loop exception", exception));
            }
            else
            {
                tcs.SetResult();
            }

            _uiThreadId = default;
        })
        {
            IsBackground = false,
            Name = "Momiji UI Thread"
        };

        //TODO MTAも指定可能にしてみる？
        thread.SetApartmentState(ApartmentState.STA);
        _logger.LogWithLine(LogLevel.Information, $"GetApartmentState {thread.GetApartmentState()}", Environment.CurrentManagedThreadId);
        _uiThreadId = thread.ManagedThreadId;
        thread.Start();

        //このタスクでcontinue withすると、UIスレッドでQueue登録してスレッド終了し、QueueのCOMアクセスが失敗する
        return tcs.Task;
    }

    private void SetupMessageLoopThread()
    {
        WindowDebug.CheckIntegrityLevel(_loggerFactory);
        WindowDebug.CheckDesktop(_loggerFactory);
        WindowDebug.CheckGetProcessInformation(_loggerFactory);

        {
            var result = User32.IsGUIThread(true);
            if (!result)
            {
                var error = new Win32Exception();
                throw new WindowException("IsGUIThread failed", error);
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

    private void RunMessageLoop()
    {
        if (_processCancel == null)
        {
            throw new InvalidOperationException($"{nameof(_processCancel)} is null.");
        }

        var forceCancel = false;
        var cancelled = false;

        var ct = _processCancel.Token;

        using var waitHandlesPin = new PinnedBuffer<nint[]>([
            _queueEvent.WaitHandle.SafeWaitHandle.DangerousGetHandle(),
            ct.WaitHandle.SafeWaitHandle.DangerousGetHandle()
        ]);
        var handleCount = waitHandlesPin.Target.Length;

        _logger.LogWithLine(LogLevel.Information, "start message loop", Environment.CurrentManagedThreadId);
        while (true)
        {
            if (forceCancel)
            {
                _logger.LogWithLine(LogLevel.Information, "force canceled.", Environment.CurrentManagedThreadId);
                break;
            }

            if (cancelled)
            {
                if (_windowMap.IsEmpty)
                {
                    _logger.LogWithLine(LogLevel.Information, "all closed.", Environment.CurrentManagedThreadId);
                    break;
                }
            }
            else if (ct.IsCancellationRequested)
            {
                cancelled = true;

                _logger.LogWithLine(LogLevel.Information, "canceled.", Environment.CurrentManagedThreadId);
                CloseAll();

                // 10秒以内にクローズされなければ、ループを終わらせる
                var _ =
                    Task.Delay(10000, CancellationToken.None)
                    .ContinueWith(
                        (task) => { forceCancel = true; },
                        TaskScheduler.Default
                    );
            }

            {
                //TODO 同時にシグナル状態になっていても１コずつ返るんだっけ？

                _logger.LogWithLine(LogLevel.Trace, "MsgWaitForMultipleObjectsEx", Environment.CurrentManagedThreadId);
                var res =
                    User32.MsgWaitForMultipleObjectsEx(
                        (uint)handleCount,
                        waitHandlesPin.AddrOfPinnedObject,
                        1000,
                        0x04FF, //QS_ALLINPUT
                        0x0004 //MWMO_INPUTAVAILABLE
                    );
                if (res == 258) // WAIT_TIMEOUT
                {
                    _logger.LogWithLine(LogLevel.Trace, "MsgWaitForMultipleObjectsEx timeout.", Environment.CurrentManagedThreadId);
                    continue;
                }
                else if (res == handleCount) // WAIT_OBJECT_0+2
                {
                    //ウインドウメッセージキューのディスパッチ
                    _logger.LogWithLine(LogLevel.Trace, "MsgWaitForMultipleObjectsEx comes window message.", Environment.CurrentManagedThreadId);
                    DispatchMessage();
                    continue;
                }
                else if (res == 0) // WAIT_OBJECT_0
                {
                    //タスクキューのディスパッチ
                    //TODO RTWQなどに置き換えできる？

                    _logger.LogWithLine(LogLevel.Trace, $"MsgWaitForMultipleObjectsEx comes queue event. {_queue.Count}", Environment.CurrentManagedThreadId);
                    _queueEvent.Reset();

                    while (_queue.TryDequeue(out var result))
                    {
                        _logger.LogWithLine(LogLevel.Trace, "Invoke", Environment.CurrentManagedThreadId);
                        result.Invoke();
                    }
                    continue;
                }
                else if (res == 1) // WAIT_OBJECT_0+1
                {
                    //キャンセル通知
                    _logger.LogWithLine(LogLevel.Trace, "MsgWaitForMultipleObjectsEx comes cancel event.", Environment.CurrentManagedThreadId);
                    //ctがシグナル状態になりっぱなしになるので、リストから外す
                    handleCount--;
                    continue;
                }
                else
                {
                    //エラー
                    var error = new Win32Exception();
                    throw new WindowException("MsgWaitForMultipleObjectsEx failed", error);
                }
            }
        }
        _logger.LogWithLine(LogLevel.Information, "end message loop.", Environment.CurrentManagedThreadId);
    }

    private void DispatchMessage()
    {
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
            _logger.LogWithLine(LogLevel.Trace, $"MSG {msg}", Environment.CurrentManagedThreadId);

            if (msg.hwnd.Handle == User32.HWND.None.Handle)
            {
                _logger.LogWithLine(LogLevel.Trace, "hwnd is none", Environment.CurrentManagedThreadId);
            }

            //TODO: msg.hwnd がnullのとき(= thread宛メッセージ)は、↓以降はバイパスした方がよい？

            {
                _logger.LogWithLine(LogLevel.Trace, "TranslateMessage", Environment.CurrentManagedThreadId);
                var ret = User32.TranslateMessage(ref msg);
                var error = new Win32Exception();
                _logger.LogWithHWndAndError(LogLevel.Trace, $"TranslateMessage {ret}", msg.hwnd, error.ToString(), Environment.CurrentManagedThreadId);
            }

            {
                var isWindowUnicode = (msg.hwnd.Handle != User32.HWND.None.Handle) && User32.IsWindowUnicode(msg.hwnd);
                _logger.LogWithLine(LogLevel.Trace, $"IsWindowUnicode {isWindowUnicode}", Environment.CurrentManagedThreadId);

                _logger.LogWithLine(LogLevel.Trace, "DispatchMessage", Environment.CurrentManagedThreadId);
                var ret = isWindowUnicode
                            ? User32.DispatchMessageW(ref msg)
                            : User32.DispatchMessageA(ref msg)
                            ;
                var error = new Win32Exception();
                _logger.LogWithHWndAndError(LogLevel.Trace, $"DispatchMessage {ret}", msg.hwnd, error.ToString(), Environment.CurrentManagedThreadId);
            }
        }
    }

    private bool TryGetWindow(User32.HWND hwnd, [MaybeNullWhen(false)] out NativeWindow window)
    {
        if (_windowMap.TryGetValue(hwnd, out window))
        {
            _logger.LogWithLine(LogLevel.Trace, $"window map found. hwnd:[{hwnd}] -> hash:[{window.GetHashCode():X}]", Environment.CurrentManagedThreadId);
            return true;
        }

        if (_windowStack.TryPeek(out var windowFromStack))
        {
            _logger.LogWithLine(LogLevel.Trace, $"window stack hash:{windowFromStack.GetHashCode():X}", Environment.CurrentManagedThreadId);

            if (windowFromStack._hWindow.Handle != User32.HWND.None.Handle)
            {
                _logger.LogWithLine(LogLevel.Trace, $"no managed hwnd:[{hwnd}]", Environment.CurrentManagedThreadId);
                return false;
            }
        }
        else
        {
            _logger.LogWithLine(LogLevel.Trace, "stack none", Environment.CurrentManagedThreadId);
            return false;
        }

        //最速でHWNDを受け取る
        windowFromStack._hWindow = hwnd;
        if (_windowMap.TryAdd(hwnd, windowFromStack))
        {
            _logger.LogWithLine(LogLevel.Information, $"add window map hwnd:[{hwnd}]", Environment.CurrentManagedThreadId);
            window = windowFromStack;
            return true;
        }
        else
        {
            throw new WindowException($"failed add window map hwnd:[{hwnd}]");
        }
    }


    private nint WndProc(User32.HWND hwnd, uint msg, nint wParam, nint lParam)
    {
        try
        {
            _logger.LogWithMsg(LogLevel.Trace, "WndProc", hwnd, msg, wParam, lParam, Environment.CurrentManagedThreadId);

            {
                var result = WndProcBefore(hwnd, msg, wParam, lParam, out var handled);
                if (handled)
                {
                    return result;
                }
            }

            if (TryGetWindow(hwnd, out var window))
            {
                //ウインドウに流す
                var result = window.WndProc(msg, wParam, lParam, out var handled);
                if (handled)
                {
                    _logger.LogWithLine(LogLevel.Trace, $"handled msg:{msg:X} result:{result}", Environment.CurrentManagedThreadId);
                    return result;
                }
                else
                {
                    _logger.LogWithLine(LogLevel.Trace, $"no handled msg:{msg:X} result:{result}", Environment.CurrentManagedThreadId);
                }
            }
            else
            {
                _logger.LogWithLine(LogLevel.Trace, "unkown window handle", Environment.CurrentManagedThreadId);
            }

            var isWindowUnicode = (hwnd.Handle != User32.HWND.None.Handle) && User32.IsWindowUnicode(hwnd);

            //TODO 子ウインドウでないときはこの処理が無駄
            if (_oldWndProcMap.TryGetValue(hwnd, out var oldWndProc))
            {
                _logger.LogWithMsg(LogLevel.Trace, "CallWindowProc", hwnd, msg, wParam, lParam, Environment.CurrentManagedThreadId);
                var result = isWindowUnicode
                            ? User32.CallWindowProcW(oldWndProc, hwnd, msg, wParam, lParam)
                            : User32.CallWindowProcA(oldWndProc, hwnd, msg, wParam, lParam)
                            ;
                _logger.LogWithLine(LogLevel.Trace, $"CallWindowProc [{result:X}]", Environment.CurrentManagedThreadId);
                return result;
            }
            else
            {
                var result = isWindowUnicode
                    ? User32.DefWindowProcW(hwnd, msg, wParam, lParam)
                    : User32.DefWindowProcA(hwnd, msg, wParam, lParam)
                    ;
                _logger.LogWithLine(LogLevel.Trace, $"DefWindowProc [{result:X}]", Environment.CurrentManagedThreadId);
                return result;
            }
        }
        catch (Exception e)
        {
            //msgによってreturnを分ける
            return WndProcError(hwnd, msg, wParam, lParam, e);
        }
    }

    private nint WndProcBefore(User32.HWND hwnd, uint msg, nint wParam, nint lParam, out bool handled)
    {
        {
            var ret = User32.InSendMessageEx(nint.Zero);
            _logger.LogWithLine(LogLevel.Trace, $"InSendMessageEx {ret:X}", Environment.CurrentManagedThreadId);
            if ((ret & (0x00000008 | 0x00000001)) == 0x00000001) //ISMEX_SEND
            {
                _logger.LogWithLine(LogLevel.Trace, "ISMEX_SEND", Environment.CurrentManagedThreadId);
                //TODO ISMEX_SENDに返す値を指定できるようにする　どうやってここに渡す？
                var ret2 = User32.ReplyMessage(new nint(1));
                var error = new Win32Exception();
                _logger.LogWithHWndAndError(LogLevel.Trace, $"ReplyMessage {ret2}", hwnd, error.ToString(), Environment.CurrentManagedThreadId);
            }
        }

        {
            var context = User32.GetThreadDpiAwarenessContext();
            var awareness = User32.GetAwarenessFromDpiAwarenessContext(context);
            var dpi = User32.GetDpiFromDpiAwarenessContext(context);

            _logger.LogWithHWnd(LogLevel.Trace, $"GetThreadDpiAwarenessContext [context:{context}][awareness:{awareness}][dpi:{dpi}]", hwnd, Environment.CurrentManagedThreadId);
        }

        {
            var context = User32.GetWindowDpiAwarenessContext(hwnd);
            var awareness = User32.GetAwarenessFromDpiAwarenessContext(context);
            var dpi = User32.GetDpiFromDpiAwarenessContext(context);
            var dpiForWindow = User32.GetDpiForWindow(hwnd);

            _logger.LogWithHWnd(LogLevel.Trace, $"GetWindowDpiAwarenessContext [context:{context}][awareness:{awareness}][dpi:{dpi}][dpiForWindow:{dpiForWindow}]", hwnd, Environment.CurrentManagedThreadId);
        }

        handled = false;
        var result = nint.Zero;

        switch (msg)
        {
            case 0x0018://WM_SHOWWINDOW
                _logger.LogWithLine(LogLevel.Trace, $"WM_SHOWWINDOW {wParam:X} {lParam:X}", Environment.CurrentManagedThreadId);
                break;

            case 0x001C://WM_ACTIVATEAPP
                _logger.LogWithLine(LogLevel.Trace, $"WM_ACTIVATEAPP {wParam:X} {lParam:X}", Environment.CurrentManagedThreadId);
                break;

            case 0x0024://WM_GETMINMAXINFO
                _logger.LogWithLine(LogLevel.Trace, $"WM_GETMINMAXINFO {wParam:X} {lParam:X}", Environment.CurrentManagedThreadId);
                break;

            case 0x0046://WM_WINDOWPOSCHANGING
                _logger.LogWithLine(LogLevel.Trace, "WM_WINDOWPOSCHANGING", Environment.CurrentManagedThreadId);
                break;

            case 0x0081://WM_NCCREATE
                _logger.LogWithLine(LogLevel.Trace, "WM_NCCREATE", Environment.CurrentManagedThreadId);
                OnWM_NCCREATE(hwnd);
                break;

            case 0x0082://WM_NCDESTROY
                _logger.LogWithLine(LogLevel.Trace, "WM_NCDESTROY", Environment.CurrentManagedThreadId);
                OnWM_NCDESTROY(hwnd);
                handled = true;
                break;

            case 0x0083://WM_NCCALCSIZE
                _logger.LogWithLine(LogLevel.Trace, "WM_NCCALCSIZE", Environment.CurrentManagedThreadId);
                break;

            case 0x0210://WM_PARENTNOTIFY
                _logger.LogWithLine(LogLevel.Trace, "WM_PARENTNOTIFY", Environment.CurrentManagedThreadId);
                OnWM_PARENTNOTIFY(hwnd, wParam, lParam);
                break;

            case 0x02E0://WM_DPICHANGED
                _logger.LogWithLine(LogLevel.Trace, "WM_DPICHANGED", Environment.CurrentManagedThreadId);
                break;

            case 0x02E2://WM_DPICHANGED_BEFOREPARENT
                _logger.LogWithLine(LogLevel.Trace, "WM_DPICHANGED_BEFOREPARENT", Environment.CurrentManagedThreadId);
                break;

            case 0x02E3://WM_DPICHANGED_AFTERPARENT
                _logger.LogWithLine(LogLevel.Trace, "WM_DPICHANGED_AFTERPARENT", Environment.CurrentManagedThreadId);
                break;

            case 0x02E4://WM_GETDPISCALEDSIZE
                _logger.LogWithLine(LogLevel.Trace, "WM_GETDPISCALEDSIZE", Environment.CurrentManagedThreadId);
                break;
        }

        return result;
    }

    internal class WndProcException(
        string message,
        User32.HWND hwnd,
        uint msg,
        nint wParam,
        nint lParam,
        Exception innerException
    ) : Exception(message, innerException)
    {
        public readonly User32.HWND Hwnd = hwnd;
        public readonly uint Msg = msg;
        public readonly nint WParam = wParam;
        public readonly nint LParam = lParam;
    }

    private nint WndProcError(User32.HWND hwnd, uint msg, nint wParam, nint lParam, Exception exception)
    {
        var result = nint.Zero;

        _wndProcExceptionStack.Push(new WndProcException("error occurred in WndProc", hwnd, msg, wParam, lParam, exception));

        switch (msg)
        {
            case 0x0001://WM_CREATE
                _logger.LogWithLine(LogLevel.Error, "WM_CREATE error", Environment.CurrentManagedThreadId);
                result = -1;
                break;

            case 0x0081://WM_NCCREATE
                _logger.LogWithLine(LogLevel.Error, "WM_NCCREATE error", Environment.CurrentManagedThreadId);
                result = -1;
                break;
        }

        return result;
    }

    internal void ThrowIfOccurredInWndProc()
    {
        lock (_wndProcExceptionStack)
        {
            if (_wndProcExceptionStack.Count > 0)
            {
                var aggregateException = new AggregateException(_wndProcExceptionStack.AsEnumerable());
                _wndProcExceptionStack.Clear();
                throw aggregateException;
            }
        }
    }
}
