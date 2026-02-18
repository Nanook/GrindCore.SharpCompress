using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SharpCompress.Compressors.Deflate;

public partial class GZipStream
{
    public override async Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken
    )
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(GZipStream));
        }

        if (_isEncoder || _grindCoreStream == null)
        {
            return 0;
        }

        var n = await _grindCoreStream
            .ReadAsync(buffer, offset, count, cancellationToken)
            .ConfigureAwait(false);

        // Extract GZip metadata on first read for decompression
        if (!_firstReadDone && n > 0)
        {
            _firstReadDone = true;
            // TODO: Extract FileName, Comment, and LastModified from GrindCore stream
        }

        return n;
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
            throw new ObjectDisposedException(nameof(GZipStream));
        }

        if (!_isEncoder || _grindCoreStream == null)
        {
            throw new NotSupportedException("Stream is not in write mode.");
        }

        // Handle GZip header emission on first write for compression
        if (!_firstReadDone && _isEncoder)
        {
            _firstReadDone = true;
            // TODO: Emit GZip header - GrindCore handles this automatically
        }

        return _grindCoreStream.WriteAsync(buffer, offset, count, cancellationToken);
    }

#if !LEGACY_DOTNET
public override async ValueTask<int> ReadAsync(
    Memory<byte> buffer,
    CancellationToken cancellationToken = default
)
{
    if (_isDisposed)
    {
        throw new ObjectDisposedException(nameof(GZipStream));
    }

    if (_isEncoder || _grindCoreStream == null)
    {
        return 0;
    }

    var n = await _grindCoreStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);

    // Extract GZip metadata on first read for decompression
    if (!_firstReadDone && n > 0)
    {
        _firstReadDone = true;
        // TODO: Extract FileName, Comment, and LastModified from GrindCore stream
    }

    return n;
}

    public override ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default
    )
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(GZipStream));
        }

        if (!_isEncoder || _grindCoreStream == null)
        {
            throw new NotSupportedException("Stream is not in write mode.");
        }

        // Handle GZip header emission on first write for compression
        if (!_firstReadDone && _isEncoder)
        {
            _firstReadDone = true;
            // TODO: Emit GZip header - GrindCore handles this automatically
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
