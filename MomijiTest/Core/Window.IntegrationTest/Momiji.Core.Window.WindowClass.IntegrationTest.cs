using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Momiji.Core.Window;
using User32 = Momiji.Interop.User32.NativeMethods;

namespace MomijiTest.Core.Window.IntegrationTest;

[TestClass]
public partial class WindowClassIntegrationTest
{
    public WindowClassIntegrationTest()
    {
    }

    private static IHost Setup()
    {
        var builder = Host.CreateDefaultBuilder(default);

        builder.UseContentRoot(AppContext.BaseDirectory);
        builder.UseConsoleLifetime();
        builder.UseMomijiWindow();

        return builder.Build();
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
    [DataRow(0u, "")]
    [DataRow(0u, "button")]
    [DataRow(0u, "button?", true)]
    public void TestConstruct(
        uint classStyle,
        string className,
        bool error = false
    )
    {
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger<WindowClassIntegrationTest>();
        var proc = host.Services.GetRequiredService<IWindowProcedure>();

        using var clazz = new WindowClass(
            loggerFactory.CreateLogger<WindowClass>(),
            proc
        );


        proc.OnWindowMessage += (hwnd, message) =>
        {
            logger.LogInformation($"Åöthread:[{Environment.CurrentManagedThreadId:X}] OnWindowMessage {hwnd:X} {message}");
        };

        proc.OnThreadMessage += (message) =>
        {
            logger.LogInformation($"Åöthread:[{Environment.CurrentManagedThreadId:X}] OnThreadMessage {message}");
        };

        using var windowClass = new WindowClass(loggerFactory.CreateLogger<WindowClass>(), proc);

        try
        {
            windowClass.Initialize(
                (User32.WNDCLASSEX.CS)classStyle,
                className
            );
            if (error)
            {
                Assert.Fail();
            }
        }
        catch (Win32Exception e)
        {
            logger.LogInformation(e, $"Åöthread:[{Environment.CurrentManagedThreadId:X}] error end");

            if (error)
            {
                if (e.NativeErrorCode == 1411)
                {
                    return;
                }
                else
                {
                    throw;
                }
            }
            else
            {
                throw;
            }
        }

        try
        {
            logger.LogInformation($"[HInstance:{windowClass.HInstance}]");
            logger.LogInformation($"[ClassName:{windowClass.ClassName}]");

            var message = new Message()
            {
                Msg = 0,
                WParam = 0,
                LParam = 0
            };

            windowClass.CallOriginalWindowProc(User32.HWND.None, message);
        }
        finally
        {
            windowClass.Dispose();
        }
    }



}
