using Aion2DpsMeter.Core.Combat;

namespace Aion2DpsMeter.Core.Tests.Combat;

/// <summary>▲/▼ beim Überholen: wer steigt, wer rutscht ab, neue Zeilen ohne Pfeil.</summary>
public class RankTrackerTests
{
    [Fact]
    public void ReportsOvertakesOnly()
    {
        var ranks = new RankTracker();
        Assert.Empty(ranks.Update([1, 2, 3])); // erster Stand: nichts

        var changes = ranks.Update([2, 1, 3]); // 2 überholt 1
        Assert.Equal(2, changes.Count);
        Assert.Equal(1, changes[2]);
        Assert.Equal(-1, changes[1]);

        Assert.Empty(ranks.Update([2, 1, 3])); // unverändert
    }

    [Fact]
    public void NewRowsGetNoArrowAndResetForgets()
    {
        var ranks = new RankTracker();
        ranks.Update([1, 2]);
        var changes = ranks.Update([3, 1, 2]); // 3 neu ganz oben, 1 und 2 rutschen ab
        Assert.False(changes.ContainsKey(3));
        Assert.Equal(-1, changes[1]);
        Assert.Equal(-1, changes[2]);

        ranks.Reset();
        Assert.Empty(ranks.Update([2, 1, 3]));
    }
}
