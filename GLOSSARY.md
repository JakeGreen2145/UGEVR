# UGEVR

UGEVR adds VR capabilities to Unity games that were built without them. Its capability target uses UEVR's observable user and adaptation-author outcomes.

## Language

**Capability target**:
The set of observable VR and authoring outcomes UGEVR aims to provide, each with evidence limits and an acceptance criterion.
_Avoid_: Claims of universal compatibility

**Compatibility cell**:
A particular combination of game build, Unity version, scripting runtime, render pipeline, graphics API and VR runtime under which a capability can be evaluated.
_Avoid_: An undifferentiated supported game count

**Game profile**:
Saved configuration and object selections for adapting a particular game to VR.
_Avoid_: A guarantee that every feature works in that game

**Game adaptation**:
Game-specific configuration or extension behavior that supplies capabilities the generic injector cannot infer reliably.
_Avoid_: Treating visual attachment as gameplay interaction

**Generic VR core**:
The VR capabilities that operate across unrelated Unity games without game-specific gameplay code, including stereo rendering and 6DoF head tracking.
_Avoid_: A collection of independent game-specific VR mods

**Functional compatibility**:
A specified set of VR capabilities works in a tested compatibility cell. It is recorded separately from the performance achieved in that cell.
_Avoid_: Native-refresh performance

**Runtime validation**:
An observed capability result in a named game/build and VR setup, evaluated against a stated acceptance criterion.
_Avoid_: Static source confirmation

**Engine adapter**:
The part of UGEVR that provides engine access for a particular Unity player family through the common engine-access contract.
_Avoid_: A game-specific gameplay adaptation

**Synchronized sequential stereo**:
Two eye views rendered in sequence for one VR frame from the same gameplay state, with a consistent pose association.
_Avoid_: Alternating-eye stereo

**Alternating-eye stereo**:
A compatibility mode that updates the eyes on alternating render frames, with separately tracked image and pose times.
_Avoid_: A synchronized eye pair

**Fidelity fallback**:
An explicit change to the game's visual effects or rendering behavior that makes a declared VR capability work, with the resulting quality change recorded.
_Avoid_: A silent rendering defect
