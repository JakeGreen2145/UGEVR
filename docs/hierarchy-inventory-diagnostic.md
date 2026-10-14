# Scene hierarchy inventory comparison

`Diagnostics.CompareHierarchyInventory=false` controls an experimental read-only comparison. It requires `CaptureImages=true` and a valid controller session. It never changes the production UI layer inventory, collision checks or inspection limit. Ordinary launches perform no hierarchy comparison.

With both options enabled, atomically create `UnityVR-evidence/hierarchy-inventory-request` in the current session directory containing UTF-8 without BOM:

```text
compare=1
```

The next eligible eye pair consumes the request after camera selection and before any camera or UI state borrowing. An enabled desktop layout flush can already have completed. At most two requests are consumed per launch, including invalid or failed requests. Durable claim/result files preserve the limit across component restart. There is no automatic retry or periodic benchmark. These diagnostic frames can stall gameplay and do not qualify performance.

Each request writes a numbered `hierarchy-inventory-result-N.yaml` through an atomic, no-clobber rename. It records run/PID identity, game frame and timestamps. Path ancestors and requests must not cross reparse points. Collector/query/write failures remain diagnostic errors and do not authorize a different rendering path.

The reference path queries all GameObjects through `Resources.FindObjectsOfTypeAll` and keeps IDs only where `scene.IsValid()` and `scene.isLoaded` both hold. The candidate freshly enumerates `SceneManager.sceneCount` and `GetSceneAt`, calls `GetRootGameObjects()` for each valid loaded scene, then calls each root's `GetComponentsInChildren<Transform>(true)`. Transform-to-GameObject conversion, scene checks and ID collection are part of candidate inspection time. It imposes no active-object, renderer, hide-flag or save-flag exclusion.

The candidate also reads the already-owned injector GameObject's scene and adds it when distinct from the listed scenes. This tests the persistent-scene hypothesis without creating, moving or marking an object as persistent. `anchor_included_separately` records the observed inclusion. It does not by itself prove that this is the only scene absent from the public scene list. Deduplication uses public `Scene.Equals`/`GetHashCode`; recorded identities are labeled hashes. The inspected 2017.4 build exposes `Scene.handle` internally, so the diagnostic never reads that property or a private field.

Both paths inspect at most 1,000,000 returned entries. The candidate admits at most 256 listed scenes plus the distinct anchor scene, 1,000,000 root entries and 1,000,000 transform entries across its root queries. Repeated IDs are deduplicated and separately counted. A cap, query failure or unavailable anchor prevents an exact comparison. Unity still allocates complete native query result arrays before these managed bounds apply. This diagnostic does not bound native allocation.

For completed collections, set subtraction computes exact full missing/extra ID counts. The YAML serializes at most 64 IDs per direction and reports the remainder explicitly. Partial collections retain their counts but set `comparison_exact=false` and `difference_counts_complete=false`. `set_equal` only describes the two collected sets. It cannot establish future completeness or an atomic snapshot.

Collections run sequentially. Before/after signatures include listed scene identity hashes, validity/load state, root counts and the anchor scene state. A changed signature reports `scene_list_stable=false`; unchanged signatures cannot rule out object changes within a scene. No inventory is retained across requests or render callbacks. Every result records `sequential_only=true`, `production_guard_replaced=false` and `gate_eligible=false`.

Resource query/inspection ticks and candidate query/inspection ticks use `Stopwatch` with recorded frequency. Candidate query time includes scene enumeration, root queries and transform queries. The total collection time also includes scene signatures and difference calculation, but excludes result-file writing. Divide ticks by frequency to obtain seconds. Set construction and instance-ID reads add diagnostic work absent from some production scans, so these totals are not a controlled comparison of final guard implementations or a whole-frame/GPU measurement.

## What the sources establish

The shipped Firewatch Unity 2017.4.40f1 and Outer Wilds Unity 2019.4.39f1 CoreModule assemblies expose the array-returning root API, scene enumeration and generic inactive hierarchy API. REA decoded these selected signatures; its broader metadata report remains partial because some unrelated generic-list signatures are unsupported. Both game-reference builds compile the diagnostic. These checks establish API availability, not native enumeration completeness.

[Unity's scene-count documentation](https://docs.unity3d.com/2019.4/Documentation/ScriptReference/SceneManagement.SceneManager-sceneCount.html) describes currently loaded scenes. The [Scene source](https://raw.githubusercontent.com/Unity-Technologies/UnityCsReference/2019.4/Runtime/Export/SceneManager/Scene.cs) obtains roots through native enumeration. The [hierarchy API documentation](https://docs.unity3d.com/2019.4/Documentation/ScriptReference/GameObject.GetComponentsInChildren.html) provides the inactive-child option. The [Resources documentation](https://docs.unity3d.com/2019.4/Documentation/ScriptReference/Resources.FindObjectsOfTypeAll.html) includes disabled and internal objects.

Those sources do not establish that scene-root traversal plus one persistent anchor covers every valid loaded object during loading/unloading, hidden-object handling or native callback mutation. A physical equality result is evidence for that particular collection. Replacing production guards requires stronger completeness evidence across these cases and measured cost at each existing boundary.
