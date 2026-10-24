using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using AiO.Core.Native;
using Microsoft.Win32.SafeHandles;

namespace AiO.Core.Hid;

/// <summary>One HID top-level collection as reported by Windows.</summary>
public sealed record HidDeviceInfo(
    string Path,
    ushort VendorId,
    ushort ProductId,
    ushort UsagePage,
    ushort Usage,
    int FeatureReportLength,
    int InputReportLength,
    int OutputReportLength,
    string ProductName,
    string SerialNumber)
{
    /// <summary>USB interface number parsed from the path ("...&amp;mi_02..."), or -1.</summary>
    public int InterfaceNumber
    {
        get
        {
            int i = Path.IndexOf("&mi_", StringComparison.OrdinalIgnoreCase);
            if (i < 0 || i + 6 > Path.Length) return -1;
            return int.TryParse(Path.AsSpan(i + 4, 2), System.Globalization.NumberStyles.HexNumber, null, out int n) ? n : -1;
        }
    }
}

public static class HidEnumerator
{
    /// <summary>Lists HID collections, optionally filtered by vendor id.</summary>
    public static IReadOnlyList<HidDeviceInfo> Enumerate(ushort? vendorId = null)
    {
        var result = new List<HidDeviceInfo>();
        HidNative.HidD_GetHidGuid(out Guid hidGuid);
        IntPtr set = HidNative.SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero,
            HidNative.DIGCF_PRESENT | HidNative.DIGCF_DEVICEINTERFACE);
        if (set == IntPtr.Zero || set == new IntPtr(-1)) return result;

        try
        {
            var data = new HidNative.SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<HidNative.SP_DEVICE_INTERFACE_DATA>() };
            for (int index = 0; HidNative.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hidGuid, index, ref data); index++)
            {
                string? path = GetInterfacePath(set, ref data);
                if (path is null) continue;

                // Cheap pre-filter: the path contains "vid_XXXX".
                if (vendorId is ushort vid && !path.Contains($"vid_{vid:x4}", StringComparison.OrdinalIgnoreCase))
                    continue;

                var info = Describe(path);
                if (info is not null) result.Add(info);
            }
        }
        finally
        {
            HidNative.SetupDiDestroyDeviceInfoList(set);
        }

        return result;
    }

    private static string? GetInterfacePath(IntPtr set, ref HidNative.SP_DEVICE_INTERFACE_DATA data)
    {
        HidNative.SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out int size, IntPtr.Zero);
        if (size <= 0) return null;

        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            // SP_DEVICE_INTERFACE_DETAIL_DATA_W.cbSize is 8 on x64, 6 on x86.
            Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);
            if (!HidNative.SetupDiGetDeviceInterfaceDetail(set, ref data, buffer, size, out _, IntPtr.Zero))
                return null;
            return Marshal.PtrToStringUni(buffer + 4);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static HidDeviceInfo? Describe(string path)
    {
        // Zero access rights are enough to query attributes, even for mice and keyboards
        // that Windows holds open exclusively.
        using var handle = Kernel32.CreateFile(path, 0, Kernel32.FILE_SHARE_READ | Kernel32.FILE_SHARE_WRITE,
            IntPtr.Zero, Kernel32.OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle.IsInvalid) return null;

        var attributes = new HidNative.HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HidNative.HIDD_ATTRIBUTES>() };
        if (!HidNative.HidD_GetAttributes(handle, ref attributes)) return null;

        HidNative.HIDP_CAPS caps = default;
        if (HidNative.HidD_GetPreparsedData(handle, out IntPtr preparsed))
        {
            try { HidNative.HidP_GetCaps(preparsed, out caps); }
            finally { HidNative.HidD_FreePreparsedData(preparsed); }
        }

        return new HidDeviceInfo(path, attributes.VendorID, attributes.ProductID, caps.UsagePage, caps.Usage,
            caps.FeatureReportByteLength, caps.InputReportByteLength, caps.OutputReportByteLength,
            ReadString(handle, HidNative.HidD_GetProductString),
            ReadString(handle, HidNative.HidD_GetSerialNumberString));
    }

    private static string ReadString(SafeFileHandle handle, Func<SafeFileHandle, byte[], int, bool> getter)
    {
        var buffer = new byte[256];
        if (!getter(handle, buffer, buffer.Length)) return "";
        return Encoding.Unicode.GetString(buffer).TrimEnd('\0');
    }
}

/// <summary>An open handle to a HID collection used for feature-report traffic.</summary>
public sealed class HidConnection : IDisposable
{
    private readonly SafeFileHandle _handle;

    public HidDeviceInfo Info { get; }

    private HidConnection(HidDeviceInfo info, SafeFileHandle handle)
    {
        Info = info;
        _handle = handle;
    }

    public static HidConnection Open(HidDeviceInfo info)
    {
        const uint share = Kernel32.FILE_SHARE_READ | Kernel32.FILE_SHARE_WRITE;
        var handle = Kernel32.CreateFile(info.Path, Kernel32.GENERIC_READ | Kernel32.GENERIC_WRITE, share,
            IntPtr.Zero, Kernel32.OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            // Mouse/keyboard collections refuse read/write access, but feature reports still work.
            handle.Dispose();
            handle = Kernel32.CreateFile(info.Path, 0, share, IntPtr.Zero, Kernel32.OPEN_EXISTING, 0, IntPtr.Zero);
        }
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error, $"Could not open HID device {info.ProductName}");
        }
        return new HidConnection(info, handle);
    }

    /// <summary>Sends a feature report. <paramref name="report"/>[0] is the report id.</summary>
    public void SetFeature(byte[] report)
    {
        if (!HidNative.HidD_SetFeature(_handle, report, report.Length))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "HidD_SetFeature failed");
    }

    /// <summary>Reads a feature report into <paramref name="report"/>; set [0] to the report id first.</summary>
    public void GetFeature(byte[] report)
    {
        if (!HidNative.HidD_GetFeature(_handle, report, report.Length))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "HidD_GetFeature failed");
    }

    public void Dispose() => _handle.Dispose();
}
