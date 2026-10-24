namespace AiO.Core.Asus;

/// <summary>
/// ASUS ATK ACPI device ids. These are firmware facts, as catalogued by the
/// G-Helper project (https://github.com/seerge/g-helper).
/// </summary>
public static class AsusDeviceIds
{
    public const uint PerformanceMode = 0x00120075;
    public const uint VivoBookMode = 0x00110019;

    public const uint GpuEcoRog = 0x00090020;
    public const uint GpuEcoVivo = 0x00090120;
    public const uint GpuMuxRog = 0x00090016;
    public const uint GpuMuxVivo = 0x00090026;

    public const uint BatteryLimit = 0x00120057;

    public const uint ScreenOverdrive = 0x00050019;
    public const uint ScreenOverdriveSupport = 0x00050020;

    public const uint CpuFanSpeed = 0x00110013;
    public const uint GpuFanSpeed = 0x00110014;
    public const uint MidFanSpeed = 0x00110031;

    public const uint CpuFanCurve = 0x00110024;
    public const uint GpuFanCurve = 0x00110025;
    public const uint MidFanCurve = 0x00110032;

    public const uint CpuTemperature = 0x00120094;
    public const uint GpuTemperature = 0x00120097;

    public const uint PptSlow = 0x001200A0;      // sPPT / PL2
    public const uint PptSustained = 0x001200A3; // SPL / PL1
    public const uint PptFast = 0x001200C1;      // fPPT
    public const uint GpuDynamicBoost = 0x001200C0;
    public const uint GpuTempTarget = 0x001200C2;

    public const uint KeyboardBrightnessAcpi = 0x00050021;
}

public enum AsusPerformanceMode
{
    Balanced = 0,
    Turbo = 1,
    Silent = 2,
}

public enum AsusGpuMode
{
    Eco,
    Standard,
    Ultimate,
}

public enum AsusFan
{
    Cpu,
    Gpu,
    Mid,
}

/// <summary>Packing for calls to the \\.\ATKACPI driver.</summary>
public static class AsusAcpiProtocol
{
    public const uint IoControlCode = 0x0022240C;
    public const uint MethodGet = 0x53545344; // "DSTS"
    public const uint MethodSet = 0x53564544; // "DEVS"

    /// <summary>Call buffer: method id, argument length, arguments (all little-endian).</summary>
    public static byte[] BuildCall(uint methodId, ReadOnlySpan<byte> args)
    {
        var buffer = new byte[8 + args.Length];
        BitConverter.TryWriteBytes(buffer.AsSpan(0), methodId);
        BitConverter.TryWriteBytes(buffer.AsSpan(4), (uint)args.Length);
        args.CopyTo(buffer.AsSpan(8));
        return buffer;
    }

    public static byte[] DeviceArgs(uint deviceId, uint value)
    {
        var args = new byte[8];
        BitConverter.TryWriteBytes(args.AsSpan(0), deviceId);
        BitConverter.TryWriteBytes(args.AsSpan(4), value);
        return args;
    }

    public static byte[] DeviceArgs(uint deviceId, ReadOnlySpan<byte> payload)
    {
        var args = new byte[4 + payload.Length];
        BitConverter.TryWriteBytes(args.AsSpan(0), deviceId);
        payload.CopyTo(args.AsSpan(4));
        return args;
    }

    /// <summary>
    /// DSTS results carry a "present" flag in bit 16; subtracting it gives the value,
    /// and a negative result means the firmware doesn't implement the device id.
    /// </summary>
    public static int DecodeGet(ReadOnlySpan<byte> output) => BitConverter.ToInt32(output) - 0x10000;

    /// <summary>Fan-curve reads use 0 = balanced, 1 = silent, 2 = turbo (silent and turbo are swapped).</summary>
    public static uint FanCurveModeArgument(AsusPerformanceMode mode) => mode switch
    {
        AsusPerformanceMode.Turbo => 2,
        AsusPerformanceMode.Silent => 1,
        _ => 0,
    };
}
