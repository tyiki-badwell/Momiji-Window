using Microsoft.Extensions.Logging;
using Momiji.Internal.Log;

namespace Momiji.Core.Window;

internal sealed class UIThreadOperator : IUIThreadOperator
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private bool _disposed;

    private UIThread UIThread { get; }

    internal UIThreadOperator(
        ILoggerFactory loggerFactory,
        UIThread uiThread    
    )
    {
        _loggerFactory = loggerFactory;
        _logger = _loggerFactory.CreateLogger<UIThreadOperator>();

        UIThread = uiThread;
    }

    ~UIThreadOperator()
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

        await UIThread.CancelAsync();

        _logger.LogWithLine(LogLevel.Trace, "DisposeAsync end", Environment.CurrentManagedThreadId);
    }

    public async ValueTask<TResult> DispatchAsync<TResult>(Func<IWindowManager, TResult> item)
    {
        TResult func()
        {
            return item(UIThread.WindowManager);
        }

        return await UIThread.DispatcherQueue.DispatchAsync(func);
    }
}
