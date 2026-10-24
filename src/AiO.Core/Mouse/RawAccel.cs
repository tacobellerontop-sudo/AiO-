using System.Diagnostics;
using AiO.Core.Native;

namespace AiO.Core.Mouse;

public sealed record RawAccelApplyResult(bool Success, string Message);

/// <summary>
/// Integration with RawAccel (https://github.com/a1xd/rawaccel), the open-source, signed mouse
/// acceleration driver. AiO edits RawAccel's settings.json and asks RawAccel's writer.exe to push it
/// to the driver, so AiO and RawAccel's own GUI stay in sync.
/// </summary>
public static class RawAccel
{
    public const string DownloadUrl = "https://github.com/a1xd/rawaccel/releases";
    public const string SettingsFileName = "settings.json";
    public const string WriterFileName = "writer.exe";

    /// <summary>True when the RawAccel driver is installed and running (needs a reboot after installing).</summary>
    public static bool IsDriverInstalled()
    {
        using var handle = Kernel32.CreateFile(@"\\.\rawaccel", 0, 0, IntPtr.Zero, Kernel32.OPEN_EXISTING, 0, IntPtr.Zero);
        return !handle.IsInvalid;
    }

    /// <summary>A folder is usable if it holds RawAccel's writer.exe (the release zip's folder).</summary>
    public static bool IsValidFolder(string? folder) =>
        !string.IsNullOrWhiteSpace(folder) && File.Exists(Path.Combine(folder, WriterFileName));

    /// <summary>Looks in the usual places people unzip RawAccel.</summary>
    public static string? FindFolder()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] roots =
        [
            Path.Combine(home, "Downloads"),
            Path.Combine(home, "Desktop"),
            Path.Combine(home, "Documents"),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            @"C:\",
        ];
        foreach (var root in roots.Where(Directory.Exists))
        {
            try
            {
                foreach (var dir in Directory.EnumerateDirectories(root, "*rawaccel*", SearchOption.TopDirectoryOnly))
                {
                    if (IsValidFolder(dir)) return dir;
                    // Release zips often extract to rawaccel_vX/rawaccel/.
                    var nested = Directory.EnumerateDirectories(dir).FirstOrDefault(IsValidFolder);
                    if (nested is not null) return nested;
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }
        return null;
    }

    public static string SettingsPath(string folder) => Path.Combine(folder, SettingsFileName);

    public static RawAccelProfile? ReadProfile(string folder)
    {
        string path = SettingsPath(folder);
        if (!File.Exists(path)) return null;
        try { return RawAccelSettingsFile.ReadFirstProfile(File.ReadAllText(path)); }
        catch (System.Text.Json.JsonException) { return null; }
    }

    /// <summary>Saves the profile into RawAccel's settings.json (backing up the original once) and applies it.</summary>
    public static RawAccelApplyResult Apply(string folder, RawAccelProfile profile)
    {
        if (!IsValidFolder(folder)) return new(false, "Choose the RawAccel folder (the one with writer.exe) first.");
        if (!IsDriverInstalled())
            return new(false, "The RawAccel driver isn't running. Run installer.exe from the RawAccel folder, then restart Windows.");

        string path = SettingsPath(folder);
        string? existing = File.Exists(path) ? File.ReadAllText(path) : null;
        if (existing is not null && !File.Exists(path + ".aio-backup")) File.WriteAllText(path + ".aio-backup", existing);
        File.WriteAllText(path, RawAccelSettingsFile.WriteFirstProfile(existing, profile));
        return RunWriter(folder);
    }

    /// <summary>Re-sends the current settings.json (the driver forgets its settings on reboot).</summary>
    public static RawAccelApplyResult Reapply(string folder) =>
        IsValidFolder(folder) && File.Exists(SettingsPath(folder)) && IsDriverInstalled()
            ? RunWriter(folder)
            : new(false, "RawAccel isn't set up.");

    private static RawAccelApplyResult RunWriter(string folder)
    {
        var info = new ProcessStartInfo(Path.Combine(folder, WriterFileName), $"\"{SettingsPath(folder)}\"")
        {
            WorkingDirectory = folder,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var process = Process.Start(info);
        if (process is null) return new(false, "Could not start RawAccel's writer.exe.");
        // writer.exe shows its own message box on bad settings and waits for it to be closed.
        if (!process.WaitForExit(30_000)) return new(false, "RawAccel's writer.exe didn't finish; check for a message box from it.");
        return process.ExitCode == 0
            ? new(true, "RawAccel settings applied.")
            : new(false, "RawAccel's writer.exe reported a problem (see its message box).");
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
