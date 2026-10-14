using Aion2DpsMeter.App;

namespace Aion2DpsMeter.App.Tests;

public class StartupErrorLogTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"aion2dps-log-{Guid.NewGuid():N}");
    private static readonly DateTimeOffset Time = new(2026, 10, 12, 1, 10, 35, TimeSpan.FromHours(2));

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void Append_CreatesDirectoryAndWritesEntry()
    {
        var path = StartupErrorLog.Append(new InvalidOperationException("Testfehler"), _directory, Time);

        Assert.Equal(Path.Combine(_directory, "startfehler.log"), path);
        var text = File.ReadAllText(path!);
        Assert.Contains("===== 2026-10-12 01:10:35 +02:00 =====", text);
        Assert.Contains("System.InvalidOperationException: Testfehler", text);
    }

    [Fact]
    public void Append_Twice_KeepsBothEntries()
    {
        StartupErrorLog.Append(new Exception("erster"), _directory, Time);
        var path = StartupErrorLog.Append(new Exception("zweiter"), _directory, Time.AddMinutes(1));

        var text = File.ReadAllText(path!);
        Assert.Contains("erster", text);
        Assert.Contains("zweiter", text);
    }

    [Fact]
    public void Format_IncludesInnerException()
    {
        var ex = new Exception("außen", new FileLoadException("Datei blockiert (0x800711C7)"));

        var text = StartupErrorLog.Format(ex, Time);

        Assert.Contains("außen", text);
        Assert.Contains("Datei blockiert (0x800711C7)", text);
    }

    [Fact]
    public void Append_UnwritableLocation_ReturnsNullInsteadOfThrowing()
    {
        // Eine Datei an der Stelle des Ordners verhindert das Anlegen des Ordners.
        Directory.CreateDirectory(_directory);
        var blocker = Path.Combine(_directory, "kein-ordner");
        File.WriteAllText(blocker, "x");

        Assert.Null(StartupErrorLog.Append(new Exception("x"), blocker, Time));
    }
}
