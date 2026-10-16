using System;
using Ugevr;
using UnityEngine;

static class OverlayPairPreparationChecks
{
    // Controlled translation coupling only. Native RectTransform layout,
    // matrices, Camera.Render and game callbacks are not reproduced here.
    sealed class Child : RectTransform
    {
        public override Vector3 position
        {
            get {var p=parent.position;var l=localPosition;return new Vector3(p.x+l.x,p.y+l.y,p.z+l.z);}
            set {var p=parent.position;localPosition=new Vector3(value.x-p.x,value.y-p.y,value.z-p.z);}
        }
    }
    sealed class Clip : IPanelClipAccess
    {
        public int Captures,Enables,Disables;public bool Active;
        readonly object token=new object();
        public bool InheritedAlphaAvailable {get {return false;}}
        public bool SoftnessAvailable {get {return false;}}
        public PanelClipSnapshot Capture(bool borrowed)
        {
            ++Captures;
            return new PanelClipSnapshot {Inventory=new[]{token},RootPlacement=new[]{borrowed?1f:0f},Nodes=new[]{new PanelClipNode {
                Renderer=token,Root=token,Parent=token,Graphic=token,ChildRect=token,RendererObject=token,
                RendererId=1,RootId=1,ParentId=1,GraphicId=1,ChildRectId=1,RendererObjectId=1,CanonicalGraphic="Image",
                Panel=new Rect(0,0,100,50),ConvertedPanel=new Rect(0,0,100,50),Child=new Rect(0,0,10,10),
                Clip=Active,Color=new Color {a=1},Alpha=1,Materials=new[]{token},Shaders=new[]{token},PopMaterials=new object[0],
                MaterialIds=new[]{1},ShaderIds=new[]{1},PopMaterialIds=new int[0]}}};
        }
        public void Enable(object value,Rect rectangle) {++Enables;Active=true;}
        public void Disable(object value) {++Disables;Active=false;}
    }
    static bool Exact(Vector3 a,Vector3 b) {return a.x==b.x&&a.y==b.y&&a.z==b.z;}
    static bool Exact(Quaternion a,Quaternion b) {return a.x==b.x&&a.y==b.y&&a.z==b.z&&a.w==b.w;}
    static RectTransform Root() {return new RectTransform {localPosition=new Vector3(1,2,3),localRotation=new Quaternion(0,0,0,1),localScale=new Vector3(1,1,1),sizeDelta=new Vector2(1920,1080),pivot=new Vector2(.5f,.5f)};}
    public static void Run(Action<bool,string> check)
    {
        try
        {
            RenderChecks(check,false,false);RenderChecks(check,true,false);
            RenderChecks(check,false,true);RenderChecks(check,true,true);
            LazyFirstEyeShowsRisk(check);
            FailureChecks(check,false);FailureChecks(check,true);ZeroAndAdmissionChecks(check);ClipChecks(check);
        }
        finally {Canvas.Instances.Clear();Canvas.ForceAction=null;Resources.Extras.Clear();LayerMask.Name=null;}
    }
    static void RenderChecks(Action<bool,string> check,bool cameraIsRoot,bool guarded)
    {
        Canvas.Instances.Clear();var root=Root();
        Transform transform=cameraIsRoot?(Transform)root:new Child {parent=root,localPosition=new Vector3(4,5,6),localRotation=new Quaternion(0,0,0,1),localScale=new Vector3(1,1,1)};
        var canvas=new Canvas {Rects=cameraIsRoot?new[]{root}:new[]{root,(RectTransform)transform},renderMode=RenderMode.ScreenSpaceOverlay};Canvas.Instances.Add(canvas);
        var camera=new Camera {transform=transform,nearClipPlane=.1f,farClipPlane=100,cullingMask=123};
        var original=camera.transform.position;int flushes=0,renders=0;Canvas.ForceAction=()=>++flushes;
        var overlays=new OverlayCanvasScope(camera,1,2,false);overlays.SetCommonHead(new RigidPose(new Position(0,0,0),Orientation.Identity));
        var saved=new CameraState(camera,null,guarded?CameraTransformRestoration.GuardedLocal:CameraTransformRestoration.World);
        using(saved)
        using(overlays)
        {
            overlays.PrepareForPair();int preparedFlushes=flushes;var placed=root.position;
            check(preparedFlushes==1&&canvas.renderMode==RenderMode.WorldSpace,"Pair preparation performs shared layout before eye assignment");
            overlays.PrepareForPair();check(flushes==preparedFlushes,"Repeated pair preparation does not repeat shared layout");
            for(int eye=0;eye<2;++eye)
            {
                var expected=new Vector3(9+eye,8,7);var rotation=new Quaternion(0,.6f,0,.8f);camera.transform.position=expected;camera.transform.rotation=rotation;
                saved.RecordOwnedEyePose();camera.ResetWorldToCameraMatrix();camera.targetTexture=new RenderTexture();camera.projectionMatrix=new Matrix4x4 {Marker=31+eye};camera.rect=new Rect(0,0,1,1);
                overlays.ApplyEye(eye);
                check(Exact(camera.transform.position,expected)&&flushes==preparedFlushes,"ApplyEye preserves assigned eye after explicit preparation");
                var ui=new Camera();OverlayCameraPass.Prepare(camera,ui,1<<20);
                camera.RenderAction=()=>{++renders;check(Exact(camera.transform.position,expected)&&Exact(camera.transform.rotation,rotation)&&camera.worldToCameraMatrix.Marker==12+expected.x,"Game render callback sees the assigned eye pose and automatic view");};
                ui.RenderAction=()=>check(Exact(ui.transform.position,expected)&&Exact(ui.transform.rotation,rotation)&&ui.worldToCameraMatrix.Marker==12+expected.x&&ui.projectionMatrix.Marker==31+eye&&ui.targetTexture==camera.targetTexture&&ui.rect.width==1,"Independent UI render copies the assigned eye pose, view, projection and target");
                overlays.RenderGameEye(eye);OverlayCameraPass.Render(ui);
                if(!cameraIsRoot)check(Exact(root.position,placed),"Assigning a descendant camera eye leaves the prepared panel root fixed");
                else check(Exact(root.position,expected),"Camera-as-root remains a disclosed panel-role coupling, not an independent UI fidelity guarantee");
            }
        }
        check(renders==2&&Exact(camera.transform.position,original)&&canvas.renderMode==RenderMode.ScreenSpaceOverlay,"Prepared pair retains nested camera and overlay cleanup");
        if(guarded)check(saved.CachedLocalRestoration.AlreadyRestoredNoWrite && saved.CachedLocalRestoration.RestoreReturned,"Prepared eye pair and exact already-restored guard compose for overlapping camera/UI roles");
    }
    static void LazyFirstEyeShowsRisk(Action<bool,string> check)
    {
        Canvas.Instances.Clear();var root=Root();Canvas.Instances.Add(new Canvas {Rects=new[]{root}});
        var camera=new Camera {transform=root,nearClipPlane=.1f,farClipPlane=100};
        var overlays=new OverlayCanvasScope(camera,1,2,false);overlays.SetCommonHead(new RigidPose(new Position(0,0,0),Orientation.Identity));
        var eye=new Vector3(9,8,7);camera.transform.position=eye;camera.ResetWorldToCameraMatrix();
        overlays.ApplyEye(0);
        camera.RenderAction=()=>check(!Exact(camera.transform.position,eye)&&camera.worldToCameraMatrix.Marker!=12+eye.x,"Old lazy first-eye placement can change the rendered eye without a game callback");
        overlays.RenderGameEye(0);overlays.Dispose();Canvas.ForceAction=null;
    }
    static void FailureChecks(Action<bool,string> check,bool failDuringPlacement)
    {
        Canvas.Instances.Clear();Resources.Extras.Clear();var root=Root();root.gameObject.layer=5;
        var canvas=new Canvas {Rects=new[]{root},gameObject=root.gameObject,renderMode=RenderMode.ScreenSpaceOverlay};Canvas.Instances.Add(canvas);
        var camera=new Camera {nearClipPlane=.1f,farClipPlane=100,cullingMask=-1};LayerMask.Name=layer=>"named";
        var overlays=new OverlayCanvasScope(camera,1,2,false);overlays.SetCommonHead(new RigidPose(new Position(0,0,0),Orientation.Identity));overlays.UseAfterEffectsCamera(new Camera(),20);
        bool failed=false;int rendered=0;camera.RenderAction=()=>++rendered;int flush=0;
        Canvas.ForceAction=()=>{if(++flush==1){check(root.gameObject.layer==20&&camera.cullingMask==~(1<<20),"Layer lease and game culling are applied before preparation layout");if(!failDuringPlacement)throw new InvalidOperationException("controlled preparation callback");}};
        bool firstRotation=true;
        if(failDuringPlacement)root.LocalRotationSetter=value=>{if(firstRotation){firstRotation=false;throw new InvalidOperationException("controlled preparation callback");}return value;};
        try {using(var saved=new CameraState(camera))using(overlays){overlays.PrepareForPair();camera.Render();}}
        catch(InvalidOperationException error){failed=error.Message=="controlled preparation callback";}
        check(failed&&rendered==0&&root.gameObject.layer==5&&camera.cullingMask==-1&&canvas.renderMode==RenderMode.ScreenSpaceOverlay&&Exact(root.position,new Vector3(1,2,3)),"Preparation failure unwinds partial placement, layer, canvas and camera before any eye render");
        Canvas.ForceAction=null;
        bool disposed=false;try {overlays.PrepareForPair();}catch(ObjectDisposedException){disposed=true;}check(disposed,"Preparation after disposal refuses");
    }
    static void ZeroAndAdmissionChecks(Action<bool,string> check)
    {
        Canvas.Instances.Clear();Resources.Extras.Clear();int flushes=0;Canvas.ForceAction=()=>++flushes;
        var camera=new Camera {nearClipPlane=.1f,farClipPlane=100};var empty=new OverlayCanvasScope(camera,1,2,false);
        int queries=Resources.QueryTypes.Count;empty.PrepareForPair();empty.ApplyEye(0);empty.Dispose();
        check(flushes==0&&queries==Resources.QueryTypes.Count,"Zero-root preparation makes no layout or layer query");
        var root=Root();Canvas.Instances.Add(new Canvas {Rects=new[]{root}});var missing=new OverlayCanvasScope(camera,1,2,false);
        bool refused=false;try {missing.PrepareForPair();}catch(InvalidOperationException){refused=true;}finally {missing.Dispose();}
        check(refused&&flushes==0,"Missing common head refuses before shared layout");
        Canvas.ForceAction=null;
    }
    static void ClipChecks(Action<bool,string> check)
    {
        Canvas.Instances.Clear();Resources.Extras.Clear();var root=Root();Canvas.Instances.Add(new Canvas {Rects=new[]{root}});
        var fake=new Clip();var camera=new Camera {nearClipPlane=.1f,farClipPlane=100};var ui=new Camera();LayerMask.Name=layer=>"named";
        var overlays=new OverlayCanvasScope(camera,1,2,false,1.8,false,null,false,1024,UiPanelClipping.CanonicalUguiDefaultRootV1,roots=>new UiPanelClipScope(fake));
        overlays.SetCommonHead(new RigidPose(new Position(0,0,0),Orientation.Identity));overlays.UseAfterEffectsCamera(ui,20);
        int admission=fake.Captures;overlays.PrepareForPair();
        check(fake.Captures==admission&&fake.Enables==0,"Pair preparation does not advance clipping placement evidence or enable native clipping");
        camera.transform.position=new Vector3(9,8,7);camera.ResetWorldToCameraMatrix();camera.targetTexture=new RenderTexture();
        overlays.ApplyEye(0);check(fake.Captures==admission+1&&fake.Enables==0,"Clipping placement remains in ApplyEye after eye assignment");
        overlays.ValidateLayerExclusive("before controlled game eye");OverlayCameraPass.Prepare(camera,ui,overlays.IsolatedMask);
        camera.RenderAction=()=>check(!fake.Active,"Game render remains outside clipping");ui.RenderAction=()=>check(fake.Active,"UI render remains inside clipping");
        overlays.RenderGameEye(0);overlays.RenderUiEye(0);overlays.ValidateLayerExclusive("after controlled UI eye");overlays.Dispose();
        check(fake.Enables==1&&fake.Disables==1&&!fake.Active&&overlays.RestorationConflicts==0,"Prepared clipping scope preserves bounded owned cleanup");
    }
}
