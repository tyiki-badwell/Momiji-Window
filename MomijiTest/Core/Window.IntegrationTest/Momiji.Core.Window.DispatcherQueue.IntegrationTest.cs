using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Momiji.Core.Window;

namespace MomijiTest.Core.Window.IntegrationTest;

[TestClass]
public partial class DispatcherQueueIntegrationTest
{
    public DispatcherQueueIntegrationTest()
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

    private class TestException : Exception
    {
    
    }

    public enum MethodType
    {
        Action,
        SendOrPostCallback,
        Async
    }

    [TestMethod]
    [DataRow(MethodType.Action, false, true, false, false)]
    [DataRow(MethodType.Action, true, true, false, false)]
    [DataRow(MethodType.SendOrPostCallback, false, true, false, false)]
    [DataRow(MethodType.SendOrPostCallback, true, true, false, false)]
    [DataRow(MethodType.Async, false, true, false, false)]
    [DataRow(MethodType.Async, true, true, false, false)]
    [DataRow(MethodType.Async, false, true, false, false, true)]
    [DataRow(MethodType.Async, true, true, false, false, true)]
    [DataRow(MethodType.Action, false, false, false, false)]
    [DataRow(MethodType.Action, true, false, false, false)]
    [DataRow(MethodType.SendOrPostCallback, false, false, false, false)]
    [DataRow(MethodType.SendOrPostCallback, true, false, false, false)]
    [DataRow(MethodType.Async, false, false, false, false)]
    [DataRow(MethodType.Async, true, false, false, false)]
    [DataRow(MethodType.Async, false, false, false, false, true)]
    [DataRow(MethodType.Async, true, false, false, false, true)]

    [DataRow(MethodType.Action, false, true, true, false)]
    [DataRow(MethodType.Action, true, true, true, false)]
    [DataRow(MethodType.SendOrPostCallback, false, true, true, false)]
    [DataRow(MethodType.SendOrPostCallback, true, true, true, false)]
    [DataRow(MethodType.Async, false, true, true, false)]
    [DataRow(MethodType.Async, true, true, true, false)]
    [DataRow(MethodType.Async, false, true, true, false, true)]
    [DataRow(MethodType.Async, true, true, true, false, true)]
    [DataRow(MethodType.Action, false, false, true, false)]
    [DataRow(MethodType.Action, true, false, true, false)]
    [DataRow(MethodType.SendOrPostCallback, false, false, true, false)]
    [DataRow(MethodType.SendOrPostCallback, true, false, true, false)]
    [DataRow(MethodType.Async, false, false, true, false)]
    [DataRow(MethodType.Async, true, false, true, false)]
    [DataRow(MethodType.Async, false, false, true, false, true)]
    [DataRow(MethodType.Async, true, false, true, false, true)]

    [DataRow(MethodType.Action, false, true, true, true)]
    [DataRow(MethodType.Action, true, true, true, true)]
    [DataRow(MethodType.SendOrPostCallback, false, true, true, true)]
    [DataRow(MethodType.SendOrPostCallback, true, true, true, true)]
    [DataRow(MethodType.Async, false, true, true, true)]
    [DataRow(MethodType.Async, true, true, true, true)]
    [DataRow(MethodType.Async, false, true, true, true, true)]
    [DataRow(MethodType.Async, true, true, true, true, true)]
    [DataRow(MethodType.Action, false, false, true, true)]
    [DataRow(MethodType.Action, true, false, true, true)]
    [DataRow(MethodType.SendOrPostCallback, false, false, true, true)]
    [DataRow(MethodType.SendOrPostCallback, true, false, true, true)]
    [DataRow(MethodType.Async, false, false, true, true)]
    [DataRow(MethodType.Async, true, false, true, true)]
    [DataRow(MethodType.Async, false, false, true, true, true)]
    [DataRow(MethodType.Async, true, false, true, true, true)]

    public async Task TestDispatchQueue(
        MethodType methodType,
        bool error,
        bool handleException,
        bool asyncAction,
        bool configureAwait,
        bool sameThread = false
    )
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger<DispatcherQueueIntegrationTest>();

        var startupTcs = new TaskCompletionSource();
        var mainTcs = new TaskCompletionSource<int>();
        using var mre = new ManualResetEventSlim();

        var uiThreadActivator = host.Services.GetRequiredService<IUIThreadActivator>();
        var onUnhandledExceptionHandler = host.Services.GetRequiredService<IOnUnhandledExceptionHandler>();
        onUnhandledExceptionHandler.OnUnhandledException += (sender, e) =>
        {
            logger.LogInformation(e, $"★thread:[{Environment.CurrentManagedThreadId:X}] OnError mainTcs SetException");
            mainTcs.SetException(e);
            return handleException;
        };

        var dispatcherQueueSynchronizationContext = host.Services.GetRequiredService<IDispatcherQueueSynchronizationContext>();

        var queue = host.Services.GetRequiredService<IDispatcherQueue>();

        var queueTaskExecutor = host.Services.GetRequiredService<IDispatcherQueueTaskExecutor>();

        logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] task run");
        var factory = new TaskFactory();
        var main = factory.StartNew(() =>
        {
            logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] task start");
            //起動
            using var active = uiThreadActivator.Activate();

            startupTcs.SetResult();
            logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] startupTcs SetResult");

            logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] mre wait");
            if (!mre.Wait(5000))
            {
                throw new TestException();
            }
            logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] mre wait end");

            while (true)
            {
                if (mainTcs.Task.IsCompleted)
                {
                    logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] loop end");
                    break;
                }

                logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] DispatchQueue start");
                queueTaskExecutor.DispatchQueue();
                logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] DispatchQueue end");

                logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] DispatchQueue wait start");
                Task.Delay(1000).Wait();
                logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] DispatchQueue wait end");
            }
        }, TaskCreationOptions.RunContinuationsAsynchronously);

        logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] startupTcs await start");
        await startupTcs.Task.ConfigureAwait(false);
        logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] startupTcs await end");

        if (methodType == MethodType.Action && !asyncAction)
        {
            queue.Enqueue(() =>
            {
                if (error)
                {
                    throw new TestException();
                }

                mainTcs.SetResult(1);
                logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] SetResult");
            });
        }
        else if (methodType == MethodType.Action && asyncAction)
        {
            queue.Enqueue(async () =>
            {
                logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] delay start");

                //TODO ConfigureAwait(true)でデッドロックさせない方法？ DispatcherQueueSynchronizationContextで仕掛けを作れる？ デッドロックしそうならthrowしたい
                await Task.Delay(1).ConfigureAwait(configureAwait);
                logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] delay end");

                //awaitからのcontinueで、DispatchQueue()が終わってDispatcherQueueSynchronizationContextからPost()されるので、もう一度DispatchQueue()を実行

                if (error)
                {
                    throw new TestException();
                }

                mainTcs.SetResult(1);
                logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] SetResult");
            });
        }
        else if (methodType == MethodType.SendOrPostCallback && !asyncAction)
        {
            queue.Enqueue((param) =>
            {
                if (error)
                {
                    throw new TestException();
                }

                mainTcs.SetResult((int)param!);
                logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] SetResult");
            }, 1);
        }
        else if (methodType == MethodType.SendOrPostCallback && asyncAction)
        {
            queue.Enqueue(async (param) =>
            {
                logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] delay start");

                //TODO ConfigureAwait(true)でデッドロックさせない方法？ DispatcherQueueSynchronizationContextで仕掛けを作れる？ デッドロックしそうならthrowしたい
                await Task.Delay(1).ConfigureAwait(configureAwait);
                logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] delay end");

                //awaitからのcontinueで、DispatchQueue()が終わってDispatcherQueueSynchronizationContextからPost()されるので、もう一度DispatchQueue()を実行

                if (error)
                {
                    throw new TestException();
                }

                mainTcs.SetResult((int)param!);
                logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] SetResult");
            }, 1);
        }
        else if (methodType == MethodType.Async && !asyncAction && !sameThread)
        {
            var _ = Task.Run(async () =>
            {
                var result = queue.EnqueueAsync(() =>
                {
                    if (error)
                    {
                        throw new TestException();
                    }

                    return 1;
                });

                try
                {
                    var result2 = await result.ConfigureAwait(false);
                    logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] mainTcs SetResult");
                    mainTcs.SetResult(result2);
                }
                catch (TestException e)
                {
                    logger.LogInformation(e, $"★thread:[{Environment.CurrentManagedThreadId:X}] mainTcs SetException");
                    mainTcs.SetException(e);
                }
            });
        }
        else if (methodType == MethodType.Async && !asyncAction && sameThread)
        {
            var result = queue.EnqueueAsync(() =>
            {
                if (error)
                {
                    throw new TestException();
                }

                return 1;
            });

            try
            {
                var result2 = await result.ConfigureAwait(false);
                logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] mainTcs SetResult");
                mainTcs.SetResult(result2);
            }
            catch (TestException e)
            {
                logger.LogInformation(e, $"★thread:[{Environment.CurrentManagedThreadId:X}] mainTcs SetException");
                mainTcs.SetException(e);
            }
        }
        else if (methodType == MethodType.Async && asyncAction && !sameThread)
        {
            var _ = Task.Run(async () =>
            {
                var result = await queue.EnqueueAsync(async () =>
                {
                    logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] delay start");

                    //TODO ConfigureAwait(true)でデッドロックさせない方法？ DispatcherQueueSynchronizationContextで仕掛けを作れる？ デッドロックしそうならthrowしたい
                    await Task.Delay(1).ConfigureAwait(configureAwait);
                    logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] delay end");

                    //awaitからのcontinueで、DispatchQueue()が終わってDispatcherQueueSynchronizationContextからPost()されるので、もう一度DispatchQueue()を実行

                    if (error)
                    {
                        throw new TestException();
                    }

                    return 1;
                });

                try
                {
                    var result2 = await result.ConfigureAwait(false);
                    logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] mainTcs SetResult");
                    mainTcs.SetResult(result2);
                }
                catch (TestException e)
                {
                    logger.LogInformation(e, $"★thread:[{Environment.CurrentManagedThreadId:X}] mainTcs SetException");
                    mainTcs.SetException(e);
                }
            });
        }
        else if (methodType == MethodType.Async && asyncAction && sameThread)
        {
            var result = await queue.EnqueueAsync(async () =>
            {
                logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] delay start");

                //TODO ConfigureAwait(true)でデッドロックさせない方法？ DispatcherQueueSynchronizationContextで仕掛けを作れる？ デッドロックしそうならthrowしたい
                await Task.Delay(1).ConfigureAwait(configureAwait);
                logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] delay end");

                //awaitからのcontinueで、DispatchQueue()が終わってDispatcherQueueSynchronizationContextからPost()されるので、もう一度DispatchQueue()を実行

                if (error)
                {
                    throw new TestException();
                }

                return 1;
            });

            try
            {
                var result2 = await result.ConfigureAwait(false);
                logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] mainTcs SetResult");
                mainTcs.SetResult(result2);
            }
            catch (TestException e)
            {
                logger.LogInformation(e, $"★thread:[{Environment.CurrentManagedThreadId:X}] mainTcs SetException");
                mainTcs.SetException(e);
            }
        }
        else
        {
            Assert.Fail();
        }

        logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] mre set");
        mre.Set();

        try
        {
            logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] mainTcs await start");
            var result = await mainTcs.Task;

            if (error)
            {
                Assert.Fail();
            }

            logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] mainTcs await end");
            Assert.AreEqual(1, result);
        }
        catch (TestException e)
        {
            logger.LogInformation(e, $"★thread:[{Environment.CurrentManagedThreadId:X}] mainTcs await error");
            if (!error)
            {
                Assert.Fail();
            }
        }

        try
        {
            //asynではない場合は、ここに例外が上がってくる
            logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] main await start");
            await main;
            logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] main await end");

            if (error && !handleException && methodType != MethodType.Async)
            {
                Assert.Fail("エラー発生　＆　ハンドリング無しで例外が発生していない");
            }
        }
        catch (TestException e)
        {
            logger.LogInformation(e, $"★thread:[{Environment.CurrentManagedThreadId:X}] main await error");

            if (!error && !handleException)
            {
                throw;
            }
        }

        try
        {
            //停止後は呼べない
            if (methodType == MethodType.Action)
            {
                queue.Enqueue(() => { });
            }
            else if (methodType == MethodType.SendOrPostCallback)
            {
                queue.Enqueue((param) => { }, 2);
            }
            else if (methodType == MethodType.Async)
            {
                await queue.EnqueueAsync(() => { return true; });
            }
            Assert.Fail();
        }
        catch (InvalidOperationException e)
        {
            //OK
            logger.LogInformation(e, "error occurred");
        }
    }
}
