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

        var window = manager.CreateWindow();
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

        var window = manager.CreateWindow((IWindow sender, int msg, nint wParam, nint lParam, out bool handled) => {
            handled = false;

            switch (msg)
            {
                case 0x0010://WM_CLOSE
                    _logger.LogInformation($"WM_CLOSE canClose {canClose}");
                    if (!canClose)
                    {
                        handled = true;
                    }

                    break;
            }

            return 0;
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

        var window = manager.CreateWindow((IWindow sender, int msg, nint wParam, nint lParam, out bool handled) => {
            handled = false;
            _logger.LogInformation($"on message {msg:X} {wParam:X} {lParam:X}");

            if (msg == 0x0001) //WM_CREATE
            {
                var child = manager.CreateChildWindow(sender, "EDIT", (IWindow sender, int msg, nint wParam, nint lParam, out bool handled) => {
                    handled = false;
                    _logger.LogInformation($"child on message {msg:X} {wParam:X} {lParam:X}");
                    return 0;
                });
            }

            return 0;
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
            var window = manager.CreateWindow((IWindow sender, int msg, nint wParam, nint lParam, out bool handled) =>
            {
                handled = false;
                _logger.LogInformation($"on message {msg:X} {wParam:X} {lParam:X}");

                if (msg == 0x0001) //WM_CREATE
                {
                    var child = manager.CreateChildWindow(sender, "EDITXXX", (IWindow sender, int msg, nint wParam, nint lParam, out bool handled) =>
                    {
                        handled = false;
                        _logger.LogInformation($"child on message {msg:X} {wParam:X} {lParam:X}");
                        return 0;
                    });
                }

                return 0;
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

    [TestMethod]
    public async Task TestSendMessage()
    {
        using var tokenSource = new CancellationTokenSource();

        await using var manager = new WindowManager(CreateConfiguration(), _loggerFactory);
        var task = manager.StartAsync(tokenSource.Token);

        var window = manager.CreateWindow((IWindow sender, int msg, nint wParam, nint lParam, out bool handled) => {
            handled = false;
            if (msg == 0)
            {
                _logger.LogInformation($"on message {msg:X} {wParam:X} {lParam:X}");
                handled = true;
                return 2;
            }
            return 0;
        });

        {
            var result = window.SendMessage(0, 1, 2);
            //TODO ISMEX_SENDに返す値を指定できるようにする？
            _logger.LogInformation($"SendMessage result {result}");
        }

        {
            var result = await window.DispatchAsync(() => { return window.SendMessage(0, 3, 4); });
            _logger.LogInformation($"SendMessage result {result}");
        }

        {
            async Task<nint> a()
            {
                var result = window.SendMessage(0, 5, 6);
                await Task.Delay(0);
                return result;
            }
            var result = await a();
            _logger.LogInformation($"SendMessage result {result}");
        }

        tokenSource.Cancel();
        await task;
    }

    [TestMethod]
    public async Task TestPostMessage()
    {
        using var tokenSource = new CancellationTokenSource();

        await using var manager = new WindowManager(CreateConfiguration(), _loggerFactory);
        var task = manager.StartAsync(tokenSource.Token);

        var window = manager.CreateWindow((IWindow sender, int msg, nint wParam, nint lParam, out bool handled) => {
            handled = false;
            if (msg == 0)
            {
                _logger.LogInformation($"on message {msg:X} {wParam:X} {lParam:X}");
            }
            return 0;
        });

        {
            window.PostMessage(0, 1, 2);
        }

        {
            var result = await window.DispatchAsync(() => { window.PostMessage(0, 3, 4); return 0; });
        }

        {
            async Task a()
            {
                window.PostMessage(0, 5, 6);
                await Task.Delay(0);
            }
            await a();
        }

        tokenSource.Cancel();
        await task;
    }

    [TestMethod]
    public async Task TestSetWindowStyle()
    {
        using var tokenSource = new CancellationTokenSource();

        await using var manager = new WindowManager(CreateConfiguration(), _loggerFactory);
        var task = manager.StartAsync(tokenSource.Token);

        var window = manager.CreateWindow();

        window.SetWindowStyle(0);

        {
            var result = await window.DispatchAsync(() => {
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

        var window = manager.CreateWindow();

        {
            var result = await window.DispatchAsync(() => { return 999; });
            Assert.AreEqual(999, result);
        }

        {
            var result = await await window.DispatchAsync(async () => {
                return await window.DispatchAsync(() =>
                { //re-entrant
                    return 888;
                });
            });
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


        var windowA = managerA.CreateWindow((IWindow sender, int msg, nint wParam, nint lParam, out bool handled) => {
            handled = false;
            _logger.LogInformation($"PARENT on message {msg:X} {wParam:X} {lParam:X}");
            return 0;
        });

        var buttonA = managerA.CreateChildWindow(windowA, "BUTTON", (IWindow sender, int msg, nint wParam, nint lParam, out bool handled) => {
            handled = false;
            _logger.LogInformation($"CHILD A on message {msg:X} {wParam:X} {lParam:X}");
            return 0;
        });

        var windowAA = managerA.CreateWindow(windowA, (IWindow sender, int msg, nint wParam, nint lParam, out bool handled) => {
            handled = false;
            _logger.LogInformation($"CHILD W on message {msg:X} {wParam:X} {lParam:X}");
            return 0;
        });

        var buttonB = managerB.CreateChildWindow(windowA, "BUTTON", (IWindow sender, int msg, nint wParam, nint lParam, out bool handled) => {
            handled = false;
            _logger.LogInformation($"CHILD B on message {msg:X} {wParam:X} {lParam:X}");
            return 0;
        });

        await Task.Delay(1000);

        tokenSource.Cancel();
        await taskA;
        await taskB;
    }


}
