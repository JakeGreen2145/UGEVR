# Explicit after-effects UI isolation layer

The experimental `UI.OverlayAfterImageEffects` path isolates selected screen overlay UI into a layer for its separate UI render. `UI.OverlayIsolationLayer=-1` retains the existing default, which requires an unused unnamed layer in indices 8 through 31. A failure to find one does not prove every layer is occupied.

An explicitly selected value from 8 through 31 may borrow an unused named layer. The loaded-scene occupancy check includes inactive GameObjects and excludes invalid or unloaded asset scenes. An occupied layer is always rejected, including when the occupied object belongs to the selected UI. The injector never renames a layer or edits project assets. This setting takes effect on the next launch.

When selection fails and `Diagnostics.CaptureImages=true`, a controller session can receive `UnityVR-evidence/ui-layer-selection-failure-1.yaml`, up to four numbered files per run. Each private result contains run/PID identity, the requested layer, scan completeness, used mask and exactly 32 layer names and occupancy counts. Layer names are bounded to 128 characters; the reason is bounded to 512, with control characters normalized. It contains no scene object names. An incomplete scan cannot establish that a zero-count layer is unused. No numeric candidate is automatically selected from the diagnostic.

Selection and every exclusivity query inspect at most 100,000 objects. Borrowed objects remain bounded to 8,192. Before and after rendering, the injector checks for unowned loaded-scene objects on the isolated layer, including objects with no renderer. It also checks during layer restoration and after the final desktop rebuild. Any intrusion fails the pass or records a restoration conflict before submission. Restoration returns owned layers to their saved values, preserves newer game-owned changes and leaves unowned objects untouched.

This is an explicit fidelity fallback. Its game-specific rendering, callback effects and performance still need validation on the physical gameplay gate.
