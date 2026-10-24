using System.Diagnostics;
using Microsoft.Win32;

namespace AiO.App.Infrastructure;

/// <summary>Start-with-Windows registration and elevation.</summary>
public static class SystemIntegration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "AiO";
    public const string MinimizedArgument = "--minimized";
    public const string RestartedArgument = "--restarted";

    public static string ExecutablePath => Environment.ProcessPath ?? throw new InvalidOperationException("Unknown executable path");

    /// <summary>
    /// When running as administrator, registers a logon task with highest privileges so AiO starts
    /// elevated without a UAC prompt (like G-Helper). Otherwise uses the per-user Run key.
    /// </summary>
    public static void SetStartWithWindows(bool enabled)
    {
        using (var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true))
        {
            if (enabled && !AppServices.IsAdministrator)
                key.SetValue(AppName, $"\"{ExecutablePath}\" {MinimizedArgument}");
            else
                key.DeleteValue(AppName, throwOnMissingValue: false);
        }

        if (AppServices.IsAdministrator)
        {
            if (enabled)
                RunSchtasks($"/Create /F /TN \"{AppName}\" /SC ONLOGON /RL HIGHEST /TR \"\\\"{ExecutablePath}\\\" {MinimizedArgument}\"");
            else
                RunSchtasks($"/Delete /F /TN \"{AppName}\"");
        }
    }

    private static void RunSchtasks(string arguments)
    {
        var info = new ProcessStartInfo("schtasks.exe", arguments) { CreateNoWindow = true, UseShellExecute = false };
        using var process = Process.Start(info);
        process?.WaitForExit(10_000);
    }

    /// <summary>Relaunches elevated. Returns false if the user declined the UAC prompt.</summary>
    public static bool RestartAsAdministrator()
    {
        try
        {
            Process.Start(new ProcessStartInfo(ExecutablePath, RestartedArgument) { UseShellExecute = true, Verb = "runas" });
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    public static void OpenFolder(string path) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });

    public static void OpenUrl(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    public static void RestartWindows() =>
        Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 0") { CreateNoWindow = true, UseShellExecute = false });
}
