using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Momiji.Core.Window;
using MomijiTest.Core.Window.UnitTest;

namespace MomijiTest.Core.Window.IntegrationTest;

[TestClass]
public partial class UIThreadIntegrationTest
{
    public UIThreadIntegrationTest()
    {
    }

    private static IHost Setup()
    {
        var builder = Host.CreateDefaultBuilder(default);

        builder.UseContentRoot(AppContext.BaseDirectory);
        builder.UseConsoleLifetime();
        builder.UseMomijiWindow();

        return builder.Build();
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
        var windowManager = host.Services.GetRequiredService<IWindowManagerInternal>();
        var context = host.Services.GetRequiredService<IDispatcherQueueSynchronizationContext>();
        var onUnhandledExceptionHandler = host.Services.GetRequiredService<IOnUnhandledExceptionHandler>();

        using var thread = new UIThread(
            loggerFactory,
            uiThreadActivator,
            dispatcherQueue,
            dispatcherQueueTaskExecutor,
            context,
            windowManager,
            onUnhandledExceptionHandler
        );

        using var cts = new CancellationTokenSource();
        var tcs = new TaskCompletionSource<IUIThread>(TaskCreationOptions.AttachedToParent | TaskCreationOptions.RunContinuationsAsynchronously);

        var task = TestTool.RunThread(() =>
        {
            thread.RunMessageLoop(tcs, cts.Token);
            logger.LogInformation("loop end");
        });

        var uiThread = await tcs.Task;

        var result = await await await TestTool.RunThread(async () =>
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
        var onUnhandledExceptionHandler = host.Services.GetRequiredService<IOnUnhandledExceptionHandler>();

        using var thread = new UIThread(
            loggerFactory,
            uiThreadActivator,
            dispatcherQueue,
            dispatcherQueueTaskExecutor,
            context,
            windowManager,
            onUnhandledExceptionHandler
        );

        try
        {
            using var cts = new CancellationTokenSource();
            var tcs = new TaskCompletionSource<IUIThread>(TaskCreationOptions.AttachedToParent | TaskCreationOptions.RunContinuationsAsynchronously);

            var task = TestTool.RunThread(() =>
            {
                thread.RunMessageLoop(tcs, cts.Token);
                logger.LogInformation("loop end");
            });

            var uiThread = await tcs.Task;

            cts.Cancel();
            await task;

            var result = await await TestTool.RunThread(async () =>
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
            var onUnhandledExceptionHandler = host.Services.GetRequiredService<IOnUnhandledExceptionHandler>();

            using var thread = new UIThread(
                loggerFactory,
                uiThreadActivator,
                dispatcherQueue,
                dispatcherQueueTaskExecutor,
                context,
                windowManager,
                onUnhandledExceptionHandler
            );

            var tcs = new TaskCompletionSource<IUIThread>(TaskCreationOptions.AttachedToParent | TaskCreationOptions.RunContinuationsAsynchronously);

            task = TestTool.RunThread(() =>
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
