namespace AiO.Core.Asus;

/// <summary>
/// An ASUS fan curve: 8 points of (temperature °C, fan duty %).
/// On the wire it is 16 bytes: 8 temperatures followed by 8 duty values.
/// </summary>
public sealed class FanCurve
{
    public const int PointCount = 8;

    public byte[] Temperatures { get; set; } = new byte[PointCount];
    public byte[] Duties { get; set; } = new byte[PointCount];

    public static FanCurve FromBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 2 * PointCount) throw new ArgumentException("Fan curve needs 16 bytes", nameof(bytes));
        return new FanCurve
        {
            Temperatures = bytes[..PointCount].ToArray(),
            Duties = bytes.Slice(PointCount, PointCount).ToArray(),
        };
    }

    public byte[] ToBytes()
    {
        var bytes = new byte[2 * PointCount];
        Temperatures.CopyTo(bytes, 0);
        for (int i = 0; i < PointCount; i++) bytes[PointCount + i] = Math.Min(Duties[i], (byte)100);
        return bytes;
    }

    public bool IsEmpty => Temperatures.All(t => t == 0) && Duties.All(d => d == 0);

    /// <summary>
    /// A curve the firmware will accept: right length, temperatures strictly rising within 20-110 °C,
    /// duties never falling and within 0-100 %.
    /// </summary>
    public bool IsValid()
    {
        if (Temperatures.Length != PointCount || Duties.Length != PointCount || IsEmpty) return false;
        for (int i = 0; i < PointCount; i++)
        {
            if (Temperatures[i] < 20 || Temperatures[i] > 110 || Duties[i] > 100) return false;
            if (i > 0 && (Temperatures[i] <= Temperatures[i - 1] || Duties[i] < Duties[i - 1])) return false;
        }
        return true;
    }

    /// <summary>Forces monotonic, in-range values (used after the user drags a point).</summary>
    public void Normalize()
    {
        for (int i = 0; i < PointCount; i++)
        {
            byte minTemp = (byte)(i == 0 ? 20 : Temperatures[i - 1] + 1);
            Temperatures[i] = Math.Clamp(Temperatures[i], minTemp, (byte)(110 - (PointCount - 1 - i)));
            byte minDuty = i == 0 ? (byte)0 : Duties[i - 1];
            Duties[i] = Math.Clamp(Duties[i], minDuty, (byte)100);
        }
    }

    public FanCurve Clone() => new() { Temperatures = (byte[])Temperatures.Clone(), Duties = (byte[])Duties.Clone() };

    public static FanCurve Default(AsusPerformanceMode mode) => mode switch
    {
        AsusPerformanceMode.Silent => new FanCurve
        {
            Temperatures = [30, 45, 55, 65, 72, 80, 88, 95],
            Duties = [0, 0, 10, 20, 30, 45, 60, 75],
        },
        AsusPerformanceMode.Turbo => new FanCurve
        {
            Temperatures = [30, 40, 50, 60, 68, 75, 82, 90],
            Duties = [20, 30, 40, 55, 70, 85, 95, 100],
        },
        _ => new FanCurve
        {
            Temperatures = [30, 45, 55, 62, 70, 78, 85, 92],
            Duties = [0, 10, 20, 35, 50, 65, 80, 95],
        },
    };
}
