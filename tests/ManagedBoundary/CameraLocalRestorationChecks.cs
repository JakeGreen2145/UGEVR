using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Ugevr;
using UnityEngine;

static class CameraLocalRestorationChecks
{
    // Controlled native boundary: world writes have a deliberate bias; local writes do not.
    // This checks policy/ownership, not Unity's native conversion implementation.
    sealed class Probe : Transform
    {
        public Vector3 World=new Vector3(1,2,3),Local=new Vector3(1,2,3),Scale=new Vector3(1,1,1),Lossy=new Vector3(1,1,1);
        public Quaternion WorldRotation=new Quaternion(0,0,0,1),LocalRotation=new Quaternion(0,0,0,1);
        public Matrix4x4 Forward=new Matrix4x4 {Elements=Identity()},Inverse=new Matrix4x4 {Elements=Identity()};
        public bool BiasedWorld,ThrowMatrix,ThrowLocalRotation,Linked;
        public int WorldWrites,LocalWrites,RotationWrites,LocalRotationWrites,LocalReads,ScaleReads,MatrixReads;
        public readonly List<string> Calls=new List<string>();
        public Action LocalWrite;
        static float[] Identity() {var a=new float[16];a[0]=a[5]=a[10]=a[15]=1;return a;}
        public override Vector3 position {get {Calls.Add("get_world");return Linked?Local:World;} set {Calls.Add("set_world");++WorldWrites;World=value;if(Linked)Local=new Vector3(value.x,value.y,value.z+(BiasedWorld?.00005f:0));}}
        public override Quaternion rotation {get {Calls.Add("get_rotation");return Linked?LocalRotation:WorldRotation;} set {Calls.Add("set_rotation");++RotationWrites;WorldRotation=value;if(Linked)LocalRotation=value;}}
        public override Vector3 localPosition {get {++LocalReads;Calls.Add("get_local");return Local;} set {++LocalWrites;Calls.Add("set_local");Local=value;if(LocalWrite!=null)LocalWrite();}}
        public override Quaternion localRotation {get {++LocalReads;Calls.Add("get_local_rotation");return LocalRotation;} set {++LocalRotationWrites;Calls.Add("set_local_rotation");if(ThrowLocalRotation)throw new InvalidOperationException("private-native-failure");LocalRotation=value;}}
        public override Vector3 localScale {get {++ScaleReads;Calls.Add("get_scale");return Scale;} set {Scale=value;}}
        public override Vector3 lossyScale {get {++ScaleReads;return Lossy;} set {Lossy=value;}}
        public override Matrix4x4 localToWorldMatrix {get {++MatrixReads;Calls.Add("get_forward");if(ThrowMatrix)throw new InvalidOperationException("private-query");return Forward;} set {Forward=value;}}
        public override Matrix4x4 worldToLocalMatrix {get {++MatrixReads;Calls.Add("get_inverse");return Inverse;} set {Inverse=value;}}
    }
    sealed class Case
    {
        public readonly Probe Child=new Probe {Linked=true,BiasedWorld=true},Parent=new Probe();
        public readonly Camera Camera;
        public readonly CameraRestorationPoseRecord Record;
        public readonly CameraState Scope;
        public Case(string root,int number,bool owned=true)
        {
            Camera=new Camera {transform=Child};Child.parent=Parent;
            string folder=Path.Combine(root,number.ToString());Directory.CreateDirectory(folder);
            string request=Path.Combine(folder,"capture-request");File.WriteAllText(request,"");
            Record=new CameraRestorationPoseDiagnostics(folder).Begin(true,request,Camera,1);
            Scope=new CameraState(Camera,Record,CameraTransformRestoration.LocalWithWorldFallback);
            Eye(owned);
        }
        public void Eye(bool owned)
        {
            Camera.transform.position=new Vector3(9,8,7);Camera.transform.rotation=new Quaternion(0,0,0,1);
            if(owned)Scope.RecordOwnedEyePose();
        }
        public string Finish() {Scope.Dispose();return Record.ToPrivateYaml();}
    }
    static void Refused(Action<bool,string> check,Case c,string reason)
    {
        int world=c.Child.WorldWrites,local=c.Child.LocalWrites;
        string b=c.Finish();
        check(b.Contains("applied_space: world_fallback")&&b.Contains("refusal_or_fallback_reason: "+reason),"Explicit fallback reports "+reason);
        check(c.Child.WorldWrites==world+1&&c.Child.LocalWrites==local,"Refusal makes the existing world write only: "+reason);
    }
    public static void Run(Action<bool,string> check)
    {
        check(CameraLocalRestoration.Parse("world")==CameraTransformRestoration.World&&CameraLocalRestoration.Parse("local-with-world-fallback")==CameraTransformRestoration.LocalWithWorldFallback,"Only explicit documented restoration selections are supported");
        bool invalid=false;try {CameraLocalRestoration.Parse("local");}catch(ArgumentException){invalid=true;}check(invalid,"Unknown restoration selection fails rather than silently activating");
        // Compare legacy constructor and explicit world with actual public boundary traces.
        var traces=new List<string>();
        for(int i=0;i<2;++i)
        {
            var child=new Probe {Linked=true,BiasedWorld=true};var parent=new Probe();child.parent=parent;var camera=new Camera {transform=child};
            var scope=i==0?new CameraState(camera):new CameraState(camera,null,CameraTransformRestoration.World);
            scope.RecordOwnedEyePose();camera.transform.position=new Vector3(9,8,7);camera.transform.rotation=new Quaternion(0,0,0,1);scope.Dispose();
            check(child.LocalReads==0&&child.ScaleReads==0&&child.MatrixReads==0&&parent.Calls.Count==0&&child.InstanceIdQueries==0,"World mode performs no optional local/context/identity reads");
            check(child.WorldWrites==2&&child.RotationWrites==2&&child.LocalWrites==0,"World mode preserves world write order and count");
            check(child.Local.z>3&&child.Calls[0]=="get_world"&&child.Calls[1]=="get_rotation","Controlled biased world restoration exposes original roundtrip behavior");
            traces.Add(String.Join(",",child.Calls));
        }
        check(traces[0]==traces[1],"Default constructor and explicit world have identical native boundary traces");
        string root=Path.Combine(Path.GetTempPath(),"ugevr-local-restore-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);int n=0;
        try
        {
            var stable=new Case(root,++n);int world=stable.Child.WorldWrites;
            string good=stable.Finish();
            check(stable.Child.Local.x==1&&stable.Child.Local.y==2&&stable.Child.Local.z==3&&stable.Child.WorldWrites==world,"Stable admitted parent restores original raw local state without any world correction");
            check(good.Contains("changes_restoration_policy: true")&&good.Contains("selected_restoration_mode: local-with-world-fallback")&&good.Contains("applied_space: local")&&good.Contains("post_context: unchanged")&&good.Contains("restoration_returned: true"),"Private result distinguishes selected mode, actual local path and post-context verification");
            check(good.Contains("stage: before_camera_state_dispose")&&!good.Contains("stage: after_canvas_flush")&&good.Contains("stage_layout: local_experiment_pre_dispose_replaces_canvas_flush"),"Selected experiment declares its bounded pre-dispose stage layout");
            check(good.Contains("owned_world_position: [9, 8,")&&good.Contains("owned_world_rotation_xyzw: [0, 0, 0, 1]"),"Effective raw Unity eye readback is retained separately from mathematical eye poses");
            int local=stable.Child.LocalWrites;stable.Child.Local=new Vector3(99,99,99);stable.Scope.Dispose();check(stable.Child.LocalWrites==local&&stable.Child.Local.x==99,"Repeated Dispose preserves subsequent game changes");
            var missing=new Case(root,++n,false);Refused(check,missing,"owned_eye_missing");
            var noParent=new Case(root,++n);noParent.Child.parent=null;Refused(check,noParent,"parent_unavailable");
            var parentChanged=new Case(root,++n);parentChanged.Child.parent=new Probe();Refused(check,parentChanged,"parent_changed");
            var position=new Case(root,++n);position.Parent.World.x+=1;Refused(check,position,"parent_world_position_changed");
            var rotation=new Case(root,++n);rotation.Parent.WorldRotation.w=-1;Refused(check,rotation,"parent_world_rotation_changed");
            var localPosition=new Case(root,++n);localPosition.Parent.Local.y+=1;Refused(check,localPosition,"parent_local_position_changed");
            var localRotation=new Case(root,++n);localRotation.Parent.LocalRotation.w=-1;Refused(check,localRotation,"parent_local_rotation_changed");
            var parentScale=new Case(root,++n);parentScale.Parent.Scale.y=2;Refused(check,parentScale,"parent_local_scale_changed");
            var lossy=new Case(root,++n);lossy.Parent.Lossy.z=2;Refused(check,lossy,"parent_lossy_scale_changed");
            var forward=new Case(root,++n);forward.Parent.Forward.Elements[12]=1;Refused(check,forward,"parent_local_to_world_changed");
            var inverse=new Case(root,++n);inverse.Parent.Inverse.Elements[13]=1;Refused(check,inverse,"parent_world_to_local_changed");
            var scale=new Case(root,++n);scale.Child.Scale.x=2;Refused(check,scale,"camera_local_scale_changed");
            var foreign=new Case(root,++n);foreign.Child.Local.x+=.1f;Refused(check,foreign,"owned_eye_position_changed");
            check(foreign.Record.ToPrivateYaml().Contains("owned_rotation_equal: null"),"Ownership predicate preserves position-first short circuit");
            var sign=new Case(root,++n);sign.Child.LocalRotation.w=-1;Refused(check,sign,"owned_eye_rotation_changed");
            var tiny=new Case(root,++n);tiny.Child.Local.z+=.000001f;check(tiny.Finish().Contains("applied_space: local"),"Ownership uses the existing Unity position predicate, not a stricter or widened replacement");
            var failedQuery=new Case(root,++n);failedQuery.Parent.ThrowMatrix=true;Refused(check,failedQuery,"native_query_failed");check(!failedQuery.Record.ToPrivateYaml().Contains("private-query"),"Query exceptions publish only sanitized categories");
            var nonfinite=new Case(root,++n);nonfinite.Parent.Scale.z=float.NaN;Refused(check,nonfinite,"context_nonfinite_or_incomplete");
            var replaced=new Case(root,++n);replaced.Camera.transform=new Probe {parent=replaced.Parent};string replacement=replaced.Finish();check(replacement.Contains("refusal_or_fallback_reason: transform_changed"),"A replaced transform refuses the local branch while preserving explicit world fallback semantics");
            var postChange=new Case(root,++n);world=postChange.Child.WorldWrites;postChange.Child.LocalWrite=()=>postChange.Parent.Forward.Elements[12]=2;
            string post=postChange.Finish();check(post.Contains("applied_space: local")&&post.Contains("post_context: parent_local_to_world_changed")&&postChange.Child.WorldWrites==world&&postChange.Child.LocalWrites==1,"Parent change during local setter is reported with no second correction or fallback write");
            var partial=new Case(root,++n);partial.Camera.rect=new Rect {x=.8f};partial.Child.ThrowLocalRotation=true;bool threw=false;try{partial.Scope.Dispose();}catch(InvalidOperationException){threw=true;}
            string partialBody=partial.Record.ToPrivateYaml();check(threw&&partial.Child.Local.z==3&&partial.Camera.rect.x==.8f&&partialBody.Contains("restoration_returned: false")&&partialBody.Contains("failure_category: restoration_setter_or_query_exception")&&!partialBody.Contains("stage: after_camera_state_dispose"),"Partial setter failure keeps existing bounded cleanup behavior and never claims completion");
            int localWrites=partial.Child.LocalWrites;partial.Scope.Dispose();check(partial.Child.LocalWrites==localWrites,"Existing failed-dispose idempotence is not changed by experiment");
            var renderThrow=new Case(root,++n);renderThrow.Camera.targetTexture=new RenderTexture();renderThrow.Camera.rect=new Rect {x=.7f};try {using(renderThrow.Scope){renderThrow.Camera.RenderAction=()=>{throw new InvalidOperationException("render");};renderThrow.Camera.Render();}}catch(InvalidOperationException){}
            check(renderThrow.Child.Local.z==3&&renderThrow.Camera.targetTexture==null&&renderThrow.Camera.rect.x==0&&renderThrow.Record.ToPrivateYaml().Contains("restoration_returned: true"),"An eye Render exception still restores admitted local pose and all original untouched camera fields");
            var raw=new Case(root,++n);raw.Child.LocalRotation=new Quaternion(.1f,0,0,1.1f);raw.Scope.RecordOwnedEyePose();string rawBody=raw.Finish();check(rawBody.Contains("owned_world_rotation_xyzw: [0.10000000149011612, 0, 0, 1.100000023841858]"),"Owned quaternion operands retain raw finite non-unit components without normalization");
            var atAdmission=new Case(root,++n); // A query error after creation is not confused with a getter failure at save.
            atAdmission.Parent.ThrowMatrix=true;Refused(check,atAdmission,"native_query_failed");
            var initialParent=new Probe {ThrowMatrix=true};var initialChild=new Probe {Linked=true,parent=initialParent};var initialCamera=new Camera {transform=initialChild};
            string admissionFolder=Path.Combine(root,"admission");Directory.CreateDirectory(admissionFolder);string admissionRequest=Path.Combine(admissionFolder,"capture-request");File.WriteAllText(admissionRequest,"");
            var admissionRecord=new CameraRestorationPoseDiagnostics(admissionFolder).Begin(true,admissionRequest,initialCamera,1);
            using(var scope=new CameraState(initialCamera,admissionRecord,CameraTransformRestoration.LocalWithWorldFallback)) {initialParent.ThrowMatrix=false;scope.RecordOwnedEyePose();}
            check(admissionRecord.ToPrivateYaml().Contains("refusal_or_fallback_reason: native_query_failed"),"Failed saved parent query is never upgraded to admission after it recovers");
            var faultOwned=new Case(root,++n);faultOwned.Child.Local.z=float.NaN;faultOwned.Scope.RecordOwnedEyePose();faultOwned.Child.Local=new Vector3(9,8,7);Refused(check,faultOwned,"owned_eye_query_failed");check(faultOwned.Record.ToPrivateYaml().Contains("owned_world_position: [9, 8, 'NaN']"),"Known nonfinite raw owned operand remains explicit even when it cannot authorize restoration");
            var order=new Case(root,++n);order.Child.Calls.Clear();order.Parent.Calls.Clear();order.Finish();
            int firstSet=order.Child.Calls.IndexOf("set_local");check(firstSet>order.Child.Calls.IndexOf("get_inverse")&&order.Parent.Calls.IndexOf("get_inverse")>=0&&order.Child.Calls[firstSet+1]=="set_local_rotation","Pre-dispose snapshot queries precede local restoration; local setters keep position then rotation order");
            // Worst finite escaped private payload, including maximum-length IDs and eight full stages.
            string extremeFolder=Path.Combine(root,"extreme");Directory.CreateDirectory(extremeFolder);string req=Path.Combine(extremeFolder,"capture-request");File.WriteAllText(req,"");
            var child=new Probe {Linked=true};var parent=new Probe();child.parent=parent;var camera=new Camera {transform=child};
            camera.InstanceIdOverride=child.InstanceIdOverride=parent.InstanceIdOverride=int.MaxValue;
            var v=new Vector3(float.MaxValue,-float.MaxValue,float.MaxValue);var q=new Quaternion(float.MaxValue,-float.MaxValue,float.MaxValue,-float.MaxValue);
            var elements=new float[16];for(int i=0;i<16;++i)elements[i]=(i%2==0?1:-1)*float.MaxValue;
            child.Local=parent.Local=parent.World=child.Scale=parent.Scale=child.Lossy=parent.Lossy=v;child.LocalRotation=parent.LocalRotation=parent.WorldRotation=q;
            child.Forward=parent.Forward=child.Inverse=parent.Inverse=new Matrix4x4 {Elements=elements};
            var owner=new CameraRestorationPoseDiagnostics(extremeFolder);var record=owner.Begin(true,req,camera,ulong.MaxValue,v,q,ulong.MaxValue,int.MaxValue);
            using(var scope=new CameraState(camera,record,CameraTransformRestoration.LocalWithWorldFallback)) {scope.RecordOwnedEyePose();record.Observe("after_left_render");record.Observe("after_right_render");}
            record.Observe("after_canvas_flush");record.Observe("after_current_context");record.Observe("after_eye_captures");CameraRestorationComparison.Evaluate(camera,v,q,record);
            string worst=record.ToPrivateYaml();int bytes=Encoding.UTF8.GetByteCount(worst);Console.WriteLine("Maximum finite local restoration fixture bytes: "+bytes);record.Publish();
            check(bytes<=CameraRestorationPoseDiagnostics.MaximumBytes&&File.ReadAllText(Path.Combine(extremeFolder,"camera-restoration-pose-1.yaml"))==worst,"Selected experiment with all maximum finite operands fits and publishes under unchanged32KiB cap");
            check(worst.Split(new[]{"  - stage:"},StringSplitOptions.None).Length-1==8&&!worst.Contains("stage: after_canvas_flush"),"Selected experiment retains exactly eight stages including pre-dispose without duplicating parent contexts");
            // All-negative extreme components and a long fallback category can exceed the cap.
            // The publisher must refuse a whole record honestly instead of truncating or raising the cap.
            child.Local=parent.Local=parent.World=child.Scale=parent.Scale=child.Lossy=parent.Lossy=new Vector3(-float.MaxValue,-float.MaxValue,-float.MaxValue);
            child.LocalRotation=parent.LocalRotation=parent.WorldRotation=new Quaternion(-float.MaxValue,-float.MaxValue,-float.MaxValue,-float.MaxValue);
            for(int i=0;i<16;++i)elements[i]=-float.MaxValue;
            var expanded=owner.Begin(true,req,camera,ulong.MaxValue,child.position,child.rotation,ulong.MaxValue,int.MaxValue);
            using(var scope=new CameraState(camera,expanded,CameraTransformRestoration.LocalWithWorldFallback)) {scope.RecordOwnedEyePose();expanded.Observe("after_left_render");expanded.Observe("after_right_render");child.Local.x=0;}
            expanded.Observe("after_canvas_flush");expanded.Observe("after_current_context");expanded.Observe("after_eye_captures");CameraRestorationComparison.Evaluate(camera,child.position,child.rotation,expanded);
            string expandedBody=expanded.ToPrivateYaml();int expandedBytes=Encoding.UTF8.GetByteCount(expandedBody);expanded.Publish();Console.WriteLine("All-negative finite fallback fixture bytes: "+expandedBytes);
            check(expandedBytes<=CameraRestorationPoseDiagnostics.MaximumBytes?File.ReadAllText(Path.Combine(extremeFolder,"camera-restoration-pose-2.yaml"))==expandedBody:owner.LastError=="diagnostic_query_or_serialization_failure"&&!File.Exists(Path.Combine(extremeFolder,"camera-restoration-pose-2.yaml")),"Maximum finite fallback either publishes wholly under cap or refuses without malformed/truncated evidence");
            check(!worst.Contains("private-native-failure")&&!worst.Contains("fixture")&&worst.Contains("aba_changes_excluded: false"),"Private outcome contains no object names and makes no atomic or ABA claim");
        }
        finally
        {
            string resolved=Path.GetFullPath(root),temp=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            string name=Path.GetFileName(resolved),prefix="ugevr-local-restore-";Guid id;
            if(!resolved.StartsWith(temp,StringComparison.OrdinalIgnoreCase)||!name.StartsWith(prefix,StringComparison.Ordinal)||!Guid.TryParseExact(name.Substring(prefix.Length),"N",out id))throw new Exception("Unsafe fixture cleanup.");
            Directory.Delete(resolved,true);
        }
    }
}
