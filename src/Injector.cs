using System;
using System.Collections;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Globalization;
using System.Reflection;
using BepInEx;
using UnityEngine;
using Valve.VR;

[BepInPlugin("dev.codex.unityvrinjector", "Unity VR injector proof of concept", "0.1.0")]
public class Injector : BaseUnityPlugin
{
    [DllImport("UnityVRRenderBridge", CallingConvention = CallingConvention.Cdecl)] static extern void SetEyeTextures(IntPtr left, IntPtr right);
    [DllImport("UnityVRRenderBridge", CallingConvention = CallingConvention.Cdecl)] static extern IntPtr GetRenderEventFunc();
    [DllImport("UnityVRRenderBridge", CallingConvention = CallingConvention.Cdecl)] static extern ulong GetSubmittedFrames();
    [DllImport("UnityVRRenderBridge", CallingConvention = CallingConvention.Cdecl)] static extern int GetSubmitError(int eye);
    [DllImport("UnityVRRenderBridge", CallingConvention = CallingConvention.Cdecl)] static extern void RequestMirrorCapture(string path);
    [DllImport("UnityVRRenderBridge", CallingConvention = CallingConvention.Cdecl)] static extern int GetMirrorStatus();
    [DllImport("UnityVRRenderBridge", CallingConvention = CallingConvention.Cdecl)] static extern int GetRenderStage();
    CVRSystem system;
    CVRCompositor compositor;
    RenderTexture[] eyes = new RenderTexture[2];
    TrackedDevicePose_t[] poses = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];
    TrackedDevicePose_t[] gamePoses = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];
    IntPtr renderEvent;
    Vector3 center;
    Quaternion centerRotation = Quaternion.identity;
    bool recentered, active = true;
    int rendered;
    Camera lastCamera;
    string evidenceDir;
    float nextReport;
    volatile bool quitting;
    volatile string heartbeatStage = "boot";
    string hmdSerial;
    MethodInfo getKeyDown;
    int updateCount;
    void Awake()
    {
        evidenceDir = Path.Combine(Paths.GameRootPath, "UnityVR-evidence");
        Directory.CreateDirectory(evidenceDir);
        Logger.LogInfo("BOOT Unity=" + Application.unityVersion + " game=" + Application.productName + " graphics=" + SystemInfo.graphicsDeviceType);
        Application.runInBackground = true;
        var inputType = Type.GetType("UnityEngine.Input, UnityEngine.InputLegacyModule", false) ??
            Type.GetType("UnityEngine.Input, UnityEngine.InputModule", false) ?? Type.GetType("UnityEngine.Input, UnityEngine", false);
        if (inputType != null) getKeyDown = inputType.GetMethod("GetKeyDown", new[] { typeof(string) });
        StartCoroutine(Run());
    }
    IEnumerator Run()
    {
        EVRInitError error = EVRInitError.None;
        try {
            system = OpenVR.Init(ref error, EVRApplicationType.VRApplication_Scene);
            if (system == null || error != EVRInitError.None) { Logger.LogError("VR_INIT_FAILED " + error); yield break; }
            compositor = OpenVR.Compositor;
            var serial = new StringBuilder(256);
            var propertyError = ETrackedPropertyError.TrackedProp_Success;
            system.GetStringTrackedDeviceProperty(0, ETrackedDeviceProperty.Prop_SerialNumber_String, serial, 256, ref propertyError);
            Logger.LogInfo("HMD serial=" + serial + " propertyError=" + propertyError);
            hmdSerial = serial.ToString();
            compositor.SetTrackingSpace(ETrackingUniverseOrigin.TrackingUniverseStanding);
            uint width = 0, height = 0;
            system.GetRecommendedRenderTargetSize(ref width, ref height);
            // Cap POC GPU cost; preserve runtime-recommended aspect ratio.
            float factor = Math.Min(1f, 1400f / Math.Max(width, height));
            for (int i = 0; i < 2; ++i) {
                eyes[i] = new RenderTexture((int)(width * factor), (int)(height * factor), 24, RenderTextureFormat.ARGB32);
                eyes[i].antiAliasing = 1;
                eyes[i].Create();
            }
            SetEyeTextures(eyes[0].GetNativeTexturePtr(), eyes[1].GetNativeTexturePtr());
            renderEvent = GetRenderEventFunc();
            Logger.LogInfo("VR_READY resolution=" + eyes[0].width + "x" + eyes[0].height + " nativeTextures=" + eyes[0].GetNativeTexturePtr() + "," + eyes[1].GetNativeTexturePtr());
            var heartbeat = new Thread(() => {
                while (!quitting) {
                    try { File.AppendAllText(Path.Combine(evidenceDir, "heartbeat.log"), DateTime.UtcNow.ToString("o") + " managed=" + heartbeatStage + " renderStage=" + GetRenderStage() + " rendered=" + rendered + " submitted=" + GetSubmittedFrames() + Environment.NewLine); } catch { }
                    Thread.Sleep(2000);
                }
            });
            heartbeat.IsBackground = true; heartbeat.Start();
        } catch (Exception ex) { Logger.LogError(ex); yield break; }
        var end = new WaitForEndOfFrame();
        while (true) {
            heartbeatStage = "await-end-of-frame";
            yield return end;
            try { Tick(); } catch (Exception ex) { Logger.LogError(ex); active = false; }
        }
    }
    void Update() {
        Application.runInBackground = true;
        if (++updateCount <= 3) Logger.LogInfo("UPDATE frame=" + Time.frameCount + " focused=" + Application.isFocused + " active=" + gameObject.activeInHierarchy);
    }
    void Tick()
    {
        if (KeyPressed("f8")) active = !active;
        if (KeyPressed("f9")) recentered = false;
        if (!active) return;
        Camera camera = Camera.main;
        if (camera == null || !camera.enabled || camera.targetTexture != null) {
            foreach (var c in Camera.allCameras) if (c.enabled && c.targetTexture == null && !c.orthographic && (camera == null || c.depth > camera.depth)) camera = c;
        }
        if (camera == null || camera.targetTexture != null) return;
        if (camera != lastCamera) {
            lastCamera = camera;
            Logger.LogInfo("CAMERA selected=" + camera.name + " depth=" + camera.depth + " cullingMask=" + camera.cullingMask + " effects=" + camera.GetComponents<MonoBehaviour>().Length);
        }
        if (rendered < 30) Logger.LogInfo("STAGE " + rendered + " before poses");
        heartbeatStage = "wait-poses-render-thread";
        GL.IssuePluginEvent(renderEvent, 0);
        // Native texture queries synchronize Unity's render thread. OpenVR's
        // D3D11 WaitGetPoses timestamps the GPU and must run with Submit there.
        eyes[0].GetNativeTexturePtr();
        var poseResult = compositor.GetLastPoses(poses, gamePoses);
        heartbeatStage = "poses-returned";
        if (rendered < 30) Logger.LogInfo("STAGE " + rendered + " after poses " + poseResult);
        var hmd = poses[OpenVR.k_unTrackedDeviceIndex_Hmd];
        if (!hmd.bPoseIsValid) { if (Time.realtimeSinceStartup > nextReport) { Logger.LogWarning("POSE_INVALID " + poseResult); nextReport = Time.realtimeSinceStartup + 3; } return; }
        var head = ToUnity(hmd.mDeviceToAbsoluteTracking);
        var headPosition = (Vector3)head.GetColumn(3);
        var headRotation = Quaternion.LookRotation(head.GetColumn(2), head.GetColumn(1));
        if (!recentered) { center = headPosition; centerRotation = Quaternion.Euler(0, headRotation.eulerAngles.y, 0); recentered = true; }
        var basePosition = camera.transform.position;
        var baseRotation = camera.transform.rotation;
        var oldTarget = camera.targetTexture;
        var oldProjection = camera.projectionMatrix;
        var oldView = camera.worldToCameraMatrix;
        var oldRect = camera.rect;
        bool oldOcclusion = camera.useOcclusionCulling;
        Vector3[] eyePositions = new Vector3[2];
        Quaternion[] eyeRotations = new Quaternion[2];
        try {
            for (int i = 0; i < 2; ++i) {
                Matrix4x4 eye = ToUnity(system.GetEyeToHeadTransform((EVREye)i));
                Vector3 offset = Quaternion.Inverse(centerRotation) * (headPosition - center + headRotation * (Vector3)eye.GetColumn(3));
                camera.transform.position = basePosition + baseRotation * offset;
                camera.transform.rotation = baseRotation * Quaternion.Inverse(centerRotation) * headRotation;
                eyePositions[i] = camera.transform.position;
                eyeRotations[i] = camera.transform.rotation;
                camera.ResetWorldToCameraMatrix();
                camera.projectionMatrix = Projection((EVREye)i, Math.Max(.02f, camera.nearClipPlane), camera.farClipPlane);
                camera.useOcclusionCulling = false;
                camera.rect = new Rect(0, 0, 1, 1);
                camera.targetTexture = eyes[i];
                if (rendered < 30) Logger.LogInfo("STAGE " + rendered + " before render eye=" + i);
                heartbeatStage = "render-eye-" + i;
                camera.Render();
                heartbeatStage = "rendered-eye-" + i;
                if (rendered < 30) Logger.LogInfo("STAGE " + rendered + " after render eye=" + i);
            }
        } finally {
            camera.targetTexture = oldTarget;
            camera.transform.position = basePosition;
            camera.transform.rotation = baseRotation;
            camera.projectionMatrix = oldProjection;
            camera.worldToCameraMatrix = oldView;
            camera.rect = oldRect;
            camera.useOcclusionCulling = oldOcclusion;
        }
        heartbeatStage = "issue-plugin-event";
        // Rendering and image effects can recreate a RenderTexture's native
        // resource. Submit the current textures after rendering, not cached
        // pointers from initialization.
        var currentLeft = eyes[0].GetNativeTexturePtr();
        var currentRight = eyes[1].GetNativeTexturePtr();
        SetEyeTextures(currentLeft, currentRight);
        GL.IssuePluginEvent(renderEvent, 1);
        heartbeatStage = "plugin-event-issued";
        ++rendered;
        if (rendered == 120 || File.Exists(Path.Combine(evidenceDir, "capture-request"))) {
            File.Delete(Path.Combine(evidenceDir, "capture-request"));
            CaptureEye(0); CaptureEye(1);
            bool restored = camera.transform.position == basePosition && camera.transform.rotation == baseRotation && camera.targetTexture == oldTarget;
            File.WriteAllText(Path.Combine(evidenceDir, "capture-" + rendered + ".json"),
                "{\"frame\":" + rendered + ",\"hmdSerial\":\"" + hmdSerial + "\",\"headPosition\":" + VectorJson(headPosition) +
                ",\"headRotation\":" + QuaternionJson(headRotation) + ",\"basePosition\":" + VectorJson(basePosition) +
                ",\"baseRotation\":" + QuaternionJson(baseRotation) + ",\"eye0Position\":" + VectorJson(eyePositions[0]) +
                ",\"eye1Position\":" + VectorJson(eyePositions[1]) + ",\"eye0Rotation\":" + QuaternionJson(eyeRotations[0]) +
                ",\"eye1Rotation\":" + QuaternionJson(eyeRotations[1]) + ",\"gameCameraRestored\":" + restored.ToString().ToLowerInvariant() + "}");
            RequestMirrorCapture(Path.Combine(evidenceDir, "compositor-left-" + rendered + ".bmp"));
            GL.IssuePluginEvent(renderEvent, 2);
        }
        if (Time.realtimeSinceStartup > nextReport) {
            Logger.LogInfo("VR_FRAME rendered=" + rendered + " submitted=" + GetSubmittedFrames() + " errors=" + GetSubmitError(0) + "," + GetSubmitError(1) + " mirrorStatus=" + GetMirrorStatus() + " pose=" + headPosition + " headRotation=" + headRotation.eulerAngles + " base=" + basePosition + " camera=" + camera.name);
            nextReport = Time.realtimeSinceStartup + 3;
        }
    }
    Matrix4x4 Projection(EVREye eye, float near, float far) {
        float left = 0, right = 0, top = 0, bottom = 0;
        system.GetProjectionRaw(eye, ref left, ref right, ref top, ref bottom);
        // OpenVR top/bottom use downward-positive tangent coordinates.
        return Matrix4x4.Frustum(left * near, right * near, -bottom * near, -top * near, near, far);
    }
    static Matrix4x4 ToUnity(HmdMatrix34_t p) {
        // Change basis from right-handed OpenVR to left-handed Unity: S M S.
        Matrix4x4 m = Matrix4x4.identity;
        m.m00=p.m0; m.m01=p.m1; m.m02=-p.m2; m.m03=p.m3;
        m.m10=p.m4; m.m11=p.m5; m.m12=-p.m6; m.m13=p.m7;
        m.m20=-p.m8; m.m21=-p.m9; m.m22=p.m10; m.m23=-p.m11;
        return m;
    }
    void CaptureEye(int i) {
        var old = RenderTexture.active;
        RenderTexture.active = eyes[i];
        Texture2D image = new Texture2D(eyes[i].width, eyes[i].height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, image.width, image.height), 0, 0);
        image.Apply();
        File.WriteAllBytes(Path.Combine(evidenceDir, "eye-" + i + "-" + rendered + ".png"), image.EncodeToPNG());
        Destroy(image);
        RenderTexture.active = old;
    }
    static string Number(float value) { return value.ToString("R", CultureInfo.InvariantCulture); }
    bool KeyPressed(string key) {
        if (getKeyDown == null) return false;
        try { return (bool)getKeyDown.Invoke(null, new object[] { key }); }
        catch { getKeyDown = null; Logger.LogWarning("Legacy keyboard input is unavailable; VR rendering remains enabled."); return false; }
    }
    static string VectorJson(Vector3 v) { return "[" + Number(v.x) + "," + Number(v.y) + "," + Number(v.z) + "]"; }
    static string QuaternionJson(Quaternion q) { return "[" + Number(q.x) + "," + Number(q.y) + "," + Number(q.z) + "," + Number(q.w) + "]"; }
    void OnApplicationQuit() { quitting = true; if (system != null) OpenVR.Shutdown(); }
}
