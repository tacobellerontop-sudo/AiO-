using AiO.Core.Keyboard;
using static AiO.Core.Keyboard.VirtualKeys;

namespace AiO.Core.Tests;

public class KeyRemapTests
{
    private static KeyMapping Remap(int from, int to) =>
        new() { Trigger = new(KeyModifiers.None, from), Action = KeyActionType.SendKeys, Target = new(KeyModifiers.None, to) };

    private static KeyMapping Shortcut(KeyModifiers mods, int key, KeyChord target, string? app = null) =>
        new() { Trigger = new(mods, key), Action = KeyActionType.SendKeys, Target = target, AppFilter = app };

    [Fact]
    public void Unmapped_keys_pass_through()
    {
        var e = new KeyRemapEngine([Remap(CapsLock, Escape)]);
        Assert.Same(KeyDecision.Pass, e.Process('A', true, null));
        Assert.Same(KeyDecision.Pass, e.Process('A', false, null));
    }

    [Fact]
    public void Key_remap_mirrors_press_and_release()
    {
        var e = new KeyRemapEngine([Remap(CapsLock, Escape)]);
        var down = e.Process(CapsLock, true, null);
        Assert.True(down.Suppress);
        Assert.Equal([new KeyOutput(Escape, true)], down.Outputs);

        var up = e.Process(CapsLock, false, null);
        Assert.True(up.Suppress);
        Assert.Equal([new KeyOutput(Escape, false)], up.Outputs);
    }

    [Fact]
    public void Key_remap_applies_with_modifiers_held()
    {
        var e = new KeyRemapEngine([Remap(CapsLock, Escape)]);
        e.Process(LControl, true, null);
        Assert.Equal([new KeyOutput(Escape, true)], e.Process(CapsLock, true, null).Outputs);
    }

    [Fact]
    public void Key_can_map_to_a_shortcut()
    {
        var m = new KeyMapping { Trigger = new(KeyModifiers.None, 0x7B /* F12 */), Target = new(KeyModifiers.Ctrl | KeyModifiers.Shift, Escape) };
        var e = new KeyRemapEngine([m]);
        Assert.Equal([new KeyOutput(LControl, true), new KeyOutput(LShift, true), new KeyOutput(Escape, true)],
            e.Process(0x7B, true, null).Outputs);
        Assert.Equal([new KeyOutput(Escape, false), new KeyOutput(LShift, false), new KeyOutput(LControl, false)],
            e.Process(0x7B, false, null).Outputs);
    }

    [Fact]
    public void Disabled_key_is_swallowed_both_ways()
    {
        var e = new KeyRemapEngine([new KeyMapping { Trigger = new(KeyModifiers.None, LWin), Action = KeyActionType.Disable }]);
        Assert.Same(KeyDecision.Swallow, e.Process(LWin, true, null));
        Assert.Same(KeyDecision.Swallow, e.Process(LWin, false, null));
    }

    [Fact]
    public void Shortcut_lifts_held_modifiers_taps_target_and_restores()
    {
        var e = new KeyRemapEngine([Shortcut(KeyModifiers.Ctrl, 'J', new(KeyModifiers.None, Down))]);
        Assert.Same(KeyDecision.Pass, e.Process(LControl, true, null));

        var d = e.Process('J', true, null);
        Assert.True(d.Suppress);
        Assert.Equal(
            [new KeyOutput(LControl, false), new KeyOutput(Down, true), new KeyOutput(Down, false), new KeyOutput(LControl, true)],
            d.Outputs);
        Assert.Same(KeyDecision.Swallow, e.Process('J', false, null));
    }

    [Fact]
    public void Shortcut_needs_exact_modifiers()
    {
        var e = new KeyRemapEngine([Shortcut(KeyModifiers.Ctrl, 'J', new(KeyModifiers.None, Down))]);
        e.Process(LControl, true, null);
        e.Process(LShift, true, null);
        Assert.Same(KeyDecision.Pass, e.Process('J', true, null)); // Ctrl+Shift+J isn't Ctrl+J
    }

    [Fact]
    public void Right_hand_modifiers_count()
    {
        var e = new KeyRemapEngine([Shortcut(KeyModifiers.Ctrl, 'J', new(KeyModifiers.None, Down))]);
        e.Process(RControl, true, null);
        Assert.True(e.Process('J', true, null).Suppress);
    }

    [Fact]
    public void More_specific_shortcut_wins()
    {
        var e = new KeyRemapEngine(
        [
            Shortcut(KeyModifiers.Ctrl, 'K', new(KeyModifiers.None, Up)),
            Shortcut(KeyModifiers.Ctrl | KeyModifiers.Shift, 'K', new(KeyModifiers.None, Down)),
        ]);
        e.Process(LControl, true, null);
        e.Process(LShift, true, null);
        Assert.Contains(new KeyOutput(Down, true), e.Process('K', true, null).Outputs);
    }

    [Fact]
    public void Win_shortcut_sends_dummy_before_releasing_win()
    {
        var e = new KeyRemapEngine([Shortcut(KeyModifiers.Win, 'Q', new(KeyModifiers.None, Escape))]);
        e.Process(LWin, true, null);
        var outputs = e.Process('Q', true, null).Outputs;
        int dummy = outputs.ToList().IndexOf(new KeyOutput(Dummy, true));
        int winUp = outputs.ToList().IndexOf(new KeyOutput(LWin, false));
        Assert.True(dummy >= 0 && winUp > dummy);
    }

    [Fact]
    public void App_filter_limits_mapping()
    {
        var e = new KeyRemapEngine([Shortcut(KeyModifiers.Ctrl, 'J', new(KeyModifiers.None, Down), app: "chrome")]);
        e.Process(LControl, true, null);
        Assert.Same(KeyDecision.Pass, e.Process('J', true, "notepad.exe"));
        e.Process('J', false, "notepad.exe");
        Assert.True(e.Process('J', true, "Chrome.exe").Suppress);
    }

    [Fact]
    public void Actions_fire_once_per_press()
    {
        var run = new KeyMapping { Trigger = new(KeyModifiers.Ctrl | KeyModifiers.Alt, 'T'), Action = KeyActionType.Run, Command = "wt" };
        var e = new KeyRemapEngine([run]);
        e.Process(LControl, true, null);
        e.Process(LAlt, true, null);
        Assert.Same(run, e.Process('T', true, null).Action);
        Assert.Null(e.Process('T', true, null).Action); // auto-repeat
        e.Process('T', false, null);
        Assert.Same(run, e.Process('T', true, null).Action);
    }

    [Fact]
    public void Disabled_mappings_are_ignored()
    {
        var m = Remap(CapsLock, Escape);
        m.Enabled = false;
        Assert.Same(KeyDecision.Pass, new KeyRemapEngine([m]).Process(CapsLock, true, null));
    }

    [Fact]
    public void Chord_names_read_naturally()
    {
        Assert.Equal("Ctrl + Shift + Esc", new KeyChord(KeyModifiers.Ctrl | KeyModifiers.Shift, Escape).ToString());
        Assert.Equal("F5", VirtualKeys.Name(0x74));
    }
}

public class KeyRemapTextTests
{
    [Fact]
    public void Text_shortcut_lifts_modifiers_without_restoring_them()
    {
        var m = new KeyMapping { Trigger = new(KeyModifiers.Ctrl, 'E'), Action = KeyActionType.TypeText, Text = "hi" };
        var e = new KeyRemapEngine([m]);
        e.Process(VirtualKeys.LControl, true, null);
        var d = e.Process('E', true, null);
        Assert.Same(m, d.Action);
        Assert.Equal([new KeyOutput(VirtualKeys.LControl, false)], d.Outputs);
    }
}
