using System.Runtime.InteropServices;

namespace AiO.Core.Power;

/// <summary>
/// The Windows "Processor performance boost mode" power setting. Turning boost off keeps the CPU at
/// its base clock: cooler and quieter, slower in bursts. Applies to the active power plan.
/// </summary>
public static class CpuBoost
{
    private static readonly Guid SubProcessor = new("54533251-82be-4824-96c1-47b60b740d00");
    private static readonly Guid PerfBoostMode = new("be337238-0d82-4146-a960-4f3749d470c7");
    private const uint Disabled = 0, Aggressive = 2;

    /// <summary>Null when the setting can't be read.</summary>
    public static bool? IsEnabled(bool onBattery)
    {
        if (PowerGetActiveScheme(IntPtr.Zero, out var schemePtr) != 0) return null;
        try
        {
            var scheme = Marshal.PtrToStructure<Guid>(schemePtr);
            var sub = SubProcessor;
            var setting = PerfBoostMode;
            uint value;
            uint result = onBattery
                ? PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, out value)
                : PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, out value);
            return result == 0 ? value != Disabled : null;
        }
        finally
        {
            LocalFree(schemePtr);
        }
    }

    /// <summary>Sets boost for both plugged-in and battery on the active plan.</summary>
    public static bool Set(bool enabled)
    {
        if (PowerGetActiveScheme(IntPtr.Zero, out var schemePtr) != 0) return false;
        try
        {
            var scheme = Marshal.PtrToStructure<Guid>(schemePtr);
            var sub = SubProcessor;
            var setting = PerfBoostMode;
            uint value = enabled ? Aggressive : Disabled;
            bool ok = PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, value) == 0
                      & PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, value) == 0;
            // Re-activating the plan makes Windows pick up the new value now.
            return ok && PowerSetActiveScheme(IntPtr.Zero, ref scheme) == 0;
        }
        finally
        {
            LocalFree(schemePtr);
        }
    }

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerSetActiveScheme(IntPtr userRootPowerKey, ref Guid schemeGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadACValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroup, ref Guid setting, out uint value);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadDCValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroup, ref Guid setting, out uint value);

    [DllImport("powrprof.dll")]
    private static extern uint PowerWriteACValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroup, ref Guid setting, uint value);

    [DllImport("powrprof.dll")]
    private static extern uint PowerWriteDCValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroup, ref Guid setting, uint value);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);
}
