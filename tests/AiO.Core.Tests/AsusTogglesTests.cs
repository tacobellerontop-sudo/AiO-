using AiO.Core.Asus;
using AiO.Core.Keyboard;

namespace AiO.Core.Tests;

public class AsusTogglesTests
{
    [Theory]
    [InlineData(0x000000, 1, false)] // camera on → write 1 (shutter), now off
    [InlineData(0x000001, 0, true)]
    [InlineData(0x100000, 4, true)]
    [InlineData(0x100001, 5, false)]
    public void Camera_toggle_plans(int value, int write, bool onAfter)
    {
        var plan = AsusCamera.PlanToggle(value);
        Assert.NotNull(plan);
        Assert.Equal(write, plan.Value.Write);
        Assert.Equal(onAfter, plan.Value.CameraOnAfter);
    }

    [Fact]
    public void Camera_layouts_software_cant_switch_are_refused()
    {
        Assert.Null(AsusCamera.PlanToggle(0x40000));
        Assert.Null(AsusCamera.PlanToggle(0xC0001));
    }

    [Fact]
    public void Fn_lock_hid_message()
    {
        Assert.Equal(new byte[] { 0x5A, 0xD0, 0x4E, 0x00 }, AsusAura.BuildFnLockMessage(true));
        Assert.Equal(new byte[] { 0x5A, 0xD0, 0x4E, 0x01 }, AsusAura.BuildFnLockMessage(false));
    }

    [Theory]
    [InlineData(78, AsusHotkey.FnEsc, KeyActionType.ToggleFnLock)]
    [InlineData(107, AsusHotkey.FnF10, KeyActionType.ToggleTouchpad)]
    [InlineData(133, AsusHotkey.Camera, KeyActionType.ToggleCamera)]
    public void Toggle_keys_default_to_their_toggle_without_the_asus_service(int code, AsusHotkey key, KeyActionType action)
    {
        Assert.Equal(key, AsusHotkeyCodes.FromEventCode(code));
        Assert.Equal(action, AsusHotkeyCodes.DefaultAction(key, asusServiceRunning: false));
        Assert.Null(AsusHotkeyCodes.DefaultAction(key, asusServiceRunning: true));
    }
}
