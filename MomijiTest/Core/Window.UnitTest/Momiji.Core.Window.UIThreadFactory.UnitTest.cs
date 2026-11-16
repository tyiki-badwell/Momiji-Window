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

        public IWindowManager WindowManager => throw new NotImplementedException();

        public event IUIThread.InactivatedEventHandler? OnInactivated;

        public ValueTask CancelAsync() => ValueTask.CompletedTask;
        public void Dispose() => throw new NotImplementedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public ValueTask<TResult> EnqueueAsync<TResult>(Func<TResult> item) => throw new NotImplementedException();
        public void RunMessageLoop(TaskCompletionSource<IUIThread> startTcs, CancellationToken ct = default) {
            startTcs.TrySetResult(this);
        }
    }

    internal class UIThreadDummyRunMessageLoopError : IUIThreadInternal
    {
        public UIThreadDummyRunMessageLoopError()
        {
        }

        public IWindowManager WindowManager => throw new NotImplementedException();

        public event IUIThread.InactivatedEventHandler? OnInactivated;

        public ValueTask CancelAsync() => ValueTask.CompletedTask;
        public void Dispose() => throw new NotImplementedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public ValueTask<TResult> EnqueueAsync<TResult>(Func<TResult> item) => throw new NotImplementedException();

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

        using var threadFactory = new UIThreadFactory(
            host.Services.GetRequiredService<ILogger<UIThreadFactory>>(),
            host.Services.GetRequiredService<IUIThreadMap>(),
            host.Services.GetRequiredService<IOnUnhandledExceptionHandler>(),
            host.Services.GetRequiredService<IServiceScopeFactory>()
        );
    }

    [TestMethod]
    public async Task TestDisposeAsync()
    {
        using var host = Setup();

        await using var threadFactory = new UIThreadFactory(
            host.Services.GetRequiredService<ILogger<UIThreadFactory>>(),
            host.Services.GetRequiredService<IUIThreadMap>(),
            host.Services.GetRequiredService<IOnUnhandledExceptionHandler>(),
            host.Services.GetRequiredService<IServiceScopeFactory>()
        );
    }

    [TestMethod]
    public async Task TestFinalizer()
    {
        {
            using var host = Setup();

            void Run()
            {
                var _ = new UIThreadFactory(
                    host.Services.GetRequiredService<ILogger<UIThreadFactory>>(),
                    host.Services.GetRequiredService<IUIThreadMap>(),
                    host.Services.GetRequiredService<IOnUnhandledExceptionHandler>(),
                    host.Services.GetRequiredService<IServiceScopeFactory>()
                );
            }

            Run();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    [TestMethod]
    public async Task TestDoubleDestruct()
    {
        using var host = Setup();

        var threadFactory = new UIThreadFactory(
            host.Services.GetRequiredService<ILogger<UIThreadFactory>>(),
            host.Services.GetRequiredService<IUIThreadMap>(),
            host.Services.GetRequiredService<IOnUnhandledExceptionHandler>(),
            host.Services.GetRequiredService<IServiceScopeFactory>()
        );
        threadFactory.Dispose();
        threadFactory.Dispose();
    }

    [TestMethod]
    public async Task TestStartAsync()
    {
        using var host = Setup();

        using var threadFactory = new UIThreadFactory(
            host.Services.GetRequiredService<ILogger<UIThreadFactory>>(),
            host.Services.GetRequiredService<IUIThreadMap>(),
            host.Services.GetRequiredService<IOnUnhandledExceptionHandler>(),
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

        using var threadFactory = new UIThreadFactory(
            host.Services.GetRequiredService<ILogger<UIThreadFactory>>(),
            host.Services.GetRequiredService<IUIThreadMap>(),
            host.Services.GetRequiredService<IOnUnhandledExceptionHandler>(),
            host.Services.GetRequiredService<IServiceScopeFactory>()
        );

        Exception? e = default;
        try
        {
            var thread = await threadFactory.StartAsync();
            Assert.Fail();
        }
        catch (Exception e_)
        {
            e = e_;
        }
        Assert.AreEqual("ERROR", e?.Message);
    }
}
