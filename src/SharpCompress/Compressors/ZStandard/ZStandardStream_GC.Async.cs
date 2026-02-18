using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SharpCompress.Compressors.ZStandard;

internal partial class ZStandardStream
{
    internal static async ValueTask<bool> IsZStandardAsync(
        Stream stream,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var buffer = new byte[4];
        var bytesRead = await stream.ReadAsync(buffer, 0, 4, cancellationToken);
        if (bytesRead < 4)
        {
            return false;
        }

        var magic = BitConverter.ToUInt32(buffer, 0);
        if (ZstandardConstants.MAGIC != magic)
        {
            return false;
        }
        return true;
    }

    public override Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken
    )
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ZStandardStream));
        }

        return _grindCoreStream.ReadAsync(buffer, offset, count, cancellationToken);
    }

    public override Task WriteAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken
    )
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ZStandardStream));
        }

        return _grindCoreStream.WriteAsync(buffer, offset, count, cancellationToken);
    }

#if !LEGACY_DOTNET
    public override ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default
    )
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ZStandardStream));
        }

        return _grindCoreStream.ReadAsync(buffer, cancellationToken);
    }

    public override ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default
    )
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ZStandardStream));
        }

        return _grindCoreStream.WriteAsync(buffer, cancellationToken);
    }

    public override async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _grindCoreStream?.Dispose();
            // No base stream to dispose - GrindCore stream manages it
        }
        finally
        {
            _disposed = true;
            await base.DisposeAsync().ConfigureAwait(false);
        }
    }
#endif
}
