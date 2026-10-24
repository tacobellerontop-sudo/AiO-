using System.Text;
using AiO.Core.Asus;
using AiO.Core.Razer;

namespace AiO.Core.Tests;

public class AsusTests
{
    [Fact]
    public void Set_call_buffer_layout()
    {
        var args = AsusAcpiProtocol.DeviceArgs(AsusDeviceIds.PerformanceMode, (uint)AsusPerformanceMode.Turbo);
        var call = AsusAcpiProtocol.BuildCall(AsusAcpiProtocol.MethodSet, args);

        Assert.Equal("DEVS", Encoding.ASCII.GetString(call, 0, 4));
        Assert.Equal(8u, BitConverter.ToUInt32(call, 4));
        Assert.Equal(new byte[] { 0x75, 0x00, 0x12, 0x00, 0x01, 0x00, 0x00, 0x00 }, call[8..]);
    }

    [Fact]
    public void Get_method_id_spells_dsts()
    {
        var call = AsusAcpiProtocol.BuildCall(AsusAcpiProtocol.MethodGet, AsusAcpiProtocol.DeviceArgs(AsusDeviceIds.BatteryLimit, 0));
        Assert.Equal("DSTS", Encoding.ASCII.GetString(call, 0, 4));
    }

    [Fact]
    public void Buffer_payload_follows_device_id()
    {
        var args = AsusAcpiProtocol.DeviceArgs(AsusDeviceIds.CpuFanCurve, new byte[16]);
        Assert.Equal(20, args.Length);
        Assert.Equal(AsusDeviceIds.CpuFanCurve, BitConverter.ToUInt32(args, 0));
    }

    [Theory]
    [InlineData(0x00010001, 1)]
    [InlineData(0x00010050, 0x50)]
    [InlineData(-2, -2 - 0x10000)] // 0xFFFFFFFE: unsupported
    public void Get_result_strips_presence_flag(int raw, int expected)
    {
        Assert.Equal(expected, AsusAcpiProtocol.DecodeGet(BitConverter.GetBytes(raw)));
    }

    [Fact]
    public void Fan_curve_reads_swap_turbo_and_silent()
    {
        Assert.Equal(0u, AsusAcpiProtocol.FanCurveModeArgument(AsusPerformanceMode.Balanced));
        Assert.Equal(2u, AsusAcpiProtocol.FanCurveModeArgument(AsusPerformanceMode.Turbo));
        Assert.Equal(1u, AsusAcpiProtocol.FanCurveModeArgument(AsusPerformanceMode.Silent));
    }

    [Fact]
    public void Fan_curve_round_trips()
    {
        var curve = FanCurve.Default(AsusPerformanceMode.Turbo);
        var bytes = curve.ToBytes();
        Assert.Equal(16, bytes.Length);
        var parsed = FanCurve.FromBytes(bytes);
        Assert.Equal(curve.Temperatures, parsed.Temperatures);
        Assert.Equal(curve.Duties, parsed.Duties);
    }

    [Fact]
    public void Default_curves_are_valid()
    {
        foreach (var mode in Enum.GetValues<AsusPerformanceMode>())
            Assert.True(FanCurve.Default(mode).IsValid(), mode.ToString());
    }

    [Fact]
    public void Invalid_curves_are_rejected()
    {
        Assert.False(new FanCurve().IsValid()); // all zero

        var falling = FanCurve.Default(AsusPerformanceMode.Balanced);
        falling.Duties[5] = 0;
        Assert.False(falling.IsValid());

        var flatTemp = FanCurve.Default(AsusPerformanceMode.Balanced);
        flatTemp.Temperatures[3] = flatTemp.Temperatures[2];
        Assert.False(flatTemp.IsValid());
    }

    [Fact]
    public void Normalize_repairs_dragged_points()
    {
        var curve = FanCurve.Default(AsusPerformanceMode.Balanced);
        curve.Temperatures[2] = 10;   // dragged below its neighbour and the minimum
        curve.Duties[6] = 5;          // dragged below the previous point
        curve.Duties[7] = 200;
        curve.Normalize();
        Assert.True(curve.IsValid());
        Assert.Equal(100, curve.Duties[7]);
    }

    [Fact]
    public void Aura_static_message_layout()
    {
        var msg = AsusAura.BuildModeMessage(AuraMode.Static, new RgbColor(1, 2, 3), new RgbColor(4, 5, 6), AuraSpeed.Normal);
        Assert.Equal(new byte[] { 0x5D, 0xB3, 0x00, 0x00, 1, 2, 3, 0xEB, 0x00, 0x00, 4, 5, 6 }, msg[..13]);
    }

    [Fact]
    public void Aura_brightness_is_clamped()
    {
        Assert.Equal(new byte[] { 0x5A, 0xBA, 0xC5, 0xC4, 3 }, AsusAura.BuildBrightnessMessage(9));
    }
}

public class AsusKeyboardTests
{
    [Fact]
    public void Init_handshake_spells_asus_tech()
    {
        var msg = AsusAura.BuildInitMessage(AsusAura.InputReportId);
        Assert.Equal(0x5A, msg[0]);
        Assert.Equal("ASUS Tech.Inc.", Encoding.ASCII.GetString(msg, 1, 14));
    }

    [Fact]
    public void Power_message_enables_all_zones()
    {
        Assert.Equal(new byte[] { 0x5D, 0xBD, 0x01, 0xFF, 0x1F, 0xFF, 0xFF, 0xFF }, AsusAura.PowerAllOnMessage);
    }

    [Fact]
    public void Acpi_rgb_payload_layout()
    {
        Assert.Equal(new byte[] { 0xB4, 0x01, 10, 20, 30, 0xF5 },
            AsusAura.BuildAcpiRgbPayload(0xB4, AuraMode.Breathe, new RgbColor(10, 20, 30), AuraSpeed.Fast));
    }

    [Fact]
    public void Acpi_power_state_turns_on_boot_and_awake()
    {
        Assert.Equal(0x000F01BD, AsusAura.AcpiPowerBootAndAwake);
    }
}

public class AsusDetectionTests
{
    [Fact]
    public void Non_asus_pc_says_so()
    {
        var d = AsusDetection.Describe("Dell Inc.", "XPS 15", false, 2);
        Assert.False(d.IsAsus);
        Assert.Contains("Dell Inc.", d.Message);
    }

    [Fact]
    public void Asus_without_driver_points_to_system_control_interface()
    {
        var d = AsusDetection.Describe("ASUSTeK COMPUTER INC.", "ROG Strix G16", false, 2);
        Assert.True(d.IsAsus);
        Assert.Contains("ASUS System Control Interface", d.Message);
    }

    [Fact]
    public void Access_denied_suggests_admin()
    {
        Assert.Contains("administrator", AsusDetection.Describe("ASUSTeK COMPUTER INC.", "", false, 5).Message);
    }

    [Fact]
    public void Opened_driver_reports_connected()
    {
        Assert.True(AsusDetection.Describe("ASUSTeK COMPUTER INC.", "", true, 0).DriverOpened);
    }
}
