using Microsoft.Win32;

namespace AiO.Core.Asus;

/// <summary>Why the ASUS (G-Helper) controls are or aren't available on this PC.</summary>
public sealed record AsusDetection(string Manufacturer, string Model, bool DriverOpened, int Win32Error, string Message)
{
    private const int ErrorFileNotFound = 2;
    private const int ErrorAccessDenied = 5;

    public bool IsAsus => Manufacturer.Contains("ASUS", StringComparison.OrdinalIgnoreCase);

    public static AsusDetection Describe(string manufacturer, string model, bool driverOpened, int win32Error)
    {
        bool isAsus = manufacturer.Contains("ASUS", StringComparison.OrdinalIgnoreCase);
        string message = (driverOpened, isAsus, win32Error) switch
        {
            (true, _, _) => "ASUS ACPI driver connected.",
            (false, false, _) =>
                $"This PC reports its maker as \"{(manufacturer.Length > 0 ? manufacturer : "unknown")}\". " +
                "The G-Helper controls only work on ASUS ROG, TUF, Zephyrus, Strix, Flow and Vivobook/Zenbook laptops.",
            (false, true, ErrorAccessDenied) =>
                "The ASUS driver is installed but refused access. Restart AiO as administrator (Settings page) and try again.",
            (false, true, ErrorFileNotFound) =>
                "This is an ASUS laptop, but the \"ASUS System Control Interface\" driver isn't installed. " +
                "Install it from Windows Update (optional driver updates) or ASUS's support page for your model, then restart AiO. " +
                "You don't need Armoury Crate itself.",
            (false, true, _) =>
                $"This is an ASUS laptop, but the ASUS ACPI driver couldn't be opened (Windows error {win32Error}). " +
                "Reinstalling \"ASUS System Control Interface\" usually fixes this.",
        };
        return new AsusDetection(manufacturer, model, driverOpened, win32Error, message);
    }
}

public static class SystemInfo
{
    /// <summary>System manufacturer and model as reported by the BIOS (no WMI needed).</summary>
    public static (string Manufacturer, string Model) ReadBios()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
            return ((key?.GetValue("SystemManufacturer") as string ?? "").Trim(),
                    (key?.GetValue("SystemProductName") as string ?? "").Trim());
        }
        catch
        {
            return ("", "");
        }
    }
}
