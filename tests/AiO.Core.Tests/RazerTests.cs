using AiO.Core.Razer;

namespace AiO.Core.Tests;

public class RazerTests
{
    private static string Hex(byte[] bytes, int start, int count) =>
        Convert.ToHexString(bytes, start, count).ToLowerInvariant();

    [Fact]
    public void Report_layout_matches_protocol()
    {
        var report = RazerCommands.SetBrightness(RazerLed.Backlight, 0xB7);
        report.TransactionId = 0x3F;
        var bytes = report.ToBytes();

        Assert.Equal(RazerReport.Size, bytes.Length);
        // status, transaction, remaining(2), protocol, size, class, id, args...
        Assert.Equal("003f0000" + "00" + "03" + "0f" + "04" + "0105b7", Hex(bytes, 0, 11));
    }

    [Fact]
    public void Crc_is_xor_of_bytes_2_to_87()
    {
        var report = RazerCommands.SetDpi(1600, 800);
        var bytes = report.ToBytes();

        byte expected = 0;
        for (int i = 2; i < 88; i++) expected ^= bytes[i];
        Assert.Equal(expected, bytes[88]);
        Assert.Equal(0, bytes[89]);
    }

    [Fact]
    public void Crc_ignores_status_and_transaction_id()
    {
        var a = RazerCommands.GetFirmwareVersion();
        var b = RazerCommands.GetFirmwareVersion();
        a.TransactionId = 0x1F;
        b.TransactionId = 0xFF;
        Assert.Equal(a.ToBytes()[88], b.ToBytes()[88]);
    }

    [Fact]
    public void Feature_report_is_prefixed_with_report_id_zero()
    {
        var buffer = RazerCommands.GetFirmwareVersion().ToFeatureReport();
        Assert.Equal(91, buffer.Length);
        Assert.Equal(0, buffer[0]);
        Assert.Equal(0x81, buffer[8]); // command id at report offset 7
    }

    [Fact]
    public void Static_effect_matches_captured_packet()
    {
        // Captured: 0f 02 010501000001ff0000 (VARSTORE, backlight, static, ..., 1 colour, red)
        var bytes = RazerCommands.SetEffect(RazerLed.Backlight, RazerEffect.Static, [new RgbColor(255, 0, 0)]).ToBytes();
        Assert.Equal("09" + "0f02" + "010501000001ff0000", Hex(bytes, 5, 12));
    }

    [Fact]
    public void Breathing_single_matches_captured_packet()
    {
        var bytes = RazerCommands.SetEffect(RazerLed.Backlight, RazerEffect.Breathing, [new RgbColor(0, 255, 0)]).ToBytes();
        Assert.Equal("09" + "0f02" + "01050201000100ff00", Hex(bytes, 5, 12));
    }

    [Fact]
    public void Spectrum_matches_captured_packet()
    {
        var bytes = RazerCommands.SetEffect(RazerLed.Backlight, RazerEffect.Spectrum).ToBytes();
        Assert.Equal("06" + "0f02" + "010503000000", Hex(bytes, 5, 9));
    }

    [Fact]
    public void Dpi_round_trips_big_endian()
    {
        var request = RazerCommands.SetDpi(26000, 400);
        Assert.Equal(new byte[] { 0x01, 0x65, 0x90, 0x01, 0x90 }, request.Arguments[..5]);

        var parsed = RazerReport.Parse(request.ToBytes());
        Assert.Equal((26000, 400), RazerCommands.ParseDpi(parsed));
    }

    [Theory]
    [InlineData(1000, 0x01)]
    [InlineData(500, 0x02)]
    [InlineData(125, 0x08)]
    public void Polling_rate_codes(int hz, byte code)
    {
        var report = RazerCommands.SetPollingRate(hz);
        Assert.Equal(code, report.Arguments[0]);
        Assert.Equal(hz, RazerCommands.ParsePollingRate(report));
    }

    [Fact]
    public void Parse_reads_status_and_response_match()
    {
        var request = RazerCommands.GetBatteryLevel();
        var bytes = request.ToBytes();
        bytes[0] = (byte)RazerStatus.Successful;
        bytes[9] = 255; // argument 1

        var response = RazerReport.Parse(bytes);
        Assert.Equal(RazerStatus.Successful, response.Status);
        Assert.True(response.IsResponseTo(request));
        Assert.Equal(100, RazerCommands.ParseBatteryPercent(response));
    }

    [Fact]
    public void Catalog_knows_common_devices()
    {
        Assert.Equal((RazerDeviceType.Mouse, "Razer DeathAdder V2"), RazerDeviceCatalog.Describe(0x0084, ""));
        Assert.Equal(RazerDeviceType.Unknown, RazerDeviceCatalog.Describe(0xFFFE, "").Type);
    }

    [Fact]
    public void Hex_colour_parsing()
    {
        Assert.Equal(new RgbColor(0x12, 0xAB, 0xEF), RgbColor.FromHex("#12abef"));
        Assert.Equal("#12ABEF", new RgbColor(0x12, 0xAB, 0xEF).ToHex());
        Assert.Throws<FormatException>(() => RgbColor.FromHex("#123"));
    }
}

public class RazerExtendedTests
{
    [Theory]
    [InlineData(8000, 0x01)]
    [InlineData(4000, 0x02)]
    [InlineData(2000, 0x04)]
    [InlineData(1000, 0x08)]
    [InlineData(125, 0x40)]
    public void Hyperpolling_codes_round_trip(int hz, byte code)
    {
        var r = RazerCommands.SetPollingRate2(hz, 0x00);
        Assert.Equal((0x00, 0x40, 0x02), (r.CommandClass, r.CommandId, r.DataSize));
        Assert.Equal(code, r.Arguments[1]);
        Assert.Equal(hz, RazerCommands.ParsePollingRate2(r));
    }

    [Fact]
    public void Dpi_stages_layout_and_parse()
    {
        var r = RazerCommands.SetDpiStages([800, 1800, 3200], activeStage: 2);
        Assert.Equal((0x04, 0x06, 0x26), (r.CommandClass, r.CommandId, r.DataSize));
        Assert.Equal(new byte[] { 0x01, 2, 3, 0, 0x03, 0x20, 0x03, 0x20, 0, 0, 1, 0x07, 0x08 }, r.Arguments[..13]);

        var (active, stages) = RazerCommands.ParseDpiStages(r);
        Assert.Equal(2, active);
        Assert.Equal([800, 1800, 3200], stages);
    }

    [Fact]
    public void Dpi_stage_count_is_limited()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RazerCommands.SetDpiStages([], 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => RazerCommands.SetDpiStages([1, 2, 3, 4, 5, 6], 1));
    }

    [Fact]
    public void Idle_time_is_clamped_and_big_endian()
    {
        var r = RazerCommands.SetIdleTime(5000);
        Assert.Equal(900, RazerCommands.ParseIdleTime(r));
        Assert.Equal(new byte[] { 0x03, 0x84 }, r.Arguments[..2]);
    }

    [Fact]
    public void Low_battery_threshold_is_clamped()
    {
        Assert.Equal(0x0C, RazerCommands.SetLowBatteryThreshold(1).Arguments[0]);
        Assert.Equal(0x3F, RazerCommands.SetLowBatteryThreshold(90).Arguments[0]);
        Assert.Equal(10, RazerCommands.ParseLowBatteryPercent(RazerCommands.SetLowBatteryThreshold(10)));
    }
}
