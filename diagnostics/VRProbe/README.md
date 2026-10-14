# Independent OpenVR diagnostics

Build with `dotnet build diagnostics/VRProbe/VRProbe.csproj -c Release`. With no arguments, the existing HMD, projection and compositor snapshot remains unchanged.

The optional Windows controller snapshot requires an explicit current scene PID:

```powershell
dotnet diagnostics/VRProbe/bin/Release/net10.0/VRProbe.dll --controllers --expected-scene-pid 12345
```

The caller must independently verify the game's run, PID and process creation time immediately before the probe. PID matching alone cannot detect PID reuse. This mode initializes a background OpenVR client, reads once and shuts down its client connection. It never sets an action manifest, requests focus, generates input or changes runtime settings.

The probe requires compositor scene focus to match the supplied PID before and after its reads. A mismatch exits with code 3 and emits no controller samples. Invalid or incomplete options exit with code 2 before OpenVR initialization. OpenVR initialization failure keeps the existing code 1. A completed snapshot exits with code 0 even if no controller is present or input is unavailable; these are observed fields, not delivery success.

Output includes `inputAvailable`, at most 64 device-class checks, actual controller roles, bounded model/serial properties and their errors, packet numbers, pressed/touched masks as decimal strings, five axes, and standing-space controller poses. `GetControllerState` receives the SDK struct's actual `Marshal.SizeOf`; unavailable state fields remain null. Nonfinite numeric values remain null. Focus changes discard all controller samples. Focus checks bound the observation but cannot prove uninterrupted focus between the two reads.

`processIdentity` remains `not_verified_pid_only` and `inputReceipt` remains `unknown`. A controller snapshot does not prove headset proxy forwarding, a flat game's input receipt, or gameplay. Effective SteamVR bindings may alter app-visible identity. A physical test should correlate stable device identity/role and a distinct pose, then compare baseline, held right A, release, two distinct axis-0 values and return to neutral. Keep that causal receiver evidence separate from proxy readback and game-consumer evidence.

Source-only checks use a controlled readback without loading the OpenVR runtime:

```powershell
dotnet run --project diagnostics/VRProbe.Checks/VRProbe.Checks.csproj -c Release
```

This executable and its checks are independent diagnostics, not an injector or Frame Testbench runtime dependency.

The API signatures and Windows layout come from this repository's `vendor/openvr_api.cs`: `CVRSystem.IsInputAvailable`, `GetControllerState`, `GetControllerRoleForTrackedDeviceIndex`, `GetStringTrackedDeviceProperty` and `VRControllerState_t`. The diagnostic reads legacy state without creating an app action manifest. [Valve's driver documentation](https://github.com/ValveSoftware/openvr/blob/master/docs/Driver_API_Documentation.md) describes legacy bindings and controller simulation; source definitions establish available APIs, while runtime receiver observations establish whether the effective controller profile exports a particular input.
