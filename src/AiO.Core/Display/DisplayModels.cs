namespace AiO.Core.Display;

public sealed record DisplayMode(int Width, int Height, int RefreshRate, int BitsPerPixel)
{
    public string Resolution => $"{Width} × {Height}";
    public override string ToString() => $"{Width} × {Height} @ {RefreshRate} Hz";
}

public enum DisplayOrientation
{
    Landscape = 0,
    Portrait = 1,
    LandscapeFlipped = 2,
    PortraitFlipped = 3,
}

public sealed record HdrState(bool Supported, bool Enabled, int BitsPerChannel);

public sealed record DisplayScale(int CurrentPercent, int RecommendedPercent, IReadOnlyList<int> AvailablePercents);

public sealed class DisplayInfo
{
    /// <summary>GDI device name, e.g. \\.\DISPLAY1. This is the key AiO uses to identify a display.</summary>
    public required string DeviceName { get; init; }
    public required string FriendlyName { get; init; }
    public required string AdapterName { get; init; }
    public bool IsPrimary { get; init; }
    public int PositionX { get; init; }
    public int PositionY { get; init; }
    public required DisplayMode CurrentMode { get; init; }
    public DisplayOrientation Orientation { get; init; }
    public required IReadOnlyList<DisplayMode> Modes { get; init; }
    public HdrState? Hdr { get; init; }
    public DisplayScale? Scale { get; init; }

    public string ShortName => DeviceName.Replace(@"\\.\", "");
    public override string ToString() => $"{FriendlyName} ({ShortName})";
}

public static class DisplayModeMath
{
    /// <summary>Dedupes and sorts modes: biggest resolution first, then highest refresh rate.</summary>
    public static IReadOnlyList<DisplayMode> Normalize(IEnumerable<DisplayMode> modes) =>
        modes.Where(m => m.BitsPerPixel >= 24 && m.RefreshRate > 1)
             .GroupBy(m => (m.Width, m.Height, m.RefreshRate))
             .Select(g => g.OrderByDescending(m => m.BitsPerPixel).First())
             .OrderByDescending(m => m.Width * m.Height)
             .ThenByDescending(m => m.Width)
             .ThenByDescending(m => m.RefreshRate)
             .ToList();

    /// <summary>Width and height to request so the desktop keeps its shape after rotating.</summary>
    public static (int Width, int Height) SizeForOrientation(int width, int height, DisplayOrientation from, DisplayOrientation to)
    {
        bool fromPortrait = from is DisplayOrientation.Portrait or DisplayOrientation.PortraitFlipped;
        bool toPortrait = to is DisplayOrientation.Portrait or DisplayOrientation.PortraitFlipped;
        return fromPortrait == toPortrait ? (width, height) : (height, width);
    }

    /// <summary>The scale steps Windows offers, in order.</summary>
    public static readonly int[] ScaleSteps = [100, 125, 150, 175, 200, 225, 250, 300, 350, 400, 450, 500];

    /// <summary>
    /// Windows reports scale relative to the display's recommended value: min/max/current offsets
    /// into <see cref="ScaleSteps"/>, with the recommended step at index |min|.
    /// </summary>
    public static DisplayScale? DecodeScale(int minRel, int curRel, int maxRel)
    {
        int recommended = Math.Abs(minRel);
        int current = recommended + curRel;
        int last = recommended + maxRel;
        if (current < 0 || last >= ScaleSteps.Length || current > last) return null;
        return new DisplayScale(ScaleSteps[current], ScaleSteps[recommended], ScaleSteps[..(last + 1)]);
    }

    public static int? EncodeScale(int percent, int recommendedPercent)
    {
        int target = Array.IndexOf(ScaleSteps, percent);
        int recommended = Array.IndexOf(ScaleSteps, recommendedPercent);
        return target < 0 || recommended < 0 ? null : target - recommended;
    }
}
