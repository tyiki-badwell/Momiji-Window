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
            await using var manager = new WindowManager(CreateConfiguration(0x9999), _loggerFactory);
            Assert.Fail("エラーが発生しなかった");
        }
        catch (WindowException)
        {
            //OK
        }
    }

    [TestMethod]
    public async Task TestCreateWindow()
    {
        using var tokenSource = new CancellationTokenSource();

        await using var manager = new WindowManager(CreateConfiguration(), _loggerFactory);
        var task = manager.StartAsync(tokenSource.Token);

        var window = manager.CreateWindow("window");
        window.Move(0, 0, 100, 100, true);
        window.Show(1);
        window.Move(100, 100, 100, 100, true);
        window.Move(200, 200, 200, 200, true);
        window.Show(0);

        window.Close();

        try
        {
            window.Show(1);
            Assert.Fail("エラーが起きなかった");
        }
        catch (Exception e)
        {
            _logger.LogInformation(e, "show failed.");
        }

        tokenSource.Cancel();
        await task;
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task TestOnMassage(bool close)
    {
        using var tokenSource = new CancellationTokenSource();

        await using var manager = new WindowManager(CreateConfiguration(), _loggerFactory);
        var task = manager.StartAsync(tokenSource.Token);

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
        window.Show(1);

        //canClose:false
        window.Close();

        if (close)
        {
            canClose = true;
            window.Close();
        }

        tokenSource.Cancel();
        await task;
    }

    [TestMethod]
    public async Task TestCreateChildWindow()
    {
        using var tokenSource = new CancellationTokenSource();

        await using var manager = new WindowManager(CreateConfiguration(), _loggerFactory);
        var task = manager.StartAsync(tokenSource.Token);

        var window = manager.CreateWindow("window", (sender, message) => {
            _logger.LogInformation($"on message {message}");

            if (message.Msg == 0x0001) //WM_CREATE
            {
                var child = manager.CreateChildWindow(sender, "EDIT", "child", (sender, message) => {
                    _logger.LogInformation($"child on message {message}");
                });
            }
        });

        tokenSource.Cancel();
        await task;
    }

    [TestMethod]
    public async Task TestCreateChildWindowFail()
    {
        using var tokenSource = new CancellationTokenSource();

        await using var manager = new WindowManager(CreateConfiguration(), _loggerFactory);
        var task = manager.StartAsync(tokenSource.Token);

        try
        {
            var window = manager.CreateWindow("window", (sender, message) =>
            {
                _logger.LogInformation($"on message {message}");

                if (message.Msg == 0x0001) //WM_CREATE
                {
                    var child = manager.CreateChildWindow(sender, "EDITXXX", "child", (sender, message) =>
                    {
                        _logger.LogInformation($"child on message {message}");
                    });
                }
            });

            Assert.Fail("エラーが発生しなかった");
        }
        catch (AggregateException e)
        {
            //OK
            e.Handle((predicate) => {
                _logger.LogInformation(e, "error occurred");
                return true;
            });
        }

        tokenSource.Cancel();
        await task;
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
        using var tokenSource = new CancellationTokenSource();

        await using var manager = new WindowManager(CreateConfiguration(), _loggerFactory);
        var task = manager.StartAsync(tokenSource.Token);

        nint method(IWindow window, nint wParam, nint lParam)
        {
            var result = nint.Zero;
            _logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] call start ===============================");
            switch (methodType)
            {
                case MethodType.Send:
                    result = window.SendMessage(0, wParam, lParam);
                    break;

                case MethodType.Post:
                    window.PostMessage(0, wParam, lParam);
                    break;
            }
            _logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] call end ===============================");
            return result;
        };

        async ValueTask<nint> call(CallType callType, IWindow window, nint wParam, nint lParam)
        {
            var result = nint.Zero;

            switch (callType)
            {
                case CallType.Direct:
                    result = method(window, wParam, lParam);
                    break;

                case CallType.InDispatch:
                    result = await window.DispatchAsync((window) => method(window, wParam, lParam));
                    break;

                case CallType.InTask:
                    async ValueTask<nint> a()
                    {
                        var result = method(window, wParam, lParam);
                        await Task.Delay(0);
                        return result;
                    };
                    result = await a();
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
                    _logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] REENTRANT on message [{message}] [cde:{cde.CurrentCount}]");
                }
                else
                {
                    _logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] on message [{message}] [cde:{cde.CurrentCount}]");
                    var result = await call(inMessage, sender, message.WParam * 10, 999);
                    cde.Signal();
                    _logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] result [{result}] [cde:{cde.CurrentCount}]");
                }

                message.Handled = true;
                message.Result = message.LParam;
            }
        });

        {
            var result = await call(start, window, 1, 2);
            _logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] result [{result}] [cde:{cde.CurrentCount}]");
        }

        _logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] cde wait start [cde:{cde.CurrentCount}] ===============================");
        cde.Wait();
        _logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] cde wait end [cde:{cde.CurrentCount}] ===============================");

        tokenSource.Cancel();
        _logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] manager await start ===============================");
        await task;
        _logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] manager await end ===============================");
    }

    [TestMethod]
    public async Task TestSetWindowStyle()
    {
        using var tokenSource = new CancellationTokenSource();

        await using var manager = new WindowManager(CreateConfiguration(), _loggerFactory);
        var task = manager.StartAsync(tokenSource.Token);

        var window = manager.CreateWindow("window");

        window.SetWindowStyle(0);

        {
            var result = await window.DispatchAsync((window) => {
                //immidiate mode
                return window.SetWindowStyle(0);
            });
            Assert.IsTrue(result);
        }

        tokenSource.Cancel();
        await task;
    }

    [TestMethod]
    public async Task TestDispatch()
    {
        using var tokenSource = new CancellationTokenSource();

        await using var manager = new WindowManager(CreateConfiguration(), _loggerFactory);
        var task = manager.StartAsync(tokenSource.Token);

        var window = manager.CreateWindow("window");

        {
            _logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] Dispatch start ===============================");
            var result = await window.DispatchAsync((window) => { return 999; });
            _logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] Dispatch end ===============================");
            Assert.AreEqual(999, result);
        }

        {
            _logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] Dispatch start ===============================");
            var result = await await window.DispatchAsync(async (window) => {
                _logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] Dispatch 2 start ===============================");
                var result = await window.DispatchAsync((window) =>
                { //re-entrant
                    return 888;
                });
                _logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] Dispatch 2 end ===============================");
                return result;
            });
            _logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] Dispatch end ===============================");
            Assert.AreEqual(888, result);
        }

        tokenSource.Cancel();
        await task;
    }


    [TestMethod]
    public async Task TestCreateWindowFromOtherThread()
    {
        using var tokenSource = new CancellationTokenSource();

        await using var managerA = new WindowManager(CreateConfiguration(), _loggerFactory);
        var taskA = managerA.StartAsync(tokenSource.Token);

        await using var managerB = new WindowManager(CreateConfiguration(), _loggerFactory);
        var taskB = managerB.StartAsync(tokenSource.Token);


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

        tokenSource.Cancel();
        await taskA;
        await taskB;
    }


}
