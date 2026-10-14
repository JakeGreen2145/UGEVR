# Fixed E engine input experiment

This diagnostic tests whether a game consumes a synthetic E state queued through its already loaded Unity InputSystem. It does not test Steam streaming, headset input forwarding, or the original physical keyboard route. Its receipts always report `engine_synthetic: true`, `physical_forwarding: false`, and `gate_eligible: false`. Game wake and input receipt remain unknown. A successful experiment cannot pass the gameplay gate automatically.

The experiment defaults off. A per-launch configuration must explicitly enable both settings:

```ini
[Diagnostics]
CaptureImages = true
AllowEngineInputExperiment = true
```

The injector must have a valid controller session. Create `UnityVR-evidence/engine-input-experiment-request` inside that session directory with exactly this content:

```ini
key=E
duration_ms=500
```

This sentinel is separate from the read-only `input-probe-request`. No arbitrary key, duration, field, getter, or game action is accepted. The injector claims at most two requests per run, including refused or unavailable requests. Fixed claim and result files retain that limit across component restarts. A disabled experiment leaves the sentinel untouched.

## Device and API restrictions

The adapter uses only the already loaded `Unity.InputSystem` assembly with version `1.4.1.0`, no public key, and MVID `eb31f34a-975f-46d1-9e8a-f468772f04b5`. It verifies the public getter, constructor, and queue shapes and metadata tokens from the shipped Outer Wilds artifact. A missing or mismatched assembly makes the experiment unavailable and does not fail VR. It never loads another assembly or creates, enables, or replaces an input device.

Before attempting DOWN, the game must be focused and the existing keyboard must be added, enabled, and current. All keys must be neutral, including the public `imeSelected` bit. The adapter scans at most 256 controls, including IME. It calls only the pinned public getters and queues a typed `KeyboardState` for E. It never calls private game methods, input actions, or `InputSystem.Update`. The public queue processes events on a subsequent normal input update, as described by the [InputSystem 1.4.1 source](https://github.com/Unity-Technologies/InputSystem/blob/1.4.1/Packages/com.unity.inputsystem/InputSystem/InputSystem.cs).

At release, the adapter takes a fresh snapshot of current non-E held keys and IME on the same original device, then queues that state without E. It does not redirect to a new current keyboard. It attempts release even after focus loss if the original device remains added. Removed or unsupported devices cannot receive a guessed replacement operation. The public state constructor and the separate IME control are documented in the [Keyboard 1.4.1 source](https://github.com/Unity-Technologies/InputSystem/blob/1.4.1/Packages/com.unity.inputsystem/InputSystem/Devices/Keyboard.cs).

The queue contains a full device state snapshot. Physical events can occur between snapshot and processing, so this is not an atomic merge with concurrent physical input. A coincident physical E press is indistinguishable from the synthetic E state. The receipt records this ambiguity; it does not record other key names or characters.

## Timing, cleanup, and evidence

The observer runs in ordinary Unity Update even with VR disabled, tracking paused, or no selected camera. The requested hold is 500 ms after the DOWN queue returns successfully. Release depends on a later Update, so a stalled game can exceed that duration. Successful queue calls record live Stopwatch samples before and after the call. The receipt reports actual queue return ticks, queue hold duration, and overshoot. `runtime_applied_hold_ms` remains unknown because queue timing is not engine processing timing.

The observer polls E down, press, and release states separately from queue return success. It allows two seconds after the release deadline to observe cleanup. A neutral keyboard that never processed DOWN cannot fabricate a release acknowledgment. Release attempts are bounded to two. Teardown attempts release through the normal queue and cannot confirm a subsequent input update. If the DOWN call throws, its queue outcome is unknown and the injector still attempts release conservatively.

Results are atomically published as private session YAML files named `engine-input-experiment-result-1.yaml` and `engine-input-experiment-result-2.yaml`. Publication never replaces an existing artifact. Each result contains run and process identity, monotonic frequency and timestamps, UTC window, observed frame count, attempt and queue return fields, observed E edges, preservation count, and `cleanup_observed`. Missing, errored, cancelled, or unobserved cleanup is not successful cleanup. Receipt failure cannot cause a VR failure.

Controlled checks use a fake keyboard backend and do not execute the shipped game assembly. They cover opt-in, neutral and unavailable baselines, fixed schema, durable request limits, normal-update acknowledgment, current-state preservation, original-device release, focus loss, teardown, queue failure, and timing overshoot. Both shipping Unity reference sets must also compile before packaging.
