using AiO.Core.Updates;
using Xunit;

namespace AiO.Core.Tests;

public class UpdateTests
{
    private const string Sha = "f428cf0a1b2c3d4e5f60718293a4b5c6d7e8f901";
    private const string OtherSha = "83f8336a1b2c3d4e5f60718293a4b5c6d7e8f902";

    private static string ReleaseJson(string target, string body = "") => $$"""
        {
          "tag_name": "latest",
          "target_commitish": "{{target}}",
          "body": "{{body}}",
          "published_at": "2026-10-20T12:00:00Z",
          "assets": [
            { "name": "other.txt", "browser_download_url": "https://example.com/other.txt", "size": 1 },
            { "name": "AiO-win-x64.zip", "browser_download_url": "https://example.com/AiO-win-x64.zip", "size": 1234 }
          ]
        }
        """;

    [Theory]
    [InlineData("0.3.0+" + Sha, Sha)]
    [InlineData("0.3.0", "")]
    [InlineData("0.3.0+local", "")]
    [InlineData(null, "")]
    public void ReadsCommitFromInformationalVersion(string? version, string expected) =>
        Assert.Equal(expected, AppUpdater.CommitFromInformationalVersion(version));

    [Fact]
    public void ParsesRelease()
    {
        var release = AppUpdater.ParseRelease(ReleaseJson(Sha));
        Assert.Equal(Sha, release.Commit);
        Assert.Equal("f428cf0", release.ShortCommit);
        Assert.Equal("https://example.com/AiO-win-x64.zip", release.DownloadUrl);
        Assert.Equal(1234, release.Size);
        Assert.Equal(2026, release.Published.Year);
    }

    [Fact]
    public void FallsBackToCommitInNotes()
    {
        var release = AppUpdater.ParseRelease(ReleaseJson("master", $"Built from {Sha}. Unzip and run AiO.exe."));
        Assert.Equal(Sha, release.Commit);
    }

    [Fact]
    public void MissingAssetThrows() =>
        Assert.Throws<InvalidOperationException>(() => AppUpdater.ParseRelease("""{ "target_commitish": "x", "assets": [] }"""));

    [Fact]
    public void ComparesCommits()
    {
        var release = AppUpdater.ParseRelease(ReleaseJson(Sha));
        Assert.Equal(UpdateState.UpToDate, AppUpdater.Compare(Sha.ToUpperInvariant(), release));
        Assert.Equal(UpdateState.Available, AppUpdater.Compare(OtherSha, release));
        Assert.Equal(UpdateState.Unknown, AppUpdater.Compare("", release));
    }

    [Fact]
    public void InstallScriptQuotesPaths()
    {
        string script = AppUpdater.BuildInstallScript(42, @"C:\Temp\it's new", @"C:\Apps\AiO", "--restarted");
        Assert.Contains("'C:\\Temp\\it''s new'", script);
        Assert.Contains("Wait-Process -Id 42", script);
        Assert.Contains("'--restarted'", script);
    }
}
