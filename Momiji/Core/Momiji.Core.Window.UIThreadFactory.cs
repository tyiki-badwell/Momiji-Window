using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Momiji.Internal.Log;

namespace Momiji.Core.Window;

public sealed class UIThreadFactory : IUIThreadFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private readonly IConfiguration _configuration;
    private bool _disposed;

    private readonly ConcurrentBag<UIThreadRunner> _uiThreadBag = [];

    public UIThreadFactory(
        IConfiguration configuration,
        ILoggerFactory loggerFactory
    )
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _loggerFactory = loggerFactory;
        _logger = _loggerFactory.CreateLogger<UIThreadFactory>();

        _configuration = configuration;
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

    public async Task<IUIThreadOperator> StartAsync(
        IUIThreadOperator.OnStop? onStop = default,
        IUIThreadOperator.OnUnhandledException? onUnhandledException = default
    )
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _logger.LogWithLine(LogLevel.Information, "StartAsync", Environment.CurrentManagedThreadId);
        var tcs = new TaskCompletionSource<IUIThreadOperator>(TaskCreationOptions.AttachedToParent | TaskCreationOptions.RunContinuationsAsynchronously);

        var uiThreadRunner = new UIThreadRunner(
            _loggerFactory,
            _configuration,
            tcs, 
            onStop, 
            onUnhandledException
        );
        var uiThread = await tcs.Task;
 
        _uiThreadBag.Add(uiThreadRunner);
        
        return uiThread;
    }
}