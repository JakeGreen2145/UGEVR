# Injector session protocol, version 1

The production session controller starts a game with `UGEVR_SESSION_ID` set to a GUID and `UGEVR_SESSION_DIRECTORY` set to an absolute per-session directory. The Mono plugin has no dependency on Frame Testbench. Without either environment variable it operates standalone; an incomplete or invalid session configuration prevents initialization and logs the reason.

The plugin atomically replaces `injector-status.yaml` at least once per second while Unity's `Update` callback runs, including when VR rendering is disabled. Paused or blocked engine callbacks can make the status stale. The controller treats a file older than ten seconds as stale. A heartbeat does not prove GPU progress or a physical headset connection.

Transient Windows replacement contention receives up to three retries after 5, 10 and 20 milliseconds. Only atomic replacement is retried. Exhaustion preserves the previous complete status file, cleans the temporary file and reports an error for the next heartbeat; it does not crash the engine callback or overwrite the destination in place. A subsequent successful publication clears its transient status-write error. Readers should allow read, write and delete sharing.

```yaml
schema_version: 1
run_id: '00000000-0000-0000-0000-000000000001'
process_id: 1234
unity_version: '2017.4.40f1'
engine_ready: true
runtime: 'openvr'
runtime_ready: true
rendering_active: false
submitted_pairs: 42
acknowledged_revision: 3
last_error: ''
```

`engine_ready` requires at least two recurring `Update` callbacks. `runtime_ready` means OpenVR initialized and the compositor and targets are available. `rendering_active` requires a recent acknowledged, successful paired submission while enabled. `submitted_pairs` is the bridge's count of successful paired submissions, not evidence of game images on a headset. The controller validates both `run_id` and `process_id` against its owned target identity. Timestamp freshness is distinct from all readiness fields.

The external producer atomically replaces `command.txt` with flat UEVR-style `key=value` lines. It serializes revisions within one run. The file is at most 4096 bytes; unknown or duplicated keys, malformed values and a mismatched run are rejected with `last_error` and no acknowledgment.

```ini
run_id=00000000-0000-0000-0000-000000000001
revision=4
enabled=true
recenter=yaw
```

`revision` is a positive signed 64-bit integer. `enabled` is exactly `true` or `false`. Optional `recenter` supports `true` for full tracked position and yaw, `position`, `yaw`, `height`, and `false` for no operation. Recenter needs enabled rendering and a valid tracked head sample. A disabled command with recenter remains unacknowledged with an error. Stale revisions have no side effects. A received command is acknowledged only after application, not after parsing. Disable waits for the native render callback to release its owned texture references; recenter waits for a valid sample.

The initial origin captures head position and yaw once. A game-camera replacement never recalibrates tracking. `UnitsPerMeter`, configured in the plugin's `Tracking` section, scales both head translation and eye separation. It defaults to one and must be finite and positive. Game position and horizontal yaw provide the default anchor. `RetainGamePitchAndRoll` explicitly permits game tilt; tracked head tilt and full eye-to-head orientation always remain live. Composition uses world-space position/rotation, so nonuniform parent scale does not stretch eye separation.

Each eye pair borrows one selected camera after the normal game frame. Both eyes use the same anchor and tracked sample. A restoration scope restores transform, target, projection, view, viewport, occlusion, background and clear flags even when rendering throws. This does not prove arbitrary game render callbacks have no gameplay side effects. Those effects and per-eye temporal history need runtime probes. `Application.runInBackground` is borrowed while VR is enabled and restored on disable/failure when the value still matches the injector's assignment.

Matrix restoration preserves automatic Unity projection/view updates when the original values match the engine's `ResetProjectionMatrix` / `ResetWorldToCameraMatrix` results. Distinct custom matrices are restored explicitly. Unity's public API does not expose its internal override flags, so a custom matrix exactly equal to an automatic matrix cannot be distinguished. This probe occurs without rendering. It avoids freezing an ordinary camera's future view or FOV by restoring every matrix as a setter override. Exact custom/default behavior, including temporal-jitter callbacks, remains a runtime validation case.

Native event 0 waits for poses, event 1 submits, event 2 optionally captures a compositor image, and event 3 clears the bridge's references. The managed producer associates waits/submissions with a monotonically increasing generation and frame ID. It waits for explicit completed identities before consuming poses or reusing the state for another pair. Callback posting is not completion. The initial conservative producer spans engine frames rather than blocking the engine thread on an undocumented texture-query synchronization assumption; prediction and timing need measured runtime validation.

Pose errors report both native bridge status and the OpenVR wait result. Bridge rejection for a removed device or invalid texture can happen before `WaitGetPoses`; its not-ready wait value must not be described as an OpenVR wait failure.

Disable stops new pairs and restores owned game/background state. Eye targets remain alive until process teardown because the reference-drain API does not establish GPU idle. The plugin does not call `OpenVR.Shutdown` or unload its DLL while queued callbacks can use runtime/resources. If drain takes longer than five seconds, status reports the unresolved operation and recommends a game restart. This initial lifetime policy supports disable/reenable, not safe DLL unloading or complete runtime-restart recovery.

Image collection is opt-in through `Diagnostics.CaptureImages=false` by default. When explicitly enabled, a `UnityVR-evidence/capture-request` file requests the next eye-pair images and compositor capture. No automatic frame-120 capture or image directory is created by default. Existing JSON pose records remain the prototype evidence format. Capture failures restore `RenderTexture.active` and destroy the temporary image, then fail the VR pipeline with an actionable log.

Run `dotnet run --project tests/ManagedBoundary/ManagedBoundary.csproj -c Release` for known transform, scale/recenter, canted-eye and restoration checks, plus a separate producer process that writes commands through the external file boundary. The camera fixture verifies the restoration code's assignments but does not simulate Unity's native camera lifetime. Clean managed builds against both game assembly sets do not establish same-binary runtime compatibility; the physical gameplay gate remains outstanding.
