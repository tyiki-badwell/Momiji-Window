using Microsoft.Extensions.Logging;
using Momiji.Internal.Log;

namespace Momiji.Core.Window;

internal interface IUIThreadRunner: IDisposable, IAsyncDisposable
{
}

internal sealed partial class UIThreadRunner : IUIThreadRunner
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private bool _disposed;

    private readonly Task _processTask;

    private readonly CancellationTokenSource _processCancel;
    private readonly IUIThreadFactory.OnStop? _onStop;
    private readonly IUIThreadFactory.OnUnhandledException? _onUnhandledException;

    internal UIThreadRunner(
        ILoggerFactory loggerFactory,
        TaskCompletionSource<IUIThread> startTcs,
        IUIThreadFactory.OnStop? onStop = default,
        IUIThreadFactory.OnUnhandledException? onUnhandledException = default
    )
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _loggerFactory = loggerFactory;
        _logger = _loggerFactory.CreateLogger<UIThreadRunner>();

        _onStop = onStop;
        _onUnhandledException = onUnhandledException;

        _processCancel = new CancellationTokenSource();

        _processTask = Start(startTcs);
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
        try
        {
            await CancelAsync().ConfigureAwait(false);
        }
        finally
        {
            //_desktop?.Close();
            //_windowStation?.Close();
            //WindowContext.Dispose();
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
            await _processTask.ConfigureAwait(ConfigureAwaitOptions.None);
        }
        catch (Exception e)
        {
            _logger.LogWithLine(LogLevel.Error, e, "failed.", Environment.CurrentManagedThreadId);
            _onUnhandledException?.Invoke(e);
        }
    }

    private async Task Start(
        TaskCompletionSource<IUIThread> startTcs //TODO IProgressの方が良い？
    )
    {
        //TODO task作りすぎ？

        var tcs = new TaskCompletionSource(TaskCreationOptions.AttachedToParent | TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            try
            {
                await Run(startTcs).ConfigureAwait(ConfigureAwaitOptions.None);
                _logger.LogWithLine(LogLevel.Information, $"process task end [process:{_processTask?.Status}]", Environment.CurrentManagedThreadId);

                //TODO このタスクでcontinue withすると、UIスレッドでQueue登録してスレッド終了し、QueueのCOMアクセスが失敗する

                _logger.LogWithLine(LogLevel.Trace, "call on stop", Environment.CurrentManagedThreadId);
                _onStop?.Invoke(default);

                tcs.SetResult();
            }
            catch (Exception e)
            {
                //_processTaskのエラー状態を伝播する
                _logger.LogWithLine(LogLevel.Trace, e, "error on stop", Environment.CurrentManagedThreadId);
                _onStop?.Invoke(e);

                throw;
            }
        }
        catch (Exception e)
        {
            tcs.SetException(e);
        }
        finally
        {
            _logger.LogWithLine(LogLevel.Information, "stopped.", Environment.CurrentManagedThreadId);
        }

        await tcs.Task.ConfigureAwait(ConfigureAwaitOptions.None);
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
                await using var uiThread = new UIThread(
                    _loggerFactory, 
                    _onUnhandledException
                );

                uiThread.RunMessageLoop(startTcs, _processCancel.Token);

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
