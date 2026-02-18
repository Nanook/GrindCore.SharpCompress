using System;
using System.IO;
using Nanook.GrindCore;
using SharpCompress.Compressors;
using SharpCompress.IO;
using GrindCoreZStdStream = Nanook.GrindCore.ZStd.ZStdStream;

namespace SharpCompress.Compressors.ZStandard;

/// <summary>
/// Lightweight wrapper for GrindCore ZStandard Stream that implements SharpCompress interfaces
/// </summary>
internal partial class ZStandardStream : Stream, IStreamStack
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
}
