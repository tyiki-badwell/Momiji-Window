using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Momiji.Core.Window;

namespace MomijiTest.Core.Window.UnitTest;

[TestClass]
public partial class DispatcherQueueSynchronizationContextUnitTest
{
    public DispatcherQueueSynchronizationContextUnitTest()
    {
    }

    private static IHost Setup()
    {
        var builder = Host.CreateDefaultBuilder(default);

        builder.UseContentRoot(AppContext.BaseDirectory);
        builder.UseConsoleLifetime();
        builder.ConfigureServices((hostContext, services) =>
        {
            services.AddSingleton<IUIThreadActivator, UIThreadActivator>();
            services.AddSingleton<IDispatcherQueue, DispatcherQueue>();
        });

        return builder.Build();
    }

    [TestMethod]
    public async Task TestConstructor()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var dispatcherQueue = host.Services.GetRequiredService<IDispatcherQueue>();

        var context = new DispatcherQueueSynchronizationContext(
            loggerFactory.CreateLogger<DispatcherQueueSynchronizationContext>(),
            dispatcherQueue
        );

        using var switchContext = new DispatcherQueueSynchronizationContextScope(context, loggerFactory.CreateLogger<DispatcherQueueSynchronizationContext>());

        context.OperationStarted();
        context.OperationCompleted();
    }

    [TestMethod]
    public async Task TestSend()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var dispatcherQueue = host.Services.GetRequiredService<IDispatcherQueue>();

        var context = new DispatcherQueueSynchronizationContext(
            loggerFactory.CreateLogger<DispatcherQueueSynchronizationContext>(),
            dispatcherQueue
        );

        try
        {
            context.Send(state =>
            {
            }, "TestState");

            Assert.Fail();
        }
        catch (NotSupportedException)
        {
            //OK
        }
    }

    [TestMethod]
    public async Task TestPostNoActive()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var dispatcherQueue = host.Services.GetRequiredService<IDispatcherQueue>();

        var context = new DispatcherQueueSynchronizationContext(
            loggerFactory.CreateLogger<DispatcherQueueSynchronizationContext>(),
            dispatcherQueue
        );

        try
        {
            context.Post(state =>
            {
            }, "TestState");

            Assert.Fail();
        }
        catch (InvalidOperationException)
        {
            //OK
        }
    }

    [TestMethod]
    public async Task TestPostActive()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var dispatcherQueue = host.Services.GetRequiredService<IDispatcherQueue>();
        var activator = host.Services.GetRequiredService<IUIThreadActivator>();

        var context = new DispatcherQueueSynchronizationContext(
            loggerFactory.CreateLogger<DispatcherQueueSynchronizationContext>(),
            dispatcherQueue
        );

        using var token = activator.Activate();

        context.Post(state =>
        {
        }, "TestState");
    }

    [TestMethod]
    public async Task TestCreateCopy()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var dispatcherQueue = host.Services.GetRequiredService<IDispatcherQueue>();

        var context = new DispatcherQueueSynchronizationContext(
            loggerFactory.CreateLogger<DispatcherQueueSynchronizationContext>(),
            dispatcherQueue
        );

        context.CreateCopy();
    }

}
