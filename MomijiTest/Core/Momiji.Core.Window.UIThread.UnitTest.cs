using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Momiji.Core.Window;

[TestClass]
public class UIThreadTest : IDisposable
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;

    public UIThreadTest()
    {
        var configuration = CreateConfiguration();

        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConfiguration(configuration);

            builder.AddFilter("Momiji", LogLevel.Warning);
            builder.AddFilter("Momiji.Core.Window", LogLevel.Trace);
            builder.AddFilter("Momiji.Core.Window.UIThreadTest", LogLevel.Trace);
            builder.AddFilter("Microsoft", LogLevel.Warning);
            builder.AddFilter("System", LogLevel.Warning);

            builder.AddConsole();
            builder.AddDebug();
        });

        _logger = _loggerFactory.CreateLogger<UIThreadTest>();
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
        var tcs = new TaskCompletionSource(TaskCreationOptions.AttachedToParent | TaskCreationOptions.RunContinuationsAsynchronously);

        using var uiThread = new UIThread(
            CreateConfiguration(),
            _loggerFactory,
            tcs
        );

        await tcs.Task;
    }

    [TestMethod]
    public async Task TestConstructFail()
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.AttachedToParent | TaskCreationOptions.RunContinuationsAsynchronously);
        tcs.SetCanceled();

        using var uiThread = new UIThread(
            CreateConfiguration(),
            _loggerFactory,
            tcs
        );

        try
        {
            await tcs.Task;
            Assert.Fail();
        }
        catch (TaskCanceledException e)
        {
            //OK
            _logger.LogInformation(e, "error occurred");
        }

        try
        {
            await uiThread.DispatchAsync(() => { return 0; });
        }
        catch (InvalidOperationException e)
        {
            //OK
            _logger.LogInformation(e, "error occurred");
        }

        try
        {
            uiThread.CreateWindow("fail", default, "dummy", default, default);
        }
        catch (InvalidOperationException e)
        {
            //OK
            _logger.LogInformation(e, "error occurred");
        }


    }


}
