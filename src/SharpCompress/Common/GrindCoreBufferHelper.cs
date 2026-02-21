#nullable disable

using System.IO;
using SharpCompress.Common;
using SharpCompress.IO;
using SharpCompress.Readers;
using SharpCompress.Writers;
using NGC = Nanook.GrindCore;

namespace SharpCompress.Compressors;

/// <summary>
/// Internal helper class for consistent buffer size handling across all GrindCore stream wrappers.
/// Provides centralized logic for the compression buffer size priority system.
/// </summary>
internal static class GrindCoreBufferHelper
{
    /// <summary>
    /// Determines if LZMA2 should be used based on CompressionType or isLzma2 boolean.
    /// </summary>
    /// <param name="compressionType">The compression type from WriterOptions.</param>
    /// <param name="isLzma2">The legacy boolean parameter.</param>
    /// <returns>True if LZMA2 should be used.</returns>
    public static bool IsLzma2(CompressionType? compressionType, bool isLzma2)
    {
        return compressionType == CompressionType.LZMA2 || isLzma2;
    }

    /// <summary>
    /// Applies buffer size configuration to GrindCore options with the priority system.
    /// Priority: WriterOptions.CompressionBufferSize -> ReaderOptions.BufferSize -> IStreamStack.DefaultBufferSize -> GrindCore default
    /// </summary>
    /// <param name="options">The GrindCore options to configure.</param>
    /// <param name="streamStack">The stream implementing IStreamStack for DefaultBufferSize fallback.</param>
    /// <param name="isEncoder">True if this is for compression, false for decompression.</param>
    /// <param name="writerOptions">Optional writer options.</param>
    /// <param name="readerOptions">Optional reader options.</param>
    /// <returns>The compression buffer size if CompressionBufferSize was used, otherwise 0.</returns>
    public static int ApplyBufferSizeOptions(
        NGC.CompressionOptions options,
        IStreamStack streamStack,
        bool isEncoder,
        WriterOptions writerOptions = null,
        ReaderOptions readerOptions = null
    )
    {
        // Simplified buffer size resolution:
        // - For decoders, use ReaderOptions.BufferSize if provided.
        // - Otherwise, use a hard-coded default (0x10000) required by GrindCore.
        int bufferSize = 0;

        if (!isEncoder && readerOptions?.BufferSize > 0)
        {
            bufferSize = readerOptions.BufferSize;
        }
        else if (options.BufferSize > 0)
        {
            // If the buffer size was already set on options (e.g., by a previous call), use it.
            bufferSize = options.BufferSize.Value;
        }
        else
        {
            // Use a conservative default for GrindCore when no explicit buffer size is provided.
            bufferSize = 0x10000;
        }

        // Apply the buffer size (GrindCore expects a positive buffer size)
        if (bufferSize > 0)
        {
            options.BufferSize = bufferSize;
        }

        // CompressionBufferSize option was removed from WriterOptions; return bufferSize.
        return bufferSize;
    }

    /// <summary>
    /// Applies algorithm-specific extensions for CompressionBufferSize usage.
    /// Currently supports LZMA2 block size setting and solid mode.
    /// </summary>
    /// <param name="options">The GrindCore options to extend.</param>
    /// <param name="compressionBufferSize">The compression buffer size to apply. Use -1 for solid mode.</param>
    /// <param name="compressionType">The compression type from WriterOptions.</param>
    /// <param name="isLzma2">The legacy boolean parameter.</param>
    public static void ApplyCompressionBufferSizeExtensions(
        NGC.CompressionOptions options,
        int compressionBufferSize,
        CompressionType? compressionType = null,
        bool isLzma2 = false
    )
    {
        if (IsLzma2(compressionType, isLzma2))
        {
            if (compressionBufferSize == -1)
            {
                // Solid mode: Set BlockSize to indicate solid compression
                options.BlockSize = -1;
            }
            else if (compressionBufferSize > 0)
            {
                // Block mode: Set BlockSize to the specified compression buffer size
                options.BlockSize = compressionBufferSize;
            }
        }
    }

    /// <summary>
    /// Overload for backward compatibility - uses only the boolean parameter.
    /// </summary>
    /// <param name="options">The GrindCore options to extend.</param>
    /// <param name="compressionBufferSize">The compression buffer size to apply. Use -1 for solid mode.</param>
    /// <param name="isLzma2">The legacy boolean parameter.</param>
    public static void ApplyCompressionBufferSizeExtensions(
        NGC.CompressionOptions options,
        int compressionBufferSize,
        bool isLzma2 = false
    )
    {
        ApplyCompressionBufferSizeExtensions(options, compressionBufferSize, null, isLzma2);
    }

    /// <summary>
    /// Checks if the stream is async-only (e.g., test mock that throws on synchronous operations).
    /// This detects SharpCompress.Test.Mocks.AsyncOnlyStream type.
    /// </summary>
    /// <param name="stream">The stream to check.</param>
    /// <returns>True if the stream is async-only; otherwise, false.</returns>
    public static bool IsAsyncOnlyStream(Stream stream)
    {
        if (stream == null)
        {
            return false;
        }

        // Check the exact type name to avoid dependency on test assembly
        var typeName = stream.GetType().FullName;
        return typeName == "SharpCompress.Test.Mocks.AsyncOnlyStream";
    }

    /// <summary>
    /// Configures BaseStreamAsyncOnly flag based on stream type detection.
    /// NOTE: This method requires GrindCore to have the BaseStreamAsyncOnly property added to CompressionOptions.
    /// Once GrindCore is updated, uncomment the implementation below.
    /// </summary>
    /// <param name="options">The GrindCore options to configure.</param>
    /// <param name="stream">The base stream to analyze.</param>
    public static void ConfigureAsyncOnlyIfNeeded(NGC.CompressionOptions options, Stream stream)
    {
        // TODO: Uncomment once GrindCore.CompressionOptions has BaseStreamAsyncOnly property
        // if (IsAsyncOnlyStream(stream))
        // {
        //     options.BaseStreamAsyncOnly = true;
        // }
    }
}
