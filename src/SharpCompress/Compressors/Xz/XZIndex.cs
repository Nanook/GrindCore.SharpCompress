using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SharpCompress.Common;
using SharpCompress.IO;

namespace SharpCompress.Compressors.Xz;

[CLSCompliant(false)]
public class XZIndex
{
    private readonly BinaryReader _reader;
    public long StreamStartPosition { get; private set; }
    public ulong NumberOfRecords { get; private set; }
    public List<XZIndexRecord> Records { get; } = new();

    private readonly bool _indexMarkerAlreadyVerified;

    public XZIndex(BinaryReader reader, bool indexMarkerAlreadyVerified)
    {
        _reader = reader;
        _indexMarkerAlreadyVerified = indexMarkerAlreadyVerified;
        StreamStartPosition = reader.BaseStream.Position;
        if (indexMarkerAlreadyVerified)
        {
            StreamStartPosition--;
        }
    }

    public static XZIndex FromStream(Stream stream, bool indexMarkerAlreadyVerified)
    {
        var index = new XZIndex(
            new BinaryReader(SharpCompressStream.Create(stream, leaveOpen: true), Encoding.UTF8),
            indexMarkerAlreadyVerified
        );
        index.Process();
        return index;
    }

    public void Process()
    {
        if (!_indexMarkerAlreadyVerified)
        {
            VerifyIndexMarker();
        }

        NumberOfRecords = _reader.ReadXZInteger();
        for (ulong i = 0; i < NumberOfRecords; i++)
        {
            Records.Add(XZIndexRecord.FromBinaryReader(_reader));
        }
        SkipPadding();
        VerifyCrc32();
    }

    private void VerifyIndexMarker()
    {
        var marker = _reader.ReadByte();
        if (marker != 0)
        {
            throw new InvalidFormatException("Not an index block");
        }
    }

    private void SkipPadding()
    {
        var bytes = (int)(_reader.BaseStream.Position - StreamStartPosition) % 4;
        if (bytes > 0)
        {
            var paddingBytes = _reader.ReadBytes(4 - bytes);
            if (paddingBytes.Any(b => b != 0))
            {
                throw new InvalidFormatException("Padding bytes were non-null");
            }
        }
    }

    private void VerifyCrc32()
    {
        var crc = _reader.ReadLittleEndianUInt32();
        // TODO verify this matches
    }
}
