using System;
using System.Text.Json;
using UnityEngine;
using Ugevr;

static class NativeUiDiagnosticsChecks
{
    class OldAlpha {public float GetAlpha() {return 1;}}
    class VirtualAlpha {public virtual float GetInheritedAlpha() {return 1;}}
    class WrongAlpha {public int GetInheritedAlpha() {return 1;}}
    static OverlayCanvasScope Scope(Camera camera,CanvasRenderer renderer,bool capture)
    {
        var rect=new RectTransform {sizeDelta=new Vector2(1400,800),localScale=new Vector3(1,1,1),pivot=new Vector2(.5f,.5f)};
        var canvas=new Canvas {renderMode=RenderMode.ScreenSpaceOverlay,Rects=new[]{rect},Renderers=new[]{renderer},gameObject=rect.gameObject};Canvas.Instances.Add(canvas);
        return new OverlayCanvasScope(camera,1,2,false,1.8,false,null,capture);
    }
    public static void Run(Action<bool,string> assert)
    {
        Canvas.Instances.Clear();Resources.Extras.Clear();Resources.QueryTypes.Clear();Camera.current=null;RenderTexture.active=null;RenderTexture.ThrowActive=false;
        try
        {
            foreach(bool configured in new[]{false,true}) foreach(bool images in new[]{false,true}) foreach(bool requested in new[]{false,true})
                assert(NativeUiStateDiagnostics.ShouldCapture(configured,images,requested)==(configured && images && requested),"Native UI sampling requires all three admission conditions");
            var camera=new Camera {nearClipPlane=.3f,farClipPlane=100};var renderer=new CanvasRenderer();Camera.CurrentQueries=0;RenderTexture.ActiveQueries=0;
            var uncaptured=Scope(camera,renderer,false);uncaptured.SetCommonHead(new RigidPose(new Position(0,0,0),Orientation.Identity));uncaptured.ApplyEye();uncaptured.RenderGameEye(0);uncaptured.Dispose();uncaptured.RebuildRestoredGeometry();uncaptured.CaptureNativeUiAfterRestoration();
            assert(renderer.NativeQueries==0 && Camera.CurrentQueries==0 && RenderTexture.ActiveQueries==0 && uncaptured.DiagnosticJson().Contains("\"nativeUiState\":null"),"Default or unrequested scope performs no native UI or render-context getter sampling");
            Canvas.Instances.Clear();
            var originalTarget=new RenderTexture();camera.targetTexture=originalTarget;RenderTexture.active=originalTarget;
            renderer=new CanvasRenderer {name="quoted \" renderer\n",AlphaValue=.8f,InheritedAlphaValue=.4f};
            var captured=Scope(camera,renderer,true);captured.SetCommonHead(new RigidPose(new Position(0,0,0),Orientation.Identity));
            int admittedIdQueries=renderer.InstanceIdQueries;
            using(var restore=new CameraState(camera))
            {
                captured.ApplyEye();camera.targetTexture=new RenderTexture();Camera.current=camera;
                camera.RenderAction=delegate {renderer.CullValue=true;renderer.ClipValue=true;renderer.InheritedAlphaValue=0;};captured.RenderGameEye(0);
                camera.RenderAction=delegate {renderer.CullValue=false;renderer.InheritedAlphaValue=.2f;};captured.RenderGameEye(1);camera.RenderAction=null;
                captured.Dispose();
            }
            captured.RebuildRestoredGeometry();captured.CaptureNativeUiAfterRestoration();
            int queries=renderer.NativeQueries;captured.DiagnosticJson();captured.CaptureNativeUiAfterRestoration();
            assert(renderer.NativeQueries==queries && renderer.InstanceIdQueries==admittedIdQueries,"Serializing diagnostics and repeated phase requests perform no additional renderer queries");
            using(var document=JsonDocument.Parse(captured.DiagnosticJson()))
            {
                JsonElement native=document.RootElement.GetProperty("nativeUiState"),node=native.GetProperty("renderers")[0],phases=node.GetProperty("phases"),contexts=native.GetProperty("contexts");
                assert(phases[0].GetProperty("cull").GetBoolean()==false && phases[1].GetProperty("cull").GetBoolean() && phases[3].GetProperty("inheritedAlpha").GetSingle()==.2f,"Requested capture freezes native fields before borrowing and after separate eye renders");
                assert(contexts[0].GetProperty("selectedCameraTargetId").GetInt32()==originalTarget.GetInstanceID() && contexts[5].GetProperty("selectedCameraTargetId").GetInt32()==originalTarget.GetInstanceID(),"Native UI context records the original target after outer camera restoration");
                assert(!phases[2].GetProperty("captured").GetBoolean() && native.GetProperty("capturedPhases").GetInt32()==4,"Unused separate UI passes remain explicitly uncaptured");
                assert(native.GetProperty("queryErrors").GetInt32()==0 && !native.GetProperty("nativeStateRestored").GetBoolean() && renderer.InheritedAlphaValue==.2f && renderer.ClipValue,"Native sampling preserves changed native state and does not excuse it through restoration writes");
            }
            assert(Resources.QueryTypes.Count==0,"Native UI sampling does not add global Resources queries");
            var bounded=new NativeUiStateDiagnostics(camera);var overflow=new CanvasRenderer();
            for(int i=0;i<NativeUiStateDiagnostics.RendererLimit;++i) bounded.Admit(new CanvasRenderer(),17);
            bounded.Admit(overflow,17);bounded.Capture(0);
            using(var document=JsonDocument.Parse(bounded.DiagnosticJson()))
                assert(document.RootElement.GetProperty("admittedRenderers").GetInt32()==1024 && document.RootElement.GetProperty("droppedOccurrences").GetInt32()==1 && overflow.NativeQueries==0 && overflow.InstanceIdQueries==0,"Renderer cap reports dropped occurrences without querying objects beyond admission");
            var duplicate=new NativeUiStateDiagnostics(camera);renderer=new CanvasRenderer();
            for(int i=0;i<NativeUiStateDiagnostics.AdmissionOccurrenceLimit+3;++i) duplicate.Admit(renderer,17);
            using(var document=JsonDocument.Parse(duplicate.DiagnosticJson()))
                assert(renderer.InstanceIdQueries==8192 && document.RootElement.GetProperty("droppedOccurrences").GetInt32()==3,"Repeated admission occurrences also have a fixed native identity-query bound");
            var failed=new NativeUiStateDiagnostics(camera);renderer=new CanvasRenderer {ThrowCull=true,ThrowInherited=true,AlphaValue=Single.NaN};failed.Admit(renderer,17);RenderTexture.ThrowActive=true;failed.Capture(0);RenderTexture.ThrowActive=false;renderer.Destroyed=true;failed.Capture(1);
            using(var document=JsonDocument.Parse(failed.DiagnosticJson()))
                assert(document.RootElement.GetProperty("queryErrors").GetInt32()==3 && document.RootElement.GetProperty("nonfiniteValues").GetInt32()==1 && document.RootElement.GetProperty("destroyedSamples").GetInt32()==1,"Getter errors, nonfinite native values and destroyed admitted objects remain bounded explicit observations");
            assert(NativeUiStateDiagnostics.FindCachedFloatMethod(typeof(OldAlpha),"GetInheritedAlpha")==null && NativeUiStateDiagnostics.FindCachedFloatMethod(typeof(VirtualAlpha),"GetInheritedAlpha")==null && NativeUiStateDiagnostics.FindCachedFloatMethod(typeof(WrongAlpha),"GetInheritedAlpha")==null,"Missing, virtual or incorrectly typed inherited-alpha methods are not invoked");
            Canvas.Instances.Clear();renderer=new CanvasRenderer();captured=Scope(camera,renderer,true);captured.SetCommonHead(new RigidPose(new Position(0,0,0),Orientation.Identity));captured.ApplyEye();
            camera.RenderAction=delegate {throw new InvalidOperationException("render failed");};bool threw=false;
            try {captured.RenderGameEye(0);} catch(InvalidOperationException) {threw=true;}finally {camera.RenderAction=null;captured.Dispose();captured.RebuildRestoredGeometry();captured.CaptureNativeUiAfterRestoration();}
            assert(threw && captured.DiagnosticJson().Contains("after-game-left") && renderer.NativeQueries>0,"Failed render preserves exception propagation while native observations and owned restoration still run");
        }
        finally {Canvas.Instances.Clear();Resources.Extras.Clear();Resources.QueryTypes.Clear();Camera.current=null;RenderTexture.active=null;RenderTexture.ThrowActive=false;}
    }
}
