using Aion2DpsMeter.Core.Capture;

namespace Aion2DpsMeter.Core.Tests.Capture;

/// <summary>Automatische Wahl des Spielprogramms für den Ein-Klick-Start (simulierte Programmliste).</summary>
public class GameProcessFinderTests
{
    private static ProcessConnectionGroup Group(int pid, string name, int connections) =>
        new(pid, name, Enumerable.Range(0, connections)
            .Select(i => new ProcessConnection(pid, TransportProtocol.Tcp, "192.0.2.10", 50000 + i))
            .ToList());

    [Fact]
    public void PicksGameAndIgnoresLauncherAndOthers()
    {
        var groups = new[]
        {
            Group(1, "chrome", 30),
            Group(2, "AION2Launcher", 9),
            Group(3, "AION2", 4),
            Group(4, "Aion2CrashReporter", 1),
        };

        Assert.Equal(3, ProcessConnectionFilter.FindGameProcess(groups)?.ProcessId);
    }

    [Fact]
    public void PrefersExactNameThenMostConnections()
    {
        var groups = new[] { Group(5, "aion2-client", 8), Group(6, "AION2", 2), Group(7, "AION 2 Game", 3) };

        Assert.Equal(6, ProcessConnectionFilter.FindGameProcess(groups)?.ProcessId);
        Assert.Equal(5, ProcessConnectionFilter.FindGameProcess(groups.Where(g => g.ProcessId != 6))?.ProcessId);
    }

    [Fact]
    public void NullWhenGameNotRunning()
    {
        Assert.Null(ProcessConnectionFilter.FindGameProcess([Group(1, "chrome", 3), Group(2, "AION2Launcher", 2)]));
    }
}
