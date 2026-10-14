# Steam Frame physical transport probes

These probes ran on 2026-10-13 using test copies of Firewatch (Unity 2017.4.40f1 Mono) and Outer Wilds (Unity 2019.4.39f1 Mono). They establish a real wireless runtime path and identify remaining functional defects. They do not complete [S1: pass physical VR gameplay in Firewatch and Outer Wilds](https://github.com/JakeGreen2145/UGEVR/issues/30).

## Same-distribution transport result

Both games used the unchanged `UGEVR-development-2ac21b1` payload. Its ZIP SHA-256 was `23bf5b98e9c78c2f70eb615c505a9f879962dcf6e45ff141e8b7de4cd1cd8ad9`.

| Observation | Result and limit |
| --- | --- |
| Headset connection | Steam Frame connected to Windows SteamVR through the wireless driver. A source-resolved Steam client call started the stream; a request alone was not accepted as readiness. |
| Synthetic pose forwarding | A distinctive position and yaw/pitch/roll override on the headset matched an independent Windows OpenVR client's 3x4 pose matrix to float precision. This tests forwarding, not physical sensors. |
| Game identity | Each injector reported engine/runtime readiness and paired eye submissions. Windows compositor focus and renderer process IDs matched the launched game. |
| Headset image | Runtime screenshots showed the Firewatch title background and Outer Wilds logo/starfield after dashboard dismissal. Screenshots are runtime output, not photographs through the lenses. |
| Camera transitions | Firewatch selected title, loading and narrative cameras. Outer Wilds reached its player camera after creating a test profile and starting an expedition. |
| Session control | Firewatch acknowledged recenter, disable and enable. Rendering resumed after enable. Native reference draining was logged; this does not prove GPU completion. |
| Cleanup | Owned game processes stopped, deployment receipts restored their files and explicit diagnostic configuration changes were restored. Testbench overrides ended. The owned stream association was removed and an independent later Windows probe found no running SteamVR server. |

## Defects found through physical testing

Firewatch's screen-overlay title and narrative UI did not appear in the original eye renders. Runtime diagnostics identified active root overlay canvases and their camera/layer context. [S1: preserve screen UI in the streamed stereo view](https://github.com/JakeGreen2145/UGEVR/issues/32) tracks the adapter. The first opt-in prototype (`52f2212`) initialized and submitted 121 pairs before detecting a RectTransform restoration conflict and pausing VR. Its game deployment, diagnostic configuration, pose lease and owned stream were restored. That prototype is not a validated fidelity remedy.

Further probes identified an anchored/local-position float round trip and a one-bit quaternion normalization difference. The adapter now restores anchored positions as the canonical value and records narrow allowances for derived-position and near-unit quaternion roundoff. A common head-centered plane replaces separate per-eye layout. Captures record its geometry before and after each camera render, with restoration conflicts kept separate from permitted numerical differences.

Steam Frame's independent runtime projection matrix also exposed a reversed vertical asymmetry. The corrected mapping follows [Valve's documented reversed raw top/bottom names](https://github.com/ValveSoftware/openvr/blob/master/docs/Driver_API_Documentation.md). The symmetric simulated headset had hidden this error. Distribution `051b182` was probed with an explicitly enlarged 3.6 m UI panel at 2 m distance. Its Firewatch menu items were readable in headset runtime captures; both eyes retained identical panel geometry through their renders, and the captured pair reported no restoration conflict. The original desktop menu advanced into the narrative scene, where text remained blurred in eye and headset images. The UI currently enters the game camera's image-effect pass, unlike original screen overlays. A separate pass after image effects is being investigated. This remains partial UI validation.

Outer Wilds reached its wake-up prompt, but short automated keyboard taps did not advance it. Remote game consumption remains unverified. No gameplay-input pass is inferred from a proxy accepting an input request.

A later source-backed held-E experiment issued one down/up pair through the headset Steam client, with release armed before down and completed after 500 ms. The game identity, focused window, compositor renderer and owned stream were checked separately. The wake-up prompt remained unchanged. A focused desktop tap also failed with VR rendering disabled. This candidate does not establish a remote gameplay-input route; it was kept out of the production Testbench provider.

A manually managed pose lease expired during an earlier Outer Wilds inspection and caused an invalid-pose pause. Subsequent probes used a bounded background lease renewer. The injector now has controlled tests for bounded pose reacquisition, but physical disconnect/recovery validation remains outstanding.

## Remaining gate

Use one final distribution for both games, demonstrate three qualified fresh launches per game, and record 3–5 minutes of actual gameplay per game with readable UI, effective controls, camera/scene transitions, six-axis tracking, recenter, desktop behavior, disable/enable and restoration. Idle menus, title scenes, synthetic-only pose checks and package tests do not substitute for that gate.

Raw screenshots, logs, game assemblies and machine/headset identifiers remain local in ignored evidence directories. The separate [Frame Testbench draft](https://github.com/JakeGreen2145/frame-testbench/pull/1) provides test orchestration; it is not a UGEVR runtime dependency.
