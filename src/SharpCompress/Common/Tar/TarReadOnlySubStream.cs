using System;
using System.IO;
using SharpCompress.IO;

namespace SharpCompress.Common.Tar;

internal class TarReadOnlySubStream : SharpCompressStream
{
    private bool _isDisposed;
    private long _amountRead;

    public TarReadOnlySubStream(Stream stream, long bytesToRead)
        : base(stream, leaveOpen: true, throwOnDispose: false) => BytesLeftToRead = bytesToRead;

    protected override void Dispose(bool disposing)
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        if (disposing)
        {
            // Ensure we read all remaining blocks for this entry.
            BaseStream.Skip(BytesLeftToRead);
            _amountRead += BytesLeftToRead;

            // If the last block wasn't a full 512 bytes, skip the remaining padding bytes.
            var bytesInLastBlock = _amountRead % 512;

            if (bytesInLastBlock != 0)
            {
                BaseStream.Skip(512 - bytesInLastBlock);
            }
        }

        base.Dispose(disposing);
    }

    private long BytesLeftToRead { get; set; }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override void Flush() { }

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (BytesLeftToRead < count)
        {
            count = (int)BytesLeftToRead;
        }
        var read = BaseStream.Read(buffer, offset, count);
        if (read > 0)
        {
            BytesLeftToRead -= read;
            _amountRead += read;
        }
        return read;
    }

    public override int ReadByte()
    {
        if (BytesLeftToRead <= 0)
        {
            return -1;
        }
        var value = BaseStream.ReadByte();
        if (value != -1)
        {
            --BytesLeftToRead;
            ++_amountRead;
        }
        return value;
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();
}
