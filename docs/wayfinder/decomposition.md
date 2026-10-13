# UGEVR decomposition

The user selected the full UEVR capability target with staged delivery. The completed [Wayfinder map](https://github.com/JakeGreen2145/UGEVR/issues/1) indexes the accepted decisions. [UGEVR: full Unity VR injector capability target with staged delivery](https://github.com/JakeGreen2145/UGEVR/issues/24) is the published implementation specification. This document summarizes the decomposition and links its contracts; it does not claim that the target has been implemented.

The [feature target](feature-target.md) records individual capabilities, evidence, UGEVR gaps, and acceptance criteria. [Source provenance](source-provenance.md) fixes the audit revisions and evidence limits.

## Subsystems and decision outputs

| Subsystem | Responsibility | Decision output required before a specification | First proof required |
| --- | --- | --- | --- |
| Bootstrap and engine access | Enter a running or launching game; detect version, Mono/IL2CPP, pipeline, graphics API, and available facilities | Loader strategy, supported cells, engine API boundary, thread rules, ownership and teardown | Same distribution enters unrelated games in each selected runtime cell without rebuilding against their game code |
| Stereo and scene rendering | Produce two correct views without advancing gameplay twice; manage temporal history, effects, multiple cameras and view models | Rendering backend contract, pipeline-specific strategies, compatibility fallbacks, camera ownership | Moving scene, particles, effects and simulation timing agree between eyes; desktop and game state are restored |
| VR runtime and GPU transport | OpenVR/OpenXR session, view prediction, textures/swapchains, frame scheduling, depth and device recovery | Runtime abstraction, frame/pose association, graphics interop ownership and synchronization | Physical headset receives correctly oriented frames at its selected cadence, including resize and runtime state transitions |
| Camera and tracking composition | Compose game camera, HMD, eye offsets, scale, origin, cutscenes and comfort transforms | Coordinate spaces, pose order, recenter semantics, camera selection/presets and transition rules | Six-axis tracking and recentering behave correctly through gameplay, camera changes and a cutscene |
| Input and feedback | Read controller actions, map them to game input, handle gamepad coexistence, bindings and haptics | Input abstraction for legacy and newer systems; precedence, emulation, rebinding and feedback contracts | Menus and gameplay can be controlled without duplicate or stuck inputs; feedback reaches the intended controller |
| Locomotion and aim | Aim/movement orientation, turning, player displacement and collision-aware roomscale | Generic discovery limits, adapter requirements, movement ownership, collision and aim semantics | Turning and head/controller aim agree with game actions; roomscale movement stops at a wall rather than moving only the view through it |
| Game UI and injector menu | Capture/project game UI; compose flat/curved panels and interactive injector controls | Capture strategies for UI families, panel spaces, pointer mapping, focus and input capture | Main menu, HUD and pause menu remain readable; injector controls work in headset and on desktop |
| Object discovery and authoring | Inspect live objects, select stable targets, attach objects/cameras to tracked devices, change/hide properties | Mono/IL2CPP reflection boundary, stable selectors, lifetime handling, visual versus gameplay transforms, persistence | Attachment survives reload and object recreation; destroyed targets fail safely; declared gameplay effects are independently verified |
| Profiles and settings | Per-game/global state, overrides, presets, import/export and migration | Identity, schema, precedence, validation, versioning, packaging and reset rules | Export/import reproduces the setup; stale selectors and unavailable features produce useful diagnostics |
| Extensions and scripting | Native/managed plugin equivalents, scripts, hooks, inspection and author tools | Public ABI/API boundary, thread and lifetime rules, capabilities, error handling and script language choices | A game-specific adaptation can consume poses/input and change behavior through a stable API without patching core code |
| Frontend and lifecycle | Process discovery, runtime selection, launch/attach, configuration and conflict recovery | Attachment/install policy, process identity, preflight, permissions handling, coexistence and removal | A user can select a game, start VR, diagnose a failure and restore normal launch behavior |
| Diagnostics and compatibility controls | Expose evidence, timing, frame state, errors, rendering overrides and reproducible support reports | Capability reporting, telemetry schema, performance budget and fix policies | A failed session yields actionable data that distinguishes injection, tracking, rendering and compositor faults |

These boundaries separate engine access from VR runtime access, visual attachments from gameplay behavior, and UI composition from input routing. Their accepted contracts are in the specification and linked decision resolutions.

[How should UGEVR bootstrap and expose engine access across Mono and IL2CPP?](https://github.com/JakeGreen2145/UGEVR/issues/6) is resolved. Startup loading precedes late attachment. Managed engine adapters provide access where possible, with native access for missing capabilities. The ticket holds the common contract and required mechanism checks.

[Which Unity stereo strategies preserve simulation and temporal state across render pipelines?](https://github.com/JakeGreen2145/UGEVR/issues/7) is resolved. Synchronized sequential stereo comes first, with explicit fidelity fallbacks and opt-in alternating-eye stereo. Native stereo paths follow. The ticket holds the rendering contract and mechanism checks across pipelines.

[What frame and GPU contract should support OpenVR and OpenXR across D3D11 and D3D12?](https://github.com/JakeGreen2145/UGEVR/issues/8) is resolved. OpenVR/D3D11 precedes OpenXR/D3D11, then D3D12 for both runtimes. OpenXR initially receives copies from Unity-owned eye targets. The ticket holds frame ownership, bounded recovery and correlated host/headset evidence requirements.

[How should HMD tracking compose with game cameras, scale, presets, and cutscenes?](https://github.com/JakeGreen2145/UGEVR/issues/9) is resolved. The default uses the game camera plus live head tracking, removes game-driven pitch/roll, and keeps compatible cutscenes in VR with explicit screen fallbacks. The ticket holds anchor, metric scale, calibration and transition contracts.

[How should controller input, bindings, gamepad coexistence, and haptics reach Unity games?](https://github.com/JakeGreen2145/UGEVR/issues/10) is resolved. XInput delivery for observed consumers precedes Unity input adapters. The action model preserves analog triggers, and automatic source takeover waits for neutral controls. The ticket holds bindings, update ownership, cancellation and feedback contracts.

[Which aiming and collision-aware roomscale behaviors can generalize across Unity player controllers?](https://github.com/JakeGreen2145/UGEVR/issues/11) is resolved. Game aim and smooth turning are defaults, with optional tracked aim, snap turning and horizontal roomscale. CharacterController integration precedes custom/kinematic motors and dynamic bodies. The ticket holds movement authority, accepted-displacement reconciliation and head-contact fade requirements.

[How should UGEVR capture and interact with game UI and its own VR menu?](https://github.com/JakeGreen2145/UGEVR/issues/12) is resolved. The injector menu is fixed on open; captured screen UI follows the head. uGUI capture precedes Toolkit and IMGUI/custom paths, and injector pointing precedes game pointing. The ticket holds event/restoration contracts and explicit whole-game screen fallback requirements.

[How should object inspection and persistent tracked attachments work across Unity builds?](https://github.com/JakeGreen2145/UGEVR/issues/13) is resolved. Saved selectors require unique targets, visual attachments are the default, and inspector mutations are explicit author actions. The ticket holds lifetime, transform ownership, persistence and gameplay-evidence requirements.

[What profile schema and extension contracts should support shared Unity game adaptations?](https://github.com/JakeGreen2145/UGEVR/issues/14) is resolved. Formats and compatible syntax follow UEVR counterparts, with YAML for new package metadata. Layered personal overrides remain separate from shared profiles. Native/C# authoring precedes Lua, and imported code activation is explicit. The ticket holds schema, compatibility, ABI and lifecycle requirements.

[How should game discovery, launch or attach, and conflict recovery work for users?](https://github.com/JakeGreen2145/UGEVR/issues/15) is resolved. The frontend and CLI share UGEVR's session controller. Frame Testbench remains a separate test-time dependency that calls its external interface. Per-launch deployment is reversible, and conflict remedies require explicit selection. The ticket holds process identity, streamed launch, recovery and restoration requirements.

[Which compatibility controls and diagnostics should expose actionable failures and performance?](https://github.com/JakeGreen2145/UGEVR/issues/16) is resolved. Verified controls are explicit and reversible; bounded local logs are the default, with detailed artifacts enabled explicitly. Application, compositor and stream metrics remain separate, as do functional and performance results. The ticket holds command, timeline, bundle and fault-attribution contracts.

## Delivery stages

Stages define the accepted delivery order and measurable gates, not release dates. Foundational profiles, diagnostics and recovery start in the first stage and mature throughout. The specification governs stage dependencies and required evidence.

The [validation-gates decision](https://github.com/JakeGreen2145/UGEVR/issues/5) is resolved. The initial gate uses Firewatch and Outer Wilds, three fresh-launch checks, and 3–5 minutes of gameplay per game, with Steam Frame testbench automation and streamed-game launch support. It passes functional correctness first and reports timing separately. The later native-performance tier uses p95 application CPU/GPU timing within the selected display interval and fewer than 1% missed deadlines during steady gameplay. Broader coverage uses 24 available core combinations and two unrelated non-VR games per claimed backend/pipeline family. The ticket holds the complete policy and evidence requirements.

| Stage | Outcome | Candidate exit evidence |
| --- | --- | --- |
| S1: physical gameplay baseline | Turn the existing Mono/D3D11/OpenVR path into a reliable physical-headset gameplay session with usable menus and ordinary game controls | Recorded physical-headset runs in unrelated games, including gameplay, menus, camera changes and cutscenes; correct stereo/tracking and no accumulated state corruption |
| S2: broader engine and runtime foundation | Establish explicit compatibility cells for Mono/IL2CPP, Built-in/URP/HDRP, OpenVR/OpenXR and D3D11/D3D12 | Independent representative games and controlled scenes for each claimed cell; evidence identifies unsupported cells rather than implying universal support |
| S3: controller and UI experience | Add controller mapping, aim/turning, feedback, interactive VR menus, game UI placement and adapter-based roomscale where required | User can navigate and play; focus/input transitions are stable; collision, haptics and aiming are checked separately from visual tracking |
| S4: authoring and extension parity | Provide object inspection/attachments, first-person adaptations, persistent profiles, plugins and scripting equivalents | An adaptation author creates and shares a reproducible setup, including stable selectors and an extension using the public API |
| S5: advanced compatibility and release parity | Complete advanced projection/effect controls, optimized rendering paths, depth, spectator output, developer diagnostics and distribution polish | Every feature target row has its own result and scope; visual quality and performance are measured under defined scenes and hardware |

A feature can begin earlier when it unblocks a stage. For example, a UI capture path may be necessary in S1, and frame timing must be measured from S1. Later parity milestones add breadth and controls rather than delaying foundational correctness.

## What parity means here

UEVR-native features become equivalent Unity outcomes. Unreal's native stereo hook does not imply an identical Unity hook. UObjectHook becomes live Unity object inspection and tracked attachment. Unreal CVars become discoverable Unity/pipeline controls with explicit availability. Blueprint extension support becomes native/C# adaptation authoring followed by Lua through the accepted versioned extension contract.

The three UEVR rendering modes also encode different timing and temporal-history behavior. The existing pair of `Camera.Render()` calls is evidence for one sequential path. It is not evidence for native pipeline stereo, correct temporal effects, SRP support, or safe alternating rendering.

## Remaining work on this map

All decision tickets are resolved, the user confirmed the testing boundaries and `to-spec` published the specification. Game/version-specific mechanisms, stripping and pipeline failures, stream automation and measured performance remedies remain explicit staged implementation probes. They do not become supported capabilities until their acceptance evidence passes. A probe that exposes a new architectural choice must reopen that choice with evidence. The next implementation gate is physical gameplay in Firewatch and Outer Wilds.
