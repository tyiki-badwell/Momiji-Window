using System.ComponentModel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using User32 = Momiji.Interop.User32.NativeMethods;

namespace Momiji.Core.Window;

[TestClass]
public class WindowProcedureTest : IDisposable
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;

    public WindowProcedureTest()
    {
        var configuration = CreateConfiguration();

        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConfiguration(configuration);

            builder.AddFilter("Momiji", LogLevel.Warning);
            builder.AddFilter("Momiji.Core.Window", LogLevel.Trace);
            builder.AddFilter("Momiji.Core.Window.WindowProcedureTest", LogLevel.Trace);
            builder.AddFilter("Microsoft", LogLevel.Warning);
            builder.AddFilter("System", LogLevel.Warning);

            builder.AddConsole();
            builder.AddDebug();
        });

        _logger = _loggerFactory.CreateLogger<WindowProcedureTest>();
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

    private class TestException(string message) : Exception(message)
    {
    }

    public enum MethodType
    {
        Send,
        Post
    }

    [TestMethod]
    [DataRow(false, MethodType.Send)]
    [DataRow(true, MethodType.Send)]
    [DataRow(false, MethodType.Post)]
    [DataRow(true, MethodType.Post)]
    public async Task TestWindowProcedure(
        bool error,
        MethodType methodType
    )
    {
        WindowProcedure? proc_ = default;
        User32.HWND hwnd_ = default;

        var startupTcs = new TaskCompletionSource();
        var mainTcs = new TaskCompletionSource();

        using var threadMessageEvent = new AutoResetEvent(false);
        using var windowMessageEvent = new AutoResetEvent(false);

        using var cts = new CancellationTokenSource();

        _logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] task run");
        var factory = new TaskFactory();
        var main = factory.StartNew(() =>
        {
            _logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] task start");
            using var proc = new WindowProcedure(
                _loggerFactory, 
                (hwnd, message) => {
                    _logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] OnMessage {hwnd:X} {message}");

                    if (message.Msg == 0)
                    {
                        Thread.Sleep(200);

                        try
                        {
                            if (error)
                            {
                                throw new TestException($"{message}");
                            }
                        }
                        finally
                        {
                            windowMessageEvent.Set();
                        }
                    }
                },
                (message) => {
                    _logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] OnThreadMessage {message}");

                    if (message.Msg == 0)
                    {
                        Thread.Sleep(200);

                        try
                        {
                            if (error)
                            {
                                throw new TestException($"{message}");
                            }
                        }
                        finally
                        {
                            threadMessageEvent.Set();
                        }
                    }
                }
            );

            using var classManager = new WindowClassManager(_loggerFactory, proc.FunctionPointer);
            var windowClass = classManager.QueryWindowClass(string.Empty, 0);

            {
                hwnd_ = User32.CreateWindowExW(
                    0,
                    windowClass.ClassName,
                    nint.Zero,
                    0,
                    0,
                    0,
                    0,
                    0,
                    User32.HWND.None,
                    nint.Zero,
                    nint.Zero,
                    nint.Zero
                );
                var e = new Win32Exception();
                _logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] CreateWindowExW {hwnd_:X} {e}");
            }

            proc_ = proc;

            try
            {
                //self thread message
                if (methodType == MethodType.Post)
                {
                    proc.PostMessage(User32.HWND.None, 0, 1, 2);
                }
                else if (methodType == MethodType.Send)
                {
                    //proc.SendMessage(User32.HWND.None, 0, 1, 2);
                    threadMessageEvent.Set();
                }
                else
                {
                    Assert.Fail();
                }

                //window message
                if (methodType == MethodType.Post)
                {
                    proc.PostMessage(hwnd_, 0, 10, 20);
                }
                else if (methodType == MethodType.Send)
                {
                    //direct call
                    proc.SendMessage(hwnd_, 0, 10, 20);
                }
                else
                {
                    Assert.Fail();
                }

                _logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] DispatchMessage 1 start");
                proc.DispatchMessage();
                _logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] DispatchMessage 1 end");

                threadMessageEvent.WaitOne();
                _logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] threadMessageEvent WaitOne end");
                windowMessageEvent.WaitOne();
                _logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] windowMessageEvent WaitOne end");

                Exception? exception = default;

                try
                {
                    _logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] ThrowIfOccurredInWndProc 1 start");
                    proc.ThrowIfOccurredInWndProc();
                    _logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] ThrowIfOccurredInWndProc 1 end");
                }
                catch (Exception e)
                {
                    _logger.LogInformation(e, $"★thread:[{Environment.CurrentManagedThreadId:X}] ThrowIfOccurredInWndProc 1 throw");
                    exception = e;
                }

                if (!error && (exception != default))
                {
                    Assert.Fail();
                }

                if (error && (exception == default))
                {
                    Assert.Fail();
                }
            }
            catch (Exception e)
            {
                _logger.LogInformation(e, $"★thread:[{Environment.CurrentManagedThreadId:X}] task error");
                startupTcs.SetException(e);
                mainTcs.SetException(e);
                throw;
            }
            finally
            {
                startupTcs.SetResult();
            }
            _logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] start up OK");

            try
            {
                while (true)
                {
                    if (cts.IsCancellationRequested)
                    {
                        _logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] cancelled");
                        break;
                    }

                    _logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] DispatchMessage 2 start");
                    proc.DispatchMessage();
                    _logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] DispatchMessage 2 end");

                    Thread.Sleep(200);
                }
            }
            catch (Exception e)
            {
                _logger.LogInformation(e, $"★thread:[{Environment.CurrentManagedThreadId:X}] task error");
                mainTcs.SetException(e);
            }
            finally
            {
                mainTcs.SetResult();
            }

            User32.DestroyWindow(hwnd_);

        }, TaskCreationOptions.RunContinuationsAsynchronously);

        _logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] task await start");
        await startupTcs.Task;

        await factory.StartNew(() =>
        {
            Assert.IsNotNull(proc_);
            try
            {
                //作成したスレッド以外からは呼べない
                _logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] DispatchMessage start");
                proc_.DispatchMessage();
                Assert.Fail($"★thread:[{Environment.CurrentManagedThreadId:X}] DispatchMessage end");
            }
            catch (InvalidOperationException e)
            {
                //OK
                _logger.LogInformation(e, $"★thread:[{Environment.CurrentManagedThreadId:X}] DispatchMessage error occurred");
            }
        });

        _logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] task await end");

        Assert.IsNotNull(proc_);

        //thread message
        if (methodType == MethodType.Post)
        {
            proc_.PostMessage(User32.HWND.None, 0, 100, 200);
        }
        else if (methodType == MethodType.Send)
        {
            //proc_.SendMessage(User32.HWND.None, 0, 100, 200);
            threadMessageEvent.Set();
        }
        else
        {
            Assert.Fail();
        }

        //window message
        if (methodType == MethodType.Post)
        {
            proc_.PostMessage(hwnd_, 0, 1000, 2000);
        }
        else if (methodType == MethodType.Send)
        {
            proc_.SendMessage(hwnd_, 0, 1000, 2000);
        }
        else
        {
            Assert.Fail();
        }

        threadMessageEvent.WaitOne();
        _logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] threadMessageEvent WaitOne end");
        windowMessageEvent.WaitOne();
        _logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] windowMessageEvent WaitOne end");

        Exception? exception = default;
        try
        {
            _logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] ThrowIfOccurredInWndProc 1 start");
            proc_.ThrowIfOccurredInWndProc();
            _logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] ThrowIfOccurredInWndProc 1 end");
        }
        catch (Exception e)
        {
            _logger.LogInformation(e, $"★thread:[{Environment.CurrentManagedThreadId:X}] ThrowIfOccurredInWndProc 1 throw");
            exception = e;
        }

        cts.Cancel();

        if (!error && (exception != default))
        {
            Assert.Fail();
        }
        if (error && (exception == default))
        {
            Assert.Fail();
        }

        _logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] mainTcs await start");
        await mainTcs.Task;
        _logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] mainTcs await end");

        _logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] main await start");
        await main;
        _logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] main await end");

        try
        {
            //破棄後は呼べない
            if (methodType == MethodType.Post)
            {
                proc_.PostMessage(User32.HWND.None, 0, 10000, 20000);
            }
            else if (methodType == MethodType.Send)
            {
                proc_.SendMessage(User32.HWND.None, 0, 10000, 20000);
            }

            Assert.Fail();
        }
        catch (ObjectDisposedException e)
        {
            //OK
            _logger.LogInformation(e, $"★thread:[{Environment.CurrentManagedThreadId:X}] [{methodType}] error occurred");
        }
    }

}
