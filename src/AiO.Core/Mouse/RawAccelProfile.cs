using System.Text.Json;
using System.Text.Json.Nodes;

namespace AiO.Core.Mouse;

/// <summary>The parts of a RawAccel profile AiO edits.</summary>
public sealed record RawAccelProfile
{
    public AccelParameters Accel { get; init; } = new();
    /// <summary>RawAccel's "Output DPI"; 1000 = 1× sensitivity.</summary>
    public double OutputDpi { get; init; } = 1000;
    /// <summary>Vertical sensitivity multiplier.</summary>
    public double YxRatio { get; init; } = 1;
    public double RotationDegrees { get; init; }
    public double AngleSnapDegrees { get; init; }

    public double SensitivityMultiplier => OutputDpi / 1000;
}

/// <summary>
/// Reads and writes RawAccel's settings.json (format of RawAccel 1.6/1.7, wrapper/wrapper.cpp).
/// Editing works on the JSON tree, so fields AiO doesn't know about are preserved.
/// </summary>
public static class RawAccelSettingsFile
{
    public const string Version = "1.7.0";

    private const string ProfilesKey = "profiles";
    private const string ArgsXKey = "Whole or horizontal accel parameters";
    private const string ArgsYKey = "Vertical accel parameters";
    private const string OutputDpiKey = "Output DPI";
    private const string YxRatioKey = "Y/X output DPI ratio (vertical sens multiplier)";
    private const string RotationKey = "Degrees of rotation";
    private const string SnapKey = "Degrees of angle snapping";
    private const string CapKey = "Cap / Jump";
    private const string CapModeKey = "Cap mode";
    private const string GainKey = "Gain / Velocity";

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>Reads the first profile. Unknown modes come back as <see cref="AccelMode.Lut"/>.</summary>
    public static RawAccelProfile ReadFirstProfile(string json)
    {
        var root = JsonNode.Parse(json)?.AsObject() ?? throw new JsonException("Empty settings file");
        var profile = root[ProfilesKey]?.AsArray().FirstOrDefault()?.AsObject() ?? throw new JsonException("No profiles in settings file");
        var args = profile[ArgsXKey]?.AsObject() ?? throw new JsonException($"Missing \"{ArgsXKey}\"");

        return new RawAccelProfile
        {
            Accel = ReadArgs(args),
            OutputDpi = Num(profile, OutputDpiKey, 1000),
            YxRatio = Num(profile, YxRatioKey, 1),
            RotationDegrees = Num(profile, RotationKey, 0),
            AngleSnapDegrees = Num(profile, SnapKey, 0),
        };
    }

    /// <summary>
    /// Returns <paramref name="json"/> with the first profile updated (both axes get the same curve),
    /// or a fresh default file when <paramref name="json"/> is null.
    /// </summary>
    public static string WriteFirstProfile(string? json, RawAccelProfile value)
    {
        var root = json is null ? CreateDefault() : JsonNode.Parse(json)?.AsObject() ?? CreateDefault();
        var profiles = root[ProfilesKey] as JsonArray;
        if (profiles is null || profiles.Count == 0)
        {
            profiles = [CreateDefaultProfile()];
            root[ProfilesKey] = profiles;
        }
        var profile = profiles[0]!.AsObject();

        profile[OutputDpiKey] = value.OutputDpi;
        profile[YxRatioKey] = value.YxRatio;
        profile[RotationKey] = value.RotationDegrees;
        profile[SnapKey] = value.AngleSnapDegrees;

        foreach (var key in new[] { ArgsXKey, ArgsYKey })
        {
            if (profile[key] is not JsonObject args)
            {
                args = CreateDefaultArgs();
                profile[key] = args;
            }
            WriteArgs(args, value.Accel);
        }
        return root.ToJsonString(Indented);
    }

    private static AccelParameters ReadArgs(JsonObject a)
    {
        var cap = a[CapKey] as JsonObject;
        bool isClassic = Str(a, "mode") == "classic";
        double exponentClassic = Num(a, "exponentClassic", 2);
        return new AccelParameters
        {
            Mode = Str(a, "mode") switch
            {
                "noaccel" => AccelMode.NoAccel,
                "classic" when exponentClassic == 2 => AccelMode.Linear,
                "classic" => AccelMode.Classic,
                "natural" => AccelMode.Natural,
                "jump" => AccelMode.Jump,
                "power" => AccelMode.Power,
                "synchronous" => AccelMode.Synchronous,
                _ => AccelMode.Lut,
            },
            Gain = a[GainKey]?.GetValue<bool>() ?? true,
            InputOffset = Num(a, "inputOffset", 0),
            OutputOffset = Num(a, "outputOffset", 0),
            Acceleration = Num(a, "acceleration", 0.005),
            DecayRate = Num(a, "decayRate", 0.1),
            Gamma = Num(a, "gamma", 1),
            Motivity = Num(a, "motivity", 1.5),
            ExponentClassic = isClassic ? exponentClassic : 2,
            Scale = Num(a, "scale", 1),
            ExponentPower = Num(a, "exponentPower", 0.05),
            Limit = Num(a, "limit", 1.5),
            SyncSpeed = Num(a, "syncSpeed", 5),
            Smooth = Num(a, "smooth", 0.5),
            CapX = cap is null ? 15 : Num(cap, "x", 15),
            CapY = cap is null ? 1.5 : Num(cap, "y", 1.5),
            CapMode = Str(a, CapModeKey) switch
            {
                "in_out" => CapMode.InOut,
                "input" => CapMode.Input,
                _ => CapMode.Output,
            },
        };
    }

    private static void WriteArgs(JsonObject a, AccelParameters p)
    {
        if (p.Mode == AccelMode.Lut) return; // leave look-up tables untouched
        a["mode"] = p.Mode switch
        {
            AccelMode.Classic or AccelMode.Linear => "classic",
            AccelMode.Natural => "natural",
            AccelMode.Jump => "jump",
            AccelMode.Power => "power",
            AccelMode.Synchronous => "synchronous",
            _ => "noaccel",
        };
        a[GainKey] = p.Gain;
        a["inputOffset"] = p.InputOffset;
        a["outputOffset"] = p.OutputOffset;
        a["acceleration"] = p.Acceleration;
        a["decayRate"] = p.DecayRate;
        a["gamma"] = p.Gamma;
        a["motivity"] = p.Motivity;
        a["exponentClassic"] = p.Mode == AccelMode.Linear ? 2 : p.ExponentClassic;
        a["scale"] = p.Scale;
        a["exponentPower"] = p.ExponentPower;
        a["limit"] = p.Limit;
        a["syncSpeed"] = p.SyncSpeed;
        a["smooth"] = p.Smooth;
        a[CapKey] = new JsonObject { ["x"] = p.CapX, ["y"] = p.CapY };
        a[CapModeKey] = p.CapMode switch
        {
            CapMode.InOut => "in_out",
            CapMode.Input => "input",
            _ => "output",
        };
        a["data"] ??= new JsonArray();
    }

    private static double Num(JsonObject o, string key, double fallback) =>
        o[key] is JsonValue v && v.TryGetValue(out double d) ? d : fallback;

    private static string Str(JsonObject o, string key) =>
        o[key] is JsonValue v && v.TryGetValue(out string? s) ? s : "";

    // ---- defaults (RawAccel's own) ----------------------------------------------

    public static JsonObject CreateDefault() => new()
    {
        ["### Accel modes ###"] = "classic | jump | natural | synchronous | power | lut | noaccel",
        ["### Cap modes ###"] = "in_out | input | output",
        ["version"] = Version,
        ["defaultDeviceConfig"] = new JsonObject
        {
            ["disable"] = false,
            ["Use constant time interval based on polling rate"] = false,
            ["DPI (normalizes input speed unit: counts/ms -> in/s)"] = 0,
            ["Polling rate Hz (keep at 0 for automatic adjustment)"] = 0,
        },
        [ProfilesKey] = new JsonArray(CreateDefaultProfile()),
        ["devices"] = new JsonArray(),
    };

    private static JsonObject CreateDefaultProfile() => new()
    {
        ["name"] = "default",
        ["Stretches domain for horizontal vs vertical inputs"] = new JsonObject { ["x"] = 1.0, ["y"] = 1.0 },
        ["Stretches accel range for horizontal vs vertical inputs"] = new JsonObject { ["x"] = 1.0, ["y"] = 1.0 },
        [ArgsXKey] = CreateDefaultArgs(),
        [ArgsYKey] = CreateDefaultArgs(),
        ["Input speed calculation parameters"] = new JsonObject
        {
            ["Whole/combined accel (set false for 'by component' mode)"] = true,
            ["lpNorm"] = 2.0,
            ["Time in ms after which an input is weighted at half its original value."] = 0.0,
            ["Time in ms after which scale is weighted at half its original value."] = 0.0,
            ["Time in ms after which an output is weighted at half its original value."] = 0.0,
        },
        [OutputDpiKey] = 1000.0,
        [YxRatioKey] = 1.0,
        ["L/R output DPI ratio (left sens multiplier)"] = 1.0,
        ["U/D output DPI ratio (up sens multiplier)"] = 1.0,
        [RotationKey] = 0.0,
        [SnapKey] = 0.0,
        ["Input Speed Cap"] = 0.0,
    };

    private static JsonObject CreateDefaultArgs()
    {
        var a = new JsonObject();
        WriteArgs(a, new AccelParameters());
        return a;
    }
}
