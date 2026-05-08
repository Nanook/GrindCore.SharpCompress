# GrindCore.SharpCompress

GrindCore.SharpCompress is an enhanced version of **SharpCompress** that integrates **GrindCore** native compression. This project delivers native compression support built using the **System.IO.Compression** pattern, utilizing compression algorithms directly from their original C implementations.

This fork replaces GZip, LZMA, Deflate, ZStandard, LZ4, and Brotli implementations with **native C streams** from [GrindCore](https://github.com/Nanook/GrindCore.net), providing significant performance improvements while maintaining full API compatibility.

Based on **SharpCompress 0.48.0** — includes all upstream features plus native compression.

> For more in-depth information, see [![Ask DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/Nanook/GrindCore.SharpCompress).

## Installation

```xml
<PackageReference Include="GrindCore.SharpCompress" Version="0.48.0" />
```

## Format Support

| Format | Read | Write | Native Performance | Notes |
|--------|------|-------|-------------------|-------|
| **ZIP** | ✅ | ✅ | ✅ (Deflate, ZStd, LZMA) | Zip64, PKWare/WinZip AES encryption |
| **TAR** | ✅ | ✅ | ✅ (with compression) | GZip, BZip2, LZip, XZ, ZStandard |
| **GZIP** | ✅ | ✅ | ✅ | Native ZLib-NG |
| **BZIP2** | ✅ | ✅ | ❌ (managed) | Original C# implementation |
| **7ZIP** | ✅ | ✅ | ✅ (LZMA/LZMA2) | Non-solid write, seekable streams required |
| **RAR** | ✅ | ❌ | N/A | RAR4 and RAR5, solid archives supported |
| **LZIP** | ✅ | ✅ | ✅ | Native LZMA |
| **XZ** | ✅ | ❌ | ✅ | Native LZMA2 decompression |
| **ACE** | ✅ | ❌ | N/A | Read-only |
| **ARJ** | ✅ | ❌ | N/A | Read-only |

### Compression Algorithms

| Algorithm | Levels | Native | Notes |
|-----------|--------|--------|-------|
| **Deflate** | 1-9 | ✅ ZLib-NG v2.2.1 | Used in ZIP, GZip, Tar.GZip |
| **LZMA/LZMA2** | 1-9 | ✅ v25.1.0 | Block and Solid modes, 7zip writing |
| **ZStandard** | 1-22 | ✅ v1.5.7 | ZIP, TAR, standalone |
| **LZ4** | 1-12 | ✅ v1.10.0 | 7zip decompression, standalone |
| **Brotli** | 1-11 | ✅ v1.1.0 | 7zip decompression, standalone |
| **BZip2** | Fixed | ❌ | Managed C# implementation |
| **PPMd** | Fixed | ❌ | Managed C# implementation |
| **Deflate64** | N/A | ❌ | Decompression only |
| **Shrink/Implode/Reduce** | N/A | ❌ | Legacy ZIP decompression only |

## Key Features

### Native Compression via GrindCore
- Native C implementations compiled from original algorithm authors' code
- No external DLL dependencies — native libraries bundled per platform
- Multiplatform: Windows (x64/x86/ARM64), Linux (x64/ARM64/ARM), macOS (x64/ARM64)
- AOT/Trimming compatible on .NET 8+

### 7-Zip Writing (New in 0.48.0)
- `SevenZipWriter` for creating 7z archives with LZMA or LZMA2 compression
- Non-solid mode (each file compressed independently)
- Async writing support via `WriterFactory.OpenAsyncWriter`
- Requires seekable output stream

### Archive Detection API (New in 0.48.0)
- `ArchiveFactory.GetArchiveInformation()` — detect archive type without fully opening
- Consolidated factory helpers for format detection

### PooledMemoryStream (New in 0.48.0)
- `ArrayPool<byte>`-backed memory stream for reduced GC pressure
- Used internally for 7zip writing and CRC computation

### Zip-Slip Protection (New in 0.48.0)
- Path traversal protection on extraction
- Consolidated extraction options via `ExtractionOptions`

### Stream APIs
- **Archive API**: Random access with seekable streams (`ZipArchive`, `TarArchive`, `SevenZipArchive`, etc.)
- **Reader API**: Forward-only reading on non-seekable streams (`ZipReader`, `TarReader`, etc.)
- **Writer API**: Forward-only writing (`ZipWriter`, `TarWriter`, `SevenZipWriter`, etc.)
- Full async/await support with `CancellationToken` throughout
- Auto-detection via `ReaderFactory.OpenReader()` / `ArchiveFactory.OpenArchive()`

### LZMA2 Modes
- **Block Mode**: Configurable block sizes for compression/speed balance
- **Solid Mode**: Maximum compression (`CompressionBufferSize = -1`)

## Framework Support

- .NET 10, 9, 8, 7, 6, 5
- .NET Standard 2.1, 2.0
- .NET Framework 4.8, 4.8.1
- NativeAOT compatible (.NET 8+)

## Performance

GrindCore native implementations provide measurable improvements over managed C#:

- **Deflate/GZip**: 3-5x faster than managed implementation
- **LZ4**: 400+ MB/s compression, 1500+ MB/s decompression
- **ZStandard Level 6**: 100+ MB/s compression, 400+ MB/s decompression
- **LZMA2 Solid**: 95%+ compression ratio on text data
- **Brotli Level 9**: Excellent web compression with 85%+ ratio on text

Native implementations leverage modern CPU instruction sets (AVX2, SSE4) where available.

## Migration from SharpCompress

This is a drop-in replacement. Change your package reference:

```xml
<!-- Before -->
<PackageReference Include="SharpCompress" Version="0.48.0" />

<!-- After -->
<PackageReference Include="GrindCore.SharpCompress" Version="0.48.0" />
```

No code changes required. Existing SharpCompress code benefits from native performance automatically.

## Usage Examples

```csharp
// Write a ZIP with ZStandard compression
var options = new WriterOptions(CompressionType.ZStandard) { CompressionLevel = 6 };
using var writer = WriterFactory.OpenWriter(outputStream, ArchiveType.Zip, options);
writer.Write("file.txt", inputStream, DateTime.Now);

// Write a 7zip archive with LZMA2
var options7z = new WriterOptions(CompressionType.LZMA2) { CompressionLevel = 9 };
using var writer7z = WriterFactory.OpenWriter(outputStream, ArchiveType.SevenZip, options7z);
writer7z.Write("data.bin", inputStream, DateTime.Now);

// LZMA2 Solid Mode for maximum compression
var solidOptions = new WriterOptions(CompressionType.LZMA2)
{
    CompressionBufferSize = -1 // Solid mode
};

// Read any archive format (auto-detect)
using var archive = ArchiveFactory.OpenArchive(inputStream);
foreach (var entry in archive.Entries.Where(e => !e.IsDirectory))
{
    entry.WriteToDirectory(outputDir);
}
```

## Architecture

```
GrindCore.SharpCompress.dll
  → GrindCore.net.dll (.NET stream wrappers)
    → GrindCore.dll (Native C library)
```

When `UseGrindCore=true` (default), native stream implementations replace the managed ones at compile time. Set `UseGrindCore=false` to build with pure managed implementations (matching upstream SharpCompress behaviour).

## Documentation

- [Supported Formats](docs/FORMATS.md)
- [Basic Usage](docs/USAGE.md)
- [API Reference](docs/API.md)
- [Architecture](docs/ARCHITECTURE.md)
- [Performance](docs/PERFORMANCE.md)

## Related Projects

- **[GrindCore](https://github.com/Nanook/GrindCore)**: Multiplatform native C compression library
- **[GrindCore.net](https://github.com/Nanook/GrindCore.net)**: .NET wrapper for GrindCore (.NET Framework 3.5 through .NET 10)
- **[GrindCore NuGet](https://www.nuget.org/packages/GrindCore)**: Standalone GrindCore package
- **[SharpCompress](https://github.com/adamhathcock/sharpcompress)**: The upstream library this fork enhances

## Contributing

Contributions welcome. Areas of interest:

- Performance benchmarking
- Platform-specific testing
- Additional native algorithm integration (BZip2, PPMd)
- Real-world usage feedback

Please use GitHub issues for support requests.

## License

MIT License — same as the original SharpCompress project.
Native libraries are licensed as specified in [GrindCore documentation](https://github.com/Nanook/GrindCore).
