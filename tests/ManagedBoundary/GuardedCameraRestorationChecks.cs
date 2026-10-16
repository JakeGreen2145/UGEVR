using System;
using System.IO;
using System.Text;
using Ugevr;
using UnityEngine;

static class GuardedCameraRestorationChecks
{
    // This controlled native boundary exposes a biased world setter, exceptions
    // and callback interference. It does not emulate Unity's native transform math.
    sealed class NativeTransform : Transform
    {
        public Vector3 Local=new Vector3(1,2,3);
        public Quaternion Rotation=new Quaternion(0,0,0,1);
        public int WorldWrites,LocalWrites,LocalRotationWrites;
        public bool ThrowRotation,ThrowQuery,QuantizedWorld;
        public Action AfterLocalWrite;
        public NativeTransform()
        {
            localScale=lossyScale=new Vector3(1,1,1);
            var a=new float[16];a[0]=a[5]=a[10]=a[15]=1;
            localToWorldMatrix=worldToLocalMatrix=new Matrix4x4 {Elements=a};
        }
        public override Vector3 position {get {if(ThrowQuery)throw new InvalidOperationException("native query");return QuantizedWorld?new Vector3((float)Math.Round(Local.x,2),(float)Math.Round(Local.y,2),(float)Math.Round(Local.z,2)):Local;}set {++WorldWrites;Local=new Vector3(value.x,value.y,value.z+.00005f);}}
        public override Quaternion rotation {get {return Rotation;}set {Rotation=value;}}
        public override Vector3 localPosition {get {return Local;}set {++LocalWrites;Local=value;if(AfterLocalWrite!=null)AfterLocalWrite();}}
        public override Quaternion localRotation {get {return Rotation;}set {++LocalRotationWrites;if(ThrowRotation)throw new InvalidOperationException("native setter");Rotation=value;}}
    }
    sealed class Borrow
    {
        public readonly NativeTransform Child=new NativeTransform(),Parent=new NativeTransform();
        public readonly Camera Camera;
        public readonly CameraState Scope;
        public Borrow(bool root=false,bool quantizedWorld=false)
        {
            Child.QuantizedWorld=quantizedWorld;
            Child.parent=root?null:Parent;
            Camera=new Camera {transform=Child,rect=new Rect {x=.1f},cullingMask=17,useOcclusionCulling=true,backgroundColor=new Color {r=.2f,a=1},clearFlags=CameraClearFlags.Skybox};
            Camera.projectionMatrix=new Matrix4x4 {Marker=31};Camera.worldToCameraMatrix=new Matrix4x4 {Marker=41};
            Scope=new CameraState(Camera,null,CameraTransformRestoration.GuardedLocal);
            Child.position=new Vector3(9,8,7);Child.rotation=new Quaternion(0,0,0,1);Scope.RecordOwnedEyePose();
            Camera.targetTexture=new RenderTexture();Camera.rect=new Rect {x=.8f};Camera.cullingMask=5;Camera.useOcclusionCulling=false;Camera.backgroundColor=new Color();Camera.clearFlags=CameraClearFlags.Nothing;
            Camera.projectionMatrix=new Matrix4x4 {Marker=99};Camera.worldToCameraMatrix=new Matrix4x4 {Marker=98};
        }
    }
    static string Dispose(Borrow b)
    {try {b.Scope.Dispose();return null;}catch(InvalidOperationException error){return error.Message;}}
    static void OtherFields(Action<bool,string> check,Borrow b)
    {
        check(b.Camera.targetTexture==null&&b.Camera.rect.x==.1f&&b.Camera.cullingMask==17&&b.Camera.useOcclusionCulling,"Conflict/exception still restores target, viewport, mask and occlusion independently");
        check(b.Camera.backgroundColor.r==.2f&&b.Camera.clearFlags==CameraClearFlags.Skybox&&b.Camera.projectionMatrix.Marker==31&&b.Camera.worldToCameraMatrix.Marker==41,"Conflict/exception still restores background, clear flags, projection and view");
    }
    public static void Run(Action<bool,string> check)
    {
        check(CameraLocalRestoration.Parse("guarded-local")==CameraTransformRestoration.GuardedLocal,"Guarded restoration is an explicit independent selection");
        foreach(bool root in new[]{false,true})
        {
            var b=new Borrow(root);int world=b.Child.WorldWrites;
            check(Dispose(b)==null&&b.Child.Local.x==1&&b.Child.Local.y==2&&b.Child.Local.z==3&&b.Child.WorldWrites==world,"Stable parent or root camera restores exact canonical local pose without biased world setter");
            OtherFields(check,b);int local=b.Child.LocalWrites;b.Child.Local.x=101;b.Scope.Dispose();check(b.Child.LocalWrites==local&&b.Child.Local.x==101,"Completed guarded Dispose preserves later game changes");
        }
        foreach(string fault in new[]{"parent_replaced","parent_destroyed","parent_moved","scale","foreign_position","tiny_foreign_position","foreign_rotation","transform_replaced","query"})
        {
            var b=new Borrow();
            if(fault=="parent_replaced")b.Child.parent=new NativeTransform();
            if(fault=="parent_destroyed")UnityEngine.Object.Destroy(b.Parent);
            if(fault=="parent_moved")b.Parent.Local.x+=1;
            if(fault=="scale")b.Child.localScale=new Vector3(2,1,1);
            if(fault=="foreign_position")b.Child.Local.x+=1;
            if(fault=="tiny_foreign_position")b.Child.Local.z+=.000001f;
            if(fault=="foreign_rotation")b.Child.Rotation.w=-1;
            if(fault=="transform_replaced")b.Camera.transform=new NativeTransform();
            if(fault=="query")b.Child.ThrowQuery=true;
            int world=b.Child.WorldWrites,local=b.Child.LocalWrites;Vector3 foreign=b.Child.Local;
            string error=Dispose(b);
            check(error!=null&&error.StartsWith("Guarded camera restoration failed:")&&b.Child.WorldWrites==world&&b.Child.LocalWrites==local&&b.Child.Local.x==foreign.x&&b.Child.Local.z==foreign.z,"Guarded refusal preserves foreign pose with zero corrective writes: "+fault);
            OtherFields(check,b);
            b.Scope.Dispose();check(b.Child.WorldWrites==world&&b.Child.LocalWrites==local,"Refused Dispose is idempotent: "+fault);
        }
        var missing=new Borrow();var unowned=new CameraState(missing.Camera,null,CameraTransformRestoration.GuardedLocal);
        bool missingThrew=false;try {unowned.Dispose();}catch(InvalidOperationException){missingThrew=true;}check(missingThrew,"Missing effective eye ownership cannot authorize local restoration");Dispose(missing);
        var quantized=new Borrow(quantizedWorld:true);Vector3 worldBefore=quantized.Child.position;quantized.Child.Local.z+=.000001f;
        check(quantized.Child.position.z==worldBefore.z,"Controlled local foreign change is hidden by unchanged world readback");
        check(Dispose(quantized).Contains("owned_eye_local_pose_changed")&&quantized.Child.LocalWrites==0&&quantized.Child.WorldWrites==1,"Raw local ownership independently prevents overwriting foreign pose hidden by world quantization");OtherFields(check,quantized);
        var destroyed=new Borrow();UnityEngine.Object.Destroy(destroyed.Camera);check(Dispose(destroyed)!=null&&destroyed.Child.LocalWrites==0,"Destroyed camera refuses pair completion rather than silently succeeding");
        var partial=new Borrow();partial.Child.ThrowRotation=true;check(Dispose(partial)!=null&&partial.Child.Local.z==3,"Partial local setter failure throws without a world fallback");OtherFields(check,partial);
        var targetFailure=new Borrow();targetFailure.Camera.TargetSetter=value=>{if(value==null)throw new InvalidOperationException("target setter");};
        check(Dispose(targetFailure)!=null&&targetFailure.Child.Local.z==3&&targetFailure.Camera.rect.x==.1f&&targetFailure.Camera.projectionMatrix.Marker==31&&targetFailure.Camera.worldToCameraMatrix.Marker==41,"An early target exception still attempts pose, viewport and both matrices");
        var post=new Borrow();post.Child.AfterLocalWrite=()=>post.Parent.Local.x+=1;
        check(Dispose(post)!=null&&post.Child.LocalWrites==1&&post.Child.WorldWrites==1,"Mid-restore parent change refuses completion without second correction");OtherFields(check,post);
        var render=new Borrow();bool submitted=false,threw=false;
        try {using(render.Scope){render.Camera.RenderAction=()=>{throw new InvalidOperationException("original Render");};render.Camera.Render();}submitted=true;}catch(InvalidOperationException){threw=true;}
        check(threw&&!submitted&&render.Child.Local.z==3,"Render exception restores canonical pose and cannot reach submission continuation");OtherFields(check,render);
        var conflict=new Borrow();conflict.Child.Local.x+=1;submitted=false;
        try {using(conflict.Scope){}submitted=true;}catch(InvalidOperationException){}
        check(!submitted,"A guarded restoration conflict blocks the caller's post-scope submission path");
        var invalid=new NativeTransform();invalid.localScale=new Vector3(float.NaN,1,1);bool admission=false;
        try {new CameraState(new Camera {transform=invalid},null,CameraTransformRestoration.GuardedLocal);}catch(InvalidOperationException){admission=true;}
        check(admission&&invalid.WorldWrites==0&&invalid.LocalWrites==0,"Nonfinite saved context refuses admission before eye borrowing or any pose setter");
        var snapshotCamera=new Camera {transform=new NativeTransform()};snapshotCamera.projectionMatrix=new Matrix4x4 {Marker=31};snapshotCamera.worldToCameraMatrix=new Matrix4x4 {Marker=41};
        snapshotCamera.ResetViewAction=()=>{snapshotCamera.worldToCameraMatrix=new Matrix4x4 {Marker=99};throw new InvalidOperationException("snapshot query");};
        snapshotCamera.ProjectionSetter=value=>{if(value.Marker==31)throw new InvalidOperationException("restore projection");};
        bool snapshotFailed=false;try {new CameraState(snapshotCamera,null,CameraTransformRestoration.GuardedLocal);}catch(InvalidOperationException){snapshotFailed=true;}
        check(snapshotFailed&&snapshotCamera.worldToCameraMatrix.Marker==41,"Constructor snapshot/restore exceptions still restore the independent saved view before refusing admission");
        Diagnostics(check);
    }
    static void Diagnostics(Action<bool,string> check)
    {
        string root=Path.Combine(Path.GetTempPath(),"ugevr-guarded-camera-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var child=new NativeTransform();child.parent=new NativeTransform();var camera=new Camera {transform=child};
            string request=Path.Combine(root,"capture-request");File.WriteAllText(request,"");
            var record=new CameraRestorationPoseDiagnostics(root).Begin(true,request,camera,1);
            using(var scope=new CameraState(camera,record,CameraTransformRestoration.GuardedLocal))
            {child.position=new Vector3(9,8,7);scope.RecordOwnedEyePose();record.Observe("after_left_render");record.Observe("after_right_render");}
            record.ExistingGameCameraRestored=CameraRestorationComparison.Evaluate(camera,new Vector3(1,2,3),new Quaternion(0,0,0,1),record);
            string yaml=record.ToPrivateYaml();
            check(yaml.Contains("selected_restoration_mode: guarded-local")&&yaml.Contains("guarded_local_restoration:")&&!yaml.Contains("local_restoration_experiment:"),"Guarded capture reports its actual selection without pretending to use legacy fallback experiment");
            check(yaml.Contains("applied_space: local")&&yaml.Contains("restoration_returned: true")&&yaml.Contains("post_context: unchanged")&&yaml.Contains("existing_game_camera_restored: true"),"Guarded capture retains actual post-context, completion and unchanged aggregate predicate results");
            check(Encoding.UTF8.GetByteCount(yaml)<=CameraRestorationPoseDiagnostics.MaximumBytes,"Guarded diagnostics retain the existing complete-record byte cap");
        }
        finally
        {
            string resolved=Path.GetFullPath(root),parent=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
            if(Path.GetDirectoryName(resolved)!=parent)throw new Exception("Unsafe guarded fixture cleanup.");
            Directory.Delete(resolved,true);
        }
    }
}
