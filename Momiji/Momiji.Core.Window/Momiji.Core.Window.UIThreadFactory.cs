using System.Runtime.ExceptionServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Momiji.Internal.Window;

namespace Momiji.Core.Window;

internal sealed partial class UIThreadFactory : IUIThreadFactory
{
    private readonly ILogger<UIThreadFactory> _logger;

    private readonly IUIThreadMap _uiThreadMap;

    private readonly IServiceScopeFactory _serviceScopeFactory;

    public UIThreadFactory(
        ILogger<UIThreadFactory> logger,
        IUIThreadMap uiThreadMap,
        IServiceScopeFactory serviceScopeFactory
    )
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(uiThreadMap);
        ArgumentNullException.ThrowIfNull(serviceScopeFactory);

        _logger = logger;
        _uiThreadMap = uiThreadMap;

        _serviceScopeFactory = serviceScopeFactory;
    }

    public async Task<IUIThread> StartAsync()
    {
        var startTcs = new TaskCompletionSource<IUIThread>(TaskCreationOptions.AttachedToParent | TaskCreationOptions.RunContinuationsAsynchronously);
        var task = Start(startTcs);

        var uiThread = await startTcs.Task.ConfigureAwait(false);

        _uiThreadMap.Add(uiThread, task);

        return uiThread;
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

            var uiThread = await startTcs.Task;
            _uiThreadMap.Remove(uiThread);
        }
        catch (Exception e)
        {
            runException = e;
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
