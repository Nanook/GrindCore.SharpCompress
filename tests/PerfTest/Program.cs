extern alias GC;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PerfTest;

internal class Program
{
    static int Main(string[] args)
    {
        //using (var file = File.OpenWrite(@"D:\Temp\mcorpus.bin_xz_OUT_DECOM"))
        //using (var lzma2Stream = new SharpCompress.Compressors.LZMA.LzmaStream(new byte[] { 0x14 }, File.OpenRead(@"D:\Temp\mcorpus.bin_xz_OUT"), -1))
        //{
        //    lzma2Stream.CopyTo(file);
        //}


        if (args == null || args.Length == 0)
        {
            Console.WriteLine("Usage: PerfTest <file-to-compress>");
            Console.WriteLine("Runs comprehensive compression tests with GrindCore, SharpCompress, K4os LZ4, and ZstdSharp");
            Console.WriteLine("Results are output as JSON objects, one per line.");
            return 2;
        }

        var filePath = args[0];
        if (!File.Exists(filePath))
        {
            Console.WriteLine($"File not found: {filePath}");
            return 1;
        }

        var input = File.ReadAllBytes(filePath);
        var entryName = Path.GetFileName(filePath);
        var allResults = new List<CompressionResult>();

        Console.WriteLine($"# Testing file: {filePath} ({input.Length:N0} bytes)");
        Console.WriteLine();

        try
        {
            // GrindCore LZMA Tests
            Console.WriteLine("# Running GrindCore LZMA Tests");
            allResults.AddRange(RunGrindCoreLzmaTests(input));
            Console.WriteLine();

            // GrindCore LZMA2 Tests
            Console.WriteLine("# Running GrindCore LZMA2 Tests");
            allResults.AddRange(RunGrindCoreLzma2Tests(input));
            Console.WriteLine();

            // GrindCore LZ4 Tests
            Console.WriteLine("# Running GrindCore LZ4 Tests");
            allResults.AddRange(RunGrindCoreLz4Tests(input));
            Console.WriteLine();

            // GrindCore ZSTD Tests
            Console.WriteLine("# Running GrindCore ZSTD Tests");
            allResults.AddRange(RunGrindCoreZstdTests(input));
            Console.WriteLine();

            // GrindCore GZip Tests (v1.3.1)
            Console.WriteLine("# Running GrindCore GZip v1.3.1 Tests");
            allResults.AddRange(RunGrindCoreGZipTests(input, entryName));
            Console.WriteLine();

            // GrindCore GZipNg Tests (v2.2.1)
            Console.WriteLine("# Running GrindCore GZipNg v2.2.1 Tests");
            allResults.AddRange(RunGrindCoreGZipNgTests(input, entryName));
            Console.WriteLine();

            // Official LZ4 Tests
            Console.WriteLine("# Running Official LZ4 Tests");
            allResults.AddRange(RunOfficialLz4Tests(input));
            Console.WriteLine();

            // Official ZSTD Tests
            Console.WriteLine("# Running Official ZSTD Tests");
            allResults.AddRange(RunOfficialZstdTests(input));
            Console.WriteLine();

            // SharpCompress LZMA Tests
            Console.WriteLine("# Running SharpCompress LZMA Tests");
            allResults.AddRange(RunSharpCompressLzmaTests(input));
            Console.WriteLine();

            // SharpCompress GZip Tests
            Console.WriteLine("# Running SharpCompress GZip Tests");
            allResults.AddRange(RunSharpCompressGZipTests(input, entryName));
            Console.WriteLine();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"# Error during testing: {ex.Message}");
        }

        return 0;
    }

    private static List<CompressionResult> RunGrindCoreLzmaTests(byte[] input)
    {
        var results = new List<CompressionResult>();
        var levels = new[] { 1, 5, 9 };
        
        foreach (var level in levels)
        {
            results.Add(GCCompressorTests.RunLzma(input, level, false));
        }
        
        return results;
    }

    private static List<CompressionResult> RunGrindCoreLzma2Tests(byte[] input)
    {
        var results = new List<CompressionResult>();
        var levels = new[] { 1, 5, 9 };
        
        foreach (var level in levels)
        {
            results.Add(GCCompressorTests.RunLzma(input, level, true));
        }
        
        return results;
    }

    private static List<CompressionResult> RunGrindCoreLz4Tests(byte[] input)
    {
        var results = new List<CompressionResult>();
        var levels = new[] { 1, 6, 12 };
        
        foreach (var level in levels)
        {
            results.Add(GCCompressorTests.RunLz4(input, level));
        }
        
        return results;
    }

    private static List<CompressionResult> RunGrindCoreZstdTests(byte[] input)
    {
        var results = new List<CompressionResult>();
        var levels = new[] { 1, 3, 19 };
        
        foreach (var level in levels)
        {
            results.Add(GCCompressorTests.RunZstd(input, level));
        }
        
        return results;
    }

    private static List<CompressionResult> RunGrindCoreGZipTests(byte[] input, string entryName)
    {
        var results = new List<CompressionResult>();
        var levels = new[] { 1, 6, 9 };
        
        foreach (var level in levels)
        {
            results.Add(GCCompressorTests.RunGZip(input, entryName, level, "v1.3.1"));
        }
        
        return results;
    }

    private static List<CompressionResult> RunGrindCoreGZipNgTests(byte[] input, string entryName)
    {
        var results = new List<CompressionResult>();
        var levels = new[] { 1, 6, 9 };
        
        foreach (var level in levels)
        {
            results.Add(GCCompressorTests.RunGZipNg(input, entryName, level, "v2.2.1"));
        }
        
        return results;
    }

    private static List<CompressionResult> RunOfficialLz4Tests(byte[] input)
    {
        var results = new List<CompressionResult>();
        var levels = new[] { 1, 6, 12 };
        
        foreach (var level in levels)
        {
            results.Add(OfficialCompressorTests.RunLz4(input, level));
        }
        
        return results;
    }

    private static List<CompressionResult> RunOfficialZstdTests(byte[] input)
    {
        var results = new List<CompressionResult>();
        var levels = new[] { 1, 3, 19 }; // Note: Level 12 instead of 19 for official ZSTD
        
        foreach (var level in levels)
        {
            results.Add(OfficialCompressorTests.RunZstd(input, level));
        }
        
        return results;
    }

    private static List<CompressionResult> RunSharpCompressLzmaTests(byte[] input)
    {
        var results = new List<CompressionResult>();
        
        // SharpCompress LZMA has no level support, so we test with default
        results.Add(OfficialCompressorTests.RunLzma(input, 5, false)); // Use level 5 as default
        
        return results;
    }

    private static List<CompressionResult> RunSharpCompressGZipTests(byte[] input, string entryName)
    {
        var results = new List<CompressionResult>();
        var levels = new[] { 1, 6, 9 };
        
        foreach (var level in levels)
        {
            results.Add(OfficialCompressorTests.RunGZip(input, entryName, level));
        }
        
        return results;
    }
}
