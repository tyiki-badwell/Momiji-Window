using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Momiji.Internal.Window;

namespace Momiji.Core.Window;

internal sealed partial class UIThreadFactory : IUIThreadFactory, IDisposable, IAsyncDisposable
{
    private bool _disposed;
    private readonly ILogger<UIThreadFactory> _logger;

    private readonly IUIThreadRunner _uiThreadRunner;

    private readonly IOnUnhandledExceptionHandler _onUnhandledExceptionHandler;

    //TODO ここで管理するのが妥当か？
    private readonly ConcurrentDictionary<IUIThread, Task> _uiThreadMap = [];

    public UIThreadFactory(
        ILogger<UIThreadFactory> logger,
        IUIThreadRunner uiThreadRunner,
        IOnUnhandledExceptionHandler onUnhandledExceptionHandler
    )
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(uiThreadRunner);
        ArgumentNullException.ThrowIfNull(onUnhandledExceptionHandler);

        _logger = logger;
        _uiThreadRunner = uiThreadRunner;

        _onUnhandledExceptionHandler = onUnhandledExceptionHandler;
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
        await DisposeAsyncCore().ConfigureAwait(false);

        Dispose(false);

        GC.SuppressFinalize(this);
    }

    private async ValueTask DisposeAsyncCore()
    {
        _logger.LogWithLine(LogLevel.Trace, "DisposeAsync start", Environment.CurrentManagedThreadId);

        //クローズしていないウインドウが残っていると失敗する
        var tasks = _uiThreadMap.Keys.Select(thread => thread.CancelAsync().AsTask()).ToArray();

        _logger.LogWithLine(LogLevel.Trace, "when all", Environment.CurrentManagedThreadId);
        await Task.WhenAll(tasks).ConfigureAwait(false);

        _logger.LogWithLine(LogLevel.Trace, "clear", Environment.CurrentManagedThreadId);
        _uiThreadMap.Clear();

        //WindowDebug.PrintAtomNames(_logger);

        _logger.LogWithLine(LogLevel.Trace, "DisposeAsync end", Environment.CurrentManagedThreadId);
    }

    public async Task<IUIThread> StartAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _logger.LogWithLine(LogLevel.Information, "StartAsync", Environment.CurrentManagedThreadId);

        var (uiThread, processTask) = await _uiThreadRunner.StartAsync();

        _uiThreadMap[uiThread] = processTask;
        var _ = processTask.ContinueWith(t =>
        {
            _uiThreadMap.TryRemove(uiThread, out var _);
            _logger.LogWithLine(LogLevel.Information, "UIThreadRunner processTask OnStop", Environment.CurrentManagedThreadId);
            if (t.Exception != null)
            {
                _logger.LogWithLine(LogLevel.Error, t.Exception, "UIThreadRunner processTask OnStop exception", Environment.CurrentManagedThreadId);
                if (!_onUnhandledExceptionHandler.DoUnhandledException(t.Exception))
                {
                    _logger.LogWithLine(LogLevel.Error, t.Exception, "onUnhandledException failed.", Environment.CurrentManagedThreadId);
                }
            }
        });

        return uiThread;
    }
}