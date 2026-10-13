# Third-party notices

## OpenVR

The vendored OpenVR headers, C# bindings, import library and runtime DLL come from [ValveSoftware/openvr](https://github.com/ValveSoftware/openvr), commit `0924064316de3effbcd1acf1e309182a2deb1c05`.

The upstream license is retained in [vendor/Valve-OpenVR-LICENSE](vendor/Valve-OpenVR-LICENSE). The diagnostic driver's SDK header has the same upstream license in `diagnostics/test-hmd-driver/vendor/LICENSE.openvr`.

## BepInEx

The dependency-fetch script downloads the official [BepInEx 5.4.23.5 Windows x64 release](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5). The downloaded package is excluded from Git. Its upstream license is retained in [vendor/BepInEx-LICENSE](vendor/BepInEx-LICENSE). Bundled dependency licenses remain governed by their upstream projects.

## Research references

UUVR, UnityVRMod and VRGIN were inspected as research references. Their source trees, game files and machine-specific analysis artifacts are excluded from this repository.
