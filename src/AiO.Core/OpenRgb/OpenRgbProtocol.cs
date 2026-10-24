using System.Buffers.Binary;
using System.Text;
using AiO.Core.Razer;

namespace AiO.Core.OpenRgb;

public static class OpenRgbPacket
{
    public const uint RequestControllerCount = 0;
    public const uint RequestControllerData = 1;
    public const uint RequestProtocolVersion = 40;
    public const uint SetClientName = 50;
    public const uint DeviceListUpdated = 100;
    public const uint UpdateLeds = 1050;
    public const uint UpdateZoneLeds = 1051;
    public const uint SetCustomMode = 1100;
    public const uint UpdateMode = 1101;
    public const uint SaveMode = 1102;
}

public sealed class OpenRgbMode
{
    public string Name { get; set; } = "";
    public int Value { get; set; }
    public uint Flags { get; set; }
    public uint SpeedMin { get; set; }
    public uint SpeedMax { get; set; }
    public uint BrightnessMin { get; set; }
    public uint BrightnessMax { get; set; }
    public uint ColorsMin { get; set; }
    public uint ColorsMax { get; set; }
    public uint Speed { get; set; }
    public uint Brightness { get; set; }
    public uint Direction { get; set; }
    public uint ColorMode { get; set; }
    public List<RgbColor> Colors { get; set; } = [];

    public const uint FlagHasSpeed = 1 << 0;
    public const uint FlagHasBrightness = 1 << 4;
    public const uint ColorModeModeSpecific = 2;

    public override string ToString() => Name;
}

public sealed record OpenRgbZone(string Name, int Type, uint LedsMin, uint LedsMax, uint LedCount);

public sealed class OpenRgbController
{
    public int Index { get; set; }
    public int Type { get; set; }
    public string Name { get; set; } = "";
    public string Vendor { get; set; } = "";
    public string Description { get; set; } = "";
    public string Version { get; set; } = "";
    public string Serial { get; set; } = "";
    public string Location { get; set; } = "";
    public int ActiveMode { get; set; }
    public List<OpenRgbMode> Modes { get; set; } = [];
    public List<OpenRgbZone> Zones { get; set; } = [];
    public List<string> LedNames { get; set; } = [];
    public List<RgbColor> Colors { get; set; } = [];

    public override string ToString() => Name;
}

/// <summary>
/// Serialisation for the OpenRGB SDK network protocol (see OpenRGB's Documentation/OpenRGBSDK.md).
/// AiO speaks protocol version 3, the newest version without zone segments.
/// All integers are little-endian.
/// </summary>
public static class OpenRgbProtocol
{
    public const uint ClientVersion = 3;
    public const int HeaderSize = 16;
    private static ReadOnlySpan<byte> Magic => "ORGB"u8;

    public static byte[] BuildHeader(uint deviceId, uint packetId, int payloadSize)
    {
        var header = new byte[HeaderSize];
        Magic.CopyTo(header);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), deviceId);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), packetId);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), (uint)payloadSize);
        return header;
    }

    public static (uint DeviceId, uint PacketId, int PayloadSize) ParseHeader(ReadOnlySpan<byte> header)
    {
        if (header.Length < HeaderSize || !header[..4].SequenceEqual(Magic))
            throw new InvalidDataException("Not an OpenRGB packet");
        return (BinaryPrimitives.ReadUInt32LittleEndian(header[4..]),
                BinaryPrimitives.ReadUInt32LittleEndian(header[8..]),
                (int)BinaryPrimitives.ReadUInt32LittleEndian(header[12..]));
    }

    public static OpenRgbController ParseController(ReadOnlySpan<byte> payload, uint version, int index)
    {
        var r = new Reader(payload);
        r.UInt32(); // data_size
        var c = new OpenRgbController { Index = index, Type = r.Int32(), Name = r.String() };
        if (version >= 1) c.Vendor = r.String();
        c.Description = r.String();
        c.Version = r.String();
        c.Serial = r.String();
        c.Location = r.String();

        int modeCount = r.UInt16();
        c.ActiveMode = r.Int32();
        for (int i = 0; i < modeCount; i++) c.Modes.Add(ReadMode(ref r, version));

        int zoneCount = r.UInt16();
        for (int i = 0; i < zoneCount; i++)
        {
            string name = r.String();
            int type = r.Int32();
            uint min = r.UInt32(), max = r.UInt32(), count = r.UInt32();
            int matrixLength = r.UInt16();
            r.Skip(matrixLength); // height, width and the map itself; AiO doesn't use per-key layouts.
            if (version >= 4)
            {
                int segments = r.UInt16();
                for (int s = 0; s < segments; s++) { r.String(); r.Skip(12); }
            }
            if (version >= 5) r.UInt32();
            c.Zones.Add(new OpenRgbZone(name, type, min, max, count));
        }

        int ledCount = r.UInt16();
        for (int i = 0; i < ledCount; i++)
        {
            c.LedNames.Add(r.String());
            r.UInt32(); // value
        }

        int colorCount = r.UInt16();
        for (int i = 0; i < colorCount; i++) c.Colors.Add(r.Color());
        return c;
    }

    private static OpenRgbMode ReadMode(ref Reader r, uint version)
    {
        var m = new OpenRgbMode
        {
            Name = r.String(),
            Value = r.Int32(),
            Flags = r.UInt32(),
            SpeedMin = r.UInt32(),
            SpeedMax = r.UInt32(),
        };
        if (version >= 3)
        {
            m.BrightnessMin = r.UInt32();
            m.BrightnessMax = r.UInt32();
        }
        m.ColorsMin = r.UInt32();
        m.ColorsMax = r.UInt32();
        m.Speed = r.UInt32();
        if (version >= 3) m.Brightness = r.UInt32();
        m.Direction = r.UInt32();
        m.ColorMode = r.UInt32();
        int colors = r.UInt16();
        for (int i = 0; i < colors; i++) m.Colors.Add(r.Color());
        return m;
    }

    public static void WriteMode(Writer w, OpenRgbMode m, uint version)
    {
        w.String(m.Name);
        w.Int32(m.Value);
        w.UInt32(m.Flags);
        w.UInt32(m.SpeedMin);
        w.UInt32(m.SpeedMax);
        if (version >= 3)
        {
            w.UInt32(m.BrightnessMin);
            w.UInt32(m.BrightnessMax);
        }
        w.UInt32(m.ColorsMin);
        w.UInt32(m.ColorsMax);
        w.UInt32(m.Speed);
        if (version >= 3) w.UInt32(m.Brightness);
        w.UInt32(m.Direction);
        w.UInt32(m.ColorMode);
        w.UInt16((ushort)m.Colors.Count);
        foreach (var color in m.Colors) w.Color(color);
    }

    public static byte[] BuildUpdateLeds(IReadOnlyList<RgbColor> colors)
    {
        var w = new Writer();
        w.UInt32(0); // data_size placeholder
        w.UInt16((ushort)colors.Count);
        foreach (var c in colors) w.Color(c);
        return w.ToArrayWithSizePrefix();
    }

    public static byte[] BuildUpdateMode(int modeIndex, OpenRgbMode mode, uint version)
    {
        var w = new Writer();
        w.UInt32(0);
        w.Int32(modeIndex);
        WriteMode(w, mode, version);
        return w.ToArrayWithSizePrefix();
    }

    public static byte[] BuildUInt32(uint value)
    {
        var b = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(b, value);
        return b;
    }

    public static byte[] BuildClientName(string name) => Encoding.ASCII.GetBytes(name + "\0");

    public ref struct Reader(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> _data = data;
        private int _pos;

        private ReadOnlySpan<byte> Take(int n)
        {
            if (_pos + n > _data.Length) throw new InvalidDataException("OpenRGB packet truncated");
            var s = _data.Slice(_pos, n);
            _pos += n;
            return s;
        }

        public void Skip(int n) => Take(n);
        public ushort UInt16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
        public uint UInt32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
        public int Int32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));

        public string String()
        {
            int length = UInt16();
            return Encoding.UTF8.GetString(Take(length)).TrimEnd('\0');
        }

        public RgbColor Color()
        {
            var b = Take(4); // R, G, B, padding
            return new RgbColor(b[0], b[1], b[2]);
        }
    }

    public sealed class Writer
    {
        private readonly MemoryStream _stream = new();

        public void UInt16(ushort v) { Span<byte> b = stackalloc byte[2]; BinaryPrimitives.WriteUInt16LittleEndian(b, v); _stream.Write(b); }
        public void UInt32(uint v) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(b, v); _stream.Write(b); }
        public void Int32(int v) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteInt32LittleEndian(b, v); _stream.Write(b); }
        public void Bytes(ReadOnlySpan<byte> b) => _stream.Write(b);

        public void String(string s)
        {
            var bytes = Encoding.UTF8.GetBytes(s + "\0");
            UInt16((ushort)bytes.Length);
            _stream.Write(bytes);
        }

        public void Color(RgbColor c) => _stream.Write([c.R, c.G, c.B, 0]);

        public byte[] ToArray() => _stream.ToArray();

        /// <summary>Returns the bytes with the leading 4-byte data_size field filled in.</summary>
        public byte[] ToArrayWithSizePrefix()
        {
            var bytes = _stream.ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)bytes.Length);
            return bytes;
        }
    }
}
