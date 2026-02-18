using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SharpCompress.Compressors.Deflate;

public partial class DeflateStream
{
    public override async Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken
    )
    {
        if (_disposed)
        {
            throw new ObjectDisposedException("DeflateStream");
        }

        // Initialize read-to-compress mode on first read if in compression mode
        if (!_modeInitialized && _isEncoder)
        {
            InitializeReadToCompressMode();
        }
        else if (!_modeInitialized)
        {
            throw new InvalidOperationException("Stream not properly initialized");
        }

        if (_readToCompressMode)
        {
            return await ReadCompressedAsync(buffer, offset, count, cancellationToken)
                .ConfigureAwait(false);
        }

        return await _grindCoreStream!
            .ReadAsync(buffer, offset, count, cancellationToken)
            .ConfigureAwait(false);
    }

    public override async Task WriteAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken
    )
    {
        if (_disposed)
        {
            throw new ObjectDisposedException("DeflateStream");
        }

        // Initialize write-to-compress mode on first write if in compression mode
        if (!_modeInitialized && _isEncoder)
        {
            InitializeWriteToCompressMode();
        }
        else if (!_modeInitialized)
        {
            throw new InvalidOperationException("Stream not properly initialized");
        }

        if (_readToCompressMode)
        {
            throw new NotSupportedException("Cannot write to a read-to-compress stream");
        }

        await _grindCoreStream!
            .WriteAsync(buffer, offset, count, cancellationToken)
            .ConfigureAwait(false);
    }

#if !LEGACY_DOTNET
    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default
    )
    {
        if (_disposed)
        {
            throw new ObjectDisposedException("DeflateStream");
        }

        // Initialize read-to-compress mode on first read if in compression mode
        if (!_modeInitialized && _isEncoder)
        {
            InitializeReadToCompressMode();
        }
        else if (!_modeInitialized)
        {
            throw new InvalidOperationException("Stream not properly initialized");
        }

        if (_readToCompressMode)
        {
            // Memory<byte> overload for read-compressed - use temporary array
            byte[] tempBuffer = new byte[buffer.Length];
            int bytesRead = await ReadCompressedAsync(
                    tempBuffer,
                    0,
                    tempBuffer.Length,
                    cancellationToken
                )
                .ConfigureAwait(false);
            tempBuffer.AsSpan(0, bytesRead).CopyTo(buffer.Span);
            return bytesRead;
        }

        return await _grindCoreStream!.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
    }

    public override async ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default
    )
    {
        if (_disposed)
        {
            throw new ObjectDisposedException("DeflateStream");
        }

        // Initialize write-to-compress mode on first write if in compression mode
        if (!_modeInitialized && _isEncoder)
        {
            InitializeWriteToCompressMode();
        }
        else if (!_modeInitialized)
        {
            throw new InvalidOperationException("Stream not properly initialized");
        }

        if (_readToCompressMode)
        {
            throw new NotSupportedException("Cannot write to a read-to-compress stream");
        }

        await _grindCoreStream!.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
    }
#endif

    /// <summary>
    /// Async version of ReadCompressed for read-to-compress mode.
    /// </summary>
    private async Task<int> ReadCompressedAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken
    )
    {
        // Ensure we have compressed data available
        if (_compressionBuffer!.Position >= _compressionBuffer.Length && !_compressionComplete)
        {
            // Read more data from the input stream and compress it
            var inputBuffer = new byte[4096];
            int bytesRead = await _baseStream
                .ReadAsync(inputBuffer, 0, inputBuffer.Length, cancellationToken)
                .ConfigureAwait(false);

            if (bytesRead > 0)
            {
                // Write the data to the compressor
                await _grindCoreStream!
                    .WriteAsync(inputBuffer, 0, bytesRead, cancellationToken)
                    .ConfigureAwait(false);
                await _grindCoreStream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                // No more input data - finalize compression
                EnsureCompressionComplete();
            }
        }

        // Read from the compressed output buffer
        int availableBytes = (int)(_compressionBuffer.Length - _compressionBuffer.Position);
        int bytesToRead = Math.Min(count, availableBytes);

        if (bytesToRead > 0)
        {
            _compressionBuffer.Read(buffer, offset, bytesToRead);
        }

        return bytesToRead;
    }

#if !LEGACY_DOTNET
    public override async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            if (_readToCompressMode && !_compressionComplete)
            {
                // Finalize compression in read-to-compress mode
                _grindCoreStream?.Flush();
                _compressionComplete = true;
            }

            // Dispose GrindCore stream (which now has LeaveOpen=true, so won't dispose base stream)
            _grindCoreStream?.Dispose();
            _internalBuffer?.Dispose();
            _compressionBuffer?.Dispose();

            // Dispose base stream ourselves using async disposal
            if (!_leaveOpen && _baseStream != null)
            {
                await _baseStream.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _disposed = true;
            await base.DisposeAsync().ConfigureAwait(false);
        }
    }
#endif
}
