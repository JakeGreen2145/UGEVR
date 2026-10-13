# Steam Frame validation integration

The user selected 3–5 minutes of gameplay per game for the initial validation gate, accepted the proposed expansion policy, and identified [Frame Testbench](https://github.com/JakeGreen2145/frame-testbench) as the automation harness. Extending it to launch streamed games is part of the requested plan. This document records existing facilities and requirements; the linked decision tickets hold the selected contracts.

Frame Testbench remains a separate test-time dependency in its own repository. UGEVR's frontend and CLI share its production session controller, which works without Testbench installed or running. The Testbench extension calls that controller's versioned external interface and retains ownership of headset capture, input overrides and leases. Correlating a test run does not combine the projects or make the harness a UGEVR runtime dependency.

## Inspected baseline

Source snapshot: [`f999d3bb70de55efb6a01214108f08c6b5e4f776`](https://github.com/JakeGreen2145/frame-testbench/tree/f999d3bb70de55efb6a01214108f08c6b5e4f776).

| Facility | Source evidence | Limit |
| --- | --- | --- |
| Typed MCP control | [Tool definitions](https://github.com/JakeGreen2145/frame-testbench/blob/f999d3bb70de55efb6a01214108f08c6b5e4f776/frame_testbench/mcp_server.py) and [tool documentation](https://github.com/JakeGreen2145/frame-testbench/blob/f999d3bb70de55efb6a01214108f08c6b5e4f776/docs/mcp.md) expose status, poses, controller actions, worn state, capture, recording and sessions | No game launch, PC process attachment, streamed-session start or app-readiness tool is exposed in this snapshot |
| Real headset runtime with synthetic overrides | [Driver proxy](https://github.com/JakeGreen2145/frame-testbench/blob/f999d3bb70de55efb6a01214108f08c6b5e4f776/native/input_proxy.cpp) and [input state](https://github.com/JakeGreen2145/frame-testbench/blob/f999d3bb70de55efb6a01214108f08c6b5e4f776/native/input_state.cpp) wrap the existing HMD and add synthetic controller states | Source establishes the local override mechanism. Forwarding to the streamed Windows game needs a causal probe; it does not move physical cameras or prove optical/sensor behavior |
| Runtime observation | [Observer](https://github.com/JakeGreen2145/frame-testbench/blob/f999d3bb70de55efb6a01214108f08c6b5e4f776/native/observe.cpp) reports device poses, coordinate-space transforms, frame indices, presentation/drop data and render pose | Advancing headset compositor counters alone do not identify or prove a particular streamed Windows game |
| Capture and recording | [CLI](https://github.com/JakeGreen2145/frame-testbench/blob/f999d3bb70de55efb6a01214108f08c6b5e4f776/frame_testbench/cli.py), [recording](https://github.com/JakeGreen2145/frame-testbench/blob/f999d3bb70de55efb6a01214108f08c6b5e4f776/frame_testbench/recording.py) and [headset recorder](https://github.com/JakeGreen2145/frame-testbench/blob/f999d3bb70de55efb6a01214108f08c6b5e4f776/frame_testbench/headset_recording.py) produce artifacts and metadata | Record operations are bounded to 60 seconds; a 3–5 minute test needs an orchestrated timeline. Headset view is left-eye video; stereo screenshots are a separate observation and not full optical scanout |
| Sessions and restoration | [Session implementation](https://github.com/JakeGreen2145/frame-testbench/blob/f999d3bb70de55efb6a01214108f08c6b5e4f776/frame_testbench/session.py) and MCP shutdown cleanup restore overrides through bounded leases | The game/stream lifecycle extension must integrate with that existing ownership and cleanup rather than treat process exit as sufficient restoration |
| SSH transport | [Transport](https://github.com/JakeGreen2145/frame-testbench/blob/f999d3bb70de55efb6a01214108f08c6b5e4f776/frame_testbench/transport.py) targets a configured host and checkout | Headset-host SSH transport alone does not launch or inspect the Windows game host |

A local Windows Python import probe of this snapshot's CLI succeeded. It did not run an MCP server, connect to the headset, launch a game or validate streaming. No Frame Testbench MCP tools are currently exposed in this task's available tool inventory.

## Required extension outcomes

- Select and launch the intended streamed game on the Windows host, with the selected UGEVR/runtime configuration, and distinguish process launch from injector readiness, runtime readiness and stream readiness.
- Coordinate the host game and headset stream as one identifiable test run. Preserve host/game identity, injector build, profile, runtime and headset evidence in the result.
- Drive the existing deterministic pose/input operations through the streaming path and verify their effect in the intended game's rendered views.
- Collect host-side eye/submission and timing evidence alongside headset-side captures and runtime observations. Do not accept a desktop or compositor image merely because a file was successfully written.
- Run three minutes of gameplay by default, extending up to five to reach the agreed checks. Keep the agreed fresh-launch checks; do not reintroduce a 30-minute initial gameplay requirement.
- Compose bounded captures/recordings into a run timeline, renew owned test sessions as needed, and recover from launch/stream timeout, process exit or lost connectivity.
- End the run by restoring the injector/game state and releasing testbench overrides, with separately observable cleanup results on both hosts.
- Record whether input was overridden or physical. Only make claims for capabilities the evidence actually exercises; optical behavior, physical tracking, skeletal input and haptics need appropriate separate observations.

## Decisions that own the contracts

- [How should game discovery, launch or attach, and conflict recovery work for users?](https://github.com/JakeGreen2145/UGEVR/issues/15) holds the streamed launch/control contract and separate Testbench integration boundary.
- [What frame and GPU contract should support OpenVR and OpenXR across D3D11 and D3D12?](https://github.com/JakeGreen2145/UGEVR/issues/8) chooses host/runtime frame association and evidence transport.
- [Which compatibility controls and diagnostics should expose actionable failures and performance?](https://github.com/JakeGreen2145/UGEVR/issues/16) chooses the result/timeline schema, metrics and failure attribution.

These requirements must be included in the eventual implementation specification, with the cross-repository testbench extension made explicit. They are validation infrastructure requirements; they do not replace the full UGEVR capability target.
