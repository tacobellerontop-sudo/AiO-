using AiO.Core.Asus;
using AiO.Core.Color;
using AiO.Core.Display;
using AiO.Core.Gpu;
using AiO.Core.Settings;

namespace AiO.Core.Tests;

public class DisplayAndColorTests
{
    [Fact]
    public void Neutral_ramp_is_identity()
    {
        var ramp = GammaRamp.Build(ColorAdjustment.Neutral);
        Assert.Equal(768, ramp.Length);
        for (int i = 0; i < 256; i++)
        {
            Assert.Equal(i * 257, ramp[i]);
            Assert.Equal(i * 257, ramp[512 + i]);
        }
    }

    [Fact]
    public void Warm_temperature_reduces_blue_most()
    {
        var ramp = GammaRamp.Build(ColorAdjustment.Neutral with { Temperature = 3400 });
        Assert.Equal(65535, ramp[255]);          // red full
        Assert.True(ramp[512 + 255] < ramp[256 + 255]); // blue < green
        Assert.True(ramp[256 + 255] < 65535);
    }

    [Fact]
    public void Ramp_stays_monotonic_with_extreme_values()
    {
        var ramp = GammaRamp.Build(new ColorAdjustment { Gamma = 2.5, Brightness = 0.4, Contrast = 1.5, Temperature = 1900 });
        for (int c = 0; c < 3; c++)
            for (int i = 1; i < 256; i++)
                Assert.True(ramp[c * 256 + i] >= ramp[c * 256 + i - 1]);
    }

    [Fact]
    public void Kelvin_6500_is_white()
    {
        var (r, g, b) = GammaRamp.KelvinToRgb(6500);
        Assert.Equal(1.0, r, 3);
        Assert.Equal(1.0, g, 3);
        Assert.Equal(1.0, b, 3);
    }

    [Fact]
    public void Modes_are_deduped_and_sorted()
    {
        var modes = DisplayModeMath.Normalize(
        [
            new(1920, 1080, 60, 32),
            new(2560, 1440, 144, 32),
            new(1920, 1080, 60, 16),   // 16-bit dropped
            new(2560, 1440, 60, 32),
            new(1920, 1080, 60, 32),   // duplicate
            new(2560, 1440, 1, 32),    // "default refresh" placeholder dropped
        ]);
        Assert.Equal(
        [
            new DisplayMode(2560, 1440, 144, 32),
            new DisplayMode(2560, 1440, 60, 32),
            new DisplayMode(1920, 1080, 60, 32),
        ], modes);
    }

    [Theory]
    [InlineData(DisplayOrientation.Landscape, DisplayOrientation.Portrait, 1080, 1920)]
    [InlineData(DisplayOrientation.Landscape, DisplayOrientation.LandscapeFlipped, 1920, 1080)]
    [InlineData(DisplayOrientation.PortraitFlipped, DisplayOrientation.Landscape, 1080, 1920)]
    public void Orientation_swaps_dimensions(DisplayOrientation from, DisplayOrientation to, int w, int h)
    {
        Assert.Equal((w, h), DisplayModeMath.SizeForOrientation(1920, 1080, from, to));
    }

    [Fact]
    public void Scale_decoding_uses_recommended_offset()
    {
        // Recommended 150 %, currently 125 %, up to three steps above recommended (225 %).
        var scale = DisplayModeMath.DecodeScale(-2, -1, 3);
        Assert.NotNull(scale);
        Assert.Equal(125, scale.CurrentPercent);
        Assert.Equal(150, scale.RecommendedPercent);
        Assert.Equal([100, 125, 150, 175, 200, 225], scale.AvailablePercents);
        Assert.Equal(2, DisplayModeMath.EncodeScale(200, 150));
        Assert.Null(DisplayModeMath.EncodeScale(110, 150));
    }

    [Fact]
    public void Settings_round_trip()
    {
        string path = Path.Combine(Path.GetTempPath(), $"aio-{Guid.NewGuid():N}.json");
        try
        {
            var store = new SettingsStore(path);
            store.Current.Displays[@"\\.\DISPLAY1"] = new DisplayColorSettings
            {
                Gamma = new ColorAdjustment { Temperature = 5000, Gamma = 1.2 },
                Gpu = { [GpuColorProperty.Saturation] = 30 },
            };
            store.Current.Asus.PerformanceMode = AsusPerformanceMode.Turbo;
            store.Current.Asus.Profile(AsusPerformanceMode.Turbo).CpuCurve = FanCurveSetting.From(FanCurve.Default(AsusPerformanceMode.Turbo));
            store.Save();

            var loaded = new SettingsStore(path).Load();
            var display = loaded.Displays[@"\\.\display1"]; // case-insensitive
            Assert.Equal(5000, display.Gamma.Temperature);
            Assert.Equal(30, display.Gpu[GpuColorProperty.Saturation]);
            Assert.Equal(AsusPerformanceMode.Turbo, loaded.Asus.PerformanceMode);
            Assert.True(loaded.Asus.Profile(AsusPerformanceMode.Turbo).CpuCurve!.ToCurve().IsValid());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Corrupt_settings_fall_back_to_defaults()
    {
        string path = Path.Combine(Path.GetTempPath(), $"aio-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{ not json");
            var settings = new SettingsStore(path).Load();
            Assert.True(settings.General.ApplyOnStartup);
            Assert.True(File.Exists(path + ".bak"));
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".bak");
        }
    }
}
