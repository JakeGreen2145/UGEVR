# Input delivery in shipped Unity players

This report resolves [Which input delivery mechanisms can reach shipped Unity players without duplicating events?](https://github.com/JakeGreen2145/UGEVR/issues/20), a prerequisite for [How should controller input, bindings, gamepad coexistence, and haptics reach Unity games?](https://github.com/JakeGreen2145/UGEVR/issues/10). It establishes mechanisms and limits, leaving architecture and product choices to that decision.

Research date: 2026-10-13. Evidence is official documentation and inspected source, not runtime validation. No drivers were installed, runtime settings changed, game launched, or physical controller tested.

## Source boundary

The inspected Input System release is **1.7.0**, commit `e29989176f3a8473ba30854e4bfae953083eba28`. Its installation guide requires Unity 2019.4+ and .NET 4, so this package cannot be presumed usable in the existing Firewatch Unity 2017/.NET 3.5 baseline. Legacy bindings are UnityCsReference 2022.3 commit `91471827ee2cab002d877b827d67d120c09ba9d5`. UEVR is commit `4ee5c6b6162dee2291fc75f9dfc57667f6d45a2d`, consistent with the existing [interaction inventory](https://github.com/JakeGreen2145/UGEVR/blob/59018468d5bf160ae88fb79103a77903e7d6c70b/docs/research/uevr-interaction.md). These source versions do not establish compatibility with every version of a shipped player. [Input System installation][installation]

## Mechanisms and what each reaches

| Mechanism | Established contract | Limit for an injector |
|---|---|---|
| Legacy `UnityEngine.Input` | Named axes/buttons, key and mouse polling, edge queries, joystick names and global input reset. | The inspected public surface has no arbitrary gamepad/axis state setter. Getters cross native bindings. Intercepting managed getters, internal calls, native implementations or game abstractions is adaptation, not a stock injection API. [Legacy bindings][legacy] |
| Input System virtual device | `AddDevice("Gamepad")` or `AddDevice<Gamepad>()`, queued device state, discovery notifications and action resolution. | This is a device in the game's Input System instance. It does not register an OS controller or populate Legacy Input. The game may restrict bindings to paired devices/control schemes. [Device creation][adddevice], [device lifecycle][devices], [pairing][playerinput] |
| XInput interception | Replace/augment responses to polling for user slots 0–3; emulate connection through successful state queries. | Only callers reaching the hooked API are covered. No coverage follows for DirectInput, HID, GameInput, keyboard-only logic or arbitrary game abstractions. DLL/version and discovery paths must be observed. [Microsoft state contract][xget], [API comparison][comparison], [UEVR hooks][xhook] |
| Game-specific input adapter | Feed a discovered game-facing abstraction rather than an emulated hardware device. | Public Unity contracts do not define universal action names or gameplay semantics. Such adapters require evidence about that game and must avoid feeding the same action through another active route. This is an engineering inference from the separate contracts above. |

Legacy `GetAxis`, `GetAxisRaw`, `GetButton`, `GetButtonDown` and `GetButtonUp` forward to native entry points; key-code and mouse paths differ from named-input paths. The source also declares unmanaged entry points for Burst callers. A patch of one managed wrapper therefore does not prove all reads are intercepted. The pinned reference supplies declarations, not the proprietary native engine implementation. `ResetInputAxes` is a global reset, not a method to release only injector-owned input. [Legacy bindings][legacy]

Input System requires the package **and** an enabled native input backend; the project setting can enable Legacy, Input System or both. Copying a managed package into an arbitrary player does not prove that native entry points and player-loop integration exist. The normal runtime calls native allocation, queueing, updates, discovery and commands. [Installation][installation], [native runtime bridge][nativeruntime]

Mono/IL2CPP availability must be probed separately: assembly/type presence, retained methods, native backend, layouts, event processing and the game's actual consumers. Unity stripping can remove unused code; IL2CPP adds AOT limitations, including ungenerated generic instantiations. Prefer a retained non-generic entry point where useful, but its existence alone does not recover missing native code or layouts. This conclusion combines the source dependency with Unity's [stripping documentation][stripping] and [scripting restrictions][aot]. The earlier [engine access report](https://github.com/JakeGreen2145/UGEVR/blob/5176cbd1554bb703479af8451b1452d51e7dff28/docs/research/unity-engine-access.md) describes wrapper/native-code distinctions; no new IL2CPP test was performed here.

## Events, update phase and discovery

Queued input is device state, not a direct call to a gameplay action callback. `QueueStateEvent` copies a complete state record; `QueueDeltaStateEvent` changes a control's part of state. State formats must match the chosen layout. `GamepadState` is suitable for the standard virtual Gamepad, not automatically for a hardware-specific layout. Replacing the whole state of a real controller can overwrite unrelated physical controls. [Queueing][queue], [Gamepad state formats][gamepadstate]

The event queue is consumed by the game's configured input updates. Dynamic input runs before `MonoBehaviour.Update`; fixed processing uses its timestamp window. `onBeforeUpdate` permits queued state to participate in the upcoming update. Explicit `InputSystem.Update()` advances the entire input system by an additional frame and normally should not be called outside tests or manual-update operation. Consequently, stereo eye rendering must not drive additional input updates. Events queued during callbacks can be processed in the same update, so feedback loops need bounded handling. [Queue behavior][queue], [update contract][update]

Device addition is distinct from reaching the local player. `PlayerInput` pairs devices, selects control schemes and maintains player-specific action assets; global `Gamepad.current` alone is not a local-player ownership rule. Existing bindings can filter devices, groups and control schemes. Device connect/disconnect, scene changes and player replacement therefore require rediscovery of the actual consumer. [PlayerInput source][playerinput], [binding resolution][bindingresolution]

There are three different remapping layers: runtime controller bindings produce logical VR actions; UGEVR would translate those into game-facing controls; game Input System binding overrides change which controls drive existing actions. `ApplyBindingOverride` and JSON save/load exist for the last layer, but require the actual action instance and do not invent a gameplay action or automatically preserve the game's own saved overrides. Restore only changes owned by the adapter. [Binding overrides][overrides], [override persistence][overridepersistence]

## Analog values, coexistence and duplicate delivery

XInput carries digital button bits, independent 0–255 triggers and signed thumb axes. Its packet number signals state changes, so a spoofed implementation needs coherent repeated polling and transition behavior. Thresholding triggers is a policy, not an API requirement. UEVR's inspected controller route writes trigger values as **0 or 255**, while adding/clamping sticks; proportional triggers are possible in the transport but not demonstrated by this UEVR path. [XInput state record][xstate], [gamepad fields][xgamepad], [UEVR translation][uevrinput]

Unity's standard Gamepad state has float sticks and triggers; the game's processors, deadzones and interactions still apply afterward. An injector-level deadzone plus game deadzone can change behavior. Input System documents explicit background polling for Windows XInput controllers, distinct from processing queued events in the gameplay loop. [Gamepad state][gamepadstate], [polling][polling]

Duplicate prevention cannot be inferred from event IDs alone: one physical action can enter through XInput and an independently queued virtual device, then reach separate game consumers. Microsoft also documents XInput devices appearing in DirectInput enumeration and needing exclusion when both APIs are consumed. Input System bindings may feed a control into one action multiple ways. Enabling both Unity backends makes parallel consumers possible; it does not deduplicate them. [API comparison][comparison], [binding resolution][bindingresolution], [installation][installation]

**Engineering implication, not a selected architecture:** tag the originating logical action and select one delivery owner per game/player/control. Observe reads and action traces before claiming automatic selection. Changing owners needs defined neutral/cancellation behavior, no stale queued events, and no inadvertent second player. Physical gamepad coexistence needs an explicit replace/merge/priority rule; adding axes by default can saturate and combining buttons can hold them down. UEVR has activity-based state clearing, which is evidence of one policy rather than proof of universal coexistence. [UEVR activity clearing][uevrclear]

## Focus, release and haptic feedback

Input System focus behavior depends on application background execution, device capability and settings. A reset cancels actions; queueing neutral state can instead perform a release-triggered action. Disabled devices ignore ordinary input. On focus restoration, devices are synced or reset. A virtual device can handle background-capability and sync commands, but must honestly supply current state; reporting sync success without state is not proof of correct recovery. Changes to the game's background settings require restoration and should not be silently global. [Device reset/focus behavior][devices], [virtual command examples][virtualcommands]

XInput `SetState` is vibration output, **not** a way to set buttons or axes. Two unsigned motor speeds represent different low/high-frequency actuators. Unity `Gamepad.SetMotorSpeeds` produces a `DualMotorRumbleCommand`; public `onDeviceCommand` callbacks can intercept and handle that command before the native runtime, making an in-process virtual-device feedback route source-supported. Filter by owned device and command; do not consume unrelated hardware output. [XInput output][xset], [motor fields][xvibration], [Unity rumble command][rumble], [command interception][commands]

UEVR maps the selected XInput slot's motor requests to left/right runtime haptic pulses. This approximates two rumble motors with hand actuators; it does not preserve actuator physics or establish support for every game's feedback API. Its inspected route emits positive-amplitude 0.1-second pulses; do not copy that behavior as proof of complete zero/stop semantics. Haptic amplitude/frequency/duration, hand mapping, session focus and stop on device loss need separate runtime and physical checks. [UEVR rumble conversion][uevrrumble]

## Small falsifiable probes for implementation

These probes are proposed, not run. Start with a controlled originally non-VR player, then named unrelated games; a fixture alone cannot prove commercial generalization.

1. **Retained API probe:** record exact engine/package/backend versions; create an owned virtual Gamepad using retained APIs, feed a known state, read controls and an existing game action, remove it. Separate missing method/layout/native backend/pairing failures. Repeat in Mono and stripped IL2CPP builds.
2. **Ordering probe:** send press/hold/release and analog ramp traces across Dynamic, Fixed and manual input modes. Record sample IDs, event/update IDs and game-action transitions. Assert stereo rendering introduces no extra input frame, double press or lost release.
3. **Discovery probe:** observe named-axis consumers, Input System device/action pairing and native XInput calls independently. A successful intercepted `GetState` does not pass unless the game's own consumer acts and reports the intended local player.
4. **Duplicate probe:** enable both Unity backends and instrument two consumers; expose an XInput route and a virtual device for one action. Demonstrate the duplicate risk, then enable one owner and require one game transition. Repeat with physical gamepad and external remapping enabled.
5. **Ownership/focus probe:** hold an input while opening injector UI, losing focus, changing delivery owner, disconnecting the controller, replacing the player and disabling injection. Verify no stuck state, unintended release-triggered action or leaked synthetic device; restore game settings and leave physical devices usable.
6. **Feedback probe:** send known motor amplitudes including zero, pause/resume and repeated commands. Trace routing to the selected hand once, stop on focus/device/session loss, and preserve non-owned controller output. A trace proves software routing; physical headset/controller observation is required for actuator behavior.

## References

[installation]: https://github.com/Unity-Technologies/InputSystem/blob/e29989176f3a8473ba30854e4bfae953083eba28/Packages/com.unity.inputsystem/Documentation~/Installation.md#L10-L40
[legacy]: https://github.com/Unity-Technologies/UnityCsReference/blob/91471827ee2cab002d877b827d67d120c09ba9d5/Modules/InputLegacy/Input.bindings.cs#L273-L400
[adddevice]: https://github.com/Unity-Technologies/InputSystem/blob/e29989176f3a8473ba30854e4bfae953083eba28/Packages/com.unity.inputsystem/InputSystem/InputSystem.cs#L1390-L1535
[devices]: https://github.com/Unity-Technologies/InputSystem/blob/e29989176f3a8473ba30854e4bfae953083eba28/Packages/com.unity.inputsystem/Documentation~/Devices.md#L115-L190
[playerinput]: https://github.com/Unity-Technologies/InputSystem/blob/e29989176f3a8473ba30854e4bfae953083eba28/Packages/com.unity.inputsystem/InputSystem/Plugins/PlayerInput/PlayerInput.cs#L158-L300
[xget]: https://learn.microsoft.com/en-us/windows/win32/api/xinput/nf-xinput-xinputgetstate
[comparison]: https://learn.microsoft.com/en-us/windows/win32/xinput/xinput-and-directinput
[xhook]: https://github.com/praydog/UEVR/blob/4ee5c6b6162dee2291fc75f9dfc57667f6d45a2d/src/hooks/XInputHook.cpp#L82-L271
[nativeruntime]: https://github.com/Unity-Technologies/InputSystem/blob/e29989176f3a8473ba30854e4bfae953083eba28/Packages/com.unity.inputsystem/InputSystem/NativeInputRuntime.cs#L19-L120
[stripping]: https://docs.unity3d.com/2022.3/Documentation/Manual/ManagedCodeStripping.html
[aot]: https://docs.unity3d.com/2022.3/Documentation/Manual/ScriptingRestrictions.html
[queue]: https://github.com/Unity-Technologies/InputSystem/blob/e29989176f3a8473ba30854e4bfae953083eba28/Packages/com.unity.inputsystem/InputSystem/InputSystem.cs#L2470-L2722
[gamepadstate]: https://github.com/Unity-Technologies/InputSystem/blob/e29989176f3a8473ba30854e4bfae953083eba28/Packages/com.unity.inputsystem/InputSystem/Devices/Gamepad.cs#L16-L179
[update]: https://github.com/Unity-Technologies/InputSystem/blob/e29989176f3a8473ba30854e4bfae953083eba28/Packages/com.unity.inputsystem/InputSystem/InputSystem.cs#L2796-L2858
[bindingresolution]: https://github.com/Unity-Technologies/InputSystem/blob/e29989176f3a8473ba30854e4bfae953083eba28/Packages/com.unity.inputsystem/Documentation~/ActionBindings.md#L759-L783
[overrides]: https://github.com/Unity-Technologies/InputSystem/blob/e29989176f3a8473ba30854e4bfae953083eba28/Packages/com.unity.inputsystem/Documentation~/ActionBindings.md#L440-L484
[overridepersistence]: https://github.com/Unity-Technologies/InputSystem/blob/e29989176f3a8473ba30854e4bfae953083eba28/Packages/com.unity.inputsystem/Documentation~/ActionBindings.md#L660-L678
[xstate]: https://learn.microsoft.com/en-us/windows/win32/api/xinput/ns-xinput-xinput_state
[xgamepad]: https://learn.microsoft.com/en-us/windows/win32/api/xinput/ns-xinput-xinput_gamepad
[uevrinput]: https://github.com/praydog/UEVR/blob/4ee5c6b6162dee2291fc75f9dfc57667f6d45a2d/src/mods/VR.cpp#L774-L876
[polling]: https://github.com/Unity-Technologies/InputSystem/blob/e29989176f3a8473ba30854e4bfae953083eba28/Packages/com.unity.inputsystem/Documentation~/Gamepad.md#L103-L116
[uevrclear]: https://github.com/praydog/UEVR/blob/4ee5c6b6162dee2291fc75f9dfc57667f6d45a2d/src/mods/VR.cpp#L689-L771
[virtualcommands]: https://github.com/Unity-Technologies/InputSystem/blob/e29989176f3a8473ba30854e4bfae953083eba28/Packages/com.unity.inputsystem/Documentation~/Devices.md#L591-L625
[xset]: https://learn.microsoft.com/en-us/windows/win32/api/xinput/nf-xinput-xinputsetstate
[xvibration]: https://learn.microsoft.com/en-us/windows/win32/api/xinput/ns-xinput-xinput_vibration
[rumble]: https://github.com/Unity-Technologies/InputSystem/blob/e29989176f3a8473ba30854e4bfae953083eba28/Packages/com.unity.inputsystem/InputSystem/Devices/Haptics/DualMotorRumble.cs#L93-L120
[commands]: https://github.com/Unity-Technologies/InputSystem/blob/e29989176f3a8473ba30854e4bfae953083eba28/Packages/com.unity.inputsystem/InputSystem/InputSystem.cs#L1156-L1189
[uevrrumble]: https://github.com/praydog/UEVR/blob/4ee5c6b6162dee2291fc75f9dfc57667f6d45a2d/src/mods/VR.cpp#L1033-L1053
