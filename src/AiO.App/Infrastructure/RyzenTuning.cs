using AiO.Core.Ryzen;
using AiO.Core.Settings;

namespace AiO.App.Infrastructure;

/// <summary>Applies the Danger zone CPU settings through PawnIO.</summary>
public static class RyzenTuning
{
    public const string PawnIoUrl = "https://pawnio.eu/";

    /// <summary>PawnIO's signed RyzenSMU module, shipped next to AiO.exe by the build.</summary>
    public static string ModulePath => Path.Combine(AppContext.BaseDirectory, "PawnIO", "RyzenSMU.bin");

    public static bool IsAmd => (Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "").Contains("AuthenticAMD", StringComparison.OrdinalIgnoreCase);

    /// <summary>Opens the SMU or says what's missing.</summary>
    public static RyzenSmu? Open(out string problem)
    {
        if (!IsAmd) { problem = "Undervolting here only works on AMD Ryzen CPUs."; return null; }
        if (!File.Exists(ModulePath)) { problem = "This AiO build doesn't include PawnIO's RyzenSMU module."; return null; }
        return RyzenSmu.Open(File.ReadAllBytes(ModulePath), out problem);
    }

    /// <summary>Applies the saved offset and temperature limit; returns a one-line result.</summary>
    public static string Apply(RyzenTuningSettings s)
    {
        using var smu = Open(out string problem);
        if (smu is null) return problem;
        if (smu.Family == RyzenFamily.Unsupported) return $"AiO doesn't know how to tune this CPU ({smu.CodeName}).";

        var parts = new List<string>();
        var co = smu.SetCurveOptimizer(s.CurveOffset);
        parts.Add($"Curve Optimizer {s.CurveOffset}: {Describe(co)}");
        if (s.TempLimit is int t)
            parts.Add($"temperature limit {t} °C: {Describe(smu.SetTempLimit(t))}");
        return string.Join(", ", parts) + ".";
    }

    private static string Describe(SmuResult r) => r switch
    {
        SmuResult.Ok => "applied",
        SmuResult.UnknownCommand => "not supported on this CPU",
        SmuResult.RejectedBusy => "SMU busy, try again",
        SmuResult.RejectedPrerequisite => "refused by the CPU",
        _ => $"failed ({r})",
    };

    /// <summary>
    /// Start-up apply with a crash guard: if the last automatic apply never got marked as stable, skip
    /// this one, because it may be what crashed the PC.
    /// </summary>
    public static void ApplyAtStartup(AppServices services)
    {
        var s = services.Settings.Current.Asus.Ryzen;
        if (!services.Settings.Current.Asus.DangerZoneUnlocked || !s.ApplyAutomatically) return;
        if (s.CurveOffset == 0 && s.TempLimit is null) return;

        if (s.AutoApplyPendingSince is not null)
        {
            s.ApplyAutomatically = false;
            s.AutoApplyPendingSince = null;
            services.SaveSettings();
            services.Report("Undervolt not applied: AiO didn't get to confirm the last one was stable (crash or forced shutdown?). Automatic apply is now off.");
            return;
        }

        s.AutoApplyPendingSince = DateTime.UtcNow;
        services.SaveSettings();
        services.Report(Apply(s));

        _ = Task.Delay(TimeSpan.FromMinutes(2)).ContinueWith(_ =>
        {
            s.AutoApplyPendingSince = null;
            services.SaveSettings();
        });
    }
}
