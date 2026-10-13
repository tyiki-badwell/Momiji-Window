using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Momiji.Internal.Window;

internal sealed class HGlobalUnicodeString(
    string text,
    ILogger logger
) : IDisposable
{
    public readonly nint Handle = Marshal.StringToHGlobalUni(text);
    private readonly ILogger _logger = logger;

    private bool _disposed;

    ~HGlobalUnicodeString()
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

internal readonly ref struct HGlobalUnicodeStringScope(
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


