using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Momiji.Core.Window;

namespace MomijiTest.Core.Window.UnitTest;

[TestClass]
public partial class OnUnhandledExceptionHandlerUnitTest
{
    public OnUnhandledExceptionHandlerUnitTest()
    {
    }

    private static IHost Setup()
    {
        var builder = Host.CreateDefaultBuilder(default);

        builder.UseContentRoot(AppContext.BaseDirectory);
        builder.UseConsoleLifetime();
        builder.ConfigureServices((hostContext, services) =>
        {
            services.AddSingleton<IOnUnhandledExceptionHandler, OnUnhandledExceptionHandler>();
        });

        return builder.Build();
    }

    [TestMethod]
    public async Task TestConstructor()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();

        using var handler = new OnUnhandledExceptionHandler(
            loggerFactory.CreateLogger<OnUnhandledExceptionHandler>()
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
                var _ = new OnUnhandledExceptionHandler(
                    loggerFactory.CreateLogger<OnUnhandledExceptionHandler>()
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

        var handler = new OnUnhandledExceptionHandler(
            loggerFactory.CreateLogger<OnUnhandledExceptionHandler>()
        );
        handler.Dispose();
        handler.Dispose();
    }

    [TestMethod]
    public async Task TestDoHandleException()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();

        using var handler = new OnUnhandledExceptionHandler(
            loggerFactory.CreateLogger<OnUnhandledExceptionHandler>()
        );

        handler.DoUnhandledException(new Exception("test exception"));

        handler.OnUnhandledException += (sender, exception) =>
        {
            Assert.AreEqual("test exception2", exception.Message);
            return true;
        };

        handler.DoUnhandledException(new Exception("test exception2"));
    }
}
