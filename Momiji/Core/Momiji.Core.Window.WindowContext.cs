using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Momiji.Core.Buffer;
using Momiji.Internal.Log;
using Momiji.Internal.Util;
using User32 = Momiji.Interop.User32.NativeMethods;

namespace Momiji.Core.Window;

internal sealed class WindowContext : IDisposable
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private bool _disposed;

    private readonly int _uiThreadId;

    private DispatcherQueue DispatcherQueue { get; }
    internal WindowClassManager WindowClassManager { get; }
    internal WindowProcedure WindowProcedure { get; }
    internal WindowManager WindowManager { get; }

    private readonly WindowContextSynchronizationContext _windowContextSynchronizationContext;
    private readonly CancellationTokenSource _processCancel;

    public bool IsEmpty => WindowManager.IsEmpty;

    public WindowContext(
        ILoggerFactory loggerFactory,
        IUIThread.OnUnhandledException? onUnhandledException = default
    )
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _loggerFactory = loggerFactory;
        _logger = _loggerFactory.CreateLogger<WindowContext>();
        _uiThreadId = Environment.CurrentManagedThreadId;
        _processCancel = new CancellationTokenSource();

        WindowProcedure = new(_loggerFactory, OnMessage, OnThreadMessage);
        WindowClassManager = new(loggerFactory, WindowProcedure.FunctionPointer);
        DispatcherQueue = new(_loggerFactory, onUnhandledException);
        WindowManager = new(_loggerFactory, WindowProcedure.FunctionPointer);

        _windowContextSynchronizationContext = new(_loggerFactory, DispatcherQueue);
    }

    ~WindowContext()
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

            WindowManager.Dispose();
            DispatcherQueue.Dispose();
            WindowClassManager.Dispose();
            WindowProcedure.Dispose();

            _processCancel.Dispose();
        }

        _disposed = true;
    }

    internal void Cancel()
    {
        if (_processCancel.IsCancellationRequested)
        {
            _logger.LogWithLine(LogLevel.Information, "already cancelled.", Environment.CurrentManagedThreadId);
        }
        else
        {
            _logger.LogWithLine(LogLevel.Information, "cancel.", Environment.CurrentManagedThreadId);
            _processCancel.Cancel();
        }
    }

    internal void RunMessageLoop()
    {
        var forceCancel = false;
        var cancelled = false;

        var ct = _processCancel.Token;

        using var waitHandlesPin = new PinnedBuffer<nint[]>([
            DispatcherQueue.WaitHandle.SafeWaitHandle.DangerousGetHandle(),
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
                if (WindowManager.IsEmpty)
                {
                    _logger.LogWithLine(LogLevel.Information, "all closed.", Environment.CurrentManagedThreadId);
                    break;
                }
            }
            else if (ct.IsCancellationRequested)
            {
                cancelled = true;

                _logger.LogWithLine(LogLevel.Information, "canceled.", Environment.CurrentManagedThreadId);
                WindowManager.CloseAll();

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
                    WindowProcedure.DispatchMessage();
                    continue;
                }
                else if (res == 0) // WAIT_OBJECT_0
                {
                    //タスクキューのディスパッチ
                    //TODO RTWQなどに置き換えできる？
                    _logger.LogWithLine(LogLevel.Trace, "MsgWaitForMultipleObjectsEx comes queue event.", Environment.CurrentManagedThreadId);
                    DispatcherQueue.DispatchQueue();
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

    internal async ValueTask<TResult> DispatchAsync<TResult>(Func<TResult> func)
    {
        _logger.LogWithLine(LogLevel.Trace, "DispatchAsync", Environment.CurrentManagedThreadId);

        //TODO 即時実行か遅延実行かの判断はDispatcherQueueに移した方がよいか？
        if (_uiThreadId == Environment.CurrentManagedThreadId)
        {
            _logger.LogWithLine(LogLevel.Trace, "Dispatch called from same thread id then immidiate mode", Environment.CurrentManagedThreadId);
            return func();
        }

        var tcs = new TaskCompletionSource<TResult>(TaskCreationOptions.AttachedToParent | TaskCreationOptions.RunContinuationsAsynchronously);

        DispatcherQueue.Dispatch(() =>
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

    internal IWindow CreateWindow(
        string windowTitle,
        NativeWindow? parent,
        string className,
        User32.WNDCLASSEX.CS classStyle,
        IUIThread.OnMessage? onMessage,
        IUIThread.OnMessage? onMessageAfter
    )
    {
        var window =
            new NativeWindow(
                _loggerFactory,
                this,
                className,
                classStyle,
                onMessage,
                onMessageAfter
            );

        window.CreateWindow(
            windowTitle,
            parent
        );

        return window;
    }

    private void OnMessage(User32.HWND hwnd, IUIThread.IMessage message)
    {
        _logger.LogWithMsg(LogLevel.Trace, "OnMessage", User32.HWND.None, message, Environment.CurrentManagedThreadId);

        using var switchContext = new SwitchSynchronizationContextRAII(_windowContextSynchronizationContext, _logger);

        //ウインドウに流す
        WindowManager.OnMessage(hwnd, message);
    }

    private void OnThreadMessage(IUIThread.IMessage message)
    {
        _logger.LogWithMsg(LogLevel.Trace, "OnThreadMessage", User32.HWND.None, message, Environment.CurrentManagedThreadId);

        using var switchContext = new SwitchSynchronizationContextRAII(_windowContextSynchronizationContext, _logger);

        //TODO スレッドメッセージ用の処理

    }

    //TODO 実験中
    private sealed class WindowContextSynchronizationContext : SynchronizationContext
    {
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger _logger;

        private readonly DispatcherQueue _dispatcherQueue;

        internal WindowContextSynchronizationContext(
            ILoggerFactory loggerFactory,
            DispatcherQueue dispatcherQueue
        )
        {
            _loggerFactory = loggerFactory;
            _logger = _loggerFactory.CreateLogger<WindowContextSynchronizationContext>();
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
}
