using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SharpCompress.Compressors;
using SharpCompress.IO;
using SharpCompress.Providers;
#if GRINDCORE
using Nanook.GrindCore;
using GrindCoreBZip2Stream = Nanook.GrindCore.BZip2.BZip2Stream;
#endif

namespace SharpCompress.Compressors.BZip2;

/// <summary>
/// GrindCore-backed BZip2 stream that replaces the managed CBZip2InputStream/CBZip2OutputStream
/// with native libbzip2 1.0.8 via GrindCore. Preserves the same public API surface as the
/// managed BZip2Stream (Create/CreateAsync factory methods, IFinishable, IAsyncDisposable).
/// </summary>
public sealed partial class BZip2Stream : Stream, IFinishable, IAsyncDisposable, IStreamStack
{
#if GRINDCORE
    private GrindCoreBZip2Stream? _grindCoreStream;
    private bool _isDisposed;

    Stream IStreamStack.BaseStream() => _grindCoreStream?.BaseStream ?? Stream.Null;

    private BZip2Stream(GrindCoreBZip2Stream grindCoreStream)
    {
        _grindCoreStream = grindCoreStream;
        Mode = grindCoreStream.CanWrite ? CompressionMode.Compress : CompressionMode.Decompress;
    }

    /// <summary>
    /// Create a BZip2Stream
    /// </summary>
    /// <param name="stream">The stream to read from or write to</param>
    /// <param name="compressionMode">Compression Mode</param>
    /// <param name="decompressConcatenated">Decompress Concatenated (handled natively by GrindCore's multi-stream support)</param>
    /// <param name="leaveOpen">Leave the underlying stream open when this stream is disposed</param>
    /// <param name="tolerateTruncatedStream">
    /// Decompression only. When true, an end-of-stream reached at a bzip2 block boundary is treated as a
    /// normal end of stream rather than throwing. This allows decoding a truncated or partial stream.
    /// </param>
    /// <param name="inputSize">
    /// Decompression only. When positive, limits the number of compressed bytes read from the base stream.
    /// Used by archive formats (e.g., Zip) where the compressed entry size is known from the header.
    /// </param>
    public static BZip2Stream Create(
        Stream stream,
        CompressionMode compressionMode,
        bool decompressConcatenated,
        bool leaveOpen = false,
        bool tolerateTruncatedStream = false,
        long inputSize = -1
    )
    {
        var options = BuildOptions(
            stream,
            compressionMode,
            decompressConcatenated,
            leaveOpen,
            tolerateTruncatedStream,
            inputSize
        );

        var grindCoreStream = new GrindCoreBZip2Stream(stream, options);
        return new BZip2Stream(grindCoreStream);
    }

    /// <summary>
    /// Create a BZip2Stream asynchronously
    /// </summary>
    public static ValueTask<BZip2Stream> CreateAsync(
        Stream stream,
        CompressionMode compressionMode,
        bool decompressConcatenated,
        bool leaveOpen = false,
        bool tolerateTruncatedStream = false,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        // GrindCore BZip2Stream construction is synchronous; wrap in ValueTask for API compat.
        var result = Create(
            stream,
            compressionMode,
            decompressConcatenated,
            leaveOpen,
            tolerateTruncatedStream
        );
        return new ValueTask<BZip2Stream>(result);
    }

    public async ValueTask FinishAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Mode == CompressionMode.Compress && _grindCoreStream is not null)
        {
            // Use CompleteAsync directly — it goes through onFlushAsync which uses
            // BaseWriteAsync unconditionally, avoiding the _baseStreamAsyncOnly cold-start
            // gap that would cause sync Flush() to fail on async-only streams.
            await _grindCoreStream.CompleteAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public void Finish()
    {
        // For compression, GrindCore's CompressionStream.Flush() triggers BZ_FINISH.
        // For decompression, Flush() does the overread rewind, so don't call it here.
        if (Mode == CompressionMode.Compress)
        {
            _grindCoreStream?.Flush();
        }
    }

    public CompressionMode Mode { get; private set; }

    public override bool CanRead => _grindCoreStream?.CanRead ?? false;
    public override bool CanSeek => false;
    public override bool CanWrite => _grindCoreStream?.CanWrite ?? false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => _grindCoreStream?.Position ?? 0;
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
        if (_grindCoreStream is null)
        {
            return;
        }

        if (Mode == CompressionMode.Decompress)
        {
            // GrindCore overreads from the base stream into internal buffers.
            // BufferedBytesUnused tells us how many bytes were read from the base stream
            // but not consumed by the bzip2 decompressor. Rewind the underlying
            // SharpCompressStream by that amount so the next entry starts correctly.
            int unused = _grindCoreStream.BufferedBytesUnused;
            if (unused > 0)
            {
                ((IStreamStack)this).Rewind(unused);
            }
        }
        else
        {
            _grindCoreStream.Flush();
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        try
        {
            return _grindCoreStream?.Read(buffer, offset, count) ?? 0;
        }
        catch (InvalidDataException ex)
        {
            throw new Common.ArchiveOperationException(ex.Message, ex);
        }
    }

    public override int ReadByte()
    {
        if (_grindCoreStream is null)
        {
            return -1;
        }

        try
        {
            return _grindCoreStream.ReadByte();
        }
        catch (InvalidDataException ex)
        {
            throw new Common.ArchiveOperationException(ex.Message, ex);
        }
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) =>
        _grindCoreStream?.Write(buffer, offset, count);

    public override void WriteByte(byte value) => _grindCoreStream?.WriteByte(value);

#if !LEGACY_DOTNET
    public override int Read(Span<byte> buffer)
    {
        if (_grindCoreStream is null)
        {
            return 0;
        }

        try
        {
            var tmp = new byte[buffer.Length];
            int read = _grindCoreStream.Read(tmp, 0, tmp.Length);
            tmp.AsSpan(0, read).CopyTo(buffer);
            return read;
        }
        catch (InvalidDataException ex)
        {
            throw new Common.ArchiveOperationException(ex.Message, ex);
        }
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (_grindCoreStream is null)
        {
            return;
        }

        var tmp = buffer.ToArray();
        _grindCoreStream.Write(tmp, 0, tmp.Length);
    }
#endif

    public override async Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken
    )
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(BZip2Stream));
        }

        try
        {
            return await _grindCoreStream!
                .ReadAsync(buffer, offset, count, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (InvalidDataException ex)
        {
            if (Mode == CompressionMode.Decompress && _grindCoreStream!.PositionFullSize > 0)
            {
                return 0;
            }

            throw new Common.ArchiveOperationException(ex.Message, ex);
        }
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
            throw new ObjectDisposedException(nameof(BZip2Stream));
        }

        return _grindCoreStream!.WriteAsync(buffer, offset, count, cancellationToken);
    }

#if !LEGACY_DOTNET
    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default
    )
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(BZip2Stream));
        }

        try
        {
            return await _grindCoreStream!
                .ReadAsync(buffer, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (InvalidDataException ex)
        {
            throw new Common.ArchiveOperationException(ex.Message, ex);
        }
    }

    public override ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default
    )
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(BZip2Stream));
        }

        return _grindCoreStream!.WriteAsync(buffer, cancellationToken);
    }

    public override async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        if (_grindCoreStream is not null)
        {
            await _grindCoreStream.DisposeAsync().ConfigureAwait(false);
        }

        await base.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
#elif NETSTANDARD2_1
    public override async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _grindCoreStream?.Dispose();
        await Task.CompletedTask.ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
#else
    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _grindCoreStream?.Dispose();
        await Task.CompletedTask.ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
#endif

    protected override void Dispose(bool disposing)
    {
        if (_isDisposed)
        {
            base.Dispose(disposing);
            return;
        }

        _isDisposed = true;
        if (disposing)
        {
            _grindCoreStream?.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// Consumes two bytes to test if there is a BZip2 header
    /// </summary>
    public static bool IsBZip2(Stream stream)
    {
        using var br = new BinaryReader(stream, Encoding.Default, leaveOpen: true);
        var chars = br.ReadBytes(2);
        if (chars.Length < 2 || chars[0] != 'B' || chars[1] != 'Z')
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Asynchronously consumes two bytes to test if there is a BZip2 header
    /// </summary>
    public static async ValueTask<bool> IsBZip2Async(
        Stream stream,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var buffer = new byte[2];
        var bytesRead = await stream
            .ReadAsync(buffer, 0, 2, cancellationToken)
            .ConfigureAwait(false);
        if (bytesRead < 2 || buffer[0] != 'B' || buffer[1] != 'Z')
        {
            return false;
        }

        return true;
    }

    private static Nanook.GrindCore.CompressionOptions BuildOptions(
        Stream stream,
        CompressionMode compressionMode,
        bool decompressConcatenated,
        bool leaveOpen,
        bool tolerateTruncatedStream,
        long inputSize = -1
    )
    {
        var options = new Nanook.GrindCore.CompressionOptions { LeaveOpen = leaveOpen };

        if (compressionMode == CompressionMode.Compress)
        {
            // Default to block size 9 (maximum compression, matching CBZip2OutputStream default)
            options.Type = (Nanook.GrindCore.CompressionType)9;
        }
        else
        {
            options.Type = Nanook.GrindCore.CompressionType.Decompress;

            // When the compressed size is known (e.g., from a Zip entry header),
            // set PositionLimit so GrindCore stops reading from the base stream
            // at the entry boundary rather than overreading into the next entry.
            if (inputSize > 0)
            {
                options.PositionLimit = inputSize;
            }

            // Enable tolerance for truncation and data errors when explicitly requested.
            if (tolerateTruncatedStream)
            {
                options.Dictionary ??= new Nanook.GrindCore.CompressionDictionaryOptions();
                options.Dictionary.TolerateTruncation = true;
            }
        }

        // Configure async-only mode if base stream requires it
        GrindCoreBufferHelper.ConfigureAsyncOnlyIfNeeded(options, stream);

        return options;
    }
#else
    // Non-GrindCore fallback - should never be compiled when UseGrindCore=true
    private BZip2Stream()
    {
        throw new NotSupportedException("BZip2 GrindCore wrapper requires GrindCore library");
    }

    public static BZip2Stream Create(
        Stream stream,
        CompressionMode compressionMode,
        bool decompressConcatenated,
        bool leaveOpen = false,
        bool tolerateTruncatedStream = false
    ) => throw new NotSupportedException("BZip2 GrindCore wrapper requires GrindCore library");

    public static ValueTask<BZip2Stream> CreateAsync(
        Stream stream,
        CompressionMode compressionMode,
        bool decompressConcatenated,
        bool leaveOpen = false,
        bool tolerateTruncatedStream = false,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException("BZip2 GrindCore wrapper requires GrindCore library");

    public ValueTask FinishAsync(CancellationToken cancellationToken = default) => default;

    public void Finish() { }

    public CompressionMode Mode { get; private set; }
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush() { }

    public override int Read(byte[] buffer, int offset, int count) => 0;

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) { }

    public static bool IsBZip2(Stream stream) => false;

    public static ValueTask<bool> IsBZip2Async(
        Stream stream,
        CancellationToken cancellationToken = default
    ) => new ValueTask<bool>(false);
#endif
}
