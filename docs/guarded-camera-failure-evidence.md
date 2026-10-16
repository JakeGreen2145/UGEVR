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
non-overwriting move. The temporary basename is 24 random hexadecimal characters plus `.tmp`, in the same directory. It is shorter than both fixed evidence destination names and does not append to a long destination. Cleanup removes a temporary file only after this writer successfully opened it with `CreateNew`; an existing temporary-name collision is preserved. Path reparse points, collisions, and oversized records
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
not identify a foreign game callback. The explicit guarded mode also recognizes an exact already-restored final state;
this does not identify who restored it.

## First physical publication attempt

An automated Firewatch startup on October 16 used clean source `69ab26f` and
distribution `0.2.0-dev.guarded-failure-inspect`, ZIP SHA-256
`d2ab6ca5e0d9f20444b5881ecd7fba16bd825d001b1a960026909fb65685a683`.
Both initial owned rendering gates passed. At the first passive follow-up,
observed at 17:17:18 UTC, rendering was inactive at 114 submitted pairs with
`owned_eye_position_changed`. The complete original log also recorded
`GUARDED_CAMERA_FAILURE_EVIDENCE diagnostic_query_or_serialization_failure`.
The most recent camera selection named LoadingCamera; its failing instance
identity and actual guard operands were not retained.

The trial stopped before GUI, save loading, controller input, active observers,
recordings or capture requests. Owned cleanup exited zero and confirmed process,
deployment, configuration, stream and lease cleanup. The bounded collector then
confirmed owned-process absence and found zero exact-run failure files. Root
verified all 84 frozen artifact sizes and hashes. No gameplay time qualified.

This is a known failure of diagnostic publication on that physical route as well
as a repeated camera-restoration refusal. The generic warning does not identify
the failed serialization or filesystem operation. The expected destination was
254 characters and its temporary name 291 characters, but those lengths alone
do not establish the cause. The camera policy remains unchanged while offline
reproduction investigates publication under the shipped Mono runtime.

When publication fails, the existing `LastError` category remains unchanged.
The guarded warning adds fixed `stage` and `exception_kind` labels only while
that category is nonempty. Stages distinguish cached serialization, strict UTF-8
encoding, the byte bound, owner-thread admission, destination/temporary path
construction, directory checks/creation, temporary creation/write/close and the
final move. They identify the operation in progress, not an inferred cause.
Final temporary-file deletion failures cannot replace the publication failure label. Write and stream-disposal exceptions retain the original `using` precedence, with the stage identifying the exception that propagates. A successful
publication clears the category and both labels.

Exception kinds come from a fixed System exception whitelist, including
`isolated_storage`, `path_too_long`, `io_exception`, `unauthorized_access`,
`encoder_fallback`, argument failures and runtime member/type failures. All other
exceptions become `other_exception`. No message, filesystem path, arbitrary
runtime type name or inner exception enters these labels.

A controlled host carrying Firewatch's legacy manifest reproduced an embedded
Mono `IsolatedStorageException` for the old appended temporary path at 291
characters, while the 254-character destination was writable. This establishes
a controlled path failure, not the exception from the earlier physical run.
The short-name publisher and exact cached serializer are checked separately
under that host before release. Camera ownership/restoration predicates and
capture flags/limits are unchanged.
Before UTF-8 conversion, primitive UTF-16 code-unit checks reject lone high or
low surrogates with a fixed `EncoderFallbackException`. Supported legacy Mono
can otherwise encode an unpaired surrogate despite requesting a throwing UTF-8
encoder. Valid BMP text and valid surrogate pairs retain their expected bytes.
This preserves the strict encoding contract across runtimes and does not
attribute the physical failure to Unicode.


## Exact already-restored completion

In explicit `guarded-local` mode, `already_restored_no_write` is a separate
successful outcome. It requires a valid finite owned-eye record and complete
finite saved/current snapshots, unchanged camera/transform/parent references and
IDs, and exact saved world and local position/rotation. Camera local/lossy scales,
both camera matrices and every saved parent-context component must also match
exactly. The current snapshot is the existing pre-disposal capture. No new engine
query or tolerance is introduced for this decision.

This outcome skips all camera transform setters. Other borrowed camera fields
still receive their existing independent restoration attempts. The existing
post-restoration capture must again satisfy the full exact saved-state predicate.
A setter/query fault or post-setter state change refuses completion without a
corrective transform write. Diagnostics retain the original owned-eye comparison,
including `owned_position_equal: false` and short-circuited rotation when applicable;
`applied_space` identifies why that mismatch no longer requires a transform write.

A native startup record showed exactly equal saved/pre-disposal/post-disposal
camera and parent snapshots after overlay unwind while the last owned eye differed.
That is final-state evidence. It does not prove writer identity or rendered-eye
correctness. World defaults, explicit world restoration, local-with-world-fallback
and ordinary guarded owned-eye restoration retain their existing behavior. Any
near miss, unavailable context or arbitrary different foreign pose continues to
refuse the new outcome. Physical validation of the correction is separate.

## UI preparation before eye assignment

The injector now prepares the common UI panel inside the existing borrowed
camera and overlay scopes before assigning either eye pose. Previously the first
`ApplyEye` performed that placement after the first eye assignment. Moving an
admitted UI ancestor could therefore move the selected camera before its render.
Per-eye clipping and layout observations still happen after eye assignment, and
the overlay-then-camera cleanup order is unchanged.

Controlled render callbacks check the applied world pose and copied UI view for
both eyes, including the combined guarded no-write path. These checks do not
reproduce native Unity layout or establish physical image correctness. A camera
that is itself a panel root still moves that panel when its eye pose is assigned;
independent UI placement for that case remains unresolved.

## Physical startup follow-up

The 2026-10-16 follow-up used distribution
`0.2.0-dev.guarded-ready-state`, source
`619a322f056cc0055db659f1cc60a86b20231303`, with the explicit guarded-local,
overlay-capture and no-input configuration. The launch receipt and all recorded
status samples were active, fresh and rendering, with empty injector and
initialization errors. The original log records Main Camera, LoadingCamera,
then Main Camera again. The last admitted passive status reported 1,254 submitted
pairs. The previous guarded restoration refusal did not recur in those observations.
Root independently verified all 83 retained artifacts and read the complete log.

The last passive sample ended 23.886268 seconds after startup admission. A local
observation-end bookkeeping assertion later refused and prevented its shell
command from dispatching cleanup. The one actual cleanup began 40.854309 seconds
after admission and completed with exit 0. No additional game observation or SDK
phase followed the last passive sample. The guardian reserve remained available;
no deadline was extended. The timing deviation is retained separately, and neither
a full 30-second observation nor early cleanup dispatch is claimed.

Owned game, stream and headset cleanup was verified; the original config and
save metadata were unchanged, without reading save contents. No input, GUI action,
capture request or recording was admitted. Zero failure files were collected,
which means no retained diagnostic rather than proof of no possible failure.
This supports the configured startup path through the camera transition. It does
not prove native stereo/UI image correctness or the 3–5 minute gameplay gate.
