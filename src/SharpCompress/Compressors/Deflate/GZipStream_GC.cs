#nullable disable

using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SharpCompress.Common;
using SharpCompress.Common.Options;
using SharpCompress.IO;
using SharpCompress.Readers;
using SharpCompress.Writers;
using NGC = Nanook.GrindCore;

namespace SharpCompress.Compressors.Deflate;

public class GZipStream : Stream, IStreamStack
{
    internal static readonly DateTime UNIX_EPOCH = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    Stream IStreamStack.BaseStream() => _inputStream;

    private readonly Stream _inputStream;
    private readonly bool _leaveOpen;
    private readonly bool _isEncoder;
    private NGC.GZip.GZipStream _grindCoreStream;
#pragma warning disable IDE0052 // Remove unread private members
    private readonly CompressionLevel _compressionLevel;
    private readonly Encoding _encoding;
#pragma warning restore IDE0052 // Remove unread private members
    private bool _isDisposed;
    private bool _firstReadDone;
    private bool _isNg;
    private WriterOptions _storedWriterOptions;
    private bool _compressModeInitialized;
    private bool _customHeaderMode;
    private CRC32 _crc32;
    private long _originalSize;
    private NGC.DeflateZLib.DeflateStream _rawDeflateStream;

    // GZip-specific properties
    private string _comment;
    private string _fileName;
    private DateTime? _lastModified;

    public GZipStream(Stream stream, CompressionMode mode)
        : this(stream, mode, CompressionLevel.Default, Encoding.UTF8) { }

    public GZipStream(Stream stream, CompressionMode mode, IReaderOptions readerOptions)
        : this(stream, mode, CompressionLevel.Default, readerOptions) { }

    public GZipStream(
        Stream stream,
        CompressionMode mode,
        CompressionLevel level,
        IReaderOptions readerOptions
    )
        : this(
            stream,
            mode,
            level,
            (
                readerOptions ?? throw new ArgumentNullException(nameof(readerOptions))
            ).ArchiveEncoding.GetEncoding(),
            false,
            null,
            readerOptions as ReaderOptions
        )
    { }
    public GZipStream(
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
        _compressionLevel = level;
        _encoding = encoding;
        _isNg = isNg;

        if (_isEncoder)
        {
            // For compress mode, defer stream creation until first Write so that
            // FileName/Comment/LastModified set before the first write can be
            // included in a custom GZip header.
            _storedWriterOptions = writerOptions;
        }
        else
        {
            var options = new NGC.CompressionOptions()
            {
                Type = NGC.CompressionType.Decompress,
                BufferSize = 0x10000,
                LeaveOpen = true,
                Version = isNg
                    ? NGC.CompressionVersion.ZLibNgLatest()
                    : NGC.CompressionVersion.ZLibLatest(),
            };

            GrindCoreBufferHelper.ApplyBufferSizeOptions(options, this, false, null, readerOptions);
            GrindCoreBufferHelper.ConfigureAsyncOnlyIfNeeded(options, stream);

            // Attempt to extract GZip header metadata (filename, comment, mtime) when
            // decompressing. GrindCore focuses on stream processing and does not expose
            // header fields, so parse the header here for seekable streams.
            try
            {
                if (stream.CanSeek)
                {
                    var (name, comment, mtime) = GZipHeaderHelper.ParseGzipHeader(stream, _encoding);
                    if (name != null)
                    {
                        _fileName = name;
                    }

                    if (comment != null)
                    {
                        _comment = comment;
                    }

                    if (mtime != null)
                    {
                        _lastModified = mtime;
                    }

                    stream.Position = 0;
                }
            }
            catch
            {
                try
                {
                    if (stream.CanSeek)
                    {
                        stream.Position = 0;
                    }
                }
                catch { }
            }

            _grindCoreStream = new NGC.GZip.GZipStream(stream, options);
        }

#if DEBUG_STREAMS
        this.DebugConstruct(typeof(GZipStream));
#endif
    }

    /// <summary>
    /// Gets or sets the comment for the GZip file.
    /// </summary>
    public string Comment
    {
        get => _comment;
        set
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(GZipStream));
            }
            _comment = value;
        }
    }

    /// <summary>
    /// Gets or sets the last modified time for the GZip file.
    /// </summary>
    public DateTime? LastModified
    {
        get => _lastModified;
        set
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(GZipStream));
            }
            _lastModified = value;
        }
    }

    /// <summary>
    /// Gets or sets the filename for the GZip file.
    /// </summary>
    public string FileName
    {
        get => _fileName;
        set
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(GZipStream));
            }
            _fileName = value;
            if (_fileName is null)
            {
                return;
            }
#pragma warning disable CA1307 // Specify StringComparison for clarity
            if (_fileName.Contains('/'))
            {
                _fileName = _fileName.Replace('/', '\\');
            }
#pragma warning restore CA1307 // Specify StringComparison for clarity
            if (_fileName.EndsWith('\\'))
            {
                throw new InvalidOperationException("Illegal filename");
            }
#pragma warning disable CA1307 // Specify StringComparison for clarity
            if (_fileName.Contains('\\'))
            {
                // trim any leading path
                _fileName = System.IO.Path.GetFileName(_fileName);
            }
#pragma warning restore CA1307 // Specify StringComparison for clarity
        }
    }

    /// <summary>
    /// Gets the CRC32 checksum of the data processed.
    /// </summary>
    public int Crc32 { get; private set; }

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
                throw new ObjectDisposedException("GZipStream");
            }
        }
    }

    /// <summary>
    /// Gets the total number of bytes input so far.
    /// </summary>
    internal virtual long TotalIn => _grindCoreStream?.BasePosition ?? 0;

    /// <summary>
    /// Gets the total number of bytes output so far.
    /// </summary>
    internal virtual long TotalOut => _grindCoreStream?.PositionFullSize ?? 0;

    public override bool CanRead => !_isEncoder && _grindCoreStream?.CanRead == true;

    public override bool CanSeek => false;

    public override bool CanWrite
    {
        get
        {
            if (!_isEncoder)
            {
                return false;
            }

            if (!_compressModeInitialized)
            {
                return !_isDisposed;
            }

            return _customHeaderMode ? _rawDeflateStream?.CanWrite == true : _grindCoreStream?.CanWrite == true;
        }
    }

    public override void Flush()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException("GZipStream");
        }

        if (!_isEncoder)
        {
            ((IStreamStack)this).Rewind(
                (int)(_grindCoreStream.BasePosition - _grindCoreStream.Position)
            ); //seek back to the bytes used
        }
        else if (_customHeaderMode)
        {
            _rawDeflateStream?.Flush();
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
        this.DebugDispose(typeof(GZipStream));
#endif

        if (disposing)
        {
            if (_customHeaderMode && _rawDeflateStream != null)
            {
                _rawDeflateStream.Dispose();
                WriteGzipTrailer();
            }
            else
            {
                _grindCoreStream?.Dispose();
            }

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
            throw new ObjectDisposedException("GZipStream");
        }

        if (_isEncoder || _grindCoreStream == null)
        {
            return 0;
        }

        var n = _grindCoreStream.Read(buffer, offset, count);

        // Extract GZip metadata on first read for decompression
        if (!_firstReadDone && n > 0)
        {
            _firstReadDone = true;
            // TODO: Extract FileName, Comment, and LastModified from GrindCore stream
            // For now, these would need to be extracted from the GrindCore implementation
            // or parsed manually from the GZip header
        }

        return n;
    }

    public override int ReadByte()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException("GZipStream");
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
            throw new ObjectDisposedException("GZipStream");
        }

        if (!_isEncoder)
        {
            throw new NotSupportedException("Stream is not in write mode.");
        }

        InitializeCompressMode();

        if (_customHeaderMode)
        {
            _crc32!.SlurpBlock(buffer, offset, count);
            _originalSize += count;
            _rawDeflateStream!.Write(buffer, offset, count);
        }
        else
        {
            _grindCoreStream!.Write(buffer, offset, count);
        }
    }

    public override void WriteByte(byte value)
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException("GZipStream");
        }

        if (!_isEncoder)
        {
            throw new NotSupportedException("Stream is not in write mode.");
        }

        InitializeCompressMode();

        if (_customHeaderMode)
        {
            var buf = new byte[] { value };
            _crc32!.SlurpBlock(buf, 0, 1);
            _originalSize++;
            _rawDeflateStream!.WriteByte(value);
        }
        else
        {
            _grindCoreStream!.WriteByte(value);
        }
    }

    private int EmitHeader()
    {
        var commentBytes = (_comment is null) ? null : _encoding.GetBytes(_comment);
        var filenameBytes = (_fileName is null) ? null : _encoding.GetBytes(_fileName);

        var cbLength = commentBytes?.Length + 1 ?? 0;
        var fnLength = filenameBytes?.Length + 1 ?? 0;

        var bufferLength = 10 + cbLength + fnLength;
        var header = new byte[bufferLength];
        var i = 0;

        header[i++] = 0x1F;
        header[i++] = 0x8B;
        header[i++] = 8;

        byte flag = 0;
        if (_comment != null)
        {
            flag ^= 0x10;
        }

        if (_fileName != null)
        {
            flag ^= 0x08;
        }
        header[i++] = flag;

        _lastModified ??= DateTime.Now;
        var delta = _lastModified.Value - UNIX_EPOCH;
        var timet = (int)delta.TotalSeconds;
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(i), timet);
        i += 4;

        header[i++] = 0;    // xflg
        header[i++] = 0xFF; // OS = unspecified

        if (fnLength != 0)
        {
            Array.Copy(filenameBytes!, 0, header, i, fnLength - 1);
            i += fnLength - 1;
            header[i++] = 0;
        }

        if (cbLength != 0)
        {
            Array.Copy(commentBytes!, 0, header, i, cbLength - 1);
            i += cbLength - 1;
            header[i++] = 0;
        }

        _inputStream.Write(header, 0, header.Length);
        return header.Length;
    }

    private void InitializeCompressMode()
    {
        if (_compressModeInitialized)
        {
            return;
        }
        _compressModeInitialized = true;

        if (_fileName != null || _comment != null || _lastModified != null)
        {
            _customHeaderMode = true;
            EmitHeader();
            _crc32 = new CRC32();
            _originalSize = 0;

            var options = new NGC.CompressionOptions()
            {
                Type = (NGC.CompressionType)_compressionLevel,
                BufferSize = 0x10000,
                LeaveOpen = true,
                Version = _isNg ? NGC.CompressionVersion.ZLibNgLatest() : NGC.CompressionVersion.ZLibLatest(),
            }.WithDeflate(9);

            GrindCoreBufferHelper.ApplyBufferSizeOptions(options, this, true, _storedWriterOptions, null);
            GrindCoreBufferHelper.ConfigureAsyncOnlyIfNeeded(options, _inputStream);

            _rawDeflateStream = new NGC.DeflateZLib.DeflateStream(_inputStream, options);
        }
        else
        {
            _customHeaderMode = false;

            var options = new NGC.CompressionOptions()
            {
                Type = (NGC.CompressionType)_compressionLevel,
                BufferSize = 0x10000,
                LeaveOpen = true,
                Version = _isNg ? NGC.CompressionVersion.ZLibNgLatest() : NGC.CompressionVersion.ZLibLatest(),
            };

            GrindCoreBufferHelper.ApplyBufferSizeOptions(options, this, true, _storedWriterOptions, null);
            GrindCoreBufferHelper.ConfigureAsyncOnlyIfNeeded(options, _inputStream);

            _grindCoreStream = new NGC.GZip.GZipStream(_inputStream, options);
        }
    }

    private void WriteGzipTrailer()
    {
        var trailer = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(trailer.AsSpan(0), (uint)_crc32!.Crc32Result);
        BinaryPrimitives.WriteUInt32LittleEndian(trailer.AsSpan(4), (uint)_originalSize);
        _inputStream.Write(trailer, 0, trailer.Length);
    }

    public override async Task FlushAsync(CancellationToken cancellationToken)
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(GZipStream));
        }

        if (_grindCoreStream != null)
        {
            await _grindCoreStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await _inputStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }

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

        if (!_firstReadDone && n > 0)
        {
            _firstReadDone = true;
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

        if (!_isEncoder)
        {
            throw new NotSupportedException("Stream is not in write mode.");
        }

        InitializeCompressMode();

        if (_customHeaderMode)
        {
            _crc32!.SlurpBlock(buffer, offset, count);
            _originalSize += count;
            return _rawDeflateStream!.WriteAsync(buffer, offset, count, cancellationToken);
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
            throw new ObjectDisposedException(nameof(GZipStream));
        }

        if (_isEncoder || _grindCoreStream == null)
        {
            return 0;
        }

        var n = await _grindCoreStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);

        if (!_firstReadDone && n > 0)
        {
            _firstReadDone = true;
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

        if (!_isEncoder)
        {
            throw new NotSupportedException("Stream is not in write mode.");
        }

        InitializeCompressMode();

        if (_customHeaderMode)
        {
            var arr = buffer.ToArray();
            _crc32!.SlurpBlock(arr, 0, arr.Length);
            _originalSize += arr.Length;
            return _rawDeflateStream!.WriteAsync(buffer, cancellationToken);
        }

        return _grindCoreStream!.WriteAsync(buffer, cancellationToken);
    }

    public override async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        try
        {
            if (_customHeaderMode && _rawDeflateStream != null)
            {
                _rawDeflateStream.Dispose();
                WriteGzipTrailer();
            }
            else
            {
                _grindCoreStream?.Dispose();
            }

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

// Helper parsing for GZip header metadata when underlying GrindCore does not expose it.
static class GZipHeaderHelper
{
    // Returns (filename, comment, mtime)
    public static (string name, string comment, DateTime? mtime) ParseGzipHeader(Stream stream, Encoding encoding)
    {
        if (stream == null || !stream.CanSeek)
        {
            return (null, null, null);
        }

        long originalPos = stream.Position;
        try
        {
            using var br = new BinaryReader(stream, encoding, leaveOpen: true);
            // ID1 ID2
            var id1 = br.ReadByte();
            var id2 = br.ReadByte();
            if (id1 != 0x1f || id2 != 0x8b)
            {
                return (null, null, null);
            }
            var cm = br.ReadByte(); // compression method
            var flags = br.ReadByte();
            var mtime = br.ReadInt32();
            // skip extra flags and OS
            br.ReadByte();
            br.ReadByte();

            string name = null;
            string comment = null;

            const int FEXTRA = 4;
            const int FNAME = 8;
            const int FCOMMENT = 16;

            if ((flags & FEXTRA) != 0)
            {
                int xlen = br.ReadUInt16();
                br.ReadBytes(xlen);
            }

            if ((flags & FNAME) != 0)
            {
                name = ReadNullTerminatedString(br, encoding);
            }

            if ((flags & FCOMMENT) != 0)
            {
                comment = ReadNullTerminatedString(br, encoding);
            }

            DateTime? dt = null;
            if (mtime != 0)
            {
                dt = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(mtime);
            }

            return (name, comment, dt);
        }
        catch
        {
            return (null, null, null);
        }
        finally
        {
            try
            {
                stream.Position = originalPos;
            }
            catch { }
        }
    }

    private static string ReadNullTerminatedString(BinaryReader br, Encoding encoding)
    {
        using var ms = new MemoryStream();
        while (true)
        {
            var b = br.ReadByte();
            if (b == 0)
            {
                break;
            }
            ms.WriteByte(b);
        }
        return encoding.GetString(ms.ToArray());
    }
}
