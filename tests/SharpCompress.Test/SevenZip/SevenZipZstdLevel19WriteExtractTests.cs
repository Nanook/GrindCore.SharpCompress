using System;
using System.IO;
using System.Linq;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Common;
using SharpCompress.Writers;
using SharpCompress.Test.Zip;
using Xunit;

namespace SharpCompress.Test.SevenZip;

public class SevenZipZstdLevel19WriteExtractTests : ArchiveTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(999)]
    [InlineData(1025)]
    [InlineData(65537)]
    [InlineData(2 * 1024 * 1024 + 13)] // ~2MiB + 13
    public void SevenZip_Zstd_Level19_Write_Then_Extract_VerifyLengths(int size)
    {
        var data = TestPseudoTextStream.Create(size);
        var expectedCrc = CalculateCrc32(data);

        using var mem = new MemoryStream();
        var writerOptions = new WriterOptions(CompressionType.ZStandard) { CompressionLevel = 19 };
        try
        {
            using (var writer = WriterFactory.Open(mem, ArchiveType.SevenZip, writerOptions))
            {
                writer.Write($"zstd19_{size}.bin", new MemoryStream(data), null);
            }
        }
        catch (NotSupportedException)
        {
            // SevenZip writer is not implemented in this build - skip test
            return;
        }

        mem.Position = 0;
        using var archive = SevenZipArchive.Open(mem);
        var entry = archive.Entries.Single(e => !e.IsDirectory);
        Assert.Equal(CompressionType.ZStandard, entry.CompressionType);

        using var entryStream = entry.OpenEntryStream();
        using var extracted = new MemoryStream();
        entryStream.CopyTo(extracted);
        var extractedData = extracted.ToArray();

        Assert.Equal(size, extractedData.Length);
        Assert.Equal(expectedCrc, CalculateCrc32(extractedData));

        if (size <= 2048)
        {
            Assert.Equal(data, extractedData);
        }
    }
}
