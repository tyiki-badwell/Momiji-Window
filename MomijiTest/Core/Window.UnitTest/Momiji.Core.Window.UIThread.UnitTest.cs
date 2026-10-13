using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Momiji.Core.Window;
using MomijiTest.Core.Window.IntegrationTest;

namespace MomijiTest.Core.Window.UnitTest;

[TestClass]
public partial class UIThreadUnitTest
{
    public UIThreadUnitTest()
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
            services.AddSingleton<IWindowManagerInternal, WindowManager>();
            services.AddSingleton<IWindowProcedure, WindowProcedure>();
            services.AddSingleton<IWindowClassManager, WindowClassManager>();
            services.AddSingleton<IDispatcherQueue, DispatcherQueue>();
            services.AddSingleton<IDispatcherQueueTaskExecutor, DispatcherQueueTaskExecutor>();
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
        var dispatcherQueue = host.Services.GetRequiredService<IDispatcherQueue>();
        var dispatcherQueueTaskExecutor = host.Services.GetRequiredService<IDispatcherQueueTaskExecutor>();
        var context = host.Services.GetRequiredService<IDispatcherQueueSynchronizationContext>();
        var windowManager = host.Services.GetRequiredService<IWindowManagerInternal>();

        using var thread = new UIThread(
            loggerFactory,
            uiThreadActivator,
            dispatcherQueue,
            dispatcherQueueTaskExecutor,
            context,
            windowManager
        );
    }

    [TestMethod]
    public async Task TestDisposeAsync()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var uiThreadActivator = host.Services.GetRequiredService<IUIThreadActivator>();
        var dispatcherQueue = host.Services.GetRequiredService<IDispatcherQueue>();
        var dispatcherQueueTaskExecutor = host.Services.GetRequiredService<IDispatcherQueueTaskExecutor>();
        var context = host.Services.GetRequiredService<IDispatcherQueueSynchronizationContext>();
        var windowManager = host.Services.GetRequiredService<IWindowManagerInternal>();

        await using var thread = new UIThread(
            loggerFactory,
            uiThreadActivator,
            dispatcherQueue,
            dispatcherQueueTaskExecutor,
            context,
            windowManager
        );
    }

    [TestMethod]
    public async Task TestCancelAsync()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var uiThreadActivator = host.Services.GetRequiredService<IUIThreadActivator>();
        var dispatcherQueue = host.Services.GetRequiredService<IDispatcherQueue>();
        var dispatcherQueueTaskExecutor = host.Services.GetRequiredService<IDispatcherQueueTaskExecutor>();
        var context = host.Services.GetRequiredService<IDispatcherQueueSynchronizationContext>();
        var windowManager = host.Services.GetRequiredService<IWindowManagerInternal>();

        await using var thread = new UIThread(
            loggerFactory,
            uiThreadActivator,
            dispatcherQueue,
            dispatcherQueueTaskExecutor,
            context,
            windowManager
        );

        await thread.CancelAsync();
        await thread.CancelAsync();
    }

    [TestMethod]
    public async Task TestFinalizer()
    {
        {
            using var host = Setup();
            var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();

            void Run()
            {
                var uiThreadActivator = host.Services.GetRequiredService<IUIThreadActivator>();
                var dispatcherQueue = host.Services.GetRequiredService<IDispatcherQueue>();
                var dispatcherQueueTaskExecutor = host.Services.GetRequiredService<IDispatcherQueueTaskExecutor>();
                var context = host.Services.GetRequiredService<IDispatcherQueueSynchronizationContext>();
                var windowManager = host.Services.GetRequiredService<IWindowManagerInternal>();

                var _ = new UIThread(
                    loggerFactory,
                    uiThreadActivator,
                    dispatcherQueue,
                    dispatcherQueueTaskExecutor,
                    context,
                    windowManager
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
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var uiThreadActivator = host.Services.GetRequiredService<IUIThreadActivator>();
        var dispatcherQueue = host.Services.GetRequiredService<IDispatcherQueue>();
        var dispatcherQueueTaskExecutor = host.Services.GetRequiredService<IDispatcherQueueTaskExecutor>();
        var context = host.Services.GetRequiredService<IDispatcherQueueSynchronizationContext>();
        var windowManager = host.Services.GetRequiredService<IWindowManagerInternal>();

        var thread = new UIThread(
            loggerFactory,
            uiThreadActivator,
            dispatcherQueue,
            dispatcherQueueTaskExecutor,
            context,
            windowManager
        );
        thread.Dispose();
        thread.Dispose();
    }

    [TestMethod]
    public async Task TestDispatchAsyncOK()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger<DispatcherQueueIntegrationTest>();

        var uiThreadActivator = host.Services.GetRequiredService<IUIThreadActivator>();
        var dispatcherQueue = host.Services.GetRequiredService<IDispatcherQueue>();
        var dispatcherQueueTaskExecutor = host.Services.GetRequiredService<IDispatcherQueueTaskExecutor>();
        var context = host.Services.GetRequiredService<IDispatcherQueueSynchronizationContext>();
        var windowManager = host.Services.GetRequiredService<IWindowManagerInternal>();

        using var thread = new UIThread(
            loggerFactory,
            uiThreadActivator,
            dispatcherQueue,
            dispatcherQueueTaskExecutor,
            context,
            windowManager
        );

        using var cts = new CancellationTokenSource();
        var tcs = new TaskCompletionSource<IUIThread>(TaskCreationOptions.AttachedToParent | TaskCreationOptions.RunContinuationsAsynchronously);

        var task = Task.Run(() =>
        {
            thread.RunMessageLoop(tcs, cts.Token);
            logger.LogInformation("loop end");
        });

        var uiThread = await tcs.Task;

        var result = await await Task.Run(async () =>
        {
            return await uiThread.EnqueueAsync(async (manager) =>
            {
                //immidiate mode
                var result = await uiThread.EnqueueAsync((manager) => 1);
                return 1 + result;
            });
        });

        Assert.AreEqual(2, result);

        cts.Cancel();
        await task;
    }

    [TestMethod]
    public async Task TestDispatchAsyncFail()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger<DispatcherQueueIntegrationTest>();

        var uiThreadActivator = host.Services.GetRequiredService<IUIThreadActivator>();
        var dispatcherQueue = host.Services.GetRequiredService<IDispatcherQueue>();
        var dispatcherQueueTaskExecutor = host.Services.GetRequiredService<IDispatcherQueueTaskExecutor>();
        var windowManager = host.Services.GetRequiredService<IWindowManagerInternal>();
        var context = host.Services.GetRequiredService<IDispatcherQueueSynchronizationContext>();

        using var thread = new UIThread(
            loggerFactory,
            uiThreadActivator,
            dispatcherQueue,
            dispatcherQueueTaskExecutor,
            context,
            windowManager
        );

        try
        {
            using var cts = new CancellationTokenSource();
            var tcs = new TaskCompletionSource<IUIThread>(TaskCreationOptions.AttachedToParent | TaskCreationOptions.RunContinuationsAsynchronously);

            var task = Task.Run(() =>
            {
                thread.RunMessageLoop(tcs, cts.Token);
                logger.LogInformation("loop end");
            });

            var uiThread = await tcs.Task;

            cts.Cancel();
            await task;

            var result = await Task.Run(async () =>
            {
                return await uiThread.EnqueueAsync((manager) => 1);
            });

            Assert.Fail();
        }
        catch (InvalidOperationException e)
        {
            logger.LogInformation(e, $"Åöthread:[{Environment.CurrentManagedThreadId:X}] error end");
        }
    }

    [TestMethod]
    public async Task TestMessageLoopForceEnd()
    {
        Task? task = null;

        {
            using var host = Setup();
            var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger<DispatcherQueueIntegrationTest>();

            var uiThreadActivator = host.Services.GetRequiredService<IUIThreadActivator>();
            var dispatcherQueue = host.Services.GetRequiredService<IDispatcherQueue>();
            var dispatcherQueueTaskExecutor = host.Services.GetRequiredService<IDispatcherQueueTaskExecutor>();
            var windowManager = host.Services.GetRequiredService<IWindowManagerInternal>();
            var context = host.Services.GetRequiredService<IDispatcherQueueSynchronizationContext>();

            using var thread = new UIThread(
                loggerFactory,
                uiThreadActivator,
                dispatcherQueue,
                dispatcherQueueTaskExecutor,
                context,
                windowManager
            );

            var tcs = new TaskCompletionSource<IUIThread>(TaskCreationOptions.AttachedToParent | TaskCreationOptions.RunContinuationsAsynchronously);

            task = Task.Run(() =>
            {
                thread.RunMessageLoop(tcs, default);
                logger.LogInformation("loop end");
            });

            var uiThread = await tcs.Task;
            thread.Dispose();
        }

        if (task != default)
        {
            await task;
        }
    }
}
