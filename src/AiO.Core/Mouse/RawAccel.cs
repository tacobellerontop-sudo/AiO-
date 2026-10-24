using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using AiO.Core.Native;

namespace AiO.Core.Mouse;

public sealed record RawAccelApplyResult(bool Success, string Message);

/// <summary>
/// AiO's mouse acceleration engine. The kernel-mode part is RawAccel's signed driver
/// (https://github.com/a1xd/rawaccel, MIT, by a1xd), shipped inside AiO's folder; AiO itself provides
/// the curve editor, the settings and the start-up handling. RawAccel's own GUI isn't used.
/// </summary>
public static class RawAccel
{
    public const string ProjectUrl = "https://github.com/a1xd/rawaccel";
    private const string LatestReleaseApi = "https://api.github.com/repos/a1xd/rawaccel/releases/latest";
    public const string WriterFileName = "writer.exe";
    public const string InstallerFileName = "installer.exe";
    public const string UninstallerFileName = "uninstaller.exe";

    /// <summary>Where the Windows build puts the driver package: next to AiO.exe.</summary>
    public static string BundledFolder => Path.Combine(AppContext.BaseDirectory, "RawAccel");

    /// <summary>Where AiO fetches the package itself when a build doesn't include it.</summary>
    public static string DownloadedFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AiO", "RawAccel");

    /// <summary>AiO keeps the curve here (writable even when AiO lives in Program Files).</summary>
    public static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AiO", "mouse-acceleration.json");

    /// <summary>The driver package AiO will use, or null if it has none yet.</summary>
    public static string? PackageFolder =>
        IsValidFolder(BundledFolder) ? BundledFolder : IsValidFolder(DownloadedFolder) ? DownloadedFolder : null;

    /// <summary>True when the driver is installed and running (it needs a restart after installing).</summary>
    public static bool IsDriverInstalled()
    {
        using var handle = Kernel32.CreateFile(@"\\.\rawaccel", 0, 0, IntPtr.Zero, Kernel32.OPEN_EXISTING, 0, IntPtr.Zero);
        return !handle.IsInvalid;
    }

    public static bool IsValidFolder(string? folder) =>
        !string.IsNullOrWhiteSpace(folder) && File.Exists(Path.Combine(folder, WriterFileName));

    /// <summary>
    /// Downloads RawAccel's latest release package into <see cref="DownloadedFolder"/>
    /// (only needed when this AiO build doesn't already include it).
    /// </summary>
    public static async Task<string> DownloadPackageAsync(CancellationToken ct = default)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AiO", "1.0"));

        var release = JsonNode.Parse(await http.GetStringAsync(LatestReleaseApi, ct));
        string url = release?["assets"]?.AsArray()
                         .Select(a => (string?)a?["browser_download_url"])
                         .FirstOrDefault(u => u?.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) == true)
                     ?? throw new InvalidOperationException("RawAccel's release has no zip package.");

        string temp = Path.Combine(Path.GetTempPath(), $"aio-rawaccel-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            string zipPath = Path.Combine(temp, "package.zip");
            await using (var file = File.Create(zipPath))
            await using (var stream = await http.GetStreamAsync(url, ct))
                await stream.CopyToAsync(file, ct);

            string extracted = Path.Combine(temp, "x");
            ZipFile.ExtractToDirectory(zipPath, extracted);
            string root = Directory.EnumerateFiles(extracted, WriterFileName, SearchOption.AllDirectories)
                              .Select(Path.GetDirectoryName).FirstOrDefault()
                          ?? throw new InvalidOperationException("The RawAccel package didn't contain writer.exe.");

            if (Directory.Exists(DownloadedFolder)) Directory.Delete(DownloadedFolder, recursive: true);
            Directory.CreateDirectory(Path.GetDirectoryName(DownloadedFolder)!);
            CopyDirectory(root!, DownloadedFolder);
            return DownloadedFolder;
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch (IOException) { }
        }
    }

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from)) File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.EnumerateDirectories(from)) CopyDirectory(dir, Path.Combine(to, Path.GetFileName(dir)));
    }

    /// <summary>Installs (or removes) the driver with RawAccel's installer, elevated. A restart finishes it.</summary>
    public static RawAccelApplyResult RunInstaller(bool uninstall)
    {
        if (PackageFolder is not { } folder) return new(false, "The driver package isn't available yet.");
        string exe = Path.Combine(folder, uninstall ? UninstallerFileName : InstallerFileName);
        if (!File.Exists(exe)) return new(false, $"{Path.GetFileName(exe)} is missing from the driver package.");
        try
        {
            using var process = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas", WorkingDirectory = folder });
            process?.WaitForExit();
            return process?.ExitCode == 0
                ? new(true, uninstall ? "Driver removed. Restart Windows to finish." : "Driver installed. Restart Windows to start using it.")
                : new(false, "The driver installer reported a problem.");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return new(false, "Installing the driver needs administrator approval.");
        }
    }

    /// <summary>
    /// The settings format differs slightly between driver versions, so the first time round AiO asks the
    /// bundled writer for its own default file (it writes settings.json when started with no arguments,
    /// then shows a usage message, which AiO closes) and edits that.
    /// </summary>
    private static string? DefaultFromPackage(string folder)
    {
        string temp = Path.Combine(Path.GetTempPath(), $"aio-rawaccel-default-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            using var process = Process.Start(new ProcessStartInfo(Path.Combine(folder, WriterFileName))
            {
                WorkingDirectory = temp,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (process is null) return null;
            string file = Path.Combine(temp, "settings.json");
            for (int i = 0; i < 100 && !File.Exists(file); i++) Thread.Sleep(100);
            Thread.Sleep(200); // let it finish writing
            try { process.Kill(); } catch (InvalidOperationException) { }
            return File.Exists(file) ? File.ReadAllText(file) : null;
        }
        catch
        {
            return null;
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>The driver's own default settings file, kept once generated.</summary>
    private static string BaseSettingsPath => Path.ChangeExtension(SettingsPath, ".base.json");

    private static string? BaseSettings()
    {
        if (File.Exists(BaseSettingsPath)) return File.ReadAllText(BaseSettingsPath);
        // The writer only produces its default when the driver is installed.
        if (PackageFolder is not { } package || !IsDriverInstalled()) return null;
        string? generated = DefaultFromPackage(package);
        if (generated is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(BaseSettingsPath)!);
            File.WriteAllText(BaseSettingsPath, generated);
        }
        return generated;
    }

    /// <summary>Builds the driver's settings file from <paramref name="profile"/> and sends it to the driver.</summary>
    public static RawAccelApplyResult Apply(RawAccelProfile profile)
    {
        if (PackageFolder is null) return new(false, "Curve saved. Set up the driver above to use it.");
        if (!IsDriverInstalled()) return new(false, "Curve saved. Install the driver and restart Windows to use it.");

        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, RawAccelSettingsFile.WriteFirstProfile(BaseSettings(), profile));
        return RunWriter(PackageFolder);
    }

    /// <summary>Re-sends the saved curve (the driver forgets it on every restart).</summary>
    public static RawAccelApplyResult Reapply() =>
        PackageFolder is { } folder && File.Exists(SettingsPath) && IsDriverInstalled()
            ? RunWriter(folder)
            : new(false, "Mouse acceleration isn't set up.");

    private static RawAccelApplyResult RunWriter(string folder)
    {
        var info = new ProcessStartInfo(Path.Combine(folder, WriterFileName), $"\"{SettingsPath}\"")
        {
            WorkingDirectory = folder,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var process = Process.Start(info);
        if (process is null) return new(false, "Could not reach the acceleration driver.");
        // The driver's writer shows a message box on bad settings and waits for it to be closed.
        if (!process.WaitForExit(30_000)) return new(false, "The acceleration driver didn't respond; check for a message box.");
        return process.ExitCode == 0
            ? new(true, "Mouse acceleration applied.")
            : new(false, "The acceleration driver rejected these settings (see its message box).");
    }
}

/// <summary>Windows' own pointer settings (Settings → Mouse).</summary>
public static class WindowsMouse
{
    private const uint SPI_GETMOUSE = 0x0003;
    private const uint SPI_SETMOUSE = 0x0004;
    private const uint SPI_GETMOUSESPEED = 0x0070;
    private const uint SPI_SETMOUSESPEED = 0x0071;
    private const uint SPIF_UPDATEINIFILE_SENDCHANGE = 0x3;

    /// <summary>Pointer speed 1-20; 10 is the default and the only value with no scaling.</summary>
    public static int GetSpeed()
    {
        int speed = 10;
        User32.SystemParametersInfo(SPI_GETMOUSESPEED, 0, ref speed, 0);
        return speed;
    }

    public static bool SetSpeed(int speed) =>
        User32.SystemParametersInfo(SPI_SETMOUSESPEED, 0, (IntPtr)Math.Clamp(speed, 1, 20), SPIF_UPDATEINIFILE_SENDCHANGE);

    /// <summary>"Enhance pointer precision", Windows' built-in acceleration.</summary>
    public static bool GetEnhancePointerPrecision()
    {
        var values = new int[3];
        User32.SystemParametersInfo(SPI_GETMOUSE, 0, values, 0);
        return values[2] != 0;
    }

    public static bool SetEnhancePointerPrecision(bool enabled)
    {
        // Thresholds 6/10 with acceleration 1 are Windows' defaults when the box is ticked.
        int[] values = enabled ? [6, 10, 1] : [0, 0, 0];
        return User32.SystemParametersInfo(SPI_SETMOUSE, 0, values, SPIF_UPDATEINIFILE_SENDCHANGE);
    }
}
