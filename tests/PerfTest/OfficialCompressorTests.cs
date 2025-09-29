using System;
using System.Diagnostics;
using System.IO;
using K4os.Compression.LZ4.Streams;
using SharpCompress.Compressors;
using SharpCompress.Compressors.Deflate;
using SharpCompress.Compressors.LZMA;
using ZstdSharp;

namespace PerfTest;

internal static class OfficialCompressorTests
{
    public static CompressionResult RunZipDeflate(byte[] input, string entryName, int level)
    {
        var result = new CompressionResult
        {
            Implementation = "Official",
            Algorithm = "Deflate",
            Level = level,
            OriginalSizeBytes = input.Length
        };

        using var compressedStream = new MemoryStream();
        var sw = Stopwatch.StartNew();

        // wrap underlying stream so disposing the deflate stream doesn't close it
        using (var def = new DeflateStream(new NonClosingStream(compressedStream), CompressionMode.Compress, (CompressionLevel)ClampLevel(level, 0, 9)))
        {
            def.Write(input, 0, input.Length);
            def.Flush();
        }

        sw.Stop();
        result.CompressedSizeBytes = compressedStream.Length;
        result.ElapsedMilliseconds = sw.Elapsed.TotalMilliseconds;

        CompressionResult.PrintResultCompact(result);
        return result;
    }

    public static CompressionResult RunLzma(byte[] input, int level, bool isLzma2)
    {
        var result = new CompressionResult
        {
            Implementation = "Official",
            Algorithm = isLzma2 ? "LZMA2" : "LZMA",
            Level = level,
            OriginalSizeBytes = input.Length
        };

        using var compressedStream = new MemoryStream();
        var sw = Stopwatch.StartNew();

        var props = new LzmaEncoderProperties();
        using (var encoder = new LzmaStream(props, isLzma2, new NonClosingStream(compressedStream)))
        {
            encoder.Write(input, 0, input.Length);
            encoder.Flush();
        }

        sw.Stop();
        result.CompressedSizeBytes = compressedStream.Length;
        result.ElapsedMilliseconds = sw.Elapsed.TotalMilliseconds;

        CompressionResult.PrintResultCompact(result);
        return result;
    }

    public static CompressionResult RunZstd(byte[] input, int level)
    {
        var result = new CompressionResult
        {
            Implementation = "Official",
            Algorithm = "ZSTD",
            Level = level,
            OriginalSizeBytes = input.Length
        };

        using var compressedStream = new MemoryStream();
        var sw = Stopwatch.StartNew();

        // Use ZstdSharp CompressionStream with leaveOpen behaviour by wrapping
        using (var z = new CompressionStream(new NonClosingStream(compressedStream), level))
        {
            z.Write(input, 0, input.Length);
            z.Flush();
        }

        sw.Stop();
        result.CompressedSizeBytes = compressedStream.Length;
        result.ElapsedMilliseconds = sw.Elapsed.TotalMilliseconds;

        CompressionResult.PrintResultCompact(result);
        return result;
    }

    public static CompressionResult RunLz4(byte[] input, int level)
    {
        var result = new CompressionResult
        {
            Implementation = "Official",
            Algorithm = "LZ4",
            Level = level,
            OriginalSizeBytes = input.Length
        };

        using var compressedStream = new MemoryStream();
        var sw = Stopwatch.StartNew();

        var lvl = (K4os.Compression.LZ4.LZ4Level)ClampLevel(level, 0, 12);
        using (var lz4 = LZ4Stream.Encode(compressedStream, lvl, leaveOpen: true))
        {
            lz4.Write(input, 0, input.Length);
            lz4.Flush();
        } // LZ4Stream is disposed here, but compressedStream remains open

        sw.Stop();
        result.CompressedSizeBytes = compressedStream.Length;
        result.ElapsedMilliseconds = sw.Elapsed.TotalMilliseconds;

        CompressionResult.PrintResultCompact(result);
        return result;
    }

    public static CompressionResult RunGZip(byte[] input, string entryName, int level)
    {
        var result = new CompressionResult
        {
            Implementation = "Official",
            Algorithm = "GZip Deflate",
            Level = level,
            OriginalSizeBytes = input.Length
        };

        using var compressedStream = new MemoryStream();
        var sw = Stopwatch.StartNew();

        // Use SharpCompress GZipStream with NonClosingStream wrapper
        using (var gzip = new GZipStream(
                    new NonClosingStream(compressedStream), 
                    CompressionMode.Compress, 
                    (CompressionLevel)ClampLevel(level, 0, 9)))
        {
            gzip.Write(input, 0, input.Length);
            gzip.Flush();
        }

        sw.Stop();
        result.CompressedSizeBytes = compressedStream.Length;
        result.ElapsedMilliseconds = sw.Elapsed.TotalMilliseconds;

        CompressionResult.PrintResultCompact(result);
        return result;
    }

    /// <summary>
    /// Cross-framework compatible clamping function to replace Math.Clamp
    /// </summary>
    private static int ClampLevel(int value, int min, int max)
    {
        return value < min ? min : (value > max ? max : value);
    }
}
