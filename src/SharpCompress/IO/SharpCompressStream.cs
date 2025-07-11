using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace SharpCompress.IO;

/// <summary>
/// A stream wrapper that optionally provides fixed-size buffering and rewind support,
/// as well as stackable stream composition via <see cref="IStreamStack"/>.
/// When buffering is enabled, reads are satisfied from an internal buffer of the specified size,
/// and seeking within the buffer is supported. If buffering is not enabled, the stream acts as a transparent wrapper.
/// </summary>
public class SharpCompressStream : Stream, IStreamStack
{
#if DEBUG_STREAMS
    /// <inheritdoc/>
    long IStreamStack.InstanceId { get; set; }
#endif

    /// <inheritdoc/>
    Stream IStreamStack.BaseStream() => Stream;

    // Buffering fields
    private int _bufferSize;
    private byte[]? _buffer;
    private int _bufferPosition;
    private int _bufferedLength;
    private bool _bufferingEnabled;
    private long _baseInitialPos;

    /// <inheritdoc/>
    /// <remarks>
    /// Setting this property will allocate a new buffer and enable buffering if the value is greater than zero.
    /// If set to zero, buffering is disabled.
    /// </remarks>
    int IStreamStack.BufferSize
    {
        get => _bufferingEnabled ? _bufferSize : 0;
        set
        {
            if (_bufferSize != value)
            {
                _bufferSize = value;
                _bufferingEnabled = _bufferSize > 0;
                if (_bufferingEnabled)
                {
                    _buffer = new byte[_bufferSize];
                    _bufferPosition = 0;
                    _bufferedLength = 0;
                    try
                    {
                        _internalPosition = Stream.Position;
                    }
                    catch
                    {
                        _internalPosition = 0;
                    }
                }
            }
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Gets or sets the current position within the buffer if buffering is enabled.
    /// Throws <see cref="ArgumentOutOfRangeException"/> if set outside the valid range.
    /// </remarks>
    int IStreamStack.BufferPosition
    {
        get => _bufferingEnabled ? _bufferPosition : 0;
        set
        {
            if (_bufferingEnabled)
            {
                if (value < 0 || value > _bufferedLength)
                    throw new ArgumentOutOfRangeException(nameof(value));
                _internalPosition = value;
                _bufferPosition = value;
            }
        }
    }

    /// <summary>
    /// The immediate underlying stream.
    /// </summary>
    public Stream Stream { get; }

    private bool _readOnly; // Some archive detection requires seek to be disabled to cause it to exception to try the next arc type
    private bool _isDisposed;
    private long _internalPosition = 0;

    /// <summary>
    /// If true, throws an exception on dispose instead of disposing the underlying stream.
    /// </summary>
    public bool ThrowOnDispose { get; set; }

    /// <summary>
    /// If true, leaves the underlying stream open when this stream is disposed.
    /// </summary>
    public bool LeaveOpen { get; set; }

    /// <summary>
    /// Gets the logical position within this stream, including any buffer offset.
    /// </summary>
    public long InternalPosition => _internalPosition;

    /// <summary>
    /// Creates a <see cref="SharpCompressStream"/> wrapper, optionally enabling buffering.
    /// If the input stream is already a <see cref="SharpCompressStream"/>, updates its buffer if requested.
    /// </summary>
    /// <param name="stream">The underlying stream to wrap.</param>
    /// <param name="leaveOpen">Whether to leave the underlying stream open on dispose.</param>
    /// <param name="throwOnDispose">Whether to throw on dispose instead of disposing the underlying stream.</param>
    /// <param name="bufferSize">The buffer size to use (0 disables buffering).</param>
    /// <param name="forceBuffer">If true, forces the buffer size to be set even if already set.</param>
    /// <returns>A <see cref="SharpCompressStream"/> instance.</returns>
    public static SharpCompressStream Create(Stream stream, bool leaveOpen = false, bool throwOnDispose = false, int bufferSize = 0, bool forceBuffer = false)
    {
        if (
            stream is SharpCompressStream sc
            && sc.LeaveOpen == leaveOpen
            && sc.ThrowOnDispose == throwOnDispose
        )
        {
            if (bufferSize != 0)
                ((IStreamStack)stream).SetBuffer(bufferSize, forceBuffer);
            return sc;
        }
        return new SharpCompressStream(stream, leaveOpen, throwOnDispose, bufferSize, forceBuffer);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SharpCompressStream"/> class.
    /// </summary>
    /// <param name="stream">The underlying stream to wrap.</param>
    /// <param name="leaveOpen">Whether to leave the underlying stream open on dispose.</param>
    /// <param name="throwOnDispose">Whether to throw on dispose instead of disposing the underlying stream.</param>
    /// <param name="bufferSize">The buffer size to use (0 disables buffering).</param>
    /// <param name="forceBuffer">If true, forces the buffer size to be set even if already set.</param>
    public SharpCompressStream(Stream stream, bool leaveOpen = false, bool throwOnDispose = false, int bufferSize = 0, bool forceBuffer = false)
    {
        Stream = stream;
        this.LeaveOpen = leaveOpen;
        this.ThrowOnDispose = throwOnDispose;
        _readOnly = !Stream.CanSeek;

        ((IStreamStack)this).SetBuffer(bufferSize, forceBuffer);
        try
        {
            _baseInitialPos = stream.Position;
        }
        catch
        {
            _baseInitialPos = 0;
        }

#if DEBUG_STREAMS
        this.DebugConstruct(typeof(SharpCompressStream));
#endif
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
#if DEBUG_STREAMS
        this.DebugDispose(typeof(SharpCompressStream));
#endif
        if (_isDisposed)
        {
            return;
        }
        _isDisposed = true;
        base.Dispose(disposing);

        if (this.LeaveOpen)
        {
            return;
        }
        if (ThrowOnDispose)
        {
            throw new InvalidOperationException(
                $"Attempt to dispose of a {nameof(SharpCompressStream)} when {nameof(ThrowOnDispose)} is {ThrowOnDispose}"
            );
        }
        if (disposing)
        {
            Stream.Dispose();
        }
    }

    /// <inheritdoc/>
    public override bool CanRead => Stream.CanRead;

    /// <inheritdoc/>
    public override bool CanSeek => !_readOnly && Stream.CanSeek;

    /// <inheritdoc/>
    public override bool CanWrite => !_readOnly && Stream.CanWrite;

    /// <inheritdoc/>
    public override void Flush()
    {
        Stream.Flush();
    }

    /// <inheritdoc/>
    public override long Length
    {
        get
        {
            return Stream.Length;
        }
    }

    /// <inheritdoc/>
    public override long Position
    {
        get => _internalPosition;
        set
        {
            Seek(value, SeekOrigin.Begin);
        }
    }

    /// <summary>
    /// Reads data from the stream, using the buffer if enabled.
    /// If the buffer is exhausted, it is refilled from the underlying stream.
    /// </summary>
    /// <param name="buffer">The buffer to read data into.</param>
    /// <param name="offset">The offset in the buffer at which to begin storing data.</param>
    /// <param name="count">The maximum number of bytes to read.</param>
    /// <returns>The number of bytes read.</returns>
    public override int Read(byte[] buffer, int offset, int count)
    {
        if (count == 0)
            return 0;

        if (_bufferingEnabled)
        {
            // Fill buffer if needed
            if (_bufferedLength == 0)
            {
                _bufferedLength = Stream.Read(_buffer!, 0, _bufferSize);
                _bufferPosition = 0;
            }
            int available = _bufferedLength - _bufferPosition;
            int toRead = Math.Min(count, available);
            if (toRead > 0)
            {
                Array.Copy(_buffer!, _bufferPosition, buffer, offset, toRead);
                _bufferPosition += toRead;
#if DEBUG_STREAMS
                //Debug.WriteLine($"[SharpCompressStream#{((IStreamStack)this).InstanceId}] _internalPosition=0x{_internalPosition:x} - Read=0x{toRead:x} - BufferPos=0x{_bufferPosition:x} - BufferLength=0x{_bufferedLength:x}");
#endif
                _internalPosition += toRead;
                return toRead;
            }
            // If buffer exhausted, refill
            int r = Stream.Read(_buffer!, 0, _bufferSize);
            if (r == 0)
                return 0;
            _bufferedLength = r;
            _bufferPosition = 0;
            if (_bufferedLength == 0)
            {
#if DEBUG_STREAMS
                //Debug.WriteLine($"[SharpCompressStream#{((IStreamStack)this).InstanceId}] _internalPosition=0x{_internalPosition:x} - Read=0x{0:x} - BufferPos=0x{_bufferPosition:x} - BufferLength=0x{_bufferedLength:x}");
#endif
                return 0;
            }
            toRead = Math.Min(count, _bufferedLength);
            Array.Copy(_buffer!, 0, buffer, offset, toRead);
            _bufferPosition = toRead;
#if DEBUG_STREAMS
            //Debug.WriteLine($"[SharpCompressStream#{((IStreamStack)this).InstanceId}] _internalPosition=0x{_internalPosition:x} - Read=0x{toRead:x} - BufferPos=0x{_bufferPosition:x} - BufferLength=0x{_bufferedLength:x}");
#endif
            _internalPosition += toRead;
            return toRead;
        }
        else
        {
            int read = Stream.Read(buffer, offset, count);
            _internalPosition += read;
            return read;
        }
    }

    /// <summary>
    /// Seeks to a position in the stream, using the buffer if the target is within the buffered range.
    /// If the target is outside the buffer, the underlying stream is repositioned and the buffer is reset.
    /// </summary>
    /// <param name="offset">The offset to seek to.</param>
    /// <param name="origin">The reference point for the offset.</param>
    /// <returns>The new position within the stream.</returns>
    public override long Seek(long offset, SeekOrigin origin)
    {
        long orig = _internalPosition;
        long targetPos;
        // Calculate the absolute target position based on origin
        switch (origin)
        {
            case SeekOrigin.Begin:
                targetPos = offset;
                break;
            case SeekOrigin.Current:
                targetPos = _internalPosition + offset;
                break;
            case SeekOrigin.End:
                targetPos = this.Length + offset;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(origin), origin, null);
        }

        long bufferPos = _internalPosition - _bufferPosition;

        if (targetPos >= bufferPos && targetPos < bufferPos + _bufferedLength)
        {
            _bufferPosition = (int)(targetPos - bufferPos); // repoint within the buffer
            _internalPosition = targetPos;
        }
        else
        {
            long newStreamPos = Stream.Seek(targetPos + _baseInitialPos, SeekOrigin.Begin) - _baseInitialPos;
            _internalPosition = newStreamPos;
            _bufferPosition = 0;
            _bufferedLength = 0;
        }

#if DEBUG_STREAMS
        Debug.WriteLine($"[SharpCompressStream#{((IStreamStack)this).InstanceId}] SEEK from 0x{orig:x} to 0x{_internalPosition:x}");
#endif
        return _internalPosition;
    }

    /// <inheritdoc/>
    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc/>
    public override void WriteByte(byte value)
    {
        Stream.WriteByte(value);
        ++_internalPosition;
    }

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count)
    {
        Stream.Write(buffer, offset, count);
        _internalPosition += count;
    }

#if !NETFRAMEWORK && !NETSTANDARD2_0

    //public override int Read(Span<byte> buffer)
    //{
    //    int bytesRead = Stream.Read(buffer);
    //    _internalPosition += bytesRead;
    //    return bytesRead;
    //}

    //public override void Write(ReadOnlySpan<byte> buffer)
    //{
    //    Stream.Write(buffer);
    //    _internalPosition += buffer.Length;
    //}

#endif

}
