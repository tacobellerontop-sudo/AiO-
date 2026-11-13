using AiO.Core.Asus;
using AiO.Core.Hid;

namespace AiO.Core.Tests;

public class SlashTests
{
    [Theory]
    [InlineData("ROG Zephyrus G14 GA403UV_GA403UV", true)]
    [InlineData("ROG Zephyrus G16 GU605MI", true)]
    [InlineData("ROG Zephyrus G14 GA402XV", false)]
    public void Slash_models(string model, bool expected) => Assert.Equal(expected, AsusSlash.IsSlashModel(model));

    [Fact]
    public void Packets_carry_report_id_and_pad_to_128()
    {
        var p = AsusSlash.Packet(0x5E, AsusSlash.Enable(true));
        Assert.Equal(128, p.Length);
        Assert.Equal(new byte[] { 0x5E, 0xD8, 0x02, 0x00, 0x01, 0x00 }, p[..6]);
        Assert.Equal(0x80, AsusSlash.Enable(false)[4]);
    }

    [Fact]
    public void Effect_and_options_bytes()
    {
        Assert.Equal(0x19, AsusSlash.Effect(SlashEffect.Flow)[5]);
        var o = AsusSlash.Options(true, 3, 2);
        Assert.Equal(255, o[9]);
        Assert.Equal(2, o[11]);
        Assert.Equal(85, AsusSlash.BrightnessByte(1));
    }

    [Fact]
    public void Off_sequence_only_disables()
    {
        var seq = AsusSlash.Sequence(false, SlashEffect.Flow, 2, 1, true, false);
        Assert.Equal(AsusSlash.Enable(false), seq[^1]);
        Assert.DoesNotContain(seq, c => c[0] == 0xD4);
    }

    [Fact]
    public void Dedicated_slash_device_is_tried_first()
    {
        HidDeviceInfo D(ushort pid, int feature) => new("p", 0x0B05, pid, 0xFF31, 1, feature, 0, 0, "", "");
        var picked = AsusSlash.Candidates([D(0x19B6, 128), D(0x193B, 128), D(0x193B, 64)]).ToList();
        Assert.Equal(2, picked.Count);
        Assert.Equal((ushort)0x193B, picked[0].Device.ProductId);
        Assert.Equal(0x5E, picked[0].ReportId);
        Assert.Equal(0x5D, picked[1].ReportId);
    }
}
