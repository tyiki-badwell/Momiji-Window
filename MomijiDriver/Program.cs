using System.Windows.Interop;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Momiji.Core.Window;

namespace Momiji.Driver;

public class Program
{
    public static async Task Main(string[] args)
    {
        var a = Run(args);
        var b = Run(args);

        var logger = (ILogger<Program>?)a.Item1.Services.GetService(typeof(ILogger<Program>));

        var set = new HashSet<Task>
        {
            a.Item2,
            b.Item2
        };

        var windowA = a.Item3.CreateWindow("windowA", async (sender, message) => {
            //logger?.LogInformation($"   windowA:{message}");
            switch (message.Msg)
            {
                case 0x0210: //WM_PARENTNOTIFY
                    //message.WParam 子のウインドウメッセージ
                    //message.LParam 下位ワード　X座標／上位ワード　Y座標
                    break;

                case 0x0111: //WM_COMMAND
                    //message.WParam 上位ワード　0：メニュー／1：アクセラレータ／その他：ボタン識別子　下位ワード　識別子
                    //message.LParam ウインドウハンドル

                    logger?.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] delay start ===============================");
                    await Task.Delay(1000);
                    logger?.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] delay end ===============================");

                    break;

            }
        });
        var windowB = b.Item3.CreateWindow("windowB");

        var buttonA = a.Item3.CreateChildWindow(windowA, "BUTTON", "buttonA", (sender, message) => {
            //logger?.LogInformation($"       buttonA:{message}");
            switch (message.Msg)
            {
                case 0x0084: //WM_NCHITTEST
                    //message.LParam 下位ワード　X座標／上位ワード　Y座標
                    break;
                case 0x0020: //WM_SETCURSOR
                    //message.WParam カーソルを含むウィンドウへのハンドル
                    //message.LParam 下位ワード　WM_NCHITTESTの戻り値／上位ワード　マウスウインドウメッセージ
                    break;
                case 0x0200: //WM_MOUSEMOVE
                    //message.WParam 仮想キー
                    //message.LParam 下位ワード　X座標／上位ワード　Y座標
                    break;
                case 0x0021: //WM_MOUSEACTIVATE
                    //message.WParam トップレベルウインドウハンドル
                    //message.LParam 下位ワード　WM_NCHITTESTの戻り値／上位ワード　マウスウインドウメッセージ
                    break;
                case 0x0201: //WM_LBUTTONDOWN
                    //message.WParam 仮想キー
                    //message.LParam 下位ワード　X座標／上位ワード　Y座標
                    break;
                case 0x0281: //WM_IME_SETCONTEXT
                    //message.WParam ウインドウがアクティブなら1
                    //message.LParam 表示オプション
                    break;
                case 0x0007: //WM_SETFOCUS
                    //message.WParam フォーカスが移る前にフォーカスを持っていたウインドウハンドル
                    break;
                case 0x00F3: //BM_SETSTATE
                    //message.WParam ボタンを強調表示するならtrue
                    break;
                case 0x0202: //WM_LBUTTONUP
                    //message.WParam 仮想キー
                    //message.LParam 下位ワード　X座標／上位ワード　Y座標
                    break;
                case 0x0215: //WM_CAPTURECHANGED
                    //message.LParam マウスキャプチャしているウインドウハンドル
                    break;

            }
        
        });
        var textA = a.Item3.CreateChildWindow(windowA, "EDIT", "textA");

        var buttonB = b.Item3.CreateChildWindow(windowB, "BUTTON", "buttonB");
        var textB = b.Item3.CreateChildWindow(windowB, "EDIT", "textB");

        //BスレッドからAにボタン追加
        //TODO handleを保存できてない
        var buttonC = b.Item3.CreateChildWindow(windowA, "BUTTON", "buttonC");

        //WPFコンテンツを挿入する
        //AウインドウのスレッドでAウインドウに追加
        await windowA.DispatchAsync((window) => {
            var style = 0U;
            style = 0x10000000U; //WS_VISIBLE
            style |= 0x40000000U; //WS_CHILD
            var page = new TestPage();
            var param = new HwndSourceParameters("TestPage", 500, 500);
            param.SetPosition(200, 200);
            param.WindowStyle = (int)style;
            param.ParentWindow = window.Handle;

            var hwndSource = new HwndSource(param)
            {
                RootVisual = page
            };

            return 0;
        });

        await buttonA.MoveAsync(10, 10, 200, 80, true);
        await textA.MoveAsync(10, 300, 200, 80, true);

        //TODO 失敗する
        //buttonC.Move(300, 100, 200, 80, true);

        await windowA.ShowAsync(1);

        await buttonB.MoveAsync(10, 10, 200, 80, true);
        await textB.MoveAsync(10, 300, 200, 80, true);
        await windowB.ShowAsync(1);

        while (set.Count > 0)
        {
            var task = await Task.WhenAny(set).ConfigureAwait(false);
            set.Remove(task);
        }
    }

    private static (IHost, Task, IWindowManager) Run(string[] args)
    {
        var host = Worker.CreateHost(args);

        var task = host.RunAsync();

        var manager = (IWindowManager?)host.Services.GetService(typeof(IWindowManager));
        ArgumentNullException.ThrowIfNull(manager, nameof(manager));

        return (host, task, manager);
    }
}

public class Worker : BackgroundService
{
    public static IHost CreateHost(string[] args)
    {
        var builder = Host.CreateDefaultBuilder(args);

        builder.UseContentRoot(AppContext.BaseDirectory);
        builder.UseConsoleLifetime();

        builder.ConfigureServices((hostContext, services) =>
        {
            services.AddSingleton<IWindowManager, WindowManager>();
            services.AddHostedService<Worker>();
        });

        var host = builder.Build();

        return host;
    }

    private readonly ILogger _logger;
    private readonly IWindowManager _windowManager;

    public Worker(
        ILogger<Worker> logger,
        IWindowManager windowManager,
        IHostApplicationLifetime hostApplicationLifetime
    )
    {
        _logger = logger;
        _windowManager = windowManager;

        hostApplicationLifetime?.ApplicationStarted.Register(() =>
        {
            _logger.LogInformation("ApplicationStarted");
        });
        hostApplicationLifetime?.ApplicationStopping.Register(() =>
        {
            _logger.LogInformation("ApplicationStopping");
        });
        hostApplicationLifetime?.ApplicationStopped.Register(() =>
        {
            _logger.LogInformation("ApplicationStopped");
        });
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return _windowManager.StartAsync(stoppingToken);
    }
}