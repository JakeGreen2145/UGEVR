using System;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;
using Valve.VR;

ProbeOptions options;
try { options = ProbeOptions.Parse(args); }
catch (ArgumentException error) {
    Console.WriteLine(JsonSerializer.Serialize(new { error = error.Message }));
    Environment.ExitCode = 2;
    return;
}

var initError = EVRInitError.None;
var vr = OpenVR.Init(ref initError, EVRApplicationType.VRApplication_Background);
if (vr == null || initError != EVRInitError.None) {
    Console.WriteLine(JsonSerializer.Serialize(new { initError = initError.ToString() }));
    Environment.Exit(1);
}
try {
    if (options.Controllers) {
        var snapshot = ControllerProbe.Capture(new OpenVrControllerReadback(vr, OpenVR.Compositor), options.ExpectedScenePid.Value);
        Console.WriteLine(JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        Environment.ExitCode = snapshot.Status == "observed" ? 0 : 3;
        return;
    }
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
    float[] Matrix(HmdMatrix34_t value) => new[]{value.m0,value.m1,value.m2,value.m3,value.m4,value.m5,value.m6,value.m7,value.m8,value.m9,value.m10,value.m11};
    float[] ProjectionMatrix(HmdMatrix44_t value) => new[]{value.m0,value.m1,value.m2,value.m3,value.m4,value.m5,value.m6,value.m7,value.m8,value.m9,value.m10,value.m11,value.m12,value.m13,value.m14,value.m15};
    object Projection(EVREye eye) {
        float left=0,right=0,top=0,bottom=0;
        vr.GetProjectionRaw(eye,ref left,ref right,ref top,ref bottom);
        // These normalized near-plane bounds match Injector.Projection's Unity
        // Frustum(left*n,right*n,top*n,bottom*n,n,f) call. OpenVR's raw
        // top/bottom names are reversed; the values already describe -Y/+Y.
        // Actual clipping
        // and applied matrices are recorded by opt-in injector diagnostics.
        return new {eye=eye.ToString(),left,right,top,bottom,
            unityFrustumAtUnitNear=new[]{left,right,top,bottom},
            runtimeMatrixAtNear1Far2500=ProjectionMatrix(vr.GetProjectionMatrix(eye,1,2500)),
            convention="OpenVR tangents; Unity frustum ordered left,right,bottom,top at near=1"};
    }
    var compositor = OpenVR.Compositor;
    var timing = new Compositor_FrameTiming(); timing.m_nSize = (uint)Marshal.SizeOf(typeof(Compositor_FrameTiming));
    bool timingValid = compositor != null && compositor.GetFrameTiming(ref timing, 0);
    var fade = compositor?.GetCurrentFadeColor(false);
    Console.WriteLine(JsonSerializer.Serialize(new {
        timestamp = DateTime.UtcNow, serial = Property(ETrackedDeviceProperty.Prop_SerialNumber_String),
        model = Property(ETrackedDeviceProperty.Prop_ModelNumber_String), valid=p.bPoseIsValid,
        connected=p.bDeviceIsConnected, trackingResult=p.eTrackingResult.ToString(),
        width, height, eyeOffsetX=new[]{eyeL.m3,eyeR.m3},
        eyeToHead = new[]{Matrix(eyeL),Matrix(eyeR)},
        projectionRaw=new[]{Projection(EVREye.Eye_Left),Projection(EVREye.Eye_Right)},
        focusProcess=compositor?.GetCurrentSceneFocusProcess(), lastRenderer=compositor?.GetLastFrameRenderer(),
        canRender=compositor?.CanRenderScene(), fullscreen=compositor?.IsFullscreen(),
        gridAlpha=compositor?.GetCurrentGridAlpha(), fade=fade.HasValue ? new[]{fade.Value.r,fade.Value.g,fade.Value.b,fade.Value.a} : null,
        timingValid, timing = new { timing.m_nFrameIndex,timing.m_nNumFramePresents,timing.m_nNumDroppedFrames,timing.m_nNumMisPresented,timing.m_flTotalRenderGpuMs,timing.m_flClientFrameIntervalMs },
        transform = new[]{m.m0,m.m1,m.m2,m.m3,m.m4,m.m5,m.m6,m.m7,m.m8,m.m9,m.m10,m.m11}
    }));
} finally { OpenVR.Shutdown(); }
