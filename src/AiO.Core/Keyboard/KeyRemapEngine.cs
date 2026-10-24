namespace AiO.Core.Keyboard;

/// <summary>A synthetic key press or release to inject.</summary>
public readonly record struct KeyOutput(int Key, bool Down);

/// <summary>What to do with one physical key event.</summary>
public sealed record KeyDecision(bool Suppress, IReadOnlyList<KeyOutput> Outputs, KeyMapping? Action)
{
    public static readonly KeyDecision Pass = new(false, [], null);
    public static readonly KeyDecision Swallow = new(true, [], null);
}

/// <summary>
/// Decides how each physical key event is remapped. Pure logic (no Win32), so it can be tested;
/// <see cref="KeyboardHook"/> feeds it events and injects the outputs.
/// </summary>
public sealed class KeyRemapEngine
{
    private readonly IReadOnlyList<KeyMapping> _keyRemaps;
    private readonly IReadOnlyList<KeyMapping> _shortcuts;

    /// <summary>Physically held keys (by virtual-key code).</summary>
    private readonly HashSet<int> _held = [];

    /// <summary>Keys whose press we replaced: what to send when they are released (null = swallow).</summary>
    private readonly Dictionary<int, IReadOnlyList<KeyOutput>?> _active = [];

    public KeyRemapEngine(IEnumerable<KeyMapping> mappings)
    {
        var enabled = mappings.Where(m => m.Enabled).ToList();
        _keyRemaps = enabled.Where(m => !m.Trigger.HasModifiers).ToList();
        // Most specific first, so Ctrl+Shift+K wins over Ctrl+K.
        _shortcuts = enabled.Where(m => m.Trigger.HasModifiers)
            .OrderByDescending(m => BitCount((int)m.Trigger.Modifiers)).ToList();
    }

    private static int BitCount(int v) => System.Numerics.BitOperations.PopCount((uint)v);

    public KeyModifiers HeldModifiers
    {
        get
        {
            var m = KeyModifiers.None;
            foreach (var key in _held) m |= VirtualKeys.ModifierOf(key);
            return m;
        }
    }

    public KeyDecision Process(int key, bool down, string? foregroundApp)
    {
        bool repeat = down && _held.Contains(key);
        if (down) _held.Add(key);
        else _held.Remove(key);

        if (!down) return Release(key);

        // Auto-repeat of a key we already handled.
        if (repeat && _active.TryGetValue(key, out _))
            return RepeatActive(key, foregroundApp);

        var shortcut = FindShortcut(key, foregroundApp);
        if (shortcut is not null) return StartShortcut(key, shortcut);

        var remap = _keyRemaps.FirstOrDefault(m => m.Trigger.Key == key && m.AppliesTo(foregroundApp));
        if (remap is not null) return StartRemap(key, remap);

        return KeyDecision.Pass;
    }

    private KeyMapping? FindShortcut(int key, string? app)
    {
        if (VirtualKeys.IsModifier(key)) return null;
        var held = HeldModifiers;
        return _shortcuts.FirstOrDefault(m => m.Trigger.Key == key && m.Trigger.Modifiers == held && m.AppliesTo(app));
    }

    /// <summary>A plain key remap mirrors press and release onto the target.</summary>
    private KeyDecision StartRemap(int key, KeyMapping m)
    {
        switch (m.Action)
        {
            case KeyActionType.SendKeys when m.Target is { } target:
                var press = new List<KeyOutput>();
                foreach (var mod in Flags(target.Modifiers)) press.Add(new(VirtualKeys.KeyFor(mod), true));
                press.Add(new(target.Key, true));

                var release = new List<KeyOutput> { new(target.Key, false) };
                foreach (var mod in Flags(target.Modifiers).Reverse()) release.Add(new(VirtualKeys.KeyFor(mod), false));
                _active[key] = release;
                return new(true, press, null);

            case KeyActionType.SendKeys:
            case KeyActionType.Disable:
                _active[key] = null;
                return KeyDecision.Swallow;

            default:
                _active[key] = null;
                return new(true, [], m);
        }
    }

    /// <summary>
    /// A shortcut fires once on press: the held trigger modifiers are released, the target is tapped,
    /// and the modifiers are pressed again so the user's still-held keys stay consistent.
    /// </summary>
    private KeyDecision StartShortcut(int key, KeyMapping m)
    {
        _active[key] = null; // swallow the trigger key's release
        if (m.Action == KeyActionType.Disable) return KeyDecision.Swallow;

        var heldModifierKeys = _held.Where(VirtualKeys.IsModifier).OrderBy(k => k).ToList();
        var outputs = new List<KeyOutput>();
        bool needsLift = m.Action is KeyActionType.SendKeys or KeyActionType.TypeText;
        if (needsLift) LiftModifiers(heldModifierKeys, outputs);

        if (m.Action == KeyActionType.SendKeys && m.Target is { } target)
            Tap(target, outputs);

        // After a key target, restore the still-held modifiers. Text is typed afterwards (off the hook
        // thread), so for text they stay lifted until the user presses them again.
        if (m.Action == KeyActionType.SendKeys)
            foreach (var k in heldModifierKeys) outputs.Add(new(k, true));

        return new(true, outputs, m.Action == KeyActionType.SendKeys ? null : m);
    }

    private KeyDecision RepeatActive(int key, string? app)
    {
        // Shortcuts to key targets repeat; actions (run, type...) fire once per press.
        var shortcut = FindShortcut(key, app);
        if (shortcut is { Action: KeyActionType.SendKeys, Target: not null }) return StartShortcut(key, shortcut);

        var remap = _keyRemaps.FirstOrDefault(m => m.Trigger.Key == key && m.AppliesTo(app));
        if (remap is { Action: KeyActionType.SendKeys, Target: { } target })
            return new(true, [new(target.Key, true)], null);

        return KeyDecision.Swallow;
    }

    private KeyDecision Release(int key)
    {
        if (!_active.Remove(key, out var release)) return KeyDecision.Pass;
        return release is null ? KeyDecision.Swallow : new(true, release, null);
    }

    private static void LiftModifiers(IReadOnlyList<int> modifierKeys, List<KeyOutput> outputs)
    {
        bool winHeld = false;
        foreach (var k in modifierKeys)
        {
            if (VirtualKeys.ModifierOf(k) == KeyModifiers.Win) winHeld = true;
            else outputs.Add(new(k, false));
        }
        if (winHeld)
        {
            // A Win release with nothing in between opens Start; a dummy key prevents that.
            outputs.Add(new(VirtualKeys.Dummy, true));
            outputs.Add(new(VirtualKeys.Dummy, false));
            foreach (var k in modifierKeys.Where(k => VirtualKeys.ModifierOf(k) == KeyModifiers.Win)) outputs.Add(new(k, false));
        }
    }

    private static void Tap(KeyChord chord, List<KeyOutput> outputs)
    {
        var mods = Flags(chord.Modifiers).ToList();
        foreach (var mod in mods) outputs.Add(new(VirtualKeys.KeyFor(mod), true));
        outputs.Add(new(chord.Key, true));
        outputs.Add(new(chord.Key, false));
        foreach (var mod in Enumerable.Reverse(mods)) outputs.Add(new(VirtualKeys.KeyFor(mod), false));
    }

    private static IEnumerable<KeyModifiers> Flags(KeyModifiers m)
    {
        foreach (var flag in new[] { KeyModifiers.Ctrl, KeyModifiers.Alt, KeyModifiers.Shift, KeyModifiers.Win })
            if (m.HasFlag(flag)) yield return flag;
    }
}
