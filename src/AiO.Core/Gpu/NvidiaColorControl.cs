using System.Runtime.InteropServices;
using System.Text;

namespace AiO.Core.Gpu;

/// <summary>
/// Digital vibrance and hue through NVAPI (nvapi64.dll, installed with the NVIDIA driver).
/// NVAPI exports only nvapi_QueryInterface; functions are looked up by id.
/// </summary>
public sealed class NvidiaColorControl : IGpuColorControl
{
    private const uint IdInitialize = 0x0150E828;
    private const uint IdUnload = 0xD22BDD7E;
    private const uint IdEnumNvidiaDisplayHandle = 0x9ABDD40D;
    private const uint IdGetAssociatedNvidiaDisplayName = 0x22A78B05;
    private const uint IdGetDvcInfo = 0x4085DE45;
    private const uint IdSetDvcLevel = 0x172409B4;
    private const uint IdGetHueInfo = 0x95B64341;
    private const uint IdSetHueAngle = 0xF5A0F22C;

    [StructLayout(LayoutKind.Sequential)]
    private struct DvcInfo
    {
        public uint Version;
        public int CurrentLevel;
        public int MinLevel;
        public int MaxLevel;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HueInfo
    {
        public uint Version;
        public int CurrentAngle;
        public int DefaultAngle;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int InitializeFn();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int UnloadFn();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int EnumDisplayFn(uint index, out IntPtr handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetDisplayNameFn(IntPtr handle, [Out] byte[] name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetDvcInfoFn(IntPtr handle, uint outputId, ref DvcInfo info);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetDvcLevelFn(IntPtr handle, uint outputId, int level);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetHueInfoFn(IntPtr handle, uint outputId, ref HueInfo info);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetHueAngleFn(IntPtr handle, uint outputId, int angle);

    [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr QueryInterface(uint id);

    private readonly UnloadFn? _unload;
    private readonly GetDvcInfoFn? _getDvc;
    private readonly SetDvcLevelFn? _setDvc;
    private readonly GetHueInfoFn? _getHue;
    private readonly SetHueAngleFn? _setHue;
    private readonly Dictionary<string, IntPtr> _handles = new(StringComparer.OrdinalIgnoreCase);

    public string Vendor => "NVIDIA";
    public IReadOnlyList<string> Displays => _handles.Keys.ToList();
    public IReadOnlyList<GpuColorProperty> SupportedProperties { get; }

    private NvidiaColorControl()
    {
        _unload = Get<UnloadFn>(IdUnload);
        _getDvc = Get<GetDvcInfoFn>(IdGetDvcInfo);
        _setDvc = Get<SetDvcLevelFn>(IdSetDvcLevel);
        _getHue = Get<GetHueInfoFn>(IdGetHueInfo);
        _setHue = Get<SetHueAngleFn>(IdSetHueAngle);

        var enumerate = Get<EnumDisplayFn>(IdEnumNvidiaDisplayHandle);
        var getName = Get<GetDisplayNameFn>(IdGetAssociatedNvidiaDisplayName);
        if (enumerate is not null && getName is not null)
        {
            for (uint i = 0; i < 32 && enumerate(i, out IntPtr handle) == 0; i++)
            {
                var name = new byte[64];
                if (getName(handle, name) == 0)
                    _handles[Encoding.ASCII.GetString(name).TrimEnd('\0')] = handle;
            }
        }

        var props = new List<GpuColorProperty>();
        if (_getDvc is not null && _setDvc is not null) props.Add(GpuColorProperty.Saturation);
        if (_getHue is not null && _setHue is not null) props.Add(GpuColorProperty.Hue);
        SupportedProperties = props;
    }

    public static NvidiaColorControl? TryCreate()
    {
        try
        {
            var init = Get<InitializeFn>(IdInitialize);
            if (init is null || init() != 0) return null;
            var control = new NvidiaColorControl();
            if (control._handles.Count > 0) return control;
            control.Dispose();
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
        return null;
    }

    private static T? Get<T>(uint id) where T : Delegate
    {
        IntPtr ptr = QueryInterface(id);
        return ptr == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer<T>(ptr);
    }

    // NVAPI struct versions are sizeof(struct) | (version << 16).
    private static uint Version<T>(int version = 1) where T : struct => (uint)(Marshal.SizeOf<T>() | (version << 16));

    public GpuColorRange? Get(string deviceName, GpuColorProperty property)
    {
        if (!_handles.TryGetValue(deviceName, out var handle)) return null;
        switch (property)
        {
            case GpuColorProperty.Saturation when _getDvc is not null:
                var dvc = new DvcInfo { Version = Version<DvcInfo>() };
                if (_getDvc(handle, 0, ref dvc) != 0) return null;
                return new GpuColorRange(dvc.CurrentLevel, dvc.MinLevel, dvc.MinLevel, dvc.MaxLevel);

            case GpuColorProperty.Hue when _getHue is not null:
                var hue = new HueInfo { Version = Version<HueInfo>() };
                if (_getHue(handle, 0, ref hue) != 0) return null;
                return new GpuColorRange(hue.CurrentAngle, hue.DefaultAngle, 0, 359);
        }
        return null;
    }

    public bool Set(string deviceName, GpuColorProperty property, int value)
    {
        if (!_handles.TryGetValue(deviceName, out var handle)) return false;
        return property switch
        {
            GpuColorProperty.Saturation when _setDvc is not null => _setDvc(handle, 0, value) == 0,
            GpuColorProperty.Hue when _setHue is not null => _setHue(handle, 0, value) == 0,
            _ => false,
        };
    }

    public void Dispose() => _unload?.Invoke();
}
