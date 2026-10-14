using Aion2DpsMeter.Core.Combat;

namespace Aion2DpsMeter.Core.Tests.Combat;

/// <summary>Kontraststufen: Kontrastverhältnis nach WCAG 2 über typischen Spielhintergründen.</summary>
public class OverlayContrastTests
{
    // Weiß (Schnee, Himmel), Schwarz, kräftiges Rot (Wald wie im Screenshot), Grün, Hautton/Sand.
    public static TheoryData<int> Backgrounds => new() { 0xFFFFFF, 0x000000, 0xC02020, 0x30A040, 0xE0C090 };

    [Fact]
    public void RatioMatchesKnownValues()
    {
        Assert.Equal(21, OverlayContrast.Ratio(0xFFFFFF, 0x000000), 1);
        Assert.Equal(1, OverlayContrast.Ratio(0x777777, 0x777777), 3);
        Assert.Equal(4.48, OverlayContrast.Ratio(0x777777, 0xFFFFFF), 2);
    }

    [Theory]
    [MemberData(nameof(Backgrounds))]
    public void HighLevelsReachAaForSecondaryTextEvenAtLowestOpacity(int behind)
    {
        foreach (var level in new[] { OverlayContrast.High, OverlayContrast.VeryHigh })
        {
            var style = OverlayContrast.For(level);
            var panel = OverlayContrast.Composite(OverlayContrast.EffectiveOpacity(0.1, level), behind); // Regler ganz unten
            Assert.True(OverlayContrast.Ratio(style.Secondary, panel) >= 4.5, $"{level} über {behind:X6}: {OverlayContrast.Ratio(style.Secondary, panel):0.00}");
            Assert.True(OverlayContrast.Ratio(style.Name, panel) >= 4.5);
        }

        Assert.True(OverlayContrast.Ratio(OverlayContrast.For(OverlayContrast.VeryHigh).Secondary,
            OverlayContrast.Composite(0.9, behind)) >= 7); // „Sehr hoch“: AAA
    }

    [Fact]
    public void DefaultIsHighAndUnknownFallsBack()
    {
        Assert.Equal(OverlayContrast.High, new OverlaySettings().Normalized().Contrast);
        Assert.Equal(OverlayContrast.High, new OverlaySettings(Contrast: "Neon").Normalized().Contrast);
        Assert.Equal(OverlayContrast.Normal, new OverlaySettings(Contrast: "Normal").Normalized().Contrast);
        Assert.Equal(0.75, OverlayContrast.EffectiveOpacity(0.6, OverlayContrast.High));
        Assert.Equal(0.6, OverlayContrast.EffectiveOpacity(0.6, OverlayContrast.Normal));
        Assert.Equal(0.95, OverlayContrast.EffectiveOpacity(0.95, OverlayContrast.High));
    }
}
