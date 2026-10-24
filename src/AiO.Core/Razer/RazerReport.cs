namespace AiO.Core.Razer;

public enum RazerStatus : byte
{
    New = 0x00,
    Busy = 0x01,
    Successful = 0x02,
    Failure = 0x03,
    Timeout = 0x04,
    NotSupported = 0x05,
}

/// <summary>
/// The 90-byte control report Razer devices use for every command.
/// Layout follows the protocol documented by the OpenRazer project:
/// status, transaction id, remaining packets (BE16), protocol type, data size,
/// command class, command id, 80 argument bytes, CRC, reserved.
/// </summary>
public sealed class RazerReport
{
    public const int Size = 90;
    public const int ArgumentCount = 80;

    public RazerStatus Status { get; set; }
    public byte TransactionId { get; set; }
    public ushort RemainingPackets { get; set; }
    public byte ProtocolType { get; set; }
    public byte DataSize { get; set; }
    public byte CommandClass { get; set; }
    public byte CommandId { get; set; }
    public byte[] Arguments { get; } = new byte[ArgumentCount];

    public RazerReport(byte commandClass, byte commandId, byte dataSize)
    {
        CommandClass = commandClass;
        CommandId = commandId;
        DataSize = dataSize;
    }

    public byte[] ToBytes()
    {
        var bytes = new byte[Size];
        bytes[0] = (byte)Status;
        bytes[1] = TransactionId;
        bytes[2] = (byte)(RemainingPackets >> 8);
        bytes[3] = (byte)RemainingPackets;
        bytes[4] = ProtocolType;
        bytes[5] = DataSize;
        bytes[6] = CommandClass;
        bytes[7] = CommandId;
        Arguments.CopyTo(bytes, 8);
        bytes[88] = ComputeCrc(bytes);
        return bytes;
    }

    /// <summary>Feature-report buffer for HidD_SetFeature: report id 0 followed by the report.</summary>
    public byte[] ToFeatureReport()
    {
        var buffer = new byte[Size + 1];
        ToBytes().CopyTo(buffer, 1);
        return buffer;
    }

    public static RazerReport Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < Size) throw new ArgumentException($"Razer report needs {Size} bytes", nameof(bytes));
        var report = new RazerReport(bytes[6], bytes[7], bytes[5])
        {
            Status = (RazerStatus)bytes[0],
            TransactionId = bytes[1],
            RemainingPackets = (ushort)((bytes[2] << 8) | bytes[3]),
            ProtocolType = bytes[4],
        };
        bytes.Slice(8, ArgumentCount).CopyTo(report.Arguments);
        return report;
    }

    /// <summary>Parses a buffer returned by HidD_GetFeature (leading report id byte).</summary>
    public static RazerReport ParseFeatureReport(ReadOnlySpan<byte> buffer) => Parse(buffer[1..]);

    /// <summary>XOR of bytes 2..87, i.e. everything except status, transaction id, CRC and reserved.</summary>
    public static byte ComputeCrc(ReadOnlySpan<byte> report)
    {
        byte crc = 0;
        for (int i = 2; i < 88; i++) crc ^= report[i];
        return crc;
    }

    /// <summary>True when this (response) report answers <paramref name="request"/>.</summary>
    public bool IsResponseTo(RazerReport request) =>
        CommandClass == request.CommandClass && CommandId == request.CommandId;
}
