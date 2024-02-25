using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Momiji.Core.Buffer;
using Momiji.Internal.Debug;
using Momiji.Internal.Log;
using Kernel32 = Momiji.Interop.Kernel32.NativeMethods;
using User32 = Momiji.Interop.User32.NativeMethods;

namespace Momiji.Core.Window;

internal class WindowManager : IWindowManager
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private readonly IConfiguration _configuration;
    private bool _disposed;

    //TODO UIスレッドクラスに切り出す
    private readonly object _sync = new();
    private CancellationTokenSource? _processCancel;
    private Task? _processTask;
    private int _uiThreadId;
    private DispatcherQueue? _dispatcherQueue;
    private WindowClassManager? _windowClassManager;

    internal WindowClassManager? WindowClassManager => _windowClassManager;

    public WindowManager(
        IConfiguration configuration,
        ILoggerFactory loggerFactory
    )
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _loggerFactory = loggerFactory;
        _logger = _loggerFactory.CreateLogger<WindowManager>();

        _configuration = configuration;
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
            //_desktop?.Close();
            //_windowStation?.Close();
        }
        _logger.LogWithLine(LogLevel.Trace, "DisposeAsync end", Environment.CurrentManagedThreadId);
    }

    private async Task CancelAsync()
    {
        var processCancel = _processCancel;
        if (processCancel == null)
        {
            _logger.LogWithLine(LogLevel.Information, "already stopped.", Environment.CurrentManagedThreadId);
            return;
        }

        if (processCancel.IsCancellationRequested)
        {
            _logger.LogWithLine(LogLevel.Information, "already cancelled.", Environment.CurrentManagedThreadId);
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

    internal async ValueTask<T> DispatchAsync<T>(Func<IWindow, T> item, NativeWindow window)
    {
        T func()
        {
            //TODO スレッドセーフになっているか要確認(再入しても問題ないなら気にしない)
            _windowClassManager!.Push(window);

            try
            {
                //TODO CreateWindowだけ囲えば良さそうだが、一旦全部囲ってしまう
                using var context = new PerMonitorAwareV2ThreadDpiAwarenessContextSetter(_loggerFactory, window);

                _logger.LogWithLine(LogLevel.Trace, "Invoke start", Environment.CurrentManagedThreadId);
                var result = item.Invoke(window);
                _logger.LogWithLine(LogLevel.Trace, "Invoke end", Environment.CurrentManagedThreadId);
                return result;
            }
            finally
            {
                var result = _windowClassManager!.Pop();
                Debug.Assert(result == window);
            }
        }

        //TODO 即時実行か遅延実行かの判断はDispatcherQueueに移した方がよいか？
        if (_uiThreadId == Environment.CurrentManagedThreadId)
        {
            _logger.LogWithLine(LogLevel.Trace, "Dispatch called from same thread id then immidiate mode", Environment.CurrentManagedThreadId);
            return func();
        }

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.AttachedToParent | TaskCreationOptions.RunContinuationsAsynchronously);

        Dispatch(() =>
        {
            _logger.LogWithLine(LogLevel.Trace, "Dispatch called from other thread id then async mode", Environment.CurrentManagedThreadId);
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

        return await tcs.Task;
    }

    private void Dispatch(Action item)
    {
        _logger.LogWithLine(LogLevel.Information, "Dispatch", Environment.CurrentManagedThreadId);
        if (_processCancel == default)
        {
            throw new WindowException("message loop is not exists.");
        }

        _dispatcherQueue!.Dispatch(item);
    }

    public IWindow CreateWindow(
        string windowTitle,
        IWindowManager.OnMessage? onMessage = default
    )
    {
        var window =
            new NativeWindow(
                _loggerFactory,
                this,
                onMessage
            );

        window.CreateWindow(windowTitle);

        return window;
    }

    public IWindow CreateWindow(
        IWindow parent,
        string windowTitle,
        IWindowManager.OnMessage? onMessage = default
    )
    {
        var window =
            new NativeWindow(
                _loggerFactory,
                this,
                onMessage
            );

        window.CreateWindow(windowTitle, (parent as NativeWindow)!);

        return window;
    }

    public IWindow CreateChildWindow(
        IWindow parent,
        string className,
        string windowTitle,
        IWindowManager.OnMessage? onMessage = default
    )
    {
        var window =
            new NativeWindow(
                _loggerFactory,
                this,
                onMessage
            );

        window.CreateWindow((parent as NativeWindow)!, className, windowTitle);

        return window;
    }

    public void Start(TaskCompletionSource<IWindowManager> startTcs)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_processCancel != null)
            {
                throw new InvalidOperationException("already started.");
            }

            _processCancel = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken.None);
        }

        _processTask = Run(startTcs).ContinueWith((task) => {
            _logger.LogWithLine(LogLevel.Information, "process task end", Environment.CurrentManagedThreadId);

            //TODO エラー時のみキャンセルすればよいハズ？
            //await CancelAsync().ConfigureAwait(false);
            //_logger.LogWithLine(LogLevel.Trace, "cancel end", Environment.CurrentManagedThreadId);

            if (!_processCancel.IsCancellationRequested)
            {
                _logger.LogWithLine(LogLevel.Warning, "キャンセル済になってない", Environment.CurrentManagedThreadId);
            }

            _processTask = default;

            _processCancel.Dispose();
            _processCancel = default;

            //TODO スレッドが終わったことの通知は要る？

            _logger.LogWithLine(LogLevel.Information, "stopped.", Environment.CurrentManagedThreadId);
        });
    }

    private Task Run(TaskCompletionSource<IWindowManager> startTcs)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.AttachedToParent | TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            _logger.LogWithLine(LogLevel.Information, "*** thread start ***", Environment.CurrentManagedThreadId);

            try
            {
                using var dispatcherQueue = new DispatcherQueue(_loggerFactory);
                _dispatcherQueue = dispatcherQueue;

                using var windowClassManager = new WindowClassManager(_configuration, _loggerFactory, _dispatcherQueue);
                _windowClassManager = windowClassManager;

                SetupMessageLoopThread();

                startTcs.SetResult(this);

                RunMessageLoop(_dispatcherQueue!, _windowClassManager!);
                _logger.LogWithLine(LogLevel.Information, "message loop normal end.", Environment.CurrentManagedThreadId);
                tcs.SetResult();
            }
            catch (Exception e)
            {
                _logger.LogWithLine(LogLevel.Error, e, "message loop abnormal end.", Environment.CurrentManagedThreadId);

                startTcs.TrySetException(e);
                tcs.SetException(e);
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

    private void RunMessageLoop(
        DispatcherQueue dispatcherQueue,
        WindowClassManager windowClassManager
    )
    {
        if (_processCancel == null)
        {
            throw new InvalidOperationException($"{nameof(_processCancel)} is null.");
        }

        var forceCancel = false;
        var cancelled = false;

        var ct = _processCancel.Token;

        using var waitHandlesPin = new PinnedBuffer<nint[]>([
            dispatcherQueue.SafeWaitHandle.DangerousGetHandle(),
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
                if (windowClassManager.IsEmpty)
                {
                    _logger.LogWithLine(LogLevel.Information, "all closed.", Environment.CurrentManagedThreadId);
                    break;
                }
            }
            else if (ct.IsCancellationRequested)
            {
                cancelled = true;

                _logger.LogWithLine(LogLevel.Information, "canceled.", Environment.CurrentManagedThreadId);
                windowClassManager.CloseAll();

                // 10秒以内にクローズされなければ、ループを終わらせる
                var _ =
                    Task.Delay(10000, CancellationToken.None)
                    .ContinueWith(
                        (task) => { forceCancel = true; },
                        TaskScheduler.Default
                    );
            }

            {
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
                    windowClassManager.DispatchMessage();
                    continue;
                }
                else if (res == 0) // WAIT_OBJECT_0
                {
                    //タスクキューのディスパッチ
                    //TODO RTWQなどに置き換えできる？
                    _logger.LogWithLine(LogLevel.Trace, "MsgWaitForMultipleObjectsEx comes queue event.", Environment.CurrentManagedThreadId);
                    dispatcherQueue.DispatchQueue();
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
                    throw error;
                }
            }
        }
        _logger.LogWithLine(LogLevel.Information, "end message loop.", Environment.CurrentManagedThreadId);
    }

}