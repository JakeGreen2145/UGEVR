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

**VR frame**:
An identifiable rendering and submission operation that associates its eye images with the views actually rendered and the intended display time or runtime prediction identity.
_Avoid_: A Unity gameplay frame or a texture pointer alone

**Tracking anchor**:
The game-space position and orientation relative to which UGEVR composes recentered head and eye motion.
_Avoid_: A player-body position or a headset tracking origin

**Tracking origin**:
The reference used to express calibrated head and controller motion before UGEVR maps it into the game world.
_Avoid_: A game camera or player-body position

**Virtual-screen presentation**:
The game's image presented on a screen in VR, with headset tracking controlling the view of that screen.
_Avoid_: Full world VR or a remedy for invalid tracking

**Active controller source**:
The selected source, VR controllers or a physical gamepad, whose actions UGEVR currently delivers to the game's controller input.
_Avoid_: A local-player identity or an input delivery mechanism

**Game input adapter**:
The part of UGEVR that delivers its logical actions to a particular game's input consumer and routes supported feedback from that consumer.
_Avoid_: A runtime controller binding or a visual controller attachment

**Roomscale body movement**:
Physical tracked movement applied to the identified player body through the game's movement and collision authority.
_Avoid_: A render-only camera offset

**Movement adapter**:
The part of UGEVR that supplies movement requests to an identified game movement owner and reports the movement that owner accepts.
_Avoid_: A collider type or an arbitrary Transform write

**Head contact**:
A verified overlap or contact between the tracked head's configured shape and blocking game geometry, evaluated separately from player-body collision.
_Avoid_: A successful body sweep alone

**Injector menu**:
UGEVR's configuration controls, available on desktop and in the headset through shared settings state, with placement independent of game UI.
_Avoid_: A captured game menu

**Game UI capture**:
An independently obtained image of an identified game UI source for VR composition, with its original coordinate domain and ownership retained.
_Avoid_: An opaque image of the whole game

**Target selector**:
A saved description used to resolve one intended game object or component through scene, hierarchy, type or declared role criteria.
_Avoid_: A saved runtime address or instance ID

**Visual attachment**:
A tracked pose override applied for rendering, with game-owned state restored before subsequent gameplay.
_Avoid_: A verified gameplay interaction

**Game-state attachment**:
An explicitly enabled tracked pose override applied through a declared game update, animation or physics owner, with gameplay effects evaluated separately.
_Avoid_: A visual attachment or automatic firing/collision support

**Profile package**:
A versioned portable bundle of shared game settings, target rules and explicitly identified supporting extensions, separate from personal overrides and activation state.
_Avoid_: A complete copy of a user's local configuration

**Personal game override**:
A user's saved changes applied above a shared game profile and below temporary session overrides.
_Avoid_: An edit to the shared profile package
