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
        sealed class PoseStage
        {
            public string Name,Error;
            public int? CameraId,TransformId,ParentId;
            public bool? HasParent;
            public double[] WorldPosition,WorldRotation,LocalPosition,LocalRotation;
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
        public bool CaptureRequestConsumed {get;set;}
        public int EyeCaptureAttempts {get;set;}
        public bool EyeCapturesReturned {get;set;}
        public bool? ExistingGameCameraRestored {get;set;}
        internal CameraRestorationPoseRecord(CameraRestorationPoseDiagnostics owner,Camera camera,ulong frame,ulong generation,int renderedPairOrdinal,string destination)
        {this.owner=owner;this.camera=camera;this.frame=frame;this.generation=generation;this.renderedPairOrdinal=renderedPairOrdinal;this.destination=destination;}
        static double[] Position(Vector3 value) {return new double[]{value.x,value.y,value.z};}
        static double[] Rotation(Quaternion value) {return new double[]{value.x,value.y,value.z,value.w};}
        public void Observe(string stage) {Observe(stage,null,null);}
        internal void ObserveAnchor(Vector3? position,Quaternion? rotation) {Observe("anchor_before_scope",position,rotation);}
        internal void ObserveSavedWorld(Vector3 position,Quaternion rotation) {Observe("camera_state_saved",position,rotation);}
        void Observe(string stage,Vector3? savedPosition,Quaternion? savedRotation)
        {
            if(published || ownerThread!=Thread.CurrentThread.ManagedThreadId || stages.Count>=CameraRestorationPoseDiagnostics.MaximumStages)return;
            // Fixed stage vocabulary keeps private records bounded and prevents object names entering logs.
            if(stage!="anchor_before_scope" && stage!="camera_state_saved" && stage!="after_left_render" && stage!="after_right_render" && stage!="after_camera_state_dispose" && stage!="after_canvas_flush" && stage!="after_current_context" && stage!="after_eye_captures")throw new ArgumentException("Unknown camera evidence stage.");
            var value=new PoseStage {Name=stage};stages.Add(value);
            try
            {
                if(camera==null) {value.Error="camera_unavailable";return;}
                value.CameraId=camera.GetInstanceID();var transform=camera.transform;
                value.TransformId=transform.GetInstanceID();var parent=transform.parent;
                value.HasParent=parent!=null;if(parent!=null)value.ParentId=parent.GetInstanceID();
                value.WorldPosition=Position(savedPosition??transform.position);value.WorldRotation=Rotation(savedRotation??transform.rotation);
                value.LocalPosition=Position(transform.localPosition);value.LocalRotation=Rotation(transform.localRotation);
            }
            catch(Exception error) {value.Error=CameraRestorationPoseDiagnostics.Failure(error);}
        }
        static string Bool(bool? value) {return value.HasValue?value.Value.ToString().ToLowerInvariant():"null";}
        static string Id(int? value) {return value.HasValue?value.Value.ToString(CultureInfo.InvariantCulture):"null";}
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
            var result=new StringBuilder("schema_version: 1\nprivate_camera_pose: true\nchanges_restoration_policy: false\nvr_frame_id: "+frame.ToString(CultureInfo.InvariantCulture)+"\nruntime_generation: "+generation.ToString(CultureInfo.InvariantCulture)+"\nrendered_pair_ordinal: "+renderedPairOrdinal.ToString(CultureInfo.InvariantCulture)+"\ncapture_request_consumed: "+CaptureRequestConsumed.ToString().ToLowerInvariant()+"\neye_capture_attempts: "+EyeCaptureAttempts+"\neye_captures_returned: "+EyeCapturesReturned.ToString().ToLowerInvariant()+"\nexisting_game_camera_restored: "+Bool(ExistingGameCameraRestored)+"\nstages:\n");
            foreach(var value in stages)
            {
                result.Append("  - stage: "+value.Name+"\n    camera_instance_id: "+Id(value.CameraId)+"\n    transform_instance_id: "+Id(value.TransformId)+"\n    has_parent: "+Bool(value.HasParent)+"\n    parent_instance_id: "+Id(value.ParentId)+"\n    query_error: "+(value.Error??"null")+"\n");
                Array(result,"world_position",value.WorldPosition);Array(result,"world_rotation_xyzw",value.WorldRotation);
                Array(result,"local_position",value.LocalPosition);Array(result,"local_rotation_xyzw",value.LocalRotation);
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
