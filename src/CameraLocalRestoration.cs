using System;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace Ugevr
{
    public enum CameraTransformRestoration { World, LocalWithWorldFallback, GuardedLocal }

    // Explicit candidate experiment. Sampled context cannot detect ABA changes.
    public sealed class CameraLocalRestoration
    {
        sealed class Snapshot
        {
            public Transform Transform,Parent;
            public bool HasParent;
            public int? CameraId,TransformId,ParentId;
            public float[] WorldPosition,WorldRotation,LocalPosition,LocalRotation,LocalScale,LossyScale,LocalToWorld,WorldToLocal;
            public float[][] ParentContext=new float[8][];
            public string Error;
        }
        readonly Camera camera;
        readonly bool guarded;
        Snapshot saved,before,after;
        Vector3 ownedPosition;
        Quaternion ownedRotation;
        bool hasOwnedPose,ownedQueryFailed,ownedPositionRead,ownedRotationRead;
        bool? ownedPositionEqual,ownedRotationEqual;
        public string AppliedSpace {get;private set;}
        public string Refusal {get;private set;}
        public string PostContext {get;private set;}
        public string Failure {get;private set;}
        public bool RestoreReturned {get;private set;}
        public bool Guarded {get {return guarded;}}
        public static CameraTransformRestoration Parse(string value)
        {
            if(value=="world")return CameraTransformRestoration.World;
            if(value=="local-with-world-fallback")return CameraTransformRestoration.LocalWithWorldFallback;
            if(value=="guarded-local")return CameraTransformRestoration.GuardedLocal;
            throw new ArgumentException("Rendering.CameraTransformRestoration must be world, local-with-world-fallback or guarded-local.");
        }
        public CameraLocalRestoration(Camera camera,Vector3 position,Quaternion rotation)
            : this(camera,position,rotation,false) {}
        public CameraLocalRestoration(Camera camera,Vector3 position,Quaternion rotation,bool guarded)
        {
            this.camera=camera;this.guarded=guarded;AppliedSpace="not_attempted";Refusal="not_evaluated";PostContext="not_evaluated";Failure="none";
            saved=Capture();saved.WorldPosition=V(position);saved.WorldRotation=Q(rotation);
        }
        static float[] V(Vector3 v) {return new[]{v.x,v.y,v.z};}
        static float[] Q(Quaternion q) {return new[]{q.x,q.y,q.z,q.w};}
        static float[] M(Matrix4x4 m) {var a=new float[16];for(int i=0;i<16;++i)a[i]=m[i];return a;}
        static bool Finite(float[] a)
        {if(a==null)return false;foreach(float f in a)if(Single.IsNaN(f)||Single.IsInfinity(f))return false;return true;}
        static bool Exact(float[] a,float[] b)
        {if(a==null||b==null||a.Length!=b.Length)return false;for(int i=0;i<a.Length;++i)if(a[i]!=b[i])return false;return true;}
        static bool CompleteFinite(Snapshot s)
        {
            if(s==null||s.Error!=null||!Finite(s.WorldPosition)||!Finite(s.WorldRotation)||!Finite(s.LocalPosition)||!Finite(s.LocalRotation)||!Finite(s.LocalScale))return false;
            if(s.HasParent)foreach(var a in s.ParentContext)if(!Finite(a))return false;return true;
        }
        Snapshot Capture()
        {
            var s=new Snapshot();
            try
            {
                if(camera==null){s.Error="camera_unavailable";return s;}
                s.CameraId=camera.GetInstanceID();s.Transform=camera.transform;
                if(s.Transform==null){s.Error="transform_unavailable";return s;}
                s.TransformId=s.Transform.GetInstanceID();s.Parent=s.Transform.parent;
                s.WorldPosition=V(s.Transform.position);s.WorldRotation=Q(s.Transform.rotation);
                s.LocalPosition=V(s.Transform.localPosition);s.LocalRotation=Q(s.Transform.localRotation);s.LocalScale=V(s.Transform.localScale);
                s.LossyScale=V(s.Transform.lossyScale);s.LocalToWorld=M(s.Transform.localToWorldMatrix);s.WorldToLocal=M(s.Transform.worldToLocalMatrix);
                s.HasParent=s.Parent!=null;
                if(!s.HasParent)
                {
                    if(!guarded || !System.Object.ReferenceEquals(s.Parent,null)){s.Error="parent_unavailable";return s;}
                }
                else
                {
                s.ParentId=s.Parent.GetInstanceID();
                s.ParentContext[0]=V(s.Parent.position);s.ParentContext[1]=Q(s.Parent.rotation);
                s.ParentContext[2]=V(s.Parent.localPosition);s.ParentContext[3]=Q(s.Parent.localRotation);
                s.ParentContext[4]=V(s.Parent.localScale);s.ParentContext[5]=V(s.Parent.lossyScale);
                s.ParentContext[6]=M(s.Parent.localToWorldMatrix);s.ParentContext[7]=M(s.Parent.worldToLocalMatrix);
                }
                if(camera==null||s.Transform==null||(s.HasParent&&s.Parent==null)||!System.Object.ReferenceEquals(camera.transform,s.Transform)||!System.Object.ReferenceEquals(s.Transform.parent,s.Parent))s.Error="context_changed_during_query";
                if(guarded&&(!Finite(s.LossyScale)||!Finite(s.LocalToWorld)||!Finite(s.WorldToLocal)))s.Error="context_nonfinite_or_incomplete";
            }
            catch {s.Error="native_query_failed";}
            return s;
        }
        public void RecordOwnedEyePose()
        {
            // Retain effective raw engine structs before Render; no domain normalization.
            hasOwnedPose=false;ownedPositionRead=false;ownedRotationRead=false;
            try
            {
                if(camera==null||saved.Transform==null||!System.Object.ReferenceEquals(camera.transform,saved.Transform)) {ownedQueryFailed=true;return;}
                ownedPosition=saved.Transform.position;ownedPositionRead=true;ownedRotation=saved.Transform.rotation;ownedRotationRead=true;
                hasOwnedPose=Finite(V(ownedPosition))&&Finite(Q(ownedRotation));ownedQueryFailed=!hasOwnedPose;
            }
            catch {ownedQueryFailed=true;}
        }
        static string ContextDifference(Snapshot a,Snapshot b)
        {
            if(b.Error!=null)return b.Error;
            if(!System.Object.ReferenceEquals(a.Transform,b.Transform))return "transform_changed";
            if(!System.Object.ReferenceEquals(a.Parent,b.Parent))return "parent_changed";
            if(a.HasParent!=b.HasParent||a.CameraId!=b.CameraId||a.TransformId!=b.TransformId||a.ParentId!=b.ParentId)return "context_identity_changed";
            if(!CompleteFinite(b))return "context_nonfinite_or_incomplete";
            if(!Exact(a.LocalScale,b.LocalScale))return "camera_local_scale_changed";
            string[] names={"parent_world_position_changed","parent_world_rotation_changed","parent_local_position_changed","parent_local_rotation_changed","parent_local_scale_changed","parent_lossy_scale_changed","parent_local_to_world_changed","parent_world_to_local_changed"};
            if(a.HasParent)for(int i=0;i<8;++i)if(!Exact(a.ParentContext[i],b.ParentContext[i]))return names[i];
            return null;
        }
        public bool ChooseLocal()
        {
            before=Capture();
            string reason=saved.Error;
            if(reason==null&&!CompleteFinite(saved))reason="saved_context_nonfinite_or_incomplete";
            if(reason==null)reason=ContextDifference(saved,before);
            if(reason==null&&!hasOwnedPose)reason=ownedQueryFailed?"owned_eye_query_failed":"owned_eye_missing";
            if(reason==null)
            {
                var p=new Vector3(before.WorldPosition[0],before.WorldPosition[1],before.WorldPosition[2]);
                var q=new Quaternion(before.WorldRotation[0],before.WorldRotation[1],before.WorldRotation[2],before.WorldRotation[3]);
                ownedPositionEqual=guarded?Exact(before.WorldPosition,V(ownedPosition)):p==ownedPosition;
                if(ownedPositionEqual==false)reason="owned_eye_position_changed";
                else {ownedRotationEqual=guarded?Exact(before.WorldRotation,Q(ownedRotation)):q==ownedRotation;if(ownedRotationEqual==false)reason="owned_eye_rotation_changed";}
            }
            Refusal=reason??"none";AppliedSpace=reason==null?"local":guarded?"foreign_pose_preserved":"world_fallback";return reason==null;
        }
        public string AdmissionRefusal {get {return saved.Error??(!CompleteFinite(saved)?"saved_context_nonfinite_or_incomplete":null);}}
        public void RestoreLocal()
        {
            // A query or setter exception retains the existing partial-cleanup behavior.
            saved.Transform.localPosition=new Vector3(saved.LocalPosition[0],saved.LocalPosition[1],saved.LocalPosition[2]);
            saved.Transform.localRotation=new Quaternion(saved.LocalRotation[0],saved.LocalRotation[1],saved.LocalRotation[2],saved.LocalRotation[3]);
        }
        public void ObserveAfterRestoration()
        {
            after=Capture();string difference=ContextDifference(saved,after);
            PostContext=difference??"unchanged";
            // Never make a second corrective write, including on mid-restore context change.
        }
        public void Completed() {RestoreReturned=true;}
        public string GuardedResultRefusal()
        {
            if(PostContext!="unchanged")return "post_context_"+PostContext;
            if(!Exact(saved.LocalPosition,after.LocalPosition)||!Exact(saved.LocalRotation,after.LocalRotation))return "saved_local_pose_not_retained";
            var p=new Vector3(after.WorldPosition[0],after.WorldPosition[1],after.WorldPosition[2]);
            var original=new Vector3(saved.WorldPosition[0],saved.WorldPosition[1],saved.WorldPosition[2]);
            if(!(p==original))return "saved_world_position_not_restored";
            var q=new Quaternion(after.WorldRotation[0],after.WorldRotation[1],after.WorldRotation[2],after.WorldRotation[3]);
            var rotation=new Quaternion(saved.WorldRotation[0],saved.WorldRotation[1],saved.WorldRotation[2],saved.WorldRotation[3]);
            return q==rotation?null:"saved_world_rotation_not_restored";
        }
        public void Failed() {Failure="restoration_setter_or_query_exception";RestoreReturned=false;}
        public void GuardedFailed(string reason) {Failure=reason??"restoration_setter_or_query_exception";RestoreReturned=false;}
        static string Bool(bool? b) {return b.HasValue?b.Value.ToString().ToLowerInvariant():"null";}
        static void Array(StringBuilder b,string key,float[] values,int indent)
        {
            string pad=new string(' ',indent);b.Append(pad+key+": ");
            if(values==null){b.Append("null\n");return;}
            b.Append("[");for(int i=0;i<values.Length;++i){if(i!=0)b.Append(", ");float f=values[i];bool finite=!Single.IsNaN(f)&&!Single.IsInfinity(f);string s=((double)f).ToString("R",CultureInfo.InvariantCulture);b.Append(finite?s:"'"+s+"'");}b.Append("]\n");
        }
        static double[] Doubles(float[] values) {if(values==null)return null;var result=new double[values.Length];for(int i=0;i<values.Length;++i)result[i]=values[i];return result;}
        internal CameraRestorationPoseRecord.PoseStage DiagnosticStage(string name)
        {
            Snapshot s=name=="camera_state_saved"?saved:name=="before_camera_state_dispose"?before:after;
            var result=new CameraRestorationPoseRecord.PoseStage {Name=name};
            if(s==null){result.Error="not_queried";return result;}
            result.Error=s.Error;result.CameraId=s.CameraId;result.TransformId=s.TransformId;result.ParentId=s.ParentId;result.HasParent=s.HasParent;
            result.WorldPosition=Doubles(s.WorldPosition);result.WorldRotation=Doubles(s.WorldRotation);result.LocalPosition=Doubles(s.LocalPosition);result.LocalRotation=Doubles(s.LocalRotation);result.LocalScale=Doubles(s.LocalScale);
            result.LossyScale=Doubles(s.LossyScale);result.LocalToWorld=Doubles(s.LocalToWorld);result.WorldToLocal=Doubles(s.WorldToLocal);
            result.ParentWorldPosition=Doubles(s.ParentContext[0]);result.ParentWorldRotation=Doubles(s.ParentContext[1]);result.ParentLocalPosition=Doubles(s.ParentContext[2]);result.ParentLocalRotation=Doubles(s.ParentContext[3]);
            result.ParentLocalScale=Doubles(s.ParentContext[4]);result.ParentLossyScale=Doubles(s.ParentContext[5]);result.ParentLocalToWorld=Doubles(s.ParentContext[6]);result.ParentWorldToLocal=Doubles(s.ParentContext[7]);return result;
        }
        public string ToPrivateYaml()
        {
            var b=new StringBuilder((guarded?"guarded_local_restoration:":"local_restoration_experiment:")+"\n  applied_space: "+AppliedSpace+"\n  refusal_or_fallback_reason: "+Refusal+"\n  post_context: "+PostContext+"\n  restoration_returned: "+RestoreReturned.ToString().ToLowerInvariant()+"\n  failure_category: "+Failure+"\n  aba_changes_excluded: false\n  owned_pose_available: "+hasOwnedPose.ToString().ToLowerInvariant()+"\n  owned_position_equal: "+Bool(ownedPositionEqual)+"\n  owned_rotation_equal: "+Bool(ownedRotationEqual)+"\n");
            Array(b,"owned_world_position",ownedPositionRead?V(ownedPosition):null,2);Array(b,"owned_world_rotation_xyzw",ownedRotationRead?Q(ownedRotation):null,2);
            return b.ToString();
        }
    }
}
