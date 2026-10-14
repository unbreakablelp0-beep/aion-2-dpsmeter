using System.Buffers.Binary;
using Aion2DpsMeter.Core.Analysis;
using static Aion2DpsMeter.Core.Tests.Analysis.SyntheticPackets;

namespace Aion2DpsMeter.Core.Tests.Analysis;

/// <summary>Synthetische 04-38-Datensätze nach der beschriebenen Hypothese – keine echten Spieldaten.</summary>
public class DamageRecordHypothesisTests
{
    private static readonly byte[] Block11 = [0x11, 0x00, 0x22, 0, 0, 0, 0, 0, 0, 0, 0];
    private static readonly byte[] Block8 = [0, 0, 0, 0, 0, 0, 0, 0];

    [Fact]
    public void Layout6RecordWithClosingMarkerIsComplete()
    {
        var body = Rec(target: 500, sw: 0x06, actor: 77, skill: 17_000_100, damageType: 3, block: Block11, scalar: 11_910, candidate: 4_321)
            .Concat(new byte[] { 0x01, 0x00 }).ToArray();

        var result = DamageRecordHypothesis.Parse(body);

        Assert.Equal(DamageFrameStatus.Complete, result.Status);
        var r = Assert.Single(result.Records);
        Assert.Equal(500u, r.Target);
        Assert.Equal(77u, r.Actor);
        Assert.Equal(17_000_100u, r.Skill);
        Assert.Equal(6, r.Layout);
        Assert.Equal(3u, r.DamageType);
        Assert.Equal((byte)0x11, r.Modifier);
        Assert.Equal((byte)0x22, r.Direction);
        Assert.Equal(11, r.ValueSkip);
        Assert.Equal(11_910u, r.Scalar);
        Assert.Equal(4_321u, r.Candidate);
        Assert.False(r.SelfTarget);
        Assert.Equal(body.Length, result.ConsumedBytes);
    }

    [Fact]
    public void RecordEndingExactlyAtFrameEndIsComplete()
    {
        var result = DamageRecordHypothesis.Parse(Rec(500, 0x06, 77, 17_000_100, 2, Block11, 0, 999));

        Assert.Equal(DamageFrameStatus.Complete, result.Status);
        Assert.Equal(0u, result.Records[0].Scalar);
    }

    [Fact]
    public void LayoutZeroIsCastMarker()
    {
        var result = DamageRecordHypothesis.Parse([.. Varint(500), 0x00, 0x05, 0x09]);

        Assert.Equal(DamageFrameStatus.CastMarker, result.Status);
        Assert.Empty(result.Records);
    }

    [Fact]
    public void Layout4NeedsExtraTailValueAndDetectsSelfTarget()
    {
        var body = Rec(42, 0x04, 42, 1_010_000, 2, Block8, 10_000, 1_064, tail: [0x05]);

        var result = DamageRecordHypothesis.Parse(body);

        Assert.Equal(DamageFrameStatus.Complete, result.Status);
        Assert.True(result.Records[0].SelfTarget);
        Assert.Equal(8, result.Records[0].ValueSkip);
        Assert.Null(result.Records[0].Modifier);
    }

    [Fact]
    public void ExtraHitsAreReadWhenFlagIsSet()
    {
        var body = Rec(500, 0x26, 77, 17_000_100, 2, Block11, 11_910, 2_000, tail: [0x02, 10, 20]);

        var r = Assert.Single(DamageRecordHypothesis.Parse(body).Records);

        Assert.Equal(2, r.ExtraHits);
        Assert.Equal(30, r.ExtraSum);
        Assert.True(r.TailClean);
    }

    [Fact]
    public void ChainedRecordsAreAllRead()
    {
        var body = Rec(500, 0x06, 77, 17_000_100, 2, Block11, 11_910, 100)
            .Concat(new byte[] { 0x02, 0x00 })
            .Concat(Rec(501, 0x06, 78, 17_000_200, 3, Block11, 11_910, 200))
            .Concat(new byte[] { 0x01, 0x00 })
            .ToArray();

        var result = DamageRecordHypothesis.Parse(body);

        Assert.Equal(DamageFrameStatus.Complete, result.Status);
        Assert.Equal(new uint[] { 100, 200 }, result.Records.Select(r => r.Candidate));
    }

    [Fact]
    public void ThirdLayoutVariantCritCase()
    {
        // Layout 6: erster Wert 5.000, zweiter 2 → Kandidat ist der erste Wert; die 2 gehört zur Kennung 02 00.
        var body = Rec(500, 0x06, 77, 17_000_100, 3, Block11, 5_000, 2).Concat(new byte[] { 0x00 }).ToArray();

        var r = Assert.Single(DamageRecordHypothesis.Parse(body).Records);

        Assert.Equal(5_000u, r.Candidate);
        Assert.Null(r.Scalar);
    }

    [Fact]
    public void LifeStealSuffixIsRead()
    {
        var body = Rec(500, 0x06, 77, 17_000_100, 2, Block11, 11_910, 900, tail: [0x03, 0x00, 55]);

        var result = DamageRecordHypothesis.Parse(body);

        Assert.Equal(DamageFrameStatus.Complete, result.Status);
        Assert.Equal(55u, result.Records[0].LifeSteal);
    }

    [Fact]
    public void ItemSkillRangeIsMapped()
    {
        var r = Assert.Single(DamageRecordHypothesis.Parse(Rec(500, 0x06, 77, 3_000_005, 2, Block11, 11_910, 900)).Records);

        Assert.Equal(30_000_051u, r.Skill);
    }

    [Theory]
    [InlineData(0u, 0x06, "Ziel")]
    [InlineData(500u, 0x03, "Layout 3")]
    [InlineData(500u, 0x08, "Layout 8")]
    public void RejectsImplausibleFirstRecord(uint target, byte sw, string problem)
    {
        var result = DamageRecordHypothesis.Parse(Rec(target, sw, 77, 17_000_100, 2, Block11, 11_910, 900));

        Assert.Equal(DamageFrameStatus.Rejected, result.Status);
        Assert.Equal(problem, result.Problem);
    }

    [Fact]
    public void RejectsWhenNoValueBlockFits()
    {
        // Kandidat 0; erster Wert 500 liegt außerhalb des Sonderfalls (1.000–5.000.000), der sonst ihn nähme.
        var result = DamageRecordHypothesis.Parse(Rec(500, 0x06, 77, 17_000_100, 2, Block11, 500, 0));

        Assert.Equal(DamageFrameStatus.Rejected, result.Status);
        Assert.Equal("Werte", result.Problem);
    }

    [Fact]
    public void LeftoverAfterRecordIsPartlyParsed()
    {
        var body = Rec(500, 0x06, 77, 17_000_100, 2, Block11, 11_910, 900).Concat(new byte[] { 0x01, 0x00, 0x9B, 0x02 }).ToArray();

        var result = DamageRecordHypothesis.Parse(body);

        Assert.Equal(DamageFrameStatus.PartlyParsed, result.Status);
        Assert.Single(result.Records);
    }

    [Fact]
    public void ReportFindsFramesInStreamAndWritesCsv()
    {
        const uint isn = 100;
        var stream = new List<byte>();
        for (var i = 0; i < 25; i++)
        {
            stream.AddRange(Frame([0x00, 0x36, 1, 2, 3, 4, 5, 6, 7, 8])); // Heartbeat
            stream.AddRange(Frame([0x04, 0x38, .. Rec(500, 0x06, 77, 17_000_100, 2, Block11, 11_910, (uint)(1000 + i)), 0x01, 0x00]));
            stream.AddRange(Frame([0x04, 0x38, .. Varint(500), 0x00])); // Ankündigung
        }

        var packets = new List<Aion2DpsMeter.Core.Analysis.RawPacket>
        {
            Tcp(0, Server, 7777, Local, 50001, [], isn, TcpFlags.Syn | TcpFlags.Ack),
            Tcp(0.5, Server, 7777, Local, 50001, stream.ToArray(), isn + 1),
        };

        var analysis = DamageCandidateReport.Analyze(packets, [Local]);

        Assert.Equal(50, analysis.Frames.Count);
        Assert.Equal(25, analysis.Frames.Count(f => f.Result.Status == DamageFrameStatus.Complete));
        Assert.Equal(25, analysis.Frames.Count(f => f.Result.Status == DamageFrameStatus.CastMarker));

        var text = DamageCandidateReport.Format(analysis, "test.pcap");
        Assert.Contains("vollständig zerlegt: 25 (50,0 %)", text);
        Assert.Contains("Hypothese bestätigt (Aufbau)", text);
        Assert.Contains("Kandidatenwert (Akteur ≠ Ziel): Summe 25.300, Min 1.000, Max 1.024", text);

        var csv = DamageCandidateReport.ToCsv(analysis).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(51, csv.Length); // Kopf + 25 Datensätze + 25 Ankündigungen
        Assert.StartsWith("Zeit;Sekunden;Richtung;", csv[0]);
        var first = csv[1].Split(';');
        Assert.Equal("500", first[7]);
        Assert.Equal("77", first[8]);
        Assert.Equal("1000", first[20]);
    }

    [Fact]
    public void CsvPathNeverOverwrites()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"aion2dps-csv-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var first = DamageCandidateReport.CreateCsvPath(dir, @"C:\x\aufnahme-1.pcap");
            File.WriteAllText(first, "alt");
            var second = DamageCandidateReport.CreateCsvPath(dir, @"C:\x\aufnahme-1.pcap");

            Assert.EndsWith("aufnahme-1-0438.csv", first);
            Assert.EndsWith("aufnahme-1-0438-2.csv", second);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // --- Hilfen -----------------------------------------------------------------------------------

    private static byte[] Rec(uint target, byte sw, uint actor, uint skill, uint damageType, byte[] block, uint scalar, uint candidate, byte[]? tail = null)
    {
        var b = new List<byte>();
        b.AddRange(Varint(target));
        b.Add(sw);
        b.AddRange(Varint(0)); // Flag
        b.AddRange(Varint(actor));
        var s = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(s, skill);
        b.AddRange(s);
        b.Add(0x01); // Trefferbyte
        b.AddRange(Varint(damageType));
        b.AddRange(block);
        b.AddRange(Varint(scalar));
        b.AddRange(Varint(candidate));
        if (tail is not null) b.AddRange(tail);
        return b.ToArray();
    }

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

    private static byte[] Frame(byte[] body) => [.. Varint((uint)body.Length + 4), .. body];
}
