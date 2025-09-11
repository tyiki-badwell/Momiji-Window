using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Momiji.Core.Window;

namespace MomijiTest.Core.Window.UnitTest;

[TestClass]
public partial class DispatcherQueueTaskExecutorUnitTest
{
    public DispatcherQueueTaskExecutorUnitTest()
    {
    }

    private static IHost Setup()
    {
        var builder = Host.CreateDefaultBuilder(default);

        builder.UseContentRoot(AppContext.BaseDirectory);
        builder.UseConsoleLifetime();
        builder.ConfigureServices((hostContext, services) =>
        {
            services.AddSingleton<IUIThreadActivator, UIThreadActivator>();
            services.AddSingleton<IOnUnhandledExceptionHandler, OnUnhandledExceptionHandler>();
            services.AddSingleton<IDispatcherQueue, DispatcherQueue>();
            services.AddSingleton<IDispatcherQueueSynchronizationContext, DispatcherQueueSynchronizationContext>();
        });

        return builder.Build();
    }

    [TestMethod]
    public async Task TestConstructor()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var uiThreadActivator = host.Services.GetRequiredService<IUIThreadActivator>();
        var onUnhandledExceptionHandler = host.Services.GetRequiredService<IOnUnhandledExceptionHandler>();
        var queue = host.Services.GetRequiredService<IDispatcherQueue>();
        var context = host.Services.GetRequiredService<IDispatcherQueueSynchronizationContext>();

        using var executor = new DispatcherQueueTaskExecutor(
            loggerFactory.CreateLogger<DispatcherQueueTaskExecutor>(),
            uiThreadActivator,
            onUnhandledExceptionHandler,
            queue,
            context
        );
    }

    [TestMethod]
    public async Task TestFinalizer()
    {
        {
            using var host = Setup();
            var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();

            void run()
            {
                var uiThreadActivator = host.Services.GetRequiredService<IUIThreadActivator>();
                var onUnhandledExceptionHandler = host.Services.GetRequiredService<IOnUnhandledExceptionHandler>();
                var queue = host.Services.GetRequiredService<IDispatcherQueue>();
                var context = host.Services.GetRequiredService<IDispatcherQueueSynchronizationContext>();

                var _ = new DispatcherQueueTaskExecutor(
                    loggerFactory.CreateLogger<DispatcherQueueTaskExecutor>(),
                    uiThreadActivator,
                    onUnhandledExceptionHandler,
                    queue,
                    context
                );
            }

            run();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    [TestMethod]
    public async Task TestDoubleDestruct()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var uiThreadActivator = host.Services.GetRequiredService<IUIThreadActivator>();
        var onUnhandledExceptionHandler = host.Services.GetRequiredService<IOnUnhandledExceptionHandler>();
        var queue = host.Services.GetRequiredService<IDispatcherQueue>();
        var context = host.Services.GetRequiredService<IDispatcherQueueSynchronizationContext>();

        var executor = new DispatcherQueueTaskExecutor(
            loggerFactory.CreateLogger<DispatcherQueueTaskExecutor>(),
            uiThreadActivator,
            onUnhandledExceptionHandler,
            queue,
            context
        );
        executor.Dispose();
        executor.Dispose();
    }

    [TestMethod]
    public async Task TestOnInactivated()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var uiThreadActivator = host.Services.GetRequiredService<IUIThreadActivator>();
        var onUnhandledExceptionHandler = host.Services.GetRequiredService<IOnUnhandledExceptionHandler>();
        var queue = host.Services.GetRequiredService<IDispatcherQueue>();
        var context = host.Services.GetRequiredService<IDispatcherQueueSynchronizationContext>();

        var active = uiThreadActivator.Activate();

        using var executor = new DispatcherQueueTaskExecutor(
            loggerFactory.CreateLogger<DispatcherQueueTaskExecutor>(),
            uiThreadActivator,
            onUnhandledExceptionHandler,
            queue,
            context
        );

        active.Dispose();
    }

    [TestMethod]
    public async Task TestDispatcherQueueAction()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var uiThreadActivator = host.Services.GetRequiredService<IUIThreadActivator>();
        var onUnhandledExceptionHandler = host.Services.GetRequiredService<IOnUnhandledExceptionHandler>();
        var queue = host.Services.GetRequiredService<IDispatcherQueue>();
        var context = host.Services.GetRequiredService<IDispatcherQueueSynchronizationContext>();

        using var active = uiThreadActivator.Activate();

        using var executor = new DispatcherQueueTaskExecutor(
            loggerFactory.CreateLogger<DispatcherQueueTaskExecutor>(),
            uiThreadActivator,
            onUnhandledExceptionHandler,
            queue,
            context
        );

        queue.Enqueue(() => { });

        executor.DispatchQueue();
    }

    [TestMethod]
    public async Task TestDispatcherQueueSendOrPostCallback()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var uiThreadActivator = host.Services.GetRequiredService<IUIThreadActivator>();
        var onUnhandledExceptionHandler = host.Services.GetRequiredService<IOnUnhandledExceptionHandler>();
        var queue = host.Services.GetRequiredService<IDispatcherQueue>();
        var context = host.Services.GetRequiredService<IDispatcherQueueSynchronizationContext>();

        using var active = uiThreadActivator.Activate();

        using var executor = new DispatcherQueueTaskExecutor(
            loggerFactory.CreateLogger<DispatcherQueueTaskExecutor>(),
            uiThreadActivator,
            onUnhandledExceptionHandler,
            queue,
            context
        );

        queue.Enqueue((p) => { }, 1);

        executor.DispatchQueue();
    }

    [TestMethod]
    public async Task TestDispatcherQueueUnkown()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var uiThreadActivator = host.Services.GetRequiredService<IUIThreadActivator>();
        var onUnhandledExceptionHandler = host.Services.GetRequiredService<IOnUnhandledExceptionHandler>();
        var queue = host.Services.GetRequiredService<IDispatcherQueue>();
        var context = host.Services.GetRequiredService<IDispatcherQueueSynchronizationContext>();

        using var active = uiThreadActivator.Activate();

        using var executor = new DispatcherQueueTaskExecutor(
            loggerFactory.CreateLogger<DispatcherQueueTaskExecutor>(),
            uiThreadActivator,
            onUnhandledExceptionHandler,
            queue,
            context
        );

        queue.Enqueue((p) => { }, (object?)null);

        try
        {
            executor.DispatchQueue();
            Assert.Fail();
        }
        catch (Exception e)
        {

        }
    }

    [TestMethod]
    public async Task TestDispatcherQueueError()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var uiThreadActivator = host.Services.GetRequiredService<IUIThreadActivator>();
        var onUnhandledExceptionHandler = host.Services.GetRequiredService<IOnUnhandledExceptionHandler>();
        var queue = host.Services.GetRequiredService<IDispatcherQueue>();
        var context = host.Services.GetRequiredService<IDispatcherQueueSynchronizationContext>();

        using var active = uiThreadActivator.Activate();

        using var executor = new DispatcherQueueTaskExecutor(
            loggerFactory.CreateLogger<DispatcherQueueTaskExecutor>(),
            uiThreadActivator,
            onUnhandledExceptionHandler,
            queue,
            context
        );

        queue.Enqueue(() => { throw new Exception("error"); });

        try
        {
            executor.DispatchQueue();
            Assert.Fail();
        }
        catch (Exception e)
        {
        
        }
    }

    [TestMethod]
    public async Task TestDispatcherQueueErrorWithHandler()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var uiThreadActivator = host.Services.GetRequiredService<IUIThreadActivator>();
        var onUnhandledExceptionHandler = host.Services.GetRequiredService<IOnUnhandledExceptionHandler>();
        var queue = host.Services.GetRequiredService<IDispatcherQueue>();
        var context = host.Services.GetRequiredService<IDispatcherQueueSynchronizationContext>();

        onUnhandledExceptionHandler.OnUnhandledException += (sender, e) =>
        {
            //OK
            return true;
        };

        using var active = uiThreadActivator.Activate();

        using var executor = new DispatcherQueueTaskExecutor(
            loggerFactory.CreateLogger<DispatcherQueueTaskExecutor>(),
            uiThreadActivator,
            onUnhandledExceptionHandler,
            queue,
            context
        );

        queue.Enqueue(() => { throw new Exception("error"); });

        executor.DispatchQueue();
    }
}
