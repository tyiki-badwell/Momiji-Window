using System.Diagnostics.CodeAnalysis;
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

    private class DispatcherQueueDummy : IDispatcherQueue
    {
        public int Count => throw new NotImplementedException();

        public event IDispatcherQueue.EnqueueHandler? OnEnqueue;

        public void Dispose()
        {
        }
        public void Enqueue(SendOrPostCallback callback, object? param)
        {
        }
        public void Enqueue(Action action) => throw new NotImplementedException();
        public ValueTask<TResult> EnqueueAsync<TResult>(Func<TResult> func) => throw new NotImplementedException();
        public bool TryDequeue([MaybeNullWhen(false)] out object item) => throw new NotImplementedException();
    }

    private static IHost Setup()
    {
        var builder = Host.CreateDefaultBuilder(default);

        builder.UseContentRoot(AppContext.BaseDirectory);
        builder.UseConsoleLifetime();
        builder.ConfigureServices((hostContext, services) =>
        {
            services.AddSingleton<IDispatcherQueue, DispatcherQueueDummy>();
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
    public async Task TestPost()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var dispatcherQueue = host.Services.GetRequiredService<IDispatcherQueue>();

        var context = new DispatcherQueueSynchronizationContext(
            loggerFactory.CreateLogger<DispatcherQueueSynchronizationContext>(),
            dispatcherQueue
        );

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
