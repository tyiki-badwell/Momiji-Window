using System.ComponentModel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using User32 = Momiji.Interop.User32.NativeMethods;

namespace Momiji.Core.Window;

[TestClass]
public class WindowClassTest : IDisposable
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;

    public WindowClassTest()
    {
        var configuration = CreateConfiguration();

        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConfiguration(configuration);

            builder.AddFilter("Momiji", LogLevel.Warning);
            builder.AddFilter("Momiji.Core.Window", LogLevel.Trace);
            builder.AddFilter("Momiji.Core.Window.WindowClassTest", LogLevel.Trace);
            builder.AddFilter("Microsoft", LogLevel.Warning);
            builder.AddFilter("System", LogLevel.Warning);

            builder.AddConsole();
            builder.AddDebug();
        });

        _logger = _loggerFactory.CreateLogger<WindowClassTest>();
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

    private sealed record class Message : IUIThread.IMessage
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
        var uiThreadActivator = new UIThreadActivator(_loggerFactory);

        using var proc = new WindowProcedure(
            _loggerFactory,
            uiThreadActivator,
            (hwnd, message) => {
                _logger.LogInformation($"Åöthread:[{Environment.CurrentManagedThreadId:X}] OnMessage {hwnd:X} {message}");
            },
            (message) => {
                _logger.LogInformation($"Åöthread:[{Environment.CurrentManagedThreadId:X}] OnThreadMessage {message}");
            }
        );

        WindowClass windowClass;

        try
        {
            windowClass = new WindowClass(
                _loggerFactory,
                proc,
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
            _logger.LogInformation(e, $"Åöthread:[{Environment.CurrentManagedThreadId:X}] error end");

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
            _logger.LogInformation($"[HInstance:{windowClass.HInstance}]");
            _logger.LogInformation($"[ClassName:{windowClass.ClassName}]");

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
