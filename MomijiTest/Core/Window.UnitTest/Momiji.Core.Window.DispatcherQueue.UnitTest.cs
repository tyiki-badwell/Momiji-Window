using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Momiji.Core.Window;

namespace MomijiTest.Core.Window.UnitTest;

[TestClass]
public partial class DispatcherQueueUnitTest
{
    public DispatcherQueueUnitTest()
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

        using var queue = new DispatcherQueue(
            loggerFactory.CreateLogger<DispatcherQueue>(), 
            uiThreadActivator
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

                var _ = new DispatcherQueue(
                    loggerFactory.CreateLogger<DispatcherQueue>(),
                    uiThreadActivator
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

        var queue = new DispatcherQueue(
            loggerFactory.CreateLogger<DispatcherQueue>(),
            uiThreadActivator
        );
        queue.Dispose();
        queue.Dispose();
    }

    [TestMethod]
    public async Task TestDestructWithQueueLeft()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var uiThreadActivator = host.Services.GetRequiredService<IUIThreadActivator>();
        using var active = uiThreadActivator.Activate();

        var queue = new DispatcherQueue(
            loggerFactory.CreateLogger<DispatcherQueue>(),
            uiThreadActivator
        );
        queue.Enqueue(() => { });
        queue.Dispose();
    }

    [TestMethod]
    public async Task TestOnInactivated()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var uiThreadActivator = host.Services.GetRequiredService<IUIThreadActivator>();
        var active = uiThreadActivator.Activate();

        using var queue = new DispatcherQueue(
            loggerFactory.CreateLogger<DispatcherQueue>(),
            uiThreadActivator
        );

        active.Dispose();
    }

    [TestMethod]
    public async Task TestEnqueueNonEvent()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var uiThreadActivator = host.Services.GetRequiredService<IUIThreadActivator>();
        using var active = uiThreadActivator.Activate();

        using var queue = new DispatcherQueue(
            loggerFactory.CreateLogger<DispatcherQueue>(),
            uiThreadActivator
        );

        queue.Enqueue(() => { });
        Assert.AreEqual(1, queue.Count);

        queue.Enqueue((p) => { }, 1);
        Assert.AreEqual(2, queue.Count);
    }

    [TestMethod]
    public async Task TestEnqueueWithEvent()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var uiThreadActivator = host.Services.GetRequiredService<IUIThreadActivator>();
        using var active = uiThreadActivator.Activate();

        using var queue = new DispatcherQueue(
            loggerFactory.CreateLogger<DispatcherQueue>(),
            uiThreadActivator
        );

        using var cdeTask = new CountdownEvent(2);
        using var cdeEnqueue = new CountdownEvent(2);

        queue.OnEnqueue += () =>
        {
            cdeEnqueue.Signal();
        };

        queue.Enqueue(() => { cdeTask.Signal(); });
        Assert.AreEqual(1, queue.Count);

        queue.Enqueue((p) => { cdeTask.Signal(); }, 1);
        Assert.AreEqual(2, queue.Count);

        if (!cdeEnqueue.Wait(500))
        {
            Assert.Fail();
        }

        {
            var result = queue.TryDequeue(out var item);
            Assert.IsTrue(result);
            Assert.AreEqual(1, queue.Count);
        }

        {
            var result = queue.TryDequeue(out var item);
            Assert.IsTrue(result);
            Assert.AreEqual(0, queue.Count);
        }

        {
            var result = queue.TryDequeue(out var item);
            Assert.IsFalse(result);
            Assert.AreEqual(0, queue.Count);
        }

    }

    [TestMethod]
    public async Task TestEnqueueAsync()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var uiThreadActivator = host.Services.GetRequiredService<IUIThreadActivator>();
        using var active = uiThreadActivator.Activate();

        using var queue = new DispatcherQueue(
            loggerFactory.CreateLogger<DispatcherQueue>(),
            uiThreadActivator
        );

        using var mre = new ManualResetEventSlim();
        queue.OnEnqueue += () =>
        {
            mre.Set();
        };

        {
            var task = queue.EnqueueAsync(() => { return 1; });
            var result = await task.ConfigureAwait(false);
            Assert.AreEqual(1, result);
        }

        { // immediate mode
#pragma warning disable CS0162 // 到達できないコードが検出されました
            var task = queue.EnqueueAsync(() => { throw new Exception("1"); return 1; });
#pragma warning restore CS0162 // 到達できないコードが検出されました
            var exception = "0";
            try
            {
                var result = await task.ConfigureAwait(false);
                Assert.Fail();
            }
            catch (Exception e)
            {
                exception = e.Message;
            }
            Assert.AreEqual("1", exception);
        }

        async Task<Task<Task<int>>> thread(Func<Task<int>> action)
        {
            var tcs = new TaskCompletionSource<Task<int>>(TaskCreationOptions.AttachedToParent | TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(async () =>
            {
                tcs.TrySetResult(action());
            })
            {
                IsBackground = false
            };
            thread.Start();

            return tcs.Task;
        }

        { // async mode

            var task = thread(() => {
                var task = queue.EnqueueAsync(() => { return 2; });
                return task.AsTask();
            });

            if (!mre.Wait(500))
            {
                Assert.Fail();
            }
            mre.Reset();

            if (queue.TryDequeue(out var item))
            {
                if (item is Action action)
                {
                    action();
                }
                else
                {
                    Assert.Fail();
                }
            }
            else
            {
                Assert.Fail();
            }

            var result = await await await task.ConfigureAwait(false);
            Assert.AreEqual(2, result);
        }

        {
            var task = thread(() => {
#pragma warning disable CS0162 // 到達できないコードが検出されました
                var task = queue.EnqueueAsync(() => { throw new Exception("2"); return 2; });
#pragma warning restore CS0162 // 到達できないコードが検出されました
                return task.AsTask();
            });

            if (!mre.Wait(500))
            {
                Assert.Fail();
            }
            mre.Reset();

            if (queue.TryDequeue(out var item))
            {
                if (item is Action action)
                {
                    action();
                }
                else
                {
                    Assert.Fail(item.GetType().ToString());
                }
            }
            else
            {
                Assert.Fail();
            }

            var exception = "0";
            try
            {
                var result = await await await task.ConfigureAwait(false);
            }
            catch (Exception e)
            {
                exception = e.Message;
            }
            Assert.AreEqual("2", exception);
        }
    }


    /*
    [TestMethod]
    public async Task TestDispatchQueueOnOtherThread()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger<DispatcherQueueUnitTest>();

        var startupTcs = new TaskCompletionSource();

        using var mre = new ManualResetEventSlim();

        var uiThreadActivator = host.Services.GetRequiredService<IUIThreadActivator>();
        var onUnhandledExceptionHandler = host.Services.GetRequiredService<IOnUnhandledExceptionHandler>();
        onUnhandledExceptionHandler.OnUnhandledException += (sender, e) =>
        {
            logger.LogInformation(e, $"★thread:[{Environment.CurrentManagedThreadId:X}] OnError mainTcs SetException");
            return false;
        };
        var dispatcherQueueSynchronizationContext = host.Services.GetRequiredService<IDispatcherQueueSynchronizationContext>();

        using var queue = new DispatcherQueue(
            loggerFactory.CreateLogger<DispatcherQueue>(),
            uiThreadActivator
        );

        using var queueTaskExecutor = new DispatcherQueueTaskExecutor(
            loggerFactory.CreateLogger<DispatcherQueueTaskExecutor>(),
            uiThreadActivator,
            onUnhandledExceptionHandler,
            queue,
            dispatcherQueueSynchronizationContext
        );

        logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] task await start");
        logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] task run");
        var factory = new TaskFactory();
        var main = factory.StartNew(() =>
        {
            logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] task start");
            //起動
            using var active = uiThreadActivator.Activate();

            startupTcs.SetResult();

            mre.Wait();

        }, TaskCreationOptions.RunContinuationsAsynchronously);

        await startupTcs.Task;

        try
        {
            await Task.Run(() =>
            {
                //起動したスレッド以外からは呼べない
                logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] DispatchQueue start");
                queueTaskExecutor.DispatchQueue();
            });

            Assert.Fail($"★thread:[{Environment.CurrentManagedThreadId:X}] DispatchQueue end");
        }
        catch (InvalidOperationException e)
        {
            //OK
            logger.LogInformation(e, "error occurred");
        }
        logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] startupTcs await end");

        mre.Set();

        await main;
        logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] main await end");
    }
    */
}
