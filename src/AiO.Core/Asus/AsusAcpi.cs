using System.ComponentModel;
using System.Runtime.InteropServices;
using AiO.Core.Native;
using Microsoft.Win32.SafeHandles;

namespace AiO.Core.Asus;

/// <summary>
/// Raw access to the ASUS ATKACPI driver that ships with ASUS System Control Interface.
/// This is the same channel Armoury Crate and G-Helper use.
/// </summary>
public sealed class AsusAcpi : IDisposable
{
    private const string DevicePath = @"\\.\ATKACPI";

    private readonly SafeFileHandle _handle;
    private readonly object _lock = new();
    private readonly Dictionary<uint, bool> _supported = new();

    private AsusAcpi(SafeFileHandle handle) => _handle = handle;

    /// <summary>Opens the driver, or returns null when this isn't an ASUS machine (or the driver is missing).</summary>
    public static AsusAcpi? TryOpen()
    {
        var handle = Kernel32.CreateFile(DevicePath, Kernel32.GENERIC_READ | Kernel32.GENERIC_WRITE,
            Kernel32.FILE_SHARE_READ | Kernel32.FILE_SHARE_WRITE, IntPtr.Zero, Kernel32.OPEN_EXISTING,
            Kernel32.FILE_ATTRIBUTE_NORMAL, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            return null;
        }
        return new AsusAcpi(handle);
    }

    private byte[] Call(uint method, byte[] args)
    {
        var input = AsusAcpiProtocol.BuildCall(method, args);
        var output = new byte[16];
        lock (_lock)
        {
            if (!Kernel32.DeviceIoControl(_handle, AsusAcpiProtocol.IoControlCode, input, (uint)input.Length,
                    output, (uint)output.Length, out _, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "ATKACPI call failed");
        }
        return output;
    }

    /// <summary>Reads a device value; negative when unsupported.</summary>
    public int Get(uint deviceId) => AsusAcpiProtocol.DecodeGet(Call(AsusAcpiProtocol.MethodGet, AsusAcpiProtocol.DeviceArgs(deviceId, 0)));

    /// <summary>Reads the raw 16-byte buffer for a device (used for fan curves).</summary>
    public byte[] GetBuffer(uint deviceId, uint argument) =>
        Call(AsusAcpiProtocol.MethodGet, AsusAcpiProtocol.DeviceArgs(deviceId, argument));

    /// <summary>Writes a device value. Returns the firmware status (1 = OK).</summary>
    public int Set(uint deviceId, int value) =>
        BitConverter.ToInt32(Call(AsusAcpiProtocol.MethodSet, AsusAcpiProtocol.DeviceArgs(deviceId, (uint)value)));

    public int Set(uint deviceId, byte[] payload) =>
        BitConverter.ToInt32(Call(AsusAcpiProtocol.MethodSet, AsusAcpiProtocol.DeviceArgs(deviceId, payload)));

    public bool IsSupported(uint deviceId)
    {
        lock (_supported)
        {
            if (!_supported.TryGetValue(deviceId, out bool supported))
            {
                try { supported = Get(deviceId) >= 0; }
                catch (Win32Exception) { supported = false; }
                _supported[deviceId] = supported;
            }
            return supported;
        }
    }

    public void Dispose() => _handle.Dispose();
}
