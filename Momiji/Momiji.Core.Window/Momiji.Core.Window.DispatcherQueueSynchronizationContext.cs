using Microsoft.Extensions.Logging;
using Momiji.Internal.Window;

namespace Momiji.Core.Window;

internal interface IDispatcherQueueSynchronizationContext
{
    SynchronizationContext SynchronizationContext { get; }
}

internal sealed class DispatcherQueueSynchronizationContext
    : SynchronizationContext, IDispatcherQueueSynchronizationContext
{
    public SynchronizationContext SynchronizationContext => this;

    private readonly ILogger<DispatcherQueueSynchronizationContext> _logger;
    private readonly IDispatcherQueue _dispatcherQueue;

    public DispatcherQueueSynchronizationContext(
        ILogger<DispatcherQueueSynchronizationContext> logger,
        IDispatcherQueue dispatcherQueue
    )
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(dispatcherQueue);

        _logger = logger;
        _dispatcherQueue = dispatcherQueue;
    }

    public override void Send(SendOrPostCallback d, object? state)
    {
        _logger.LogWithLine(LogLevel.Trace, "Send", Environment.CurrentManagedThreadId);
        throw new NotSupportedException("Send");
    }

    public override void Post(SendOrPostCallback d, object? state)
    {
        _logger.LogWithLine(LogLevel.Trace, "Post", Environment.CurrentManagedThreadId);
        _dispatcherQueue.Enqueue(d, state);
    }

    public override SynchronizationContext CreateCopy()
    {
        _logger.LogWithLine(LogLevel.Trace, "CreateCopy", Environment.CurrentManagedThreadId);
        return this;
    }

    public override void OperationStarted()
    {
        _logger.LogWithLine(LogLevel.Trace, "OperationStarted", Environment.CurrentManagedThreadId);
    }

    public override void OperationCompleted()
    {
        _logger.LogWithLine(LogLevel.Trace, "OperationCompleted", Environment.CurrentManagedThreadId);
    }
}

internal readonly ref struct DispatcherQueueSynchronizationContextScope : IDisposable
{
    private readonly ILogger _logger;
    private readonly SynchronizationContext? _previousContext;

    public DispatcherQueueSynchronizationContextScope(
        IDispatcherQueueSynchronizationContext context,
        ILogger logger
    )
    {
        _logger = logger;

        _previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context.SynchronizationContext);
        if (_logger.IsEnabled(LogLevel.Trace))
        {
            _logger.LogWithLine(LogLevel.Trace, $"switch context [{_previousContext}] -> [{context.GetType()}]", Environment.CurrentManagedThreadId);
        }
    }

    public void Dispose()
    {
        SynchronizationContext.SetSynchronizationContext(_previousContext);
        if (_logger.IsEnabled(LogLevel.Trace))
        {
            _logger.LogWithLine(LogLevel.Trace, $"restore context [{_previousContext}]", Environment.CurrentManagedThreadId);
        }
    }
}
