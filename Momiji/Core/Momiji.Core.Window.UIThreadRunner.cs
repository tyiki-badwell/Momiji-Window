using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Logging;
using Momiji.Internal.Log;

namespace Momiji.Core.Window;

internal interface IUIThreadRunner: IDisposable, IAsyncDisposable
{
    delegate void OnStopHandler(IUIThreadRunner sender, Exception? exception);
    event OnStopHandler OnStop;

    Task<IUIThread> StartAsync();
}

internal sealed partial class UIThreadRunner : IUIThreadRunner
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private bool _disposed;

    private CancellationTokenSource? _processCancel;
    private Task? _processTask;

    public event IUIThreadRunner.OnStopHandler? OnStop;

    private readonly IUIThreadFactory.OnUnhandledExceptionHandler? _onUnhandledException;

    internal UIThreadRunner(
        ILoggerFactory loggerFactory,
        IUIThreadFactory.OnUnhandledExceptionHandler? onUnhandledException = default
    )
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _loggerFactory = loggerFactory;
        _logger = _loggerFactory.CreateLogger<UIThreadRunner>();

        _onUnhandledException = onUnhandledException;
    }

    ~UIThreadRunner()
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
        await CancelAsync().ConfigureAwait(false);
        _logger.LogWithLine(LogLevel.Trace, "DisposeAsync end", Environment.CurrentManagedThreadId);
    }

    private async Task CancelAsync()
    {
        if ((_processCancel == default) || (_processTask == default))
        {
            _logger.LogWithLine(LogLevel.Trace, "already stopped.", Environment.CurrentManagedThreadId);
            return;
        }

        if (_processCancel.IsCancellationRequested)
        {
            _logger.LogWithLine(LogLevel.Information, "already cancelled.", Environment.CurrentManagedThreadId);
        }
        else
        {
            _logger.LogWithLine(LogLevel.Information, "cancel.", Environment.CurrentManagedThreadId);
            _processCancel.Cancel();
        }

        Exception? unhandledException = default;
        try
        {
            await _processTask.ConfigureAwait(ConfigureAwaitOptions.None);
        }
        catch (Exception e)
        {
            _logger.LogWithLine(LogLevel.Error, e, "failed.", Environment.CurrentManagedThreadId);
            unhandledException = e;
        }

        if (unhandledException != default)
        {
            try
            {
                _logger.LogWithLine(LogLevel.Trace, "call onUnhandledException", Environment.CurrentManagedThreadId);
                _onUnhandledException?.Invoke(unhandledException);
            }
            catch (Exception e)
            {
                _logger.LogWithLine(LogLevel.Error, e, "onUnhandledException failed.", Environment.CurrentManagedThreadId);
            }
        }
    }

    public async Task<IUIThread> StartAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_processCancel != default)
        {
            throw new InvalidOperationException("already started.");
        }
        _processCancel = new CancellationTokenSource();

        var tcs = new TaskCompletionSource<IUIThread>(TaskCreationOptions.AttachedToParent | TaskCreationOptions.RunContinuationsAsynchronously);
        _processTask = Start(tcs).ContinueWith((task) => {
            _logger.LogWithLine(LogLevel.Trace, "process task continue with start", Environment.CurrentManagedThreadId);
            
            _processTask = default;

            _processCancel.Dispose();
            _processCancel = default;

            _logger.LogWithLine(LogLevel.Trace, "process task continue with end", Environment.CurrentManagedThreadId);
        });

        return await tcs.Task;
    }

    private async Task Start(
        TaskCompletionSource<IUIThread> startTcs
    )
    {
        Exception? runException = default;
        try
        {
            await Run(startTcs).ConfigureAwait(ConfigureAwaitOptions.None);
            _logger.LogWithLine(LogLevel.Information, $"process task end [process:{_processTask?.Status}]", Environment.CurrentManagedThreadId);

            //NOTE このタスクでcontinue withすると、UIスレッドでQueue登録してスレッド終了し、QueueのCOMアクセスが失敗する
        }
        catch (Exception e)
        {
            runException = e;
        }

        try
        {
            _logger.LogWithLine(LogLevel.Trace, "call onStop", Environment.CurrentManagedThreadId);
            OnStop?.Invoke(this, runException);
        }
        catch (Exception e)
        {
            _logger.LogWithLine(LogLevel.Error, e, "onStop failed.", Environment.CurrentManagedThreadId);
        }

        _logger.LogWithLine(LogLevel.Information, "stopped.", Environment.CurrentManagedThreadId);

        if (runException != default)
        {
            //_processTaskのエラー状態を伝播する
            ExceptionDispatchInfo.Throw(runException);
        }
    }

    private Task Run(
        TaskCompletionSource<IUIThread> startTcs
    )
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.AttachedToParent | TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(async () =>
        {
            _logger.LogWithLine(LogLevel.Information, "*** thread start ***", Environment.CurrentManagedThreadId);

            try
            {
                //TODO scopeから取り出す

                var uiThreadChecker = new UIThreadActivator(_loggerFactory);
                using var dispatcherQueue = new DispatcherQueue(_loggerFactory, uiThreadChecker);
                dispatcherQueue.OnUnhandledException += _onUnhandledException;
                using var windowManager = new WindowManager(_loggerFactory, uiThreadChecker, dispatcherQueue);

                await using var uiThread = new UIThread(
                    _loggerFactory,
                    uiThreadChecker,
                    dispatcherQueue,
                    windowManager
                );

                uiThread.RunMessageLoop(startTcs, _processCancel!.Token);

                _logger.LogWithLine(LogLevel.Information, "message loop normal end.", Environment.CurrentManagedThreadId);
            }
            catch (Exception e)
            {
                //TODO エラーになるケースがあるか？
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
