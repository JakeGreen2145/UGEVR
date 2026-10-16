# Private guarded camera failure evidence

`Diagnostics.CaptureCameraRestorationPoseDetails=true` and `CaptureImages=true`,
an available controller-owned session, and `Rendering.CameraTransformRestoration=guarded-local`
enable automatic failure evidence. Defaults remain off. This evidence requires no
capture request and does not take an image or issue an engine query when a guard
fails. It does not alter camera selection, equality, restoration, submission, or
failure policy.

Coverage begins once `CameraLocalRestoration` exists. Earlier constructor
getter failures have no cached operands and are outside this collector's
coverage. The notification phase distinguishes constructor admission refusals
from guarded disposal; constructor refusals precede outer overlay cleanup.
Publication after cleanup unwind does not assert that every cleanup setter
succeeded.

The owned session's private `UnityVR-evidence` directory receives at most eight
`guarded-camera-failure-<run>-<attempt>.yaml` files per launch. An attempt, including
a serialization, byte-limit, or I/O failure, consumes capacity. Each whole UTF-8
file is limited to 32 KiB, published by same-directory temporary file and
non-overwriting move. Path reparse points, collisions, and oversized records
refuse publication; diagnostic errors do not replace the original guard refusal.

Each record associates the run, VR frame, resource generation, pair ordinal,
cached camera/transform/parent instance IDs, and original failure reason. The
saved, pre-guard, and post-restoration snapshots are the existing guard's raw
world/local pose, scale, matrices, and parent-context operands. Partial queries
retain their error and null fields. An unexecuted stage says `not_queried`.
Owned-eye evidence contains the world/local pose already sampled by
`RecordOwnedEyePose`. Owned-eye scale/matrices and separately sampled parent
context are unavailable; the evidence does not substitute saved values for them.

The existing cached pose-record format is included intact. Its
`changes_restoration_policy` field describes the selected guarded mode, rather
than a change made by this collector. Its `actual_comparison` is the separate
image-capture aggregate comparison, which this collector does not invoke.
Guard eligibility uses the original exact component comparisons and short
circuit. The final saved-world check retains Unity's original equality operators.
The collector does not recompute unexecuted predicate verdicts.

`OverlayCanvasScope.Dispose` runs before `CameraState.Dispose`. The collector
matches the guard's cached transform and direct-parent references against the
scope's already admitted rectangle inventory by object identity. It retains
their cached rectangle IDs and original values, counts actual attempted layout
and pose assignments, and separately counts the later final camera-rectangle
rebuild attempts. A counter increments immediately before its setter; an
attempt does not prove setter success. Separate returned counters increment only
after the actual setter returns; a return does not prove the value was retained.
Counter totals are observed at
publication after the outer cleanup, while pre/post-guard snapshots precede
the final rebuild. Higher ancestor overlap is unavailable. This evidence does
not query or enumerate another hierarchy.

Object identity, instance IDs and sampled context do not establish who wrote a
pose or exclude ABA changes. An overlap can support a controlled ownership
handover investigation; an error named `owned_eye_position_changed` alone does
not identify a foreign game callback. No already-restored/no-write remedy or
ownership relaxation is included.
