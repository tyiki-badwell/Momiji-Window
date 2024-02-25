using System;
using System.ComponentModel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestPlatform.CommunicationUtilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Windows.Devices.PointOfService;
using static Momiji.Core.Window.IWindowManager;

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
public class WindowUnitTest : IDisposable
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

    private static IConfiguration CreateConfiguration(
        int cs = 0
    )
    {
        var configuration =
            new ConfigurationBuilder()
            .AddJsonFile("appsettings.json")
            .Build();

        var section = configuration.GetSection("Momiji.Core.Window.WindowManager");

        if (cs != 0)
        {
            section["CS"] = cs.ToString();
        }

        return configuration;
    }

    [TestMethod]
    public async Task TestRegisterClassFail()
    {
        try
        {
            await using var factory = new WindowManagerFactory(CreateConfiguration(0x9999), _loggerFactory);
            await using var manager = await factory.StartAsync();
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
        await using var factory = new WindowManagerFactory(CreateConfiguration(), _loggerFactory);

        {//１回目起動
            _logger.LogInformation("======================= 1st start");
            await using var manager = await factory.StartAsync();
            _logger.LogInformation("======================= 1st end");
        }

        {//２回目起動
            _logger.LogInformation("======================= 2nd start");
            await using var manager = await factory.StartAsync();
            _logger.LogInformation("======================= 2nd end");
        }

        {//二重起動
            _logger.LogInformation("======================= 3rd start");
            await using var manager1 = await factory.StartAsync();
            _logger.LogInformation("======================= 3rd status");

            _logger.LogInformation("======================= 4th start");
            await using var manager2 = await factory.StartAsync();
            _logger.LogInformation($"======================= 4th status");
        }
    }

    [TestMethod]
    public async Task TestDispose()
    {
        var factory = new WindowManagerFactory(CreateConfiguration(), _loggerFactory);

        {//起動したままdispose
            _logger.LogInformation("======================= start");
            var _ = await factory.StartAsync();

            factory.Dispose();
            _logger.LogInformation("======================= end");
        }

        try
        {//dispose後のstartはNG
            var manager = await factory.StartAsync();
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
        var factory = new WindowManagerFactory(CreateConfiguration(), _loggerFactory);

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
        await using var factory = new WindowManagerFactory(CreateConfiguration(), _loggerFactory);
        await using var manager = await factory.StartAsync();

        var window = manager.CreateWindow("window");
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
    [DataRow(true)]
    [DataRow(false)]
    public async Task TestOnMassage(bool close)
    {
        await using var factory = new WindowManagerFactory(CreateConfiguration(), _loggerFactory);
        await using var manager = await factory.StartAsync();

        var canClose = false;

        var window = manager.CreateWindow("window", (sender, message) => {
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
        });
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
        await using var factory = new WindowManagerFactory(CreateConfiguration(), _loggerFactory);
        await using var manager = await factory.StartAsync();

        var window = manager.CreateWindow("window", (sender, message) => {
            _logger.LogInformation($"on message {message}");

            if (message.Msg == 0x0001) //WM_CREATE
            {
                var child = manager.CreateChildWindow(sender, "EDIT", "child", (sender, message) => {
                    _logger.LogInformation($"child on message {message}");
                });
            }
        });
    }

    [TestMethod]
    public async Task TestCreateChildWindowFail()
    {
        await using var factory = new WindowManagerFactory(CreateConfiguration(), _loggerFactory);
        await using var manager = await factory.StartAsync();

        try
        {
            var window = manager.CreateWindow("window", (sender, message) =>
            {
                _logger.LogInformation($"on message {message}");

                if (message.Msg == 0x0001) //WM_CREATE
                {
                    try
                    {
                        //*** async でイベントを作っているとWM_CREATEでエラーにできない
                        var child = manager.CreateChildWindow(sender, "EDITXXX", "child", (sender, message) =>
                        {
                            _logger.LogInformation($"child on message {message}");
                        });
                    }
                    catch (Exception e)
                    {
                        _logger.LogInformation(e, "ERROR");
                        throw;
                    }
                }
            });

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
    [DataRow(MethodType.Post, CallType.Direct, CallType.Direct)]
    [DataRow(MethodType.Post, CallType.Direct, CallType.InDispatch)]
    [DataRow(MethodType.Post, CallType.Direct, CallType.InTask)]
    [DataRow(MethodType.Post, CallType.InDispatch, CallType.Direct)]
    [DataRow(MethodType.Post, CallType.InDispatch, CallType.InDispatch)]
    [DataRow(MethodType.Post, CallType.InDispatch, CallType.InTask)]
    [DataRow(MethodType.Post, CallType.InTask, CallType.Direct)]
    [DataRow(MethodType.Post, CallType.InTask, CallType.InDispatch)]
    [DataRow(MethodType.Post, CallType.InTask, CallType.InTask)]
    public async Task TestMessage(
        MethodType methodType,
        CallType start,
        CallType inMessage
    )
    {
        await using var factory = new WindowManagerFactory(CreateConfiguration(), _loggerFactory);
        await using var manager = await factory.StartAsync();

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
            _logger.LogInformation($"●thread:[{Environment.CurrentManagedThreadId:X}] call end ===============================");
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

            return result;
        };

        using var cde = new CountdownEvent(1);

        var window = manager.CreateWindow("window", async (sender, message) => {
            if (message.Msg == 0)
            {
                if (message.LParam == 999)
                {
                    _logger.LogInformation($"■thread:[{Environment.CurrentManagedThreadId:X}] REENTRANT on message [{message}] [cde:{cde.CurrentCount}]");
                }
                else
                {
                    _logger.LogInformation($"■thread:[{Environment.CurrentManagedThreadId:X}] on message [{message}] [cde:{cde.CurrentCount}]");
                    var result = await call(inMessage, sender, message.WParam * 10, 999);
                    cde.Signal();
                    _logger.LogInformation($"■thread:[{Environment.CurrentManagedThreadId:X}] on message result [{result}] [cde:{cde.CurrentCount}]");
                }

                message.Handled = true;
                message.Result = message.LParam;
            }
        });

        {
            var result = await call(start, window, 1, 2);
            _logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] result [{result}] [cde:{cde.CurrentCount}]");
        }

        _logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] cde wait start [cde:{cde.CurrentCount}] ===============================");
        cde.Wait(1000);
        _logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] cde wait end [cde:{cde.CurrentCount}] ===============================");
    }

    [TestMethod]
    public async Task TestSetWindowStyle()
    {
        await using var factory = new WindowManagerFactory(CreateConfiguration(), _loggerFactory);
        await using var manager = await factory.StartAsync();

        var window = manager.CreateWindow("window");

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
        await using var factory = new WindowManagerFactory(CreateConfiguration(), _loggerFactory);
        await using var manager = await factory.StartAsync();

        var window = manager.CreateWindow("window");

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
        await using var factory = new WindowManagerFactory(CreateConfiguration(), _loggerFactory);
        await using var manager = await factory.StartAsync();

        var window = manager.CreateWindow("window");

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
                _logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] Dispatch 3 end ===============================");

                var after = Environment.CurrentManagedThreadId;
                Assert.AreEqual(before, after);

                return result;
            });
            _logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] Dispatch 2 end ===============================");
            Assert.AreEqual(888, result);
        }
    }

    [TestMethod]
    public async Task TestDispatch3()
    {
        await using var factory = new WindowManagerFactory(CreateConfiguration(), _loggerFactory);
        await using var manager = await factory.StartAsync();

        var window = manager.CreateWindow("window");

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
        await using var factory = new WindowManagerFactory(CreateConfiguration(), _loggerFactory);
        await using var manager = await factory.StartAsync();

        using var cde = new CountdownEvent(1);

        var window = manager.CreateWindow("window", async (sender, message) => {
            if (message.Msg == 0)
            {
                _logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] message [{message}] ===============================");
                var result = await await sender.DispatchAsync(async (window) => {
                    _logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] task run ===============================");

                    {
                        _logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] dispatch 2 put ===============================");
                        var result2 = await sender.DispatchAsync((window) => {
                            _logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] dispatch 2 run ===============================");
                            return 2;
                        });
                        _logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] dispatch 2 end[{result2}] ===============================");
                    }

                    var task = Task.Run(async () => {
                        _logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] sleep ===============================");
                        Thread.Sleep(1000);

                        _logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] dispatch 3 put ===============================");
                        var result3 = await sender.DispatchAsync((window) => {
                            _logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] dispatch 3 run ===============================");
                            return 3;
                        });
                        _logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] dispatch 3 end[{result3}] ===============================");

                        return result3;

                    }).ContinueWith((task) => {
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
        });

        var result = window.SendMessage(0, 1, 2);

        cde.Wait();
    }

    [TestMethod]
    public async Task TestDispatch5()
    {
        var errorMessage = $"ERROR{Guid.NewGuid()}";

        await using var factory = new WindowManagerFactory(CreateConfiguration(), _loggerFactory);
        await using var manager = await factory.StartAsync();

        var window = manager.CreateWindow("window", async (sender, message) => {
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
        });

        var result = window.SendMessage(0, 1, 2);

        await Task.Delay(1000);
    }

    [TestMethod]
    public async Task TestCreateWindowFromOtherThread()
    {
        await using var factory = new WindowManagerFactory(CreateConfiguration(), _loggerFactory);
        await using var managerA = await factory.StartAsync();
        await using var managerB = await factory.StartAsync();

        var windowA = managerA.CreateWindow("windowA", (sender, message) => {
            _logger.LogInformation($"PARENT on message {message}");
        });

        var buttonA = managerA.CreateChildWindow(windowA, "BUTTON", "buttonA", (sender, message) => {
            _logger.LogInformation($"CHILD A on message {message}");
        });

        var windowAA = managerA.CreateWindow(windowA, "windowAA", (sender, message) => {
            _logger.LogInformation($"CHILD W on message {message}");
        });

        var buttonB = managerB.CreateChildWindow(windowA, "BUTTON", "buttonB", (sender, message) => {
            _logger.LogInformation($"CHILD B on message {message}");
        });

        await Task.Delay(1000);
    }


}
