# Bounded E-key receiver diagnostic

This opt-in diagnostic observes the game's public Unity InputSystem keyboard state for four seconds. It reads E only. It sends no input, invokes no game actions and installs no hooks. It runs in ordinary Unity `Update`, independently of VR enabled state, tracking pauses, camera selection and eye submission.

Enable `Diagnostics.CaptureImages=true` before launch and use a controller session. Then atomically create `UnityVR-evidence/input-probe-request` inside that session directory with this exact UTF-8 text:

```ini
duration_seconds=4
```

The injector consumes the request once and writes `input-probe-result-1.yaml`, then numbered results up to 4. Invalid requests also consume a slot. Requests cannot choose another key, duration, field or method. Input reads occur only while `Application.isFocused` is true. Missing InputSystem support produces an `unavailable` result and leaves VR running. A fifth request remains untouched. Existing result and claim markers preserve the limit if the component restarts during the same run.

The receiver accepts only the inspected `Unity.InputSystem` assembly with version `1.4.1.0`, no public key token and module identifier `eb31f34a-975f-46d1-9e8a-f468772f04b5`. It resolves the exact public properties `Keyboard.current`, `Keyboard.eKey`, `InputDevice.enabled`, and `ButtonControl.isPressed`, `wasPressedThisFrame`, `wasReleasedThisFrame`. `eKey` returns `KeyControl`, which inherits `ButtonControl`. The diagnostic resolves no game assembly or private getter. Other assembly versions and builds remain unavailable for this diagnostic.

Each private YAML result records run ID, PID, probe number, UTC window, monotonic clock frequency/start/deadline/end, observed duration and frame counts. Device presence/enabled and down/press/release are aggregates from focused frames. Focus fields show whether any, all or the last sampled frame was focused. Completion occurs at the first Update at or after the deadline, without another key read. A suspended player can therefore finish late; the explicit ticks and frame count expose that gap. Cancellation on teardown or loss of capture opt-in writes a partial result and drops the receiver references.

Every result retains `input_receipt: unknown`. Observing E in public engine state does not prove the game's input routing or interaction accepted it. The diagnostic polls only the focused game's public engine state. It captures no screenshots of its own and no arbitrary fields. Results remain session-local alongside other explicitly enabled evidence.
