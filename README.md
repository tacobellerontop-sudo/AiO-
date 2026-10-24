# AiO

One Windows app for the hardware settings that normally need several vendor tools:
monitor colour and resolution, NVIDIA / AMD colour controls, Razer peripherals, and
ASUS laptop controls in the style of G-Helper. It runs from the tray and re-applies your
settings at start-up.

> **Status: early, untested on hardware.** Everything compiles and the protocol code has
> unit tests, but none of it has run against real devices yet. Expect rough edges, and
> please report what does and doesn't work on your hardware.

## Features

| Page | What it does | How |
|---|---|---|
| **Displays** | Resolution, refresh rate, orientation, scaling, HDR on/off. Changes revert after 15 s unless you keep them. | `ChangeDisplaySettingsEx`, Windows display-config (CCD) API |
| **Colour → Monitor** | Brightness, contrast, RGB gain, volume, input source, set in the monitor itself | DDC/CI (`dxva2.dll`) |
| **Colour → GPU** | NVIDIA digital vibrance and hue; AMD saturation, hue, brightness and contrast | NVAPI, AMD Display Library (ADL) |
| **Colour → Software** | Colour temperature, gamma, brightness, contrast, per-channel gain on any GPU | GPU gamma ramp |
| **Colour → Presets** | Built-in (Default, Vivid, Competitive, Movie, Reading, Night) and your own presets combining software colour, GPU vibrance and monitor brightness/contrast; switch from the tray or a keyboard shortcut | All of the above |
| **Mouse** | RawAccel acceleration curves (linear, classic, natural, synchronous, power, jump) with a live graph; Windows pointer speed and Enhance Pointer Precision | [RawAccel](https://github.com/a1xd/rawaccel)'s signed driver: AiO edits its `settings.json` and runs its `writer.exe` |
| **Keyboard** | Remap any key, remap shortcuts, launch apps, type text, cycle ASUS modes; optionally only inside specific apps. Works with any keyboard | Low-level keyboard hook, like PowerToys Keyboard Manager |
| **Razer & RGB** | DPI, polling rate, lighting effects, brightness and battery for Razer devices, **without Synapse** | Razer HID protocol as documented by [OpenRazer](https://github.com/openrazer/openrazer) |
| | Lighting for any device [OpenRGB](https://openrgb.org) supports | OpenRGB SDK network protocol |
| **ASUS** | Silent / Balanced / Turbo, Eco / Standard / Ultimate GPU modes, live temperatures and fan RPM, custom fan curves and power limits per mode, battery charge limit, panel overdrive, keyboard backlight | ASUS ATKACPI driver and Aura HID, as documented by [G-Helper](https://github.com/seerge/g-helper) |

The ASUS page only appears on ASUS laptops that have the ASUS System Control Interface driver installed.

## Requirements

- Windows 10 or 11, x64
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
- Optional: [OpenRGB](https://openrgb.org) with its SDK server enabled, for non-Razer RGB

### Tips

- **RawAccel:** download it, unzip it, run `installer.exe` and restart Windows. Then point AiO's Mouse page at the folder.
- **Keyboard remaps** can't reach apps running as administrator unless AiO is elevated too.
- **Razer:** quit Synapse before using AiO's Razer page. Two programs sending commands to the same device conflict.
- **Strong colour temperatures:** Windows limits how far gamma ramps can move. The Colour page has an
  "Unlock full range" button (needs admin and a sign-out).
- **Admin:** most features work without admin. Use *Settings → Restart as administrator* to unlock the
  gamma range, or to make *Start with Windows* register an elevated logon task (no UAC prompt each boot).

## Building

```
dotnet build AiO.sln
dotnet test tests/AiO.Core.Tests
dotnet publish src/AiO.App -c Release -o publish
```

The app targets `net8.0-windows` with `EnableWindowsTargeting`, so it also *compiles* on Linux and macOS.
It only *runs* on Windows. CI (`.github/workflows/build.yml`) builds, tests and publishes on `windows-latest`.

## Layout

```
src/AiO.Core     Hardware access and protocol code (no UI). Plain net8.0 so tests run anywhere.
  Display/       Modes, orientation, HDR, scaling
  Color/         Gamma ramps, DDC/CI
  Gpu/           NVAPI (NVIDIA) and ADL (AMD) colour controls
  Hid/           Windows HID enumeration and feature reports
  Razer/         Razer report format, commands, device discovery, device catalogue
  OpenRgb/       OpenRGB SDK client
  Asus/          ATKACPI calls, fan curves, performance/GPU modes, Aura keyboard
  Mouse/         RawAccel curve maths (ported for the preview) and settings.json editing, Windows pointer
  Keyboard/      Remapping engine (pure, tested) and the low-level keyboard hook
  Settings/      JSON settings in %APPDATA%\AiO\settings.json
src/AiO.App      WPF UI, tray icon, start-up integration
tests/           Unit tests for the protocol and maths code
tools/           Script that regenerates the Razer device catalogue from OpenRazer
```

## Credits

AiO reimplements hardware protocols documented by these open-source projects. It doesn't copy their code:

- [OpenRazer](https://github.com/openrazer/openrazer): the Razer HID report format and the product IDs in `RazerDeviceCatalog.g.cs`
- [G-Helper](https://github.com/seerge/g-helper): ASUS ACPI device IDs and the Aura keyboard message format
- [OpenRGB](https://openrgb.org): the SDK network protocol
- [RawAccel](https://github.com/a1xd/rawaccel) (MIT): acceleration curve formulas, ported for AiO's preview graph, and the settings file format

See [NOTICE.md](NOTICE.md).

## License

MIT. See [LICENSE](LICENSE).
