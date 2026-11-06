# GrindCore.SharpCompress Release Notes

## 0.50.8 — November 2026

Based on **SharpCompress 0.50.8** (plus post-release upstream fixes). Upgrades GrindCore from 0.9.0 to **0.10.0**.

### What's New in Upstream SharpCompress 0.50.8

**Security hardening**
- Hardened RAR5, 7z, GZip, ZIP, ARC, ARJ, ACE, and LZip parsing against truncated or malformed input
- Bounds checking for 7z next-header allocations and LZMA2 dictionary properties
- Pool lifetime fixes across multiple decoder paths

**Encrypted archive improvements**
- RAR5: key derivation cached across file entries for performance
- RAR5: encrypted archive comments and header re-scanning fixed
- 7z: encryption detection improved for stream-less archives

**GZip**
- Async CRC and size validation during stream disposal
- Fragmented FEXTRA extra-field reading now handled correctly
- GZip header CRC field now correctly consumed as two bytes

**ZIP**
- UTF-8 flag set correctly for entry names encoded as UTF-8 without a BOM
- ZIP64 streaming data descriptor width parsing fixed
- Empty entries now produce a valid compressed stream instead of zero bytes

**LZMA / 7z**
- Invalid LZMA2 chunk properties now raise `InvalidFormatException` rather than silently succeeding
- Solid 7z archives with Deflate entries now extract correctly

**TAR**
- USTAR long filename splitting corrected on Windows

**General**
- Security: pooled-buffer cleanup, partial-read disposal, and borrowed-stream ownership hardened
- `PooledMemoryStream` used for additional temporary buffers to reduce GC pressure

### GrindCore 0.10.0

- Updated from 0.9.0 → 0.10.0
- Maintains native implementations for: Deflate/GZip (ZLib-NG), LZMA/LZMA2, ZStandard, LZ4, Brotli, BZip2

### GrindCore.SharpCompress-Specific Fixes

- **LZMA exception mapping**: GrindCore decode errors now surface as `DataErrorException` (a `SharpCompressException`) and invalid properties surface as `InvalidParamException`, matching the managed implementation's exception hierarchy
- **Empty encoder streams**: `GZipStream_GC` and `DeflateStream_GC` now produce valid compressed output when closed without writing any data (previously produced zero bytes, causing a compression-method mismatch in non-seekable ZIP archives)
- **Non-seekable ZIP**: `ZipCentralDirectoryEntry` no longer switches empty entries to Store compression in the central directory when the local header has already been written with a different method

---

## 0.50.5 — August 2026

### What's New

- **Native BZip2** via GrindCore (libbzip2 1.0.8) — replaces managed CBZip2InputStream / CBZip2OutputStream
- All 53 BZip2-related tests passing
- GrindCore updated to 0.9.0

---

## 0.50.4 — June 2026

Based on **SharpCompress 0.50.4**.

### What's New in Upstream SharpCompress 0.50.4

- **7-Zip writing** — `SevenZipWriter` for LZMA and LZMA2 archives (non-solid, seekable streams)
- **Archive Detection API** — `ArchiveFactory.GetArchiveInformation()` for lightweight format detection
- **PooledMemoryStream** — `ArrayPool<byte>`-backed stream for reduced GC pressure
- **Zip-Slip protection** — path traversal protection on extraction
- **RAR5 decompression fix** — async decompression corruption fixed
- **GZip flat-stream fix** — seekable stream rewind fix for GZip reader/archive

---

## Support

If you find GrindCore.SharpCompress — or any of my other open-source projects — useful, you're welcome to show your appreciation:

- **Sponsor**: [ko-fi.com/nanook_/tiers](https://ko-fi.com/nanook_/tiers)
- **Buy Me a Beer 🍺**: [ko-fi.com/nanook_](https://ko-fi.com/nanook_)

This project builds on [SharpCompress](https://github.com/adamhathcock/sharpcompress) (MIT). Any support goes towards my open-source work in general.

---

## Prior Releases

See [docs/OLD_CHANGELOG.md](docs/OLD_CHANGELOG.md) for earlier history.
