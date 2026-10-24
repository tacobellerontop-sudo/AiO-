namespace AiO.Core.Gpu;

public enum GpuColorProperty
{
    /// <summary>NVIDIA "Digital Vibrance" / AMD "Saturation".</summary>
    Saturation,
    Hue,
    Brightness,
    Contrast,
}

public sealed record GpuColorRange(int Current, int Default, int Min, int Max);

/// <summary>Colour controls the GPU driver applies after the gamma ramp, per display.</summary>
public interface IGpuColorControl : IDisposable
{
    string Vendor { get; }

    /// <summary>GDI device names (\\.\DISPLAYn) this driver controls.</summary>
    IReadOnlyList<string> Displays { get; }

    IReadOnlyList<GpuColorProperty> SupportedProperties { get; }

    GpuColorRange? Get(string deviceName, GpuColorProperty property);

    bool Set(string deviceName, GpuColorProperty property, int value);
}

public static class GpuColorControls
{
    /// <summary>Loads every available vendor backend (NVIDIA and/or AMD).</summary>
    public static IReadOnlyList<IGpuColorControl> LoadAll()
    {
        var list = new List<IGpuColorControl>();
        if (NvidiaColorControl.TryCreate() is { } nvidia) list.Add(nvidia);
        if (AmdColorControl.TryCreate() is { } amd) list.Add(amd);
        return list;
    }
}
