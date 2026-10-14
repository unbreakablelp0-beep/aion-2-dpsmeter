using System.Buffers.Binary;
using Aion2DpsMeter.Core.Analysis;

namespace Aion2DpsMeter.Core.Tests.Analysis;

/// <summary>Synthetische 41-36-Inhalte nach der beschriebenen Hypothese – keine echten Spieldaten.</summary>
public class SummonOwnerHypothesisTests
{
    [Fact]
    public void FindsOwnerBehindAnchor()
    {
        byte[] body = [.. Varint(49865), 0x1F, 0, 0, 0, 9, 9, .. Anchor, .. Varint(13569), 1, 2, 3];

        var c = SummonOwnerHypothesis.Parse(body);

        Assert.Equal(49865u, c?.RawId);
        Assert.Equal(49865u, c?.MappedId);
        Assert.Equal(13569u, c?.Owner);
        Assert.Equal(SummonOwnerSource.Anchor, c?.Source);
    }

    [Fact]
    public void FindsOwnerViaParentLink()
    {
        var link = new byte[14];
        link[0] = 0x06;
        BinaryPrimitives.WriteUInt32LittleEndian(link.AsSpan(1), 3172); // Besitzer
        BinaryPrimitives.WriteUInt32LittleEndian(link.AsSpan(5), 555); // Legion
        BinaryPrimitives.WriteUInt16LittleEndian(link.AsSpan(11), 2309); // Server
        link[13] = 4; // Namenslänge
        byte[] body = [.. Varint(28846), 0x5F, 0x10, 0, 0, 7, .. link, .. "Gild"u8];

        var c = SummonOwnerHypothesis.Parse(body);

        Assert.Equal(3172u, c?.Owner);
        Assert.Equal(SummonOwnerSource.ParentLink, c?.Source);
    }

    [Fact]
    public void LargeRawIdIsMapped()
    {
        byte[] body = [.. Varint(1_234_567), 0x1F, 0, 0, 0, .. Anchor, .. Varint(13569)];

        var c = SummonOwnerHypothesis.Parse(body);

        Assert.Equal((1_234_567u & 0x3FFF) | 0x4000, c?.MappedId);
    }

    [Theory]
    [InlineData((byte)0x0C)] // NPC-Art: kein Besitzer
    [InlineData((byte)0x1F)] // Pet-Art, aber kein Anker
    public void NoOwnerWithoutAnchorOrForOtherKinds(byte kind)
    {
        byte[] body = kind == 0x1F
            ? [.. Varint(500), kind, 0, 0, 0, 1, 2, 3, 4, 5]
            : [.. Varint(500), kind, 0, 0, 0, .. Anchor, .. Varint(13569)];

        Assert.Null(SummonOwnerHypothesis.Parse(body));
    }

    [Fact]
    public void OwnerEqualToSummonIsRejected()
    {
        Assert.Null(SummonOwnerHypothesis.Parse([.. Varint(500), 0x1F, 0, 0, 0, .. Anchor, .. Varint(500)]));
    }

    private static readonly byte[] Anchor = [0x80, 0x75, 0xD5, 0x2A, 0xBB, 0x03, 0x00, 0x00];

    private static byte[] Varint(uint value)
    {
        var bytes = new List<byte>();
        while (value >= 0x80)
        {
            bytes.Add((byte)(value | 0x80));
            value >>= 7;
        }

        bytes.Add((byte)value);
        return bytes.ToArray();
    }
}
