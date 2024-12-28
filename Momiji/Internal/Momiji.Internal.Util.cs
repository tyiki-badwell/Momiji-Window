using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Momiji.Internal.Log;

namespace Momiji.Internal.Util;

internal sealed partial class StringToHGlobalUni(
    string text,
    ILogger logger
) : IDisposable
{
    public readonly nint Handle = Marshal.StringToHGlobalUni(text);
    private readonly ILogger _logger = logger;

    private bool _disposed;

    ~StringToHGlobalUni()
    {
        Dispose(false);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
        }

        if (Handle != nint.Zero)
        {
            Marshal.FreeHGlobal(Handle);
            _logger.LogWithLine(LogLevel.Trace, "FreeHGlobal", Environment.CurrentManagedThreadId);
        }

        _disposed = true;
    }
}

internal readonly ref struct StringToHGlobalUniRAII(
    string text,
    ILogger logger
)
{
    public readonly nint Handle = Marshal.StringToHGlobalUni(text);
    private readonly ILogger _logger = logger;

    public void Dispose()
    {
        if (Handle != nint.Zero)
        {
            Marshal.FreeHGlobal(Handle);
            _logger.LogWithLine(LogLevel.Trace, "FreeHGlobal", Environment.CurrentManagedThreadId);
        }
    }
}

internal readonly ref struct SwitchSynchronizationContextRAII
{
    private readonly ILogger _logger;
    private readonly SynchronizationContext? _oldContext;

    public SwitchSynchronizationContextRAII(
        SynchronizationContext? context,
        ILogger logger
    )
    {
        _logger = logger;

        _oldContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        _logger.LogWithLine(LogLevel.Trace, $"switch context [{_oldContext}] -> [{context?.GetType()}]", Environment.CurrentManagedThreadId);
    }

    public void Dispose()
    {
        SynchronizationContext.SetSynchronizationContext(_oldContext);
        _logger.LogWithLine(LogLevel.Trace, $"restore context [{_oldContext}]", Environment.CurrentManagedThreadId);
    }
}

