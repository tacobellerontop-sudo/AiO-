using System.Text.Json;
using System.Text.Json.Serialization;
using AiO.Core.Asus;
using AiO.Core.Color;
using AiO.Core.Display;
using AiO.Core.Gpu;
using AiO.Core.Keyboard;
using AiO.Core.Mouse;

namespace AiO.Core.Settings;

public sealed class AppSettings
{
    public GeneralSettings General { get; set; } = new();
    public Dictionary<string, DisplayColorSettings> Displays { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public AsusSettings Asus { get; set; } = new();
    public Dictionary<string, RazerDeviceSettings> Razer { get; set; } = new();
    public RazerOptions RazerOptions { get; set; } = new();
    public MouseSettings Mouse { get; set; } = new();
    public KeyboardSettings Keyboard { get; set; } = new();
    /// <summary>User-made colour presets (built-in ones live in <see cref="ColorPreset.BuiltIn"/>).</summary>
    public List<ColorPreset> ColorPresets { get; set; } = [];
}

public sealed class MouseSettings
{
    /// <summary>The acceleration curve being edited and applied.</summary>
    public RawAccelProfile? Profile { get; set; }
    /// <summary>The acceleration driver forgets its curve on reboot; AiO re-sends it at start-up.</summary>
    public bool ReapplyRawAccelOnStartup { get; set; } = true;
}

public sealed class GeneralSettings
{
    public bool StartWithWindows { get; set; }
    public bool StartMinimized { get; set; } = true;
    public bool CloseToTray { get; set; } = true;
    /// <summary>Re-apply colour, ASUS and Razer settings when AiO starts.</summary>
    public bool ApplyOnStartup { get; set; } = true;
    /// <summary>Look for a newer AiO build in the background when AiO starts.</summary>
    public bool CheckForUpdates { get; set; } = true;
    /// <summary>Show a small on-screen popup when a hotkey or power change switches something.</summary>
    public bool ShowOsd { get; set; } = true;
}

public sealed class DisplayColorSettings
{
    public ColorAdjustment Gamma { get; set; } = ColorAdjustment.Neutral;
    /// <summary>GPU driver colour values (vibrance, hue...) the user changed.</summary>
    public Dictionary<GpuColorProperty, int> Gpu { get; set; } = new();
    /// <summary>Name of the preset last applied to this display, if any.</summary>
    public string? Preset { get; set; }
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

/// <summary>Ryzen CPU undervolting (Curve Optimizer) and temperature limit, applied through PawnIO.</summary>
public sealed class RyzenTuningSettings
{
    /// <summary>All-core Curve Optimizer offset, -40..0 (0 = stock).</summary>
    public int CurveOffset { get; set; }
    /// <summary>CPU temperature limit in °C; null = leave the firmware's.</summary>
    public int? TempLimit { get; set; }
    /// <summary>Re-apply at start-up and after sleep (the CPU forgets these on every reboot and sleep).</summary>
    public bool ApplyAutomatically { get; set; }
    /// <summary>
    /// Set while an automatic apply is being tested; cleared once AiO has run for two minutes after it.
    /// If AiO starts and finds it still set, the previous apply probably crashed the PC, so it skips auto-apply.
    /// </summary>
    public DateTime? AutoApplyPendingSince { get; set; }
}

/// <summary>Slash lid lighting (Zephyrus G14 2024+, G16).</summary>
public sealed class SlashSettings
{
    /// <summary>Set once the user applies Slash settings in AiO; until then AiO leaves the bar alone.</summary>
    public bool Manage { get; set; }
    public bool On { get; set; } = true;
    public SlashEffect Effect { get; set; } = SlashEffect.Flow;
    public int Brightness { get; set; } = 2;
    public int Interval { get; set; } = 1;
    public bool OnBattery { get; set; } = true;
    public bool OnLidClosed { get; set; }
}

/// <summary>What to switch to on one power source. Null fields are left alone.</summary>
public sealed class AsusPowerProfile
{
    public AsusPerformanceMode? Mode { get; set; }
    /// <summary>Only Eco or Standard: Ultimate needs a restart, so it's never switched automatically.</summary>
    public AsusGpuMode? Gpu { get; set; }
    public RefreshChoice? Refresh { get; set; }
}

public sealed class AsusSettings
{
    public AsusPerformanceMode PerformanceMode { get; set; } = AsusPerformanceMode.Balanced;
    public bool SyncWindowsPowerMode { get; set; } = true;
    public int BatteryLimit { get; set; } = 100;
    public bool? PanelOverdrive { get; set; }
    /// <summary>Hardware Fn-lock; null until the user sets it.</summary>
    public bool? FnLock { get; set; }
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

    /// <summary>Apply <see cref="OnAc"/> / <see cref="OnBattery"/> when the charger is plugged in or pulled.</summary>
    public bool AutoSwitch { get; set; }
    public AsusPowerProfile OnAc { get; set; } = new() { Mode = AsusPerformanceMode.Balanced, Refresh = RefreshChoice.Highest };
    public AsusPowerProfile OnBattery { get; set; } = new() { Mode = AsusPerformanceMode.Silent, Refresh = RefreshChoice.Lowest };

    /// <summary>Seconds without input before the keyboard backlight turns off (0 = never).</summary>
    public int BacklightTimeoutAc { get; set; }
    public int BacklightTimeoutBattery { get; set; }

    public SlashSettings Slash { get; set; } = new();

    /// <summary>The user ticked "I understand" on the Danger zone card.</summary>
    public bool DangerZoneUnlocked { get; set; }
    public RyzenTuningSettings Ryzen { get; set; } = new();

    /// <summary>Listen for the M-keys and Fn hotkeys.</summary>
    public bool HotkeysEnabled { get; set; } = true;

    /// <summary>
    /// What each ASUS key does, keyed by <see cref="AsusHotkey"/> name ("M3") or raw event code ("Code181").
    /// Keys without an entry keep their default. Only the action fields of each mapping are used.
    /// </summary>
    public Dictionary<string, KeyMapping> Hotkeys { get; set; } = new();

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
    /// <summary>Set once lighting has been applied from AiO, so start-up restore leaves untouched lighting alone.</summary>
    public bool LightingSet { get; set; }
    /// <summary>Send the saved DPI, polling rate and lighting when AiO starts (useful for devices without onboard memory).</summary>
    public bool RestoreOnStartup { get; set; }
}

public sealed class RazerOptions
{
    /// <summary>Warn when a wireless Razer device's battery runs low.</summary>
    public bool BatteryAlerts { get; set; } = true;
    public int AlertPercent { get; set; } = 15;
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

    private readonly object _saveLock = new();

    /// <summary>Thread-safe: presets and hotkeys save from background threads too.</summary>
    public void Save()
    {
        lock (_saveLock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            string temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(Current, Options));
            File.Move(temp, _path, overwrite: true);
        }
    }
}
