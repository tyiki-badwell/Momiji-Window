using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Momiji.Internal.Log;

namespace Momiji.Core.Window;

public sealed partial class UIThreadFactory : IUIThreadFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private bool _disposed;

    private readonly ConcurrentBag<UIThreadRunner> _uiThreadBag = [];

    public UIThreadFactory(
        ILoggerFactory loggerFactory
    )
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _loggerFactory = loggerFactory;
        _logger = _loggerFactory.CreateLogger<UIThreadFactory>();
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
        var taskList = 
            _uiThreadBag
            .Select((uiThread, idx) => uiThread.DisposeAsync().AsTask())
            .ToList();

        try
        {
            await Task.WhenAll(taskList);
        }
        catch (Exception e)
        {
            _logger.LogWithLine(LogLevel.Error, e, "error", Environment.CurrentManagedThreadId);
        }

        //WindowDebug.PrintAtomNames(_logger);

        _logger.LogWithLine(LogLevel.Trace, "DisposeAsync end", Environment.CurrentManagedThreadId);
    }

    public async Task<IUIThread> StartAsync(
        IUIThreadFactory.OnStop? onStop = default,
        IUIThreadFactory.OnUnhandledException? onUnhandledException = default
    )
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _logger.LogWithLine(LogLevel.Information, "StartAsync", Environment.CurrentManagedThreadId);
        var tcs = new TaskCompletionSource<IUIThread>(TaskCreationOptions.AttachedToParent | TaskCreationOptions.RunContinuationsAsynchronously);

        var uiThreadRunner = new UIThreadRunner(
            _loggerFactory,
            tcs, 
            onStop, 
            onUnhandledException
        );
        var uiThread = await tcs.Task;
 
        _uiThreadBag.Add(uiThreadRunner);
        
        return uiThread;
    }
}