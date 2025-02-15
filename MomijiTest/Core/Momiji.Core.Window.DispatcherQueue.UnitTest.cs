using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Momiji.Core.Window;

[TestClass]
public partial class DispatcherQueueTest : IDisposable
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;

    public DispatcherQueueTest()
    {
        var configuration = CreateConfiguration();

        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConfiguration(configuration);

            builder.AddFilter("Momiji", LogLevel.Warning);
            builder.AddFilter("Momiji.Core.Window", LogLevel.Trace);
            builder.AddFilter("Momiji.Core.Window.DispatcherQueueTest", LogLevel.Trace);
            builder.AddFilter("Microsoft", LogLevel.Warning);
            builder.AddFilter("System", LogLevel.Warning);

            builder.AddConsole();
            builder.AddDebug();
        });

        _logger = _loggerFactory.CreateLogger<DispatcherQueueTest>();
    }

    public void Dispose()
    {
        _loggerFactory.Dispose();
        GC.SuppressFinalize(this);
    }

    private static IConfiguration CreateConfiguration()
    {
        var configuration =
            new ConfigurationBuilder()
            .AddJsonFile("appsettings.json")
            .Build();

        return configuration;
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
    [DataRow(false, MethodType.Action)]
    [DataRow(true, MethodType.Action)]
    [DataRow(false, MethodType.SendOrPostCallback)]
    [DataRow(true, MethodType.SendOrPostCallback)]
    [DataRow(false, MethodType.Async)]
    [DataRow(true, MethodType.Async)]
    public async Task TestDispatchQueue(
        bool error,
        MethodType methodType
    )
    {
        var startupTcs = new TaskCompletionSource();
        var mainTcs = new TaskCompletionSource<int>();
        var uiThreadActivator = new UIThreadActivator(_loggerFactory);

        using var queue = new DispatcherQueue(_loggerFactory, uiThreadActivator);
        queue.OnUnhandledException += (e) =>
        {
            _logger.LogInformation(e, $"★thread:[{Environment.CurrentManagedThreadId:X}] OnError");
            mainTcs.SetException(e);
            _logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] SetException");
            return true;
        };

        _logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] task run");
        var factory = new TaskFactory();
        var main = factory.StartNew(() =>
        {
            _logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] task start");
            //起動
            using var active = uiThreadActivator.Activate();

            startupTcs.SetResult();
            _logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] SetResult");

            queue.WaitHandle.WaitOne();
            _logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] WaitOne end");

            _logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] DispatchQueue 1 start");
            queue.DispatchQueue();
            _logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] DispatchQueue 1 end");

            //postTcs.Task.Wait();
            Thread.Sleep(1000);

            _logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] DispatchQueue 2 start");
            queue.DispatchQueue();
            _logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] DispatchQueue 2 end");
        }, TaskCreationOptions.RunContinuationsAsynchronously);

        _logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] task await start");
        await startupTcs.Task.ContinueWith((task)=> {

            try
            {
                //起動したスレッド以外からは呼べない
                _logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] DispatchQueue start");
                queue.DispatchQueue();
                Assert.Fail($"★thread:[{Environment.CurrentManagedThreadId:X}] DispatchQueue end");
            }
            catch (InvalidOperationException e)
            {
                //OK
                _logger.LogInformation(e, "error occurred");
            }
        });
        _logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] task await end");

        if (methodType == MethodType.Action)
        {
            queue.Dispatch(async () =>
            {
                _logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] delay start");

                //TODO ConfigureAwait(true)でデッドロックさせない方法？ DispatcherQueueSynchronizationContextで仕掛けを作れる？ デッドロックしそうならthrowしたい
                await Task.Delay(1).ConfigureAwait(false);
                _logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] delay end");

                //awaitからのcontinueで、DispatchQueue()が終わってDispatcherQueueSynchronizationContextからPost()されるので、もう一度DispatchQueue()を実行

                if (error)
                {
                    throw new TestException();
                }

                mainTcs.SetResult(1);
                _logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] SetResult");
            });
        }
        else if (methodType == MethodType.SendOrPostCallback)
        {
            queue.Dispatch(async (param) =>
            {
                _logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] delay start");

                //TODO ConfigureAwait(true)でデッドロックさせない方法？ DispatcherQueueSynchronizationContextで仕掛けを作れる？ デッドロックしそうならthrowしたい
                await Task.Delay(1).ConfigureAwait(false);
                _logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] delay end");

                //awaitからのcontinueで、DispatchQueue()が終わってDispatcherQueueSynchronizationContextからPost()されるので、もう一度DispatchQueue()を実行

                if (error)
                {
                    throw new TestException();
                }

                mainTcs.SetResult((int)param!);
                _logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] SetResult");
            }, 1);
        }
        else if (methodType == MethodType.Async)
        {
            var result = await queue.DispatchAsync(async () =>
            {
                _logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] delay start");

                //TODO ConfigureAwait(true)でデッドロックさせない方法？ DispatcherQueueSynchronizationContextで仕掛けを作れる？ デッドロックしそうならthrowしたい
                await Task.Delay(1).ConfigureAwait(false);
                _logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] delay end");

                //awaitからのcontinueで、DispatchQueue()が終わってDispatcherQueueSynchronizationContextからPost()されるので、もう一度DispatchQueue()を実行

                if (error)
                {
                    throw new TestException();
                }

                return 1;
            });

            try
            {
                _logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] SetResult");
                mainTcs.SetResult(await result);
            }
            catch (Exception e)
            {
                _logger.LogInformation(e, $"★thread:[{Environment.CurrentManagedThreadId:X}] OnError");
                mainTcs.SetException(e);
                _logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] SetException");
            }
        }
        else
        {
            Assert.Fail();
        }

        try
        {
            _logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] await start");
            var result = await mainTcs.Task;

            if (error)
            {
                Assert.Fail();
            }

            _logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] await end");
            Assert.AreEqual(1, result);
        }
        catch (TestException e)
        {
            _logger.LogInformation(e, $"★thread:[{Environment.CurrentManagedThreadId:X}] error end");
            if (!error)
            {
                Assert.Fail();
            }
        }

        await main;

        try
        {
            //停止後は呼べない
            if (methodType == MethodType.Action)
            {
                queue.Dispatch(() => { });
            }
            else if (methodType == MethodType.SendOrPostCallback)
            {
                queue.Dispatch((param) => { }, 2);
            }
            else if (methodType == MethodType.Async)
            {
                await queue.DispatchAsync(() => { return true; });
            }
            Assert.Fail();
        }
        catch (InvalidOperationException e)
        {
            //OK
            _logger.LogInformation(e, "error occurred");
        }
    }
}
