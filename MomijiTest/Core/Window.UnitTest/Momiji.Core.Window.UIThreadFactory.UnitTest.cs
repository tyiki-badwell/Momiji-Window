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

    private static IHost Setup()
    {
        var builder = Host.CreateDefaultBuilder(default);

        builder.UseContentRoot(AppContext.BaseDirectory);
        builder.UseConsoleLifetime();
        builder.ConfigureServices((hostContext, services) =>
        {
            services.AddSingleton<IOnUnhandledExceptionHandler, OnUnhandledExceptionHandler>();
            services.AddSingleton<IUIThreadFactory, UIThreadFactory>();

            services.AddTransient<IUIThreadRunner, UIThreadRunner>();

            services.AddScoped<IUIThreadInternal, UIThread>();
            services.AddScoped<IUIThreadActivator, UIThreadActivator>();
            services.AddScoped<IDispatcherQueue, DispatcherQueue>();
            services.AddScoped<IDispatcherQueueTaskExecutor, DispatcherQueueTaskExecutor>();
            services.AddScoped<IDispatcherQueueSynchronizationContext, DispatcherQueueSynchronizationContext>();
            services.AddScoped<IWindowManagerInternal, WindowManager>();
            services.AddScoped<IWindowProcedure, WindowProcedure>();
            services.AddScoped<IWindowClassManager, WindowClassManager>();
        });

        return builder.Build();
    }

    [TestMethod]
    public async Task TestConstructor()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var factory = host.Services.GetRequiredService<IServiceScopeFactory>();

        using var threadFactory = (IDisposable)host.Services.GetRequiredService<IUIThreadFactory>();
    }

    [TestMethod]
    public async Task TestConstructorAsync()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var factory = host.Services.GetRequiredService<IServiceScopeFactory>();

        await using var threadFactory = (IAsyncDisposable)host.Services.GetRequiredService<IUIThreadFactory>();
    }

    [TestMethod]
    public async Task TestFinalizer()
    {
        static void run()
        {
            using var host = Setup();
            var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
            var factory = host.Services.GetRequiredService<IServiceScopeFactory>();

            var _ = host.Services.GetRequiredService<IUIThreadFactory>();
        }

        run();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    [TestMethod]
    public async Task TestDoubleDestruct()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var factory = host.Services.GetRequiredService<IServiceScopeFactory>();

        var threadFactory = (IDisposable)host.Services.GetRequiredService<IUIThreadFactory>();
        threadFactory.Dispose();
        threadFactory.Dispose();
    }

    [TestMethod]
    public async Task TestStartAsync()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger<UIThreadFactoryTest>();
        
        var threadFactory = host.Services.GetRequiredService<IUIThreadFactory>();

        {//１回目起動
            logger.LogInformation("======================= 1st start");
            var thread = await threadFactory.StartAsync();
            logger.LogInformation("======================= 1st end");
        }

        {//２回目起動
            logger.LogInformation("======================= 2nd start");
            var thread = await threadFactory.StartAsync();
            logger.LogInformation("======================= 2nd end");
        }

        {//二重起動
            logger.LogInformation("======================= 3rd start");
            var thread1 = await threadFactory.StartAsync();
            logger.LogInformation("======================= 3rd status");

            logger.LogInformation("======================= 4th start");
            var thread2 = await threadFactory.StartAsync();
            logger.LogInformation($"======================= 4th status");
        }
    }

    [TestMethod]
    public async Task TestDispose()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger<UIThreadFactoryTest>();

        var threadFactory = host.Services.GetRequiredService<IUIThreadFactory>();

        {//起動したままdispose
            logger.LogInformation("======================= start");
            var _ = await threadFactory.StartAsync();

            ((IDisposable)threadFactory).Dispose();
            logger.LogInformation("======================= end");
        }

        try
        {//dispose後のstartはNG
            var thread = await threadFactory.StartAsync();
            Assert.Fail("エラーが発生しなかった");
        }
        catch (ObjectDisposedException e)
        {
            logger.LogInformation(e, "error occurred");
        }
    }

    [TestMethod]
    public async Task TestDisposeAync()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger<UIThreadFactoryTest>();

        var threadFactory = host.Services.GetRequiredService<IUIThreadFactory>();

        {//起動したままdispose async
            logger.LogInformation("======================= start");
            var _ = await threadFactory.StartAsync();

            await ((IAsyncDisposable)threadFactory).DisposeAsync();
            logger.LogInformation("======================= end");
        }

        try
        {//dispose後のstartはNG
            var _ = await threadFactory.StartAsync();
            Assert.Fail("エラーが発生しなかった");
        }
        catch (ObjectDisposedException e)
        {
            logger.LogInformation(e, "error occurred");
        }
    }

}
