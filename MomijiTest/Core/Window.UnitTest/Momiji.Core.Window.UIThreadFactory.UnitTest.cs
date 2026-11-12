using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Momiji.Core.Window;

namespace MomijiTest.Core.Window.UnitTest;

[TestClass]
public partial class UIThreadFactoryTest
{
    public UIThreadFactoryTest()
    {
    }

    internal class UIThreadDummy : IUIThreadInternal
    {
        public UIThreadDummy()
        {
        }
        public event IUIThread.InactivatedEventHandler? OnInactivated;

        public ValueTask CancelAsync() => ValueTask.CompletedTask;
        public void Dispose() => throw new NotImplementedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public ValueTask<TResult> EnqueueAsync<TResult>(Func<IWindowManager, TResult> item) => throw new NotImplementedException();
        public void RunMessageLoop(TaskCompletionSource<IUIThread> startTcs, CancellationToken ct = default) {
            startTcs.TrySetResult(this);
        }
    }

    internal class UIThreadDummyRunMessageLoopError : IUIThreadInternal
    {
        public UIThreadDummyRunMessageLoopError()
        {
        }
        public event IUIThread.InactivatedEventHandler? OnInactivated;

        public ValueTask CancelAsync() => ValueTask.CompletedTask;
        public void Dispose() => throw new NotImplementedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public ValueTask<TResult> EnqueueAsync<TResult>(Func<IWindowManager, TResult> item) => throw new NotImplementedException();
        public void RunMessageLoop(TaskCompletionSource<IUIThread> startTcs, CancellationToken ct = default)
        {
            throw new Exception("ERROR");
        }
    }

    private static IHost Setup()
    {
        var builder = Host.CreateDefaultBuilder(default);

        builder.UseContentRoot(AppContext.BaseDirectory);
        builder.UseConsoleLifetime();
        builder.ConfigureServices((hostContext, services) =>
        {
            services.AddSingleton<IOnUnhandledExceptionHandler, OnUnhandledExceptionHandler>();

            services.AddTransient<IUIThreadMap, UIThreadMap>();
            services.AddTransient<IUIThreadInternal, UIThreadDummy>();
            
        });

        return builder.Build();
    }

    [TestMethod]
    public async Task TestConstructor()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();

        var threadFactory = new UIThreadFactory(
            loggerFactory.CreateLogger<UIThreadFactory>(),
            host.Services.GetRequiredService<IUIThreadMap>(),
            host.Services.GetRequiredService<IServiceScopeFactory>()
        );
    }

    [TestMethod]
    public async Task TestStartAsync()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger<UIThreadFactoryTest>();

        var threadFactory = new UIThreadFactory(
            loggerFactory.CreateLogger<UIThreadFactory>(),
            host.Services.GetRequiredService<IUIThreadMap>(),
            host.Services.GetRequiredService<IServiceScopeFactory>()
        );

        var thread = await threadFactory.StartAsync();
    }

    private static IHost SetupForError()
    {
        var builder = Host.CreateDefaultBuilder(default);

        builder.UseContentRoot(AppContext.BaseDirectory);
        builder.UseConsoleLifetime();
        builder.ConfigureServices((hostContext, services) =>
        {
            services.AddSingleton<IOnUnhandledExceptionHandler, OnUnhandledExceptionHandler>();

            services.AddTransient<IUIThreadMap, UIThreadMap>();
            services.AddTransient<IUIThreadInternal, UIThreadDummyRunMessageLoopError>();

        });

        return builder.Build();
    }

    [TestMethod]
    public async Task TestStartAsyncError()
    {
        using var host = SetupForError();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger<UIThreadFactoryTest>();

        var threadFactory = new UIThreadFactory(
            loggerFactory.CreateLogger<UIThreadFactory>(),
            host.Services.GetRequiredService<IUIThreadMap>(),
            host.Services.GetRequiredService<IServiceScopeFactory>()
        );

        try
        {
            var thread = await threadFactory.StartAsync();
            Assert.Fail();
        }
        catch (Exception e)
        {
            Assert.AreEqual("ERROR", e.Message);
        }
    }
}
