using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Momiji.Internal.Window;

namespace Momiji.Core.Window;

internal interface IUIThreadMap
{
    ValueTask CancelAllAsync();

    void Add(IUIThread uiThread, Task task);

    void Remove(IUIThread uiThread);
}

internal sealed partial class UIThreadMap : IUIThreadMap, IDisposable, IAsyncDisposable
{
    private bool _disposed;

    private readonly ILogger<UIThreadMap> _logger;

    private readonly IOnUnhandledExceptionHandler _onUnhandledExceptionHandler;

    private readonly ConcurrentDictionary<IUIThread, Task> _uiThreadMap = [];

    public UIThreadMap(
        ILogger<UIThreadMap> logger,
        IOnUnhandledExceptionHandler onUnhandledExceptionHandler
    )
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(onUnhandledExceptionHandler);

        _logger = logger;
        _onUnhandledExceptionHandler = onUnhandledExceptionHandler;
    }

    ~UIThreadMap()
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

        await CancelAllAsync();

        _logger.LogWithLine(LogLevel.Trace, "DisposeAsync end", Environment.CurrentManagedThreadId);
    }

    public void Add(IUIThread uiThread, Task task)
    {
        _uiThreadMap.TryAdd(uiThread, task);
    }

    public void Remove(IUIThread uiThread)
    {
        _uiThreadMap.TryRemove(uiThread, out var _);
    }

    public async ValueTask CancelAllAsync()
    {
        var tasks = _uiThreadMap.Keys.Select(thread => thread.CancelAsync().AsTask()).ToArray();

        _logger.LogWithLine(LogLevel.Trace, "when all", Environment.CurrentManagedThreadId);
        await Task.WhenAll(tasks).ConfigureAwait(false);

        _logger.LogWithLine(LogLevel.Trace, "clear", Environment.CurrentManagedThreadId);
        _uiThreadMap.Clear();
    }

}
