using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Momiji.Core.Window;
using Momiji.Internal.Window;
using User32 = Momiji.Interop.User32.NativeMethods;

namespace MomijiTest.Core.Window.UnitTest;

[TestClass]
public partial class WindowClassUnitTest
{
    public WindowClassUnitTest()
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
            services.AddSingleton<IOnUnhandledExceptionHandler, OnUnhandledExceptionHandler>();
            services.AddSingleton<IDispatcherQueue, DispatcherQueue>();
            services.AddSingleton<IDispatcherQueueSynchronizationContext, DispatcherQueueSynchronizationContext>();
            services.AddSingleton<IWindowProcedure, WindowProcedure>();
        });

        return builder.Build();
    }

    [TestMethod]
    public async Task TestConstructor()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var procedure = host.Services.GetRequiredService<IWindowProcedure>();

        using var clazz = new WindowClass(
            loggerFactory.CreateLogger<WindowClass>(),
            procedure
        );
    }

    [TestMethod]
    public async Task TestFinalizer()
    {
        {
            using var host = Setup();
            var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();

            void run()
            {
                var procedure = host.Services.GetRequiredService<IWindowProcedure>();

                var _ = new WindowClass(
                    loggerFactory.CreateLogger<WindowClass>(),
                    procedure
                );
            }

            run();
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
        var procedure = host.Services.GetRequiredService<IWindowProcedure>();

        var clazz = new WindowClass(
            loggerFactory.CreateLogger<WindowClass>(),
            procedure
        );
        clazz.Dispose();
        clazz.Dispose();
    }

    [TestMethod]
    public async Task TestInitializeNull()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var procedure = host.Services.GetRequiredService<IWindowProcedure>();

        using var clazz = new WindowClass(
            loggerFactory.CreateLogger<WindowClass>(),
            procedure
        );

        clazz.Initialize(User32.WNDCLASSEX.CS.NONE, "");

        var logger = loggerFactory.CreateLogger<WindowClassUnitTest>();

        logger.LogWithLine(LogLevel.Information, $"{clazz.ClassName} {clazz.HInstance}", Environment.CurrentManagedThreadId);

    }

    [TestMethod]
    public async Task TestInitializeButton()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var procedure = host.Services.GetRequiredService<IWindowProcedure>();

        using var clazz = new WindowClass(
            loggerFactory.CreateLogger<WindowClass>(),
            procedure
        );

        clazz.Initialize(User32.WNDCLASSEX.CS.PARENTDC | User32.WNDCLASSEX.CS.OWNDC, "Button");
    }

    [TestMethod]
    public async Task TestInitializeButton2()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var procedure = host.Services.GetRequiredService<IWindowProcedure>();

        using var clazz = new WindowClass(
            loggerFactory.CreateLogger<WindowClass>(),
            procedure
        );

        try
        {
            clazz.Initialize(User32.WNDCLASSEX.CS.OWNDC, "Button?");
            Assert.Fail();
        }
        catch (Exception)
        {
            //OK
        }
    }

    private sealed record class Message : IWindowManager.IMessage
    {
        public required int Msg
        {
            get; init;
        }
        public required nint WParam
        {
            get; init;
        }
        public required nint LParam
        {
            get; init;
        }

        private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;

        public int OwnerThreadId => _ownerThreadId;

        private nint _result;

        public nint Result
        {
            get => _result;
            set
            {
                if (OwnerThreadId != Environment.CurrentManagedThreadId)
                {
                    throw new InvalidOperationException("called from other thread");
                }

                _result = value;
            }
        }

        public bool Handled
        {
            get; set;
        }
        public override string ToString()
        {
            return $"[Msg:{Msg:X}][WParam:{WParam:X}][LParam:{LParam:X}][OwnerThreadId:{OwnerThreadId:X}][Result:{Result:X}][Handled:{Handled}]";
        }
    }

    [TestMethod]
    public async Task TestCallOriginalWIndowProc()
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var procedure = host.Services.GetRequiredService<IWindowProcedure>();

        using var clazz = new WindowClass(
            loggerFactory.CreateLogger<WindowClass>(),
            procedure
        );

        var hwnd = User32.HWND.None;
        var message = new Message()
        {
            Msg = 0,
            WParam = 0,
            LParam = 0
        };

        clazz.CallOriginalWindowProc(hwnd, message);

    }

}
