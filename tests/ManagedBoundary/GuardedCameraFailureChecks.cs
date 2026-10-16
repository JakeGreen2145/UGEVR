using System;
using System.IO;
using System.Threading;
using Ugevr;
using UnityEngine;

static class GuardedCameraFailureChecks
{
    sealed class CountedTransform : Transform
    {
        public int Reads;
        public bool Throw;
        public override Vector3 position {get {++Reads;if(Throw)throw new Exception("private native failure");return localPosition;}set {localPosition=value;}}
    }
    static string PathFor(string root,string run,int n) {return Path.Combine(root,"guarded-camera-failure-"+run+"-"+n+".yaml");}
    static void Refuse(Action action)
    {try {action();throw new Exception("Expected guarded refusal.");}catch(InvalidOperationException){}}
    static Camera CameraWith(Transform transform)
    {
        transform.localPosition=new Vector3(1,2,3);transform.localRotation=new Quaternion(0,0,0,1);transform.localScale=new Vector3(1,1,1);
        return new Camera {transform=transform};
    }
    public static void Run(Action<bool,string> check)
    {
        string root=Path.Combine(Path.GetTempPath(),"ugevr-guard-failure-"+Guid.NewGuid().ToString("N")),run=Guid.NewGuid().ToString("D");
        Directory.CreateDirectory(root);
        try
        {
            foreach(var gate in new[]{new[]{false,true,true},new[]{true,false,true},new[]{true,true,false}})
                check(GuardedCameraFailureDiagnostics.Create(gate[0],gate[1],gate[2],"invalid path","invalid run")==null,"Disabled flag or absent ownership creates no failure collector, validates no path and performs no queries");
            var owner=GuardedCameraFailureDiagnostics.Create(true,true,true,root,run);
            var transform=new CountedTransform();var camera=CameraWith(transform);
            using(var stable=new CameraState(camera,null,CameraTransformRestoration.GuardedLocal,owner.Begin(10,2,4)))
            {transform.position=new Vector3(7,8,9);stable.RecordOwnedEyePose();}
            check(owner.Attempts==0 && Directory.GetFiles(root).Length==0,"Successful guarded pair creates no failure evidence and consumes no failure budget");
            int enabledReads=transform.Reads;var disabledTransform=new CountedTransform();var disabledCamera=CameraWith(disabledTransform);
            using(var disabled=new CameraState(disabledCamera,null,CameraTransformRestoration.GuardedLocal))
            {disabledTransform.position=new Vector3(7,8,9);disabled.RecordOwnedEyePose();}
            check(disabledTransform.Reads==enabledReads,"Enabling failure collection adds no native position queries to the successful guard path");
            var record=owner.Begin(11,3,5);var scope=new CameraState(camera,null,CameraTransformRestoration.GuardedLocal,record);
            transform.position=new Vector3(7,8,9);scope.RecordOwnedEyePose();transform.position=new Vector3(20,21,22);
            Refuse(scope.Dispose);int before=transform.Reads;transform.Throw=true;record.Publish();record.Publish();
            string body=File.ReadAllText(PathFor(root,run,1));
            check(transform.Reads==before,"Publishing cached failure after native getters become throwing performs zero further native pose reads");
            check(body.Contains("failure_reason: owned_eye_position_changed") && body.Contains("owned_world_position: [7, 8, 9]") && body.Contains("world_position: [20, 21, 22]"),"Actual original owned operand and pre-guard changed operand remain distinct");
            check(body.Contains("owned_run_id: "+run) && body.Contains("vr_frame_id: 11") && body.Contains("runtime_generation: 3") && body.Contains("rendered_pair_ordinal: 5"),"Failure binds owned run, frame, resource generation and pair ordinal");
            check(body.Contains("owned_position_equal: false") && body.Contains("owned_rotation_equal: null") && body.Contains("owned_pose_context_scales_matrices: unavailable") && body.Contains("exclude_ABA"),"Short-circuited comparisons and unsampled owned context remain unavailable; ABA limitation explicit");
            check(owner.Attempts==1 && Directory.GetFiles(root).Length==1 && new FileInfo(PathFor(root,run,1)).Length<=32768,"Repeated publication is idempotent and complete failure file stays bounded");
            transform.Throw=false;
            var query=owner.Begin(12,3,6);var queryScope=new CameraState(camera,null,CameraTransformRestoration.GuardedLocal,query);
            transform.position=new Vector3(4,5,6);queryScope.RecordOwnedEyePose();transform.Throw=true;
            Refuse(queryScope.Dispose);before=transform.Reads;query.Publish();string failed=File.ReadAllText(PathFor(root,run,2));
            check(transform.Reads==before && failed.Contains("query_error: native_query_failed") && !failed.Contains("private native failure") && failed.Contains("world_position: null"),"Native query fault retains partial cached fields and sanitized error without an invented snapshot");
            transform.Throw=false;
            var admission=owner.Begin(13,3,7);transform.localScale=new Vector3(float.NaN,1,1);
            Refuse(()=>new CameraState(camera,null,CameraTransformRestoration.GuardedLocal,admission));admission.Publish();
            check(File.ReadAllText(PathFor(root,run,3)).Contains("query_error: not_queried"),"Admission refusal leaves before/after stages unqueried instead of adding failure-time snapshots");
            transform.localScale=new Vector3(1,1,1);
            for(int i=4;i<=8;++i)
            {
                var r=owner.Begin((ulong)i,3,i);r.Failed(new CameraLocalRestoration(camera,transform.position,transform.rotation,true),"controlled_failure");r.Publish();
            }
            check(owner.Attempts==8 && owner.Begin(99,9,99)==null && Directory.GetFiles(root).Length==8,"Launch failure budget caps all publications at eight");
            var collision=new GuardedCameraFailureDiagnostics(root,run);var c=collision.Begin(1,1,1);c.Failed(null,"controlled_failure");string original=File.ReadAllText(PathFor(root,run,1));c.Publish();
            check(collision.LastError=="io_failure" && File.ReadAllText(PathFor(root,run,1))==original && Directory.GetFiles(root,"*.tmp").Length==0,"Atomic collision never replaces original evidence or leaves temporary output");
            var threadOwner=new GuardedCameraFailureDiagnostics(root,Guid.NewGuid().ToString("D"));GuardedCameraFailureRecord foreign=null;
            var thread=new Thread(()=>foreign=threadOwner.Begin(1,1,1));thread.Start();thread.Join();check(foreign==null && threadOwner.Attempts==0,"Wrong thread cannot admit failure collection");
            var publisher=new CameraRestorationPoseDiagnostics(root);string oversized=Path.Combine(root,"oversized.yaml");publisher.Publish(oversized,new string('x',32769));
            check(!File.Exists(oversized) && publisher.LastError=="diagnostic_query_or_serialization_failure" && Directory.GetFiles(root,"*.tmp").Length==0,"Byte overflow refuses the whole file before atomic publication");
            string blocked=Path.Combine(root,"blocked");File.WriteAllText(blocked,"sentinel");var ioOwner=new GuardedCameraFailureDiagnostics(blocked,run);var io=ioOwner.Begin(1,1,1);io.Failed(null,"controlled_failure");io.Publish();
            check(ioOwner.Attempts==1 && ioOwner.LastError=="io_failure" && File.ReadAllText(blocked)=="sentinel","I/O failure retains original target and consumes one bounded attempt");
            var bad=ioOwner.Begin(1,1,1);bad.Failed(null,new string('x',129));bad.Publish();
            check(ioOwner.Attempts==2 && ioOwner.LastError=="diagnostic_query_or_serialization_failure","Invalid or oversized failure vocabulary refuses the whole record without masking the original failure");
            string matrixRun=Guid.NewGuid().ToString("D");var matrixOwner=new GuardedCameraFailureDiagnostics(root,matrixRun);var matrix=matrixOwner.Begin(15,8,19);
            camera.ResetViewAction=()=>{throw new Exception("private matrix setter");};
            Refuse(()=>new CameraState(camera,null,CameraTransformRestoration.GuardedLocal,matrix));matrix.Publish();camera.ResetViewAction=null;
            string matrixBody=File.ReadAllText(PathFor(root,matrixRun,1));
            check(matrixBody.Contains("failure_reason: matrix_snapshot_or_restore_exception") && matrixBody.Contains("query_error: not_queried") && !matrixBody.Contains("private matrix setter"),"Matrix admission failure retains cached saved state and sanitizes setter faults without inventing restoration snapshots");
            check(matrixBody.Contains("failure_notification_phase: constructor_matrix_admission") && body.Contains("failure_notification_phase: guarded_dispose"),"Constructor admission and nested-scope disposal notifications cannot be conflated by a constant ordering label");
            ExtremeRecord(check,root);
            OverlayOverlap(check,root,false);OverlayOverlap(check,root,true);
        }
        finally
        {
            string resolved=Path.GetFullPath(root),temp=Path.GetFullPath(Path.GetTempPath());
            if(!resolved.StartsWith(temp,StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(resolved).StartsWith("ugevr-guard-failure-",StringComparison.Ordinal))throw new Exception("Unsafe temporary cleanup.");
            Directory.Delete(resolved,true);
        }
    }
    static void ExtremeRecord(Action<bool,string> check,string root)
    {
        var raw=new Vector3(float.MinValue,float.MinValue,float.MinValue);var rotation=new Quaternion(float.MinValue,float.MinValue,float.MinValue,float.MinValue);
        var matrix=new float[16];for(int i=0;i<16;++i)matrix[i]=float.MinValue;
        var parent=new Transform {localPosition=raw,localRotation=rotation,localScale=raw,lossyScale=raw,localToWorldMatrix=new Matrix4x4 {Elements=matrix},worldToLocalMatrix=new Matrix4x4 {Elements=matrix}};
        var child=new Transform {parent=parent,localPosition=raw,localRotation=rotation,localScale=raw,lossyScale=raw,localToWorldMatrix=new Matrix4x4 {Elements=matrix},worldToLocalMatrix=new Matrix4x4 {Elements=matrix}};
        var camera=new Camera {transform=child};var cached=new CameraLocalRestoration(camera,raw,rotation,true);cached.RecordOwnedEyePose();
        child.localPosition=new Vector3(1,2,3);cached.ChooseLocal();cached.ObserveAfterRestoration();
        string run=Guid.NewGuid().ToString("D");var owner=new GuardedCameraFailureDiagnostics(root,run);var record=owner.Begin(1,1,1);record.Failed(cached,cached.Refusal);record.Publish();
        long bytes=new FileInfo(PathFor(root,run,1)).Length;Console.WriteLine("Extreme cached guarded failure fixture bytes: "+bytes);
        check(bytes<=32768 && owner.LastError=="","Complete three-stage parented extreme-finite operand record fits the hard byte bound without truncation");
    }
    static void OverlayOverlap(Action<bool,string> check,string root,bool setterFault)
    {
        Canvas.Instances.Clear();Canvas.ForceAction=null;
        try
        {
            var rect=new RectTransform {sizeDelta=new Vector2(1920,1080),pivot=new Vector2(.5f,.5f),localScale=new Vector3(1,1,1),localRotation=new Quaternion(0,0,0,1),anchoredPosition3D=new Vector3(1,2,3)};
            var canvas=new Canvas {renderMode=RenderMode.ScreenSpaceOverlay,Rects=new[]{rect}};Canvas.Instances.Add(canvas);
            var camera=new Camera {transform=rect};string run=Guid.NewGuid().ToString("D");
            var owner=new GuardedCameraFailureDiagnostics(root,run);var evidence=owner.Begin(88,4,17);
            var overlay=new OverlayCanvasScope(camera,1,2,false);overlay.SetCommonHead(new RigidPose(new Position(0,0,0),Orientation.Identity));evidence.Bind(null,overlay);
            bool threw=false;
            try
            {
                using(var restore=new CameraState(camera,null,CameraTransformRestoration.GuardedLocal,evidence))
                using(overlay)
                {
                    evidence.Bind(restore.CachedLocalRestoration,overlay);
                    rect.position=new Vector3(7,8,9);restore.RecordOwnedEyePose();overlay.ApplyEye();
                    if(setterFault) {rect.LocalRotationSetter=q=>{throw new Exception("controlled setter");};camera.TargetSetter=t=>{throw new Exception("controlled target setter");};}
                }
            }
            catch(InvalidOperationException) {threw=true;}
            overlay.RebuildRestoredGeometry();evidence.Publish();
            if(!setterFault)
            {
                check(!threw && owner.Attempts==0 && !File.Exists(PathFor(root,run,1)),"Exact already-restored overlay camera completes without manufacturing failure evidence");
                return;
            }
            check(threw,"An independent borrowed-camera setter fault still rejects the already-restored path");
            string body=File.ReadAllText(PathFor(root,run,1));
            check(body.Contains("camera_rect_id: "+rect.GetInstanceID()) && body.Contains("camera_dispose_pose_write_attempts: 3") && body.Contains("camera_final_rebuild_write_attempts: 2"),"Cached actual object overlap tracks scope-dispose and later final-rebuild write attempts separately");
            check(body.Contains("world_position: [7, 8, 9]") && body.Contains("world_position: [1, 2, 3]") && body.Contains("failure_reason: restoration_setter_or_query_exception"),"Cached exact overlay restoration cannot hide another borrowed-camera setter failure");
            check((setterFault?overlay.RestorationConflicts>0:overlay.RestorationConflicts==0) && body.Contains("attempts_are_not_successes: true") && body.Contains("local_scale: [1, 1, 1]") && body.Contains("local_to_world_matrix:"),"Handover evidence retains original expected values, cached matrices and bounded ownership limits");
            check(body.Contains("camera_dispose_pose_setters_returned: "+(setterFault?2:3)) && body.Contains("camera_final_rebuild_setters_returned: "+(setterFault?1:2)) && body.Contains("camera_dispose_layout_setters_returned: 4"),"Actual setter returns are distinct from attempts, including a controlled setter exception without retries");
        }
        finally {Canvas.Instances.Clear();Canvas.ForceAction=null;}
    }
}
