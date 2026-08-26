using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SharpCompress.Common;
using SharpCompress.Compressors;
using SharpCompress.Compressors.BZip2;

namespace SharpCompress.Providers.Default;

/// <summary>
/// Provides BZip2 compression using SharpCompress's internal implementation.
/// </summary>
public sealed class BZip2CompressionProvider : CompressionProviderBase
{
    public override CompressionType CompressionType => CompressionType.BZip2;
    public override bool SupportsCompression => true;
    public override bool SupportsDecompression => true;

    public override Stream CreateCompressStream(Stream destination, int compressionLevel)
    {
        // BZip2 doesn't use compressionLevel parameter in this implementation
        return BZip2Stream.Create(destination, CompressionMode.Compress, false);
    }

    public override async ValueTask<Stream> CreateCompressStreamAsync(
        Stream destination,
        int compressionLevel,
        CancellationToken cancellationToken = default
    )
    {
        // BZip2 doesn't use compressionLevel parameter in this implementation
        return await BZip2Stream
            .CreateAsync(
                destination,
                CompressionMode.Compress,
                false,
                false,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
    }

    public override Stream CreateDecompressStream(Stream source)
    {
        // Enable tolerance in all decompression contexts because GrindCore's buffer
        // may overread past the bzip2 stream end into non-bzip2 data. The caller's
        // rewind mechanism (IStreamStack.Flush) corrects the base stream position.
        return BZip2Stream.Create(
            source,
            CompressionMode.Decompress,
            false,
            leaveOpen: false,
            tolerateTruncatedStream: true
        );
    }

    public override Stream CreateDecompressStream(Stream source, CompressionContext context)
    {
        // When InputSize is known (e.g., from a Zip entry header), pass it so GrindCore
        // limits reads from the base stream to the exact compressed entry size.
        // Always enable tolerance in archive context because buffer overread past the
        // entry boundary into non-bzip2 archive structure is expected.
        if (context.InputSize > 0)
        {
            return BZip2Stream.Create(
                source,
                CompressionMode.Decompress,
                false,
                leaveOpen: false,
                tolerateTruncatedStream: true,
                inputSize: context.InputSize
            );
        }

        return BZip2Stream.Create(
            source,
            CompressionMode.Decompress,
            false,
            leaveOpen: false,
            tolerateTruncatedStream: true
        );
    }

    public override async ValueTask<Stream> CreateDecompressStreamAsync(
        Stream source,
        CancellationToken cancellationToken = default
    )
    {
        return await BZip2Stream
            .CreateAsync(
                source,
                CompressionMode.Decompress,
                false,
                leaveOpen: false,
                tolerateTruncatedStream: true,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
    }

    public override async ValueTask<Stream> CreateDecompressStreamAsync(
        Stream source,
        CompressionContext context,
        CancellationToken cancellationToken = default
    )
    {
        return await BZip2Stream
            .CreateAsync(
                source,
                CompressionMode.Decompress,
                false,
                leaveOpen: false,
                tolerateTruncatedStream: true,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
    }
}
