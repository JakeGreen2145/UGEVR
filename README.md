# UGEVR

A proof-of-concept VR injector for Unity games. UGEVR renders the game's camera once per eye, applies headset tracking, and submits the resulting D3D11 textures directly to SteamVR.

The prototype works through Unity's ordinary camera and texture APIs. It does not require the game to include Unity XR providers, and it contains no game-specific rendering code.

## Validation

The same plugin binary rendered the 3D title scenes of two originally non-VR games:

| Game | Unity version | Runtime |
| --- | --- | --- |
| Firewatch | 2017.4.40f1 | Mono CLR 2 |
| Outer Wilds | 2019.4.39f1 | Mono CLR 4 |

Both final runs exceeded 16,000 successful paired eye submissions. Fourteen tracking checks passed across the two games, covering translation, yaw, pitch, roll and reset. The checks compared an independent OpenVR client's pose matrices against the injected eye transforms, verified 64 mm eye separation, and checked that the game camera was restored after rendering. Outer Wilds' title camera also moved independently while the injected views followed it.

**These tests used a simulated SteamVR headset.** Physical-headset playback, comfort, motion prediction and full gameplay remain unverified. The results establish a working software path and promise for generalization, not universal compatibility. [Validation metrics](docs/validation.json) contain the recorded checks. Raw captures and machine-specific evidence are kept outside the public repository.

## Current scope

- Windows x64, Unity Mono, Direct3D 11 and OpenVR through SteamVR.
- Separate eye render textures, runtime eye offsets and asymmetric projection bounds.
- Tracking relative to the game's camera, with camera state restored after rendering.
- F8 toggles rendering and F9 recenters when the game's legacy keyboard API is available.

IL2CPP, OpenXR, D3D12, Vulkan, URP/HDRP, motion controllers and multi-camera composition are not implemented or validated. Screen-space UI is not converted to a VR panel. Reusing the original camera runs its effects repeatedly; temporal effects and game callbacks may need adaptation. Rendering the desktop view and both eyes adds cost.

## Build

Requirements are a .NET SDK, Visual Studio C++ x64 build tools, a Windows 10 SDK, and the managed assemblies from a compatible Unity Mono game. The independent pose reader targets .NET 10.

```powershell
.\scripts\Fetch-Dependencies.ps1
.\scripts\Build-Prototype.ps1 -GameManaged 'C:\Games\Game\Game_Data\Managed'
```

The script discovers MSVC and the Windows SDK. Optional `-CompilerRoot`, `-SdkRoot` and `-SdkVersion` parameters select a specific toolchain. The managed plugin targets .NET 3.5 and compiles against the game's Unity assemblies. Those game assemblies are not distributed here.

## Install and launch

Install into an unmodded test copy of a supported game:

```powershell
.\scripts\Install-Prototype.ps1 -GameRoot 'C:\Games\Game-Test'
.\scripts\Launch-Prototype.ps1 -GameExe 'C:\Games\Game-Test\Game.exe'
```

Start the headset's normal SteamVR PC connection before launching. The launcher requests D3D11. For a Steam copy that needs its application ID, pass `-SteamAppId` to the launcher. The tested Outer Wilds copy needed `-SteamAppId 753640`.

The installer refuses an existing `winhttp.dll` loader. It sets BepInEx's `HideManagerGameObject` option to true, which fixed an observed failure where Outer Wilds initialized plugins but never ticked them. Existing game controls remain available. Use the desktop window for menus when the UI does not appear in VR.

The installer adds BepInEx, Doorstop and the injector files to the test copy. Keeping a separate copy makes removal straightforward.

## How rendering works

BepInEx loads the managed plugin. For each frame, the plugin selects the game camera, samples the headset pose, combines it with the game camera's transform, and renders both eyes. A native bridge uses Unity render-thread callbacks for OpenVR's GPU-touching frame wait and texture submission.

Native texture queries synchronize Unity's render thread before reading poses and after rendering. Acquiring current native textures after rendering fixed black compositor output during development. Vertical texture bounds correct Unity's D3D11 texture orientation. The bridge leaves frame handoff to the next `WaitGetPoses` call rather than handing off before Unity's desktop `Present`.

## Repeat the simulated tracking test

The diagnostic driver publishes file-controlled poses through SteamVR's server-driver API. It is a simulated HMD, not a physical tracker.

```powershell
dotnet build .\diagnostics\VRProbe\VRProbe.csproj -c Release
.\diagnostics\test-hmd-driver\build.ps1
# With SteamVR closed:
.\scripts\Use-SimulatedHeadset.ps1
.\scripts\Use-TrackingTestHeadset.ps1
```

Start SteamVR and the prepared game. Once its scene is visible:

```powershell
.\scripts\Test-SimulatedTracking.ps1 -GameRoot 'C:\Games\Game-Test' -OutputName 'game-test'
python .\diagnostics\verify_captures.py game-test
```

The verifier needs NumPy and Pillow. Results and settings backups go into the ignored `evidence` folder. An empty `UnityVR-evidence/capture-request` file in the game's directory requests another capture.

After closing the game and SteamVR, restore the previous settings and remove the diagnostic driver registration:

```powershell
.\scripts\Use-TrackingTestHeadset.ps1 -Restore
```

Rebuild the diagnostic driver after moving the repository because its pose-file path is generated at build time.

## Prior work and dependencies

Research included [UUVR](https://github.com/Raicuparta/uuvr), [UnityVRMod](https://github.com/NewUnityModder/UnityVRMod) and [VRGIN](https://github.com/Eusth/VRGIN). Their source helped identify possible mechanisms and failure modes. The local tests above establish this prototype's result. Their project implementations are not included in this repository.

The rendering synchronization follows [Valve's Unity sample](https://github.com/ValveSoftware/openvr/blob/0924064316de3effbcd1acf1e309182a2deb1c05/samples/unity_teleport_sample/Assets/SteamVR/Scripts/SteamVR_Render.cs). BepInEx's manager behavior is discussed in [issue 1393](https://github.com/BepInEx/BepInEx/issues/1393). The suggested `MonoBehaviour..cctor` workaround crashed in the tested Outer Wilds build; the prototype uses manager hiding instead.

OpenVR SDK files are pinned to the commit in `vendor/OpenVR-commit.txt`. BepInEx is pinned to 5.4.23.5. See [third-party notices](THIRD_PARTY_NOTICES.md) for sources and upstream licenses.
