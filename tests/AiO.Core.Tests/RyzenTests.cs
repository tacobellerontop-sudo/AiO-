using System.Text;
using AiO.Core.Ryzen;

namespace AiO.Core.Tests;

public class RyzenTests
{
    [Theory]
    [InlineData(0, 0x100000u)]
    [InlineData(-10, 0xFFFF6u)]
    [InlineData(-40, 0xFFFD8u)]
    [InlineData(-99, 0xFFFD8u)] // clamped to -40
    [InlineData(5, 0x100000u)]  // positive offsets aren't offered
    public void Curve_offsets_are_encoded_and_clamped(int offset, uint expected) =>
        Assert.Equal(expected, RyzenSmuProtocol.EncodeCurveOffset(offset));

    [Fact]
    public void Zephyrus_g14_2024_cpu_uses_the_mobile_mailbox()
    {
        var family = RyzenSmuProtocol.FamilyOf(RyzenCodeName.HawkPoint);
        Assert.Equal(RyzenFamily.Mobile, family);
        Assert.Equal(new SmuMailbox(0x03B10528, 0x03B10578, 0x03B10998), RyzenSmuProtocol.Mp1(family));
        Assert.Equal((false, 0x4Cu), RyzenSmuProtocol.CurveOptimizerCommand(family));
        Assert.Equal(0x19u, RyzenSmuProtocol.TempLimitCommand(family));
    }

    [Fact]
    public void Unknown_cpus_get_no_commands()
    {
        var family = RyzenSmuProtocol.FamilyOf((RyzenCodeName)99);
        Assert.Equal(RyzenFamily.Unsupported, family);
        Assert.Null(RyzenSmuProtocol.Mp1(family));
        Assert.Null(RyzenSmuProtocol.CurveOptimizerCommand(family));
    }

    [Fact]
    public void Execute_buffer_is_name_then_arguments()
    {
        var buffer = PawnIo.BuildExecuteBuffer("ioctl_write_smu_register", [0x03B10528ul, 0x4Cul]);
        Assert.Equal(48, buffer.Length);
        Assert.Equal("ioctl_write_smu_register", Encoding.ASCII.GetString(buffer, 0, 24));
        Assert.Equal(0, buffer[24]);
        Assert.Equal(0x03B10528ul, BitConverter.ToUInt64(buffer, 32));
        Assert.Equal(0x4Cul, BitConverter.ToUInt64(buffer, 40));
    }
}
