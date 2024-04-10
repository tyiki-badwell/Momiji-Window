﻿using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Momiji.Internal.Log;

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

    internal UIThread(
        ILoggerFactory loggerFactory,
        IConfiguration configuration,
        TaskCompletionSource startTcs,
        IUIThread.OnStop? onStop = default,
        IUIThread.OnUnhandledException? onUnhandledException = default
    )
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(configuration);

        _loggerFactory = loggerFactory;
        _logger = _loggerFactory.CreateLogger<UIThread>();
        UIThreadActivator = new(_loggerFactory);
        WindowContext = new(_loggerFactory, configuration, UIThreadActivator, onUnhandledException);

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
            WindowContext.Dispose();
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

    public async ValueTask<TResult> DispatchAsync<TResult>(Func<IWindowManager, TResult> func)
    {
        UIThreadActivator.ThrowIfNoActive();
        CheckRunning();
        return await WindowContext.DispatchAsync(func);
    }

    private void CheckRunning()
    {
        _logger.LogWithLine(LogLevel.Trace, $"CheckRunning {_processTask.Status}", Environment.CurrentManagedThreadId);

        if (!(_processTask.Status == TaskStatus.WaitingForActivation) || (_processTask.Status == TaskStatus.Running))
        {
            throw new InvalidOperationException("message loop is not exists.");
        }
    }

    private Task Start(
        TaskCompletionSource startTcs,
        IUIThread.OnStop? onStop = default
    )
    {
        return Run(
            startTcs
        ).ContinueWith((task) => {
            //TODO このタスクでcontinue withすると、UIスレッドでQueue登録してスレッド終了し、QueueのCOMアクセスが失敗する

            _logger.LogWithLine(LogLevel.Information, $"process task end [continues:{task.Status}][process:{_processTask?.Status}]", Environment.CurrentManagedThreadId);

            try
            {
                task.Wait();
                _logger.LogWithLine(LogLevel.Trace, "call on stop", Environment.CurrentManagedThreadId);
                onStop?.Invoke(default);
            }
            catch (Exception e)
            {
                //_processTaskのエラー状態を伝播する
                _logger.LogWithLine(LogLevel.Trace, e, "error on stop", Environment.CurrentManagedThreadId);
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
