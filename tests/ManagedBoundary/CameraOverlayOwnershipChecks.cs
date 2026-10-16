using System;
using Ugevr;
using UnityEngine;

static class CameraOverlayOwnershipChecks
{
    // Translation-only controlled hierarchy. This is not Unity native matrix,
    // RectTransform layout, floating-point conversion or callback evidence.
    sealed class ParentCoupledTransform : Transform
    {
        public override Vector3 position
        {
            get {var p=parent.position;var l=localPosition;return new Vector3(p.x+l.x,p.y+l.y,p.z+l.z);}
            set {var p=parent.position;localPosition=new Vector3(value.x-p.x,value.y-p.y,value.z-p.z);}
        }
    }
    static RectTransform Rect(float x,float y,float z)
    {
        return new RectTransform {sizeDelta=new Vector2(1920,1080),pivot=new Vector2(.5f,.5f),
            localPosition=new Vector3(x,y,z),localRotation=new Quaternion(0,0,0,1),localScale=new Vector3(1,1,1)};
    }
    static bool Exact(Vector3 a,Vector3 b) {return a.x==b.x&&a.y==b.y&&a.z==b.z;}
    public static void Run(Action<bool,string> check)
    {
        Canvas.Instances.Clear();Canvas.ForceAction=null;
        try
        {
            Exercise(check,"disjoint",0);
            Exercise(check,"camera_is_overlay_root",1);
            Exercise(check,"camera_is_overlay_descendant_rect",2);
            Exercise(check,"camera_parent_is_overlay_root",3);
        }
        finally {Canvas.Instances.Clear();Canvas.ForceAction=null;}
    }
    static void Exercise(Action<bool,string> check,string name,int shape)
    {
        Canvas.Instances.Clear();var root=Rect(1,2,3);var cameraRect=Rect(4,5,6);
        cameraRect.parent=root;
        Transform selected=shape==1?root:shape==2?cameraRect:shape==3?
            new ParentCoupledTransform {parent=root,localPosition=new Vector3(4,5,6),localRotation=new Quaternion(0,0,0,1),localScale=new Vector3(1,1,1)}:
            new Transform {localPosition=new Vector3(4,5,6),localRotation=new Quaternion(0,0,0,1),localScale=new Vector3(1,1,1)};
        var camera=new Camera {transform=selected,nearClipPlane=.1f,farClipPlane=100,targetDisplay=0};
        var canvas=new Canvas {name=name,Rects=shape==2?new[]{root,cameraRect}:new[]{root},renderMode=RenderMode.ScreenSpaceOverlay};
        Canvas.Instances.Add(canvas);
        int renders=0;camera.RenderAction=delegate {++renders;};
        // Production snapshots overlays before CameraState, and disposes overlays
        // first. RecordOwnedEyePose is before ApplyEye for both eyes.
        var overlays=new OverlayCanvasScope(camera,1,2,false);
        overlays.SetCommonHead(new RigidPose(new Position(0,0,0),Orientation.Identity));
        Vector3 owned=new Vector3(),afterOverlay=new Vector3(),afterApply=new Vector3();
        string failure=null;CameraLocalRestoration outcome=null;
        try
        {
            using(var restore=new CameraState(camera,null,CameraTransformRestoration.GuardedLocal))
            {
                outcome=restore.CachedLocalRestoration;
                using(overlays)
                {
                    for(int eye=0;eye<2;++eye)
                    {
                        camera.transform.position=new Vector3(9+eye,8,7);
                        camera.transform.rotation=new Quaternion(0,0,0,1);
                        restore.RecordOwnedEyePose();owned=camera.transform.position;
                        overlays.ApplyEye(eye);afterApply=camera.transform.position;
                        overlays.RenderGameEye(eye);overlays.RecordAfterRender(eye);
                    }
                }
                afterOverlay=camera.transform.position;
            }
        }
        catch(InvalidOperationException error) {failure=error.Message;}
        check(renders==2,"Controlled overlap executes exactly two no-op Camera.Render callbacks: "+name);
        check(overlays.RestorationConflicts==0,"Overlay cleanup restores its own admitted rect values: "+name);
        if(shape==0)
        {
            check(failure==null,"Disjoint camera and overlay restores successfully without callback mutation");
            check(Exact(owned,afterApply)&&Exact(owned,afterOverlay),"Disjoint overlay does not mutate the last camera eye pose");
        }
        else if(shape==1 || shape==2)
        {
            check(failure==null && outcome.AppliedSpace=="already_restored_no_write" && outcome.RestoreReturned,"Exact saved camera state restored by inner overlay scope admits explicit no-write completion: "+name);
            check(!Exact(owned,afterOverlay) && Exact(afterOverlay,camera.transform.position),"Admission preserves exact already-restored pose rather than reapplying an eye or a correction: "+name);
        }
        else
        {
            check(failure!=null&&failure.Contains("owned_eye_position_changed"),"Parent-only overlay restoration does not admit a camera that differs from its saved state: "+name);
            check(!Exact(owned,afterOverlay),"Inner overlay cleanup changes the last sampled eye before outer guard: "+name);
            check(Exact(afterOverlay,camera.transform.position),"Guard preserves the camera pose present after inner overlay disposal: "+name);
        }
        Console.WriteLine("CONTROLLED_OVERLAY_OWNERSHIP name="+name+" renders="+renders+" refusal="+(failure??"none")+
            " owned="+owned.x+","+owned.y+","+owned.z+" after_apply="+afterApply.x+","+afterApply.y+","+afterApply.z+
            " after_overlay_dispose="+afterOverlay.x+","+afterOverlay.y+","+afterOverlay.z);
    }
}
