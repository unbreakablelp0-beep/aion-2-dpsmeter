using System.Security.Cryptography;
using Aion2DpsMeter.Core.Updates;

namespace Aion2DpsMeter.Core.Tests.Updates;

/// <summary>Update-Prüfung ohne Netz: Antwort lesen, Versionen, erlaubte Adressen, Prüfsummen, Austausch der EXE.</summary>
public class UpdateCheckTests
{
    private const string Base = "https://github.com/unbreakablelp0-beep/aion-2-dpsmeter/releases/download";

    private static string Release(string tag, string assets, bool draft = false, bool pre = false) => $$"""
        {"tag_name":"{{tag}}","draft":{{(draft ? "true" : "false")}},"prerelease":{{(pre ? "true" : "false")}},
         "html_url":"https://github.com/unbreakablelp0-beep/aion-2-dpsmeter/releases/tag/{{tag}}",
         "body":"## Neu\r\n- Boss-Karte rot\r\n- Update per Klick","assets":[{{assets}}]}
        """;

    [Fact]
    public void ComparesSemVerTagsAndIgnoresOldOnes()
    {
        Assert.Equal((0, 0, 1), UpdateCheck.ParseTag("v0.0.1"));
        Assert.Equal((1, 2, 3), UpdateCheck.ParseTag("1.2.3"));
        Assert.Null(UpdateCheck.ParseTag("v43"));        // altes Schema → ignorieren
        Assert.Null(UpdateCheck.ParseTag("v0.0.2-beta")); // Vorabversion
        Assert.Null(UpdateCheck.ParseTag("neu"));
        Assert.True(UpdateCheck.IsNewer("v0.0.10", "0.0.9"));
        Assert.True(UpdateCheck.IsNewer("v0.1.0", "0.0.99"));
        Assert.True(UpdateCheck.IsNewer("v1.0.0", "0.9.9"));
        Assert.False(UpdateCheck.IsNewer("v0.0.1", "0.0.1"));
        Assert.False(UpdateCheck.IsNewer("v43", "0.0.1"));
        Assert.Equal("0.0.1", AppRelease.Current);
    }

    [Fact]
    public void PicksHighestSemVerReleaseNotBlindlyLatest()
    {
        var list = "[" + string.Join(",",
            Release("v43", ""),                 // altes Release ohne SemVer
            Release("v0.0.9", ""),
            Release("v0.0.12", "", pre: true), // Vorabversion
            Release("v0.0.10", ""),
            Release("v0.0.11", "", draft: true)) + "]";
        Assert.Equal("v0.0.10", UpdateCheck.ParseReleases(list)!.Tag);
        Assert.Null(UpdateCheck.ParseReleases("[" + Release("v43", "") + "]"));
        Assert.Null(UpdateCheck.ParseReleases("""{"message":"API rate limit exceeded"}"""));
    }

    [Fact]
    public void ReadsLatestReleaseWithDigest()
    {
        var sha = new string('a', 64);
        var json = Release("v0.0.2", $$"""{"name":"Aion2DpsMeter-0.0.2-win-x64.exe","size":1234,"digest":"sha256:{{sha}}","browser_download_url":"{{Base}}/v0.0.2/Aion2DpsMeter-0.0.2-win-x64.exe"}""");
        var info = UpdateCheck.ParseLatest(json)!;
        Assert.Equal("v0.0.2", info.Tag);
        Assert.Equal("Aion2DpsMeter-0.0.2-win-x64.exe", info.AssetName);
        Assert.Equal(1234, info.AssetSize);
        Assert.Equal(sha, info.Sha256);
        Assert.True(info.CanInstall);
        Assert.Equal("Neu\n- Boss-Karte rot\n- Update per Klick", UpdateCheck.ShortNotes(info.Notes));
    }

    [Fact]
    public void WithoutChecksumOrExeItCannotInstall()
    {
        var noDigest = UpdateCheck.ParseLatest(Release("v0.0.2", $$"""{"name":"a.exe","size":1,"browser_download_url":"{{Base}}/v0.0.2/a.exe"}"""))!;
        Assert.Null(noDigest.Sha256);
        Assert.False(noDigest.CanInstall); // dann nur die Download-Seite öffnen

        var withSums = UpdateCheck.ParseLatest(Release("v0.0.2",
            $$"""{"name":"a.exe","size":1,"browser_download_url":"{{Base}}/v0.0.2/a.exe"},{"name":"SHA256SUMS","size":80,"browser_download_url":"{{Base}}/v0.0.2/SHA256SUMS"}"""))!;
        Assert.NotNull(withSums.SumsUrl);

        var noExe = UpdateCheck.ParseLatest(Release("v0.0.2", ""))!;
        Assert.Null(noExe.AssetUrl);
        Assert.False(noExe.CanInstall);

        Assert.Null(UpdateCheck.ParseLatest(Release("v0.0.2", "", draft: true)));
        Assert.Null(UpdateCheck.ParseLatest(Release("v0.0.2", "", pre: true)));
        Assert.Null(UpdateCheck.ParseLatest("kein json"));
        Assert.Null(UpdateCheck.ParseLatest("""{"message":"API rate limit exceeded"}"""));
    }

    [Fact]
    public void OnlyAllowedHostsAndThisRepository()
    {
        Assert.True(UpdateCheck.IsAllowedDownload(new Uri($"{Base}/v0.0.2/a.exe")));
        Assert.True(UpdateCheck.IsAllowedDownload(new Uri("https://objects.githubusercontent.com/github-production-release-asset/1")));
        Assert.True(UpdateCheck.IsAllowedDownload(new Uri("https://release-assets.githubusercontent.com/github-production-release-asset/1")));
        Assert.False(UpdateCheck.IsAllowedDownload(new Uri("http://github.com/unbreakablelp0-beep/aion-2-dpsmeter/releases/download/v0.0.2/a.exe"))); // kein HTTPS
        Assert.False(UpdateCheck.IsAllowedDownload(new Uri("https://github.com/fremd/repo/releases/download/v0.0.2/a.exe")));      // anderes Repo
        Assert.False(UpdateCheck.IsAllowedDownload(new Uri("https://example.com/a.exe")));

        var foreign = UpdateCheck.ParseLatest(Release("v0.0.2", $$"""{"name":"a.exe","size":1,"digest":"sha256:{{new string('b', 64)}}","browser_download_url":"https://example.com/a.exe"}"""))!;
        Assert.False(foreign.CanInstall);
    }

    [Fact]
    public void ChecksumsAndProgramHeader()
    {
        var data = new byte[2048];
        data[0] = (byte)'M';
        data[1] = (byte)'Z';
        var sha = Convert.ToHexStringLower(SHA256.HashData(data));
        Assert.True(UpdateCheck.MatchesSha256(data, sha.ToUpperInvariant()));
        Assert.False(UpdateCheck.MatchesSha256(data, new string('0', 64)));
        Assert.False(UpdateCheck.MatchesSha256(data, "zu-kurz"));
        Assert.True(UpdateCheck.LooksLikeExe(data));
        Assert.False(UpdateCheck.LooksLikeExe(new byte[2048]));

        var sums = $"{new string('1', 64)}  andere.exe\n{sha} *Aion2DpsMeter-0.0.2-win-x64.exe\n";
        Assert.Equal(sha, UpdateCheck.FindSha256(sums, "Aion2DpsMeter-0.0.2-win-x64.exe"));
        Assert.Null(UpdateCheck.FindSha256(sums, "fehlt.exe"));
    }

    [Fact]
    public void InstallerSwapsExeAndCleansUp()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"aion2dps-update-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var exe = Path.Combine(directory, "Aion2DpsMeter.App.exe");
            File.WriteAllText(exe, "alt");
            UpdateInstaller.Install(exe, "neu"u8.ToArray());
            Assert.Equal("neu", File.ReadAllText(exe));
            Assert.Equal("alt", File.ReadAllText(UpdateInstaller.OldPath(exe)));
            Assert.False(File.Exists(UpdateInstaller.NewPath(exe)));

            UpdateInstaller.Install(exe, "neuer"u8.ToArray()); // vorhandene .old eines früheren Updates stört nicht
            Assert.Equal("neuer", File.ReadAllText(exe));

            UpdateInstaller.CleanUp(exe);
            Assert.False(File.Exists(UpdateInstaller.OldPath(exe)));
            Assert.True(File.Exists(exe));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void UpdateCheckIsOnByDefault()
    {
        Assert.True(new Aion2DpsMeter.Core.Combat.OverlaySettings().Normalized().CheckForUpdates);
    }
}
