using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace AiO.Core.Updates;

/// <summary>A published AiO build: the "latest" release CI makes on every push to master.</summary>
public sealed record AppRelease(string Commit, DateTimeOffset Published, string DownloadUrl, long Size)
{
    public string ShortCommit => Commit.Length > 7 ? Commit[..7] : Commit;
}

public enum UpdateState
{
    UpToDate,
    Available,
    /// <summary>This build doesn't know which commit it came from (a local build).</summary>
    Unknown,
}

/// <summary>Checks GitHub for a newer AiO build, downloads it and swaps it in.</summary>
public static partial class AppUpdater
{
    public const string ReleasesUrl = "https://github.com/tacobellerontop-sudo/AiO-/releases/tag/latest";
    private const string ReleaseApi = "https://api.github.com/repos/tacobellerontop-sudo/AiO-/releases/tags/latest";
    public const string AssetName = "AiO-win-x64.zip";

    /// <summary>Git commit this build came from ("" when unknown).</summary>
    public static string CurrentCommit { get; } = CommitFromInformationalVersion(
        Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    /// <summary>"0.3.0+abc123..." → "abc123..." (the SDK appends the source revision after '+').</summary>
    public static string CommitFromInformationalVersion(string? version)
    {
        if (string.IsNullOrEmpty(version)) return "";
        int plus = version.IndexOf('+');
        if (plus < 0) return "";
        string commit = version[(plus + 1)..].Trim();
        return IsSha(commit) ? commit.ToLowerInvariant() : "";
    }

    /// <summary>Reads the release JSON GitHub returns for the "latest" tag.</summary>
    public static AppRelease ParseRelease(string json)
    {
        var release = JsonNode.Parse(json) ?? throw new InvalidOperationException("Empty release information.");
        var asset = release["assets"]?.AsArray()
                        .FirstOrDefault(a => string.Equals((string?)a?["name"], AssetName, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException($"The latest release has no {AssetName}.");

        // CI creates the release with --target <sha>; the notes also say "Built from <sha>".
        string commit = (string?)release["target_commitish"] ?? "";
        if (!IsSha(commit))
            commit = ShaPattern().Match((string?)release["body"] ?? "").Value;

        var published = DateTimeOffset.TryParse((string?)release["published_at"] ?? (string?)release["created_at"], out var p) ? p : DateTimeOffset.MinValue;
        return new AppRelease(commit.ToLowerInvariant(), published,
            (string?)asset["browser_download_url"] ?? throw new InvalidOperationException("The release asset has no download link."),
            (long?)asset["size"] ?? 0);
    }

    public static UpdateState Compare(string currentCommit, AppRelease release)
    {
        if (string.IsNullOrEmpty(currentCommit) || string.IsNullOrEmpty(release.Commit)) return UpdateState.Unknown;
        return string.Equals(currentCommit, release.Commit, StringComparison.OrdinalIgnoreCase) ? UpdateState.UpToDate : UpdateState.Available;
    }

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AiO", "1.0"));
        return http;
    }

    public static async Task<AppRelease> GetLatestAsync(CancellationToken ct = default)
    {
        using var http = CreateClient();
        return ParseRelease(await http.GetStringAsync(ReleaseApi, ct));
    }

    /// <summary>Downloads and unpacks <paramref name="release"/>; returns the folder holding the new AiO.exe.</summary>
    public static async Task<string> DownloadAsync(AppRelease release, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        string root = Path.Combine(Path.GetTempPath(), "AiO-update");
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        Directory.CreateDirectory(root);
        string zip = Path.Combine(root, AssetName);

        using (var http = CreateClient())
        using (var response = await http.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            long total = response.Content.Headers.ContentLength ?? release.Size;
            await using var source = await response.Content.ReadAsStreamAsync(ct);
            await using var target = File.Create(zip);
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), ct);
                done += read;
                if (total > 0) progress?.Report((double)done / total);
            }
        }

        string staging = Path.Combine(root, "files");
        ZipFile.ExtractToDirectory(zip, staging);
        // Compress-Archive puts the files at the root, but accept a single wrapping folder too.
        string? exe = Directory.EnumerateFiles(staging, "AiO.exe", SearchOption.AllDirectories)
                          .OrderBy(f => f.Length).FirstOrDefault()
                      ?? throw new InvalidOperationException("The download doesn't contain AiO.exe.");
        return Path.GetDirectoryName(exe)!;
    }

    /// <summary>
    /// Starts a hidden helper that waits for this process to exit, copies <paramref name="newFiles"/>
    /// over <paramref name="installFolder"/> and starts AiO again. The caller should exit right after.
    /// </summary>
    public static void StartInstaller(string newFiles, string installFolder, string restartArguments, bool elevate)
    {
        string script = BuildInstallScript(Environment.ProcessId, newFiles, installFolder, restartArguments);
        string path = Path.Combine(Path.GetTempPath(), "AiO-update", "install.ps1");
        File.WriteAllText(path, script, new UTF8Encoding(true));

        var start = new ProcessStartInfo("powershell.exe",
            $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{path}\"")
        {
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        if (elevate) start.Verb = "runas";
        Process.Start(start);
    }

    public static string BuildInstallScript(int processId, string newFiles, string installFolder, string restartArguments)
    {
        static string Quote(string s) => "'" + s.Replace("'", "''") + "'";
        return $$"""
            $ErrorActionPreference = 'Stop'
            $source = {{Quote(newFiles)}}
            $target = {{Quote(installFolder)}}
            $log = Join-Path $env:TEMP 'AiO-update.log'
            try {
                Wait-Process -Id {{processId}} -Timeout 60 -ErrorAction SilentlyContinue
                $copied = $false
                for ($i = 0; $i -lt 20 -and -not $copied; $i++) {
                    try {
                        Copy-Item -Path (Join-Path $source '*') -Destination $target -Recurse -Force
                        $copied = $true
                    } catch {
                        Start-Sleep -Milliseconds 500
                        if ($i -eq 19) { throw }
                    }
                }
                "Updated $(Get-Date)" | Out-File $log
            } catch {
                "Update failed $(Get-Date): $_" | Out-File $log
            }
            Start-Process -FilePath (Join-Path $target 'AiO.exe') -ArgumentList {{Quote(restartArguments)}}
            """;
    }

    /// <summary>True when this process can write into <paramref name="folder"/> (false under Program Files).</summary>
    public static bool CanWrite(string folder)
    {
        try
        {
            string probe = Path.Combine(folder, $".aio-write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    private static bool IsSha(string s) => s.Length == 40 && s.All(Uri.IsHexDigit);

    [GeneratedRegex("(?<![0-9a-fA-F])[0-9a-fA-F]{40}(?![0-9a-fA-F])")]
    private static partial Regex ShaPattern();
}
