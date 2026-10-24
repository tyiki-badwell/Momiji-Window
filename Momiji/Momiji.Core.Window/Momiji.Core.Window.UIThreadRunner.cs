using System.Runtime.ExceptionServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Momiji.Internal.Window;

namespace Momiji.Core.Window;

internal interface IUIThreadRunner
{
    //TODO IUIThreadも渡すようにする
    delegate void OnStopHandler(IUIThreadRunner sender, Exception? exception);
    event OnStopHandler OnStop;

    Task<(IUIThread, Task)> StartAsync();
}

internal sealed partial class UIThreadRunner : IUIThreadRunner
{
    private readonly ILogger<UIThreadRunner> _logger;

    private readonly IServiceScopeFactory _serviceScopeFactory;

    public event IUIThreadRunner.OnStopHandler? OnStop;

    public UIThreadRunner(
        ILogger<UIThreadRunner> logger,
        IServiceScopeFactory serviceScopeFactory
    )
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(serviceScopeFactory);

        _logger = logger;

        _serviceScopeFactory = serviceScopeFactory;
    }

    public async Task<(IUIThread, Task)> StartAsync()
    {
        var tcs = new TaskCompletionSource<IUIThread>(TaskCreationOptions.AttachedToParent | TaskCreationOptions.RunContinuationsAsynchronously);
        var task = Start(tcs);

        var uiThread = await tcs.Task.ConfigureAwait(false);

        return (uiThread, task);
    }

    private async Task Start(
        TaskCompletionSource<IUIThread> startTcs
    )
    {
        Exception? runException = default;
        try
        {
            await Run(startTcs).ConfigureAwait(ConfigureAwaitOptions.None);
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogWithLine(LogLevel.Information, $"process task end.", Environment.CurrentManagedThreadId);
            }

            //NOTE このタスクでcontinue withすると、UIスレッドでQueue登録してスレッド終了し、QueueのCOMアクセスが失敗する
        }
        catch (Exception e)
        {
            runException = e;
        }

        try
        {
            _logger.LogWithLine(LogLevel.Trace, "call onStop", Environment.CurrentManagedThreadId);
            OnStop?.Invoke(this, runException);
        }
        catch (Exception e)
        {
            _logger.LogWithLine(LogLevel.Error, e, "onStop failed.", Environment.CurrentManagedThreadId);
        }

        _logger.LogWithLine(LogLevel.Information, "stopped.", Environment.CurrentManagedThreadId);

        if (runException != default)
        {
            //_processTaskのエラー状態を伝播する
            ExceptionDispatchInfo.Throw(runException);
        }
    }

    private Task Run(
        TaskCompletionSource<IUIThread> startTcs
    )
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.AttachedToParent | TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(async () =>
        {
            _logger.LogWithLine(LogLevel.Information, "*** thread start ***", Environment.CurrentManagedThreadId);

            try
            {
                //scopeから取り出す
                await using var scope = _serviceScopeFactory.CreateAsyncScope();
                var uiThread = scope.ServiceProvider.GetRequiredService<IUIThreadInternal>();

                uiThread.RunMessageLoop(startTcs);

                _logger.LogWithLine(LogLevel.Information, "message loop normal end.", Environment.CurrentManagedThreadId);
            }
            catch (Exception e)
            {
                //TODO エラーになるケースがあるか？
                _logger.LogWithLine(LogLevel.Error, e, "message loop abnormal end.", Environment.CurrentManagedThreadId);

                startTcs.TrySetException(e);
                tcs.SetException(e);
            }

            tcs.TrySetResult();

            _logger.LogWithLine(LogLevel.Information, "*** thread end ***", Environment.CurrentManagedThreadId);
        })
        {
            IsBackground = false,
            Name = "Momiji UI Thread"
        };

        //TODO MTAも指定可能にしてみる？
        thread.SetApartmentState(ApartmentState.STA);
        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogWithLine(LogLevel.Information, $"GetApartmentState {thread.GetApartmentState()}", Environment.CurrentManagedThreadId);
        }
        thread.Start();

        return tcs.Task;
    }
}
