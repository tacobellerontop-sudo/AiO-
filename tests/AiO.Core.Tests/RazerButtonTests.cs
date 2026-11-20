using AiO.Core.Razer;

namespace AiO.Core.Tests;

public class RazerButtonTests
{
    private static string Hex(byte[] bytes, int start, int count) =>
        Convert.ToHexString(bytes, start, count).ToLowerInvariant();

    [Fact]
    public void Get_button_uses_class_02_command_8c()
    {
        var bytes = RazerButtonCommands.Get(0x04, RazerButtonLayer.Standard).ToBytes();
        // size, class, id, then profile, button, layer
        Assert.Equal("0a028c" + "010400", Hex(bytes, 5, 6));
    }

    [Fact]
    public void Set_side_button_to_keyboard_key_matches_captures()
    {
        // Published capture: side 12 -> F24 = "01 4b 00 02 02 00 73 00 00 00".
        var action = RazerButtonAction.Key(0x73);
        var bytes = RazerButtonCommands.Set(0x4B, RazerButtonLayer.Standard, action).ToBytes();
        Assert.Equal("0a020c", Hex(bytes, 5, 3));
        Assert.Equal("014b0002020073000000", Hex(bytes, 8, 10));
    }

    [Fact]
    public void Set_media_and_mouse_actions_match_captures()
    {
        var mute = RazerButtonCommands.Set(0x4B, RazerButtonLayer.Standard, RazerButtonAction.Media(0xE2)).ToBytes();
        Assert.Equal("014b000a0200e2000000", Hex(mute, 8, 10));

        var back = RazerButtonCommands.Set(0x4B, RazerButtonLayer.Standard, RazerButtonAction.Mouse(RazerMouseFunction.Back)).ToBytes();
        Assert.Equal("014b0001010400000000", Hex(back, 8, 10));
    }

    [Fact]
    public void Hypershift_layer_byte_is_set()
    {
        var bytes = RazerButtonCommands.Set(0x05, RazerButtonLayer.Hypershift, RazerButtonAction.Key(0x68, RazerModifiers.Ctrl)).ToBytes();
        Assert.Equal("0105010202016800", Hex(bytes, 8, 8));
    }

    [Fact]
    public void Left_click_cannot_be_remapped()
    {
        Assert.Throws<ArgumentException>(() =>
            RazerButtonCommands.Set(RazerButton.LeftId, RazerButtonLayer.Standard, RazerButtonAction.Disabled()));
        // Writing left click back to itself is allowed.
        RazerButtonCommands.Set(RazerButton.LeftId, RazerButtonLayer.Standard, RazerButtonAction.Mouse(RazerMouseFunction.LeftClick));
    }

    [Fact]
    public void Parse_reads_action_block_and_rejects_other_buttons()
    {
        var reply = RazerButtonCommands.Set(0x05, RazerButtonLayer.Standard, RazerButtonAction.Mouse(RazerMouseFunction.Forward));
        var parsed = RazerButtonCommands.Parse(reply, 0x05);
        Assert.NotNull(parsed);
        Assert.Equal(RazerActionKind.Mouse, parsed!.KnownKind);
        Assert.Equal("Forward", parsed.Describe());
        Assert.Null(RazerButtonCommands.Parse(reply, 0x04));
    }

    [Fact]
    public void Action_hex_round_trips_unknown_kinds()
    {
        var raw = RazerButtonAction.FromHex("06030102030000");
        Assert.Equal(RazerActionKind.Dpi, raw.KnownKind);
        Assert.Equal("06030102030000", raw.ToHex());
        Assert.True(raw.SameAs(RazerButtonAction.FromHex(raw.ToHex())));
    }

    [Fact]
    public void SameAs_ignores_padding_but_not_values()
    {
        var a = RazerButtonAction.FromHex("010104FF000000");
        Assert.True(a.SameAs(RazerButtonAction.Mouse(RazerMouseFunction.Back)));
        Assert.False(a.SameAs(RazerButtonAction.Mouse(RazerMouseFunction.Forward)));
    }

    [Fact]
    public void Key_names_and_function_keys()
    {
        Assert.Equal("Ctrl+Shift+F13", RazerKeys.Describe(0x68, RazerModifiers.Ctrl | RazerModifiers.Shift));
        Assert.Equal("F12", RazerKeys.KeyName(0x45));
        Assert.Equal("F24", RazerKeys.KeyName(0x73));
        Assert.Equal("A", RazerKeys.KeyName(0x04));
    }

    [Fact]
    public void Candidate_buttons_are_unique()
    {
        var ids = RazerButton.Candidates.Select(b => b.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.Equal(RazerButton.LeftId, ids[0]);
    }
}
