using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Momiji.Core.Window;

[TestClass]
public class UIThreadActivatorTest : IDisposable
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;

    public UIThreadActivatorTest()
    {
        var configuration = CreateConfiguration();

        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConfiguration(configuration);

            builder.AddFilter("Momiji", LogLevel.Warning);
            builder.AddFilter("Momiji.Core.Window", LogLevel.Trace);
            builder.AddFilter("Momiji.Core.Window.UIThreadActivatorTest", LogLevel.Trace);
            builder.AddFilter("Microsoft", LogLevel.Warning);
            builder.AddFilter("System", LogLevel.Warning);

            builder.AddConsole();
            builder.AddDebug();
        });

        _logger = _loggerFactory.CreateLogger<UIThreadActivatorTest>();
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
    public void TestActivate()
    {
        var uiThreadActivator = new UIThreadActivator(_loggerFactory);

        Assert.IsFalse(uiThreadActivator.IsActivatedThread);

        try
        {
            uiThreadActivator.ThrowIfNoActive();
            Assert.Fail();
        }
        catch (InvalidOperationException e)
        {
            //OK
            _logger.LogInformation(e, "error occurred");
        }

        try
        {
            uiThreadActivator.ThrowIfCalledFromOtherThread();
            Assert.Fail();
        }
        catch (InvalidOperationException e)
        {
            //OK
            _logger.LogInformation(e, "error occurred");
        }

        using var active1 = uiThreadActivator.Activate();

        Assert.IsTrue(uiThreadActivator.IsActivatedThread);
        uiThreadActivator.ThrowIfNoActive();
        uiThreadActivator.ThrowIfCalledFromOtherThread();

        try
        {
            using var active2 = uiThreadActivator.Activate();
            Assert.Fail();
        }
        catch (InvalidOperationException e)
        {
            //OK
            _logger.LogInformation(e, "error occurred");
        }
    }



}
