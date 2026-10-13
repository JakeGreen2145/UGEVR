using System;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;
using Valve.VR;

var initError = EVRInitError.None;
var vr = OpenVR.Init(ref initError, EVRApplicationType.VRApplication_Background);
if (vr == null || initError != EVRInitError.None) {
    Console.WriteLine(JsonSerializer.Serialize(new { initError = initError.ToString() }));
    Environment.Exit(1);
}
try {
    string Property(ETrackedDeviceProperty property) {
        var value = new StringBuilder(256); var error = ETrackedPropertyError.TrackedProp_Success;
        vr.GetStringTrackedDeviceProperty(0, property, value, 256, ref error);
        return value.ToString();
    }
    var poses = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];
    vr.GetDeviceToAbsoluteTrackingPose(ETrackingUniverseOrigin.TrackingUniverseStanding, 0, poses);
    var p = poses[0]; var m = p.mDeviceToAbsoluteTracking;
    uint width=0, height=0; vr.GetRecommendedRenderTargetSize(ref width, ref height);
    var eyeL = vr.GetEyeToHeadTransform(EVREye.Eye_Left);
    var eyeR = vr.GetEyeToHeadTransform(EVREye.Eye_Right);
    var compositor = OpenVR.Compositor;
    var timing = new Compositor_FrameTiming(); timing.m_nSize = (uint)Marshal.SizeOf(typeof(Compositor_FrameTiming));
    bool timingValid = compositor != null && compositor.GetFrameTiming(ref timing, 0);
    var fade = compositor?.GetCurrentFadeColor(false);
    Console.WriteLine(JsonSerializer.Serialize(new {
        timestamp = DateTime.UtcNow, serial = Property(ETrackedDeviceProperty.Prop_SerialNumber_String),
        model = Property(ETrackedDeviceProperty.Prop_ModelNumber_String), valid=p.bPoseIsValid,
        connected=p.bDeviceIsConnected, trackingResult=p.eTrackingResult.ToString(),
        width, height, eyeOffsetX=new[]{eyeL.m3,eyeR.m3},
        focusProcess=compositor?.GetCurrentSceneFocusProcess(), lastRenderer=compositor?.GetLastFrameRenderer(),
        canRender=compositor?.CanRenderScene(), fullscreen=compositor?.IsFullscreen(),
        gridAlpha=compositor?.GetCurrentGridAlpha(), fade=fade.HasValue ? new[]{fade.Value.r,fade.Value.g,fade.Value.b,fade.Value.a} : null,
        timingValid, timing = new { timing.m_nFrameIndex,timing.m_nNumFramePresents,timing.m_nNumDroppedFrames,timing.m_nNumMisPresented,timing.m_flTotalRenderGpuMs,timing.m_flClientFrameIntervalMs },
        transform = new[]{m.m0,m.m1,m.m2,m.m3,m.m4,m.m5,m.m6,m.m7,m.m8,m.m9,m.m10,m.m11}
    }));
} finally { OpenVR.Shutdown(); }
