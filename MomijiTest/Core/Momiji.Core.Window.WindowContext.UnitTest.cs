using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Momiji.Core.Window;

[TestClass]
public class WindowContextTest : IDisposable
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;

    public WindowContextTest()
    {
        var configuration = CreateConfiguration();

        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConfiguration(configuration);

            builder.AddFilter("Momiji", LogLevel.Warning);
            builder.AddFilter("Momiji.Core.Window", LogLevel.Trace);
            builder.AddFilter("Momiji.Core.Window.WindowContextTest", LogLevel.Trace);
            builder.AddFilter("Microsoft", LogLevel.Warning);
            builder.AddFilter("System", LogLevel.Warning);

            builder.AddConsole();
            builder.AddDebug();
        });

        _logger = _loggerFactory.CreateLogger<WindowContextTest>();
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
    public async Task TestDispatchImmidiate()
    {
        var uiThreadActivator = new UIThreadActivator(_loggerFactory);
        using var context = new WindowContext(_loggerFactory, CreateConfiguration(), uiThreadActivator);
        using var active = uiThreadActivator.Activate();

        var result = await context.DispatchAsync((manager) => 1);
        Assert.AreEqual(1, result);
    }

    [TestMethod]
    public async Task TestDispatchAsyncFail1()
    {
        var uiThreadActivator = new UIThreadActivator(_loggerFactory);
        using var context = new WindowContext(_loggerFactory, CreateConfiguration(), uiThreadActivator);

        try
        {
            var result = await Task.Run(async () =>
            {
                return await context.DispatchAsync((manager) => 1);
            });
            Assert.Fail();
        }
        catch (InvalidOperationException e)
        {
            _logger.LogInformation(e, $"šthread:[{Environment.CurrentManagedThreadId:X}] error end");
        }
    }

    [TestMethod]
    public async Task TestDispatchAsyncFail2()
    {
        var uiThreadActivator = new UIThreadActivator(_loggerFactory);
        using var context = new WindowContext(_loggerFactory, CreateConfiguration(), uiThreadActivator);

        try
        {
            using var cts = new CancellationTokenSource();
            var tcs = new TaskCompletionSource(TaskCreationOptions.AttachedToParent | TaskCreationOptions.RunContinuationsAsynchronously);

            var task = Task.Run(() =>
            {
                using var active = uiThreadActivator.Activate();
                context.RunMessageLoop(tcs, cts.Token);
                _logger.LogInformation("loop end");
            });

            await tcs.Task;

            cts.Cancel();
            await task;

            var result = await Task.Run(async () =>
            {
                return await context.DispatchAsync((manager) => 1);
            });

            Assert.Fail();
        }
        catch (InvalidOperationException e)
        {
            _logger.LogInformation(e, $"šthread:[{Environment.CurrentManagedThreadId:X}] error end");
        }
    }

    [TestMethod]
    public async Task TestDispatchAsyncOK()
    {
        var uiThreadActivator = new UIThreadActivator(_loggerFactory);
        using var context = new WindowContext(_loggerFactory, CreateConfiguration(), uiThreadActivator);

        using var cts = new CancellationTokenSource();
        var tcs = new TaskCompletionSource(TaskCreationOptions.AttachedToParent | TaskCreationOptions.RunContinuationsAsynchronously);

        var task = Task.Run(() =>
        {
            using var active = uiThreadActivator.Activate();
            context.RunMessageLoop(tcs, cts.Token);
            _logger.LogInformation("loop end");
        });

        await tcs.Task;

        var result = await Task.Run(async () =>
        {
            return await context.DispatchAsync((manager) => 1);
        });

        Assert.AreEqual(result, 1);

        cts.Cancel();
        await task;
    }

    [TestMethod]
    public async Task TestMessageLoopForceEnd()
    {
        Task? task = null;

        {
            var uiThreadActivator = new UIThreadActivator(_loggerFactory);
            var context = new WindowContext(_loggerFactory, CreateConfiguration(), uiThreadActivator);

            var tcs = new TaskCompletionSource(TaskCreationOptions.AttachedToParent | TaskCreationOptions.RunContinuationsAsynchronously);

            task = Task.Run(() =>
            {
                context.RunMessageLoop(tcs, default);
                _logger.LogInformation("loop end");
            });

            await tcs.Task;
            context.Dispose();
        }

        if (task != default)
        {
            await task;
        }
    }
}
