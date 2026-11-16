using System.Runtime.ExceptionServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Momiji.Internal.Window;

namespace Momiji.Core.Window;

internal sealed partial class UIThreadFactory : IUIThreadFactory, IDisposable, IAsyncDisposable
{
    private bool _disposed;
    private readonly ILogger<UIThreadFactory> _logger;

    private readonly IUIThreadMap _uiThreadMap;
    private readonly IOnUnhandledExceptionHandler _onUnhandledExceptionHandler;

    private readonly IServiceScopeFactory _serviceScopeFactory;

    public UIThreadFactory(
        ILogger<UIThreadFactory> logger,
        IUIThreadMap uiThreadMap,
        IOnUnhandledExceptionHandler onUnhandledExceptionHandler,
        IServiceScopeFactory serviceScopeFactory
    )
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(uiThreadMap);
        ArgumentNullException.ThrowIfNull(onUnhandledExceptionHandler);
        ArgumentNullException.ThrowIfNull(serviceScopeFactory);

        _logger = logger;
        _uiThreadMap = uiThreadMap;
        _onUnhandledExceptionHandler = onUnhandledExceptionHandler;

        _serviceScopeFactory = serviceScopeFactory;
    }

    ~UIThreadFactory()
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
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await DisposeAsyncCore().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _logger.LogWithLine(LogLevel.Error, e, "failed.", Environment.CurrentManagedThreadId);
            if (!_onUnhandledExceptionHandler.DoUnhandledException(e))
            {
                _logger.LogWithLine(LogLevel.Error, e, "onUnhandledException failed.", Environment.CurrentManagedThreadId);
            }
        }

        Dispose(false);

        GC.SuppressFinalize(this);
    }

    private async ValueTask DisposeAsyncCore()
    {
        _logger.LogWithLine(LogLevel.Trace, "DisposeAsync start", Environment.CurrentManagedThreadId);

        if (_uiThreadMap.IsEmpty)
        {
            _logger.LogWithLine(LogLevel.Trace, "_uiThreadMap.IsEmpty", Environment.CurrentManagedThreadId);
            return;
        }

        await _uiThreadMap.CancelAllAsync().ConfigureAwait(false);

        await Task.Run(async () => {
            while (true)
            {
                if (_uiThreadMap.IsEmpty)
                {
                    break;
                }

                await Task.Delay(1000);

                if (_logger.IsEnabled(LogLevel.Trace))
                {
                    _logger.LogWithLine(LogLevel.Trace, $"uiThreadMap left {_uiThreadMap.Count}", Environment.CurrentManagedThreadId);
                }
            }
        });
        _logger.LogWithLine(LogLevel.Trace, "DisposeAsync end", Environment.CurrentManagedThreadId);
    }

    public async Task<IUIThread> StartAsync(
        CancellationToken ct = default
    )
    {
        var startTcs = new TaskCompletionSource<IUIThread>(TaskCreationOptions.AttachedToParent | TaskCreationOptions.RunContinuationsAsynchronously);
        var task = StartAsyncCore(startTcs, ct);

        try
        {
            var uiThread = await startTcs.Task.ConfigureAwait(false);
            _uiThreadMap.Add(uiThread, task);
            return uiThread;
        }
        catch(Exception e)
        {
            _logger.LogWithLine(LogLevel.Error, e, "startTcs failed.", Environment.CurrentManagedThreadId);
            await task.ConfigureAwait(false);
            throw;
        }
    }

    private async Task StartAsyncCore(
        TaskCompletionSource<IUIThread> startTcs,
        CancellationToken ct
    )
    {
        Exception? runException = default;
        try
        {
            await RunAsync(startTcs, ct).ConfigureAwait(ConfigureAwaitOptions.None);
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogWithLine(LogLevel.Information, "process task end.", Environment.CurrentManagedThreadId);
            }

            //NOTE このタスクでcontinue withすると、UIスレッドでQueue登録してスレッド終了し、QueueのCOMアクセスが失敗する

        }
        catch (Exception e)
        {
            runException = e;
        }

        try
        {
            var uiThread = await startTcs.Task;
            _uiThreadMap.Remove(uiThread);
        }
        catch (Exception)
        {
            //NOP
        }

        _logger.LogWithLine(LogLevel.Information, "stopped.", Environment.CurrentManagedThreadId);

        if (runException != default)
        {
            //_processTaskのエラー状態を伝播する
            ExceptionDispatchInfo.Throw(runException);
        }
    }

    private async Task RunAsync(
        TaskCompletionSource<IUIThread> startTcs,
        CancellationToken ct
    )
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.AttachedToParent | TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(async () =>
        {
            _logger.LogWithLine(LogLevel.Information, "*** thread start ***", Environment.CurrentManagedThreadId);

            try
            {
                //scopeから取り出す
                await using var scope = _serviceScopeFactory.CreateAsyncScope();
                var uiThread = scope.ServiceProvider.GetRequiredService<IUIThreadInternal>();

                uiThread.RunMessageLoop(startTcs, ct);

                _logger.LogWithLine(LogLevel.Information, "message loop normal end.", Environment.CurrentManagedThreadId);
            }
            catch (Exception e)
            {
                _logger.LogWithLine(LogLevel.Error, e, "message loop abnormal end.", Environment.CurrentManagedThreadId);

                tcs.SetException(e);
                startTcs.TrySetException(e);
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
        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogWithLine(LogLevel.Information, $"GetApartmentState {thread.GetApartmentState()}", Environment.CurrentManagedThreadId);
        }
        thread.Start();

        await tcs.Task;
    }
}
