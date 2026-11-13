using AiO.Core.Keyboard;

namespace AiO.Core.Asus;

/// <summary>ASUS laptop keys that Windows doesn't handle on its own.</summary>
public enum AsusHotkey
{
    /// <summary>M1: sends Volume Down on most ROG models.</summary>
    M1,
    /// <summary>M2: sends Volume Up on most ROG models.</summary>
    M2,
    /// <summary>M3: microphone mute on most ROG models.</summary>
    M3,
    /// <summary>M4 / ROG key: opens Armoury Crate when ASUS software is installed.</summary>
    M4,
    /// <summary>Fn + F4: keyboard lighting (Aura) on ROG/TUF.</summary>
    FnF4,
    /// <summary>Fn + F5: fan / performance mode.</summary>
    FnF5,
    /// <summary>Fn + C.</summary>
    FnC,
    /// <summary>Fn + V.</summary>
    FnV,
    /// <summary>Fn + Esc: Fn-lock.</summary>
    FnEsc,
    /// <summary>Fn + F10: touchpad on/off.</summary>
    FnF10,
    /// <summary>Camera key (Fn + F10 on some Zenbooks / Vivobooks).</summary>
    Camera,
}

/// <summary>
/// Event codes the ASUS firmware sends for special keys, through the AsusAtkWmiEvent WMI event and
/// as input report 0x5A from the keyboard's vendor HID interface. The code values are firmware facts
/// catalogued by the G-Helper project.
/// </summary>
public static class AsusHotkeyCodes
{
    /// <summary>Key code for a hotkey, from either event source; null for codes AiO doesn't name.</summary>
    public static AsusHotkey? FromEventCode(int code) => code switch
    {
        124 => AsusHotkey.M3,
        56 => AsusHotkey.M4,
        134 => AsusHotkey.M4,   // Fn + F12 / ROG key on older models
        139 => AsusHotkey.M4,   // ProArt
        95 => AsusHotkey.M4,    // Flow Z13 side button
        179 or 178 => AsusHotkey.FnF4,
        174 or 153 or 93 => AsusHotkey.FnF5,
        158 => AsusHotkey.FnC,
        138 => AsusHotkey.FnV,
        78 => AsusHotkey.FnEsc,
        107 => AsusHotkey.FnF10,
        133 => AsusHotkey.Camera,
        _ => null,
    };

    /// <summary>Codes that are never key presses (charger, lid, keep-alive) and shouldn't be shown as "unknown key".</summary>
    public static bool IsNoise(int code) => code is 0 or 236 or 123 or 189;

    /// <summary>Settings key for a named hotkey ("M1") or a raw code the user bound ("Code181").</summary>
    public static string SettingsKey(AsusHotkey key) => key.ToString();

    public static string SettingsKey(int code) => FromEventCode(code) is { } key ? SettingsKey(key) : $"Code{code}";

    public static int? CodeFromSettingsKey(string settingsKey) =>
        settingsKey.StartsWith("Code", StringComparison.Ordinal) && int.TryParse(settingsKey.AsSpan(4), out int code) ? code : null;

    public static string DisplayName(AsusHotkey key) => key switch
    {
        AsusHotkey.FnF4 => "Fn + F4",
        AsusHotkey.FnF5 => "Fn + F5",
        AsusHotkey.FnC => "Fn + C",
        AsusHotkey.FnV => "Fn + V",
        AsusHotkey.FnEsc => "Fn + Esc",
        AsusHotkey.FnF10 => "Fn + F10",
        AsusHotkey.Camera => "Camera key",
        AsusHotkey.M4 => "M4 / ROG key",
        _ => key.ToString(),
    };

    /// <summary>
    /// M1 and M2 are ordinary Volume Down / Up keys as far as Windows is concerned; AiO can only take them
    /// over by catching those keys, which also catches volume keys on other keyboards.
    /// </summary>
    public static int? VirtualKeyOf(AsusHotkey key) => key switch
    {
        AsusHotkey.M1 => VirtualKeys.VolumeDown,
        AsusHotkey.M2 => VirtualKeys.VolumeUp,
        _ => null,
    };

    /// <summary>
    /// What a key does until the user picks something else. Null means "leave it to Windows / ASUS software".
    /// M3 only mutes the microphone when the ASUS Optimization service isn't doing it already.
    /// </summary>
    public static KeyActionType? DefaultAction(AsusHotkey key, bool asusServiceRunning) => key switch
    {
        AsusHotkey.M3 when !asusServiceRunning => KeyActionType.MicrophoneMute,
        AsusHotkey.M4 => KeyActionType.OpenAiO,
        AsusHotkey.FnF4 => KeyActionType.NextKeyboardLighting,
        AsusHotkey.FnF5 => KeyActionType.CycleAsusMode,
        // Without ASUS's service nothing else handles these; with it, it does.
        AsusHotkey.FnEsc when !asusServiceRunning => KeyActionType.ToggleFnLock,
        AsusHotkey.FnF10 when !asusServiceRunning => KeyActionType.ToggleTouchpad,
        AsusHotkey.Camera when !asusServiceRunning => KeyActionType.ToggleCamera,
        _ => null,
    };

    public static string DefaultDescription(AsusHotkey key, bool asusServiceRunning) => key switch
    {
        AsusHotkey.M1 => "Volume down (Windows)",
        AsusHotkey.M2 => "Volume up (Windows)",
        AsusHotkey.M3 when asusServiceRunning => "Microphone mute (ASUS service)",
        AsusHotkey.FnEsc or AsusHotkey.FnF10 or AsusHotkey.Camera when asusServiceRunning => "Handled by the ASUS service",
        _ when DefaultAction(key, asusServiceRunning) is { } action => KeyMapping.DescribeAction(action),
        _ => "Nothing",
    };
}

/// <summary>
/// The same key press can arrive twice, once over WMI and once over HID. This drops a repeat of the
/// same code within a short window.
/// </summary>
public sealed class HotkeyDebouncer(TimeSpan window)
{
    private readonly Dictionary<int, DateTime> _last = new();

    public HotkeyDebouncer() : this(TimeSpan.FromMilliseconds(250)) { }

    public bool ShouldHandle(int code, DateTime now)
    {
        lock (_last)
        {
            if (_last.TryGetValue(code, out var previous) && now - previous < window) return false;
            _last[code] = now;
            return true;
        }
    }
}
