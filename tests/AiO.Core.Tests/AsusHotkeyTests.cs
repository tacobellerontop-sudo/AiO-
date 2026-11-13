using System.Text;
using AiO.Core.Asus;
using AiO.Core.Hid;
using AiO.Core.Keyboard;

namespace AiO.Core.Tests;

public class AsusHotkeyTests
{
    [Theory]
    [InlineData(124, AsusHotkey.M3)]
    [InlineData(56, AsusHotkey.M4)]
    [InlineData(134, AsusHotkey.M4)]
    [InlineData(179, AsusHotkey.FnF4)]
    [InlineData(174, AsusHotkey.FnF5)]
    [InlineData(153, AsusHotkey.FnF5)]
    [InlineData(158, AsusHotkey.FnC)]
    [InlineData(138, AsusHotkey.FnV)]
    public void Event_codes_map_to_keys(int code, AsusHotkey expected) =>
        Assert.Equal(expected, AsusHotkeyCodes.FromEventCode(code));

    [Fact]
    public void Unknown_codes_are_kept_by_number()
    {
        Assert.Null(AsusHotkeyCodes.FromEventCode(181));
        Assert.Equal("Code181", AsusHotkeyCodes.SettingsKey(181));
        Assert.Equal(181, AsusHotkeyCodes.CodeFromSettingsKey("Code181"));
        Assert.Null(AsusHotkeyCodes.CodeFromSettingsKey("M3"));
        Assert.Equal("M3", AsusHotkeyCodes.SettingsKey(124));
    }

    [Fact]
    public void Defaults_follow_the_keys_labels()
    {
        Assert.Equal(KeyActionType.MicrophoneMute, AsusHotkeyCodes.DefaultAction(AsusHotkey.M3, asusServiceRunning: false));
        Assert.Null(AsusHotkeyCodes.DefaultAction(AsusHotkey.M3, asusServiceRunning: true));
        Assert.Equal(KeyActionType.OpenAiO, AsusHotkeyCodes.DefaultAction(AsusHotkey.M4, false));
        Assert.Equal(KeyActionType.CycleAsusMode, AsusHotkeyCodes.DefaultAction(AsusHotkey.FnF5, false));
        Assert.Null(AsusHotkeyCodes.DefaultAction(AsusHotkey.M1, false)); // Windows already handles volume
    }

    [Fact]
    public void M1_and_M2_are_the_volume_keys()
    {
        Assert.Equal(VirtualKeys.VolumeDown, AsusHotkeyCodes.VirtualKeyOf(AsusHotkey.M1));
        Assert.Equal(VirtualKeys.VolumeUp, AsusHotkeyCodes.VirtualKeyOf(AsusHotkey.M2));
        Assert.Null(AsusHotkeyCodes.VirtualKeyOf(AsusHotkey.M3));
    }

    [Fact]
    public void Debouncer_drops_the_same_press_from_a_second_channel()
    {
        var d = new HotkeyDebouncer(TimeSpan.FromMilliseconds(250));
        var t = new DateTime(2026, 1, 1);
        Assert.True(d.ShouldHandle(124, t));
        Assert.False(d.ShouldHandle(124, t.AddMilliseconds(40)));
        Assert.True(d.ShouldHandle(56, t.AddMilliseconds(40)));
        Assert.True(d.ShouldHandle(124, t.AddMilliseconds(400)));
    }

    [Fact]
    public void Input_report_carries_the_code_in_byte_one()
    {
        Assert.Equal(124, AsusKeyListener.ParseInputReport(new byte[] { 0x5A, 124, 0, 0 }, 4));
        Assert.Null(AsusKeyListener.ParseInputReport(new byte[] { 0x5A, 0, 0, 0 }, 4));
        Assert.Null(AsusKeyListener.ParseInputReport(new byte[] { 0x5D, 124, 0, 0 }, 4));
        Assert.Null(AsusKeyListener.ParseInputReport(new byte[] { 0x5A, 124 }, 1));
    }

    [Fact]
    public void Listens_on_the_keyboards_vendor_collection_only()
    {
        HidDeviceInfo D(ushort pid, ushort page, int input) => new("p", 0x0B05, pid, page, 0x76, 64, input, 64, "", "");
        var devices = new[]
        {
            D(0x19B6, 0xFF31, 64), // N-KEY vendor collection
            D(0x19B6, 0x0001, 8),  // the ordinary keyboard collection
            D(0x19B6, 0xFF31, 0),  // feature-only
            D(0x1234, 0xFF00, 64), // unknown product, generic vendor page
        };
        var picked = AsusKeyListener.InputCandidates(devices).ToList();
        Assert.Single(picked);
        Assert.Equal(0xFF31, picked[0].UsagePage);
    }

    [Fact]
    public void Init_method_id_spells_init()
    {
        var call = AsusAcpiProtocol.BuildCall(AsusAcpiProtocol.MethodInit, new byte[8]);
        Assert.Equal("INIT", Encoding.ASCII.GetString(call, 0, 4));
    }

    [Fact]
    public void New_actions_have_readable_descriptions()
    {
        foreach (var action in Enum.GetValues<KeyActionType>())
            Assert.NotEqual(action.ToString(), KeyMapping.DescribeAction(action));
    }
}
