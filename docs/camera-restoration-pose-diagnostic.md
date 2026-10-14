# Camera restoration pose diagnostic

`Diagnostics.CaptureCameraRestorationPoseDetails = true` explicitly enables private passive pose evidence. It defaults to false, requires `CaptureImages` and a controller session, and observes only a `capture-request` that already exists before the eye pair. A request arriving later can retain the existing image-capture behavior, but has no pose record for that pair. The diagnostic neither consumes the request nor changes image admission.

Each launch permits at most eight records named `camera-restoration-pose-N.yaml` in its controller-owned `UnityVR-evidence` directory. Existing files count toward that bound. Each record has at most eight stages and a 32 KiB UTF-8 limit. Serialization completes before file creation, and publication uses a fresh temporary file and non-overwriting rename. Output collisions preserve the existing file. Path ancestors must have no reparse points. These checks assume the private directory remains stable; they do not make filesystem operations atomic against another writer.

The stages follow the existing engine path:

1. `anchor_before_scope`, before overlay scope construction. World position and rotation use the exact earlier base anchor used by the existing aggregate comparison; local pose and parent are fresh sequential reads at this boundary.
2. `camera_state_saved`, using the exact world position and rotation saved by `CameraState`, before its matrix inspection. Local pose and parent identity are separate sequential public reads at that point.
3. `after_left_render` and `after_right_render`, following each eye's game/UI render and existing UI observations.
4. `after_camera_state_dispose`, after all normal camera restoration writes return and before the outer canvas rebuild/flush. Failed construction or disposal has no such stage.
5. `after_canvas_flush`, after the existing optional restored-geometry rebuild returns. With no overlay scope this is a boundary observation with no canvas flush.
6. `after_current_context`, after the existing current-camera scope disposal returns.
7. `after_eye_captures`, in a finally block around image capture. Separate attempt and normal-return fields distinguish a failed first image from two normally returned captures.

Each stage records public camera/transform/parent instance identities, exact stored world/local positions and quaternion components, per-array finiteness flags, and a bounded query-error category. Finite numbers use round-trip formatting; NaN and infinity are quoted and flagged. Values are never normalized or substituted. A missing or destroyed camera remains unavailable. A getter failure preserves any already-returned fields and leaves later fields unavailable. Parent absence is distinct from failed parent inspection. No object name, game getter or game callback is queried.

`existing_game_camera_restored` copies the unchanged aggregate after eye capture: Unity's existing world-position and quaternion equality against the earlier base anchor. Its tolerance and comparison phase are unchanged. An interrupted path retains null if that comparison did not run. Exact component differences in this record do not override that result, identify a cause, or authorize a restoration fix. Native frame ID, runtime generation and planned rendered-pair ordinal link the record to the existing `capture-N.json` and eye filenames. A failed pair may never reach that ordinal or queue submission; the record does not claim GPU completion. Unity API reads and stage observations are sequential, not an atomic pose snapshot.

Normal logs report only the bounded attempt count and sanitized diagnostic failure category. Pose and instance identities remain in private evidence. Use an ignored private controller session directory and sanitize any public export. Disabled diagnostics create no observer and add no transform/identity reads, evidence files or per-pair logs. This code makes no additional camera, parent, matrix or game-state writes. It cannot interrupt a blocked Unity API or image-capture operation, and a process exit before publication may leave no record.
