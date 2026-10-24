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
        KeyActionType.Disable => "Disabled",
        KeyActionType.TypeText => $"Type \"{Text}\"",
        KeyActionType.Run => $"Run {Command}",
        KeyActionType.CycleAsusMode => "Next ASUS performance mode",
        KeyActionType.OpenAiO => "Open AiO",
        _ => Action.ToString(),
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
