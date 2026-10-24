using AiO.Core.Color;
using AiO.Core.Gpu;
using AiO.Core.Settings;

namespace AiO.Core.Tests;

public class ColorPresetTests
{
    private static readonly GpuColorRange Nvidia = new(Current: 0, Default: 0, Min: 0, Max: 63);
    private static readonly GpuColorRange Amd = new(Current: 100, Default: 100, Min: 0, Max: 200);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.5, 32)]
    [InlineData(1, 63)]
    [InlineData(-1, 0)] // NVIDIA can't go below its default
    [InlineData(2, 63)]
    public void Relative_saturation_maps_onto_nvidia_range(double relative, int expected)
    {
        Assert.Equal(expected, ColorPresetMath.FromRelative(Nvidia, relative));
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(0.5, 150)]
    [InlineData(-0.3, 70)]
    [InlineData(-1, 0)]
    public void Relative_saturation_maps_onto_amd_range(double relative, int expected)
    {
        Assert.Equal(expected, ColorPresetMath.FromRelative(Amd, relative));
    }

    [Theory]
    [InlineData(150)]
    [InlineData(40)]
    [InlineData(100)]
    public void Relative_round_trips(int value)
    {
        Assert.Equal(value, ColorPresetMath.FromRelative(Amd, ColorPresetMath.ToRelative(Amd, value)));
    }

    [Fact]
    public void Built_in_presets_have_unique_names_and_valid_ramps()
    {
        Assert.Equal(ColorPreset.BuiltIn.Count, ColorPreset.BuiltIn.Select(p => p.Name).Distinct().Count());
        foreach (var preset in ColorPreset.BuiltIn)
        {
            Assert.True(preset.IsBuiltIn);
            var ramp = GammaRamp.Build(preset.Gamma);
            for (int c = 0; c < 3; c++)
                for (int i = 1; i < 256; i++)
                    Assert.True(ramp[c * 256 + i] >= ramp[c * 256 + i - 1], preset.Name);
        }
    }

    [Fact]
    public void Night_swatch_is_warm()
    {
        var night = ColorPreset.BuiltIn.Single(p => p.Name == "Night");
        var (r, _, b) = ColorPresetMath.Swatch(night);
        Assert.True(r > b);
    }

    [Fact]
    public void User_presets_persist()
    {
        string path = Path.Combine(Path.GetTempPath(), $"aio-{Guid.NewGuid():N}.json");
        try
        {
            var store = new SettingsStore(path);
            store.Current.ColorPresets.Add(new ColorPreset
            {
                Name = "Mine",
                Gamma = new ColorAdjustment { Temperature = 5600 },
                Saturation = 0.4,
                MonitorBrightness = 0.7,
            });
            store.Save();
            var loaded = new SettingsStore(path).Load().ColorPresets.Single();
            Assert.Equal("Mine", loaded.Name);
            Assert.Equal(5600, loaded.Gamma.Temperature);
            Assert.Equal(0.4, loaded.Saturation);
            Assert.Equal(0.7, loaded.MonitorBrightness);
            Assert.Null(loaded.MonitorContrast);
            Assert.False(loaded.IsBuiltIn);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
