using System.Management;
using System.Runtime.InteropServices;

namespace AiO.Core.Power;

/// <summary>Battery wear and live power draw, from the battery driver's WMI classes.</summary>
public sealed record BatteryReport(
    int? DesignedMilliwattHours,
    int? FullChargeMilliwattHours,
    int? RemainingMilliwattHours,
    int? ChargeRateMilliwatts,
    int? DischargeRateMilliwatts,
    int? CycleCount)
{
    /// <summary>Full-charge capacity as a share of the design capacity, 0-100+.</summary>
    public double? HealthPercent => DesignedMilliwattHours is > 0 && FullChargeMilliwattHours is int full
        ? 100.0 * full / DesignedMilliwattHours.Value
        : null;

    public string Describe()
    {
        var parts = new List<string>();
        if (HealthPercent is double h && FullChargeMilliwattHours is int full && DesignedMilliwattHours is int design)
            parts.Add($"Health {h:0}% ({full / 1000.0:0.0} of {design / 1000.0:0.0} Wh)");
        if (ChargeRateMilliwatts is > 0 and int charge) parts.Add($"charging at {charge / 1000.0:0.0} W");
        else if (DischargeRateMilliwatts is > 0 and int discharge)
        {
            string text = $"using {discharge / 1000.0:0.0} W";
            if (RemainingMilliwattHours is int left && discharge > 0)
            {
                var time = TimeSpan.FromHours(left / (double)discharge);
                text += $" (about {(int)time.TotalHours}h {time.Minutes:00}m left)";
            }
            parts.Add(text);
        }
        if (CycleCount is > 0 and int cycles) parts.Add($"{cycles} charge cycles");
        return parts.Count == 0 ? "The battery doesn't report its health." : string.Join(" · ", parts);
    }
}

public static class BatteryInfo
{
    public static BatteryReport Read() => new(
        ReadInt("BatteryStaticData", "DesignedCapacity"),
        ReadInt("BatteryFullChargedCapacity", "FullChargedCapacity"),
        ReadInt("BatteryStatus", "RemainingCapacity"),
        ReadInt("BatteryStatus", "ChargeRate"),
        ReadInt("BatteryStatus", "DischargeRate"),
        ReadInt("BatteryCycleCount", "CycleCount"));

    private static int? ReadInt(string wmiClass, string property)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\WMI", $"SELECT {property} FROM {wmiClass}");
            using var results = searcher.Get();
            foreach (ManagementObject o in results)
                using (o)
                    if (o[property] is { } v) return Convert.ToInt32(v);
        }
        catch (ManagementException) { }
        catch (COMException) { }
        catch (UnauthorizedAccessException) { }
        catch (OverflowException) { }
        return null;
    }
}
