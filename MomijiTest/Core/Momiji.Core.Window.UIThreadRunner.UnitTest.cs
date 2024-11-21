using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Momiji.Core.Window;

[TestClass]
public partial class UIThreadRunnerTest : IDisposable
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;

    public UIThreadRunnerTest()
    {
        var configuration = CreateConfiguration();

        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConfiguration(configuration);

            builder.AddFilter("Momiji", LogLevel.Warning);
            builder.AddFilter("Momiji.Core.Window", LogLevel.Trace);
            builder.AddFilter("Momiji.Core.Window.UIThreadRunnerTest", LogLevel.Trace);
            builder.AddFilter("Microsoft", LogLevel.Warning);
            builder.AddFilter("System", LogLevel.Warning);

            builder.AddConsole();
            builder.AddDebug();
        });

        _logger = _loggerFactory.CreateLogger<UIThreadRunnerTest>();
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

    [TestMethod]
    public async Task TestConstruct()
    {
        using var uiThreadRunner = new UIThreadRunner(_loggerFactory);
        using var uiThread = await uiThreadRunner.StartAsync();

        var result = await uiThread.DispatchAsync((manager) => { return 999; });
        Assert.AreEqual(999, result);
    }

    [TestMethod]
    public async Task TestStartAsync()
    {
        using var mre = new ManualResetEventSlim(false);

        using var uiThreadRunner = new UIThreadRunner(_loggerFactory);
        uiThreadRunner.OnStop += (sender, e) => {
            _logger.LogInformation(e, "on stop");
            mre.Set();
        };

        try
        {
            using var uiThread = await uiThreadRunner.StartAsync();

            //２重起動はさせない
            using var uiThread2 = await uiThreadRunner.StartAsync();
            Assert.Fail();
        }
        catch (InvalidOperationException e)
        {
            //OK
            _logger.LogInformation(e, "error occurred");
        }

        _logger.LogInformation("mre wait");
        mre.Wait();
        _logger.LogInformation("mre end");


        try
        {
            //UIスレッドが終わっていれば、もう一度起動できる
            using var uiThread = await uiThreadRunner.StartAsync();
        }
        catch (InvalidOperationException e)
        {
            //OK
            _logger.LogInformation(e, "error occurred");
        }

    }


    [TestMethod]
    public async Task TestErrorOnStop()
    {
        using var uiThreadRunner = new UIThreadRunner(_loggerFactory);
        uiThreadRunner.OnStop += (sender, e) => {
            throw new Exception("ON STOP ERROR", e);
        };

        using var uiThread = await uiThreadRunner.StartAsync();

        try
        {
            //UIスレッドが耐える
            var result = await uiThread.DispatchAsync<int>((manager) => { throw new Exception("DISPATCH ERROR"); });
            Assert.Fail();
        }
        catch (Exception e)
        {
            _logger.LogInformation(e, "dispatch error occurred");
        }

        try
        {
            //UIスレッドが耐える
            var window = uiThread.CreateWindow(new()
            {
                onMessage = (sender, message) => {
                    throw new Exception($"ON MESSAGE ERROR {message}");
                }
            });
        }
        catch (Exception e)
        {
            _logger.LogInformation(e, "message error occurred");
        }
    }

    [TestMethod]
    public async Task TestAccessDisposedObject()
    {
        IUIThread? uiThread = default;
        {
            using var uiThreadRunner = new UIThreadRunner(_loggerFactory);
            uiThread = await uiThreadRunner.StartAsync();
        }

        try
        {
            await uiThread.DispatchAsync((manager) => {
                return 0;
            });
            Assert.Fail();
        }
        catch (Exception e)
        {
            _logger.LogInformation(e, "dispatch error occurred");
        }
    }

}
