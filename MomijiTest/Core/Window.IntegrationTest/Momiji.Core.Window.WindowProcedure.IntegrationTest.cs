using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Momiji.Core.Window;
using MomijiTest.Core.Window.UnitTest;
using User32 = Momiji.Interop.User32.NativeMethods;

namespace MomijiTest.Core.Window.IntegrationTest;

[TestClass]
public partial class WindowProcedureIntegrationTest
{
    public WindowProcedureIntegrationTest()
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
        using var host = Setup();
        var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger<WindowManagerUnitTest>();

        IWindowProcedure? proc_ = default;
        User32.HWND hwnd_ = default;

        var startupTcs = new TaskCompletionSource();
        var mainTcs = new TaskCompletionSource();

        using var threadMessageEvent = new AutoResetEvent(false);
        using var windowMessageEvent = new AutoResetEvent(false);

        using var cts = new CancellationTokenSource();

        logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] task run");
        var factory = new TaskFactory();
        var main = factory.StartNew(() =>
        {
            //scopeから取り出す
            var serviceScopeFactory = host.Services.GetRequiredService<IServiceScopeFactory>();
            using var scope = serviceScopeFactory.CreateScope();
            var uiThreadActivator = scope.ServiceProvider.GetRequiredService<IUIThreadActivator>();

            var proc = scope.ServiceProvider.GetRequiredService<IWindowProcedure>();

            proc.OnWindowMessage += (hwnd, message) =>
            {
                logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] OnWindowMessage {hwnd:X} {message}");

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
            };

            proc.OnThreadMessage += (message) =>
            {
                logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] OnThreadMessage {message}");
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
            };

            using var active = uiThreadActivator.Activate();
            logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] task start");

            var classManager = scope.ServiceProvider.GetRequiredService<IWindowClassManager>();
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
                logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] CreateWindowExW {hwnd_:X} {e}");
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

                logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] DispatchMessage 1 start");
                proc.DispatchMessage();
                logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] DispatchMessage 1 end");

                if (!threadMessageEvent.WaitOne(5000))
                {
                    Assert.Fail();
                }
                logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] threadMessageEvent WaitOne end");
                if (!windowMessageEvent.WaitOne(5000))
                {
                    Assert.Fail();
                }
                logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] windowMessageEvent WaitOne end");

                Exception? exception = default;

                try
                {
                    logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] ThrowIfOccurredInWndProc 1 start");
                    proc.ThrowIfOccurredInWndProc();
                    logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] ThrowIfOccurredInWndProc 1 end");
                }
                catch (Exception e)
                {
                    logger.LogInformation(e, $"★thread:[{Environment.CurrentManagedThreadId:X}] ThrowIfOccurredInWndProc 1 throw");
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
                logger.LogInformation(e, $"★thread:[{Environment.CurrentManagedThreadId:X}] task error");
                startupTcs.SetException(e);
                mainTcs.SetException(e);
                throw;
            }
            finally
            {
                startupTcs.TrySetResult();
            }
            logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] start up OK");

            try
            {
                while (true)
                {
                    if (cts.IsCancellationRequested)
                    {
                        logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] cancelled");
                        break;
                    }

                    logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] DispatchMessage 2 start");
                    proc.DispatchMessage();
                    logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] DispatchMessage 2 end");

                    Thread.Sleep(200);
                }
            }
            catch (Exception e)
            {
                logger.LogInformation(e, $"★thread:[{Environment.CurrentManagedThreadId:X}] task error");
                mainTcs.SetException(e);
            }
            finally
            {
                mainTcs.SetResult();
            }

            User32.DestroyWindow(hwnd_);

            //}, TaskCreationOptions.RunContinuationsAsynchronously);
        });

        logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] task await start");
        await startupTcs.Task;

        await factory.StartNew(() =>
        {
            Assert.IsNotNull(proc_);
            try
            {
                //作成したスレッド以外からは呼べない
                logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] DispatchMessage start");
                proc_.DispatchMessage();
                Assert.Fail($"★thread:[{Environment.CurrentManagedThreadId:X}] DispatchMessage end");
            }
            catch (InvalidOperationException e)
            {
                //OK
                logger.LogInformation(e, $"★thread:[{Environment.CurrentManagedThreadId:X}] DispatchMessage error occurred");
            }
        });

        logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] task await end");

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
        logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] threadMessageEvent WaitOne end");
        windowMessageEvent.WaitOne();
        logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] windowMessageEvent WaitOne end");

        Exception? exception = default;
        try
        {
            logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] ThrowIfOccurredInWndProc 1 start");
            proc_.ThrowIfOccurredInWndProc();
            logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] ThrowIfOccurredInWndProc 1 end");
        }
        catch (Exception e)
        {
            logger.LogInformation(e, $"★thread:[{Environment.CurrentManagedThreadId:X}] ThrowIfOccurredInWndProc 1 throw");
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

        logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] mainTcs await start");
        if (!mainTcs.Task.Wait(5000))
        {
            Assert.Fail();
        }
        logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] mainTcs await end");

        logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] main await start");
        if (!main.Wait(5000))
        {
            Assert.Fail();
        }
        logger.LogInformation($"★thread:[{Environment.CurrentManagedThreadId:X}] main await end");

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
            logger.LogInformation(e, $"★thread:[{Environment.CurrentManagedThreadId:X}] [{methodType}] error occurred");
        }
    }

}
