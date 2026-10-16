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

## First matched host images after the correction

A fresh automated Firewatch launch used the same distribution and configuration.
After a ten-second passive title-menu settling interval, one capture request
produced both 1399 by 1399 eye images, capture and camera diagnostics, and a
complete restoration record. Pair 594 joined native frame 610, generation 1.
The guarded result applied local restoration without refusal, and both actual
Unity position and rotation comparisons passed.

Root inspected both decoded images. Each shows the Firewatch logo, lookout scene
and Continue, New Game, Load Game, Settings, Special Features and Quit options.
Some white labels have limited contrast against clouds. Rectangular sky-edge
discontinuities and substantial eye-to-eye placement differences remain visible;
this observation does not establish their cause or correct binocular geometry.

The trial refused at a headset process-context query before dispatching its Frame
screenshot client. That query did not retain its failing stdout or stderr, so its
native cause remains unclassified. No headset image or delivery claim follows
from this run. Unconditional owned cleanup completed with exit 0, restoring the
deployment, baseline configuration, stream and lease. Save metadata was unchanged
and its contents remained unread. No gameplay or input was attempted. This is
host-image evidence for the title UI; headset validation and the gameplay gate
remain open.

## Frame title-image confirmation

A subsequent fresh run kept the same `619a322` injector distribution and selected
settings. One host request produced pair 580, native frame 597, generation 1,
with both 1399 by 1399 eye images and successful guarded local restoration.
One separate headset client then produced a 960 by 1080 preview and a 1920 by
1080 stereo screenshot. Root inspected both host eyes and both side-by-side
headset panels. Each headset panel shows the intended Firewatch lookout scene,
logo and title-menu options, with recognizable correspondence to the host images.
No dashboard, desktop or On-Screen Keyboard content is visible in these images.

The screenshot has right-edge title/menu cropping and limited white-text contrast
over clouds. Peripheral sky seams remain visible in the host images. This is
static intended-game delivery evidence for the configured title view, not a
complete menu-coverage, interaction or binocular-geometry pass. The headset API
supplies no shared host frame ID, so exact cross-host frame identity is unavailable.
The screenshot is cropped runtime output, not an observation through the lenses.

The separate capture helper needed a filesystem identity correction. A read-only,
nonexecuting file mapping reproduced pathname/fstat device 30 versus mapping device
29, with the same inode, size and before/after file hash. The revised helper keeps
the pathname/FD identity and compares the resolved SDK symbol's mapping against a
read-only reference mapping of that held FD. The successful native result records
both mapping devices as 29 while retaining pathname/FD device 30. This is a sampled
binding observation, not continuous integrity or full dependency attestation.
The injector package and its runtime library were unchanged.

The capture returned after SDK shutdown, and its acknowledged process was observed
absent before session end. Startup, image phase, owned cleanup and collection all
exited zero. Deployment, baseline configuration, stream and headset lease were
restored, and save metadata stayed unchanged without reading save contents. Root
verified all 119 retained artifact hashes and read the complete original log.
No input or gameplay was attempted; the 3–5 minute functional gate remains open.

## Loaded-world registration and baseline refusal

A fresh automated run kept the same `619a322` distribution. Ordinary desktop
controls loaded the existing Firewatch save before synthetic controllers were
connected. The original view showed the elevator interior, and the complete
injector log records LoadingCamera followed by ActualCamera, continuing paired
submissions and an acknowledged disable without a camera restoration refusal.
This run requested no host or headset images.

Combined controller registration passed with 300 headset-source samples and 240
Windows samples. Both hands had confirmed selected, valid neutral tails. The two
500 ms activation workers and both observers exited zero, with release and SDK
shutdown evidence. The final proxy snapshot was neutral with retained poses.
The game input profile remained disabled. Registration proves readiness of these
observation and activation paths, rather than game action consumption.

The next strict neutral baseline refused `passive_not_observed`. Its first
game-side sample changed the left controller's serial and model between the
pre-state and post-state identity reads. The following 236 samples reported
consistent identities. All 237 samples retained valid neutral state, stable
role/index context and successful axis metadata. The strict validator correctly
rejected the whole window. No row was dropped, and the cause of the native
identity transition is unproven. The recorder and subsequent windows never
started, so recorder coexistence remains unverified.

Owned cleanup exited zero in 5.156 seconds. Deployment, baseline configuration,
stream and lease restoration were confirmed; all four owned processes were
absent. Save metadata was unchanged and save contents remained unread. The later
cached-diagnostic collector exited 2 because its inherited startup-only policy
rejects registration artifacts. Its enclosing collection wrapper exited zero
after catching that failure. These are separate results; collection success is
not claimed. The eight exact owned cached-failure paths were checked separately
and were absent.

Root verified all 143 retained artifact hashes, the complete original log and
the raw identity transition. The preparation also passed 106 controlled checks,
including the new keeper/guardian deadline coverage and compatible private
cleanup helper. These checks do not replace the failed physical baseline.
No gameplay time is credited, and the functional gate remains open.
