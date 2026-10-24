using System.Buffers.Binary;
using AiO.Core.OpenRgb;
using AiO.Core.Razer;

namespace AiO.Core.Tests;

public class OpenRgbTests
{
    [Fact]
    public void Header_round_trips()
    {
        var header = OpenRgbProtocol.BuildHeader(3, OpenRgbPacket.UpdateLeds, 42);
        Assert.Equal("ORGB"u8.ToArray(), header[..4]);
        Assert.Equal((3u, OpenRgbPacket.UpdateLeds, 42), OpenRgbProtocol.ParseHeader(header));
    }

    [Fact]
    public void Header_rejects_bad_magic()
    {
        Assert.Throws<InvalidDataException>(() => OpenRgbProtocol.ParseHeader(new byte[16]));
    }

    [Fact]
    public void Update_leds_payload_layout()
    {
        var payload = OpenRgbProtocol.BuildUpdateLeds([new RgbColor(1, 2, 3), new RgbColor(4, 5, 6)]);
        Assert.Equal(4 + 2 + 8, payload.Length);
        Assert.Equal((uint)payload.Length, BinaryPrimitives.ReadUInt32LittleEndian(payload));
        Assert.Equal(2, BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(4)));
        Assert.Equal(new byte[] { 1, 2, 3, 0, 4, 5, 6, 0 }, payload[6..]);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(3u)]
    public void Parses_controller_data(uint version)
    {
        var payload = BuildController(version);
        var c = OpenRgbProtocol.ParseController(payload, version, 7);

        Assert.Equal(7, c.Index);
        Assert.Equal(1, c.Type);
        Assert.Equal("Razer Basilisk V3", c.Name);
        Assert.Equal(version >= 1 ? "Razer" : "", c.Vendor);
        Assert.Equal("serial-1", c.Serial);
        Assert.Equal(1, c.ActiveMode);
        Assert.Equal(["Direct", "Static"], c.Modes.Select(m => m.Name));
        Assert.Equal(new RgbColor(9, 8, 7), c.Modes[1].Colors.Single());
        Assert.Equal(version >= 3 ? 100u : 0u, c.Modes[1].BrightnessMax);
        Assert.Equal(2, c.Zones.Count);
        Assert.Equal(new OpenRgbZone("Keys", 2, 4, 4, 4), c.Zones[1]);
        Assert.Equal(["Logo", "Wheel", "K1", "K2", "K3", "K4"], c.LedNames);
        Assert.Equal(6, c.Colors.Count);
        Assert.Equal(new RgbColor(0xAA, 0xBB, 0xCC), c.Colors[5]);
    }

    [Fact]
    public void Truncated_controller_data_throws()
    {
        var payload = BuildController(3);
        Assert.Throws<InvalidDataException>(() => OpenRgbProtocol.ParseController(payload.AsSpan(0, payload.Length - 3), 3, 0));
    }

    [Fact]
    public void Update_mode_round_trips_through_parser_layout()
    {
        var mode = new OpenRgbMode { Name = "Breathing", Value = 2, Flags = 1, SpeedMax = 10, ColorsMax = 1, Colors = [new RgbColor(1, 1, 1)] };
        var payload = OpenRgbProtocol.BuildUpdateMode(4, mode, 3);
        Assert.Equal((uint)payload.Length, BinaryPrimitives.ReadUInt32LittleEndian(payload));
        Assert.Equal(4, BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(4)));
    }

    /// <summary>Builds a controller-data payload the way an OpenRGB server would.</summary>
    private static byte[] BuildController(uint version)
    {
        var w = new OpenRgbProtocol.Writer();
        w.UInt32(0);
        w.Int32(1); // type
        w.String("Razer Basilisk V3");
        if (version >= 1) w.String("Razer");
        w.String("Mouse");
        w.String("1.0");
        w.String("serial-1");
        w.String("HID: path");

        w.UInt16(2);
        w.Int32(1);
        OpenRgbProtocol.WriteMode(w, new OpenRgbMode { Name = "Direct" }, version);
        OpenRgbProtocol.WriteMode(w, new OpenRgbMode { Name = "Static", BrightnessMax = 100, ColorsMax = 1, Colors = [new RgbColor(9, 8, 7)] }, version);

        w.UInt16(2);
        // Zone with no matrix
        w.String("Logo");
        w.Int32(0);
        w.UInt32(2); w.UInt32(2); w.UInt32(2);
        w.UInt16(0);
        // Zone with a 1x4 matrix: height, width, then 4 entries
        w.String("Keys");
        w.Int32(2);
        w.UInt32(4); w.UInt32(4); w.UInt32(4);
        w.UInt16(8 + 4 * 4);
        w.UInt32(1); w.UInt32(4);
        for (uint i = 0; i < 4; i++) w.UInt32(i);

        string[] leds = ["Logo", "Wheel", "K1", "K2", "K3", "K4"];
        w.UInt16((ushort)leds.Length);
        foreach (var led in leds) { w.String(led); w.UInt32(0); }

        w.UInt16(6);
        for (int i = 0; i < 5; i++) w.Color(new RgbColor(0, 0, 0));
        w.Color(new RgbColor(0xAA, 0xBB, 0xCC));
        return w.ToArrayWithSizePrefix();
    }
}
