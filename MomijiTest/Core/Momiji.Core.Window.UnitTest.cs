using System.ComponentModel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Momiji.Core.Window;

[TestClass]
public class WindowExceptionUnitTest
{
    [TestMethod]
    public void Test1()
    {
        var test = new WindowException("test1");
        Assert.IsNotNull(test.Message);
    }

    [TestMethod]
    public void Test2()
    {
        var test = new WindowException("test2", new Exception("inner"));
        Assert.IsNotNull(test.Message);
    }
}

[TestClass]
public partial class WindowUnitTest : IDisposable
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;

    public WindowUnitTest()
    {
        var configuration = CreateConfiguration();

        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConfiguration(configuration);

            builder.AddFilter("Momiji", LogLevel.Warning);
            builder.AddFilter("Momiji.Core.Window", LogLevel.Trace);
            builder.AddFilter("Momiji.Core.Window.WindowUnitTest", LogLevel.Trace);
            builder.AddFilter("Microsoft", LogLevel.Warning);
            builder.AddFilter("System", LogLevel.Warning);

            builder.AddConsole();
            builder.AddDebug();
        });

        _logger = _loggerFactory.CreateLogger<WindowUnitTest>();
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

    [TestMethod]
    public async Task TestRegisterClassFail()
    {
        try
        {
            await using var factory = new UIThreadFactory(_loggerFactory);
            await using var thread = await factory.StartAsync();

            var window = await thread.DispatchAsync((manager) => manager.CreateWindow(new() { classStyle = 0x9999 }));

            Assert.Fail("エラーが発生しなかった");
        }
        catch (Win32Exception e)
        {
            //OK
            _logger.LogInformation(e, "error occurred");
        }
    }

    [TestMethod]
    public async Task TestStartAsync()
    {
        await using var factory = new UIThreadFactory(_loggerFactory);

        {//１回目起動
            _logger.LogInformation("======================= 1st start");
            await using var thread = await factory.StartAsync();
            _logger.LogInformation("======================= 1st end");
        }

        {//２回目起動
            _logger.LogInformation("======================= 2nd start");
            await using var thread = await factory.StartAsync();
            _logger.LogInformation("======================= 2nd end");
        }

        {//二重起動
            _logger.LogInformation("======================= 3rd start");
            await using var thread1 = await factory.StartAsync();
            _logger.LogInformation("======================= 3rd status");

            _logger.LogInformation("======================= 4th start");
            await using var thread2 = await factory.StartAsync();
            _logger.LogInformation($"======================= 4th status");
        }
    }

    [TestMethod]
    public async Task TestDispose()
    {
        var factory = new UIThreadFactory(_loggerFactory);

        {//起動したままdispose
            _logger.LogInformation("======================= start");
            var _ = await factory.StartAsync();

            factory.Dispose();
            _logger.LogInformation("======================= end");
        }

        try
        {//dispose後のstartはNG
            var thread = await factory.StartAsync();
            Assert.Fail("エラーが発生しなかった");
        }
        catch (ObjectDisposedException e)
        {
            _logger.LogInformation(e, "error occurred");
        }
    }

    [TestMethod]
    public async Task TestDisposeAync()
    {
        var factory = new UIThreadFactory(_loggerFactory);

        {//起動したままdispose async
            _logger.LogInformation("======================= start");
            var _ = await factory.StartAsync();

            await factory.DisposeAsync();
            _logger.LogInformation("======================= end");
        }

        try
        {//dispose後のstartはNG
            var _ = await factory.StartAsync();
            Assert.Fail("エラーが発生しなかった");
        }
        catch (ObjectDisposedException e)
        {
            _logger.LogInformation(e, "error occurred");
        }
    }

    [TestMethod]
    public async Task TestCreateWindow()
    {
        await using var factory = new UIThreadFactory(_loggerFactory);
        await using var thread = await factory.StartAsync();

        var window = await thread.DispatchAsync((manager) => manager.CreateWindow(new()));
        await window.MoveAsync(0, 0, 100, 100, true);
        await window.ShowAsync(1);
        await window.MoveAsync(100, 100, 100, 100, true);
        await window.MoveAsync(200, 200, 200, 200, true);
        await window.ShowAsync(0);

        window.Close();

        try
        {
            await window.ShowAsync(1);
            Assert.Fail("エラーが起きなかった");
        }
        catch (Exception e)
        {
            _logger.LogInformation(e, "show failed.");
        }
    }

    [TestMethod]
    public async Task TestCloseImmidiateCall()
    {
        await using var factory = new UIThreadFactory(_loggerFactory);
        await using var thread = await factory.StartAsync();

        var window = await thread.DispatchAsync((manager) => manager.CreateWindow(new()));

        await window.DispatchAsync((window) => { window.Close(); return 0; });
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task TestOnMassage(bool close)
    {
        await using var factory = new UIThreadFactory(_loggerFactory);
        await using var thread = await factory.StartAsync();

        var canClose = false;

        var window = await thread.DispatchAsync((manager) => manager.CreateWindow(new()
        {
            onMessage = (sender, message) =>
            {
                switch (message.Msg)
                {
                    case 0x0010://WM_CLOSE
                        _logger.LogInformation($"WM_CLOSE canClose {canClose}");
                        if (!canClose)
                        {
                            message.Handled = true;
                        }

                        break;
                }
            }
        }));
        await window.ShowAsync(1);

        //canClose:false
        window.Close();

        if (close)
        {
            canClose = true;
            window.Close();
        }
    }

    [TestMethod]
    public async Task TestCreateChildWindow()
    {
        await using var factory = new UIThreadFactory(_loggerFactory);
        await using var thread = await factory.StartAsync();

        var window = await thread.DispatchAsync((manager) => manager.CreateWindow(new()
        {
            onMessage = (sender, message) =>
            {
                _logger.LogInformation($"on message {message}");

                if (message.Msg == 0x0001) //WM_CREATE
                {
                    var child = manager.CreateWindow(new()
                    {
                        parent = sender,
                        style = 0x40000000, //WS_CHILD
                        className = "EDIT",
                        onMessage = (sender, message) =>
                        {
                            _logger.LogInformation($"child on message {message}");
                        }
                    });
                }
            }
        }));
    }

    [TestMethod]
    public async Task TestCreateChildWindowFail()
    {
        await using var factory = new UIThreadFactory(_loggerFactory);
        await using var thread = await factory.StartAsync();

        try
        {
            var window = await thread.DispatchAsync((manager) => manager.CreateWindow(new()
            {
                onMessage = (sender, message) =>
                {
                    _logger.LogInformation($"on message {message}");

                    if (message.Msg == 0x0001) //WM_CREATE
                    {
                        try
                        {
                            //*** async でイベントを作っているとWM_CREATEでエラーにできない
                            var child = manager.CreateWindow(new()
                            {
                                parent = sender,
                                style = 0x40000000, //WS_CHILD
                                className = "EDITXXX",
                                onMessage = (sender, message) =>
                                {
                                    _logger.LogInformation($"child on message {message}");
                                }
                            });
                        }
                        catch (Exception e)
                        {
                            _logger.LogInformation(e, "ERROR");
                            throw;
                        }
                    }
                }
            }));

            Assert.Fail("エラーが発生しなかった");
        }
        catch (Exception e)
        {
            //OK
            _logger.LogInformation(e, "error occurred");
        }
    }

    public enum MethodType
    {
        Send,
        Post
    }

    public enum CallType
    {
        Direct,
        InDispatch,
        InTask,
    }

    [TestMethod]
    [DataRow(MethodType.Send, CallType.Direct, CallType.Direct)]
    [DataRow(MethodType.Send, CallType.Direct, CallType.InDispatch)]
    [DataRow(MethodType.Send, CallType.Direct, CallType.InTask)]
    [DataRow(MethodType.Send, CallType.InDispatch, CallType.Direct)]
    [DataRow(MethodType.Send, CallType.InDispatch, CallType.InDispatch)]
    [DataRow(MethodType.Send, CallType.InDispatch, CallType.InTask)]
    [DataRow(MethodType.Send, CallType.InTask, CallType.Direct)]
    [DataRow(MethodType.Send, CallType.InTask, CallType.InDispatch)]
    [DataRow(MethodType.Send, CallType.InTask, CallType.InTask)]
    [DataRow(MethodType.Send, CallType.Direct, CallType.Direct, true)]
    [DataRow(MethodType.Send, CallType.Direct, CallType.InDispatch, true)]
    [DataRow(MethodType.Send, CallType.Direct, CallType.InTask, true)]
    [DataRow(MethodType.Send, CallType.InDispatch, CallType.Direct, true)]
    [DataRow(MethodType.Send, CallType.InDispatch, CallType.InDispatch, true)]
    [DataRow(MethodType.Send, CallType.InDispatch, CallType.InTask, true)]
    [DataRow(MethodType.Send, CallType.InTask, CallType.Direct, true)]
    [DataRow(MethodType.Send, CallType.InTask, CallType.InDispatch, true)]
    [DataRow(MethodType.Send, CallType.InTask, CallType.InTask, true)]
    [DataRow(MethodType.Post, CallType.Direct, CallType.Direct)]
    [DataRow(MethodType.Post, CallType.Direct, CallType.InDispatch)]
    [DataRow(MethodType.Post, CallType.Direct, CallType.InTask)]
    [DataRow(MethodType.Post, CallType.InDispatch, CallType.Direct)]
    [DataRow(MethodType.Post, CallType.InDispatch, CallType.InDispatch)]
    [DataRow(MethodType.Post, CallType.InDispatch, CallType.InTask)]
    [DataRow(MethodType.Post, CallType.InTask, CallType.Direct)]
    [DataRow(MethodType.Post, CallType.InTask, CallType.InDispatch)]
    [DataRow(MethodType.Post, CallType.InTask, CallType.InTask)]
    [DataRow(MethodType.Post, CallType.Direct, CallType.Direct, true)]
    [DataRow(MethodType.Post, CallType.Direct, CallType.InDispatch, true)]
    [DataRow(MethodType.Post, CallType.Direct, CallType.InTask, true)]
    [DataRow(MethodType.Post, CallType.InDispatch, CallType.Direct, true)]
    [DataRow(MethodType.Post, CallType.InDispatch, CallType.InDispatch, true)]
    [DataRow(MethodType.Post, CallType.InDispatch, CallType.InTask, true)]
    [DataRow(MethodType.Post, CallType.InTask, CallType.Direct, true)]
    [DataRow(MethodType.Post, CallType.InTask, CallType.InDispatch, true)]
    [DataRow(MethodType.Post, CallType.InTask, CallType.InTask, true)]
    public async Task TestMessage(
        MethodType methodType,
        CallType start,
        CallType inMessage,
        bool error = false
    )
    {
        Exception? errorStop = default;
        using var cde = new CountdownEvent(3);
        {
            await using var factory = new UIThreadFactory(_loggerFactory);
            var thread = await factory.StartAsync(
                (exception) => {
                    _logger.LogInformation(exception, $"★thread:[{Environment.CurrentManagedThreadId:X}] on stop  [cde:{cde.CurrentCount}]===============================[{SynchronizationContext.Current}]");

                    //exceptionが来たらNG
                    Assert.IsNull(exception);

                    _logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] on stop end [cde:{cde.CurrentCount}]===============================[{SynchronizationContext.Current}]");
                },
                (exception) =>
                {
                    _logger.LogInformation(exception, $"★thread:[{Environment.CurrentManagedThreadId:X}] on error [cde:{cde.CurrentCount}]===============================[{SynchronizationContext.Current}]");

                    errorStop = exception;

                    if (!cde.IsSet)
                    {
                        cde.Signal();
                    }

                    _logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] on error end [cde:{cde.CurrentCount}]===============================[{SynchronizationContext.Current}]");
                    return true;
                }
            );

            nint method(IWindow window, nint wParam, nint lParam)
            {
                var result = nint.Zero;
                _logger.LogInformation($"●thread:[{Environment.CurrentManagedThreadId:X}] call start ===============================[{SynchronizationContext.Current}]");
                switch (methodType)
                {
                    case MethodType.Send:
                        result = window.SendMessage(0, wParam, lParam);
                        break;

                    case MethodType.Post:
                        window.PostMessage(0, wParam, lParam);
                        break;
                }
                _logger.LogInformation($"●thread:[{Environment.CurrentManagedThreadId:X}] call end ===============================[{SynchronizationContext.Current}]");
                return result;
            };

            async ValueTask<nint> call(CallType callType, IWindow window, nint wParam, nint lParam)
            {
                var result = nint.Zero;

                switch (callType)
                {
                    case CallType.Direct:
                        _logger.LogInformation($"▼thread:[{Environment.CurrentManagedThreadId:X}] Direct start ===============================");
                        result = method(window, wParam, lParam);
                        _logger.LogInformation($"▼thread:[{Environment.CurrentManagedThreadId:X}] Direct end ===============================");
                        break;

                    case CallType.InDispatch:
                        _logger.LogInformation($"▼thread:[{Environment.CurrentManagedThreadId:X}] InDispatch start ===============================");
                        result = await window.DispatchAsync((window) => method(window, wParam, lParam));
                        _logger.LogInformation($"▼thread:[{Environment.CurrentManagedThreadId:X}] InDispatch end ===============================");
                        break;

                    case CallType.InTask:
                        async ValueTask<nint> a()
                        {
                            var result = method(window, wParam, lParam);
                            await Task.Delay(1);
                            return result;
                        };
                        _logger.LogInformation($"▼thread:[{Environment.CurrentManagedThreadId:X}] InTask start ===============================");
                        result = await a();
                        _logger.LogInformation($"▼thread:[{Environment.CurrentManagedThreadId:X}] InTask end ===============================");
                        break;
                }

                cde.Signal();
                _logger.LogInformation($"■thread:[{Environment.CurrentManagedThreadId:X}] call end [cde:{cde.CurrentCount}]");
                return result;
            };

            var window = await thread.DispatchAsync((manager) => manager.CreateWindow(new()
            {
                onMessage = async (sender, message) =>
                {
                    if (message.Msg == 0)
                    {
                        if (message.LParam == 999)
                        {
                            _logger.LogInformation($"■thread:[{Environment.CurrentManagedThreadId:X}] REENTRANT on message [{message}] [cde:{cde.CurrentCount}]");
                            if (error)
                            {
                                _logger.LogInformation($"■thread:[{Environment.CurrentManagedThreadId:X}] throw ERROR on message [{message}] [cde:{cde.CurrentCount}]");
                                throw new Exception("！！！ERROR！！！");
                            }
                            cde.Signal();
                            _logger.LogInformation($"■thread:[{Environment.CurrentManagedThreadId:X}] REENTRANT on message end [{message}] [cde:{cde.CurrentCount}]");
                        }
                        else
                        {
                            _logger.LogInformation($"■thread:[{Environment.CurrentManagedThreadId:X}] on message [{message}] [cde:{cde.CurrentCount}]");

                            var result = await call(inMessage, sender, message.WParam * 10, 999);

                            _logger.LogInformation($"■thread:[{Environment.CurrentManagedThreadId:X}] on message result [{result}] [cde:{cde.CurrentCount}]");
                        }

                        message.Handled = true;
                        message.Result = message.LParam;
                    }
                }
            }));

            {
                var result = await call(start, window, 1, 2);
                _logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] result [{result}] [cde:{cde.CurrentCount}]");
            }

            _logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] cde wait start [cde:{cde.CurrentCount}] ===============================");
            cde.Wait();
            _logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] cde wait end [cde:{cde.CurrentCount}] ===============================");

            _logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] cde wait thread DisposeAsync start ===============================");
            await thread.DisposeAsync();
            _logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] cde wait thread DisposeAsync end ===============================");
        }

        if (error)
        {
            Assert.IsNotNull(errorStop);
        }
        else
        {
            Assert.IsNull(errorStop);
        }
    }

    [TestMethod]
    [DataRow(CallType.Direct, CallType.Direct)]
    [DataRow(CallType.Direct, CallType.InDispatch)]
    [DataRow(CallType.Direct, CallType.InTask)]
    [DataRow(CallType.InDispatch, CallType.Direct)]
    [DataRow(CallType.InDispatch, CallType.InDispatch)]
    [DataRow(CallType.InDispatch, CallType.InTask)]
    [DataRow(CallType.InTask, CallType.Direct)]
    [DataRow(CallType.InTask, CallType.InDispatch)]
    [DataRow(CallType.InTask, CallType.InTask)]
    [DataRow(CallType.Direct, CallType.Direct, true)]
    [DataRow(CallType.Direct, CallType.InDispatch, true)]
    [DataRow(CallType.Direct, CallType.InTask, true)]
    [DataRow(CallType.InDispatch, CallType.Direct, true)]
    [DataRow(CallType.InDispatch, CallType.InDispatch, true)]
    [DataRow(CallType.InDispatch, CallType.InTask, true)]
    [DataRow(CallType.InTask, CallType.Direct, true)]
    [DataRow(CallType.InTask, CallType.InDispatch, true)]
    [DataRow(CallType.InTask, CallType.InTask, true)]
    public async Task TestDispatch(
        CallType start,
        CallType inMessage,
        bool error = false
    )
    {
        Exception? errorStop = default;
        using var cde = new CountdownEvent(1);
        {
            await using var factory = new UIThreadFactory(_loggerFactory);
            var thread = await factory.StartAsync((exception) => {
                _logger.LogInformation(exception, $"★thread:[{Environment.CurrentManagedThreadId:X}] on stop  [cde:{cde.CurrentCount}]===============================[{SynchronizationContext.Current}]");

                if (!cde.IsSet)
                {
                    cde.Signal();
                }
            });

            async ValueTask<int> method(int param)
            {
                if (param == 2)
                {
                    _logger.LogInformation($"■thread:[{Environment.CurrentManagedThreadId:X}] REENTRANT on message [cde:{cde.CurrentCount}]");
                    if (error)
                    {
                        _logger.LogInformation($"■thread:[{Environment.CurrentManagedThreadId:X}] throw ERROR on message [cde:{cde.CurrentCount}]");
                        throw new Exception("！！！ERROR！！！");
                    }
                    return 0;
                }
                else
                {
                    _logger.LogInformation($"●thread:[{Environment.CurrentManagedThreadId:X}] call start ===============================[{SynchronizationContext.Current}]");
                    var result = await call(inMessage, param * 2);
                    _logger.LogInformation($"●thread:[{Environment.CurrentManagedThreadId:X}] call end ===============================");
                    cde.Signal();
                    return result;
                }
            };

            async ValueTask<int> call(CallType callType, int param)
            {
                var result = 0;

                switch (callType)
                {
                    case CallType.Direct:
                        _logger.LogInformation($"▼thread:[{Environment.CurrentManagedThreadId:X}] Direct start ===============================");
                        result = await method(param);
                        _logger.LogInformation($"▼thread:[{Environment.CurrentManagedThreadId:X}] Direct end ===============================");
                        break;

                    case CallType.InDispatch:
                        _logger.LogInformation($"▼thread:[{Environment.CurrentManagedThreadId:X}] InDispatch start ===============================");
                        result = await await thread.DispatchAsync((manager) => method(param));
                        _logger.LogInformation($"▼thread:[{Environment.CurrentManagedThreadId:X}] InDispatch end ===============================");
                        break;

                    case CallType.InTask:
                        async ValueTask<int> a()
                        {
                            var result = method(param);
                            await Task.Delay(1);
                            return await result;
                        };
                        _logger.LogInformation($"▼thread:[{Environment.CurrentManagedThreadId:X}] InTask start ===============================");
                        result = await a();
                        _logger.LogInformation($"▼thread:[{Environment.CurrentManagedThreadId:X}] InTask end ===============================");
                        break;

                }

                return result;
            };

            try
            {
                var result = await call(start, 1);
                _logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] result [{result}] [cde:{cde.CurrentCount}]");
            }
            catch (Exception e)
            {
                _logger.LogInformation(e, $"★thread:[{Environment.CurrentManagedThreadId:X}] exception  [cde:{cde.CurrentCount}]===============================[{SynchronizationContext.Current}]");

                errorStop = e;

                if (!cde.IsSet)
                {
                    cde.Signal();
                }
            }

            _logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] cde wait start [cde:{cde.CurrentCount}] ===============================");
            cde.Wait();
            _logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] cde wait end [cde:{cde.CurrentCount}] ===============================");

            _logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] cde wait thread DisposeAsync start ===============================");
            await thread.DisposeAsync();
            _logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] cde wait thread DisposeAsync end ===============================");
        }

        if (error)
        {
            Assert.IsNotNull(errorStop);
        }
        else
        {
            Assert.IsNull(errorStop);
        }
    }

    [TestMethod]
    public async Task TestSetWindowStyle()
    {
        await using var factory = new UIThreadFactory(_loggerFactory);
        await using var thread = await factory.StartAsync();

        var window = await thread.DispatchAsync((manager) => manager.CreateWindow(new()));

        await window.SetWindowStyleAsync(0);

        {
            var result = await await window.DispatchAsync(async (window) => {
                //immidiate mode
                return await window.SetWindowStyleAsync(0);
            });
            Assert.IsTrue(result);
        }
    }

    [TestMethod]
    public async Task TestDispatch1()
    {
        await using var factory = new UIThreadFactory(_loggerFactory);
        await using var thread = await factory.StartAsync();

        var window = await thread.DispatchAsync((manager) => manager.CreateWindow(new()));

        {
            _logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] Dispatch 1 put ===============================");
            var result = await window.DispatchAsync((window) => {
                _logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] Dispatch 1 run ===============================");
                return 999; 
            });
            _logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] Dispatch 1 end ===============================");
            Assert.AreEqual(999, result);
        }
    }

    [TestMethod]
    public async Task TestDispatch2()
    {
        await using var factory = new UIThreadFactory(_loggerFactory);
        await using var thread = await factory.StartAsync();

        var window = await thread.DispatchAsync((manager) => manager.CreateWindow(new()));

        {
            _logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] Dispatch 2 put ===============================");
            var result = await await window.DispatchAsync(async (window) => {
                _logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] Dispatch 2 run ===============================");
                _logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] Dispatch 3 put ===============================");
                var before = Environment.CurrentManagedThreadId;

                var result = await await window.DispatchAsync(async (window) =>
                { //re-entrant
                    _logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] Dispatch 3 run ===============================");
                    var before = Environment.CurrentManagedThreadId;
                    await Task.Delay(1);
                    var after = Environment.CurrentManagedThreadId;
                    Assert.AreEqual(before, after);
                    _logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] Dispatch 3 exit ===============================");
                    return 888;
                });
                _logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] Dispatch 3 end [{result}] ===============================");

                var after = Environment.CurrentManagedThreadId;
                Assert.AreEqual(before, after);

                return result;
            });
            _logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] Dispatch 2 end [{result}] ===============================");
            Assert.AreEqual(888, result);
        }
    }

    [TestMethod]
    public async Task TestDispatch3()
    {
        await using var factory = new UIThreadFactory(_loggerFactory);
        await using var thread = await factory.StartAsync();

        var window = await thread.DispatchAsync((manager) => manager.CreateWindow(new()));

        {
            _logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] Dispatch 4 put ===============================");
            var result = await await window.DispatchAsync(async (window) => {
                _logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] Dispatch 4 run ===============================");
                await Task.Run(() => {
                    _logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] sleep ===============================");
                    Thread.Sleep(1000);
                }).ContinueWith((task) => {
                    _logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] continue ===============================");
                }); //デッドロックしない？
                _logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] Dispatch 4 exit ===============================");
                return 999;
            });
            _logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] Dispatch 4 end ===============================");
            Assert.AreEqual(999, result);
        }
    }

    [TestMethod]
    public async Task TestDispatch4()
    {
        await using var factory = new UIThreadFactory(_loggerFactory);
        await using var thread = await factory.StartAsync();

        using var cde = new CountdownEvent(1);

        var window = await thread.DispatchAsync((manager) => manager.CreateWindow(new()
        {
            onMessage = async (sender, message) =>
            {
                if (message.Msg == 0)
                {
                    _logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] message [{message}] ===============================");
                    var result = await await sender.DispatchAsync(async (window) =>
                    {
                        _logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] task run ===============================");

                        {
                            _logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] dispatch 2 put ===============================");
                            var result2 = await sender.DispatchAsync((window) =>
                            {
                                _logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] dispatch 2 run ===============================");
                                return 2;
                            });
                            _logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] dispatch 2 end[{result2}] ===============================");
                        }

                        var task = Task.Run(async () =>
                        {
                            _logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] sleep ===============================");
                            Thread.Sleep(1000);

                            _logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] dispatch 3 put ===============================");
                            var result3 = await sender.DispatchAsync((window) =>
                            {
                                _logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] dispatch 3 run ===============================");
                                return 3;
                            });
                            _logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] dispatch 3 end[{result3}] ===============================");

                            return result3;

                        }).ContinueWith((task) =>
                        {
                            _logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] continue[{task.Result}] ===============================");
                            return task.Result * 2;
                        });

                        //デッドロックしない
                        var result4 = await task.ConfigureAwait(true);
                        _logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] task end[{result4}] ===============================");

                        return result4;
                    });
                    _logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] dispatch end[{result}] ===============================");

                    cde.Signal();
                }
            }
        }));

        var result = window.SendMessage(0, 1, 2);

        cde.Wait();
    }

    [TestMethod]
    public async Task TestDispatch5()
    {
        var errorMessage = $"ERROR{Guid.NewGuid()}";

        await using var factory = new UIThreadFactory(_loggerFactory);
        await using var thread = await factory.StartAsync();

        var window = await thread.DispatchAsync((manager) => manager.CreateWindow(new()
        {
            onMessage = async (sender, message) =>
            {
                if (message.Msg == 0)
                {
                    _logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] message [{message}] ===============================[{SynchronizationContext.Current}]");

                    var task = await sender.DispatchAsync(async (window) =>
                    {
                        _logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] task run ===============================[{SynchronizationContext.Current}]");

                        var result4 = await Task.Run(async () =>
                        {
                            _logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] dispatch 3 put ===============================[{SynchronizationContext.Current}]");
                            var result3 = await sender.DispatchAsync((window) =>
                            {
                                throw new Exception(errorMessage);
    #pragma warning disable CS0162 // 到達できないコードが検出されました
                                return 0;
    #pragma warning restore CS0162 // 到達できないコードが検出されました
                            });
                            _logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] dispatch 3 end[{result3}] ===============================[{SynchronizationContext.Current}]");
                            return result3;

                        }).ContinueWith((task) =>
                        {
                            _logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] continue[{task.Result}] ===============================[{SynchronizationContext.Current}]");
                            return task.Result * 2;

                        }).ConfigureAwait(true);

                        _logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] task end[{result4}] ===============================[{SynchronizationContext.Current}]");

                        return result4;
                    });

                    //AggregateExceptionがthrowされる
                    var result = await task;
                    _logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] dispatch end[{result}] ===============================[{SynchronizationContext.Current}]");
                }
            }
        }));

        var result = window.SendMessage(0, 1, 2);

        await Task.Delay(1000);
    }

    [TestMethod]
    public async Task TestCreateWindowFromOtherThread()
    {
        await using var factory = new UIThreadFactory(_loggerFactory);
        await using var threadA = await factory.StartAsync();
        await using var threadB = await factory.StartAsync();

        var windowA = await threadA.DispatchAsync((manager) => manager.CreateWindow(new()
        {
            windowTitle = "windowA",
            onMessage = (sender, message) =>
            {
                _logger.LogInformation($"PARENT on message {message}");
            }
        }));

        var buttonA = await threadA.DispatchAsync((manager) => manager.CreateWindow(new()
        {
            windowTitle = "buttonA",
            parent = windowA,
            style = 0x40000000, //WS_CHILD
            className = "BUTTON",
            onMessage = (sender, message) =>
            {
                _logger.LogInformation($"CHILD A on message {message}");
            }
        }));

        var windowAA = await threadA.DispatchAsync((manager) => manager.CreateWindow(new()
        {
            windowTitle = "windowAA",
            parent = windowA,
            style = 0x40000000, //WS_CHILD
            onMessage = (sender, message) =>
            {
                _logger.LogInformation($"CHILD W on message {message}");
            }
        }));

        var buttonB = await threadB.DispatchAsync((manager) => manager.CreateWindow(new()
        {
            windowTitle = "buttonB",
            parent = windowA,
            style = 0x40000000, //WS_CHILD
            className = "BUTTON",
            onMessage = (sender, message) =>
            {
                _logger.LogInformation($"CHILD B on message {message}");
            }
        }));

        await Task.Delay(1000);
    }


}
