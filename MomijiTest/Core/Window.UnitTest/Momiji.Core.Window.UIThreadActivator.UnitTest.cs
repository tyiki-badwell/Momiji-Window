using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MomijiTest.Core.Window.UnitTest;

[TestClass]
public partial class UIThreadActivatorUnitTest
{
    public UIThreadActivatorUnitTest()
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
        });

        return builder.Build();
    }

    [TestMethod]
    public async Task TestConstructor()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();

        using var activator = new UIThreadActivator(
            loggerFactory
        );
    }

    [TestMethod]
    public async Task TestFinalizer()
    {
        {
            using var host = Setup();
            var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();

            void Run()
            {
                var _ = new UIThreadActivator(
                    loggerFactory
                );
            }

            Run();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    [TestMethod]
    public async Task TestDoubleDestruct()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();

        var activator = new UIThreadActivator(
            loggerFactory
        );
        activator.Dispose();
        activator.Dispose();
    }

    [TestMethod]
    public async Task TesActivate()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger<UIThreadActivatorUnitTest>();

        using var activator = new UIThreadActivator(
            loggerFactory
        );

        Assert.IsFalse(activator.IsActivatedThread);

        try
        {
            activator.ThrowIfNoActive();
            Assert.Fail();

        }
        catch (InvalidOperationException)
        {
            //OK
        }

        {
            logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] Activate start");
            using var token = activator.Activate();
            logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] Activate end");

            Assert.IsTrue(activator.IsActivatedThread);
            activator.ThrowIfNoActive();
            activator.ThrowIfCalledFromOtherThread();

            await Task.Run(() =>
            {
                Task.Delay(500);
                logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] IsActivatedThread start");
                Assert.IsFalse(activator.IsActivatedThread);
                logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] IsActivatedThread end");
                try
                {
                    activator.ThrowIfCalledFromOtherThread();
                    Assert.Fail();
                }
                catch (InvalidOperationException)
                {
                    //OK
                }
            });
        }

        Assert.IsFalse(activator.IsActivatedThread);
        try
        {
            activator.ThrowIfNoActive();
            Assert.Fail();
        }
        catch (InvalidOperationException)
        {
            //OK
        }
    }

    [TestMethod]
    public async Task TesDoubleActivate()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();

        using var activator = new UIThreadActivator(
            loggerFactory
        );

        {
            using var token = activator.Activate();

            try
            {
                using var token2 = activator.Activate();
                Assert.Fail();
            }
            catch (InvalidOperationException)
            {
                //OK
            }
        }
    }


    [TestMethod]
    public async Task TestTokenFinalizer()
    {
        {
            using var host = Setup();
            var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
            using var activator = new UIThreadActivator(
                loggerFactory
            );

            void Run()
            {
                var _ = activator.Activate();
            }

            Run();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    [TestMethod]
    public async Task TestTokenDoubleDestruct()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        using var activator = new UIThreadActivator(
            loggerFactory
        );

        var token = activator.Activate();
        token.Dispose();
        token.Dispose();
    }

    [TestMethod]
    public async Task TestOnInactivated()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        using var activator = new UIThreadActivator(
            loggerFactory
        );

        using var cde = new CountdownEvent(1);

        activator.OnInactivated += () =>
        {
            cde.Signal();
        };

        {
            using var token = activator.Activate();
        }

        if (!cde.Wait(5000))
        {
            Assert.Fail("Timeout waiting for inactivation");
        }
    }

}
