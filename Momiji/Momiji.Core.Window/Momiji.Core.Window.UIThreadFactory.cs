using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Momiji.Internal.Window;

namespace Momiji.Core.Window;

public sealed partial class UIThreadFactory : IUIThreadFactory, IDisposable, IAsyncDisposable
{
    private bool _disposed;
    private readonly ILogger<UIThreadFactory> _logger;

    private readonly IServiceScopeFactory _serviceScopeFactory;

    private readonly ConcurrentBag<IServiceScope> _scopeBag = [];

    public UIThreadFactory(
        ILogger<UIThreadFactory> logger,
        IServiceScopeFactory serviceScopeFactory
    )
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(serviceScopeFactory);

        _logger = logger;
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

        foreach (var scope in _scopeBag)
        {
            //クローズしていないウインドウが残っていると失敗する
            scope.Dispose();
        }

        _scopeBag.Clear();

        //WindowDebug.PrintAtomNames(_logger);

        _logger.LogWithLine(LogLevel.Trace, "DisposeAsync end", Environment.CurrentManagedThreadId);
    }

    public async Task<IUIThread> StartAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _logger.LogWithLine(LogLevel.Information, "StartAsync", Environment.CurrentManagedThreadId);

        var scope = _serviceScopeFactory.CreateScope();

        var uiThreadRunner = scope.ServiceProvider.GetRequiredService<IUIThreadRunner>();

        _scopeBag.Add(scope);

        uiThreadRunner.OnStop += (sender, e) => {
            //削除
            scope.Dispose();
        };

        var uiThread = await uiThreadRunner.StartAsync();
 
        
        return uiThread;
    }
}