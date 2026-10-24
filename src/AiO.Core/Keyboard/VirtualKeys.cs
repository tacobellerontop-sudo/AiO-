namespace AiO.Core.Keyboard;

/// <summary>Windows virtual-key codes and display names.</summary>
public static class VirtualKeys
{
    public const int Backspace = 0x08, Tab = 0x09, Enter = 0x0D, Pause = 0x13, CapsLock = 0x14, Escape = 0x1B, Space = 0x20;
    public const int PageUp = 0x21, PageDown = 0x22, End = 0x23, Home = 0x24, Left = 0x25, Up = 0x26, Right = 0x27, Down = 0x28;
    public const int PrintScreen = 0x2C, Insert = 0x2D, Delete = 0x2E;
    public const int LWin = 0x5B, RWin = 0x5C, Apps = 0x5D;
    public const int NumLock = 0x90, ScrollLock = 0x91;
    public const int LShift = 0xA0, RShift = 0xA1, LControl = 0xA2, RControl = 0xA3, LAlt = 0xA4, RAlt = 0xA5;
    public const int Shift = 0x10, Control = 0x11, Alt = 0x12;
    public const int VolumeMute = 0xAD, VolumeDown = 0xAE, VolumeUp = 0xAF;
    public const int MediaNext = 0xB0, MediaPrevious = 0xB1, MediaStop = 0xB2, MediaPlayPause = 0xB3;
    /// <summary>Unassigned key code, sent to stop a lone Win release from opening Start.</summary>
    public const int Dummy = 0xFF;

    private static readonly Dictionary<int, string> Names = BuildNames();

    private static Dictionary<int, string> BuildNames()
    {
        var n = new Dictionary<int, string>
        {
            [Backspace] = "Backspace", [Tab] = "Tab", [Enter] = "Enter", [Pause] = "Pause", [CapsLock] = "Caps Lock",
            [Escape] = "Esc", [Space] = "Space", [PageUp] = "Page Up", [PageDown] = "Page Down", [End] = "End", [Home] = "Home",
            [Left] = "Left", [Up] = "Up", [Right] = "Right", [Down] = "Down", [PrintScreen] = "Print Screen",
            [Insert] = "Insert", [Delete] = "Delete", [LWin] = "Left Win", [RWin] = "Right Win", [Apps] = "Menu",
            [NumLock] = "Num Lock", [ScrollLock] = "Scroll Lock",
            [LShift] = "Left Shift", [RShift] = "Right Shift", [LControl] = "Left Ctrl", [RControl] = "Right Ctrl",
            [LAlt] = "Left Alt", [RAlt] = "Right Alt", [Shift] = "Shift", [Control] = "Ctrl", [Alt] = "Alt",
            [VolumeMute] = "Mute", [VolumeDown] = "Volume Down", [VolumeUp] = "Volume Up",
            [MediaNext] = "Next Track", [MediaPrevious] = "Previous Track", [MediaStop] = "Stop", [MediaPlayPause] = "Play/Pause",
            [0xA6] = "Browser Back", [0xA7] = "Browser Forward", [0xAC] = "Browser Home", [0xB4] = "Mail", [0xB6] = "Launch App 1", [0xB7] = "Calculator",
            [0xBA] = ";", [0xBB] = "=", [0xBC] = ",", [0xBD] = "-", [0xBE] = ".", [0xBF] = "/", [0xC0] = "`",
            [0xDB] = "[", [0xDC] = "\\", [0xDD] = "]", [0xDE] = "'", [0xE2] = "\\ (ISO)",
            [0x6A] = "Num *", [0x6B] = "Num +", [0x6D] = "Num -", [0x6E] = "Num .", [0x6F] = "Num /",
            [Dummy] = "(none)",
        };
        for (int c = 'A'; c <= 'Z'; c++) n[c] = ((char)c).ToString();
        for (int d = 0; d <= 9; d++) { n['0' + d] = d.ToString(); n[0x60 + d] = $"Num {d}"; }
        for (int f = 1; f <= 24; f++) n[0x6F + f] = $"F{f}";
        return n;
    }

    public static string Name(int vk) => Names.TryGetValue(vk, out var name) ? name : $"Key 0x{vk:X2}";

    /// <summary>Keys offered in pickers, in a sensible order.</summary>
    public static IReadOnlyList<int> Pickable { get; } = Names.Keys.Where(k => k != Dummy && k is not (Shift or Control or Alt))
        .OrderBy(SortKey).ToList();

    private static int SortKey(int vk) => vk switch
    {
        >= 'A' and <= 'Z' => vk,
        >= '0' and <= '9' => 100 + vk,
        >= 0x70 and <= 0x87 => 200 + vk,
        _ => 400 + vk,
    };

    public static bool IsModifier(int vk) => ModifierOf(vk) != KeyModifiers.None;

    public static KeyModifiers ModifierOf(int vk) => vk switch
    {
        LControl or RControl or Control => KeyModifiers.Ctrl,
        LAlt or RAlt or Alt => KeyModifiers.Alt,
        LShift or RShift or Shift => KeyModifiers.Shift,
        LWin or RWin => KeyModifiers.Win,
        _ => KeyModifiers.None,
    };

    /// <summary>Left-hand key for a modifier flag, used when synthesising shortcuts.</summary>
    public static int KeyFor(KeyModifiers modifier) => modifier switch
    {
        KeyModifiers.Ctrl => LControl,
        KeyModifiers.Alt => LAlt,
        KeyModifiers.Shift => LShift,
        KeyModifiers.Win => LWin,
        _ => 0,
    };

    /// <summary>Keys whose scan code needs the "extended" flag when synthesised.</summary>
    public static bool IsExtended(int vk) => vk is RControl or RAlt or Insert or Delete or Home or End or PageUp or PageDown
        or Left or Up or Right or Down or NumLock or PrintScreen or LWin or RWin or Apps or 0x6F
        or (>= 0xA6 and <= 0xB7);
}
