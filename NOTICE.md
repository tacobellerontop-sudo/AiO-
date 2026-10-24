# Third-party notices

AiO talks to hardware using protocols and identifiers that other open-source projects worked out and
documented. AiO's code is its own implementation; apart from RawAccel's MIT-licensed curve formulas (credited below), no source code from these projects is included.

| Project | License | What AiO relies on |
|---|---|---|
| [OpenRazer](https://github.com/openrazer/openrazer) | GPL-2.0 | Razer 90-byte report layout, command classes and ids, CRC, transaction ids; USB product ids and device names (generated into `src/AiO.Core/Razer/RazerDeviceCatalog.g.cs` by `tools/gen_razer_catalog.py`) |
| [G-Helper](https://github.com/seerge/g-helper) | GPL-3.0 | ATKACPI method and device ids (performance modes, GPU eco/MUX, fan curves, battery limit, overdrive, power limits) and the Aura HID keyboard message format |
| [OpenRGB](https://gitlab.com/CalcProgrammer1/OpenRGB) | GPL-2.0 | The OpenRGB SDK network protocol (`Documentation/OpenRGBSDK.md`) |
| [RawAccel](https://github.com/a1xd/rawaccel) | MIT | Acceleration curve formulas in `src/AiO.Core/Mouse/AccelCurve.cs` are ported from RawAccel's `common/accel-*.hpp` (Copyright (c) 2020 a1xd, MIT); settings.json format. AiO uses RawAccel's own driver and writer.exe |
| [NvAPIWrapper](https://github.com/falahati/NvAPIWrapper) | LGPL-3.0 | Cross-checked NVAPI function ids |

Razer, Synapse and Chroma are trademarks of Razer Inc. ASUS, ROG, TUF, Armoury Crate and Aura are
trademarks of ASUSTeK Computer Inc. NVIDIA is a trademark of NVIDIA Corporation, and AMD is a trademark of
Advanced Micro Devices, Inc. AiO is not affiliated with or endorsed by any of them.

## RawAccel licence

The curve formulas in `src/AiO.Core/Mouse/AccelCurve.cs` are ported from RawAccel and used under this licence:

```
MIT License

Copyright (c) 2020 a1xd

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```
