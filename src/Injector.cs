using System;
using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using BepInEx;
using UnityEngine;
using Valve.VR;
using Ugevr;

[BepInPlugin("dev.codex.unityvrinjector", "Unity VR injector proof of concept", "0.2.0")]
public class Injector : BaseUnityPlugin
{
    [DllImport("UnityVRRenderBridge", CallingConvention=CallingConvention.Cdecl)] static extern void SetEyeTextures(IntPtr left, IntPtr right);
    [DllImport("UnityVRRenderBridge", CallingConvention=CallingConvention.Cdecl)] static extern void SetFrameState(ulong generation, ulong frameId);
    [DllImport("UnityVRRenderBridge", CallingConvention=CallingConvention.Cdecl)] static extern IntPtr GetRenderEventFunc();
    [DllImport("UnityVRRenderBridge", CallingConvention=CallingConvention.Cdecl)] static extern ulong GetSubmittedFrames();
    [DllImport("UnityVRRenderBridge", CallingConvention=CallingConvention.Cdecl)] static extern int GetSubmitError(int eye);
    [DllImport("UnityVRRenderBridge", CallingConvention=CallingConvention.Cdecl)] static extern int GetPoseWaitError();
    [DllImport("UnityVRRenderBridge", CallingConvention=CallingConvention.Cdecl)] static extern ulong GetCompletedPoseFrame();
    [DllImport("UnityVRRenderBridge", CallingConvention=CallingConvention.Cdecl)] static extern ulong GetCompletedPoseGeneration();
    [DllImport("UnityVRRenderBridge", CallingConvention=CallingConvention.Cdecl)] static extern ulong GetCompletedSubmitFrame();
    [DllImport("UnityVRRenderBridge", CallingConvention=CallingConvention.Cdecl)] static extern ulong GetCompletedSubmitGeneration();
    [DllImport("UnityVRRenderBridge", CallingConvention=CallingConvention.Cdecl)] static extern int GetBridgeStatus();
    [DllImport("UnityVRRenderBridge", CallingConvention=CallingConvention.Cdecl)] static extern ulong GetCompletedDrainGeneration();
    [DllImport("UnityVRRenderBridge", CallingConvention=CallingConvention.Cdecl)] static extern void RequestMirrorCapture(string path);

    CVRSystem system;
    CVRCompositor compositor;
    readonly RenderTexture[] eyes=new RenderTexture[2];
    readonly TrackedDevicePose_t[] poses=new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];
    readonly TrackedDevicePose_t[] gamePoses=new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];
    readonly TrackingOrigin origin=new TrackingOrigin();
    readonly TrackingReacquisition tracking=new TrackingReacquisition(5);
    InjectorSession session;
    KeyboardEProbe inputProbe;
    HierarchyInventoryProbe hierarchyProbe;
    EngineEInputExperiment engineInputExperiment;
    OpenVrGamepadDiagnostic gamepadDiagnostic;
    double nextInputReport;
    SessionCommand pending;
    MethodInfo getKeyDown;
    IntPtr renderEvent;
    ulong generation=1, frameId;
    // 0 idle, 1 waiting for render-thread pose, 2 waiting for submit callback.
    int phase, updateCount, rendered;
    int uiFailureCaptures;
    bool active=true, runtimeReady, draining, renderingActive, quitting;
    bool ownsBackground, oldBackground;
    bool capturesEnabled, retainGameTilt, disableOcclusionCulling;
    bool allowEngineInputExperiment;
    bool captureOverlayCanvases,includeOverlayLayers;
    bool overlayAfterImageEffects;
    int overlayIsolationLayer=-1,layerFailureCaptures;
    int overlaySceneScanLimit=OverlayLayerInventory.DefaultScanLimit;
    bool captureLayoutInputs;
    bool captureNativeUiState;
    int nativeUiRendererLimit=NativeUiStateDiagnostics.DefaultRendererLimit;
    bool compareHierarchyInventory;
    bool flushDesktopLayoutBeforeBorrow;
    string layoutInputRootRectIds;
    Camera overlayCamera;
    double overlayPlaneMeters;
    double overlayWidthMeters;
    double unitsPerMeter;
    string evidenceDir, pendingRecenter, trackingDiagnostic;
    float phaseStarted, nextReport, lastSubmittedAt;
    Camera lastCamera;
    ulong submittedPairs;

    void Awake()
    {
        Logger.LogInfo("BOOT Unity="+Application.unityVersion+" game="+Application.productName+" graphics="+SystemInfo.graphicsDeviceType);
        try
        {
            session=new InjectorSession(Environment.GetEnvironmentVariable("UGEVR_SESSION_ID"),
                Environment.GetEnvironmentVariable("UGEVR_SESSION_DIRECTORY"),Process.GetCurrentProcess().Id);
            if (IntPtr.Size!=8 || SystemInfo.graphicsDeviceType.ToString()!="Direct3D11")
                throw new InvalidOperationException("This adapter requires Windows x64 D3D11. No native texture pointers were passed to the bridge. Select -force-d3d11 or use a supported adapter.");
            unitsPerMeter=Config.Bind("Tracking","UnitsPerMeter",1f,"Game world units per physical meter.").Value;
            retainGameTilt=Config.Bind("Tracking","RetainGamePitchAndRoll",false,"Keep game camera pitch and roll in the tracking anchor.").Value;
            capturesEnabled=Config.Bind("Diagnostics","CaptureImages",false,"Allow explicitly requested eye and compositor images.").Value;
            captureNativeUiState=Config.Bind("Diagnostics","CaptureNativeUiState",false,"Opt-in cached CanvasRenderer cull/clipping/alpha and render-context observations only for capture requests admitted before the eye pair. No native UI state is restored; requires CaptureImages.").Value && capturesEnabled;
            nativeUiRendererLimit=Config.Bind("Diagnostics","NativeUiRendererLimit",NativeUiStateDiagnostics.DefaultRendererLimit,"Explicit native UI diagnostic renderer bound 1024..8192; default 1024. Identity-query occurrences are bounded to eight times this limit. Validated only when CaptureNativeUiState and CaptureImages are enabled.").Value;
            nativeUiRendererLimit=NativeUiStateDiagnostics.ResolveRendererLimit(captureNativeUiState,capturesEnabled,nativeUiRendererLimit);
            compareHierarchyInventory=Config.Bind("Diagnostics","CompareHierarchyInventory",false,"Diagnostic only: permit at most two explicitly requested sequential Resources-versus-scene hierarchy comparisons before an eye pair. Requires CaptureImages and controller session identity; does not replace layer guards.").Value && capturesEnabled;
            allowEngineInputExperiment=Config.Bind("Diagnostics","AllowEngineInputExperiment",false,"Explicitly allow at most two fixed E 500ms synthetic engine keyboard experiments per session. Requires CaptureImages; this is not physical forwarding or gameplay gate evidence.").Value && capturesEnabled;
            bool allowGamepad=Config.Bind("Input","AllowOpenVrGamepadDiagnostic",false,"Explicit code activation for one bounded owned InputSystem gamepad experiment per launch. Never changes physical keyboard state; game consumption requires observation.").Value;
            string inputProfile=Config.Bind("Input","DiagnosticProfile","none","Select outer-1.4.1-right-a-south-axis0-leftstick-v1 explicitly. This is a test profile, not general game bindings.").Value;
            string inputConflict=Config.Bind("Input","CurrentGamepadConflictPolicy","refuse","refuse leaves an existing current gamepad untouched. prefer-owned explicitly permits the owned diagnostic pad to become current, with conditional restoration.").Value;
            float inputSeconds=Config.Bind("Input","DiagnosticDurationSeconds",15f,"Bounded 5..300 second diagnostic window, starting only with a neutral valid right controller and focused active VR.").Value;
            float inputDeadzone=Config.Bind("Input","DiagnosticDeadzone",0.2f,"Explicit radial left-stick deadzone, 0.1..0.8.").Value;
            if(allowGamepad)
            {
                try
                {
                    if(!session.Available || allowEngineInputExperiment || inputProfile!=OuterGamepadCompatibilityProfile.Name)
                        throw new InvalidOperationException("Select a valid session, the explicit diagnostic profile and disable the separate E experiment.");
                    gamepadDiagnostic=new OpenVrGamepadDiagnostic(OuterGamepadCompatibilityProfile.Create,inputSeconds,inputDeadzone,inputConflict);
                    Logger.LogWarning("EXPLICIT_INPUT_DIAGNOSTIC profile="+inputProfile+" currentConflict="+inputConflict+" duration="+inputSeconds+"; synthetic owned gamepad, game consumption unknown.");
                }
                catch(Exception inputError) {Logger.LogWarning("INPUT_DIAGNOSTIC unavailable: "+inputError.Message);}
            }
            captureLayoutInputs=Config.Bind("Diagnostics","CaptureLayoutInputs",false,"Opt-in bounded cached text/layout input snapshots for UI restoration mismatches. Requires CaptureImages; no preferred-size, generator or game getters are queried.").Value && capturesEnabled;
            layoutInputRootRectIds=Config.Bind("Diagnostics","LayoutInputRootRectIds","","Optional comma-separated runtime RectTransform IDs selecting diagnostic subtrees plus up to eight ancestors. Empty retains full coverage. Runtime IDs require observed name/parent validation each launch.").Value;
            disableOcclusionCulling=Config.Bind("Rendering","DisableOcclusionCulling",false,"Explicit fidelity remedy: disable the selected camera's occlusion culling only during eye rendering.").Value;
            captureOverlayCanvases=Config.Bind("UI","CaptureOverlayCanvases",false,"Experimental: borrow active screen overlay canvases during eye rendering and restore desktop state.").Value;
            flushDesktopLayoutBeforeBorrow=Config.Bind("UI","FlushDesktopLayoutBeforeBorrow",false,"Explicit experimental fallback: flush pending desktop canvas layout once before current camera selection and all state snapshots. Runs game canvas callbacks; requires CaptureOverlayCanvases.").Value;
            includeOverlayLayers=Config.Bind("UI","IncludeOverlayLayers",false,"Explicitly include selected overlay canvas renderer layers in the borrowed camera mask; may also reveal other geometry on those layers.").Value;
            overlayPlaneMeters=Config.Bind("UI","OverlayPlaneDistanceMeters",2f,"Experimental overlay camera plane distance in physical meters; bounded by game camera clipping.").Value;
            overlayWidthMeters=Config.Bind("UI","OverlayPanelWidthMeters",1.8f,"Experimental common physical panel width; desktop aspect/layout retained independently of game FOV.").Value;
            overlayAfterImageEffects=Config.Bind("UI","OverlayAfterImageEffects",false,"Experimental opt-in: render selected overlay UI in an isolated no-effect pass after the game eye's effects. Requires explicit overlay capture activation.").Value;
            overlayIsolationLayer=Config.Bind("UI","OverlayIsolationLayer",-1,"-1 selects an unused unnamed layer. Explicit 8..31 may borrow a named layer only while no loaded scene object, including inactive objects, occupies it.").Value;
            overlaySceneScanLimit=Config.Bind("UI","OverlaySceneScanLimit",OverlayLayerInventory.DefaultScanLimit,"Per-query object inspection bound for UI layer inventory and exclusivity. Explicit range 100000..1000000; incomplete inventories reject borrowing. Unity still allocates each complete query result.").Value;
            if(overlayIsolationLayer!=-1 && (overlayIsolationLayer<8 || overlayIsolationLayer>31)) throw new ArgumentOutOfRangeException("OverlayIsolationLayer","Use -1 or 8 through 31.");
            OverlayLayerInventory.ValidateScanLimit(overlaySceneScanLimit);
            if (Double.IsNaN(unitsPerMeter) || Double.IsInfinity(unitsPerMeter) || unitsPerMeter<=0)
                throw new ArgumentException("UnitsPerMeter must be finite and greater than zero.");
            if (disableOcclusionCulling) Logger.LogWarning("EXPLICIT_FIDELITY_REMEDY DisableOcclusionCulling=true; game culling is restored after the eye pair.");
            if (captureOverlayCanvases) Logger.LogWarning("EXPERIMENTAL_UI_ADAPTER CaptureOverlayCanvases=true; only active root overlay canvases are borrowed; IncludeOverlayLayers="+includeOverlayLayers+". Layout, canvas callbacks and headset readability require validation.");
            if(captureOverlayCanvases && flushDesktopLayoutBeforeBorrow) Logger.LogInfo("EXPLICIT_UI_FALLBACK FlushDesktopLayoutBeforeBorrow=true; pending desktop layout is flushed before fresh camera selection and state snapshots.");
            string directory=Environment.GetEnvironmentVariable("UGEVR_SESSION_DIRECTORY");
            evidenceDir=Path.Combine(String.IsNullOrEmpty(directory)?Paths.GameRootPath:directory,"UnityVR-evidence");
            if (capturesEnabled) Directory.CreateDirectory(evidenceDir);
            if (capturesEnabled && session.Available)
                inputProbe=new KeyboardEProbe(directory,Environment.GetEnvironmentVariable("UGEVR_SESSION_ID"),Process.GetCurrentProcess().Id,Stopwatch.Frequency,PublicKeyboardEReader.Create);
            if(compareHierarchyInventory && session.Available)
                hierarchyProbe=new HierarchyInventoryProbe(directory,Environment.GetEnvironmentVariable("UGEVR_SESSION_ID"),Process.GetCurrentProcess().Id);
            if(allowEngineInputExperiment && session.Available)
                engineInputExperiment=new EngineEInputExperiment(directory,Environment.GetEnvironmentVariable("UGEVR_SESSION_ID"),Process.GetCurrentProcess().Id,Stopwatch.Frequency,PublicEngineEKeyboard.Create,Stopwatch.GetTimestamp);
            var inputType=Type.GetType("UnityEngine.Input, UnityEngine.InputLegacyModule",false) ??
                Type.GetType("UnityEngine.Input, UnityEngine.InputModule",false) ?? Type.GetType("UnityEngine.Input, UnityEngine",false);
            if (inputType!=null) getKeyDown=inputType.GetMethod("GetKeyDown",new[] { typeof(string) });
            Publish(true);
            StartCoroutine(Run());
        }
        catch (Exception ex) { Fail(ex,true); }
    }

    IEnumerator Run()
    {
        EVRInitError error=EVRInitError.None;
        try
        {
            system=OpenVR.Init(ref error,EVRApplicationType.VRApplication_Scene);
            if (system==null || error!=EVRInitError.None) throw new InvalidOperationException("OpenVR initialization failed: "+error);
            compositor=OpenVR.Compositor;
            if (compositor==null) throw new InvalidOperationException("OpenVR compositor is unavailable.");
            compositor.SetTrackingSpace(ETrackingUniverseOrigin.TrackingUniverseStanding);
            uint width=0,height=0;
            system.GetRecommendedRenderTargetSize(ref width,ref height);
            if (width==0 || height==0) throw new InvalidOperationException("Runtime recommended a zero-sized target.");
            float factor=Math.Min(1f,1400f/Math.Max(width,height));
            for (int i=0;i<2;++i)
            {
                eyes[i]=new RenderTexture(Math.Max(1,(int)(width*factor)),Math.Max(1,(int)(height*factor)),24,RenderTextureFormat.ARGB32);
                eyes[i].antiAliasing=1;
                if (!eyes[i].Create()) throw new InvalidOperationException("Eye texture creation failed.");
            }
            renderEvent=GetRenderEventFunc();
            if (renderEvent==IntPtr.Zero) throw new InvalidOperationException("Native render callback is unavailable.");
            runtimeReady=true;
            AcquireBackground();
            Logger.LogInfo("VR_READY resolution="+eyes[0].width+"x"+eyes[0].height+" unitsPerMeter="+unitsPerMeter+" captureImages="+capturesEnabled);
            Publish(true);
        }
        catch (Exception ex) { Fail(ex,true); yield break; }
        var end=new WaitForEndOfFrame();
        while (!quitting)
        {
            yield return end;
            try { Tick(); } catch (Exception ex) { Fail(ex); }
        }
    }

    void Update()
    {
        ++updateCount;
        if (!quitting && inputProbe!=null) inputProbe.Tick(capturesEnabled,Application.isFocused,DateTime.UtcNow,Stopwatch.GetTimestamp());
        if(!quitting && engineInputExperiment!=null) engineInputExperiment.Tick(allowEngineInputExperiment && capturesEnabled,Application.isFocused,DateTime.UtcNow,Stopwatch.GetTimestamp());
        if (session!=null)
        {
            SessionCommand command=session.Poll(DateTime.UtcNow);
            if (command!=null && (pending==null || command.Revision>pending.Revision))
            {
                pending=command; pendingRecenter=null;
                // A new explicit command can retry a paused tracking window.
                // Reprocessing the old pending recenter must not restart it.
                tracking.Reset(); ClearTrackingDiagnostic();
            }
            ApplyCommand();
        }
        CheckDrain();
        if (KeyPressed("f8")) SetEnabled(!active);
        if (KeyPressed("f9")) pendingRecenter="true";
        TickGamepadDiagnostic();
        Publish(false);
    }

    double InputClock() {return Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency;}
    void TickGamepadDiagnostic()
    {
        if(gamepadDiagnostic==null) return;
        double now=InputClock();bool enabled=!quitting && active && runtimeReady && !draining && renderingActive && !tracking.Paused;
        var input=new LegacyRightSample();
        if(enabled && Application.isFocused && !gamepadDiagnostic.Complete)
            try {input=OpenVrLegacyRightReader.Read(system);} catch { }
        gamepadDiagnostic.Tick(enabled,Application.isFocused,generation,input,now);
        if(now>=nextInputReport)
        {
            nextInputReport=now+1;
            Logger.LogInfo("INPUT_DIAGNOSTIC status="+gamepadDiagnostic.Status+" reason="+gamepadDiagnostic.Reason+" addAttempts="+gamepadDiagnostic.AddAttempts+
                " queues="+gamepadDiagnostic.Queues+" appliedStateSamples="+gamepadDiagnostic.AppliedSamples+" releaseAttempts="+gamepadDiagnostic.ReleaseAttempts+
                " neutralObserved="+gamepadDiagnostic.ReleaseObserved+" removeAttempts="+gamepadDiagnostic.RemoveAttempts+" cleanup="+gamepadDiagnostic.CleanupStatus+" gameConsumption=unknown");
        }
    }
    void CancelGamepadDiagnostic(string reason)
    {
        if(gamepadDiagnostic!=null) {gamepadDiagnostic.Cancel(reason,InputClock());nextInputReport=0;TickGamepadDiagnostic();}
    }

    void ApplyCommand()
    {
        if (pending==null) return;
        if (pending.Enabled && tracking.Paused) return;
        if (!pending.Enabled && pending.Recenter!=null)
        {
            session.LastError="command: recenter requires enabled VR and a valid tracked head sample.";
            return;
        }
        if (pending.Enabled && !runtimeReady)
        {
            session.LastError="command: runtime is unavailable; restart the game after resolving initialization failure.";
            return;
        }
        SetEnabled(pending.Enabled);
        if (draining) return;
        if (pending.Recenter!=null) { pendingRecenter=pending.Recenter; return; }
        session.Acknowledge(pending); pending=null;
        Publish(true);
    }

    void SetEnabled(bool value)
    {
        if (value)
        {
            if (draining || !runtimeReady) return;
            if (!active) { ++generation; frameId=0; phase=0; active=true; tracking.Reset(); ClearTrackingDiagnostic(); AcquireBackground(); }
        }
        else if (active)
        {
            CancelGamepadDiagnostic("session_disabled");
            active=false; renderingActive=false; pendingRecenter=null;
            RestoreBackground();
            BeginDrain();
        }
    }

    void BeginDrain()
    {
        if (renderEvent==IntPtr.Zero || draining) return;
        ++generation; ++frameId;
        SetFrameState(generation,frameId);
        GL.IssuePluginEvent(renderEvent,3);
        draining=true; phaseStarted=Time.realtimeSinceStartup;
        // Posting this event does not acknowledge render-thread completion or GPU idle.
    }

    void CheckDrain()
    {
        if (!draining) return;
        try
        {
            if (GetCompletedDrainGeneration()==generation)
            {
                draining=false; phase=0;
                // Keep engine targets alive until process teardown. The bridge releases
                // its COM references here, but no GPU-idle fence is claimed by this API.
                Logger.LogInfo("VR_DISABLED camera restored; native references drained; textures retained until process exit.");
            }
            else if (Time.realtimeSinceStartup-phaseStarted>5 && session!=null)
                session.LastError="disable: render callback drain has not completed; keep resources alive and restart the game if it does not recover.";
        }
        catch (Exception ex) { if (session!=null) session.LastError="disable: "+ex.Message; }
    }

    void Tick()
    {
        if (draining || !active || !runtimeReady) return;
        if (phase==2)
        {
            if (GetCompletedSubmitGeneration()!=generation || GetCompletedSubmitFrame()!=frameId) { CheckPhaseTimeout(); return; }
            int leftError=GetSubmitError(0),rightError=GetSubmitError(1);
            submittedPairs=GetSubmittedFrames();
            int bridgeStatus=GetBridgeStatus();
            if (bridgeStatus!=0) throw new InvalidOperationException("Native bridge rejected submit: "+bridgeStatus);
            if (leftError!=0 || rightError!=0) throw new InvalidOperationException("OpenVR submit failed: "+leftError+","+rightError);
            renderingActive=true; lastSubmittedAt=Time.realtimeSinceStartup; phase=0;
        }
        if (phase==0)
        {
            if (tracking.Observe(false,0,NativePoseCallbackStatus.Success,Time.realtimeSinceStartup)==PoseCallbackDisposition.PauseTracking)
            {
                HandleTrackingInterruption(PoseCallbackDisposition.PauseTracking); return;
            }
            ++frameId;
            SetFrameState(generation,frameId);
            SetEyeTextures(eyes[0].GetNativeTexturePtr(),eyes[1].GetNativeTexturePtr());
            GL.IssuePluginEvent(renderEvent,0);
            phase=1; phaseStarted=Time.realtimeSinceStartup;
            return;
        }
        bool poseAcknowledged=GetCompletedPoseGeneration()==generation && GetCompletedPoseFrame()==frameId;
        int waitError=poseAcknowledged?GetPoseWaitError():0;
        int poseBridgeStatus=poseAcknowledged?GetBridgeStatus():0;
        var disposition=tracking.Observe(poseAcknowledged,waitError,(NativePoseCallbackStatus)poseBridgeStatus,Time.realtimeSinceStartup);
        if (disposition==PoseCallbackDisposition.AwaitCallback) { CheckPhaseTimeout(); return; }
        if (disposition==PoseCallbackDisposition.RetryPose || disposition==PoseCallbackDisposition.PauseTracking)
        {
            HandleTrackingInterruption(disposition); return;
        }
        if (disposition==PoseCallbackDisposition.Fail)
            throw new InvalidOperationException("Native pose callback failed: bridgeStatus="+poseBridgeStatus+", poseWaitError="+waitError+
                ". A rejected callback may not have called OpenVR WaitGetPoses.");
        var poseResult=compositor.GetLastPoses(poses,gamePoses);
        if (poseResult!=EVRCompositorError.None) throw new InvalidOperationException("OpenVR GetLastPoses failed: "+poseResult);
        var hmd=poses[OpenVR.k_unTrackedDeviceIndex_Hmd];
        if (!hmd.bPoseIsValid || !hmd.bDeviceIsConnected)
        {
            HandleTrackingInterruption(tracking.Observe(true,0,NativePoseCallbackStatus.InvalidPose,Time.realtimeSinceStartup)); return;
        }
        RigidPose head=Pose(hmd.mDeviceToAbsoluteTracking);
        tracking.ConfirmValidPose(); ClearTrackingDiagnostic();
        if (!origin.Initialized) { origin.RecenterPosition(head.Position); origin.RecenterYaw(head.Rotation); }
        if (pendingRecenter!=null)
        {
            switch (pendingRecenter)
            {
                case "true": origin.RecenterPosition(head.Position); origin.RecenterYaw(head.Rotation); break;
                case "position": origin.RecenterPosition(head.Position); break;
                case "height": origin.RecenterHeight(head.Position); break;
                case "yaw": origin.RecenterYaw(head.Rotation); break;
            }
            pendingRecenter=null;
            if (pending!=null && pending.Recenter!=null) { session.Acknowledge(pending); pending=null; Publish(true); }
        }
        Camera camera;
        DesktopLayoutFlush desktopFlush=null;
        if(captureOverlayCanvases && flushDesktopLayoutBeforeBorrow)
            desktopFlush=DesktopLayoutFlush.BeforeCameraSelection(true,captureLayoutInputs,layoutInputRootRectIds,SelectCamera,out camera);
        else camera=SelectCamera();
        if (camera==null)
        {
            renderingActive=false; phase=0;
            if (session!=null) session.LastError="camera: No enabled perspective camera targeting the desktop was found; a camera adapter/profile may be required.";
            Report("CAMERA_UNAVAILABLE"); return;
        }
        if (session!=null && session.LastError.StartsWith("camera: ")) session.LastError="";
        if (camera!=lastCamera) { lastCamera=camera; Logger.LogInfo("CAMERA selected="+camera.name+" depth="+camera.depth); }
        if(hierarchyProbe!=null) hierarchyProbe.Tick(capturesEnabled && compareHierarchyInventory,gameObject,Time.frameCount,DateTime.UtcNow,Stopwatch.GetTimestamp());
        Vector3 basePosition=camera.transform.position;
        Quaternion baseRotation=camera.transform.rotation;
        RigidPose anchor=new RigidPose(Position(basePosition),Rotation(baseRotation));
        var actualEyes=new RigidPose[2];
        var beforeRenderProjection=new Matrix4x4[2];
        string request=Path.Combine(evidenceDir,"capture-request");
        CaptureRequestAdmission captureAdmission=CaptureRequestAdmission.BeforePair(captureNativeUiState,capturesEnabled,captureOverlayCanvases,request);
        OverlayCanvasScope overlays=captureOverlayCanvases?new OverlayCanvasScope(camera,unitsPerMeter,overlayPlaneMeters,includeOverlayLayers,overlayWidthMeters,captureLayoutInputs,layoutInputRootRectIds,captureAdmission.CaptureNativeUiState,nativeUiRendererLimit):null;
        if(overlays!=null && capturesEnabled) overlays.EnableWallTiming();
        if(overlays!=null) overlays.SetDesktopLayoutFlush(desktopFlush);
        if (overlays!=null) overlays.SetCommonHead(TrackingMath.ComposeEye(anchor,head,new RigidPose(new Position(0,0,0),Orientation.Identity),origin,unitsPerMeter,retainGameTilt));
        if (overlays!=null && overlays.SelectedCount>0 && overlayAfterImageEffects)
        {
            if(overlayCamera==null)
            {
                var owner=new GameObject("UGEVR overlay camera");owner.hideFlags=HideFlags.HideAndDontSave;
                overlayCamera=owner.AddComponent<Camera>();overlayCamera.enabled=false;
            }
            try {overlays.UseAfterEffectsCamera(overlayCamera,overlayIsolationLayer,overlaySceneScanLimit);}
            catch(OverlayLayerSelectionException selectionError) {WriteLayerFailureDiagnostic(selectionError);overlays.Dispose();throw;}
        }
        try
        {
        using (var restore=new CameraState(camera))
        using (overlays)
        {
            for (int i=0;i<2;++i)
            {
                actualEyes[i]=TrackingMath.ComposeEye(anchor,head,Pose(system.GetEyeToHeadTransform((EVREye)i)),origin,unitsPerMeter,retainGameTilt);
                camera.transform.position=Vector(actualEyes[i].Position);
                camera.transform.rotation=QuaternionValue(actualEyes[i].Rotation);
                camera.ResetWorldToCameraMatrix();
                camera.projectionMatrix=Projection((EVREye)i,camera.nearClipPlane,camera.farClipPlane);
                if (disableOcclusionCulling) camera.useOcclusionCulling=false;
                camera.rect=new Rect(0,0,1,1);
                camera.targetTexture=eyes[i];
                beforeRenderProjection[i]=camera.projectionMatrix;
                // Evidence must describe the applied engine transform, including
                // Unity float precision, rather than only the requested math result.
                actualEyes[i]=new RigidPose(Position(camera.transform.position),Rotation(camera.transform.rotation));
                if (overlays!=null) overlays.ApplyEye(i);
                bool isolatedUi=overlays!=null && overlays.IsolatedMask!=0;
                if(isolatedUi) overlays.ValidateLayerExclusive(i==0?"before game left":"before game right");
                if(isolatedUi) OverlayCameraPass.Prepare(camera,overlayCamera,overlays.IsolatedMask);
                if(overlays!=null) overlays.RenderGameEye(i);else camera.Render();
                if(overlays!=null) overlays.ValidateInactiveCanvases("after game render");
                if(isolatedUi)
                {
                    overlays.ValidateLayerExclusive(i==0?"after game left":"after game right");
                    overlays.RenderUiEye(i);
                    overlays.ValidateLayerExclusive(i==0?"after UI left":"after UI right");
                }
                if(overlays!=null) overlays.ValidateInactiveCanvases("after UI render");
                if (overlays!=null) overlays.RecordAfterRender(i);
            }
        }
        }
        finally
        {
            if(overlayCamera!=null) overlayCamera.targetTexture=null;
            if(overlays!=null) overlays.RebuildRestoredGeometry();
            if(overlays!=null) overlays.CaptureNativeUiAfterRestoration();
            if(overlays!=null)
                try {overlays.ValidateInactiveCanvases("after desktop restoration");}
                catch(InvalidOperationException inactiveError) {Logger.LogError(inactiveError.Message);}
            if (overlays!=null && overlays.RestorationConflicts>0)
            {
                Logger.LogError("UI_RESTORATION_CONFLICT "+overlays.RestorationError);
                if (session!=null) session.LastError="UI restoration conflict: "+overlays.RestorationError;
                WriteUiFailureDiagnostic(overlays);
            }
        }
        if (overlays!=null && overlays.RestorationConflicts>0) throw new InvalidOperationException("UI state could not be fully restored: "+overlays.RestorationError);
        if (overlays!=null && overlays.UnexpectedGeometryChanges>0) Report("UI_RENDER_STATE_CHANGED count="+overlays.UnexpectedGeometryChanges+"; inspect appliedEyes/afterRenderEyes before claiming UI stereo correctness.");
        SetEyeTextures(eyes[0].GetNativeTexturePtr(),eyes[1].GetNativeTexturePtr());
        GL.IssuePluginEvent(renderEvent,1);
        phase=2; phaseStarted=Time.realtimeSinceStartup; ++rendered;
        if (captureAdmission.TryConsume(request))
        {
            CaptureEye(0); CaptureEye(1);
            WriteCapture(head,anchor,actualEyes,camera.transform.position==basePosition && camera.transform.rotation==baseRotation);
            try { CameraDiagnostics.Write(evidenceDir,rendered,system,camera,beforeRenderProjection,overlays==null?null:overlays.DiagnosticJson()); }
            catch (Exception diagnosticError) { Logger.LogWarning("Capture diagnostics unavailable: "+diagnosticError.Message); }
            RequestMirrorCapture(Path.Combine(evidenceDir,"compositor-left-"+rendered+".bmp"));
            GL.IssuePluginEvent(renderEvent,2);
        }
        Report("VR_FRAME rendered="+rendered+" submitted="+submittedPairs+" frame="+frameId+" generation="+generation+
            (overlays==null?"":" uiOverlayRoots="+overlays.SelectedCount+" uiPlane="+overlays.PlaneDistance+" excludedUILayers="+(overlays.OverlayLayerMask & ~overlays.AppliedMask)));
    }

    void HandleTrackingInterruption(PoseCallbackDisposition disposition)
    {
        bool firstReport=trackingDiagnostic==null;
        renderingActive=false; phase=0;
        if (disposition==PoseCallbackDisposition.PauseTracking)
        {
            SetEnabled(false);
            trackingDiagnostic="tracking: VR paused after five seconds without a valid HMD pose. Wake/reconnect the headset, then explicitly enable VR with F8 or the session controller. Pending recenter was not applied; native drain may still be pending.";
            Logger.LogWarning("TRACKING_PAUSED "+trackingDiagnostic);
        }
        else
        {
            trackingDiagnostic="tracking: HMD pose is invalid or disconnected; reacquiring for up to five seconds. Eye rendering, submission and recenter are suspended.";
            Report("TRACKING_REACQUIRING generation="+generation+" frame="+frameId);
        }
        if (session!=null) session.LastError=trackingDiagnostic;
        Publish(firstReport || disposition==PoseCallbackDisposition.PauseTracking);
    }
    void WriteUiFailureDiagnostic(OverlayCanvasScope overlays)
    {
        if (!capturesEnabled || uiFailureCaptures>=8) return;
        ++uiFailureCaptures;
        try
        {
            File.WriteAllText(Path.Combine(evidenceDir,"ui-restoration-failure-"+generation+"-"+frameId+".json"),
                "{\"diagnosticVersion\":1,\"outcome\":\"restoration-failed\",\"pairSubmitted\":false,\"gameFrame\":"+Time.frameCount+
                ",\"generation\":"+generation+",\"vrFrameId\":"+frameId+",\"previousRenderedPairs\":"+rendered+
                ",\"failureCaptureLimit\":8,\"overlayCapture\":"+overlays.DiagnosticJson()+"}");
        }
        catch (Exception error) { Logger.LogWarning("UI failure diagnostics unavailable: "+error.Message); }
    }
    void WriteLayerFailureDiagnostic(OverlayLayerSelectionException error)
    {
        if(!capturesEnabled || session==null || !session.Available || error.Inventory==null || layerFailureCaptures>=4) return;
        while(layerFailureCaptures<4 && File.Exists(Path.Combine(evidenceDir,"ui-layer-selection-failure-"+(layerFailureCaptures+1)+".yaml"))) ++layerFailureCaptures;
        if(layerFailureCaptures>=4) return;
        ++layerFailureCaptures;
        string destination=Path.Combine(evidenceDir,"ui-layer-selection-failure-"+layerFailureCaptures+".yaml");
        string temporary=destination+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            string body=error.Inventory.DiagnosticYaml(Environment.GetEnvironmentVariable("UGEVR_SESSION_ID"),Process.GetCurrentProcess().Id,error.RequestedLayer,error.Message);
            File.WriteAllText(temporary,body,new System.Text.UTF8Encoding(false));
            File.Move(temporary,destination);
        }
        catch(Exception failure) {Logger.LogWarning("UI layer selection diagnostic unavailable: "+failure.Message);}
        finally {try {if(File.Exists(temporary)) File.Delete(temporary);} catch {}}
    }
    void ClearTrackingDiagnostic()
    {
        // Clear only this owned diagnostic, not a newer command/status failure.
        if (session!=null && trackingDiagnostic!=null && session.LastError==trackingDiagnostic) session.LastError="";
        trackingDiagnostic=null;
    }
    void CheckPhaseTimeout()
    {
        if (Time.realtimeSinceStartup-phaseStarted>5) throw new TimeoutException("Native render callback did not acknowledge frame "+frameId+"; restart the game if drain cannot complete.");
    }
    void Report(string text)
    {
        if (Time.realtimeSinceStartup<nextReport) return;
        Logger.LogInfo(text); nextReport=Time.realtimeSinceStartup+3;
    }
    Camera SelectCamera()
    {
        Camera camera=Camera.main;
        if (camera!=null && camera.enabled && camera.targetTexture==null && !camera.orthographic) return camera;
        camera=null;
        foreach (Camera candidate in Camera.allCameras)
            if (candidate.enabled && candidate.targetTexture==null && !candidate.orthographic && (camera==null || candidate.depth>camera.depth)) camera=candidate;
        return camera;
    }
    Matrix4x4 Projection(EVREye eye,float near,float far)
    {
        if (Single.IsNaN(near) || Single.IsInfinity(near) || near<=0 || Single.IsNaN(far) || Single.IsInfinity(far) || far<=near)
            throw new InvalidOperationException("Camera clip distances must be finite, with 0 < near < far. Use an explicit profile remedy for invalid clipping.");
        float left=0,right=0,top=0,bottom=0;
        system.GetProjectionRaw(eye,ref left,ref right,ref top,ref bottom);
        EyeFrustum bounds=EyeProjection.FromOpenVrRaw(left,right,top,bottom,near,far);
        return Matrix4x4.Frustum((float)bounds.Left,(float)bounds.Right,(float)bounds.Bottom,(float)bounds.Top,near,far);
    }
    static RigidPose Pose(HmdMatrix34_t m) { return TrackingMath.FromOpenVr(new double[] {m.m0,m.m1,m.m2,m.m3,m.m4,m.m5,m.m6,m.m7,m.m8,m.m9,m.m10,m.m11}); }
    static Position Position(Vector3 v) { return new Position(v.x,v.y,v.z); }
    static Orientation Rotation(Quaternion q) { return new Orientation(q.x,q.y,q.z,q.w); }
    static Vector3 Vector(Position p) { return new Vector3((float)p.X,(float)p.Y,(float)p.Z); }
    static Quaternion QuaternionValue(Orientation q) { return new Quaternion((float)q.X,(float)q.Y,(float)q.Z,(float)q.W); }
    void CaptureEye(int eye)
    {
        var old=RenderTexture.active;
        Texture2D image=null;
        try
        {
            RenderTexture.active=eyes[eye];
            image=new Texture2D(eyes[eye].width,eyes[eye].height,TextureFormat.RGB24,false);
            image.ReadPixels(new Rect(0,0,image.width,image.height),0,0); image.Apply();
            File.WriteAllBytes(Path.Combine(evidenceDir,"eye-"+eye+"-"+rendered+".png"),image.EncodeToPNG());
        }
        finally { RenderTexture.active=old; if (image!=null) Destroy(image); }
    }
    static string Number(double value) { return value.ToString("R",CultureInfo.InvariantCulture); }
    static string VectorJson(Position p) { return "["+Number(p.X)+","+Number(p.Y)+","+Number(p.Z)+"]"; }
    static string QuaternionJson(Orientation q) { return "["+Number(q.X)+","+Number(q.Y)+","+Number(q.Z)+","+Number(q.W)+"]"; }
    void WriteCapture(RigidPose head,RigidPose anchor,RigidPose[] eye,bool restored)
    {
        File.WriteAllText(Path.Combine(evidenceDir,"capture-"+rendered+".json"),
            "{\"evidenceVersion\":2,\"frame\":"+rendered+",\"vrFrameId\":"+frameId+",\"generation\":"+generation+
            ",\"gameFrame\":"+Time.frameCount+",\"unitsPerMeter\":"+Number(unitsPerMeter)+
            ",\"retainGameTilt\":"+retainGameTilt.ToString().ToLowerInvariant()+
            ",\"originPosition\":"+VectorJson(origin.Center)+",\"originRotation\":"+QuaternionJson(origin.Yaw)+
            ",\"headPosition\":"+VectorJson(head.Position)+",\"headRotation\":"+QuaternionJson(head.Rotation)+
            ",\"basePosition\":"+VectorJson(anchor.Position)+",\"baseRotation\":"+QuaternionJson(anchor.Rotation)+
            ",\"eye0Position\":"+VectorJson(eye[0].Position)+",\"eye1Position\":"+VectorJson(eye[1].Position)+
            ",\"eye0Rotation\":"+QuaternionJson(eye[0].Rotation)+",\"eye1Rotation\":"+QuaternionJson(eye[1].Rotation)+
            ",\"gameCameraRestored\":"+restored.ToString().ToLowerInvariant()+"}");
    }
    bool KeyPressed(string key)
    {
        if (getKeyDown==null) return false;
        try { return (bool)getKeyDown.Invoke(null,new object[] {key}); }
        catch { getKeyDown=null; Logger.LogWarning("Legacy keyboard toggle is unavailable; use the session interface."); return false; }
    }
    void AcquireBackground() { if (!ownsBackground) { oldBackground=Application.runInBackground; Application.runInBackground=true; ownsBackground=true; } }
    void RestoreBackground() { if (ownsBackground) { if (Application.runInBackground) Application.runInBackground=oldBackground; ownsBackground=false; } }
    void Fail(Exception error)
    {
        Fail(error,false);
    }
    void Fail(Exception error,bool initialization)
    {
        Logger.LogError(error);
        if (session!=null)
        {
            session.LastError=error.Message;
            if (initialization) session.InitializationError=error.Message;
        }
        active=false; renderingActive=false; RestoreBackground();
        CancelGamepadDiagnostic("vr_failure");
        try { BeginDrain(); } catch (Exception drainError) { Logger.LogError("Drain failed: "+drainError.Message); }
        Publish(true);
    }
    void Publish(bool force)
    {
        if (session!=null) session.Publish(DateTime.UtcNow,Application.unityVersion,updateCount>=2,runtimeReady,
            active && renderingActive && Time.realtimeSinceStartup-lastSubmittedAt<3,submittedPairs,force);
    }
    void OnApplicationQuit() { quitting=true; StopOwnedWork(); }
    void OnDestroy() { quitting=true; StopOwnedWork(); }
    void OnDisable() {CancelGamepadDiagnostic("component_disabled");}
    void StopOwnedWork()
    {
        CancelGamepadDiagnostic("game_lifecycle_end");
        if (inputProbe!=null) { inputProbe.Cancel(DateTime.UtcNow,Stopwatch.GetTimestamp()); inputProbe=null; }
        if(hierarchyProbe!=null)
        {
            hierarchyProbe.Dispose();
            if(hierarchyProbe.LastDiagnosticError!=null) Logger.LogWarning("Hierarchy diagnostic cleanup: "+hierarchyProbe.LastDiagnosticError);
            hierarchyProbe=null;
        }
        if(engineInputExperiment!=null) {engineInputExperiment.Cancel(DateTime.UtcNow,Stopwatch.GetTimestamp(),"game_lifecycle_end");engineInputExperiment=null;}
        active=false; renderingActive=false; RestoreBackground();
        if(overlayCamera!=null) {overlayCamera.enabled=false;overlayCamera.targetTexture=null;}
        try { BeginDrain(); } catch (Exception error) { Logger.LogError("Final drain pending: "+error.Message); }
        if (session!=null) session.LastError="Process teardown: queued native work and GPU completion cannot be acknowledged after the final engine callback.";
        Publish(true);
        // Do not call OpenVR.Shutdown or destroy eye textures while queued callbacks
        // may still use them. Process teardown reclaims them; disable is not DLL unload.
    }
}
