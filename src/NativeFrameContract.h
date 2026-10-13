#pragma once
#include <cstdint>

// Negative bridge results are distinct from positive OpenVR errors.
enum BridgeStatus : int {
    BridgeOk = 0, BridgeNotReady = -1, BridgeRuntimeUnavailable = -2,
    BridgeInvalidTextures = -10, BridgeStaleFrame = -11,
    BridgeDuplicateFrame = -12, BridgeInvalidPose = -13,
    BridgeWrongRenderThread = -14, BridgeDeviceRemoved = -15,
    BridgeDrained = -16, BridgeUnknownEvent = -17
};

struct NativeFrameDiagnostics {
    uint32_t size;
    uint32_t version;
    uint64_t generation;
    uint64_t frame;
    uint64_t textureRevision;
    uint64_t completedPoseGeneration;
    uint64_t completedPoseFrame;
    uint64_t completedSubmitGeneration;
    uint64_t completedSubmitFrame;
    uint64_t completedDrainGeneration;
    uint64_t submittedPairs;
    uint64_t rejectedEvents;
    int32_t status;
    int32_t poseWaitError;
    int32_t leftSubmitError;
    int32_t rightSubmitError;
};
