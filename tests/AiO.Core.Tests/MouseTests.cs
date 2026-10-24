using System.Text.Json.Nodes;
using AiO.Core.Mouse;

namespace AiO.Core.Tests;

public class AccelCurveTests
{
    private static double S(AccelParameters p, double x) => AccelCurve.Sensitivity(p, x);

    [Fact]
    public void No_accel_is_flat()
    {
        Assert.Equal(1, S(new AccelParameters(), 50));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Linear_rises_and_respects_output_cap(bool gain)
    {
        var p = new AccelParameters { Mode = AccelMode.Linear, Gain = gain, Acceleration = 0.05, CapY = 2, CapMode = CapMode.Output };
        Assert.Equal(1, S(p, 0.01), 2);
        Assert.True(S(p, 10) > S(p, 5));
        Assert.True(S(p, 1000) <= 2.0001);
        Assert.True(S(p, 1000) > 1.9);
    }

    [Fact]
    public void Legacy_classic_matches_formula()
    {
        // Velocity mode without cap: 1 + (a·x)^(e-1)
        var p = new AccelParameters { Mode = AccelMode.Classic, Gain = false, Acceleration = 0.1, ExponentClassic = 2, CapY = 0 };
        Assert.Equal(1 + 0.1 * 10, S(p, 10), 6);
    }

    [Fact]
    public void Input_offset_keeps_sensitivity_at_one()
    {
        var p = new AccelParameters { Mode = AccelMode.Classic, InputOffset = 5, Acceleration = 0.1 };
        Assert.Equal(1, S(p, 4));
        Assert.True(S(p, 10) > 1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Natural_approaches_limit(bool gain)
    {
        var p = new AccelParameters { Mode = AccelMode.Natural, Gain = gain, Limit = 1.8, DecayRate = 0.1 };
        Assert.True(S(p, 1) < 1.2);
        Assert.InRange(S(p, 2000), 1.75, 1.8001);
    }

    [Fact]
    public void Sharp_jump_steps_at_threshold()
    {
        var p = new AccelParameters { Mode = AccelMode.Jump, Gain = false, CapX = 10, CapY = 2, Smooth = 0 };
        Assert.Equal(1, S(p, 9.9));
        Assert.Equal(2, S(p, 10.1));
    }

    [Fact]
    public void Smooth_gain_jump_is_monotonic()
    {
        var p = new AccelParameters { Mode = AccelMode.Jump, CapX = 15, CapY = 1.5, Smooth = 0.5 };
        double previous = 0;
        foreach (var (_, s) in AccelCurve.Sample(p, 100))
        {
            Assert.True(s >= previous - 1e-9);
            previous = s;
        }
        Assert.InRange(S(p, 1000), 1.4, 1.5);
    }

    [Fact]
    public void Power_grows_with_speed()
    {
        var p = new AccelParameters { Mode = AccelMode.Power, Scale = 1, ExponentPower = 0.2, CapY = 0 };
        Assert.True(S(p, 20) > S(p, 2));
    }

    [Fact]
    public void Synchronous_is_one_at_sync_speed_and_bounded()
    {
        var p = new AccelParameters { Mode = AccelMode.Synchronous, Gain = false, SyncSpeed = 5, Motivity = 1.5, Gamma = 1, Smooth = 0.5 };
        Assert.Equal(1, S(p, 5), 6);
        Assert.InRange(S(p, 0.01), 1 / 1.5 - 1e-6, 1);
        Assert.InRange(S(p, 1000), 1, 1.5 + 1e-6);
        var gain = p with { Gain = true };
        Assert.InRange(S(gain, 1000), 1, 1.5 + 1e-6);
    }

    [Fact]
    public void Curves_never_return_nan()
    {
        foreach (var mode in Enum.GetValues<AccelMode>())
            foreach (bool gain in new[] { true, false })
                foreach (var cap in Enum.GetValues<CapMode>())
                    foreach (var (_, s) in AccelCurve.Sample(new AccelParameters { Mode = mode, Gain = gain, CapMode = cap }, 200, 50))
                        Assert.True(double.IsFinite(s), $"{mode} gain={gain} cap={cap}");
    }
}

public class RawAccelSettingsTests
{
    [Fact]
    public void Default_file_round_trips()
    {
        var profile = new RawAccelProfile
        {
            Accel = new AccelParameters { Mode = AccelMode.Natural, Limit = 2, DecayRate = 0.2, Gain = false },
            OutputDpi = 1500,
            YxRatio = 1.1,
            RotationDegrees = 3,
        };
        string json = RawAccelSettingsFile.WriteFirstProfile(null, profile);
        var read = RawAccelSettingsFile.ReadFirstProfile(json);

        Assert.Equal(profile.Accel, read.Accel);
        Assert.Equal(1500, read.OutputDpi);
        Assert.Equal(1.1, read.YxRatio);
        Assert.Equal(3, read.RotationDegrees);
        Assert.Equal(1.5, read.SensitivityMultiplier);
    }

    [Fact]
    public void Default_file_has_rawaccel_shape()
    {
        var root = JsonNode.Parse(RawAccelSettingsFile.WriteFirstProfile(null, new RawAccelProfile()))!;
        Assert.Equal("1.7.0", (string?)root["version"]);
        var args = root["profiles"]![0]!["Whole or horizontal accel parameters"]!;
        Assert.Equal("noaccel", (string?)args["mode"]);
        Assert.Equal("output", (string?)args["Cap mode"]);
        Assert.Equal(15, (double)args["Cap / Jump"]!["x"]!);
        Assert.NotNull(root["profiles"]![0]!["Vertical accel parameters"]);
    }

    [Fact]
    public void Linear_is_written_as_classic_exponent_two()
    {
        var root = JsonNode.Parse(RawAccelSettingsFile.WriteFirstProfile(null,
            new RawAccelProfile { Accel = new AccelParameters { Mode = AccelMode.Linear, ExponentClassic = 3 } }))!;
        var args = root["profiles"]![0]!["Whole or horizontal accel parameters"]!;
        Assert.Equal("classic", (string?)args["mode"]);
        Assert.Equal(2, (double)args["exponentClassic"]!);
    }

    [Fact]
    public void Editing_preserves_unknown_fields_and_other_profiles()
    {
        string original = """
        {
          "version": "1.6.1",
          "customField": 42,
          "profiles": [
            { "name": "main", "Output DPI": 800, "Whole or horizontal accel parameters": { "mode": "lut", "data": [1, 2] } },
            { "name": "second" }
          ],
          "devices": []
        }
        """;
        string edited = RawAccelSettingsFile.WriteFirstProfile(original,
            new RawAccelProfile { Accel = new AccelParameters { Mode = AccelMode.Jump }, OutputDpi = 1200 });
        var root = JsonNode.Parse(edited)!;
        Assert.Equal(42, (int)root["customField"]!);
        Assert.Equal("1.6.1", (string?)root["version"]);
        Assert.Equal("second", (string?)root["profiles"]![1]!["name"]);
        Assert.Equal(1200, (double)root["profiles"]![0]!["Output DPI"]!);
        Assert.Equal("jump", (string?)root["profiles"]![0]!["Whole or horizontal accel parameters"]!["mode"]);
    }

    [Fact]
    public void Lut_profiles_are_read_but_not_overwritten()
    {
        string original = """{ "profiles": [ { "Whole or horizontal accel parameters": { "mode": "lut", "data": [1, 2] } } ] }""";
        var profile = RawAccelSettingsFile.ReadFirstProfile(original);
        Assert.Equal(AccelMode.Lut, profile.Accel.Mode);
        var edited = JsonNode.Parse(RawAccelSettingsFile.WriteFirstProfile(original, profile))!;
        Assert.Equal("lut", (string?)edited["profiles"]![0]!["Whole or horizontal accel parameters"]!["mode"]);
    }
}

public class MouseSettingsTests
{
    [Fact]
    public void Acceleration_profile_persists_in_aio_settings()
    {
        string path = Path.Combine(Path.GetTempPath(), $"aio-{Guid.NewGuid():N}.json");
        try
        {
            var store = new AiO.Core.Settings.SettingsStore(path);
            store.Current.Mouse.Profile = new RawAccelProfile
            {
                Accel = new AccelParameters { Mode = AccelMode.Natural, Limit = 1.7 },
                OutputDpi = 1250,
            };
            store.Save();
            var loaded = new AiO.Core.Settings.SettingsStore(path).Load().Mouse.Profile;
            Assert.NotNull(loaded);
            Assert.Equal(AccelMode.Natural, loaded.Accel.Mode);
            Assert.Equal(1.7, loaded.Accel.Limit);
            Assert.Equal(1.25, loaded.SensitivityMultiplier);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
