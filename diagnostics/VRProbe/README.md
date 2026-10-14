# Independent OpenVR diagnostics

Build with `dotnet build diagnostics/VRProbe/VRProbe.csproj -c Release`. With no arguments, the existing HMD, projection and compositor snapshot remains unchanged.

The optional Windows controller snapshot requires an explicit current scene PID:

```powershell
dotnet diagnostics/VRProbe/bin/Release/net10.0/VRProbe.dll --controllers --expected-scene-pid 12345
```

The caller must independently verify the game's run, PID and process creation time immediately before the probe. PID matching alone cannot detect PID reuse. This mode initializes a background OpenVR client, reads once and shuts down its client connection. It never sets an action manifest, requests focus, generates input or changes runtime settings.

The probe requires compositor scene focus to match the supplied PID before and after its reads. A mismatch exits with code 3 and emits no controller samples. Invalid or incomplete options exit with code 2 before OpenVR initialization. OpenVR initialization failure keeps the existing code 1. A completed snapshot exits with code 0 even if no controller is present or input is unavailable; these are observed fields, not delivery success.

Output includes `inputAvailable`, at most 64 device-class checks, actual controller roles, bounded model/serial properties and their errors, packet numbers, pressed/touched masks as decimal strings, five axes, and standing-space controller poses. `GetControllerState` receives the SDK struct's actual `Marshal.SizeOf`; unavailable state fields remain null. Nonfinite numeric values remain null. Focus changes discard all controller samples. Focus checks bound the observation but cannot prove uninterrupted focus between the two reads.

Each controller also includes `axis0Type` through `axis4Type`. These are `{value, error}` readbacks of the public `Prop_Axis0Type_Int32` through `Prop_Axis4Type_Int32` properties, with exactly one integer query per axis. A successful value is the raw signed integer. In the vendored SDK, `None=0`, `TrackPad=1`, `Joystick=2`, and `Trigger=3`; future or unknown integer values remain unchanged. A failed query reports a null value and the actual property error. Axis type properties and axis state are independent observations: nonzero axis coordinates do not establish joystick type, and a joystick property does not establish available axis state. Consumers must require a successful explicit joystick type rather than infer it from axis coordinates.

`processIdentity` remains `not_verified_pid_only` and `inputReceipt` remains `unknown`. A controller snapshot does not prove headset proxy forwarding, a flat game's input receipt, or gameplay. Effective SteamVR bindings may alter app-visible identity. A physical test should correlate stable device identity/role and a distinct pose, then compare baseline, held right A, release, two distinct axis-0 values and return to neutral. Keep that causal receiver evidence separate from proxy readback and game-consumer evidence.

Source-only checks use a controlled readback without loading the OpenVR runtime:

```powershell
dotnet run --project diagnostics/VRProbe.Checks/VRProbe.Checks.csproj -c Release
```

This executable and its checks are independent diagnostics, not an injector or Frame Testbench runtime dependency.

## Fixed controller receiver window

For a causal press/release probe, the opt-in window requires both the current game PID and its exact process start time in UTC DateTime ticks:

```powershell
dotnet diagnostics/VRProbe/bin/Release/net10.0/VRProbe.dll --controller-window --expected-scene-pid 12345 --expected-process-start-utc-ticks 639275456966499101 > receiver-window.private.jsonl
```

Obtain the start ticks from the verified owned game session. This mode preserves the default HMD snapshot and the existing `--controllers` one-shot syntax. It does not accept `--controllers` together with `--controller-window`. Invalid options exit 2 before SDK startup.

The supervisor starts one owned worker, drains its stdout and stderr continuously, and enforces a fixed 20-second deadline that includes worker startup, OpenVR initialization, the observation window, and SDK shutdown. On timeout it terminates only that direct child and exits 124. Earlier JSONL records remain available, while the timeout record says `shutdownConfirmed=false`. An `end` record with `shutdownConfirmed=true` appears only after `OpenVR.Shutdown` returns. The worker uses one background OpenVR connection, no action manifest, and makes no focus, input, binding or runtime-setting changes. The internal worker argument is an implementation detail; invoke the documented supervised mode.

After initialization, a `begin` record starts a fixed 15-second window with at most 300 samples. The worker spaces reads by at least 50 ms using a monotonic Stopwatch, and does not catch up after slow SDK calls. It starts no new read after the deadline and excludes a read that finishes beyond it. A hung native call is bounded by the supervisor's whole deadline. Sample `elapsedSeconds` is monotonic worker uptime, including initialization; UTC timestamps allow correlation with the independently recorded Frame pulse. `begin` and `end` expose window boundaries and actual sample count. A normal completed diagnostic exits 0 regardless of missing controllers, identity rejections or unavailable input; completion alone is no receipt claim.

Every sample looks up the supplied PID's process start ticks before and after its SDK reads, and queries compositor focus before and after the availability/state/pose reads. PID reuse, process exit and focus changes exclude controller data. Accepted records retain both process-start boundaries, both focus boundaries, both availability boundaries, actual controller identity and role, legacy decimal-string button masks, all axes and standing poses. Enumeration remains bounded to 64 devices, and model/serial queries use 256 bytes without retry. Null/error fields preserve unavailable observations. Boundary equality cannot prove uninterrupted ownership between reads.

`inputReceipt` stays `unknown` and `gateEligible` stays false in every record. This diagnostic can establish Windows OpenVR receiver state during a causal pulse; Unity game input and gameplay require separate evidence. Save JSONL privately because controller serials and model identities are present. During a paired Frame pulse, avoid starting other SDK observers in the same window, correlate the exact synthetic device identity and pose, require a neutral baseline, and release the pulse in cleanup.

Add `--head-pose` to the supervised controller window to include `snapshot.headPose` from the same Standing pose-array read and Background client. The flag is invalid in other modes. It adds one HMD device-class query per accepted capture, with no extra pose-array, controller-state or property queries. Without the flag, the field is absent and the existing query count is unchanged.

The optional object records `deviceIndex`, `deviceClass`, `connected`, `poseValid`, `trackingResult`, the twelve raw `transform` components, and `status`. Status is `observed`, `device_class_mismatch`, `pose_unavailable`, or `nonfinite_pose`. Missing fields and nonfinite components remain null. A focus or process-identity change discards this object along with controller data. Samples use the existing UTC and monotonic timestamps and the same 15-second, 300-sample limit. This readback establishes a PC OpenVR pose observation. It does not establish Unity camera use, optical headset output, or qualifying gameplay.

The API signatures and Windows layout come from this repository's `vendor/openvr_api.cs`: `CVRSystem.IsInputAvailable`, `GetControllerState`, `GetControllerRoleForTrackedDeviceIndex`, `GetStringTrackedDeviceProperty` and `VRControllerState_t`. The diagnostic reads legacy state without creating an app action manifest. [Valve's driver documentation](https://github.com/ValveSoftware/openvr/blob/master/docs/Driver_API_Documentation.md) describes legacy bindings and controller simulation; source definitions establish available APIs, while runtime receiver observations establish whether the effective controller profile exports a particular input.
