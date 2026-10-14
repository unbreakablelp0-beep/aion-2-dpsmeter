using Aion2DpsMeter.Core.Analysis;

namespace Aion2DpsMeter.Core.Tests.Analysis;

/// <summary>Von Hand gebaute LZ4-Blöcke nach der öffentlichen Blockformat-Beschreibung.</summary>
public class Lz4BlockDecoderTests
{
    [Fact]
    public void DecodesLiteralOnlyBlock()
    {
        var text = "hallo"u8.ToArray();
        var output = new byte[text.Length];

        Assert.Equal(5, Lz4BlockDecoder.Decode(Lz4Literals(text), output));
        Assert.Equal(text, output);
    }

    [Fact]
    public void DecodesLongLiteralLengthWithExtensionBytes()
    {
        var data = SyntheticPackets.RandomBytes(300, seed: 7); // 15 + 255 + 30
        var block = Lz4Literals(data);
        var output = new byte[data.Length];

        Assert.Equal(new byte[] { 0xF0, 255, 30 }, block[..3]);
        Assert.Equal(300, Lz4BlockDecoder.Decode(block, output));
        Assert.Equal(data, output);
    }

    [Fact]
    public void DecodesOverlappingMatch()
    {
        // "ab", dann Rückverweis Abstand 2, Länge 8, dann Literal "c" → "ababababab" + "c"
        byte[] block = [0x24, (byte)'a', (byte)'b', 0x02, 0x00, 0x10, (byte)'c'];
        var output = new byte[11];

        Assert.Equal(11, Lz4BlockDecoder.Decode(block, output));
        Assert.Equal("ababababab"u8.ToArray().Concat("c"u8.ToArray()), output);
    }

    [Fact]
    public void DecodesMatchLengthWithExtensionByte()
    {
        // "x", Abstand 1, Länge 15 + 10 + 4 = 29, danach leere Literal-Sequenz → 30 × 'x'
        byte[] block = [0x1F, (byte)'x', 0x01, 0x00, 10, 0x00];
        var output = new byte[30];

        Assert.Equal(30, Lz4BlockDecoder.Decode(block, output));
        Assert.All(output, b => Assert.Equal((byte)'x', b));
    }

    [Theory]
    [InlineData(new byte[] { 0x14, 0x61, 0x00, 0x00, 0x10, 0x62 })] // Abstand 0
    [InlineData(new byte[] { 0x14, 0x61, 0x05, 0x00, 0x10, 0x62 })] // Abstand vor dem Anfang
    [InlineData(new byte[] { 0x50, 0x61, 0x62 })] // Literale abgeschnitten
    [InlineData(new byte[] { 0x14, 0x61, 0x01 })] // Abstand abgeschnitten
    [InlineData(new byte[] { 0x14, 0x61, 0x01, 0x00 })] // endet nach Rückverweis
    [InlineData(new byte[] { 0xF0, 255 })] // Längenangabe abgeschnitten
    [InlineData(new byte[0])]
    public void RejectsBrokenBlocks(byte[] block)
    {
        Assert.Equal(-1, Lz4BlockDecoder.Decode(block, new byte[64]));
    }

    [Fact]
    public void RejectsOutputThatDoesNotFit()
    {
        Assert.Equal(-1, Lz4BlockDecoder.Decode(Lz4Literals("hallo"u8.ToArray()), new byte[4]));
    }

    /// <summary>Gültiger LZ4-Block nur aus Literalen (ohne Kompression).</summary>
    internal static byte[] Lz4Literals(byte[] literals)
    {
        var block = new List<byte>();
        var length = literals.Length;
        block.Add((byte)(Math.Min(length, 15) << 4));
        if (length >= 15)
        {
            var rest = length - 15;
            while (rest >= 255)
            {
                block.Add(255);
                rest -= 255;
            }

            block.Add((byte)rest);
        }

        block.AddRange(literals);
        return block.ToArray();
    }
}
