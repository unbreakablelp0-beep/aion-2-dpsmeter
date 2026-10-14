using Aion2DpsMeter.Core.Analysis;
using Aion2DpsMeter.Core.Events;

namespace Aion2DpsMeter.Core.Tests.Events;

/// <summary>Weltbosse: Restzeit lesen, Erinnerung, lokale Datei; Feldboss-Liste (01 91) mit selbst gebauten Bytes.</summary>
public class WorldBossTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 13, 16, 6, 0, TimeSpan.Zero);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"aion2dps-bosses-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    [Theory]
    [InlineData("4:16:50", 4, 16, 50)]
    [InlineData("16:50", 0, 16, 50)]
    [InlineData("4 Std. 16 Min. 50 Sek.", 4, 16, 50)]
    [InlineData("Verbleibende Zeit 4 Std. 16 Min. 50 Sek.", 4, 16, 50)]
    [InlineData("2h 5m", 2, 5, 0)]
    [InlineData("45 Min", 0, 45, 0)]
    public void ParsesGameCountdowns(string text, int h, int m, int s)
    {
        Assert.Equal(new TimeSpan(h, m, s), WorldBossTimes.ParseCountdown(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("bald")]
    [InlineData("4:75:00")]
    public void RejectsNonsense(string text)
    {
        Assert.Null(WorldBossTimes.ParseCountdown(text));
    }

    [Fact]
    public void GartuaRespawnsTwelveHoursAfterKill()
    {
        var gartua = Assert.Single(WorldBossTimes.Default);
        Assert.Equal(TimeSpan.FromHours(12), gartua.Interval);
        Assert.Equal(T0.AddHours(12), gartua.SpawnAfterKill(T0));
        Assert.Null(new WorldBoss("x", "X", null).SpawnAfterKill(T0)); // Abstand unbekannt
    }

    [Fact]
    public void KillTimeIsTodayOrYesterday()
    {
        var now = new DateTimeOffset(2026, 10, 13, 18, 6, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 10, 13, 18, 6, 0)));
        var today = WorldBossTimes.ParseKillTime("17:42", now)!.Value;
        Assert.Equal(now.Date.AddHours(17).AddMinutes(42), today.LocalDateTime);
        var yesterday = WorldBossTimes.ParseKillTime("23:10:05", now)!.Value; // läge in der Zukunft → gestern
        Assert.Equal(now.Date.AddDays(-1).AddHours(23).AddMinutes(10).AddSeconds(5), yesterday.LocalDateTime);
        Assert.Null(WorldBossTimes.ParseKillTime("25:00", now));
        Assert.Null(WorldBossTimes.ParseKillTime("gleich", now));
    }

    [Fact]
    public void ReminderFiresOnceWithinLeadTime()
    {
        var fired = new HashSet<(string, DateTimeOffset)>();
        var bosses = new[] { new WorldBoss("gartua", "Unsterblicher Gartua", T0.AddMinutes(30), Remind: true, LeadMinutes: 10) };

        Assert.Empty(WorldBossTimes.Due(bosses, T0, fired));
        Assert.Single(WorldBossTimes.Due(bosses, T0.AddMinutes(21), fired));
        Assert.Empty(WorldBossTimes.Due(bosses, T0.AddMinutes(25), fired)); // nicht doppelt
        Assert.Empty(WorldBossTimes.Due(bosses.Select(b => b with { Remind = false }), T0.AddMinutes(25), new HashSet<(string, DateTimeOffset)>()));
        Assert.Empty(WorldBossTimes.Due(bosses, T0.AddMinutes(31), new HashSet<(string, DateTimeOffset)>())); // vorbei
    }

    [Fact]
    public void StoreRoundTripAndNeverOverwritesForeignFile()
    {
        var path = Path.Combine(_directory, "weltbosse.json");
        var store = new WorldBossStore(path);
        Assert.Equal("Unsterblicher Gartua", Assert.Single(store.Load()).Name); // Standard
        Assert.True(store.Save([new WorldBoss("gartua", "Gartua", T0.AddHours(4), true, 15, TimeSpan.FromHours(12))]));

        var loaded = new WorldBossStore(path).Load();
        Assert.Equal(T0.AddHours(4), Assert.Single(loaded).RespawnAt);
        Assert.Equal(TimeSpan.FromHours(12), loaded[0].Interval);
        Assert.Equal(15, loaded[0].LeadMinutes);

        File.WriteAllText(path, "{\"Kind\":\"fremd\"}");
        var foreign = new WorldBossStore(path);
        foreign.Load();
        Assert.NotNull(foreign.Problem);
        Assert.False(foreign.Save([new WorldBoss("x", "X", null)]));
        Assert.Equal("{\"Kind\":\"fremd\"}", File.ReadAllText(path));
    }

    [Fact]
    public void ParsesFieldBossListLikeRecording()
    {
        // Karte 1110, 2 Plätze: Platz 1 lebt (mit Position), Platz 22 tot mit Rückkehrzeit.
        var spawned = T0.AddHours(-9).ToUnixTimeMilliseconds();
        var back = T0.AddMinutes(54).ToUnixTimeMilliseconds();
        var body = new List<byte> { 0, 0 };
        body.AddRange(BitConverter.GetBytes(1110u));
        body.Add(2);
        body.Add(1);
        body.AddRange(Varint(111001));
        body.AddRange(new byte[12]); // x y z
        body.AddRange(BitConverter.GetBytes(spawned));
        body.Add(0);
        body.AddRange(Varint(111022));
        body.AddRange(BitConverter.GetBytes(back));
        body.AddRange(new byte[3]);

        var list = FieldBossListHypothesis.Parse(body.ToArray())!;
        Assert.Equal(1110u, list.Map);
        Assert.Equal(2, list.Slots.Count);
        Assert.True(list.Slots[0].Alive);
        Assert.Equal(1, list.Slots[0].Place);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(spawned), list.Slots[0].Time);
        Assert.False(list.Slots[1].Alive);
        Assert.Equal(22, list.Slots[1].Place);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(back), list.Slots[1].Time);
        Assert.Null(FieldBossListHypothesis.Parse(new byte[] { 0, 0, 0xD6, 0x27, 9, 0, 0, 2 })); // anderer Aufbau (Anzahl 0)
    }

    [Fact]
    public void GartuaTakesTimeFromFieldBossListPlace21()
    {
        var back = T0.AddHours(4);
        var list = new FieldBossList(1110, 24, [new FieldBossSlot(111020, true, T0.AddHours(-9)), new FieldBossSlot(111021, false, back)]);

        var updated = WorldBossTimes.Apply(WorldBossTimes.Default, list)!;
        var gartua = Assert.Single(updated);
        Assert.Equal(back, gartua.RespawnAt);
        Assert.True(gartua.FromGame);
        Assert.Null(WorldBossTimes.Apply(updated, list)); // nichts Neues
        Assert.Null(WorldBossTimes.Apply(WorldBossTimes.Default, list with { Map = 1111 })); // andere Karte
    }

    [Fact]
    public void OlderSavedGartuaGetsFieldPlace()
    {
        var old = new WorldBoss("gartua", "Unsterblicher Gartua", null); // so gespeichert von v22
        var fixedUp = WorldBossTimes.WithKnownDefaults(old);
        Assert.Equal(1110, fixedUp.FieldMap);
        Assert.Equal(21, fixedUp.FieldPlace);
        Assert.Equal(TimeSpan.FromHours(12), fixedUp.Interval);
        Assert.Null(WorldBossTimes.WithKnownDefaults(new WorldBoss("x", "X", null)).FieldMap);
    }

    [Fact]
    public void ReadsNpcCodeAndNamesFromLocalList()
    {
        // Kennung 19072, etwas davor, dann u24 2400032 (Trainingspuppe) vor der Markierung 00 40 02.
        var code = 2_400_032;
        var body = Varint(19_072).Concat(new byte[] { 0x07, 0x00 })
            .Concat(new[] { (byte)code, (byte)(code >> 8), (byte)(code >> 16) })
            .Concat(new byte[] { 0x00, 0x40, 0x02, 1, 2, 3 }).ToArray();
        Assert.Equal(code, EnemyHpHypothesis.ParseNpcCode(body));

        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "npcs.json");
        File.WriteAllText(path, "{\"2400032\":{\"name\":\"Trainingspuppe\",\"isBoss\":true},\"2300371\":{\"name\":\"Kromedes Gier\",\"isBoss\":true},\"x\":{}}");
        var npcs = Aion2DpsMeter.Core.Combat.NpcNames.Load(path);
        Assert.Equal(2, npcs.Count);
        Assert.True(npcs.Lookup(2_400_032)!.IsDummy);
        Assert.False(npcs.Lookup(2_300_371)!.IsDummy);
        Assert.Equal("Kromedes Gier", npcs.Lookup(2_300_371)!.Name);
        Assert.Null(npcs.Lookup(null));
        Assert.Equal(0, Aion2DpsMeter.Core.Combat.NpcNames.Load(Path.Combine(_directory, "fehlt.json")).Count);
    }

    private static IEnumerable<byte> Varint(ulong value)
    {
        do
        {
            var b = (byte)(value & 0x7F);
            value >>= 7;
            yield return value == 0 ? b : (byte)(b | 0x80);
        }
        while (value != 0);
    }
}
