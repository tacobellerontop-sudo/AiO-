using AiO.Core.Asus;
using AiO.Core.Display;
using AiO.Core.Settings;

namespace AiO.Core.Tests;

public class AutomationTests
{
    private static readonly DisplayMode[] PanelModes =
    [
        new(2560, 1600, 240, 32), new(2560, 1600, 120, 32), new(2560, 1600, 60, 32), new(2560, 1600, 48, 32),
        new(1920, 1200, 240, 32), new(1920, 1200, 60, 32),
    ];

    [Fact]
    public void Highest_keeps_the_resolution()
    {
        var pick = DisplayModeMath.PickRefresh(PanelModes, new(2560, 1600, 60, 32), RefreshChoice.Highest);
        Assert.Equal(new DisplayMode(2560, 1600, 240, 32), pick);
    }

    [Fact]
    public void Lowest_prefers_60_over_lower_rates()
    {
        var pick = DisplayModeMath.PickRefresh(PanelModes, new(2560, 1600, 240, 32), RefreshChoice.Lowest);
        Assert.Equal(60, pick?.RefreshRate);
    }

    [Fact]
    public void Nothing_to_do_when_already_there()
    {
        Assert.Null(DisplayModeMath.PickRefresh(PanelModes, new(2560, 1600, 240, 32), RefreshChoice.Highest));
        Assert.Null(DisplayModeMath.PickRefresh(PanelModes, new(1280, 800, 60, 32), RefreshChoice.Highest));
    }

    [Theory]
    [InlineData(0x80000000u, true)]
    [InlineData(11u, true)]  // embedded DisplayPort
    [InlineData(10u, false)] // external DisplayPort
    [InlineData(5u, false)]  // HDMI
    public void Built_in_panels_are_recognised(uint technology, bool expected) =>
        Assert.Equal(expected, DisplayModeMath.IsInternalTechnology(technology));

    [Fact]
    public void Power_profiles_survive_a_save()
    {
        var path = Path.Combine(Path.GetTempPath(), $"aio-test-{Guid.NewGuid():N}.json");
        try
        {
            var store = new SettingsStore(path);
            store.Current.Asus.AutoSwitch = true;
            store.Current.Asus.OnBattery = new AsusPowerProfile { Mode = AsusPerformanceMode.Silent, Gpu = AsusGpuMode.Eco, Refresh = RefreshChoice.Lowest };
            store.Current.Asus.OnAc = new AsusPowerProfile { Mode = null, Refresh = RefreshChoice.Highest };
            store.Save();

            var loaded = new SettingsStore(path).Load();
            Assert.True(loaded.Asus.AutoSwitch);
            Assert.Equal(AsusGpuMode.Eco, loaded.Asus.OnBattery.Gpu);
            Assert.Equal(RefreshChoice.Lowest, loaded.Asus.OnBattery.Refresh);
            Assert.Null(loaded.Asus.OnAc.Mode);
            Assert.Null(loaded.Asus.OnAc.Gpu);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
