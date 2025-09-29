extern alias GC;
using System;
using System.Diagnostics;
using System.IO;
using K4os.Compression.LZ4.Streams;

namespace PerfTest;

internal static class GCCompressorTests
{
    public static CompressionResult RunDeflate(byte[] input, string entryName, int level, string version)
    {
        var result = new CompressionResult
        {
            Implementation = "GrindCore",
            Algorithm = $"GZip {version} Deflate",
            Level = level,
            OriginalSizeBytes = input.Length
        };

        using var compressedStream = new MemoryStream();
        var sw = Stopwatch.StartNew();

        // Use GrindCore GZip with ZLib version
        using (var gzip = new GC::SharpCompress.Compressors.Deflate.DeflateStream(
                    compressedStream,
                    GC::SharpCompress.Compressors.CompressionMode.Compress,
                    (GC::SharpCompress.Compressors.Deflate.CompressionLevel)ClampLevel(level, 0, 9),
                    leaveOpen: true,
                    isNg: false))
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

    public static CompressionResult RunZipDeflateNG(byte[] input, string entryName, int level)
    {
        var result = new CompressionResult
        {
            Implementation = "GrindCore",
            Algorithm = "DeflateNG",
            Level = level,
            OriginalSizeBytes = input.Length
        };

        using var compressedStream = new MemoryStream();
        var sw = Stopwatch.StartNew();

        // Use the DeflateNG stream from the aliased SharpCompress (GrindCore) assembly
        // DeflateNG uses ZLibNg version internally for better performance
        using (var def = new GC::SharpCompress.Compressors.Deflate.DeflateStream(
                    compressedStream,
                    GC::SharpCompress.Compressors.CompressionMode.Compress,
                    (GC::SharpCompress.Compressors.Deflate.CompressionLevel)ClampLevel(level, 0, 9),
                    leaveOpen: true,
                    isNg: true))
        {
            def.Write(input, 0, input.Length);
            def.Flush();
        } // DeflateStream is disposed here, but compressedStream remains open

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
            Implementation = "GrindCore",
            Algorithm = isLzma2 ? "LZMA2" : "LZMA",
            Level = level,
            OriginalSizeBytes = input.Length
        };

        using var compressedStream = new MemoryStream();
        var sw = Stopwatch.StartNew();

        // Use the LzmaStream encoder constructor from aliased SharpCompress
        var props = new GC::SharpCompress.Compressors.LZMA.LzmaEncoderProperties();
        // Create WriterOptions to properly signal LZMA2 usage
        var writerOptions = isLzma2 
            ? new GC::SharpCompress.Writers.WriterOptions(GC::SharpCompress.Common.CompressionType.LZMA2, level)
            : new GC::SharpCompress.Writers.WriterOptions(GC::SharpCompress.Common.CompressionType.LZMA, level);
                
        using (var encoder = new GC::SharpCompress.Compressors.LZMA.LzmaStream(null, isLzma2, compressedStream, leaveOpen: true, writerOptions: writerOptions))
        {
            encoder.Write(input, 0, input.Length);
            encoder.Flush();
        } // LzmaStream is disposed here, but compressedStream remains open

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
            Implementation = "GrindCore",
            Algorithm = "ZSTD",
            Level = level,
            OriginalSizeBytes = input.Length
        };

        using var compressedStream = new MemoryStream();
        var sw = Stopwatch.StartNew();

        // Use the ZStandard stream implementation in the aliased SharpCompress if available
        using (var z = new GC::SharpCompress.Compressors.ZStandard.ZStandardStream(
            compressedStream,
            GC::SharpCompress.Compressors.CompressionMode.Compress,
            level,
            leaveOpen: true))
        {
            z.Write(input, 0, input.Length);
            z.Flush();
        } // ZStandardStream is disposed here, but compressedStream remains open

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
            Implementation = "GrindCore",
            Algorithm = "LZ4",
            Level = level,
            OriginalSizeBytes = input.Length
        };

        using var compressedStream = new MemoryStream();
        var sw = Stopwatch.StartNew();

        // Use K4os LZ4 encoder stream - works for both GC and Official runs
        var lz4Level = (K4os.Compression.LZ4.LZ4Level)ClampLevel(level, 0, 12);
        using (var lz4 = LZ4Stream.Encode(compressedStream, lz4Level, leaveOpen: true))
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

    public static CompressionResult RunGZip(byte[] input, string entryName, int level, string version)
    {
        var result = new CompressionResult
        {
            Implementation = "GrindCore",
            Algorithm = $"GZip {version} Deflate",
            Level = level,
            OriginalSizeBytes = input.Length
        };

        using var compressedStream = new MemoryStream();
        var sw = Stopwatch.StartNew();

        // Use GrindCore GZip with ZLib version
        using (var gzip = new GC::SharpCompress.Compressors.Deflate.GZipStream(
                    compressedStream,
                    GC::SharpCompress.Compressors.CompressionMode.Compress,
                    (GC::SharpCompress.Compressors.Deflate.CompressionLevel)ClampLevel(level, 0, 9),
                    encoding: null,
                    leaveOpen: true,
                    isNg: false))
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

    public static CompressionResult RunGZipNg(byte[] input, string entryName, int level, string version)
    {
        var result = new CompressionResult
        {
            Implementation = "GrindCore",
            Algorithm = $"GZipNg {version} Deflate",
            Level = level,
            OriginalSizeBytes = input.Length
        };

        using var compressedStream = new MemoryStream();
        var sw = Stopwatch.StartNew();

        // Use GrindCore GZip with ZLibNg version - we need to create WriterOptions to force ZLibNg
        var writerOptions = new GC::SharpCompress.Writers.WriterOptions(
            GC::SharpCompress.Common.CompressionType.GZip, 
            level);

        using (var gzip = new GC::SharpCompress.Compressors.Deflate.GZipStream(
                    compressedStream,
                    GC::SharpCompress.Compressors.CompressionMode.Compress,
                    (GC::SharpCompress.Compressors.Deflate.CompressionLevel)ClampLevel(level, 0, 9),
                    encoding: null,
                    leaveOpen: true,
                    writerOptions: writerOptions,
                    isNg: true))
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
