using System.Runtime.InteropServices;

namespace AiO.Core.Audio;

/// <summary>Microphone mute through the Windows Core Audio API (the same switch as Sound settings).</summary>
public static class Microphone
{
    private const int ECapture = 1;
    private const int EConsole = 0, ECommunications = 2;
    private const int DeviceStateActive = 0x1;
    private const int ClsctxAll = 0x17;

    /// <summary>
    /// Toggles mute on the default microphone and applies the same state to every other active
    /// microphone, so calls using a different device are muted too. Returns the new state, or null
    /// when there is no microphone.
    /// </summary>
    public static bool? ToggleMute()
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        try
        {
            IAudioEndpointVolume? main = null;
            foreach (int role in new[] { EConsole, ECommunications })
            {
                if (enumerator.GetDefaultAudioEndpoint(ECapture, role, out var device) == 0 && device is not null)
                {
                    main = Volume(device);
                    break;
                }
            }
            if (main is null) return null;

            Marshal.ThrowExceptionForHR(main.GetMute(out bool muted));
            bool target = !muted;
            var context = Guid.Empty;
            Marshal.ThrowExceptionForHR(main.SetMute(target, ref context));

            if (enumerator.EnumAudioEndpoints(ECapture, DeviceStateActive, out var collection) == 0 && collection is not null)
            {
                collection.GetCount(out uint count);
                for (uint i = 0; i < count; i++)
                {
                    if (collection.Item(i, out var other) != 0 || other is null) continue;
                    try { Volume(other).SetMute(target, ref context); }
                    catch { /* a device that refuses mute doesn't matter */ }
                }
            }
            return target;
        }
        finally
        {
            Marshal.ReleaseComObject(enumerator);
        }
    }

    private static IAudioEndpointVolume Volume(IMMDevice device)
    {
        var iid = typeof(IAudioEndpointVolume).GUID;
        Marshal.ThrowExceptionForHR(device.Activate(ref iid, ClsctxAll, IntPtr.Zero, out object volume));
        return (IAudioEndpointVolume)volume;
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorComObject { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
}
