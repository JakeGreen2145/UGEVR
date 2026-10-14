using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;

namespace Ugevr
{
    // Passive evidence only. No comparison here changes the restoration policy.
    public sealed class CameraRestorationPoseDiagnostics
    {
        public const int MaximumCaptures=8,MaximumStages=8,MaximumBytes=32768;
        readonly string directory;
        readonly int ownerThread=Thread.CurrentThread.ManagedThreadId;
        int attempts;
        public int Attempts {get {return attempts;}}
        public string LastError {get;private set;}
        public CameraRestorationPoseDiagnostics(string evidenceDirectory)
        {
            if(String.IsNullOrEmpty(evidenceDirectory) || !Path.IsPathRooted(evidenceDirectory))throw new ArgumentException("An absolute private evidence directory is required.");
            directory=Path.GetFullPath(evidenceDirectory);LastError="";
        }
        internal static void CheckPath(string path)
        {
            for(var parent=new DirectoryInfo(path);parent!=null;parent=parent.Parent)
                if(parent.Exists && (parent.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Evidence path contains a reparse point.");
        }
        public CameraRestorationPoseRecord Begin(bool enabled,string request,Camera camera,ulong frame,Vector3? anchorPosition=null,Quaternion? anchorRotation=null,ulong generation=0,int renderedPairOrdinal=0)
        {
            if(!enabled || ownerThread!=Thread.CurrentThread.ManagedThreadId || attempts>=MaximumCaptures || !File.Exists(request))return null;
            try
            {
                CheckPath(directory);
                while(attempts<MaximumCaptures && File.Exists(Path.Combine(directory,"camera-restoration-pose-"+(attempts+1)+".yaml")))++attempts;
                if(attempts>=MaximumCaptures)return null;
                ++attempts;
                var record=new CameraRestorationPoseRecord(this,camera,frame,generation,renderedPairOrdinal,Path.Combine(directory,"camera-restoration-pose-"+attempts+".yaml"));
                record.ObserveAnchor(anchorPosition,anchorRotation);return record;
            }
            catch(Exception error){LastError=Failure(error);return null;}
        }
        internal static string Failure(Exception error) {return error is IOException?"io_failure":error is UnauthorizedAccessException?"access_failure":"diagnostic_query_or_serialization_failure";}
        internal void Failed(Exception error) {LastError=Failure(error);}
        internal void Publish(string destination,string text)
        {
            string temporary=null;
            try
            {
                if(ownerThread!=Thread.CurrentThread.ManagedThreadId)throw new InvalidOperationException("Wrong evidence owner thread.");
                byte[] bytes=new UTF8Encoding(false,true).GetBytes(text);
                if(bytes.Length>MaximumBytes)throw new InvalidOperationException("Camera evidence exceeds its byte bound.");
                CheckPath(directory);Directory.CreateDirectory(directory);CheckPath(directory);
                temporary=destination+"."+Guid.NewGuid().ToString("N")+".tmp";
                using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))stream.Write(bytes,0,bytes.Length);
                CheckPath(directory);File.Move(temporary,destination);temporary=null;LastError="";
            }
            catch(Exception error){LastError=Failure(error);}
            finally {if(temporary!=null)try {CheckPath(directory);File.Delete(temporary);}catch {}}
        }
    }
    public sealed class CameraRestorationPoseRecord
    {
        internal sealed class PoseStage
        {
            public string Name,Error,ParentError;
            public int? CameraId,TransformId,ParentId;
            public bool? HasParent;
            public double[] WorldPosition,WorldRotation,LocalPosition,LocalRotation;
            public double[] LocalScale,LossyScale,LocalToWorld,WorldToLocal;
            public double[] ParentWorldPosition,ParentWorldRotation,ParentLocalPosition,ParentLocalRotation;
            public double[] ParentLocalScale,ParentLossyScale,ParentLocalToWorld,ParentWorldToLocal;
        }
        readonly CameraRestorationPoseDiagnostics owner;
        readonly Camera camera;
        readonly ulong frame;
        readonly ulong generation;
        readonly int renderedPairOrdinal;
        readonly string destination;
        readonly int ownerThread=Thread.CurrentThread.ManagedThreadId;
        readonly List<PoseStage> stages=new List<PoseStage>();
        bool published;
        CameraLocalRestoration localRestoration;
        bool localSelected;
        internal void SelectLocalMode() {localSelected=true;}
        internal void SelectLocalRestoration(CameraLocalRestoration experiment) {localSelected=true;localRestoration=experiment;}
        bool comparisonAttempted;
        string comparisonError;
        bool? positionEqual,rotationEqual;
        double[] comparedAnchorPosition,comparedAnchorRotation,comparedPosition,comparedRotation;
        float? positionSquaredDistance,rotationSignedDot;
        public bool CaptureRequestConsumed {get;set;}
        public int EyeCaptureAttempts {get;set;}
        public bool EyeCapturesReturned {get;set;}
        public bool? ExistingGameCameraRestored {get;set;}
        internal CameraRestorationPoseRecord(CameraRestorationPoseDiagnostics owner,Camera camera,ulong frame,ulong generation,int renderedPairOrdinal,string destination)
        {this.owner=owner;this.camera=camera;this.frame=frame;this.generation=generation;this.renderedPairOrdinal=renderedPairOrdinal;this.destination=destination;}
        static double[] Position(Vector3 value) {return new double[]{value.x,value.y,value.z};}
        static double[] Rotation(Quaternion value) {return new double[]{value.x,value.y,value.z,value.w};}
        static double[] Matrix(Matrix4x4 value) {var result=new double[16];for(int i=0;i<16;++i)result[i]=value[i];return result;}
        internal void RecordComparison(Vector3 anchorPosition,Quaternion anchorRotation,Vector3? actualPosition,Quaternion? actualRotation,bool? samePosition,bool? sameRotation,Exception failure)
        {
            if(published || comparisonAttempted || ownerThread!=Thread.CurrentThread.ManagedThreadId)return;
            comparisonAttempted=true;positionEqual=samePosition;rotationEqual=sameRotation;
            comparisonError=failure==null?null:CameraRestorationPoseDiagnostics.Failure(failure);
            try
            {
                comparedAnchorPosition=Position(anchorPosition);comparedAnchorRotation=Rotation(anchorRotation);
                if(actualPosition.HasValue)
                {
                    comparedPosition=Position(actualPosition.Value);
                    var difference=new Vector3(actualPosition.Value.x-anchorPosition.x,actualPosition.Value.y-anchorPosition.y,actualPosition.Value.z-anchorPosition.z);
                    positionSquaredDistance=difference.sqrMagnitude;
                }
                if(actualRotation.HasValue) {comparedRotation=Rotation(actualRotation.Value);rotationSignedDot=Quaternion.Dot(actualRotation.Value,anchorRotation);}
            }
            catch(Exception error) {comparisonError=CameraRestorationPoseDiagnostics.Failure(error);owner.Failed(error);}
        }
        public void Observe(string stage) {Observe(stage,null,null);}
        internal void ObserveAnchor(Vector3? position,Quaternion? rotation) {Observe("anchor_before_scope",position,rotation);}
        internal void ObserveSavedWorld(Vector3 position,Quaternion rotation) {Observe("camera_state_saved",position,rotation);}
        void Observe(string stage,Vector3? savedPosition,Quaternion? savedRotation)
        {
            if(published || ownerThread!=Thread.CurrentThread.ManagedThreadId || stages.Count>=CameraRestorationPoseDiagnostics.MaximumStages || (localSelected && stage=="after_canvas_flush"))return;
            if(!localSelected && stage=="before_camera_state_dispose")throw new ArgumentException("Experiment stage requires explicit local selection.");
            if(localRestoration!=null && (stage=="camera_state_saved" || stage=="before_camera_state_dispose" || stage=="after_camera_state_dispose"))
            {stages.Add(localRestoration.DiagnosticStage(stage));return;}
            // Fixed stage vocabulary keeps private records bounded and prevents object names entering logs.
            if(stage!="before_camera_state_dispose" && stage!="anchor_before_scope" && stage!="camera_state_saved" && stage!="after_left_render" && stage!="after_right_render" && stage!="after_camera_state_dispose" && stage!="after_canvas_flush" && stage!="after_current_context" && stage!="after_eye_captures")throw new ArgumentException("Unknown camera evidence stage.");
            var value=new PoseStage {Name=stage};stages.Add(value);
            try
            {
                if(camera==null) {value.Error="camera_unavailable";return;}
                value.CameraId=camera.GetInstanceID();var transform=camera.transform;
                value.TransformId=transform.GetInstanceID();var parent=transform.parent;
                value.HasParent=parent!=null;if(parent!=null)value.ParentId=parent.GetInstanceID();
                value.WorldPosition=Position(savedPosition??transform.position);value.WorldRotation=Rotation(savedRotation??transform.rotation);
                value.LocalPosition=Position(transform.localPosition);value.LocalRotation=Rotation(transform.localRotation);
                value.LocalScale=Position(transform.localScale);value.LossyScale=Position(transform.lossyScale);
                value.LocalToWorld=Matrix(transform.localToWorldMatrix);value.WorldToLocal=Matrix(transform.worldToLocalMatrix);
                if(parent!=null)
                {
                    // A parent getter failure does not erase the camera's completed reads.
                    try
                    {
                        value.ParentWorldPosition=Position(parent.position);value.ParentWorldRotation=Rotation(parent.rotation);
                        value.ParentLocalPosition=Position(parent.localPosition);value.ParentLocalRotation=Rotation(parent.localRotation);
                        value.ParentLocalScale=Position(parent.localScale);value.ParentLossyScale=Position(parent.lossyScale);
                        value.ParentLocalToWorld=Matrix(parent.localToWorldMatrix);value.ParentWorldToLocal=Matrix(parent.worldToLocalMatrix);
                    }
                    catch(Exception error) {value.ParentError=CameraRestorationPoseDiagnostics.Failure(error);}
                }
            }
            catch(Exception error) {value.Error=CameraRestorationPoseDiagnostics.Failure(error);}
        }
        static string Bool(bool? value) {return value.HasValue?value.Value.ToString().ToLowerInvariant():"null";}
        static string Id(int? value) {return value.HasValue?value.Value.ToString(CultureInfo.InvariantCulture):"null";}
        static void Number(StringBuilder result,string key,float? number)
        {
            bool? finite=number.HasValue?(bool?)(!Single.IsNaN(number.Value) && !Single.IsInfinity(number.Value)):null;
            string text=number.HasValue?((double)number.Value).ToString("R",CultureInfo.InvariantCulture):"null";
            result.Append("    "+key+": "+(finite==false?"'"+text+"'":text)+"\n    "+key+"_finite: "+Bool(finite)+"\n");
        }
        static void Array(StringBuilder result,string key,double[] values)
        {
            if(values==null){result.Append("    "+key+": null\n    "+key+"_finite: null\n");return;}
            bool finite=true;result.Append("    "+key+": [");
            for(int i=0;i<values.Length;++i)
            {
                if(i!=0)result.Append(", ");double value=values[i];
                if(Double.IsNaN(value)||Double.IsInfinity(value)){finite=false;result.Append("'"+value.ToString(CultureInfo.InvariantCulture)+"'");}
                else result.Append(value.ToString("R",CultureInfo.InvariantCulture));
            }
            result.Append("]\n    "+key+"_finite: "+finite.ToString().ToLowerInvariant()+"\n");
        }
        public string ToPrivateYaml()
        {
            if(EyeCaptureAttempts<0 || EyeCaptureAttempts>2)throw new InvalidOperationException("Invalid image capture attempt count.");
            var result=new StringBuilder("schema_version: 1\nprivate_camera_pose: true\nchanges_restoration_policy: "+localSelected.ToString().ToLowerInvariant()+"\nselected_restoration_mode: "+(!localSelected?"world":"local-with-world-fallback")+"\nstage_layout: "+(!localSelected?"default_eight":"local_experiment_pre_dispose_replaces_canvas_flush")+"\nvr_frame_id: "+frame.ToString(CultureInfo.InvariantCulture)+"\nruntime_generation: "+generation.ToString(CultureInfo.InvariantCulture)+"\nrendered_pair_ordinal: "+renderedPairOrdinal.ToString(CultureInfo.InvariantCulture)+"\ncapture_request_consumed: "+CaptureRequestConsumed.ToString().ToLowerInvariant()+"\neye_capture_attempts: "+EyeCaptureAttempts+"\neye_captures_returned: "+EyeCapturesReturned.ToString().ToLowerInvariant()+"\nexisting_game_camera_restored: "+Bool(ExistingGameCameraRestored)+"\nactual_comparison:\n    attempted: "+comparisonAttempted.ToString().ToLowerInvariant()+"\n    position_equal: "+Bool(positionEqual)+"\n    rotation_term_evaluated: "+(comparedRotation!=null).ToString().ToLowerInvariant()+"\n    rotation_equal: "+Bool(rotationEqual)+"\n    query_error: "+(comparisonError??"null")+"\n    arithmetic: Unity_float_from_actual_comparison_operands\n");
            Array(result,"anchor_world_position",comparedAnchorPosition);Array(result,"actual_world_position",comparedPosition);
            Array(result,"anchor_world_rotation_xyzw",comparedAnchorRotation);Array(result,"actual_world_rotation_xyzw",comparedRotation);
            Number(result,"position_squared_distance",positionSquaredDistance);Number(result,"rotation_signed_dot",rotationSignedDot);
            if(localRestoration!=null)result.Append(localRestoration.ToPrivateYaml());
            result.Append("matrix_element_order: Unity_single_index_0_through_15\nstages:\n");
            foreach(var value in stages)
            {
                result.Append("  - stage: "+value.Name+"\n    camera_instance_id: "+Id(value.CameraId)+"\n    transform_instance_id: "+Id(value.TransformId)+"\n    has_parent: "+Bool(value.HasParent)+"\n    parent_instance_id: "+Id(value.ParentId)+"\n    query_error: "+(value.Error??"null")+"\n    parent_query_error: "+(value.ParentError??"null")+"\n");
                Array(result,"world_position",value.WorldPosition);Array(result,"world_rotation_xyzw",value.WorldRotation);
                Array(result,"local_position",value.LocalPosition);Array(result,"local_rotation_xyzw",value.LocalRotation);
                Array(result,"local_scale",value.LocalScale);Array(result,"lossy_scale",value.LossyScale);
                Array(result,"local_to_world_matrix",value.LocalToWorld);Array(result,"world_to_local_matrix",value.WorldToLocal);
                Array(result,"parent_world_position",value.ParentWorldPosition);Array(result,"parent_world_rotation_xyzw",value.ParentWorldRotation);
                Array(result,"parent_local_position",value.ParentLocalPosition);Array(result,"parent_local_rotation_xyzw",value.ParentLocalRotation);
                Array(result,"parent_local_scale",value.ParentLocalScale);Array(result,"parent_lossy_scale",value.ParentLossyScale);
                Array(result,"parent_local_to_world_matrix",value.ParentLocalToWorld);Array(result,"parent_world_to_local_matrix",value.ParentWorldToLocal);
            }
            return result.ToString();
        }
        public void Publish()
        {
            if(published || ownerThread!=Thread.CurrentThread.ManagedThreadId)return;
            published=true;try {owner.Publish(destination,ToPrivateYaml());}catch(Exception error){owner.Failed(error);}
        }
    }
}
