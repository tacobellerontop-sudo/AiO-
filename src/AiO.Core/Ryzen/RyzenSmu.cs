using System.Diagnostics;

namespace AiO.Core.Ryzen;

/// <summary>CPU code names as numbered by the PawnIO RyzenSMU module.</summary>
public enum RyzenCodeName : long
{
    Undefined = -1,
    Renoir = 1, Rembrandt = 10, Cezanne = 13, Raphael = 16, Lucienne = 22,
    Phoenix = 23, Phoenix2 = 24, DragonRange = 28, HawkPoint = 30,
    StrixPoint = 31, StrixHalo = 32, KrackanPoint = 33, KrackanPoint2 = 34,
}

/// <summary>Groups of CPUs that share SMU mailbox addresses and command ids.</summary>
public enum RyzenFamily { Unsupported, Renoir, Mobile, StrixPoint, Raphael }

public enum SmuResult : uint
{
    Ok = 0x01,
    Failed = 0xFF,
    UnknownCommand = 0xFE,
    RejectedPrerequisite = 0xFD,
    RejectedBusy = 0xFC,
    /// <summary>AiO-side: the mailbox didn't answer in time or a register access failed.</summary>
    NoResponse = 0x00,
}

/// <summary>An SMU mailbox: command, response and first-argument registers.</summary>
public readonly record struct SmuMailbox(uint Command, uint Response, uint Arguments);

/// <summary>
/// Mailbox addresses and command ids for laptop Ryzen CPUs. These are firmware facts, as documented
/// by the RyzenAdj and Universal x86 Tuning Utility projects and used by G-Helper.
/// </summary>
public static class RyzenSmuProtocol
{
    public const int MinCurveOffset = -40;
    public const int MaxCurveOffset = 0;
    public const int MinTempLimit = 70;
    public const int MaxTempLimit = 100;

    public static RyzenFamily FamilyOf(RyzenCodeName cpu) => cpu switch
    {
        RyzenCodeName.Renoir or RyzenCodeName.Lucienne or RyzenCodeName.Cezanne => RyzenFamily.Renoir,
        RyzenCodeName.Rembrandt or RyzenCodeName.Phoenix or RyzenCodeName.Phoenix2 or RyzenCodeName.HawkPoint => RyzenFamily.Mobile,
        RyzenCodeName.StrixPoint or RyzenCodeName.KrackanPoint or RyzenCodeName.KrackanPoint2 or RyzenCodeName.StrixHalo => RyzenFamily.StrixPoint,
        RyzenCodeName.Raphael or RyzenCodeName.DragonRange => RyzenFamily.Raphael,
        _ => RyzenFamily.Unsupported,
    };

    public static SmuMailbox? Mp1(RyzenFamily family) => family switch
    {
        RyzenFamily.Renoir => new(0x03B10528, 0x03B10564, 0x03B10998),
        RyzenFamily.Mobile => new(0x03B10528, 0x03B10578, 0x03B10998),
        RyzenFamily.StrixPoint => new(0x03B10928, 0x03B10978, 0x03B10998),
        RyzenFamily.Raphael => new(0x03B10530, 0x03B1057C, 0x03B109C4),
        _ => null,
    };

    public static SmuMailbox? Psmu(RyzenFamily family) => family switch
    {
        RyzenFamily.Renoir or RyzenFamily.Mobile or RyzenFamily.StrixPoint => new(0x03B10A20, 0x03B10A80, 0x03B10A88),
        RyzenFamily.Raphael => new(0x03B10524, 0x03B10570, 0x03B10A40),
        _ => null,
    };

    /// <summary>All-core Curve Optimizer: which mailbox and command id.</summary>
    public static (bool Psmu, uint Command)? CurveOptimizerCommand(RyzenFamily family) => family switch
    {
        RyzenFamily.Renoir => (false, 0x55),
        RyzenFamily.Mobile or RyzenFamily.StrixPoint => (false, 0x4C),
        RyzenFamily.Raphael => (true, 0x07),
        _ => null,
    };

    /// <summary>CPU temperature limit (Tctl): MP1 command id.</summary>
    public static uint? TempLimitCommand(RyzenFamily family) => family switch
    {
        RyzenFamily.Renoir or RyzenFamily.Mobile or RyzenFamily.StrixPoint => 0x19,
        RyzenFamily.Raphael => 0x3F,
        _ => null,
    };

    /// <summary>Negative Curve Optimizer counts are sent as 0x100000 + offset (20-bit two's complement style).</summary>
    public static uint EncodeCurveOffset(int offset)
    {
        int clamped = Math.Clamp(offset, MinCurveOffset, MaxCurveOffset);
        return (uint)(0x100000 + clamped);
    }
}

/// <summary>Sends SMU commands through PawnIO's RyzenSMU module.</summary>
public sealed class RyzenSmu : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(200);
    private readonly PawnIo _io;

    public RyzenCodeName CodeName { get; }
    public RyzenFamily Family => RyzenSmuProtocol.FamilyOf(CodeName);

    private RyzenSmu(PawnIo io, RyzenCodeName codeName)
    {
        _io = io;
        CodeName = codeName;
    }

    /// <summary>Opens the driver and loads the module; explains why not when it can't.</summary>
    public static RyzenSmu? Open(byte[] module, out string problem)
    {
        var io = PawnIo.Open(out var status);
        if (io is null)
        {
            problem = status switch
            {
                PawnIoStatus.NotInstalled => "The PawnIO driver isn't installed.",
                PawnIoStatus.AccessDenied => "AiO needs to run as administrator.",
                _ => "The PawnIO driver couldn't be opened.",
            };
            return null;
        }
        if (!io.LoadModule(module))
        {
            io.Dispose();
            problem = "This CPU isn't supported by the RyzenSMU module.";
            return null;
        }
        Span<ulong> result = stackalloc ulong[1];
        var codeName = io.Execute("ioctl_get_code_name", [], result) ? (RyzenCodeName)(long)result[0] : RyzenCodeName.Undefined;
        problem = "";
        return new RyzenSmu(io, codeName);
    }

    public SmuResult SetCurveOptimizer(int offset)
    {
        if (RyzenSmuProtocol.CurveOptimizerCommand(Family) is not { } c) return SmuResult.UnknownCommand;
        var box = c.Psmu ? RyzenSmuProtocol.Psmu(Family) : RyzenSmuProtocol.Mp1(Family);
        return box is { } b ? Send(b, c.Command, RyzenSmuProtocol.EncodeCurveOffset(offset)) : SmuResult.UnknownCommand;
    }

    public SmuResult SetTempLimit(int celsius)
    {
        if (RyzenSmuProtocol.TempLimitCommand(Family) is not uint cmd || RyzenSmuProtocol.Mp1(Family) is not { } box)
            return SmuResult.UnknownCommand;
        return Send(box, cmd, (uint)Math.Clamp(celsius, RyzenSmuProtocol.MinTempLimit, RyzenSmuProtocol.MaxTempLimit));
    }

    private SmuResult Send(SmuMailbox box, uint command, uint argument)
    {
        // Other hardware tools (LibreHardwareMonitor, HWiNFO...) take this mutex around SMU/PCI access.
        using var pci = new Mutex(false, @"Global\Access_PCI");
        bool owned = false;
        try
        {
            try { owned = pci.WaitOne(TimeSpan.FromSeconds(2)); }
            catch (AbandonedMutexException) { owned = true; }
            if (!owned) return SmuResult.RejectedBusy;

            if (!WaitForResponse(box.Response, out _)) return SmuResult.RejectedBusy;
            if (!Write(box.Response, 0)) return SmuResult.NoResponse;
            for (uint i = 0; i < 6; i++)
                if (!Write(box.Arguments + i * 4, i == 0 ? argument : 0)) return SmuResult.NoResponse;
            if (!Write(box.Command, command)) return SmuResult.NoResponse;
            return WaitForResponse(box.Response, out uint status) ? (SmuResult)status : SmuResult.NoResponse;
        }
        finally
        {
            if (owned) pci.ReleaseMutex();
        }
    }

    private bool WaitForResponse(uint address, out uint value)
    {
        var clock = Stopwatch.StartNew();
        value = 0;
        while (clock.Elapsed < Timeout)
        {
            if (!Read(address, out value)) return false;
            if (value != 0) return true;
            Thread.Sleep(1);
        }
        return false;
    }

    private bool Read(uint address, out uint value)
    {
        Span<ulong> result = stackalloc ulong[1];
        bool ok = _io.Execute("ioctl_read_smu_register", [address], result);
        value = (uint)result[0];
        return ok;
    }

    private bool Write(uint address, uint value) => _io.Execute("ioctl_write_smu_register", [address, value], []);

    public void Dispose() => _io.Dispose();
}
