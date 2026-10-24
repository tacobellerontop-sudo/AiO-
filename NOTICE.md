# Third-party notices

AiO talks to hardware using protocols and identifiers that other open-source projects worked out and
documented. AiO's code is its own implementation; no source code from these projects is included.

| Project | License | What AiO relies on |
|---|---|---|
| [OpenRazer](https://github.com/openrazer/openrazer) | GPL-2.0 | Razer 90-byte report layout, command classes and ids, CRC, transaction ids; USB product ids and device names (generated into `src/AiO.Core/Razer/RazerDeviceCatalog.g.cs` by `tools/gen_razer_catalog.py`) |
| [G-Helper](https://github.com/seerge/g-helper) | GPL-3.0 | ATKACPI method and device ids (performance modes, GPU eco/MUX, fan curves, battery limit, overdrive, power limits) and the Aura HID keyboard message format |
| [OpenRGB](https://gitlab.com/CalcProgrammer1/OpenRGB) | GPL-2.0 | The OpenRGB SDK network protocol (`Documentation/OpenRGBSDK.md`) |
| [NvAPIWrapper](https://github.com/falahati/NvAPIWrapper) | LGPL-3.0 | Cross-checked NVAPI function ids |

Razer, Synapse and Chroma are trademarks of Razer Inc. ASUS, ROG, TUF, Armoury Crate and Aura are
trademarks of ASUSTeK Computer Inc. NVIDIA is a trademark of NVIDIA Corporation, and AMD is a trademark of
Advanced Micro Devices, Inc. AiO is not affiliated with or endorsed by any of them.
