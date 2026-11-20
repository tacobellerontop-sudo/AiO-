namespace AiO.Core.Razer;

/// <summary>Which layer of a button assignment: the normal one, or the one used while a Hypershift button is held.</summary>
public enum RazerButtonLayer : byte
{
    Standard = 0x00,
    Hypershift = 0x01,
}

/// <summary>What kind of function a button is assigned (byte 3 of the button record).</summary>
public enum RazerActionKind : byte
{
    Disabled = 0x00,
    Mouse = 0x01,
    Keyboard = 0x02,
    Dpi = 0x06,
    Media = 0x0A,
    Hypershift = 0x0C,
}

/// <summary>Mouse functions a button can be given (value of a <see cref="RazerActionKind.Mouse"/> action).</summary>
public enum RazerMouseFunction : byte
{
    LeftClick = 0x01,
    RightClick = 0x02,
    MiddleClick = 0x03,
    Back = 0x04,
    Forward = 0x05,
    ScrollUp = 0x09,
    ScrollDown = 0x0A,
}

/// <summary>
/// Keyboard modifier bits for a <see cref="RazerActionKind.Keyboard"/> action (USB HID modifier byte).
/// Razer firmware never plays the left Shift (0x02) or left Alt (0x04) bit, so Shift and Alt use the
/// right-hand keys (0x20, 0x40), as Sidearm found on hardware. The left bits are still recognised when read.
/// </summary>
[Flags]
public enum RazerModifiers : byte
{
    None = 0,
    Ctrl = 0x01,
    LeftShift = 0x02,
    LeftAlt = 0x04,
    Win = 0x08,
    RightCtrl = 0x10,
    Shift = 0x20,
    Alt = 0x40,
    RightWin = 0x80,
}

/// <summary>A physical button the mouse may have, by its id in the button-assignment command.</summary>
public sealed record RazerButton(byte Id, string Name)
{
    /// <summary>Left click can't be changed from AiO, so the mouse can always click.</summary>
    public bool Locked => Id == LeftId;

    public const byte LeftId = 0x01;

    /// <summary>
    /// Every button id AiO asks about. Mice answer only for the buttons they have, so this list is a superset.
    /// Ids come from public reverse-engineering notes (OpenRazer issue #2845, MacRazer #149, OpenMouse #263).
    /// </summary>
    public static IReadOnlyList<RazerButton> Candidates { get; } = BuildCandidates();

    private static List<RazerButton> BuildCandidates()
    {
        var list = new List<RazerButton>
        {
            new(0x01, "Left button"),
            new(0x02, "Right button"),
            new(0x03, "Wheel click"),
            new(0x04, "Side button (back)"),
            new(0x05, "Side button (forward)"),
            new(0x06, "Side button 3"),
            new(0x07, "Side button 4"),
            new(0x09, "Wheel up"),
            new(0x0A, "Wheel down"),
            new(0x0B, "DPI up"),
            new(0x0C, "DPI down"),
            new(0x34, "Wheel tilt left"),
            new(0x35, "Wheel tilt right"),
        };
        // Naga-style side grids (12 buttons).
        for (int i = 1; i <= 12; i++) list.Add(new((byte)(0x3F + i), $"Side grid {i}"));
        return list;
    }

    public static string NameOf(byte id) => Candidates.FirstOrDefault(b => b.Id == id)?.Name ?? $"Button 0x{id:X2}";

    /// <summary>True for ids that aren't in <see cref="Candidates"/> (found by scanning; position unknown).</summary>
    public bool IsUnnamed => Candidates.All(b => b.Id != Id);

    /// <summary>Highest id AiO scans for buttons it has no name for (e.g. a Basilisk's thumb/clutch button).</summary>
    public const byte ScanLimit = 0x7F;

    /// <summary>Ids Razer notes say answer with unrelated data and must not be written (profile switching).</summary>
    public static IReadOnlySet<byte> NeverTouch { get; } = new HashSet<byte> { 0x60 };
}

/// <summary>
/// The 7-byte action block of a button record: kind, declared length, then up to 5 value bytes.
/// Unknown kinds are kept as raw bytes so AiO can write them back unchanged.
/// </summary>
public sealed record RazerButtonAction(byte Kind, byte Length, byte[] Values)
{
    public const int BlockSize = 7;

    public static RazerButtonAction Disabled() => new((byte)RazerActionKind.Disabled, 0x01, [0]);

    public static RazerButtonAction Mouse(RazerMouseFunction function) => new((byte)RazerActionKind.Mouse, 0x01, [(byte)function]);

    /// <param name="usage">USB HID keyboard usage id (see <see cref="RazerKeys"/>).</param>
    public static RazerButtonAction Key(byte usage, RazerModifiers modifiers = RazerModifiers.None) =>
        new((byte)RazerActionKind.Keyboard, 0x02, [(byte)modifiers, usage]);

    /// <param name="usage">USB HID consumer-page usage id, e.g. 0xCD play/pause.</param>
    public static RazerButtonAction Media(ushort usage) =>
        new((byte)RazerActionKind.Media, 0x02, [(byte)(usage >> 8), (byte)usage]);

    public static RazerButtonAction Hypershift() => new((byte)RazerActionKind.Hypershift, 0x01, [0x01]);

    public RazerActionKind? KnownKind => Enum.IsDefined(typeof(RazerActionKind), Kind) ? (RazerActionKind)Kind : null;

    public byte Value(int index) => index < Values.Length ? Values[index] : (byte)0;

    public byte[] ToBlock()
    {
        var block = new byte[BlockSize];
        block[0] = Kind;
        block[1] = Length;
        for (int i = 0; i < Math.Min(Values.Length, BlockSize - 2); i++) block[2 + i] = Values[i];
        return block;
    }

    public static RazerButtonAction FromBlock(ReadOnlySpan<byte> block)
    {
        if (block.Length < BlockSize) throw new ArgumentException($"Action block needs {BlockSize} bytes", nameof(block));
        return new RazerButtonAction(block[0], block[1], block.Slice(2, BlockSize - 2).ToArray());
    }

    /// <summary>Same function, ignoring padding after the declared length.</summary>
    public bool SameAs(RazerButtonAction other)
    {
        if (Kind != other.Kind) return false;
        if (Kind == (byte)RazerActionKind.Disabled) return true;
        int n = Math.Clamp((int)Length, 0, BlockSize - 2);
        for (int i = 0; i < n; i++) if (Value(i) != other.Value(i)) return false;
        return Length == other.Length;
    }

    public string ToHex() => Convert.ToHexString(ToBlock());

    /// <summary>All seven bytes zero: what scanned ids without a physical button usually answer.</summary>
    public bool IsEmpty => Kind == 0 && Length == 0 && Values.All(v => v == 0);

    public static RazerButtonAction FromHex(string hex) => FromBlock(Convert.FromHexString(hex));

    public string Describe() => KnownKind switch
    {
        RazerActionKind.Disabled => "Disabled",
        RazerActionKind.Mouse => Enum.IsDefined(typeof(RazerMouseFunction), Value(0))
            ? RazerKeys.MouseName((RazerMouseFunction)Value(0))
            : $"Mouse function 0x{Value(0):X2}",
        RazerActionKind.Keyboard => RazerKeys.Describe(Value(1), (RazerModifiers)Value(0)),
        RazerActionKind.Media => RazerKeys.MediaName((ushort)((Value(0) << 8) | Value(1))),
        RazerActionKind.Hypershift => "Hypershift (hold)",
        RazerActionKind.Dpi => "DPI function (device default)",
        _ => $"Device function {ToHex()}",
    };
}

/// <summary>Builders for the onboard button-assignment command (class 0x02).</summary>
public static class RazerButtonCommands
{
    public const byte CommandClass = 0x02;
    public const byte SetId = 0x0C;
    public const byte GetId = 0x8C;
    public const byte DataSize = 0x0A;

    /// <summary>Profile byte. 0x01 is the one every public capture uses (the active/onboard profile).</summary>
    public const byte Profile = 0x01;

    public static RazerReport Get(byte button, RazerButtonLayer layer)
    {
        var r = new RazerReport(CommandClass, GetId, DataSize);
        r.Arguments[0] = Profile;
        r.Arguments[1] = button;
        r.Arguments[2] = (byte)layer;
        return r;
    }

    public static RazerReport Set(byte button, RazerButtonLayer layer, RazerButtonAction action)
    {
        if (RazerButton.NeverTouch.Contains(button)) throw new ArgumentException("This button id is reserved", nameof(button));
        if (button == RazerButton.LeftId && layer == RazerButtonLayer.Standard &&
            !(action.Kind == (byte)RazerActionKind.Mouse && action.Value(0) == (byte)RazerMouseFunction.LeftClick))
            throw new ArgumentException("Left click must stay left click", nameof(action));

        var r = new RazerReport(CommandClass, SetId, DataSize);
        r.Arguments[0] = Profile;
        r.Arguments[1] = button;
        r.Arguments[2] = (byte)layer;
        action.ToBlock().CopyTo(r.Arguments, 3);
        return r;
    }

    /// <summary>
    /// Reads the action from a 0x8C reply. Returns null when the reply is for a different button
    /// (some ids answer with unrelated data and must be left alone).
    /// </summary>
    public static RazerButtonAction? Parse(RazerReport response, byte button)
    {
        if (response.Arguments[1] != button) return null;
        return RazerButtonAction.FromBlock(response.Arguments.AsSpan(3, RazerButtonAction.BlockSize));
    }

    /// <summary>The factory function for buttons whose default is well known; null otherwise.</summary>
    public static RazerButtonAction? FactoryDefault(byte button) => button switch
    {
        0x01 => RazerButtonAction.Mouse(RazerMouseFunction.LeftClick),
        0x02 => RazerButtonAction.Mouse(RazerMouseFunction.RightClick),
        0x03 => RazerButtonAction.Mouse(RazerMouseFunction.MiddleClick),
        0x04 => RazerButtonAction.Mouse(RazerMouseFunction.Back),
        0x05 => RazerButtonAction.Mouse(RazerMouseFunction.Forward),
        0x09 => RazerButtonAction.Mouse(RazerMouseFunction.ScrollUp),
        0x0A => RazerButtonAction.Mouse(RazerMouseFunction.ScrollDown),
        _ => null,
    };
}

/// <summary>Key and media names for the button editor, with their USB HID usage ids.</summary>
public static class RazerKeys
{
    public static IReadOnlyList<(string Name, byte Usage)> Keys { get; } = BuildKeys();

    public static IReadOnlyList<(string Name, ushort Usage)> MediaKeys { get; } =
    [
        ("Play / pause", 0xCD),
        ("Next track", 0xB5),
        ("Previous track", 0xB6),
        ("Stop", 0xB7),
        ("Mute", 0xE2),
        ("Volume up", 0xE9),
        ("Volume down", 0xEA),
    ];

    private static List<(string, byte)> BuildKeys()
    {
        var keys = new List<(string, byte)>();
        for (int i = 0; i < 26; i++) keys.Add((((char)('A' + i)).ToString(), (byte)(0x04 + i)));
        for (int i = 1; i <= 9; i++) keys.Add((i.ToString(), (byte)(0x1D + i)));
        keys.Add(("0", 0x27));
        for (int i = 1; i <= 12; i++) keys.Add(($"F{i}", (byte)(0x39 + i)));
        for (int i = 13; i <= 24; i++) keys.Add(($"F{i}", (byte)(0x5B + i)));
        keys.AddRange(
        [
            ("Enter", 0x28), ("Esc", 0x29), ("Backspace", 0x2A), ("Tab", 0x2B), ("Space", 0x2C),
            ("-", 0x2D), ("=", 0x2E), ("[", 0x2F), ("]", 0x30), ("\\", 0x31), (";", 0x33), ("'", 0x34),
            ("`", 0x35), (",", 0x36), (".", 0x37), ("/", 0x38), ("Caps Lock", 0x39),
            ("Print Screen", 0x46), ("Scroll Lock", 0x47), ("Pause", 0x48), ("Insert", 0x49), ("Home", 0x4A),
            ("Page Up", 0x4B), ("Delete", 0x4C), ("End", 0x4D), ("Page Down", 0x4E),
            ("Right arrow", 0x4F), ("Left arrow", 0x50), ("Down arrow", 0x51), ("Up arrow", 0x52),
            ("Menu", 0x65),
        ]);
        return keys;
    }

    public static string KeyName(byte usage) =>
        Keys.FirstOrDefault(x => x.Usage == usage) is { Name: not null } found ? found.Name : $"Key 0x{usage:X2}";

    public static string Describe(byte usage, RazerModifiers mods)
    {
        var parts = new List<string>();
        if ((mods & (RazerModifiers.Ctrl | RazerModifiers.RightCtrl)) != 0) parts.Add("Ctrl");
        if ((mods & (RazerModifiers.Shift | RazerModifiers.LeftShift)) != 0) parts.Add("Shift");
        if ((mods & (RazerModifiers.Alt | RazerModifiers.LeftAlt)) != 0) parts.Add("Alt");
        if ((mods & (RazerModifiers.Win | RazerModifiers.RightWin)) != 0) parts.Add("Win");
        parts.Add(KeyName(usage));
        return string.Join("+", parts);
    }

    public static string MediaName(ushort usage) =>
        MediaKeys.FirstOrDefault(x => x.Usage == usage) is { Name: not null } found ? found.Name : $"Media 0x{usage:X4}";

    public static string MouseName(RazerMouseFunction f) => f switch
    {
        RazerMouseFunction.LeftClick => "Left click",
        RazerMouseFunction.RightClick => "Right click",
        RazerMouseFunction.MiddleClick => "Middle click",
        RazerMouseFunction.Back => "Back",
        RazerMouseFunction.Forward => "Forward",
        RazerMouseFunction.ScrollUp => "Scroll up",
        RazerMouseFunction.ScrollDown => "Scroll down",
        _ => f.ToString(),
    };
}
