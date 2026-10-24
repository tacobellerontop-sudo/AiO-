using System.Text.Json;
using System.Text.Json.Serialization;
using AiO.Core.Asus;
using AiO.Core.Color;
using AiO.Core.Gpu;
using AiO.Core.Keyboard;

namespace AiO.Core.Settings;

public sealed class AppSettings
{
    public GeneralSettings General { get; set; } = new();
    public Dictionary<string, DisplayColorSettings> Displays { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public AsusSettings Asus { get; set; } = new();
    public Dictionary<string, RazerDeviceSettings> Razer { get; set; } = new();
    public OpenRgbSettings OpenRgb { get; set; } = new();
    public MouseSettings Mouse { get; set; } = new();
    public KeyboardSettings Keyboard { get; set; } = new();
}

public sealed class MouseSettings
{
    /// <summary>Folder of the RawAccel release (contains writer.exe).</summary>
    public string? RawAccelFolder { get; set; }
    /// <summary>RawAccel's driver forgets its curve on reboot; AiO re-sends it at start-up.</summary>
    public bool ReapplyRawAccelOnStartup { get; set; } = true;
}

public sealed class GeneralSettings
{
    public bool StartWithWindows { get; set; }
    public bool StartMinimized { get; set; } = true;
    public bool CloseToTray { get; set; } = true;
    /// <summary>Re-apply colour, ASUS and Razer settings when AiO starts.</summary>
    public bool ApplyOnStartup { get; set; } = true;
}

public sealed class DisplayColorSettings
{
    public ColorAdjustment Gamma { get; set; } = ColorAdjustment.Neutral;
    /// <summary>GPU driver colour values (vibrance, hue...) the user changed.</summary>
    public Dictionary<GpuColorProperty, int> Gpu { get; set; } = new();
}

public sealed class FanCurveSetting
{
    public int[] Temperatures { get; set; } = [];
    public int[] Duties { get; set; } = [];

    public static FanCurveSetting From(FanCurve c) => new()
    {
        Temperatures = c.Temperatures.Select(b => (int)b).ToArray(),
        Duties = c.Duties.Select(b => (int)b).ToArray(),
    };

    public FanCurve ToCurve() => new()
    {
        Temperatures = Temperatures.Select(v => (byte)Math.Clamp(v, 0, 255)).ToArray(),
        Duties = Duties.Select(v => (byte)Math.Clamp(v, 0, 100)).ToArray(),
    };
}

public sealed class AsusModeProfile
{
    public bool CustomFanCurves { get; set; }
    public FanCurveSetting? CpuCurve { get; set; }
    public FanCurveSetting? GpuCurve { get; set; }
    public FanCurveSetting? MidCurve { get; set; }

    public bool CustomPowerLimits { get; set; }
    public int SustainedWatts { get; set; } = 45;
    public int SlowWatts { get; set; } = 60;
    public int FastWatts { get; set; } = 80;
    public int GpuDynamicBoost { get; set; } = 15;
    public int GpuTempTarget { get; set; } = 87;
}

public sealed class AsusSettings
{
    public AsusPerformanceMode PerformanceMode { get; set; } = AsusPerformanceMode.Balanced;
    public bool SyncWindowsPowerMode { get; set; } = true;
    public int BatteryLimit { get; set; } = 100;
    public bool? PanelOverdrive { get; set; }
    public Dictionary<AsusPerformanceMode, AsusModeProfile> Profiles { get; set; } = new()
    {
        [AsusPerformanceMode.Silent] = new(),
        [AsusPerformanceMode.Balanced] = new(),
        [AsusPerformanceMode.Turbo] = new(),
    };

    /// <summary>Set once the user applies keyboard lighting in AiO; until then AiO leaves it alone.</summary>
    public bool ManageKeyboard { get; set; }
    public AuraMode AuraMode { get; set; } = AuraMode.Static;
    public string AuraColor { get; set; } = "#FF0000";
    public string AuraColor2 { get; set; } = "#0000FF";
    public AuraSpeed AuraSpeed { get; set; } = AuraSpeed.Normal;
    public int KeyboardBrightness { get; set; } = 3;

    public AsusModeProfile Profile(AsusPerformanceMode mode)
    {
        if (!Profiles.TryGetValue(mode, out var p)) Profiles[mode] = p = new AsusModeProfile();
        return p;
    }
}

public sealed class RazerDeviceSettings
{
    public string Name { get; set; } = "";
    public int? Dpi { get; set; }
    public int? PollingRate { get; set; }
    public string Effect { get; set; } = "Static";
    public string Color { get; set; } = "#00FF00";
    public string Color2 { get; set; } = "#0000FF";
    public int Brightness { get; set; } = 255;
}

public sealed class OpenRgbSettings
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 6742;
    public bool AutoConnect { get; set; }
}

/// <summary>Loads and saves settings as JSON in %APPDATA%\AiO\settings.json.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;

    public AppSettings Current { get; private set; } = new();

    public SettingsStore(string? path = null)
    {
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AiO", "settings.json");
    }

    public string FilePath => _path;

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
                Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), Options) ?? new AppSettings();
        }
        catch (JsonException)
        {
            // Keep a copy of the unreadable file and start fresh rather than crash at launch.
            File.Copy(_path, _path + ".bak", overwrite: true);
            Current = new AppSettings();
        }
        // Dictionaries deserialise with the default comparer; restore case-insensitive display keys.
        Current.Displays = new Dictionary<string, DisplayColorSettings>(Current.Displays, StringComparer.OrdinalIgnoreCase);
        return Current;
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        string temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(Current, Options));
        File.Move(temp, _path, overwrite: true);
    }
}
