namespace AiO.Core.Asus;

/// <summary>
/// The ACPI camera switch: bit 0 is the state (1 = shutter closed / camera off), the higher bits say
/// how the model wants it driven. The layouts are firmware facts catalogued by G-Helper.
/// </summary>
public static class AsusCamera
{
    public readonly record struct Toggle(int Write, bool CameraOnAfter);

    /// <summary>What to write to toggle the camera, or null when software can't switch it on this model.</summary>
    public static Toggle? PlanToggle(int value)
    {
        int state = value & 1;
        int kind = value & ~1;
        return kind switch
        {
            0x000000 => new Toggle(state ^ 1, CameraOnAfter: state == 1),
            0x100000 => new Toggle(4 | state, CameraOnAfter: state == 0),
            _ => null,
        };
    }
}
