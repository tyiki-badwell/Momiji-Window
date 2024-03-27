using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Momiji.Internal.Log;
using Kernel32 = Momiji.Interop.Kernel32.NativeMethods;
using User32 = Momiji.Interop.User32.NativeMethods;

namespace Momiji.Core.Window;

internal sealed class UIThread : IUIThread
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private bool _disposed;

    private readonly Task _processTask;
    private readonly CancellationTokenSource _processCancel;

    private UIThreadActivator UIThreadActivator { get; }

    private WindowContext WindowContext { get; }

    private readonly IUIThreadFactory.Param _param;

    internal UIThread(
        IConfiguration configuration,
        ILoggerFactory loggerFactory,
        TaskCompletionSource startTcs,
        IUIThread.OnStop? onStop = default,
        IUIThread.OnUnhandledException? onUnhandledException = default
    )
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _loggerFactory = loggerFactory;
        _logger = _loggerFactory.CreateLogger<UIThread>();
        UIThreadActivator = new(_loggerFactory);
        WindowContext = new(_loggerFactory, UIThreadActivator, onUnhandledException);

        _param = new IUIThreadFactory.Param();
        configuration.GetSection($"{typeof(UIThread).FullName}").Bind(_param);

        _processCancel = new CancellationTokenSource();

        _processTask = Start(
            startTcs, 
            onStop
        );
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
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _logger.LogWithLine(LogLevel.Information, "disposing", Environment.CurrentManagedThreadId);
            DisposeAsyncCore().AsTask().GetAwaiter().GetResult();
        }

        _disposed = true;
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
        try
        {
            await CancelAsync().ConfigureAwait(false);
        }
        finally
        {
            //_desktop?.Close();
            //_windowStation?.Close();

            _processCancel.Dispose();
        }
        _logger.LogWithLine(LogLevel.Trace, "DisposeAsync end", Environment.CurrentManagedThreadId);
    }

    private async Task CancelAsync()
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

        try
        {
            await _processTask;
        }
        catch (Exception e)
        {
            _logger.LogWithLine(LogLevel.Error, e, "failed.", Environment.CurrentManagedThreadId);
        }
    }

    public async ValueTask<TResult> DispatchAsync<TResult>(Func<TResult> func)
    {
        CheckRunning();
        return await WindowContext.DispatchAsync(func);
    }

    private void CheckRunning()
    {
        _logger.LogWithLine(LogLevel.Information, $"CheckRunning {_processTask.Status}", Environment.CurrentManagedThreadId);

        if (!(_processTask.Status == TaskStatus.WaitingForActivation) || (_processTask.Status == TaskStatus.Running))
        {
            throw new InvalidOperationException("message loop is not exists.");
        }
    }

    public IWindow CreateWindow(
        string windowTitle,
        IWindow? parent,
        string className,
        IUIThread.OnMessage? onMessage,
        IUIThread.OnMessage? onMessageAfter
    )
    {
        CheckRunning();

        var classStyle = (className == string.Empty) 
                ? (User32.WNDCLASSEX.CS)_param.CS
                : User32.WNDCLASSEX.CS.NONE
                ;

        var window =
            new NativeWindow(
                _loggerFactory,
                WindowContext,
                className,
                classStyle,
                onMessage,
                onMessageAfter
            );

        window.CreateWindow(
            windowTitle,
            (parent as NativeWindow)
        );

        return window;
    }

    private Task<Task> Start(
        TaskCompletionSource startTcs,
        IUIThread.OnStop? onStop = default
    )
    {
        return Run(
            startTcs
        ).ContinueWith(async (task) => {
            //TODO このタスクでcontinue withすると、UIスレッドでQueue登録してスレッド終了し、QueueのCOMアクセスが失敗する

            _logger.LogWithLine(LogLevel.Information, $"process task end {task.Status} {_processTask?.Status}", Environment.CurrentManagedThreadId);

            //TODO エラー時のみキャンセルすればよいハズ？
            await CancelAsync().ConfigureAwait(false);
            _logger.LogWithLine(LogLevel.Trace, "cancel end", Environment.CurrentManagedThreadId);

            try
            {
                await task;
                onStop?.Invoke(default);
            }
            catch (Exception e)
            {
                //_processTaskのエラー状態を伝播する
                onStop?.Invoke(e);
            }
            finally
            {
                _logger.LogWithLine(LogLevel.Information, "stopped.", Environment.CurrentManagedThreadId);
            }
        });
    }

    private Task Run(
        TaskCompletionSource startTcs
    )
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.AttachedToParent | TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            _logger.LogWithLine(LogLevel.Information, "*** thread start ***", Environment.CurrentManagedThreadId);

            try
            {
                using var active = UIThreadActivator.Activate();

                WindowContext.RunMessageLoop(startTcs, _processCancel.Token);

                _logger.LogWithLine(LogLevel.Information, "message loop normal end.", Environment.CurrentManagedThreadId);

                //TODO DispatcherQueueをdisposeした後でSendOrPostCallbackが呼ばれる場合がある. 待つ方法？
            }
            catch (Exception e)
            {
                _logger.LogWithLine(LogLevel.Error, e, "message loop abnormal end.", Environment.CurrentManagedThreadId);

                startTcs.TrySetException(e);
                tcs.SetException(e);
            }

            tcs.TrySetResult();

            _logger.LogWithLine(LogLevel.Information, "*** thread end ***", Environment.CurrentManagedThreadId);
        })
        {
            IsBackground = false,
            Name = "Momiji UI Thread"
        };

        //TODO MTAも指定可能にしてみる？
        thread.SetApartmentState(ApartmentState.STA);
        _logger.LogWithLine(LogLevel.Information, $"GetApartmentState {thread.GetApartmentState()}", Environment.CurrentManagedThreadId);
        thread.Start();

        return tcs.Task;
    }
}

internal interface IUIThreadChecker
{
    bool IsActivatedThread
    {
        get;
    }
    void ThrowIfCalledFromOtherThread();
    void ThrowIfNoActive();
    uint NativeThreadId
    {
        get;
    }
}

internal class UIThreadActivator : IUIThreadChecker
{
    private readonly ILoggerFactory _loggerFactory;
    internal int _uiThreadId;
    public uint NativeThreadId
    {
        get; private set;
    }

    internal UIThreadActivator(
        ILoggerFactory loggerFactory
    )
    {
        _loggerFactory = loggerFactory;
    }

    internal IDisposable Activate()
    {
        return new Token(this);
    }

    public bool IsActivatedThread => (_uiThreadId == Environment.CurrentManagedThreadId);

    public void ThrowIfCalledFromOtherThread()
    {
        if (!IsActivatedThread)
        {
            throw new InvalidOperationException($"called from invalid thread id [activate:{_uiThreadId:X}] [current:{Environment.CurrentManagedThreadId:X}]");
        }
    }

    public void ThrowIfNoActive()
    {
        if (_uiThreadId == 0)
        {
            throw new InvalidOperationException($"no active");
        }
    }

    internal class Token : IDisposable
    {
        private readonly ILogger _logger;
        private readonly UIThreadActivator _activator;
        private bool _disposed;

        internal Token(
            UIThreadActivator activator
        )
        {
            _activator = activator;
            _logger = _activator._loggerFactory.CreateLogger<Token>();

            var threadId = Environment.CurrentManagedThreadId;

            if (0 != Interlocked.CompareExchange(ref _activator._uiThreadId, threadId, 0))
            {
                throw new InvalidOperationException($"already activated [uiThreadId:{_activator._uiThreadId}][now:{Environment.CurrentManagedThreadId}]");
            }
            _activator.NativeThreadId = Kernel32.GetCurrentThreadId();

            _logger.LogWithLine(LogLevel.Trace, $"DispatcherQueue Activate [uiThreadId:{_activator._uiThreadId}]", Environment.CurrentManagedThreadId);
        }

        ~Token()
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
                _logger.LogWithLine(LogLevel.Trace, $"DispatcherQueue Disactivate [uiThreadId:{_activator._uiThreadId}]", Environment.CurrentManagedThreadId);
                _activator._uiThreadId = 0;
                _activator.NativeThreadId = 0;
            }

            _disposed = true;
        }
    }
}
