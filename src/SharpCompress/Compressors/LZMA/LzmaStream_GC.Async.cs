using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SharpCompress.Compressors.LZMA;

public partial class LzmaStream
{
    public override Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken
    )
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(LzmaStream));
        }

        if (_isEncoder)
        {
            throw new NotSupportedException("Cannot read from an encoder stream");
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
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(LzmaStream));
        }

        if (!_isEncoder)
        {
            throw new NotSupportedException("Cannot write to a decoder stream");
        }

        return _grindCoreStream.WriteAsync(buffer, offset, count, cancellationToken);
    }

#if !LEGACY_DOTNET
    public override ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default
    )
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(LzmaStream));
        }

        if (_isEncoder)
        {
            throw new NotSupportedException("Cannot read from an encoder stream");
        }

        return _grindCoreStream.ReadAsync(buffer, cancellationToken);
    }

    public override ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default
    )
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(LzmaStream));
        }

        if (!_isEncoder)
        {
            throw new NotSupportedException("Cannot write to a decoder stream");
        }

        return _grindCoreStream.WriteAsync(buffer, cancellationToken);
    }

    public override async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        try
        {
            _grindCoreStream?.Dispose();

            if (!_leaveOpen && _inputStream != null)
            {
                await _inputStream.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _isDisposed = true;
            await base.DisposeAsync().ConfigureAwait(false);
        }
    }
#endif
}
