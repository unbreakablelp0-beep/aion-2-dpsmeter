using Aion2DpsMeter.Core.Combat;

namespace Aion2DpsMeter.Core.Tests.Combat;

/// <summary>Aufschlüsselung pro Fähigkeit, Fähigkeitsnamen und Kopiertext (simulierte Treffer).</summary>
public class PlayerDetailTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static CombatHit Hit(double s, uint actor, uint target, uint skill, long amount, bool crit = false, bool overTime = false) =>
        new(T0.AddSeconds(s), actor, target, skill, amount, crit, overTime);

    /// <summary>Alle Treffer fast gleichzeitig (wie im Nutzer-Screenshot): keine „DPS (aktiv)“, Zeit „&lt;0:01“.</summary>
    [Fact]
    public void ActiveDpsOnlyAfterMinimumActiveTime()
    {
        var meter = new LiveCombatMeter();
        meter.Add(Hit(0, 1, 900, 11_100_340, 60_008));
        meter.Add(Hit(0.2, 1, 900, 11_100_058, 13_256));
        meter.Add(Hit(8, 2, 900, 16_100_004, 1)); // anderer Spieler (unter der Leerlaufgrenze von 10 s)

        var burst = meter.Detail(1)!;
        Assert.Null(burst.ActiveDpsIfMeaningful);
        Assert.Equal("–", OverlayFormat.ActiveDps(burst.ActiveDpsIfMeaningful));
        Assert.Equal("<0:01", OverlayFormat.ActiveTime(burst.ActiveTime));

        meter.Add(Hit(1.5, 1, 900, 11_190_130, 2_000));
        meter.Add(Hit(3.5, 1, 900, 11_190_130, 2_000));
        var steady = meter.Detail(1)!;
        Assert.Equal(TimeSpan.FromSeconds(3.5), steady.ActiveTime);
        Assert.NotNull(steady.ActiveDpsIfMeaningful);
        Assert.Equal("0:03", OverlayFormat.ActiveTime(steady.ActiveTime));
    }

    [Fact]
    public void CommunityNamesUseExactThenBaseNumber()
    {
        var community = CommunitySkillNames.From(new Dictionary<uint, string>
        {
            [14_340_230] = "Schnellschuss",
            [14_800_000] = "Jägerseele",
        });
        Assert.Equal("Schnellschuss", community.NameOf(14_340_230));
        Assert.Equal("Jägerseele", community.NameOf(14_800_007)); // Variante → Grundnummer
        Assert.Null(community.NameOf(14_900_007));
        Assert.Null(community.NameOf(9_999)); // kleine Nummern ohne Rückfall
    }

    [Fact]
    public void OwnNameBeatsCommunityNameAndFileIsNeverWritten()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"aion2dps-skills-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(directory);
            var communityPath = Path.Combine(directory, "skills-community.json");
            const string communityText = "{\"14340230\":\"Schnellschuss\",\"14800000\":\"Jägerseele\",\"x\":\"ungültig\"}";
            File.WriteAllText(communityPath, communityText);

            var store = new SkillNameStore(Path.Combine(directory, "skills.json"));
            store.Load();
            store.Community = CommunitySkillNames.Load(communityPath);
            Assert.Equal(2, store.Community.Count);

            Assert.Equal("Schnellschuss*", store.DisplayName(14_340_230, out var fromCommunity));
            Assert.True(fromCommunity);
            Assert.Equal("#14020340", store.DisplayName(14_020_340, out fromCommunity));
            Assert.False(fromCommunity);

            Assert.True(store.SetName(14_340_230, "Mein Schuss"));
            Assert.Equal("Mein Schuss", store.DisplayName(14_340_230, out fromCommunity));
            Assert.False(fromCommunity);
            Assert.Equal(communityText, File.ReadAllText(communityPath));

            Assert.Equal(0, CommunitySkillNames.Load(Path.Combine(directory, "fehlt.json")).Count);
            File.WriteAllText(communityPath, "kaputt");
            Assert.NotNull(CommunitySkillNames.Load(communityPath).Problem);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void BreaksDownBySkillKindAndTarget()
    {
        var meter = new LiveCombatMeter();
        meter.Add(Hit(0, 1, 900, 14_020_340, 1000, crit: true));
        meter.Add(Hit(1, 1, 900, 14_020_340, 3000));
        meter.Add(Hit(2, 1, 901, 14_340_230, 500));
        meter.Add(Hit(3, 1, 900, 14_050_000, 300, overTime: true));
        meter.ReportSummon(77, 1);
        meter.Add(Hit(4, 77, 900, 100_044, 200));
        meter.Add(Hit(10, 2, 900, 16_100_004, 1)); // anderer Spieler: verlängert den Kampf bis 10 s

        var all = meter.Detail(1)!;
        Assert.Equal(5000, all.Damage);
        Assert.Equal(TimeSpan.FromSeconds(10), all.Duration);
        Assert.Equal(500, all.Dps);
        Assert.Equal(4, all.Hits); // 3 direkt + 1 Pet, ohne Tick über Zeit
        Assert.Equal(1, all.CriticalHits);
        Assert.Equal(new uint[] { 900, 901 }, all.Targets.Select(t => t.Target));

        var main = all.Skills[0];
        Assert.Equal(14_020_340u, main.Skill);
        Assert.Equal(HitKind.Direct, main.Kind);
        Assert.Equal(4000, main.Damage);
        Assert.Equal(0.8, main.Share);
        Assert.Equal(2, main.Hits);
        Assert.Equal(0.5, main.CriticalRate);
        Assert.Equal(3000, main.Max);
        Assert.Equal(2000, main.Average);
        Assert.Contains(all.Skills, s => s.Kind == HitKind.OverTime && s.Damage == 300);
        Assert.Contains(all.Skills, s => s.Kind == HitKind.Pet && s.Damage == 200);

        var onlyOther = meter.Detail(1, target: 901)!;
        Assert.Equal(500, onlyOther.Damage);
        Assert.Single(onlyOther.Skills);
        Assert.Equal(2, onlyOther.Targets.Count); // Auswahl bleibt vollständig

        Assert.Null(meter.Detail(999));
    }

    [Fact]
    public void CommunityFlagsCountOnlyHitsWithTheField()
    {
        var meter = new LiveCombatMeter();
        meter.Add(new CombatHit(T0, 1, 900, 14_020_340, 100, false, false, Modifier: 0x04, Direction: 0x02, ExtraHits: 4)); // perfekt, front, multi
        meter.Add(new CombatHit(T0, 1, 900, 14_020_340, 100, false, false, Modifier: 0x0C, Direction: 0x01)); // perfekt+doppelt, rücken
        meter.Add(new CombatHit(T0, 1, 900, 14_020_340, 100, false, false, Modifier: 0x02, Direction: 0x02)); // parade
        meter.Add(new CombatHit(T0, 1, 900, 14_020_340, 100, false, false)); // Layout 4: keine Felder
        meter.Add(new CombatHit(T0, 1, 900, 14_050_000, 50, false, true)); // über Zeit

        var f = meter.Detail(1)!.Flags!;

        Assert.Equal(3, f.WithModifier);
        Assert.Equal(2.0 / 3, f.PerfectRate);
        Assert.Equal(1.0 / 3, f.DoubleRate);
        Assert.Equal(1.0 / 3, f.ParryRate);
        Assert.Equal(1.0 / 3, f.BackRate);
        Assert.Equal(2.0 / 3, f.FrontRate);
        Assert.Equal(4, f.DirectHits);
        Assert.Equal(0.25, f.MultiRate);

        var skill = meter.Detail(1)!.Skills.Single(s => s.Kind == HitKind.Direct);
        Assert.Equal(3, skill.Flags!.WithModifier);
    }

    [Fact]
    public void ActiveTimeSkipsLongPausesAndHitsIncludeExtras()
    {
        var meter = new LiveCombatMeter(TimeSpan.FromMinutes(5));
        meter.Add(new CombatHit(T0, 1, 900, 14_020_340, 1000, false, false, ExtraHits: 4));
        meter.Add(new CombatHit(T0.AddSeconds(2), 1, 900, 14_020_340, 1000, false, false));
        meter.Add(new CombatHit(T0.AddSeconds(4), 1, 900, 14_020_340, 1000, false, false, ExtraHits: 1));
        meter.Add(new CombatHit(T0.AddSeconds(14), 1, 900, 14_020_340, 1000, false, false)); // 10 s Pause zählt nicht
        meter.Add(new CombatHit(T0.AddSeconds(15), 1, 900, 14_050_000, 100, false, true)); // Tick über Zeit

        var d = meter.Detail(1)!;

        Assert.Equal(TimeSpan.FromSeconds(5), d.ActiveTime); // 2 + 2 + 1
        Assert.Equal(4100 / 5.0, d.ActiveDps);
        Assert.Equal(4, d.Hits);
        Assert.Equal(9, d.HitsWithExtra); // 4 Zauber + 5 Zusatzwerte
    }

    [Fact]
    public void PetHitsMovedLaterAppearAsPetSkills()
    {
        var meter = new LiveCombatMeter();
        meter.Add(Hit(0, 1, 900, 16_100_004, 1000));
        meter.Add(Hit(1, 77, 900, 100_044, 250));
        meter.ReportSummon(77, 1);

        var detail = meter.Detail(1)!;

        Assert.Equal(1250, detail.Damage);
        Assert.Contains(detail.Skills, s => s.Skill == 100_044 && s.Kind == HitKind.Pet);
    }

    [Fact]
    public void SkillNamesAreStoredOnlyInOwnFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"aion2dps-skills-{Guid.NewGuid():N}");
        try
        {
            var path = Path.Combine(dir, "skills.json");
            var store = new SkillNameStore(path);
            store.Load();
            Assert.Null(store.NameOf(14_020_340));
            Assert.True(store.SetName(14_020_340, "  Schnellschuss  "));
            Assert.True(store.SetName(14_340_230, "Pfeilhagel"));
            Assert.True(store.SetName(14_340_230, "")); // löschen

            var again = new SkillNameStore(path);
            again.Load();
            Assert.Equal("Schnellschuss", again.NameOf(14_020_340));
            Assert.Null(again.NameOf(14_340_230));

            File.WriteAllText(path, "fremd");
            var foreign = new SkillNameStore(path);
            foreign.Load();
            Assert.True(foreign.IsForeign);
            Assert.False(foreign.SetName(1, "x"));
            Assert.Equal("fremd", File.ReadAllText(path));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(9, "0:09")]
    [InlineData(75, "1:15")]
    [InlineData(3725, "1:02:05")]
    public void ElapsedFormat(int seconds, string expected)
    {
        Assert.Equal(expected, OverlayFormat.Elapsed(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void ClipboardTextListsRows()
    {
        var meter = new LiveCombatMeter();
        meter.ReportPlayer(1, "Lyra", isSelf: true);
        meter.Add(Hit(0, 1, 900, 14_020_340, 93_000));
        meter.Add(Hit(9, 1, 900, 14_020_340, 1));

        var content = OverlayFormat.Build(meter.Snapshot(T0.AddSeconds(9)), 8);
        var text = OverlayFormat.ToClipboardText(content);

        Assert.Contains("AionMeta – Kampf 0:09", text);
        Assert.Contains("1. Lyra (Bogenschütze): 93,0K", text);
        Assert.Equal(1u, content.Rows[0].Actor);
        Assert.Equal("100,0 %", content.Rows[0].Share);
        Assert.Equal("AionMeta – noch keine Treffer", OverlayFormat.ToClipboardText(OverlayFormat.Build(new LiveCombatMeter().Snapshot(T0), 8)));
    }
}
