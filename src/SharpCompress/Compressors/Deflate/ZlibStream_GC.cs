#nullable disable

using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SharpCompress.IO;
using SharpCompress.Readers;
using SharpCompress.Writers;
using NGC = Nanook.GrindCore;

namespace SharpCompress.Compressors.Deflate;

/// <summary>
/// A Zlib compression and decompression stream that uses the GrindCore ZLib algorithm for improved performance.
/// </summary>
public partial class ZlibStream : Stream, IStreamStack
{
    Stream IStreamStack.BaseStream() => _inputStream;

    private readonly Stream _inputStream;
    private readonly bool _leaveOpen;
    private readonly bool _isEncoder;
    private readonly NGC.ZLib.ZLibStream _grindCoreStream;
    private bool _isDisposed;

    /// <summary>
    /// Initializes a new instance of the ZlibStream class.
    /// </summary>
    /// <param name="stream">The underlying stream.</param>
    /// <param name="mode">The compression mode.</param>
    public ZlibStream(Stream stream, CompressionMode mode)
        : this(stream, mode, CompressionLevel.Default, Encoding.UTF8, false) { }

    /// <summary>
    /// Initializes a new instance of the ZlibStream class.
    /// </summary>
    /// <param name="stream">The underlying stream.</param>
    /// <param name="mode">The compression mode.</param>
    /// <param name="level">The compression level.</param>
    public ZlibStream(Stream stream, CompressionMode mode, CompressionLevel level)
        : this(stream, mode, level, Encoding.UTF8, false) { }

    /// <summary>
    /// Initializes a new instance of the ZlibStream class.
    /// </summary>
    /// <param name="stream">The underlying stream.</param>
    /// <param name="mode">The compression mode.</param>
    /// <param name="leaveOpen">true to leave the stream open after the ZlibStream object is disposed; otherwise, false.</param>
    public ZlibStream(Stream stream, CompressionMode mode, bool leaveOpen)
        : this(stream, mode, CompressionLevel.Default, Encoding.UTF8, leaveOpen) { }

    /// <summary>
    /// Initializes a new instance of the ZlibStream class.
    /// </summary>
    /// <param name="stream">The underlying stream.</param>
    /// <param name="mode">The compression mode.</param>
    /// <param name="level">The compression level.</param>
    /// <param name="encoding">The encoding (maintained for compatibility).</param>
    public ZlibStream(
        Stream stream,
        CompressionMode mode,
        CompressionLevel level,
        Encoding encoding
    )
        : this(stream, mode, level, encoding, false) { }

    /// <summary>
    /// Initializes a new instance of the ZlibStream class.
    /// </summary>
    /// <param name="stream">The underlying stream.</param>
    /// <param name="mode">The compression mode.</param>
    /// <param name="level">The compression level.</param>
    /// <param name="encoding">The encoding (maintained for compatibility).</param>
    /// <param name="leaveOpen">true to leave the stream open after the ZlibStream object is disposed; otherwise, false.</param>
    /// <param name="writerOptions">Optional writer options for buffer size configuration when compressing.</param>
    /// <param name="readerOptions">Optional reader options for buffer size configuration when decompressing.</param>
    public ZlibStream(
        Stream stream,
        CompressionMode mode,
        CompressionLevel level,
        Encoding encoding,
        bool leaveOpen = false,
        WriterOptions writerOptions = null,
        ReaderOptions readerOptions = null,
        bool isNg = true
    )
    {
        _inputStream = stream;
        _leaveOpen = leaveOpen;
        _isEncoder = mode == CompressionMode.Compress;

        var options = new NGC.CompressionOptions()
        {
            Type = _isEncoder ? (NGC.CompressionType)level : NGC.CompressionType.Decompress,
            BufferSize = 0x10000,
            LeaveOpen = _leaveOpen,
            Version = isNg
                ? NGC.CompressionVersion.ZLibNgLatest()
                : NGC.CompressionVersion.ZLibLatest(),
        };

        // Apply buffer size options using the helper
        GrindCoreBufferHelper.ApplyBufferSizeOptions(
            options,
            this,
            _isEncoder,
            writerOptions,
            readerOptions
        );

        _grindCoreStream = new NGC.ZLib.ZLibStream(stream, options);

#if DEBUG_STREAMS
        this.DebugConstruct(typeof(ZlibStream));
#endif
    }

    /// <summary>
    /// Gets or sets the flush behavior. This property is not fully supported by the GrindCore wrapper.
    /// </summary>
    public virtual FlushType FlushMode { get; set; }

    /// <summary>
    /// Gets the size of the internal buffer from the GrindCore stream.
    /// Setting this property has no effect as the buffer size is managed by GrindCore.
    /// </summary>
    public int BufferSize
    {
        get => _grindCoreStream?.BufferedBytesTotal ?? 0;
        set
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(ZlibStream));
            }
        }
    }

    /// <summary>
    /// Gets the total number of bytes input so far.
    /// </summary>
    public virtual long TotalIn => _grindCoreStream?.BasePosition ?? 0;

    /// <summary>
    /// Gets the total number of bytes output so far.
    /// </summary>
    public virtual long TotalOut => _grindCoreStream?.PositionFullSize ?? 0;

    public override bool CanRead => !_isEncoder && _grindCoreStream?.CanRead == true;

    public override bool CanSeek => false;

    public override bool CanWrite => _isEncoder && _grindCoreStream?.CanWrite == true;

    public override void Flush()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(ZlibStream));
        }

        if (!_isEncoder)
        {
            ((IStreamStack)this).Rewind(
                (int)(_grindCoreStream.BasePosition - _grindCoreStream.Position)
            ); //seek back to the bytes used
        }
        else
        {
            _grindCoreStream?.Flush();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (_isDisposed)
        {
            return;
        }
        _isDisposed = true;

#if DEBUG_STREAMS
        this.DebugDispose(typeof(ZlibStream));
#endif

        if (disposing)
        {
            _grindCoreStream?.Dispose();

            if (!_leaveOpen)
            {
                _inputStream?.Dispose();
            }
        }
        base.Dispose(disposing);
    }

    public override long Length => _grindCoreStream?.Length ?? 0;

    public override long Position
    {
        get => _grindCoreStream?.Position ?? 0;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(ZlibStream));
        }

        if (_isEncoder || _grindCoreStream == null)
        {
            return 0;
        }

        return _grindCoreStream.Read(buffer, offset, count);
    }

    public override int ReadByte()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(ZlibStream));
        }

        if (_isEncoder || _grindCoreStream == null)
        {
            return -1;
        }

        return _grindCoreStream.ReadByte();
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count)
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(ZlibStream));
        }

        if (!_isEncoder || _grindCoreStream == null)
        {
            throw new NotSupportedException("Stream is not in write mode.");
        }

        _grindCoreStream.Write(buffer, offset, count);
    }

    public override void WriteByte(byte value)
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(ZlibStream));
        }

        if (!_isEncoder || _grindCoreStream == null)
        {
            throw new NotSupportedException("Stream is not in write mode.");
        }

        _grindCoreStream.WriteByte(value);
    }

    public override Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken
    )
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(ZlibStream));
        }

        if (_isEncoder || _grindCoreStream == null)
        {
            return Task.FromResult(0);
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
            throw new ObjectDisposedException(nameof(ZlibStream));
        }

        if (!_isEncoder || _grindCoreStream == null)
        {
            throw new NotSupportedException("Stream is not in write mode.");
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
            throw new ObjectDisposedException(nameof(ZlibStream));
        }

        if (_isEncoder || _grindCoreStream == null)
        {
            return new ValueTask<int>(0);
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
            throw new ObjectDisposedException(nameof(ZlibStream));
        }

        if (!_isEncoder || _grindCoreStream == null)
        {
            throw new NotSupportedException("Stream is not in write mode.");
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

    public byte[] Properties { get; }
}
