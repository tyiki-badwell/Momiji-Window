using System.Runtime.InteropServices;

namespace Momiji.Internal.Util;

internal class StringToHGlobalUni(
    string text
) : IDisposable
{
    public readonly nint Handle = Marshal.StringToHGlobalUni(text);

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

    protected virtual void Dispose(bool disposing)
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
        }

        _disposed = true;
    }
}


