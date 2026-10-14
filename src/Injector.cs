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
    GamepadDiagnosticBinding gamepadBinding;
    string gamepadDiagnosticProfile;
    RewiredInputDiagnostic rewiredDiagnostic;
    RewiredFiveInputDiagnostic rewiredFiveDiagnostic;
    string rewiredDiagnosticProfile;
    double nextRewiredReport;
    RewiredSourceObservation rewiredSourceObservation;
    double nextRewiredSourceReport;
    bool captureRewiredSourceDetails;
    RewiredPairSourceObservation rewiredPairSourceObservation;
    double nextRewiredPairSourceReport;
    bool captureRewiredPairSourceDetails;
    CameraRestorationPoseDiagnostics cameraPoseDiagnostics;
    CameraPoseCaptureRequests cameraPoseCaptureRequests;
    string cameraPoseAdmissionError;
    bool captureInputIdentityDetails;
    ControllerIdentityCapture inputIdentityCapture;
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
    bool restoreBorrowedCameraCurrent;
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
            bool captureCameraRestorationPose=Config.Bind("Diagnostics","CaptureCameraRestorationPoseDetails",false,"Private passive transform evidence for at most eight capture requests admitted before an eye pair. Requires CaptureImages and a controller session; late requests defer to the next pair and exhausted capacity preserves requests without image capture. Records world/local pose and parent instance identity; does not change restoration or its aggregate comparison.").Value && capturesEnabled;
            captureNativeUiState=Config.Bind("Diagnostics","CaptureNativeUiState",false,"Opt-in cached CanvasRenderer cull/clipping/alpha and render-context observations only for capture requests admitted before the eye pair. No native UI state is restored; requires CaptureImages.").Value && capturesEnabled;
            nativeUiRendererLimit=Config.Bind("Diagnostics","NativeUiRendererLimit",NativeUiStateDiagnostics.DefaultRendererLimit,"Explicit native UI diagnostic renderer bound 1024..8192; default 1024. Identity-query occurrences are bounded to eight times this limit. Validated only when CaptureNativeUiState and CaptureImages are enabled.").Value;
            nativeUiRendererLimit=NativeUiStateDiagnostics.ResolveRendererLimit(captureNativeUiState,capturesEnabled,nativeUiRendererLimit);
            compareHierarchyInventory=Config.Bind("Diagnostics","CompareHierarchyInventory",false,"Diagnostic only: permit at most two explicitly requested sequential Resources-versus-scene hierarchy comparisons before an eye pair. Requires CaptureImages and controller session identity; does not replace layer guards.").Value && capturesEnabled;
            allowEngineInputExperiment=Config.Bind("Diagnostics","AllowEngineInputExperiment",false,"Explicitly allow at most two fixed E 500ms synthetic engine keyboard experiments per session. Requires CaptureImages; this is not physical forwarding or gameplay gate evidence.").Value && capturesEnabled;
            bool allowGamepad=Config.Bind("Input","AllowOpenVrGamepadDiagnostic",false,"Explicit code activation for one bounded owned InputSystem gamepad experiment per launch. Never changes physical keyboard state; game consumption requires observation.").Value;
            string inputProfile=Config.Bind("Input","DiagnosticProfile","none","Select outer-1.4.1-dual-hand-a-west-b-east-sticks-v1 to add observed right B to East with exact source model/signature guards. Both dual profiles require exactly 3 neutral pair reads. Select outer-1.4.1-dual-hand-a-west-sticks-v1 for left movement/right look/right A to West. Select outer-1.4.1-right-a-west-axis0-leftstick-v1 for single-hand original wake/interact or outer-1.4.1-right-a-south-axis0-leftstick-v1 for menu confirm/jump. The restricted outer-1.4.1-deckardmp-right-a-west-button-only-v1 maps no axes; restricted raw axis mapping requires outer-1.4.1-deckardmp-right-a-west-observed-axis0-leftstick-v1. Explicit test profiles, not general game bindings.").Value;
            string inputConflict=Config.Bind("Input","CurrentGamepadConflictPolicy","refuse","refuse leaves an existing current gamepad untouched. prefer-owned explicitly permits the owned diagnostic pad to become current, with conditional restoration.").Value;
            float inputSeconds=Config.Bind("Input","DiagnosticDurationSeconds",15f,"Bounded 5..300 second diagnostic window, starting only with a neutral valid right controller and focused active VR.").Value;
            float inputDeadzone=Config.Bind("Input","DiagnosticDeadzone",0.2f,"Explicit radial left-stick deadzone, 0.1..0.8.").Value;
            captureInputIdentityDetails=Config.Bind("Input","CaptureIdentityDetails",false,"Explicit private diagnostic: capture bounded raw controller serial/model SDK results in this controller-owned session evidence directory. Never include raw IDs in public reports.").Value;
            captureRewiredSourceDetails=Config.Bind("Input","CaptureRewiredSourceDetails",false,"Private passive OpenVR observation only, independent of Rewired input admission. A fresh run-id/request-id file in controller-owned UnityVR-evidence requests 15 seconds, at most 300 samples at 20Hz and three windows per launch. Raw identities stay private; no input is authorized.").Value;
            captureRewiredPairSourceDetails=Config.Bind("Input","CaptureRewiredPairSourceDetails",false,"Private passive two-hand SDK observation only. A fresh rewired-pair-source-request in the controller-owned UnityVR-evidence requests 15 seconds, at most 300 paired records at 20Hz and three windows per launch. Records preserve independent before/after identity and raw SDK results; no input is authorized.").Value;
            int inputNeutralReads=Config.Bind("Input","DiagnosticNeutralReads",3,"Single-hand profiles require 1..10 consecutive valid neutral reads; the dual-hand profile requires exactly 3 stable neutral pair reads before creating the first owned device.").Value;
            bool allowRewired=Config.Bind("Input","AllowRewiredInputDiagnostic",false,"Explicit code activation for one bounded owned public Rewired custom-controller window. Requires CaptureImages, a controller session and exact Firewatch profile; no private game callback is invoked.").Value;
            string rewiredProfile=Config.Bind("Input","RewiredDiagnosticProfile","none","Select firewatch-2017-rewired-right-stick-trigger-v1 for a uniquely declared trigger, or firewatch-2017-rewired-right-stick-observed-trigger-axis1-v1 for the inspected Oculus application view and observed finger-trigger axis1. Select firewatch-2017-rewired-dual-sticks-observed-trigger-axis1-v1, which additionally feeds normal Move actions29/30 from the independently observed left hand. Existing right profiles feed actions31/32/36.").Value;
            string rewiredConflict=Config.Bind("Input","RewiredConflictPolicy","refuse","Only refuse is supported. Existing custom definitions/devices or physical Rewired joysticks reject this prototype; game additions to owned content are preserved.").Value;
            float rewiredSeconds=Config.Bind("Input","RewiredDurationSeconds",15f,"Explicit bounded 5..300 seconds after fresh neutral admission. Source expires after 500ms without a refreshed valid sample.").Value;
            float rewiredDeadzone=Config.Bind("Input","RewiredStickDeadzone",0.2f,"Explicit radial stick deadzone .1...8. The trigger remains an analog axis for the game's ordinary threshold logic.").Value;
            bool rewiredReadback=Config.Bind("Input","CaptureRewiredActionReadback",false,"Optional bounded fixed public Rewired readbacks at most once per second during the owned input window: enabled state, owned raw axes, system-player raw actions and last-active reference agreement. No names or hardware identity are logged; action aggregation is not game consumption.").Value;
            if(allowRewired)
            {
                try
                {
                    if(!capturesEnabled || !session.Available || allowGamepad || allowEngineInputExperiment || !PublicOwnedRewiredInput.SupportsProfile(rewiredProfile))
                        throw new InvalidOperationException("Rewired diagnostic requires images/session/profile and no simultaneous gamepad or E experiment.");
                    rewiredDiagnosticProfile=rewiredProfile;
                    if(rewiredProfile==PublicOwnedRewiredInput.FiveChannelProfile) rewiredFiveDiagnostic=new RewiredFiveInputDiagnostic(PublicOwnedRewiredInput.CreateFive,rewiredSeconds,rewiredDeadzone,rewiredConflict,rewiredReadback);
                    else rewiredDiagnostic=new RewiredInputDiagnostic(PublicOwnedRewiredInput.Create,rewiredSeconds,rewiredDeadzone,rewiredConflict,rewiredReadback);
                    Logger.LogWarning("EXPLICIT_REWIRED_DIAGNOSTIC profile="+rewiredProfile+" duration="+rewiredSeconds+" conflict="+rewiredConflict+"; normal game action consumption remains unobserved.");
                }
                catch(Exception error) {Logger.LogWarning("REWIRED_DIAGNOSTIC unavailable: "+error.Message);}
            }
            if(allowGamepad)
            {
                try
                {
                    gamepadBinding=GamepadDiagnosticBinding.Create(allowGamepad,session.Available,allowRewired,allowEngineInputExperiment,inputProfile,inputSeconds,inputDeadzone,inputConflict,inputNeutralReads,delegate {return system;});
                    gamepadDiagnosticProfile=inputProfile;
                    if(captureInputIdentityDetails && !gamepadBinding.SupportsIdentityCapture) Logger.LogWarning("INPUT_DIAGNOSTIC CaptureIdentityDetails is unsupported for the dual-hand profile. The separate opt-in passive Rewired source observer remains available.");
                    Logger.LogWarning("EXPLICIT_INPUT_DIAGNOSTIC profile="+inputProfile+" currentConflict="+inputConflict+" duration="+inputSeconds+"; synthetic owned gamepad, game consumption unknown.");
                }
                catch(Exception inputError) {Logger.LogWarning("INPUT_DIAGNOSTIC unavailable: "+inputError.Message);}
            }
            captureLayoutInputs=Config.Bind("Diagnostics","CaptureLayoutInputs",false,"Opt-in bounded cached text/layout input snapshots for UI restoration mismatches. Requires CaptureImages; no preferred-size, generator or game getters are queried.").Value && capturesEnabled;
            layoutInputRootRectIds=Config.Bind("Diagnostics","LayoutInputRootRectIds","","Optional comma-separated runtime RectTransform IDs selecting diagnostic subtrees plus up to eight ancestors. Empty retains full coverage. Runtime IDs require observed name/parent validation each launch.").Value;
            disableOcclusionCulling=Config.Bind("Rendering","DisableOcclusionCulling",false,"Explicit fidelity remedy: disable the selected camera's occlusion culling only during eye rendering.").Value;
            captureOverlayCanvases=Config.Bind("UI","CaptureOverlayCanvases",false,"Experimental: borrow active screen overlay canvases during eye rendering and restore desktop state.").Value;
            flushDesktopLayoutBeforeBorrow=Config.Bind("UI","FlushDesktopLayoutBeforeBorrow",false,"Explicit experimental fallback: flush pending desktop canvas layout once before current camera selection and all state snapshots. Runs game canvas callbacks; requires CaptureOverlayCanvases.").Value;
            restoreBorrowedCameraCurrent=Config.Bind("Rendering","RestoreBorrowedCameraCurrent",false,"Explicit experiment: restore a live selected Camera.current after manual eye renders only from null or an owned rendered-camera context. Foreign current is preserved and reported; desktop fidelity remains unvalidated.").Value;
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
            if(restoreBorrowedCameraCurrent) Logger.LogWarning("EXPLICIT_CAMERA_CONTEXT_EXPERIMENT RestoreBorrowedCameraCurrent=true; conditional current-camera restoration, desktop fidelity unknown.");
            string directory=Environment.GetEnvironmentVariable("UGEVR_SESSION_DIRECTORY");
            evidenceDir=Path.Combine(String.IsNullOrEmpty(directory)?Paths.GameRootPath:directory,"UnityVR-evidence");
            if (capturesEnabled) Directory.CreateDirectory(evidenceDir);
            if(captureInputIdentityDetails && gamepadBinding!=null && gamepadBinding.SupportsIdentityCapture && session.Available) inputIdentityCapture=new ControllerIdentityCapture(evidenceDir);
            if(captureCameraRestorationPose && session.Available)
            {cameraPoseDiagnostics=new CameraRestorationPoseDiagnostics(evidenceDir);cameraPoseCaptureRequests=new CameraPoseCaptureRequests(evidenceDir);}
            if(captureRewiredSourceDetails && session.Available)
            {
                try {rewiredSourceObservation=new RewiredSourceObservation(directory,Environment.GetEnvironmentVariable("UGEVR_SESSION_ID"),Process.GetCurrentProcess().Id,Stopwatch.Frequency,delegate {return RewiredSourceReader.Read(system);});}
                catch(Exception error) {Logger.LogWarning("REWIRED_SOURCE_OBSERVATION unavailable: "+error.GetType().FullName);}
            }
            if(captureRewiredPairSourceDetails && session.Available)
            {
                try {rewiredPairSourceObservation=new RewiredPairSourceObservation(directory,Environment.GetEnvironmentVariable("UGEVR_SESSION_ID"),Process.GetCurrentProcess().Id,Stopwatch.Frequency,delegate {return RewiredPairSourceReader.Read(system);});}
                catch(Exception error) {Logger.LogWarning("REWIRED_PAIR_SOURCE_OBSERVATION unavailable: "+error.GetType().FullName);}
            }
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
        TickRewiredDiagnostic();
        TickRewiredSourceObservation();
        TickRewiredPairSourceObservation();
        Publish(false);
    }

    double InputClock() {return Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency;}
    void TickGamepadDiagnostic()
    {
        if(gamepadBinding==null) return;
        double now=InputClock();bool enabled=!quitting && active && runtimeReady && !draining && renderingActive && !tracking.Paused;
        gamepadBinding.Tick(enabled,Application.isFocused,generation,now);
        if(gamepadBinding.CancelDual!=null)
        {
            if(now<nextInputReport)return;nextInputReport=now+1;var cancel=gamepadBinding.CancelDual;
            Logger.LogInfo("INPUT_DIAGNOSTIC profile="+gamepadDiagnosticProfile+" sourceAxes="+(gamepadBinding.LastCancelPair.Pair.Available?"pinned_dual_joysticks":"unavailable")+" status="+cancel.Status+" reason="+cancel.Reason+" pairReason="+(gamepadBinding.LastCancelPair.Pair.UnavailableReason ?? "not_read")+" neutralReads="+cancel.NeutralReads+" requiredNeutralReads="+OpenVrDualCancelGamepadDiagnostic.RequiredNeutralReads+" addAttempts="+cancel.AddAttempts+" queues="+cancel.Queues+" appliedStateSamples="+cancel.AppliedSamples+" releaseAttempts="+cancel.ReleaseAttempts+" neutralObserved="+cancel.ReleaseObserved+" removeAttempts="+cancel.RemoveAttempts+" cleanup="+cancel.CleanupStatus+" bindingError="+gamepadBinding.LastError+" identityCapture=unsupported gameConsumption=unknown");
            return;
        }
        if(gamepadBinding.Dual!=null)
        {
            if(now<nextInputReport) return;nextInputReport=now+1;var dual=gamepadBinding.Dual;
            Logger.LogInfo("INPUT_DIAGNOSTIC profile="+gamepadDiagnosticProfile+" sourceAxes="+(gamepadBinding.LastPair.Available?"declared_dual_joysticks":"unavailable")+" status="+dual.Status+" reason="+dual.Reason+" pairReason="+(gamepadBinding.LastPair.UnavailableReason ?? "not_read")+" neutralReads="+dual.NeutralReads+" requiredNeutralReads="+OpenVrDualGamepadDiagnostic.RequiredNeutralReads+" addAttempts="+dual.AddAttempts+" queues="+dual.Queues+" appliedStateSamples="+dual.AppliedSamples+" releaseAttempts="+dual.ReleaseAttempts+" neutralObserved="+dual.ReleaseObserved+" removeAttempts="+dual.RemoveAttempts+" cleanup="+dual.CleanupStatus+" bindingError="+gamepadBinding.LastError+" identityCapture=unsupported gameConsumption=unknown");
            return;
        }
        var gamepadDiagnostic=gamepadBinding.Single;var input=gamepadBinding.LastSingle;
        if(gamepadDiagnostic==null) return;
        if(inputIdentityCapture!=null)
        {
            if(input.Available && !inputIdentityCapture.FirstSuccessAttempted) inputIdentityCapture.Capture(input.IdentityDiagnostics,true,false,system);
            if(gamepadDiagnostic.Complete && !inputIdentityCapture.TerminalAttempted) inputIdentityCapture.Capture(gamepadDiagnostic.LastIdentitySnapshot,false,true,system);
        }
        if(now>=nextInputReport)
        {
            nextInputReport=now+1;
            if(inputIdentityCapture!=null && !gamepadDiagnostic.Complete) inputIdentityCapture.Capture(gamepadDiagnostic.LastIdentitySnapshot,false,false,system);
            var identitySnapshot=gamepadDiagnostic.LastIdentitySnapshot;
            Logger.LogInfo("INPUT_DIAGNOSTIC profile="+gamepadDiagnosticProfile+" sourceAxes="+(input.AxesSource ?? "unavailable")+" axis0Metadata="+(input.Axis0Metadata ?? "not_queried")+" axis0Type="+(input.Axis0Type.HasValue?input.Axis0Type.Value.ToString():"unavailable")+" status="+gamepadDiagnostic.Status+" reason="+gamepadDiagnostic.Reason+" identityStage="+(identitySnapshot==null?"not_read":identitySnapshot.Stage)+" identityVerdict="+(identitySnapshot==null?"not_read":identitySnapshot.Verdict)+" identityCaptureAttempts="+(inputIdentityCapture==null?0:inputIdentityCapture.Attempts)+" identityCaptureError="+(inputIdentityCapture==null?"":inputIdentityCapture.LastError)+" nativeUnavailableReason="+gamepadDiagnostic.NativeUnavailableReason+" neutralReads="+gamepadDiagnostic.NeutralReads+" requiredNeutralReads="+gamepadDiagnostic.RequiredNeutralReads+" addAttempts="+gamepadDiagnostic.AddAttempts+
                " queues="+gamepadDiagnostic.Queues+" appliedStateSamples="+gamepadDiagnostic.AppliedSamples+" releaseAttempts="+gamepadDiagnostic.ReleaseAttempts+
                " neutralObserved="+gamepadDiagnostic.ReleaseObserved+" removeAttempts="+gamepadDiagnostic.RemoveAttempts+" cleanup="+gamepadDiagnostic.CleanupStatus+" gameConsumption=unknown");
        }
    }
    void CancelGamepadDiagnostic(string reason,bool reversiblePause=false)
    {
        if(gamepadBinding!=null)
        {
            if(reversiblePause) gamepadBinding.Pause(reason,InputClock());else gamepadBinding.Cancel(reason,InputClock());
            nextInputReport=0;TickGamepadDiagnostic();
        }
    }
    void TickRewiredDiagnostic()
    {
        if(rewiredFiveDiagnostic!=null) {TickRewiredFiveDiagnostic();return;}
        if(rewiredDiagnostic==null) return;
        double now=InputClock();bool enabled=!quitting && active && runtimeReady && !draining && renderingActive && !tracking.Paused;
        var source=new RewiredRightSample();if(enabled && Application.isFocused && !rewiredDiagnostic.Complete) try {source=OpenVrRewiredRightReader.Read(system,rewiredDiagnosticProfile);} catch { }
        rewiredDiagnostic.Tick(enabled,Application.isFocused,generation,source,now);
        if(now>=nextRewiredReport)
        {
            nextRewiredReport=now+1;
            Logger.LogInfo("REWIRED_DIAGNOSTIC status="+rewiredDiagnostic.Status+" reason="+rewiredDiagnostic.Reason+" addAttempts="+rewiredDiagnostic.AddAttempts+" publicUpdateFeedsReturned="+rewiredDiagnostic.AppliedUpdates+" removeAttempts="+rewiredDiagnostic.RemoveAttempts+" cleanup="+rewiredDiagnostic.CleanupStatus+" "+(rewiredDiagnostic.Failure==null?"failureStage=none":rewiredDiagnostic.Failure.ToLog())+" gameConsumption=unknown");
            if(rewiredDiagnostic.Readback!=null) Logger.LogInfo("REWIRED_READBACK samples="+rewiredDiagnostic.ReadbackSamples+" sampleAgeSeconds="+(now-rewiredDiagnostic.ReadbackAtSeconds).ToString("R",System.Globalization.CultureInfo.InvariantCulture)+" "+rewiredDiagnostic.Readback.ToLog()+" gameConsumption=unknown");
        }
    }
    void TickRewiredFiveDiagnostic()
    {
        bool enabled=!quitting && active && runtimeReady && !draining && renderingActive && !tracking.Paused;
        var source=new RewiredPairSample();ulong sourceGeneration=generation;
        if(enabled && Application.isFocused && !rewiredFiveDiagnostic.Complete)
        {
            source=OpenVrRewiredPairReader.ReadSafely(system,InputClock);
            if(sourceGeneration!=generation) {source.Available=false;source.UnavailableReason="runtime_generation_changed_during_pair_read";}
        }
        double now=InputClock();rewiredFiveDiagnostic.Tick(enabled,Application.isFocused,generation,source,now);
        if(now<nextRewiredReport) return;nextRewiredReport=now+1;
        Logger.LogInfo("REWIRED_DIAGNOSTIC profile="+rewiredDiagnosticProfile+" channelCount=5 status="+rewiredFiveDiagnostic.Status+" reason="+rewiredFiveDiagnostic.Reason+" neutralReads="+rewiredFiveDiagnostic.NeutralReads+" requiredNeutralReads=3 addAttempts="+rewiredFiveDiagnostic.AddAttempts+" publicUpdateFeedsReturned="+rewiredFiveDiagnostic.AppliedUpdates+" removeAttempts="+rewiredFiveDiagnostic.RemoveAttempts+" cleanup="+rewiredFiveDiagnostic.CleanupStatus+" "+(rewiredFiveDiagnostic.Failure==null?"failureStage=none":rewiredFiveDiagnostic.Failure.ToLog())+" gameConsumption=unknown");
        if(rewiredFiveDiagnostic.Readback!=null) Logger.LogInfo("REWIRED_READBACK samples="+rewiredFiveDiagnostic.ReadbackSamples+" sampleAgeSeconds="+(now-rewiredFiveDiagnostic.ReadbackAtSeconds).ToString("R",System.Globalization.CultureInfo.InvariantCulture)+" "+rewiredFiveDiagnostic.Readback.ToLog()+" gameConsumption=unknown");
    }
    void CancelRewiredDiagnostic(string reason)
    {
        if(rewiredFiveDiagnostic!=null) {rewiredFiveDiagnostic.Cancel(reason,InputClock());nextRewiredReport=0;TickRewiredDiagnostic();}
        else if(rewiredDiagnostic!=null) {rewiredDiagnostic.Cancel(reason,InputClock());nextRewiredReport=0;TickRewiredDiagnostic();}
    }

    void TickRewiredSourceObservation()
    {
        if(rewiredSourceObservation==null) return;
        bool enabled=!quitting && active && runtimeReady && !draining && renderingActive && !tracking.Paused;
        rewiredSourceObservation.Tick(captureRewiredSourceDetails,enabled,Application.isFocused,generation,DateTime.UtcNow,Stopwatch.GetTimestamp());
        double now=InputClock();if(now<nextRewiredSourceReport) return;nextRewiredSourceReport=now+1;
        Logger.LogInfo("REWIRED_SOURCE_OBSERVATION status="+rewiredSourceObservation.Status+" reason="+rewiredSourceObservation.Reason+" windows="+rewiredSourceObservation.Windows+" samples="+rewiredSourceObservation.Samples+" error="+rewiredSourceObservation.LastError+" authorizesInput=false");
    }
    void TickRewiredPairSourceObservation()
    {
        if(rewiredPairSourceObservation==null) return;
        bool enabled=!quitting && active && runtimeReady && !draining && renderingActive && !tracking.Paused;
        rewiredPairSourceObservation.Tick(captureRewiredPairSourceDetails,enabled,Application.isFocused,generation,DateTime.UtcNow,Stopwatch.GetTimestamp());
        double now=InputClock();if(now<nextRewiredPairSourceReport)return;nextRewiredPairSourceReport=now+1;
        Logger.LogInfo("REWIRED_PAIR_SOURCE_OBSERVATION status="+rewiredPairSourceObservation.Status+" windows="+rewiredPairSourceObservation.Windows+" samples="+rewiredPairSourceObservation.Samples+" reason="+rewiredPairSourceObservation.Reason+" error="+rewiredPairSourceObservation.LastError);
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
            CancelRewiredDiagnostic("session_disabled");
            active=false; renderingActive=false; pendingRecenter=null;
            CancelGamepadDiagnostic("session_disabled",true);
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
        var poseRequests=cameraPoseCaptureRequests;
        var poseDiagnostics=cameraPoseDiagnostics;
        CameraPoseCaptureRequest poseRequest=null;
        CameraRestorationPoseRecord cameraPoseDetails=null;
        try
        {
        poseRequest=poseRequests==null?null:poseRequests.BeforePair(capturesEnabled,poseDiagnostics.Attempts<CameraRestorationPoseDiagnostics.MaximumCaptures);
        string admittedRequest=poseRequest==null?request:poseRequest.RequestPath;
        bool pairCaptureEnabled=capturesEnabled && (poseRequests==null || poseRequest!=null);
        CaptureRequestAdmission captureAdmission=CaptureRequestAdmission.BeforePair(captureNativeUiState,pairCaptureEnabled,captureOverlayCanvases,admittedRequest);
        cameraPoseDetails=poseRequest==null?null:poseDiagnostics.Begin(true,admittedRequest,camera,frameId,basePosition,baseRotation,generation,rendered+1);
        if(poseRequests!=null && poseRequests.LastError!=cameraPoseAdmissionError)
        {
            cameraPoseAdmissionError=poseRequests.LastError;
            if(!String.IsNullOrEmpty(cameraPoseAdmissionError)) Logger.LogWarning("CAMERA_POSE_CAPTURE_ADMISSION "+cameraPoseAdmissionError+"; request retained, image capture deferred.");
        }
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
        var currentContext=new CameraCurrentScope(camera,restoreBorrowedCameraCurrent);
        try
        {
        if(overlays!=null && overlays.IsolatedMask!=0) currentContext.AllowUiCamera(overlayCamera);
        if(currentContext.Conflict) throw new InvalidOperationException("Camera current experiment failed before borrow: "+currentContext.Outcome);
        using (var restore=new CameraState(camera,cameraPoseDetails))
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
                currentContext.MarkRender(camera);
                if(overlays!=null) overlays.RenderGameEye(i);else camera.Render();
                if(overlays!=null) overlays.ValidateInactiveCanvases("after game render");
                if(isolatedUi)
                {
                    overlays.ValidateLayerExclusive(i==0?"after game left":"after game right");
                    currentContext.MarkRender(overlayCamera);
                    overlays.RenderUiEye(i);
                    overlays.ValidateLayerExclusive(i==0?"after UI left":"after UI right");
                }
                if(overlays!=null) overlays.ValidateInactiveCanvases("after UI render");
                if (overlays!=null) overlays.RecordAfterRender(i);
                if(cameraPoseDetails!=null)cameraPoseDetails.Observe(i==0?"after_left_render":"after_right_render");
            }
        }
        }
        finally
        {
            try
            {
                if(overlays!=null) overlays.Dispose();
                if(overlayCamera!=null) overlayCamera.targetTexture=null;
                if(overlays!=null) overlays.RebuildRestoredGeometry();
                if(cameraPoseDetails!=null)cameraPoseDetails.Observe("after_canvas_flush");
            }
            finally
            {
                currentContext.Dispose();
                if(cameraPoseDetails!=null)cameraPoseDetails.Observe("after_current_context");
                if(overlays!=null) overlays.SetCurrentCameraRestoration(currentContext);
                if(currentContext.Conflict) Logger.LogError("CAMERA_CURRENT_OWNERSHIP_CONFLICT "+currentContext.Outcome+"; foreign state preserved or restoration unverified.");
            }
            if(overlays!=null) overlays.CaptureNativeUiAfterRestoration();
            if(currentContext.Conflict && overlays!=null) WriteUiFailureDiagnostic(overlays);
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
        if(currentContext.Conflict) throw new InvalidOperationException("Camera current ownership experiment failed: "+currentContext.Outcome);
        if (overlays!=null && overlays.RestorationConflicts>0) throw new InvalidOperationException("UI state could not be fully restored: "+overlays.RestorationError);
        if (overlays!=null && overlays.UnexpectedGeometryChanges>0) Report("UI_RENDER_STATE_CHANGED count="+overlays.UnexpectedGeometryChanges+"; inspect appliedEyes/afterRenderEyes before claiming UI stereo correctness.");
        SetEyeTextures(eyes[0].GetNativeTexturePtr(),eyes[1].GetNativeTexturePtr());
        GL.IssuePluginEvent(renderEvent,1);
        phase=2; phaseStarted=Time.realtimeSinceStartup; ++rendered;
        bool consumeCapture=poseRequests==null?captureAdmission.TryConsume(request):poseRequest!=null && poseRequest.TryConsume(captureAdmission,cameraPoseDetails!=null);
        if (consumeCapture)
        {
            if(cameraPoseDetails!=null)cameraPoseDetails.CaptureRequestConsumed=true;
            try
            {
                if(cameraPoseDetails!=null)cameraPoseDetails.EyeCaptureAttempts=1;
                CaptureEye(0);
                if(cameraPoseDetails!=null)cameraPoseDetails.EyeCaptureAttempts=2;
                CaptureEye(1);
                if(cameraPoseDetails!=null)cameraPoseDetails.EyeCapturesReturned=true;
            }
            finally {if(cameraPoseDetails!=null)cameraPoseDetails.Observe("after_eye_captures");}
            bool gameCameraRestored=cameraPoseDetails==null?
                camera.transform.position==basePosition && camera.transform.rotation==baseRotation:
                CameraRestorationComparison.Evaluate(camera,basePosition,baseRotation,cameraPoseDetails);
            if(cameraPoseDetails!=null)cameraPoseDetails.ExistingGameCameraRestored=gameCameraRestored;
            WriteCapture(head,anchor,actualEyes,gameCameraRestored);
            try { CameraDiagnostics.Write(evidenceDir,rendered,system,camera,beforeRenderProjection,overlays==null?null:overlays.DiagnosticJson()); }
            catch (Exception diagnosticError) { Logger.LogWarning("Capture diagnostics unavailable: "+diagnosticError.Message); }
            RequestMirrorCapture(Path.Combine(evidenceDir,"compositor-left-"+rendered+".bmp"));
            GL.IssuePluginEvent(renderEvent,2);
        }
        Report("VR_FRAME rendered="+rendered+" submitted="+submittedPairs+" frame="+frameId+" generation="+generation+
            (overlays==null?"":" uiOverlayRoots="+overlays.SelectedCount+" uiPlane="+overlays.PlaneDistance+" excludedUILayers="+(overlays.OverlayLayerMask & ~overlays.AppliedMask)));
        }
        finally
        {
            try
            {
                if(cameraPoseDetails!=null)
                {
                    cameraPoseDetails.Publish();
                    Logger.LogInfo("CAMERA_RESTORATION_POSE_DIAGNOSTIC attempts="+poseDiagnostics.Attempts+" error="+poseDiagnostics.LastError);
                }
            }
            finally {if(poseRequest!=null)poseRequest.Dispose();}
        }
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
        CancelGamepadDiagnostic("vr_failure",true);
        CancelRewiredDiagnostic("vr_failure");
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
    void OnDisable() {CancelGamepadDiagnostic("component_disabled");CancelRewiredDiagnostic("component_disabled");if(rewiredSourceObservation!=null) rewiredSourceObservation.Cancel("component_disabled",DateTime.UtcNow,Stopwatch.GetTimestamp());if(rewiredPairSourceObservation!=null)rewiredPairSourceObservation.Cancel("component_disabled",DateTime.UtcNow,Stopwatch.GetTimestamp());}
    void StopOwnedWork()
    {
        CancelGamepadDiagnostic("game_lifecycle_end");
        CancelRewiredDiagnostic("game_lifecycle_end");
        if(cameraPoseCaptureRequests!=null) {cameraPoseCaptureRequests.Dispose();cameraPoseCaptureRequests=null;}
        if(rewiredSourceObservation!=null) {rewiredSourceObservation.Cancel("game_lifecycle_end",DateTime.UtcNow,Stopwatch.GetTimestamp());rewiredSourceObservation=null;}
        if(rewiredPairSourceObservation!=null) {rewiredPairSourceObservation.Cancel("game_lifecycle_end",DateTime.UtcNow,Stopwatch.GetTimestamp());rewiredPairSourceObservation=null;}
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
