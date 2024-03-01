using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Momiji.Internal.Log;

namespace Momiji.Core.Window;

public class WindowManagerFactory : IWindowManagerFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private readonly IConfiguration _configuration;
    private bool _disposed;

    private readonly ConcurrentBag<WindowManager> _windowManagerBag = [];

    public WindowManagerFactory(
        IConfiguration configuration,
        ILoggerFactory loggerFactory
    )
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _loggerFactory = loggerFactory;
        _logger = _loggerFactory.CreateLogger<WindowManagerFactory>();

        _configuration = configuration;
    }

    ~WindowManagerFactory()
    {
        Dispose(false);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
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

    protected async virtual ValueTask DisposeAsyncCore()
    {
        _logger.LogWithLine(LogLevel.Trace, "DisposeAsync start", Environment.CurrentManagedThreadId);
        var taskList = new List<Task>();

        foreach (var windowManager in _windowManagerBag)
        {
            taskList.Add(windowManager.DisposeAsync().AsTask());
        }

        try
        {
            await Task.WhenAll(taskList);
        }
        catch (Exception e)
        {
            _logger.LogWithLine(LogLevel.Error, e, "error", Environment.CurrentManagedThreadId);
        }

        _logger.LogWithLine(LogLevel.Trace, "DisposeAsync end", Environment.CurrentManagedThreadId);
    }

    public async Task<IWindowManager> StartAsync(
        IWindowManager.OnStop? onStop = default,
        IWindowManager.OnUnhandledException? onUnhandledException = default
    )
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _logger.LogWithLine(LogLevel.Information, "StartAsync", Environment.CurrentManagedThreadId);
        var tcs = new TaskCompletionSource(TaskCreationOptions.AttachedToParent | TaskCreationOptions.RunContinuationsAsynchronously);

        var windowManager = new WindowManager(
            _configuration, 
            _loggerFactory, 
            tcs, 
            onStop, 
            onUnhandledException
        );
        await tcs.Task;
        _windowManagerBag.Add(windowManager);
        return windowManager;
    }
}