using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Nanook.GrindCore;
using SharpCompress.Compressors;
using SharpCompress.IO;
using GrindCoreZStdStream = Nanook.GrindCore.ZStd.ZStdStream;

namespace SharpCompress.Compressors.ZStandard;

/// <summary>
/// Public thin wrapper that exposes a compression-specific constructor.
/// Inherits the internal ZStandardStream implementation.
/// </summary>
public sealed class CompressionStream : ZStandardStream
{
    public CompressionStream(Stream destination, int compressionLevel, bool leaveOpen = false)
        : base(destination, compressionLevel, leaveOpen) { }
}

/// <summary>
/// Public thin wrapper that exposes a decompression-specific constructor.
/// Inherits the internal ZStandardStream implementation.
/// </summary>
public sealed class DecompressionStream : ZStandardStream
{
    public DecompressionStream(Stream source, bool leaveOpen = false)
        : base(source, leaveOpen) { }
}

/// <summary>
/// Lightweight wrapper for GrindCore ZStandard Stream that implements SharpCompress interfaces
/// </summary>
public class ZStandardStream : Stream, IStreamStack
{
    private readonly GrindCoreZStdStream _grindCoreStream;
    private readonly bool _leaveOpen;
    private bool _disposed;

    internal static bool IsZStandard(Stream stream)
    {
        var br = new BinaryReader(stream);
        var magic = br.ReadUInt32();
        if (ZstandardConstants.MAGIC != magic)
        {
            return false;
        }
        return true;
    }

    /// <summary>
    /// Initializes a new instance of the ZStandardStream class for compression
    /// </summary>
    /// <param name="baseStream">The stream to write compressed data to</param>
    /// <param name="compressionLevel">The compression level (1-22)</param>
    /// <param name="leaveOpen">Whether to leave the base stream open when disposing</param>
    public ZStandardStream(Stream baseStream, int compressionLevel, bool leaveOpen = false)
    {
        var options = new CompressionOptions
        {
            Type = (Nanook.GrindCore.CompressionType)compressionLevel,
            BufferSize = DefaultBufferSize > 0 ? DefaultBufferSize : 0x200000,
        };

        // Configure async-only mode if base stream requires it
        GrindCoreBufferHelper.ConfigureAsyncOnlyIfNeeded(options, baseStream);

        _grindCoreStream = new GrindCoreZStdStream(baseStream, options);
        _leaveOpen = leaveOpen;
    }

    /// <summary>
    /// Initializes a new instance of the ZStandardStream class for decompression
    /// </summary>
    /// <param name="baseStream">The stream to read compressed data from</param>
    /// <param name="leaveOpen">Whether to leave the base stream open when disposing</param>
    public ZStandardStream(Stream baseStream, bool leaveOpen = false)
    {
        var options = new CompressionOptions
        {
            Type = Nanook.GrindCore.CompressionType.Decompress,
            BufferSize = DefaultBufferSize > 0 ? DefaultBufferSize : 0x200000,
        };

        // Configure async-only mode if base stream requires it
        GrindCoreBufferHelper.ConfigureAsyncOnlyIfNeeded(options, baseStream);

        _grindCoreStream = new GrindCoreZStdStream(baseStream, options);
        _leaveOpen = leaveOpen;
    }

    /// <summary>
    /// Initializes a new instance of the ZStandardStream class with compression mode
    /// </summary>
    /// <param name="baseStream">The base stream</param>
    /// <param name="mode">Compression or decompression mode</param>
    /// <param name="compressionLevel">The compression level (ignored for decompression)</param>
    /// <param name="leaveOpen">Whether to leave the base stream open when disposing</param>
    public ZStandardStream(
        Stream baseStream,
        CompressionMode mode,
        int compressionLevel = 3,
        bool leaveOpen = false
    )
    {
        var options = new CompressionOptions
        {
            Type =
                mode == CompressionMode.Compress
                    ? (Nanook.GrindCore.CompressionType)compressionLevel
                    : Nanook.GrindCore.CompressionType.Decompress,
            BufferSize = DefaultBufferSize > 0 ? DefaultBufferSize : 0x200000,
        };

        // Configure async-only mode if base stream requires it
        GrindCoreBufferHelper.ConfigureAsyncOnlyIfNeeded(options, baseStream);

        _grindCoreStream = new GrindCoreZStdStream(baseStream, options);
        _leaveOpen = leaveOpen;
    }

    public int DefaultBufferSize { get; set; } = 0x200000;

    public Stream BaseStream()
    {
        return _grindCoreStream?.BaseStream ?? Stream.Null;
    }

    public int BufferSize
    {
        get => DefaultBufferSize;
        set => DefaultBufferSize = value;
    }

    public int BufferPosition { get; set; }

    public void SetPosition(long position)
    {
        // ZStandard streams typically don't support seeking, so this is a no-op
    }

#if DEBUG_STREAMS
    public long InstanceId { get; set; }
#endif

    public override bool CanRead => _grindCoreStream?.CanRead ?? false;

    public override bool CanSeek => _grindCoreStream?.CanSeek ?? false;

    public override bool CanWrite => _grindCoreStream?.CanWrite ?? false;

    public override long Length => _grindCoreStream?.Length ?? 0;

    public override long Position
    {
        get => _grindCoreStream?.Position ?? 0;
        set => _grindCoreStream.Position = value;
    }

    public override void Flush()
    {
        _grindCoreStream?.Flush();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        return _grindCoreStream?.Read(buffer, offset, count) ?? 0;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        return _grindCoreStream?.Seek(offset, origin) ?? 0;
    }

    public override void SetLength(long value)
    {
        _grindCoreStream?.SetLength(value);
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        _grindCoreStream?.Write(buffer, offset, count);
    }

    protected override void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            if (!_leaveOpen)
            {
                _grindCoreStream?.Dispose();
            }
            _disposed = true;
        }
        base.Dispose(disposing);
    }

    public override async Task FlushAsync(CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ZStandardStream));
        }

        try
        {
            await _grindCoreStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // If underlying stream does not support async flush, ignore.
        }

        try
        {
            try
            {
                var unused = (int)(_grindCoreStream?.BufferedBytesUnused ?? 0);
                if (unused > 0)
                {
                    ((IStreamStack)this).Rewind(unused);
                }
            }
            catch
            {
                try
                {
                    var diff = (int)(
                        (_grindCoreStream?.BasePosition ?? 0) - (_grindCoreStream?.Position ?? 0)
                    );
                    if (diff > 0)
                    {
                        ((IStreamStack)this).Rewind(diff);
                    }
                }
                catch { }
            }
        }
        catch { }
    }

    internal static async ValueTask<bool> IsZStandardAsync(
        Stream stream,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var buffer = new byte[4];
        var bytesRead = await stream
            .ReadAsync(buffer, 0, 4, cancellationToken)
            .ConfigureAwait(false);
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
            if (!_leaveOpen)
            {
                _grindCoreStream?.Dispose();
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
