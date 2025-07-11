using System.IO;
using SharpCompress.IO;
using Xunit;

namespace SharpCompress.Test.Streams;

public class RewindableStreamTest
{
    [Fact]
    public void TestRewind()
    {
        var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);
        bw.Write(1);
        bw.Write(2);
        bw.Write(3);
        bw.Write(4);
        bw.Write(5);
        bw.Write(6);
        bw.Write(7);
        bw.Flush();
        ms.Position = 0;
        var stream = new SharpCompressStream(ms, bufferSize: 0x10000);
        long pos = ((IStreamStack)stream).GetPosition();
        var br = new BinaryReader(stream);
        Assert.Equal(1, br.ReadInt32());
        Assert.Equal(2, br.ReadInt32());
        Assert.Equal(3, br.ReadInt32());
        Assert.Equal(4, br.ReadInt32());
        ((IStreamStack)stream).StackSeek(pos);
        pos = ((IStreamStack)stream).GetPosition();
        Assert.Equal(1, br.ReadInt32());
        Assert.Equal(2, br.ReadInt32());
        Assert.Equal(3, br.ReadInt32());
        Assert.Equal(4, br.ReadInt32());
        Assert.Equal(5, br.ReadInt32());
        Assert.Equal(6, br.ReadInt32());
        Assert.Equal(7, br.ReadInt32());
        ((IStreamStack)stream).StackSeek(pos);
        Assert.Equal(1, br.ReadInt32());
        Assert.Equal(2, br.ReadInt32());
        Assert.Equal(3, br.ReadInt32());
        Assert.Equal(4, br.ReadInt32());
    }

    [Fact]
    public void TestIncompleteRewind()
    {
        var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);
        bw.Write(1);
        bw.Write(2);
        bw.Write(3);
        bw.Write(4);
        bw.Write(5);
        bw.Write(6);
        bw.Write(7);
        bw.Flush();
        ms.Position = 0;
        var stream = new SharpCompressStream(ms, bufferSize: 0x10000);
        var br = new BinaryReader(stream);
        long pos = ((IStreamStack)stream).GetPosition();
        Assert.Equal(1, br.ReadInt32());
        Assert.Equal(2, br.ReadInt32());
        Assert.Equal(3, br.ReadInt32());
        Assert.Equal(4, br.ReadInt32());
        ((IStreamStack)stream).StackSeek(pos);
        Assert.Equal(1, br.ReadInt32());
        Assert.Equal(2, br.ReadInt32());
        pos = ((IStreamStack)stream).GetPosition();
        Assert.Equal(3, br.ReadInt32());
        Assert.Equal(4, br.ReadInt32());
        Assert.Equal(5, br.ReadInt32());
        ((IStreamStack)stream).StackSeek(pos);
        Assert.Equal(3, br.ReadInt32());
        Assert.Equal(4, br.ReadInt32());
        Assert.Equal(5, br.ReadInt32());
        Assert.Equal(6, br.ReadInt32());
        Assert.Equal(7, br.ReadInt32());
    }
}
