using System;
using System.IO;

namespace SharpCompress.IO;

public class SharpCompressStream : Stream
{
    protected Stream BaseStream { get; }
    private MemoryStream _bufferStream = new();
    private bool _isRewound;
    private bool _isDisposed;

    public bool ThrowOnDispose { get; set; }
    public bool LeaveOpen { get; set; }

    public static SharpCompressStream Create(Stream stream, bool leaveOpen = false, bool throwOnDispose = false)
    {
        if (stream is SharpCompressStream sc)
        {
            sc.LeaveOpen = true;
            sc.ThrowOnDispose = throwOnDispose;
            return sc;
        }
        return new SharpCompressStream(stream, leaveOpen, throwOnDispose);
    }

    protected SharpCompressStream(Stream stream, bool leaveOpen = false, bool throwOnDispose = false)
    {
        this.BaseStream = stream;
        this.LeaveOpen = leaveOpen;
        this.ThrowOnDispose = throwOnDispose;
        this.BaseStream = stream;
    }

    internal bool IsRecording { get; private set; }

    protected override void Dispose(bool disposing)
    {
        if (ThrowOnDispose)
        {
            throw new InvalidOperationException(
                $"Attempt to dispose of a {nameof(SharpCompressStream)} when {nameof(ThrowOnDispose)} is {ThrowOnDispose}"
            );
        }
        if (_isDisposed)
        {
            return;
        }
        _isDisposed = true;
        base.Dispose(disposing);
        if (disposing && !this.LeaveOpen)
        {
            BaseStream.Dispose();
        }

    }

    public void Rewind(bool stopRecording)
    {
        _isRewound = true;
        IsRecording = !stopRecording;
        _bufferStream.Position = 0;
    }

    public void Rewind(MemoryStream buffer)
    {
        if (_bufferStream.Position >= buffer.Length)
        {
            _bufferStream.Position -= buffer.Length;
        }
        else
        {
            _bufferStream.TransferTo(buffer);
            //create new memorystream to allow proper resizing as memorystream could be a user provided buffer
            //https://github.com/adamhathcock/sharpcompress/issues/306
            _bufferStream = new MemoryStream();
            buffer.Position = 0;
            buffer.TransferTo(_bufferStream);
            _bufferStream.Position = 0;
        }
        _isRewound = true;
    }

    public void StartRecording()
    {
        //if (isRewound && bufferStream.Position != 0)
        //   throw new System.NotImplementedException();
        if (_bufferStream.Position != 0)
        {
            var data = _bufferStream.ToArray();
            var position = _bufferStream.Position;
            _bufferStream.SetLength(0);
            _bufferStream.Write(data, (int)position, data.Length - (int)position);
            _bufferStream.Position = 0;
        }
        IsRecording = true;
    }

    public override bool CanRead => BaseStream.CanRead;

    public override bool CanSeek => BaseStream.CanSeek;

    public override bool CanWrite => BaseStream.CanWrite;

    public override void Flush() => BaseStream.Flush();

    public override long Length => BaseStream.Length;


    public override long Position
    {
        get => BaseStream.Position + _bufferStream.Position - _bufferStream.Length;
        set
        {
            if (!_isRewound)
            {
                BaseStream.Position = value;
            }
            else if (value < BaseStream.Position - _bufferStream.Length || value >= BaseStream.Position)
            {
                BaseStream.Position = value;
                _isRewound = false;
                _bufferStream.SetLength(0);
            }
            else
            {
                _bufferStream.Position = value - BaseStream.Position + _bufferStream.Length;
            }
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        //don't actually read if we don't really want to read anything
        //currently a network stream bug on Windows for .NET Core
        if (count == 0)
        {
            return 0;
        }
        int read;
        if (_isRewound && _bufferStream.Position != _bufferStream.Length)
        {
            // don't read more than left
            var readCount = Math.Min(count, (int)(_bufferStream.Length - _bufferStream.Position));
            read = _bufferStream.Read(buffer, offset, readCount);
            if (read < readCount)
            {
                var tempRead = BaseStream.Read(buffer, offset + read, count - read);
                if (IsRecording)
                {
                    _bufferStream.Write(buffer, offset + read, tempRead);
                }
                read += tempRead;
            }
            if (_bufferStream.Position == _bufferStream.Length && !IsRecording)
            {
                _isRewound = false;
                _bufferStream.SetLength(0);
            }
            return read;
        }

        read = BaseStream.Read(buffer, offset, count);
        if (IsRecording)
        {
            _bufferStream.Write(buffer, offset, read);
        }
        return read;
    }

    public override long Seek(long offset, SeekOrigin origin) => BaseStream.Seek(offset, origin);

    public override void SetLength(long value) => BaseStream.SetLength(value);

    public override void Write(byte[] buffer, int offset, int count) =>
        BaseStream.Write(buffer, offset, count);

#if !NETFRAMEWORK && !NETSTANDARD2_0

    public override int Read(Span<byte> buffer) => BaseStream.Read(buffer);

    public override void Write(ReadOnlySpan<byte> buffer) => BaseStream.Write(buffer);

#endif
}
