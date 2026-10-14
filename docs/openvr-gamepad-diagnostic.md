# Owned OpenVR-to-Input-System gamepad diagnostic

This source prototype tests one explicit mapping through normal Unity input updates. It is off by default. It does not establish received Windows controller buttons, original game binding consumption or a gameplay gate pass. The separate existing-keyboard E experiment remains a separate diagnostic and must be disabled for this path.

Use the existing BepInEx INI configuration:

```ini
[Input]
AllowOpenVrGamepadDiagnostic = true
DiagnosticProfile = outer-1.4.1-right-a-south-axis0-leftstick-v1
CurrentGamepadConflictPolicy = refuse
DiagnosticDurationSeconds = 15
DiagnosticDeadzone = 0.2
```

`prefer-owned` is the explicit alternative if another gamepad is current. `refuse` performs no layout registration or device creation in that case. Duration accepts 5 through 300 seconds; the window starts only after active rendered VR, game focus and a neutral valid right controller. One window is allowed per component lifetime. Focus loss, VR disable or failure, tracked-controller loss, identity/role change, observed packet regression and runtime generation change terminate the window. Retry requires a fresh launch. A held button cannot arm it.

The profile polls the existing `CVRSystem` in ordinary `Injector.Update`, independently of eye count. It initializes no SDK, manifest or action set. It uses `GetControllerStateWithPose`, the actual RightHand role, a valid connected pose, bounded serial identity and verified axis-0 joystick metadata. It rechecks the role after polling. SDK `k_EButton_A`, pressed bit 7, maps to gamepad South. Touched bits and the Index-controller button-2 alias do not substitute for bit 7. Explicit raw axis 0 maps to leftStick with a radial deadzone. Missing axis metadata or a different axis kind stays unavailable. Equal packet numbers can represent a held state; they do not prove age. Polling cannot observe a disconnect that happens completely between samples. Windows receipt of this legacy button and axis mapping requires a separate observer.

## Ownership and compatibility

The backend queues public `GamepadState` events only to a separately owned `Gamepad`. It never changes physical keyboard state, nonowned keys, IME, physical controller input, device enable state or game input actions. No Lua, native virtual-controller driver or Frame Testbench dependency is introduced.

The explicitly selected Outer compatibility profile accepts only the inspected game assembly MVID `995fa289-c662-4f44-9176-33f00ebf2052` and the loaded Input System 1.4.1 MVID `eb31f34a-975f-46d1-9e8a-f468772f04b5`. A missing/mismatched assembly makes input unavailable without failing VR or loading another assembly. This is a build-specific compatibility boundary, not a claim of generic Unity input support.

The profile registers a unique `XInputControllerWindowsUGEVR_<guid>` layout extending the public `Gamepad` layout. Its interface is a unique UGEVR identifier, its manufacturer is `UGEVR`, its product explicitly says synthetic diagnostic gamepad and its serial starts with `ugevr-synthetic-`. Capabilities are `{}`. It supplies no physical vendor/product IDs or physical serial. Public matcher JSON has no serial field, so the matcher uses the unique synthetic interface instead. Registration cannot match existing hardware through a broad XInput or HID interface.

The inspected original Outer code accepts device names starting with `XInputControllerWindows`. This compatibility rule is isolated in `OuterGamepadCompatibilityProfile`, rather than applied as a general binding default. It permits the honest synthetic device to use the game's standard gamepad compatibility branch. The native Windows XInput layout uses internal `XINP` state, which cannot simply accept public `GPAD` events. This prototype therefore uses a public derived Gamepad layout with GPAD state. It does not generate runtime code or access the private XInput state type.

Creation captures the exact owned device reference during the synchronous public Added callback and verifies the returned reference. This permits cleanup after an add call creates the device and then throws. A failure without a captured identity is reported as an unknown add outcome; it does not guess a device to delete. Queue calls that throw still trigger release attempts because they may already have queued input. Removal operates only on the captured reference.

The backend samples `Gamepad.current` before mutation and before nonneutral queues. An observed external takeover stops the window. After removal, the previous current is restored only through public `MakeCurrent` if it remains added and enabled, no takeover was observed and current is still null. An external current selected during removal is preserved. These are synchronous observation guards, not an atomic guarantee against all other scripts or input events. Already queued normal input can affect current selection before a later Update detects a conflict. Explicit `prefer-owned` accepts the diagnostic's normal current-device selection while active.

Release queues neutral state and normally waits for a later Update to observe neutral state on the owned device. Teardown and observed current conflicts attempt release and remove immediately. In those cases, queued neutral processing is not claimed. Removal unbinds actions from the removed device through the normal public API. Cleanup has at most two release attempts and two removal attempts. If the SDK or Unity stops returning, these deadlines cannot preempt its native call or run after process teardown.

Layout ownership includes the unique name, a recorded public layout definition, public layout-change observation and a bounded 256-device check. A collision is refused before registration. A replaced layout or any other device using it prevents deregistration. Such a layout is preserved and reported. Layout/device callbacks are unsubscribed after successful bounded cleanup. An unavailable cleanup remains explicit; no original device or global input state is cleared to compensate.

`INPUT_DIAGNOSTIC` log records show status/reason, creation and queue counts, observed owned state samples, release/removal attempts and cleanup/current restoration outcomes. Observed owned state is read through public unprocessed stick state; normal game processors can alter the values the game consumes. The queue itself is not a processing acknowledgement. All records keep `gameConsumption=unknown`.

## Source evidence and remaining test

The shipped Input System artifact has SHA256 `e5eb042f7a120f3135844519bf8e3114a81da4e3c35cddeceff528a13d6bdfd7`. REA metadata record `ev_7fb98fa575ac70da612842fb0f5386ecb05da3bf9e99b3db05f44113d65d8fc1` identifies public RegisterLayout JSON at `0x0600031b`, AddDevice description at `0x06000339`, RemoveDevice at `0x0600033b`, QueueStateEvent at `0x0600035d`, Gamepad.current at `0x06000469`, Gamepad.MakeCurrent at `0x0600046d`, public `ReadUnprocessedValue` at `0x060003c4` and GPAD state fields. Broader metadata coverage is partial for unrelated generic signatures; runtime validation checks the selected public shapes and pinned method/field tokens. Controlled tests bypass build-local tokens only for their fake public API fixture. Production factory identity checks remain enabled.

The shipped Outer game artifact has SHA256 `0ef96f7ff2c37ff95012bb881c760c28e0fa2f60731945c61b7cc6b30deace87`. REA record `ev_dc559f0480035bed921e319ee6bcbee7855102230994c489faa2c19a938449cf` plus complete read-only CIL decoding identify `InputUtil.IsXInputController` at `0x06000d1e` as a name-prefix test. `GetEnableDeviceWhitelistIds`, `0x06000d1c`, includes vendor 1118/product -1. `ProcessGamepadThroughWhitelist`, `0x06000f6f`, reads description capabilities, then selects that compatibility ID for the standard prefix before checking the whitelist. Otherwise it can call public DisableDevice. The prototype never re-enables a game-disabled device.

The game also exposes an input-asset device filter. `SetPairedDeviceExclusive`, `0x06000f77`, passes one device to its input manager; `InputCommandManager.ModifyInputAssetDeviceList`, `0x06000f2c`, disables the asset, sets its devices and re-enables it. Those methods are not called by the injector. A strict user/device filter can still exclude the added gamepad. No unknown InputUser is paired or reassigned. Root must first observe the legacy Windows button and axis, then owned device state and the original game's normal wake/movement response. Neither source compatibility nor a successful add proves that consumption.

Public API behavior is documented in the [Input System 1.4.1 source](https://github.com/Unity-Technologies/InputSystem/blob/1.4.1/Packages/com.unity.inputsystem/InputSystem/InputSystem.cs), [Gamepad source](https://github.com/Unity-Technologies/InputSystem/blob/1.4.1/Packages/com.unity.inputsystem/InputSystem/Devices/Gamepad.cs) and the pinned [OpenVR header](https://github.com/ValveSoftware/openvr/blob/master/headers/openvr.h). The first gate still requires observed original game response and cleanup, followed by its accepted 3 to 5 minutes of play.
