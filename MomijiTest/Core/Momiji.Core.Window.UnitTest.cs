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

    private static IConfiguration CreateConfiguration()
    {
        var configuration =
            new ConfigurationBuilder()
            .AddJsonFile("appsettings.json")
            .Build();

        return configuration;
    }

    [TestMethod]
    public async Task TestCreateWindow()
    {
        using var tokenSource = new CancellationTokenSource();

        await using var manager = new WindowManager(_loggerFactory);
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
            Assert.Fail("ƒGƒ‰[‚ª‹N‚«‚È‚©‚Á‚½");
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

        await using var manager = new WindowManager(_loggerFactory);
        var task = manager.StartAsync(tokenSource.Token);

        var canClose = false;

        var window = manager.CreateWindow(default, (int msg, nint wParam, nint lParam, out bool handled) => {
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

        await using var manager = new WindowManager(_loggerFactory);
        var task = manager.StartAsync(tokenSource.Token);

        var window = manager.CreateWindow(default, (int msg, nint wParam, nint lParam, out bool handled) => {
            handled = false;
            _logger.LogInformation($"on message {msg:X} {wParam:X} {lParam:X}");
            return 0;
        });

        {
            var child = manager.CreateWindow(window, (int msg, nint wParam, nint lParam, out bool handled) => {
                handled = false;
                _logger.LogInformation($"child on message {msg:X} {wParam:X} {lParam:X}");
                return 0;
            });
        }

        tokenSource.Cancel();
        await task;
    }

    [TestMethod]
    public async Task TestSendMessage()
    {
        using var tokenSource = new CancellationTokenSource();

        await using var manager = new WindowManager(_loggerFactory);
        var task = manager.StartAsync(tokenSource.Token);

        var window = manager.CreateWindow(default, (int msg, nint wParam, nint lParam, out bool handled) => {
            handled = false;
            if (msg == 0)
            {
                _logger.LogInformation($"on message {msg:X} {wParam:X} {lParam:X}");
            }
            return 0;
        });

        {
            var result = window.SendMessage(0, 1, 2);
        }

        {
            var result = window.Dispatch(() => { return window.SendMessage(0, 3, 4); });
        }

        {
            async Task a()
            {
                var result = window.SendMessage(0, 5, 6);
                await Task.Delay(0);
            }
            await a();
        }

        tokenSource.Cancel();
        await task;
    }

    [TestMethod]
    public async Task TestPostMessage()
    {
        using var tokenSource = new CancellationTokenSource();

        await using var manager = new WindowManager(_loggerFactory);
        var task = manager.StartAsync(tokenSource.Token);

        var window = manager.CreateWindow(default, (int msg, nint wParam, nint lParam, out bool handled) => {
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
            var result = window.Dispatch(() => { window.PostMessage(0, 3, 4); return 0; });
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

        await using var manager = new WindowManager(_loggerFactory);
        var task = manager.StartAsync(tokenSource.Token);

        var window = manager.CreateWindow();

        window.SetWindowStyle(0);

        {
            var result = window.Dispatch(() => {
                //immidiate mode
                return window.SetWindowStyle(0);
            });
            Assert.IsTrue(result);
        }

        tokenSource.Cancel();
        await task;
    }

    [TestMethod]
    public async Task TestDIspatch()
    {
        using var tokenSource = new CancellationTokenSource();

        await using var manager = new WindowManager(_loggerFactory);
        var task = manager.StartAsync(tokenSource.Token);

        var window = manager.CreateWindow();

        {
            var result = window.Dispatch(() => { return 999; });
            Assert.AreEqual(999, result);
        }

        {
            var result = window.Dispatch(() => {
                return window.Dispatch(() =>
                { //re-entrant
                    return 888;
                });
            });
            Assert.AreEqual(888, result);
        }

        tokenSource.Cancel();
        await task;
    }
}
