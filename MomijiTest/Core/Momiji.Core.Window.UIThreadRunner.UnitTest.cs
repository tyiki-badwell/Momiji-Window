using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Momiji.Core.Window;

[TestClass]
public class UIThreadRunnerTest : IDisposable
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
        var tcs = new TaskCompletionSource<IUIThread>(TaskCreationOptions.AttachedToParent | TaskCreationOptions.RunContinuationsAsynchronously);

        using var uiThreadRunner = new UIThreadRunner(
            _loggerFactory,
            tcs
        );

        using var uiThread = await tcs.Task;

        var result = await uiThread.DispatchAsync((manager) => { return 999; });
        Assert.AreEqual(999, result);
    }

    [TestMethod]
    public async Task TestConstructFail()
    {
        var tcs = new TaskCompletionSource<IUIThread>(TaskCreationOptions.AttachedToParent | TaskCreationOptions.RunContinuationsAsynchronously);
        tcs.SetCanceled();

        using var mre = new ManualResetEventSlim(false);

        using var uiThreadRunner = new UIThreadRunner(
            _loggerFactory,
            tcs,
            (e) => {
                _logger.LogInformation(e, "error occurred");
                mre.Set();
            }
        );

        try
        {
            using var uiThread = await tcs.Task;
            Assert.Fail();
        }
        catch (TaskCanceledException e)
        {
            //OK
            _logger.LogInformation(e, "error occurred");
        }

        mre.Wait();
        /*
        try
        {
            await uiThread.DispatchAsync((manager) => { return 0; });
            Assert.Fail();
        }
        catch (InvalidOperationException e)
        {
            //OK
            _logger.LogInformation(e, "error occurred");
        }
        */
    }


    [TestMethod]
    public async Task TestErrorOnStop()
    {
        var tcs = new TaskCompletionSource<IUIThread>(TaskCreationOptions.AttachedToParent | TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            using var uiThreadRunner = new UIThreadRunner(
                _loggerFactory,
                tcs,
                (e) =>
                {
                    throw new Exception("ERROR");
                },
                (e) =>
                {
                    throw new Exception("ERROR");
                }
            );

            using var uiThread = await tcs.Task;

            var result = await uiThread.DispatchAsync((manager) => { return 999; });
            Assert.AreEqual(999, result);
        }
        catch (Exception e)
        {
            //OK
            _logger.LogInformation(e, "error occurred");
        }
    }


}
