using System.Windows.Interop;
using Momiji.Core.Window;

namespace Momiji.Driver;

public class Program
{
    public static async Task Main(string[] args)
    {
        var host = Worker.CreateHost(args);
        await host.RunAsync();
    }
}

public class Worker : BackgroundService
{
    public static IHost CreateHost(string[] args)
    {
        var builder = Host.CreateDefaultBuilder(args);

        builder.UseContentRoot(AppContext.BaseDirectory);
        builder.UseConsoleLifetime();

        builder.UseMomijiWindow();

        builder.ConfigureServices((hostContext, services) =>
        {
            services.AddHostedService<Worker>();
        });

        var host = builder.Build();

        return host;
    }

    private readonly ILogger _logger;
    private readonly IServiceScopeFactory _serviceScopeFactory;

    public Worker(
        ILogger<Worker> logger,
        IHostApplicationLifetime hostApplicationLifetime,
        IServiceScopeFactory serviceScopeFactory
    )
    {
        _logger = logger;
        _serviceScopeFactory = serviceScopeFactory;

        hostApplicationLifetime.ApplicationStarted.Register(() =>
        {
            _logger.LogInformation("ApplicationStarted");
        });
        hostApplicationLifetime.ApplicationStopping.Register(() =>
        {
            _logger.LogInformation("ApplicationStopping");
        });
        hostApplicationLifetime.ApplicationStopped.Register(() =>
        {
            _logger.LogInformation("ApplicationStopped");
        });
    }

    protected async override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ExecuteAsync");

        using var scope = _serviceScopeFactory.CreateScope();

        var a = scope.ServiceProvider.GetRequiredService<IUIThread>();
        var b = scope.ServiceProvider.GetRequiredService<IUIThread>();

        var windowA = a.CreateWindow("windowA", onMessage: async(sender, message) =>
        {
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

                    _logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] delay start ===============================");
                    await Task.Delay(1000);
                    _logger.LogInformation($"thread:[{Environment.CurrentManagedThreadId:X}] delay end ===============================");

                    break;

            }
        });

        var windowB = b.CreateWindow("windowB");

        var buttonA = a.CreateWindow("buttonA", windowA, "BUTTON", onMessage: (sender, message) =>
        {
            _logger.LogInformation($"       buttonA:{message}");
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

        var textA = a.CreateWindow("textA", windowA, "EDIT", onMessage: (sender, message) =>
        {
            _logger.LogInformation($"       textA:{message}");
        });

        var buttonB = b.CreateWindow("buttonB", windowB, "BUTTON", onMessage: (sender, message) =>
        {
            _logger.LogInformation($"       buttonB:{message}");
        });
        var textB = b.CreateWindow("textB", windowB, "EDIT", onMessage: (sender, message) =>
        {
            _logger.LogInformation($"       textB:{message}");
        });

        //BスレッドからAウインドウにボタン追加
        var buttonC = b.CreateWindow("buttonC", windowA, "BUTTON", onMessage: (sender, message) =>
        {
            _logger.LogInformation($"       buttonC:{message}");
        });

        //WPFコンテンツを挿入する
        //AウインドウのスレッドでAウインドウに追加
        await windowA.DispatchAsync((window) => 
        {
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

        await buttonC.MoveAsync(300, 100, 200, 80, true);

        await windowA.ShowAsync(1);

        await buttonB.MoveAsync(10, 10, 200, 80, true);
        await textB.MoveAsync(10, 300, 200, 80, true);
        await windowB.ShowAsync(1);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }

    public async override Task StopAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("StopAsync");
        await base.StopAsync(stoppingToken);
    }
}