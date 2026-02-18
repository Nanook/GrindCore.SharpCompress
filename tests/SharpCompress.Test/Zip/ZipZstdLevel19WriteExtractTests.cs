using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SharpCompress.Archives.Zip;
using SharpCompress.Common;
using Xunit;

namespace SharpCompress.Test.Zip;

public class ZipZstdLevel19WriteExtractTests : ArchiveTests
{
    // Test a range of sizes, including 0 and several non-KiB boundaries and a couple of MiB-ish sizes
    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(1023)]
    [InlineData(1025)]
    [InlineData(65535)]
    [InlineData(1048579)] // 1 MiB + 3
    public void Zip_Zstd_Level19_Write_Then_Extract_VerifyLengths(int size)
    {
        // Generate test data
        var data = TestPseudoTextStream.Create(size);
        var expectedCrc = CalculateCrc32(data);

        // Create archive with ZStandard level 19
        using var zipStream = new MemoryStream();
        using (var writer = CreateWriterWithLevel(zipStream, CompressionType.ZStandard, 19))
        {
            writer.Write($"zstd_level_19_{size}.bin", new MemoryStream(data), null);
        }

        // Verify archive and extracted data
        zipStream.Position = 0;
        using var archive = ZipArchive.OpenArchive(zipStream);
        var entry = archive.Entries.Single(e => !e.IsDirectory);

        Assert.Equal(CompressionType.ZStandard, entry.CompressionType);

        using var entryStream = entry.OpenEntryStream();
        using var extracted = new MemoryStream();
        entryStream.CopyTo(extracted);

        var extractedData = extracted.ToArray();

        Assert.Equal(size, extractedData.Length);
        Assert.Equal(expectedCrc, CalculateCrc32(extractedData));

        // For very small files, verify exact content; for larger ones we already checked length and CRC
        if (size <= 1024)
        {
            Assert.Equal(data, extractedData);
        }
    }

    // More thorough test: many files of varying sizes in a single archive to catch partial-buffering issues
    [Fact]
    public void Zip_Zstd_Level19_Write_MultipleFiles_VaryingSizes_NoPartialBuffering()
    {
        var sizes = new int[]
        {
            0,
            1,
            3,
            7,
            15, // very small
            1023,
            1024,
            1025, // around 1 KiB boundary
            2047,
            2048,
            2049, // around 2 KiB
            4095,
            4096,
            4097, // around 4 KiB
            65535,
            65536,
            65537, // around 64 KiB boundary
            100000, // arbitrary non-boundary
            262143,
            262144,
            262145, // around 256 KiB
            1048579, // ~1 MiB + 3
        };

        var files = new Dictionary<string, byte[]>();
        foreach (var s in sizes)
        {
            files[$"file_{s}.bin"] = TestPseudoTextStream.Create(s);
        }

        // Create archive using helper that uses CreateWriterWithLevel internally
        using var zipStream = CreateMemoryArchive(files, CompressionType.ZStandard, 19);

        // Verify entries count
        zipStream.Position = 0;
        using var archive = ZipArchive.OpenArchive(zipStream);
        var entries = archive.Entries.Where(e => !e.IsDirectory).ToList();
        Assert.Equal(files.Count, entries.Count);

        // Build expected map for VerifyArchiveContent which validates CRC and content
        var expectedMap = files.ToDictionary(
            kvp => kvp.Key,
            kvp => (kvp.Value, CalculateCrc32(kvp.Value))
        );

        // Use existing helper to verify archive contents (lengths, CRCs, and spot-checks)
        VerifyArchiveContent(zipStream, expectedMap);
    }
}
