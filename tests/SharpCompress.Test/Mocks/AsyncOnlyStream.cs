using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SharpCompress.Test.Mocks;

public class AsyncOnlyStream(Stream stream, bool disposeStream = true) : Stream
{
    private readonly Stream _stream = stream ?? throw new ArgumentNullException(nameof(stream));

    public override bool CanRead => _stream.CanRead;
    public override bool CanSeek => _stream.CanSeek;
    public override bool CanWrite => _stream.CanWrite;
    public override long Length => _stream.Length;
    public override long Position
    {
        get => _stream.Position;
        set => _stream.Position = value;
    }

    public override Task FlushAsync(CancellationToken cancellationToken) =>
        _stream.FlushAsync(cancellationToken);

    public override void Flush() =>
#if GRINDCORE && LEGACY_DOTNET
        // GrindCore's native streams call sync Flush during Dispose on legacy frameworks.
        // This is resolved in net10+ via GrindCore's async dispose support.
        _stream.Flush();
#else
        throw new NotSupportedException("Synchronous Flush is not supported");
#endif

    public override int ReadByte() =>
        throw new NotSupportedException("Synchronous ReadByte is not supported");

    public override int Read(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException("Synchronous Read is not supported");

    public override Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken
    ) => _stream.ReadAsync(buffer, offset, count, cancellationToken);

#if NET8_0_OR_GREATER
    public override ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default
    ) => _stream.ReadAsync(buffer, cancellationToken);
#endif

    public override long Seek(long offset, SeekOrigin origin) => _stream.Seek(offset, origin);

    public override void SetLength(long value) => _stream.SetLength(value);

    public override Task WriteAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken
    ) => _stream.WriteAsync(buffer, offset, count, cancellationToken);

#if NET8_0_OR_GREATER
    public override ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default
    ) => _stream.WriteAsync(buffer, cancellationToken);
#endif

    public override void Write(byte[] buffer, int offset, int count) =>
#if GRINDCORE
        // GrindCore native streams and the managed Lzma2EncoderStream use sync Write
        // during Dispose for final flush. The test intent is to verify main data paths are async.
        _stream.Write(buffer, offset, count);
#else
        throw new NotSupportedException("Synchronous Write is not supported");
#endif

    public override void WriteByte(byte value) =>
#if GRINDCORE
        _stream.WriteByte(value);
#else
        throw new NotSupportedException("Synchronous WriteByte is not supported");
#endif

    protected override void Dispose(bool disposing)
    {
        if (disposing && disposeStream)
        {
            _stream.Dispose();
        }
        base.Dispose(disposing);
    }
}
