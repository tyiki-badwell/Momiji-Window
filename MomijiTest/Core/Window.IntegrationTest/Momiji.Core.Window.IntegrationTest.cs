using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Momiji.Core.Window;
using MomijiTest.Core.Window.UnitTest;

namespace MomijiTest.Core.Window.IntegrationTest;

[TestClass]
public partial class WindowIntegrationTest
{
    public WindowIntegrationTest()
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
    public async Task TestRegisterClassFail()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger<UIThreadFactoryTest>();
        var factory = host.Services.GetRequiredService<IServiceScopeFactory>();

        try
        {
            var threadFactory = host.Services.GetRequiredService<IUIThreadFactory>();
            var thread = await threadFactory.StartAsync();

            var window = await thread.CreateWindowAsync(new() { classStyle = 0x9999 });

            Assert.Fail("エラーが発生しなかった");
        }
        catch (Win32Exception e)
        {
            //OK
            logger.LogInformation(e, "error occurred");
        }
    }


    [TestMethod]
    public async Task TestCreateWindow()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger<UIThreadFactoryTest>();
        var factory = host.Services.GetRequiredService<IServiceScopeFactory>();
        var onUnhandledExceptionHandler = host.Services.GetRequiredService<IOnUnhandledExceptionHandler>();

        var threadFactory = host.Services.GetRequiredService<IUIThreadFactory>();
        var thread = await threadFactory.StartAsync();

        var window = await thread.CreateWindowAsync(new());
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
            logger.LogInformation(e, "show failed.");
        }
    }

    [TestMethod]
    public async Task TestCloseImmidiateCall()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger<UIThreadFactoryTest>();
        var factory = host.Services.GetRequiredService<IServiceScopeFactory>();
        var onUnhandledExceptionHandler = host.Services.GetRequiredService<IOnUnhandledExceptionHandler>();

        var threadFactory = host.Services.GetRequiredService<IUIThreadFactory>();
        var thread = await threadFactory.StartAsync();

        var window = await thread.CreateWindowAsync(new());

        await window.EnqueueAsync((window) => { window.Close(); return 0; });
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task TestOnMassage(bool close)
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger<UIThreadFactoryTest>();
        var factory = host.Services.GetRequiredService<IServiceScopeFactory>();
        var onUnhandledExceptionHandler = host.Services.GetRequiredService<IOnUnhandledExceptionHandler>();

        var threadFactory = host.Services.GetRequiredService<IUIThreadFactory>();
        var thread = await threadFactory.StartAsync();

        var canClose = false;

        var window = await thread.CreateWindowAsync(new()
        {
            onMessage = (sender, message) =>
            {
                switch (message.Msg)
                {
                    case 0x0010://WM_CLOSE
                        logger.LogInformation($"WM_CLOSE canClose {canClose}");
                        if (!canClose)
                        {
                            message.Handled = true;
                        }

                        break;
                }
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
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger<UIThreadFactoryTest>();
        var factory = host.Services.GetRequiredService<IServiceScopeFactory>();
        var onUnhandledExceptionHandler = host.Services.GetRequiredService<IOnUnhandledExceptionHandler>();

        var threadFactory = host.Services.GetRequiredService<IUIThreadFactory>();
        var thread = await threadFactory.StartAsync();

        var window = await thread.CreateWindowAsync(new()
        {
            onMessage = async (sender, message) =>
            {
                logger.LogInformation($"on message {message}");

                if (message.Msg == 0x0001) //WM_CREATE
                {
                    var child = await thread.CreateWindowAsync(new()
                    {
                        parent = sender,
                        style = 0x40000000, //WS_CHILD
                        className = "EDIT",
                        onMessage = (sender, message) =>
                        {
                            logger.LogInformation($"child on message {message}");
                        }
                    });
                }
            }
        });
    }

    [TestMethod]
    public async Task TestCreateChildWindowFail()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger<UIThreadFactoryTest>();
        var factory = host.Services.GetRequiredService<IServiceScopeFactory>();
        var onUnhandledExceptionHandler = host.Services.GetRequiredService<IOnUnhandledExceptionHandler>();

        var threadFactory = host.Services.GetRequiredService<IUIThreadFactory>();
        var thread = await threadFactory.StartAsync();

        try
        {
            var window = await thread.CreateWindowAsync(new()
            {
                onMessage = async (sender, message) =>
                {
                    logger.LogInformation($"on message {message}");

                    if (message.Msg == 0x0001) //WM_CREATE
                    {
                        try
                        {
                            //*** async でイベントを作っているとWM_CREATEでエラーにできない
                            var child = await thread.CreateWindowAsync(new()
                            {
                                parent = sender,
                                style = 0x40000000, //WS_CHILD
                                className = "EDITXXX",
                                onMessage = (sender, message) =>
                                {
                                    logger.LogInformation($"child on message {message}");
                                }
                            });
                        }
                        catch (Exception e)
                        {
                            logger.LogInformation(e, "ERROR");
                            throw;
                        }
                    }
                }
            });

            Assert.Fail("エラーが発生しなかった");
        }
        catch (Exception e)
        {
            //OK
            logger.LogInformation(e, "error occurred");
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
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger<UIThreadFactoryTest>();
        var factory = host.Services.GetRequiredService<IServiceScopeFactory>();

        Exception? errorStop = default;
        using var cde = new CountdownEvent(3);
        {
            var threadFactory = host.Services.GetRequiredService<IUIThreadFactory>();
            var onUnhandledExceptionHandler = host.Services.GetRequiredService<IOnUnhandledExceptionHandler>();
            onUnhandledExceptionHandler.OnUnhandledException += (sender, exception) =>
            {
                logger.LogInformation(exception, $"★thread:[{Environment.CurrentManagedThreadId:X}] on error [cde:{cde.CurrentCount}]===============================[{SynchronizationContext.Current}]");
                errorStop = exception;
                if (!cde.IsSet)
                {
                    cde.Signal();
                }
                logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] on error end [cde:{cde.CurrentCount}]===============================[{SynchronizationContext.Current}]");
                return true;
            };

            var thread = await threadFactory.StartAsync();

            nint method(IWindow window, nint wParam, nint lParam)
            {
                var result = nint.Zero;
                logger.LogInformation($"●thread:[{Environment.CurrentManagedThreadId:X}] call start ===============================[{SynchronizationContext.Current}]");
                switch (methodType)
                {
                    case MethodType.Send:
                        result = window.SendMessage(0, wParam, lParam);
                        break;

                    case MethodType.Post:
                        window.PostMessage(0, wParam, lParam);
                        break;
                }
                logger.LogInformation($"●thread:[{Environment.CurrentManagedThreadId:X}] call end ===============================[{SynchronizationContext.Current}]");
                return result;
            };

            async ValueTask<nint> call(CallType callType, IWindow window, nint wParam, nint lParam)
            {
                var result = nint.Zero;

                switch (callType)
                {
                    case CallType.Direct:
                        logger.LogInformation($"▼thread:[{Environment.CurrentManagedThreadId:X}] Direct start ===============================");
                        result = method(window, wParam, lParam);
                        logger.LogInformation($"▼thread:[{Environment.CurrentManagedThreadId:X}] Direct end ===============================");
                        break;

                    case CallType.InDispatch:
                        logger.LogInformation($"▼thread:[{Environment.CurrentManagedThreadId:X}] InDispatch start ===============================");
                        result = await window.EnqueueAsync((window) => method(window, wParam, lParam));
                        logger.LogInformation($"▼thread:[{Environment.CurrentManagedThreadId:X}] InDispatch end ===============================");
                        break;

                    case CallType.InTask:
                        async ValueTask<nint> a()
                        {
                            var result = method(window, wParam, lParam);
                            await Task.Delay(1);
                            return result;
                        };
                        logger.LogInformation($"▼thread:[{Environment.CurrentManagedThreadId:X}] InTask start ===============================");
                        result = await a();
                        logger.LogInformation($"▼thread:[{Environment.CurrentManagedThreadId:X}] InTask end ===============================");
                        break;
                }

                cde.Signal();
                logger.LogInformation($"■thread:[{Environment.CurrentManagedThreadId:X}] call end [cde:{cde.CurrentCount}]");
                return result;
            };

            var window = await thread.CreateWindowAsync(new()
            {
                onMessage = async (sender, message) =>
                {
                    if (message.Msg == 0)
                    {
                        if (message.LParam == 999)
                        {
                            logger.LogInformation($"■thread:[{Environment.CurrentManagedThreadId:X}] REENTRANT on message [{message}] [cde:{cde.CurrentCount}]");
                            if (error)
                            {
                                logger.LogInformation($"■thread:[{Environment.CurrentManagedThreadId:X}] throw ERROR on message [{message}] [cde:{cde.CurrentCount}]");
                                throw new Exception("！！！ERROR！！！");
                            }
                            cde.Signal();
                            logger.LogInformation($"■thread:[{Environment.CurrentManagedThreadId:X}] REENTRANT on message end [{message}] [cde:{cde.CurrentCount}]");
                        }
                        else
                        {
                            logger.LogInformation($"■thread:[{Environment.CurrentManagedThreadId:X}] on message [{message}] [cde:{cde.CurrentCount}]");

                            var result = await call(inMessage, sender, message.WParam * 10, 999);

                            logger.LogInformation($"■thread:[{Environment.CurrentManagedThreadId:X}] on message result [{result}] [cde:{cde.CurrentCount}]");
                        }

                        message.Handled = true;
                        message.Result = message.LParam;
                    }
                }
            });

            {
                var result = await call(start, window, 1, 2);
                logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] result [{result}] [cde:{cde.CurrentCount}]");
            }

            logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] cde wait start [cde:{cde.CurrentCount}] ===============================");
            if (!cde.Wait(5000))
            {
                Assert.Fail($"正常終了しなかった");
            }
            logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] cde wait end [cde:{cde.CurrentCount}] ===============================");
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
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger<UIThreadFactoryTest>();
        var factory = host.Services.GetRequiredService<IServiceScopeFactory>();
        var onUnhandledExceptionHandler = host.Services.GetRequiredService<IOnUnhandledExceptionHandler>();

        Exception? errorStop = default;
        using var cde = new CountdownEvent(1);
        {
            var threadFactory = host.Services.GetRequiredService<IUIThreadFactory>();
            var thread = await threadFactory.StartAsync();

            async ValueTask<int> method(int param)
            {
                if (param == 2)
                {
                    logger.LogInformation($"■thread:[{Environment.CurrentManagedThreadId:X}] REENTRANT on message [cde:{cde.CurrentCount}]");
                    if (error)
                    {
                        logger.LogInformation($"■thread:[{Environment.CurrentManagedThreadId:X}] throw ERROR on message [cde:{cde.CurrentCount}]");
                        throw new Exception("！！！ERROR！！！");
                    }
                    return 0;
                }
                else
                {
                    logger.LogInformation($"●thread:[{Environment.CurrentManagedThreadId:X}] call start ===============================[{SynchronizationContext.Current}]");
                    var result = await call(inMessage, param * 2);
                    logger.LogInformation($"●thread:[{Environment.CurrentManagedThreadId:X}] call end ===============================");
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
                        logger.LogInformation($"▼thread:[{Environment.CurrentManagedThreadId:X}] Direct start ===============================");
                        result = await method(param);
                        logger.LogInformation($"▼thread:[{Environment.CurrentManagedThreadId:X}] Direct end ===============================");
                        break;

                    case CallType.InDispatch:
                        logger.LogInformation($"▼thread:[{Environment.CurrentManagedThreadId:X}] InDispatch start ===============================");
                        result = await await thread.EnqueueAsync((manager) => method(param));
                        logger.LogInformation($"▼thread:[{Environment.CurrentManagedThreadId:X}] InDispatch end ===============================");
                        break;

                    case CallType.InTask:
                        async ValueTask<int> a()
                        {
                            var result = method(param);
                            await Task.Delay(1);
                            return await result;
                        };
                        logger.LogInformation($"▼thread:[{Environment.CurrentManagedThreadId:X}] InTask start ===============================");
                        result = await a();
                        logger.LogInformation($"▼thread:[{Environment.CurrentManagedThreadId:X}] InTask end ===============================");
                        break;

                }

                return result;
            };

            try
            {
                var result = await call(start, 1);
                logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] result [{result}] [cde:{cde.CurrentCount}]");
            }
            catch (Exception e)
            {
                logger.LogInformation(e, $"★thread:[{Environment.CurrentManagedThreadId:X}] exception  [cde:{cde.CurrentCount}]===============================[{SynchronizationContext.Current}]");

                errorStop = e;

                if (!cde.IsSet)
                {
                    cde.Signal();
                }
            }

            logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] cde wait start [cde:{cde.CurrentCount}] ===============================");
            if (!cde.Wait(5000))
            {
                Assert.Fail($"正常終了しなかった");
            }

            logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] cde wait end [cde:{cde.CurrentCount}] ===============================");
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
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger<UIThreadFactoryTest>();
        var factory = host.Services.GetRequiredService<IServiceScopeFactory>();
        var onUnhandledExceptionHandler = host.Services.GetRequiredService<IOnUnhandledExceptionHandler>();

        var threadFactory = host.Services.GetRequiredService<IUIThreadFactory>();
        var thread = await threadFactory.StartAsync();

        var window = await thread.CreateWindowAsync(new());

        await window.SetWindowStyleAsync(0);

        {
            var result = await await window.EnqueueAsync(async (window) => {
                //immidiate mode
                return await window.SetWindowStyleAsync(0);
            });
            Assert.IsTrue(result);
        }
    }

    [TestMethod]
    public async Task TestDispatch1()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger<UIThreadFactoryTest>();
        var factory = host.Services.GetRequiredService<IServiceScopeFactory>();
        var onUnhandledExceptionHandler = host.Services.GetRequiredService<IOnUnhandledExceptionHandler>();

        var threadFactory = host.Services.GetRequiredService<IUIThreadFactory>();
        var thread = await threadFactory.StartAsync();

        var window = await thread.CreateWindowAsync(new());

        {
            logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] Dispatch 1 put ===============================");
            var result = await window.EnqueueAsync((window) => {
                logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] Dispatch 1 run ===============================");
                return 999; 
            });
            logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] Dispatch 1 end ===============================");
            Assert.AreEqual(999, result);
        }
    }

    [TestMethod]
    public async Task TestDispatch2()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger<UIThreadFactoryTest>();
        var factory = host.Services.GetRequiredService<IServiceScopeFactory>();
        var onUnhandledExceptionHandler = host.Services.GetRequiredService<IOnUnhandledExceptionHandler>();

        var threadFactory = host.Services.GetRequiredService<IUIThreadFactory>();
        var thread = await threadFactory.StartAsync();

        var window = await thread.CreateWindowAsync(new());

        {
            logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] Dispatch 2 put ===============================");
            var result = await await window.EnqueueAsync(async (window) => {
                logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] Dispatch 2 run ===============================");
                logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] Dispatch 3 put ===============================");
                var before = Environment.CurrentManagedThreadId;

                var result = await await window.EnqueueAsync(async (window) =>
                { //re-entrant
                    logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] Dispatch 3 run ===============================");
                    var before = Environment.CurrentManagedThreadId;
                    await Task.Delay(1);
                    var after = Environment.CurrentManagedThreadId;
                    Assert.AreEqual(before, after);
                    logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] Dispatch 3 exit ===============================");
                    return 888;
                });
                logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] Dispatch 3 end [{result}] ===============================");

                var after = Environment.CurrentManagedThreadId;
                Assert.AreEqual(before, after);

                return result;
            });
            logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] Dispatch 2 end [{result}] ===============================");
            Assert.AreEqual(888, result);
        }
    }

    [TestMethod]
    public async Task TestDispatch3()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger<UIThreadFactoryTest>();
        var factory = host.Services.GetRequiredService<IServiceScopeFactory>();
        var onUnhandledExceptionHandler = host.Services.GetRequiredService<IOnUnhandledExceptionHandler>();

        var threadFactory = host.Services.GetRequiredService<IUIThreadFactory>();
        var thread = await threadFactory.StartAsync();

        var window = await thread.CreateWindowAsync(new());

        {
            logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] Dispatch 4 put ===============================");
            var result = await await window.EnqueueAsync(async (window) => {
                logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] Dispatch 4 run ===============================");
                await TestTool.RunThread(() => {
                    logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] sleep ===============================");
                    Thread.Sleep(1000);
                }).ContinueWith((task) => {
                    logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] continue ===============================");
                }); //デッドロックしない？
                logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] Dispatch 4 exit ===============================");
                return 999;
            });
            logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] Dispatch 4 end ===============================");
            Assert.AreEqual(999, result);
        }
    }

    [TestMethod]
    public async Task TestDispatch4()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger<UIThreadFactoryTest>();
        var factory = host.Services.GetRequiredService<IServiceScopeFactory>();
        var onUnhandledExceptionHandler = host.Services.GetRequiredService<IOnUnhandledExceptionHandler>();

        var threadFactory = host.Services.GetRequiredService<IUIThreadFactory>();
        var thread = await threadFactory.StartAsync();

        using var cde = new CountdownEvent(1);

        var window = await thread.CreateWindowAsync(new()
        {
            onMessage = async (sender, message) =>
            {
                if (message.Msg == 0)
                {
                    logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] message [{message}] ===============================");
                    var result = await await sender.EnqueueAsync(async (window) =>
                    {
                        logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] task run ===============================");

                        {
                            logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] dispatch 2 put ===============================");
                            var result2 = await sender.EnqueueAsync((window) =>
                            {
                                logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] dispatch 2 run ===============================");
                                return 2;
                            });
                            logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] dispatch 2 end[{result2}] ===============================");
                        }

                        var task = await TestTool.RunThread(async () =>
                        {
                            logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] sleep ===============================");
                            Thread.Sleep(1000);

                            logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] dispatch 3 put ===============================");
                            var result3 = await sender.EnqueueAsync((window) =>
                            {
                                logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] dispatch 3 run ===============================");
                                return 3;
                            });
                            logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] dispatch 3 end[{result3}] ===============================");

                            return result3;

                        }).ContinueWith(async (task) =>
                        {
                            logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] continue[{task.Result}] ===============================");
                            return await await task * 2;
                        });

                        //デッドロックしない
                        var result4 = await task.ConfigureAwait(true);
                        logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] task end[{result4}] ===============================");

                        return result4;
                    });
                    logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] dispatch end[{result}] ===============================");

                    cde.Signal();
                }
            }
        });

        var result = window.SendMessage(0, 1, 2);

        if (!cde.Wait(5000))
        {
            Assert.Fail($"正常終了しなかった");
        }
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task TestDispatch_Error(
        bool resolveError
    )
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger<UIThreadFactoryTest>();
        var factory = host.Services.GetRequiredService<IServiceScopeFactory>();

        var errorMessage = $"ERROR{Guid.NewGuid()}";
        using var cde = new CountdownEvent(1);

        var threadFactory = host.Services.GetRequiredService<IUIThreadFactory>();
        var onUnhandledExceptionHandler = host.Services.GetRequiredService<IOnUnhandledExceptionHandler>();
        onUnhandledExceptionHandler.OnUnhandledException += (sender, exception) =>
        {
            logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] on error ===============================[{SynchronizationContext.Current}]");

            if (exception.Message.Contains(errorMessage))
            {
                cde.Signal();
            }

            return resolveError;
        };

        var thread = await threadFactory.StartAsync();

        var window = await thread.CreateWindowAsync(new()
        {
            onMessage = async (sender, message) =>
            {
                if (message.Msg == 0)
                {
                    logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] message [{message}] ===============================[{SynchronizationContext.Current}]");

                    var task = await sender.EnqueueAsync(async (window) =>
                    {
                        logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] task run ===============================[{SynchronizationContext.Current}]");

                        var result4 = await await TestTool.RunThread(async () =>
                        {
                            logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] dispatch 3 put ===============================[{SynchronizationContext.Current}]");
                            var result3 = await sender.EnqueueAsync<int>((window) =>
                            {
                                throw new Exception(errorMessage);
                            });
                            logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] dispatch 3 end[{result3}] ===============================[{SynchronizationContext.Current}]");
                            return result3;

                        }).ContinueWith(async(task) =>
                        {
                            logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] continue[{task.Result}] ===============================[{SynchronizationContext.Current}]");
                            return await await task * 2;

                        }).ConfigureAwait(true);

                        logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] task end[{result4}] ===============================[{SynchronizationContext.Current}]");

                        return result4;
                    });

                    //AggregateExceptionがthrowされる
                    var result = await task;
                    logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] dispatch end[{result}] ===============================[{SynchronizationContext.Current}]");
                }
            }
        });

        var result = window.SendMessage(0, 1, 2);

        if (!cde.Wait(5000))
        {
            Assert.Fail($"{errorMessage} が発生しなかった");
        }
    }

    [TestMethod]
    public async Task TestCreateWindowFromOtherThread()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger<UIThreadFactoryTest>();
        var factory = host.Services.GetRequiredService<IServiceScopeFactory>();
        var onUnhandledExceptionHandler = host.Services.GetRequiredService<IOnUnhandledExceptionHandler>();

        var threadFactory = host.Services.GetRequiredService<IUIThreadFactory>();
        var threadA = await threadFactory.StartAsync();
        var threadB = await threadFactory.StartAsync();

        var windowA = await threadA.CreateWindowAsync(new()
        {
            windowTitle = "windowA",
            onMessage = (sender, message) =>
            {
                logger.LogInformation($"PARENT on message {message}");
            }
        });

        var buttonA = await threadA.CreateWindowAsync(new()
        {
            windowTitle = "buttonA",
            parent = windowA,
            style = 0x40000000, //WS_CHILD
            className = "BUTTON",
            onMessage = (sender, message) =>
            {
                logger.LogInformation($"CHILD A on message {message}");
            }
        });

        var windowAA = await threadA.CreateWindowAsync(new()
        {
            windowTitle = "windowAA",
            parent = windowA,
            style = 0x40000000, //WS_CHILD
            onMessage = (sender, message) =>
            {
                logger.LogInformation($"CHILD W on message {message}");
            }
        });

        var buttonB = await threadB.CreateWindowAsync(new()
        {
            windowTitle = "buttonB",
            parent = windowA,
            style = 0x40000000, //WS_CHILD
            className = "BUTTON",
            onMessage = (sender, message) =>
            {
                logger.LogInformation($"CHILD B on message {message}");
            }
        });

        await Task.Delay(1000);
    }


}
