#nullable disable

using System;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices.ComTypes;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using SharpCompress.Common;
using SharpCompress.Compressors;
using SharpCompress.IO;
using SharpCompress.Readers;
using SharpCompress.Writers;

namespace SharpCompress.Compressors.LZMA;

public class LzmaStream : Stream, IStreamStack
{
    Stream IStreamStack.BaseStream() => _inputStream;

    private readonly Stream _inputStream;
    private readonly bool _isLzma2;
    private readonly bool _isEncoder;
    private readonly Nanook.GrindCore.CompressionStream _grindCoreStream;
    private bool _leaveOpen;
    private bool _isDisposed;

    public static LzmaStream Create(
        byte[] properties,
        Stream inputStream,
        bool leaveOpen = false
    ) => Create(properties, inputStream, -1, -1, null, properties.Length < 5, leaveOpen);

    public static LzmaStream Create(
        byte[] properties,
        Stream inputStream,
        long inputSize,
        bool leaveOpen = false
    ) => Create(properties, inputStream, inputSize, -1, null, properties.Length < 5, leaveOpen);

    public static LzmaStream Create(
        byte[] properties,
        Stream inputStream,
        long inputSize,
        long outputSize,
        bool leaveOpen = false
    ) =>
        Create(
            properties,
            inputStream,
            inputSize,
            outputSize,
            null,
            properties.Length < 5,
            leaveOpen
        );

    public static LzmaStream Create(
        byte[] properties,
        Stream inputStream,
        long inputSize,
        long outputSize,
        Stream presetDictionary,
        bool isLzma2,
        bool leaveOpen = false
    )
    {
        var lzma = new LzmaStream(
            properties,
            inputStream,
            inputSize,
            outputSize,
            null,
            isLzma2,
            leaveOpen
        );
        //if (!isLzma2)
        //{
        //    if (presetDictionary != null)
        //    {
        //        lzma._outWindow.Train(presetDictionary);
        //    }

        //    lzma._rangeDecoder.Init(inputStream);
        //}
        //else
        //{
        //    if (presetDictionary != null)
        //    {
        //        lzma._outWindow.Train(presetDictionary);
        //        lzma._needDictReset = false;
        //    }
        //}
        return lzma;
    }

    public static LzmaStream Create(
        LzmaEncoderProperties properties,
        bool isLzma2,
        Stream outputStream
    ) => Create(properties, isLzma2, null, outputStream);

    public static LzmaStream Create(
        LzmaEncoderProperties properties,
        bool isLzma2,
        Stream presetDictionary,
        Stream outputStream
    )
    {
        var lzma = new LzmaStream(properties, isLzma2, outputStream, true);

        //lzma._encoder!.SetStreams(null, outputStream, -1, -1);

        //if (presetDictionary != null)
        //{
        //    lzma._encoder.Train(presetDictionary);
        //}
        return lzma;
    }

    public static ValueTask<LzmaStream> CreateAsync(
        byte[] properties,
        Stream inputStream,
        long inputSize,
        long outputSize,
        bool leaveOpen = false
    ) =>
        CreateAsync(
            properties,
            inputStream,
            inputSize,
            outputSize,
            null,
            properties.Length < 5,
            leaveOpen
        );

    public static async ValueTask<LzmaStream> CreateAsync(
        byte[] properties,
        Stream inputStream,
        long inputSize,
        long outputSize,
        Stream presetDictionary,
        bool isLzma2,
        bool leaveOpen = false
    )
    {
        var lzma = new LzmaStream(
            properties,
            inputStream,
            inputSize,
            outputSize,
            null,
            isLzma2,
            leaveOpen
        );
        //if (!isLzma2)
        //{
        //    if (presetDictionary != null)
        //    {
        //        await lzma._outWindow.TrainAsync(presetDictionary);
        //    }

        //    await lzma._rangeDecoder.InitAsync(inputStream);
        //}
        //else
        //{
        //    if (presetDictionary != null)
        //    {
        //        await lzma._outWindow.TrainAsync(presetDictionary);
        //        lzma._needDictReset = false;
        //    }
        //}
        return lzma;
    }

    public LzmaStream(byte[] properties, Stream inputStream, bool leaveOpen = false)
        : this(properties, inputStream, -1, -1, null, properties.Length < 5, leaveOpen) { }

    public LzmaStream(byte[] properties, Stream inputStream, long inputSize, bool leaveOpen = false)
        : this(properties, inputStream, inputSize, -1, null, properties.Length < 5, leaveOpen) { }

    public LzmaStream(
        byte[] properties,
        Stream inputStream,
        long inputSize,
        long outputSize,
        bool leaveOpen = false
    )
        : this(
            properties,
            inputStream,
            inputSize,
            outputSize,
            null,
            properties.Length < 5,
            leaveOpen
        ) { }

    public LzmaStream(
        byte[] properties,
        Stream inputStream,
        long inputSize,
        long outputSize,
        Stream presetDictionary,
        bool isLzma2,
        bool leaveOpen = false,
        Readers.ReaderOptions readerOptions = null
    )
    {
        _inputStream = inputStream;
        _isLzma2 = isLzma2;
        _isEncoder = false;
        _leaveOpen = leaveOpen;

#if DEBUG_STREAMS
        this.DebugConstruct(typeof(LzmaStream));
#endif

        var options = new Nanook.GrindCore.CompressionOptions()
        {
            Type = Nanook.GrindCore.CompressionType.Decompress,
            InitProperties = properties,
            LeaveOpen = _leaveOpen,
            BufferSize = 0x200000,
        };

        // Apply buffer size options using the helper
        GrindCoreBufferHelper.ApplyBufferSizeOptions(options, this, false, null, readerOptions);

        if (inputSize >= 0)
        {
            options.PositionLimit = inputSize;
        }

        long length = 0;
        try
        {
            length = _inputStream.Length;
        }
        catch { }
        if (
            inputSize == -1
            && _inputStream is IO.BufferedSubStream
            && length != 0
        )
        {
            options.PositionLimit = length;
        }
        else if (outputSize >= 0)
        {
            options.PositionFullSizeLimit = outputSize;
        }

        // Configure async-only mode if base stream requires it
        GrindCoreBufferHelper.ConfigureAsyncOnlyIfNeeded(options, inputStream);

        // Create the appropriate GrindCore stream - use helper to determine LZMA2
        var shouldUseLzma2 = GrindCoreBufferHelper.IsLzma2(null, _isLzma2);
        if (shouldUseLzma2)
        {
            _grindCoreStream = new Nanook.GrindCore.Lzma.Lzma2Stream(inputStream, options);
        }
        else
        {
            _grindCoreStream = new Nanook.GrindCore.Lzma.LzmaStream(inputStream, options);
        }

        Properties = properties;
    }

    public LzmaStream(
        LzmaEncoderProperties properties,
        bool isLzma2,
        Stream outputStream,
        bool leaveOpen = false,
        WriterOptions writerOptions = null
    )
        : this(properties, isLzma2, null, outputStream, leaveOpen, writerOptions) { }

    public LzmaStream(
        LzmaEncoderProperties properties,
        bool isLzma2,
        Stream presetDictionary,
        Stream outputStream,
        bool leaveOpen = false,
        WriterOptions writerOptions = null
    )
    {
        _inputStream = outputStream;
        _isLzma2 = isLzma2;
        _isEncoder = true;
        _leaveOpen = leaveOpen;

#if DEBUG_STREAMS
        this.DebugConstruct(typeof(LzmaStream));
#endif

        var options = new Nanook.GrindCore.CompressionOptions
        {
            Type = (Nanook.GrindCore.CompressionType)(writerOptions?.CompressionLevel ?? 9), //Convert LzmaEncoderProperties to GrindCore compression level
            LeaveOpen = _leaveOpen,
        };

        bool useLzma2 = GrindCoreBufferHelper.IsLzma2(writerOptions?.CompressionType, _isLzma2);

        // Apply buffer size options using the helper and get compression buffer size for extensions
        var compressionBufferSize = GrindCoreBufferHelper.ApplyBufferSizeOptions(
            options,
            this,
            true,
            writerOptions,
            null
        );

        // Apply LZMA2-specific extensions (block size setting) - check both enum and boolean
        GrindCoreBufferHelper.ApplyCompressionBufferSizeExtensions(
            options,
            compressionBufferSize,
            writerOptions?.CompressionType,
            useLzma2
        );

        // Configure async-only mode if base stream requires it
        GrindCoreBufferHelper.ConfigureAsyncOnlyIfNeeded(options, outputStream);

        // Create the appropriate GrindCore stream - use helper to determine LZMA2
        if (useLzma2)
        {
            // options.BufferSize = 4 * 1024 * 1024;
            _grindCoreStream = new Nanook.GrindCore.Lzma.Lzma2Stream(outputStream, options);
        }
        else
        {
            _grindCoreStream = new Nanook.GrindCore.Lzma.LzmaStream(outputStream, options);
        }

        Properties = _grindCoreStream.Properties;
    }

    public override bool CanRead => !_isEncoder && _grindCoreStream?.CanRead == true;

    public override bool CanSeek => false;

    public override bool CanWrite => _isEncoder && _grindCoreStream?.CanWrite == true;

    public override void Flush()
    {
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
        this.DebugDispose(typeof(LzmaStream));
#endif

        if (disposing)
        {
            _grindCoreStream?.Dispose();
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
        if (_isEncoder || _grindCoreStream == null)
        {
            return 0;
        }

        return _grindCoreStream.Read(buffer, offset, count);
    }

    public override int ReadByte()
    {
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
        if (!_isEncoder || _grindCoreStream == null)
        {
            throw new NotSupportedException("Stream is not in write mode.");
        }

        _grindCoreStream.Write(buffer, offset, count);
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
