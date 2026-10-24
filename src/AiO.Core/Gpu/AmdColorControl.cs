using System.Runtime.InteropServices;

namespace AiO.Core.Gpu;

/// <summary>
/// Saturation, hue, brightness and contrast through AMD Display Library (atiadlxx.dll,
/// installed with the AMD driver). These are the same controls as Adrenalin → Display → Custom Color.
/// </summary>
public sealed class AmdColorControl : IGpuColorControl
{
    private const string Dll = "atiadlxx.dll";
    private const int MaxPath = 256;

    private const int ColorBrightness = 1 << 0;
    private const int ColorContrast = 1 << 1;
    private const int ColorSaturation = 1 << 2;
    private const int ColorHue = 1 << 3;

    private const int DisplayConnected = 1 << 0;
    private const int DisplayMapped = 1 << 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct AdapterInfo
    {
        public int Size;
        public int AdapterIndex;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MaxPath)] public string UDID;
        public int BusNumber;
        public int DeviceNumber;
        public int FunctionNumber;
        public int VendorID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MaxPath)] public string AdapterName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MaxPath)] public string DisplayName;
        public int Present;
        public int Exist;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MaxPath)] public string DriverPath;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MaxPath)] public string DriverPathExt;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MaxPath)] public string PNPString;
        public int OSDisplayIndex;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct DisplayInfo
    {
        public int DisplayLogicalIndex;
        public int DisplayPhysicalIndex;
        public int DisplayLogicalAdapterIndex;
        public int DisplayPhysicalAdapterIndex;
        public int DisplayControllerIndex;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MaxPath)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MaxPath)] public string DisplayManufacturerName;
        public int DisplayType;
        public int DisplayOutputType;
        public int DisplayConnector;
        public int DisplayInfoMask;
        public int DisplayInfoValue;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate IntPtr MallocCallback(int size);

    [DllImport(Dll)] private static extern int ADL2_Main_Control_Create(MallocCallback callback, int enumConnectedAdapters, out IntPtr context);
    [DllImport(Dll)] private static extern int ADL2_Main_Control_Destroy(IntPtr context);
    [DllImport(Dll)] private static extern int ADL2_Adapter_NumberOfAdapters_Get(IntPtr context, out int count);
    [DllImport(Dll)] private static extern int ADL2_Adapter_AdapterInfo_Get(IntPtr context, IntPtr info, int inputSize);
    [DllImport(Dll)] private static extern int ADL2_Display_DisplayInfo_Get(IntPtr context, int adapterIndex, out int count, out IntPtr info, int forceDetect);
    [DllImport(Dll)] private static extern int ADL2_Display_Color_Get(IntPtr context, int adapterIndex, int displayIndex, int colorType,
        out int current, out int defaultValue, out int min, out int max, out int step);
    [DllImport(Dll)] private static extern int ADL2_Display_Color_Set(IntPtr context, int adapterIndex, int displayIndex, int colorType, int value);

    // ADL allocates through this callback; keep the delegate alive for the lifetime of the context.
    private static readonly MallocCallback Malloc = size => Marshal.AllocCoTaskMem(size);

    private readonly IntPtr _context;
    private readonly Dictionary<string, (int Adapter, int Display)> _displays = new(StringComparer.OrdinalIgnoreCase);

    public string Vendor => "AMD";
    public IReadOnlyList<string> Displays => _displays.Keys.ToList();
    public IReadOnlyList<GpuColorProperty> SupportedProperties { get; } =
        [GpuColorProperty.Saturation, GpuColorProperty.Hue, GpuColorProperty.Brightness, GpuColorProperty.Contrast];

    private AmdColorControl(IntPtr context)
    {
        _context = context;
        if (ADL2_Adapter_NumberOfAdapters_Get(context, out int count) != 0 || count <= 0) return;

        int size = Marshal.SizeOf<AdapterInfo>();
        IntPtr buffer = Marshal.AllocHGlobal(size * count);
        try
        {
            if (ADL2_Adapter_AdapterInfo_Get(context, buffer, size * count) != 0) return;
            for (int i = 0; i < count; i++)
            {
                var adapter = Marshal.PtrToStructure<AdapterInfo>(buffer + i * size);
                if (adapter.Present == 0 || string.IsNullOrEmpty(adapter.DisplayName) || _displays.ContainsKey(adapter.DisplayName))
                    continue;
                if (FindDisplay(adapter.AdapterIndex) is int display)
                    _displays[adapter.DisplayName] = (adapter.AdapterIndex, display);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>The logical display index driven by this adapter index, if a monitor is connected and mapped.</summary>
    private int? FindDisplay(int adapterIndex)
    {
        if (ADL2_Display_DisplayInfo_Get(_context, adapterIndex, out int count, out IntPtr infos, 0) != 0 || infos == IntPtr.Zero)
            return null;
        try
        {
            int size = Marshal.SizeOf<DisplayInfo>();
            for (int i = 0; i < count; i++)
            {
                var info = Marshal.PtrToStructure<DisplayInfo>(infos + i * size);
                bool live = (info.DisplayInfoValue & (DisplayConnected | DisplayMapped)) == (DisplayConnected | DisplayMapped);
                if (live && info.DisplayLogicalAdapterIndex == adapterIndex) return info.DisplayLogicalIndex;
            }
            return null;
        }
        finally
        {
            Marshal.FreeCoTaskMem(infos);
        }
    }

    public static AmdColorControl? TryCreate()
    {
        try
        {
            if (ADL2_Main_Control_Create(Malloc, 1, out IntPtr context) != 0) return null;
            var control = new AmdColorControl(context);
            if (control._displays.Count > 0) return control;
            control.Dispose();
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
        return null;
    }

    private static int ColorType(GpuColorProperty p) => p switch
    {
        GpuColorProperty.Saturation => ColorSaturation,
        GpuColorProperty.Hue => ColorHue,
        GpuColorProperty.Brightness => ColorBrightness,
        _ => ColorContrast,
    };

    public GpuColorRange? Get(string deviceName, GpuColorProperty property)
    {
        if (!_displays.TryGetValue(deviceName, out var d)) return null;
        return ADL2_Display_Color_Get(_context, d.Adapter, d.Display, ColorType(property),
            out int current, out int def, out int min, out int max, out _) == 0
            ? new GpuColorRange(current, def, min, max)
            : null;
    }

    public bool Set(string deviceName, GpuColorProperty property, int value) =>
        _displays.TryGetValue(deviceName, out var d) &&
        ADL2_Display_Color_Set(_context, d.Adapter, d.Display, ColorType(property), value) == 0;

    public void Dispose() => ADL2_Main_Control_Destroy(_context);
}
