using System.Text.Json.Serialization;

namespace AiO.Core.Keyboard;

[Flags]
public enum KeyModifiers
{
    None = 0,
    Ctrl = 1,
    Alt = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>A key with optional modifiers, e.g. Ctrl + Shift + K. <see cref="Key"/> is a Windows virtual-key code.</summary>
public sealed record KeyChord(KeyModifiers Modifiers, int Key)
{
    public bool HasModifiers => Modifiers != KeyModifiers.None;

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(KeyModifiers.Ctrl)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(KeyModifiers.Win)) parts.Add("Win");
        parts.Add(VirtualKeys.Name(Key));
        return string.Join(" + ", parts);
    }
}

public enum KeyActionType
{
    /// <summary>Send another key or shortcut instead.</summary>
    SendKeys,
    /// <summary>Swallow the key.</summary>
    Disable,
    /// <summary>Type a piece of text.</summary>
    TypeText,
    /// <summary>Launch a program, file or URL.</summary>
    Run,
    /// <summary>Cycle ASUS performance modes (Silent → Balanced → Turbo).</summary>
    CycleAsusMode,
    /// <summary>Show the AiO window.</summary>
    OpenAiO,
    /// <summary>Apply the colour preset named in <see cref="KeyMapping.Text"/> to every display.</summary>
    ApplyColorPreset,
    /// <summary>Mute or unmute the default microphone.</summary>
    MicrophoneMute,
    /// <summary>Switch the ASUS keyboard to its next lighting effect.</summary>
    NextKeyboardLighting,
    /// <summary>One step brighter on the ASUS keyboard backlight (wraps from max to off).</summary>
    KeyboardBrightnessUp,
    /// <summary>One step dimmer on the ASUS keyboard backlight.</summary>
    KeyboardBrightnessDown,
    /// <summary>Lock Windows.</summary>
    LockScreen,
    /// <summary>Turn the displays off until the mouse or keyboard is used.</summary>
    ScreenOff,
    /// <summary>Turn the touchpad on or off (Windows' own Ctrl+Win+F24 toggle).</summary>
    ToggleTouchpad,
    /// <summary>Toggle the ASUS Fn-lock.</summary>
    ToggleFnLock,
    /// <summary>Turn the camera on or off on ASUS models with a software camera switch.</summary>
    ToggleCamera,
    /// <summary>Media play / pause.</summary>
    MediaPlayPause,
    MediaNext,
    MediaPrevious,
    VolumeUp,
    VolumeDown,
    VolumeMute,
    /// <summary>Switch straight to one ASUS performance mode.</summary>
    SetSilentMode,
    SetBalancedMode,
    SetTurboMode,
    /// <summary>Keep the window in front above all others (press again to release it).</summary>
    AlwaysOnTop,
    /// <summary>Windows' screen snip (Win+Shift+S).</summary>
    Screenshot,
    /// <summary>Minimise every window (Win+D).</summary>
    ShowDesktop,
}

public sealed class KeyMapping
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The key or shortcut that triggers this mapping. A trigger without modifiers is a key remap and
    /// applies whatever modifiers are held (remapping Caps Lock to Esc also turns Ctrl+Caps Lock into Ctrl+Esc).
    /// </summary>
    public KeyChord Trigger { get; set; } = new(KeyModifiers.None, VirtualKeys.CapsLock);

    public KeyActionType Action { get; set; } = KeyActionType.SendKeys;
    public KeyChord? Target { get; set; } = new(KeyModifiers.None, VirtualKeys.Escape);
    public string? Text { get; set; }
    public string? Command { get; set; }

    /// <summary>Only apply while this program is in front (e.g. "chrome" or "chrome.exe"). Empty = everywhere.</summary>
    public string? AppFilter { get; set; }

    [JsonIgnore]
    public string Description => Action switch
    {
        KeyActionType.SendKeys => Target?.ToString() ?? "(nothing)",
        KeyActionType.TypeText => $"Type \"{Text}\"",
        KeyActionType.Run => $"Run {Command}",
        KeyActionType.ApplyColorPreset => $"Colour preset \"{Text}\"",
        _ => DescribeAction(Action),
    };

    /// <summary>A short label for an action without its details.</summary>
    public static string DescribeAction(KeyActionType action) => action switch
    {
        KeyActionType.SendKeys => "Send a key or shortcut",
        KeyActionType.Disable => "Disabled",
        KeyActionType.TypeText => "Type text",
        KeyActionType.Run => "Run a program, file or URL",
        KeyActionType.CycleAsusMode => "Next ASUS performance mode",
        KeyActionType.OpenAiO => "Open AiO",
        KeyActionType.ApplyColorPreset => "Apply a colour preset",
        KeyActionType.MicrophoneMute => "Mute / unmute microphone",
        KeyActionType.NextKeyboardLighting => "Next keyboard lighting effect",
        KeyActionType.KeyboardBrightnessUp => "Keyboard backlight brighter",
        KeyActionType.KeyboardBrightnessDown => "Keyboard backlight dimmer",
        KeyActionType.LockScreen => "Lock the PC",
        KeyActionType.ScreenOff => "Turn the screen off",
        KeyActionType.ToggleTouchpad => "Touchpad on / off",
        KeyActionType.ToggleFnLock => "Fn-lock on / off",
        KeyActionType.ToggleCamera => "Camera on / off",
        KeyActionType.MediaPlayPause => "Media play / pause",
        KeyActionType.MediaNext => "Media next track",
        KeyActionType.MediaPrevious => "Media previous track",
        KeyActionType.VolumeUp => "Volume up",
        KeyActionType.VolumeDown => "Volume down",
        KeyActionType.VolumeMute => "Mute / unmute sound",
        KeyActionType.SetSilentMode => "Silent mode",
        KeyActionType.SetBalancedMode => "Balanced mode",
        KeyActionType.SetTurboMode => "Turbo mode",
        KeyActionType.AlwaysOnTop => "Keep window on top",
        KeyActionType.Screenshot => "Screen snip",
        KeyActionType.ShowDesktop => "Show desktop",
        _ => action.ToString(),
    };

    public bool AppliesTo(string? foregroundApp)
    {
        if (string.IsNullOrWhiteSpace(AppFilter)) return true;
        if (foregroundApp is null) return false;
        static string Normalize(string s) => s.Trim().ToLowerInvariant().Replace(".exe", "");
        return AppFilter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(f => Normalize(f) == Normalize(foregroundApp));
    }
}

public sealed class KeyboardSettings
{
    /// <summary>Master switch for the remapping hook.</summary>
    public bool Enabled { get; set; }
    public List<KeyMapping> Mappings { get; set; } = [];
}
