using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Momiji.Core.Buffer;
using Momiji.Internal.Window;
using Kernel32 = Momiji.Interop.Kernel32.NativeMethods;
using User32 = Momiji.Interop.User32.NativeMethods;

namespace Momiji.Core.Window;

internal interface IUIThreadInternal : IUIThread, IDisposable, IAsyncDisposable
{
    void RunMessageLoop(
        TaskCompletionSource<IUIThread> startTcs,
        CancellationToken ct = default
    );
}

internal sealed partial class UIThread : IUIThreadInternal
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<UIThread> _logger;
    private bool _disposed;

    public event IUIThread.InactivatedEventHandler? OnInactivated;

    private readonly TaskCompletionSource _tcs = new(TaskCreationOptions.AttachedToParent | TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _cts = new();
    private bool _run;

    private readonly IUIThreadActivator _uiThreadActivator;
    private readonly IDispatcherQueue _dispatcherQueue;
    private readonly IDispatcherQueueTaskExecutor _dispatcherQueueTaskExecutor;
    private readonly IDispatcherQueueSynchronizationContext _dispatcherQueueSynchronizationContext;
    private readonly IWindowManagerInternal _windowManager;
    private readonly IOnUnhandledExceptionHandler _onUnhandledExceptionHandler;

    private readonly ManualResetEventSlim _queueEvent = new();
    public WaitHandle WaitHandle => _queueEvent.WaitHandle;


    public UIThread(
        ILoggerFactory loggerFactory,
        IUIThreadActivator uiThreadActivator,
        IDispatcherQueue dispatcherQueue,
        IDispatcherQueueTaskExecutor dispatcherQueueTaskExecutor,
        IDispatcherQueueSynchronizationContext dispatcherQueueSynchronizationContext,
        IWindowManagerInternal windowManager,
        IOnUnhandledExceptionHandler onUnhandledExceptionHandler
    )
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(uiThreadActivator);
        ArgumentNullException.ThrowIfNull(dispatcherQueue);
        ArgumentNullException.ThrowIfNull(dispatcherQueueTaskExecutor);
        ArgumentNullException.ThrowIfNull(dispatcherQueueSynchronizationContext);
        ArgumentNullException.ThrowIfNull(windowManager);
        ArgumentNullException.ThrowIfNull(onUnhandledExceptionHandler);

        _loggerFactory = loggerFactory;
        _logger = _loggerFactory.CreateLogger<UIThread>();

        _uiThreadActivator = uiThreadActivator;
        _uiThreadActivator.OnInactivated += OnInactivatedBridge;

        _dispatcherQueue = dispatcherQueue;
        _dispatcherQueue.OnEnqueue += OnEnqueue;
        _dispatcherQueueSynchronizationContext = dispatcherQueueSynchronizationContext;

        _dispatcherQueueTaskExecutor = dispatcherQueueTaskExecutor;
        //RTWorkQueueManager = new(configuration, _loggerFactory);
        _windowManager = windowManager;

        _onUnhandledExceptionHandler = onUnhandledExceptionHandler;
    }

    ~UIThread()
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
            var task = DisposeAsyncCore().AsTask();

            //TODO 仮処理
            try
            {
                var retry = 5;
                while (retry-- > 0)
                {
                    if (task.Wait(1000))
                    {
                        break;
                    }
                }

                if (!task.IsCompleted)
                {
                    throw new InvalidOperationException("process task is not completed.");
                }

                task.GetAwaiter().GetResult();
            }
            catch (Exception e)
            {
                _logger.LogWithLine(LogLevel.Error, e, "failed.", Environment.CurrentManagedThreadId);
                if (!_onUnhandledExceptionHandler.DoUnhandledException(e))
                {
                    _logger.LogWithLine(LogLevel.Error, e, "onUnhandledException failed.", Environment.CurrentManagedThreadId);
                }
            }

            _uiThreadActivator.OnInactivated -= OnInactivatedBridge;
            _cts.Dispose();

            _dispatcherQueue.OnEnqueue -= OnEnqueue;
            _queueEvent.Dispose();
        }

    }

    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore().ConfigureAwait(false);

        Dispose(false);

        GC.SuppressFinalize(this);
    }

    private async ValueTask DisposeAsyncCore()
    {
        _logger.LogWithLine(LogLevel.Trace, "DisposeAsync start", Environment.CurrentManagedThreadId);

        await CancelAsync();

        _cts.Dispose();

        _logger.LogWithLine(LogLevel.Trace, "DisposeAsync end", Environment.CurrentManagedThreadId);
    }

    public async ValueTask CancelAsync()
    {
        if (!_cts.IsCancellationRequested)
        {
            _logger.LogWithLine(LogLevel.Trace, "cancel", Environment.CurrentManagedThreadId);
            _cts.Cancel();
        }
        else
        {
            _logger.LogWithLine(LogLevel.Trace, "already cancelled", Environment.CurrentManagedThreadId);
        }

        if (_run)
        {
            try
            {
                //ループの終了を待つ
                _logger.LogWithLine(LogLevel.Trace, "await task start", Environment.CurrentManagedThreadId);
                await _tcs.Task;
                _logger.LogWithLine(LogLevel.Trace, "await task end", Environment.CurrentManagedThreadId);
            }
            catch (Exception e)
            {
                //TODO エラーになるケースがあるか？
                _logger.LogWithLine(LogLevel.Error, e, "message loop error", Environment.CurrentManagedThreadId);
            }
        }
        else
        {
            _logger.LogWithLine(LogLevel.Trace, "none task", Environment.CurrentManagedThreadId);
        }
    }

    private void OnInactivatedBridge()
    {
        _logger.LogWithLine(LogLevel.Trace, "OnInactivated", Environment.CurrentManagedThreadId);
        OnInactivated?.Invoke();
    }

    private void OnEnqueue()
    {
        _logger.LogWithLine(LogLevel.Trace, "OnEnqueue", Environment.CurrentManagedThreadId);
        _queueEvent.Set();
    }

    public async ValueTask<TResult> EnqueueAsync<TResult>(Func<IWindowManager, TResult> func)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return await _dispatcherQueue.EnqueueAsync(() => func(_windowManager));
    }

    public void RunMessageLoop(
        TaskCompletionSource<IUIThread> startTcs,
        CancellationToken ct = default
    )
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, ct);
        var linkedCt = linkedCts.Token;

        using var waitHandlesPin = new PinnedBuffer<nint[]>([
            _queueEvent.WaitHandle.SafeWaitHandle.DangerousGetHandle(),
            linkedCt.WaitHandle.SafeWaitHandle.DangerousGetHandle()
        ]);
        var handleCount = (uint)waitHandlesPin.Target.Length;

        Setup();

        _logger.LogWithLine(LogLevel.Information, "start message loop", Environment.CurrentManagedThreadId);

        try
        {
            using var active = _uiThreadActivator.Activate();
            _run = true;

            startTcs.SetResult(this);

            RunMessageLoopMain(
                waitHandlesPin,
                linkedCt
            );

            //TODO ここを抜けてactiveを破棄した後でSendOrPostCallbackが呼ばれる場合がある. 待つ方法？

        }
        catch (Exception e)
        {
            //TODO エラーになるケースがあるか？
            _tcs.SetException(e);
        }
        finally
        {
            _tcs.TrySetResult();
            _run = false;
        }

        _logger.LogWithLine(LogLevel.Information, "end message loop.", Environment.CurrentManagedThreadId);
    }

    private void RunMessageLoopMain(
        PinnedBuffer<nint[]> waitHandlesPin,
        CancellationToken ct    
    )
    {
        using var switchContext = new DispatcherQueueSynchronizationContextScope(_dispatcherQueueSynchronizationContext, _logger);

        var forceCancel = false;
        var cancelled = false;
        var handleCount = (uint)waitHandlesPin.Target.Length;

        while (true)
        {
            if (forceCancel)
            {
                _logger.LogWithLine(LogLevel.Information, "force canceled.", Environment.CurrentManagedThreadId);
                break;
            }

            if (cancelled)
            {
                if (_windowManager.IsEmpty)
                {
                    _logger.LogWithLine(LogLevel.Information, "all closed.", Environment.CurrentManagedThreadId);
                    break;
                }
                else
                {
                    _logger.LogWithLine(LogLevel.Information, "wait close.", Environment.CurrentManagedThreadId);
                }
            }
            else if (ct.IsCancellationRequested)
            {
                cancelled = true;

                _logger.LogWithLine(LogLevel.Information, "canceled.", Environment.CurrentManagedThreadId);
                _windowManager.CloseAll();

                // 10秒以内にクローズされなければ、ループを終わらせる
                //TODO 設定にする
                var _ =
                    Task.Delay(10000, CancellationToken.None)
                    .ContinueWith(
                        (task) => {
                            _logger.LogWithLine(LogLevel.Warning, "forceCancel", Environment.CurrentManagedThreadId);
                            forceCancel = true; 
                        },
                        TaskScheduler.Default
                    );
            }

            {
                _logger.LogWithLine(LogLevel.Trace, "MsgWaitForMultipleObjectsEx", Environment.CurrentManagedThreadId);
                var res =
                    User32.MsgWaitForMultipleObjectsEx(
                        handleCount,
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
                    _windowManager.WindowProcedure.DispatchMessage();
                    continue;
                }
                else if (res == 0) // WAIT_OBJECT_0
                {
                    //タスクキューのディスパッチ
                    //TODO RTWQなどに置き換えできる？
                    _logger.LogWithLine(LogLevel.Trace, "MsgWaitForMultipleObjectsEx comes queue event.", Environment.CurrentManagedThreadId);
                    _queueEvent.Reset();
                    _dispatcherQueueTaskExecutor.DispatchQueue();
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
                    throw new Win32Exception($"MsgWaitForMultipleObjectsEx [result:{res}]");
                }
            }
        }
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
                var errorCode = Marshal.GetLastPInvokeError();
                var error = new Win32Exception(errorCode);
                throw error;
            }
        }

        { //メッセージキューが無ければ作られるハズ
            var result =
                User32.GetQueueStatus(
                    0x04FF //QS_ALLINPUT
                );
            var errorCode = Marshal.GetLastPInvokeError();
            if (errorCode == 0)
            {
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogWithError(LogLevel.Information, $"GetQueueStatus ok {result}", errorCode, Environment.CurrentManagedThreadId);
                }
            }
            else
            {
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogWithError(LogLevel.Information, $"GetQueueStatus error {result}", Marshal.GetPInvokeErrorMessage(errorCode), Environment.CurrentManagedThreadId);
                }
            }
        }

        {
            var si = new Kernel32.STARTUPINFOW()
            {
                cb = Marshal.SizeOf<Kernel32.STARTUPINFOW>()
            };
            Kernel32.GetStartupInfoW(ref si);
            var errorCode = Marshal.GetLastPInvokeError();
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogWithError(LogLevel.Information, $"GetStartupInfoW [dwFlags:{si.dwFlags}][wShowWindow:{si.wShowWindow}]", errorCode, Environment.CurrentManagedThreadId);
            }
        }
    }

}
