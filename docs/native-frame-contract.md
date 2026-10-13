# OpenVR D3D11 frame contract

This is the S1 transport behind [S1: make OpenVR D3D11 frame transport reject invalid and stale work](https://github.com/JakeGreen2145/UGEVR/issues/27). It supports the existing Windows x64 Mono producer, D3D11 and the pinned OpenVR API. It does not establish physical-headset support or runtime/device recovery.

## Producer and callback ordering

1. Start a nonzero generation. Use a strictly increasing frame ID for each attempted eye pair within that generation. Call `SetFrameState(generation, frame)` and `SetEyeTextures(left, right)` before issuing render event 0.
2. Event 0 checks the texture pair, calls `WaitGetPoses` on the Unity render thread, checks its error and HMD validity, and records the generation/frame that completed. The producer polls `GetCompletedPoseGeneration()` and `GetCompletedPoseFrame()`. Completion means the call returned, including a failure. Require `GetPoseWaitError() == 0` and `GetBridgeStatus() == 0` before rendering.
3. Obtain that completed pose without another wait. Render both real eye cameras from one gameplay state, then queue render event 1. Do not advance the producer frame or replace textures while a callback is pending.
4. Poll `GetCompletedSubmitGeneration()` and `GetCompletedSubmitFrame()`. Check the bridge and individual submit errors. The bridge consumes a pose once, even when one Submit fails. A failed pair cannot be retried under the same frame.
5. Render event 2 performs an explicitly requested mirror capture. It requires valid retained textures and a successful submission. A capture is diagnostic evidence and does not establish stereo correctness.
6. To disable, advance the generation, keep a nonzero frame ID, and queue event 3. Poll `GetCompletedDrainGeneration()` for that generation. Event 3 works with an empty texture pair and without a compositor. Resume only in a generation newer than the drained generation.

The existing export signatures and event IDs 0, 1 and 2 remain. Verified transport now requires the additive frame-state API. An older producer that never supplies identity is rejected rather than silently receiving a stale pose. All exports use the existing C calling convention; the callback returned by `GetRenderEventFunc()` uses `__stdcall`.

The producer must not infer callback completion from a native texture pointer query. It must not issue a second wait or advance the frame while waiting for a pose or submission acknowledgment. Every callback runs on the same Unity render thread. A callback on another thread is rejected; switching the Unity render thread requires a future explicit lifecycle contract.

## Ownership and GPU scope

`SetEyeTextures` accepts live `ID3D11Texture2D` pointers. The producer must own a valid reference during the call. Arbitrary addresses and already freed COM pointers cannot be validated safely by this ABI. The setter immediately AddRefs each changed pointer, then retains superseded references until a render callback releases them. Repeating the same pair does not change its revision. A changed pair invalidates a previously waited pose. This prevents a queued callback from using a resource freed by the producer's release.

D3D11 immediate-context calls, bridge-owned COM Releases, runtime waits and runtime submits occur on the render thread. The bridge rejects null or identical eye textures, different devices, mismatched dimensions/formats, non-render-target resources, multisampling, arrays/multiple mips and unsupported formats. The initial supported formats are the RGBA8/BGRA8 UNORM, SRGB and TYPELESS variants. A removed device is reported; the bridge does not recreate it.

Both Unity eye command streams precede the submit callback. Flush dispatches commands and is not a GPU completion fence. The bridge retains the POC's implicit OpenVR handoff through the next `WaitGetPoses`. It does not call `PostPresentHandoff` before Unity's desktop Present. These constraints come from the [pinned OpenVR interface](../vendor/openvr.h) and still need an actual Unity/OpenVR run to verify their runtime behavior.

Drain acknowledges release of the bridge's CPU references and rejection of later work in that generation. It does not establish that Unity, the compositor or the GPU has stopped using the images. The managed producer retains Unity RenderTextures after disable until a proven graphics lifetime mechanism is implemented. It must not destroy/reuse those targets or unload the DLL merely because drain returned.

Wait and Submit execute outside the producer-state mutex, so status polling and invalidation can continue if a runtime call blocks. A changed generation, frame or texture revision makes its returned result stale. Already dispatched runtime work cannot be retroactively canceled. A session controller can stop waiting after a bounded timeout, report an incomplete drain and require process exit. It cannot safely interrupt a blocked foreign runtime call, unload the DLL or claim GPU completion.

## Diagnostics

`GetNativeFrameDiagnostics(output, size)` copies version 1 of `NativeFrameDiagnostics` from [the native contract](../src/NativeFrameContract.h). It returns 0 for a null or undersized output buffer and 1 on success. The structure supplies size/version, generation/frame/texture revision, pose/submit/drain completions, successful paired submissions, rejected events, bridge status, wait error and each eye's Submit error. Callers must check the version. Individual getters remain for the managed producer. `GetSubmittedFrames()` counts only pairs where both runtime Submit calls returned zero.

Positive error values are OpenVR compositor errors. Negative bridge statuses are:

| Value | Meaning |
| --- | --- |
| -1 | No completed operation yet |
| -2 | Compositor unavailable |
| -10 | Invalid texture pair |
| -11 | Invalid or stale generation/frame/texture association |
| -12 | Repeated wait for an attempted frame |
| -13 | Disconnected, invalid or nonfinite HMD pose |
| -14 | Callback on a different render thread |
| -15 | D3D11 device removed |
| -16 | Owned references drained |
| -17 | Unknown render event |

Submit-after-consumption is rejected as stale. Completion identifiers are recorded on callback return, even when the operation was rejected. These identifiers are acknowledgments, not success flags. Use `GetNativeFrameDiagnostics` when a coherent snapshot of all fields is needed.

## Controlled checks and missing evidence

Run `scripts/Test-NativeFrameBridge.ps1`. It uses `Resolve-BuildTools.ps1`, the committed OpenVR pin, and the available x64 MSVC/Windows SDK. It compiles the shipping DLL, a separate runtime-fault DLL, and a client that loads the fault DLL and exercises its exported ABI with real D3D11 WARP textures. No SteamVR process, game or machine-wide runtime setting is touched.

Checks cover missing runtime, wait failure, invalid pose, null/mismatched/replaced textures, zero/old/drained generations, changed frames, repeated wait/submit, partial-eye failure, an update during a blocked wait, render-thread rejection, COM reference retention/release, empty/repeated drain and versioned diagnostic size negotiation. COM destruction is observed through an `IUnknown` marker attached to the real textures. Runtime faults are compiled only into the fault DLL; those control exports are absent from the shipping DLL.

The test does not validate compositor/GPU execution, device removal in a real Unity device, actual mirror pixels, color/UV correctness, a runtime restart, Unity camera command ordering, or the physical 3 to 5 minute gameplay gate. OpenVR/D3D11 recovery and safe graphics-resource destruction remain follow-up work. No OpenXR or D3D12 support is claimed by this ticket.
