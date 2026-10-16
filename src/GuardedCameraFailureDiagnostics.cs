using System;
using System.IO;
using System.Threading;

namespace Ugevr
{
    // Opt-in private evidence. Reads only caches already used by restoration.
    public sealed class GuardedCameraFailureDiagnostics
    {
        public const int MaximumFailures=8;
        readonly CameraRestorationPoseDiagnostics publisher;
        readonly string directory,run;
        readonly int thread=Thread.CurrentThread.ManagedThreadId;
        int attempts;
        public int Attempts {get {return attempts;}}
        public string LastError {get {return publisher.LastError;}}
        public string LastFailureStage {get {return publisher.LastFailureStage;}}
        public string LastExceptionKind {get {return publisher.LastExceptionKind;}}
        public static GuardedCameraFailureDiagnostics Create(bool captureImages,bool poseDetails,bool ownedSession,string directory,string run)
        {return captureImages && poseDetails && ownedSession?new GuardedCameraFailureDiagnostics(directory,run):null;}
        public GuardedCameraFailureDiagnostics(string directory,string run)
        {
            Guid parsed;
            try {parsed=new Guid(run);}catch {throw new ArgumentException("An owned session run ID is required.");}
            this.run=parsed.ToString("D");this.directory=Path.GetFullPath(directory);
            publisher=new CameraRestorationPoseDiagnostics(directory);
        }
        public GuardedCameraFailureRecord Begin(ulong frame,ulong generation,int ordinal)
        {
            if(thread!=Thread.CurrentThread.ManagedThreadId || attempts>=MaximumFailures)return null;
            return new GuardedCameraFailureRecord(this,frame,generation,ordinal);
        }
        internal void Publish(GuardedCameraFailureRecord record)
        {
            if(thread!=Thread.CurrentThread.ManagedThreadId || attempts>=MaximumFailures)return;
            // Failures and collisions consume the bounded launch allowance too.
            ++attempts;
            var stage=CameraEvidenceFailureStage.DestinationPath;
            try
            {
                string destination=Path.Combine(directory,"guarded-camera-failure-"+run+"-"+attempts+".yaml");
                stage=CameraEvidenceFailureStage.CachedSerialization;string text=record.Serialize(publisher,run);
                publisher.Publish(destination,text);
            }
            catch(Exception error){publisher.Failed(error,stage);}
        }
    }
    public sealed class GuardedCameraFailureRecord
    {
        readonly GuardedCameraFailureDiagnostics owner;
        readonly ulong frame,generation;
        readonly int ordinal;
        CameraLocalRestoration restoration;
        OverlayCanvasScope overlays;
        string reason;
        string notificationPhase="external_unclassified",publicationPhase="caller_requested";
        bool published;
        internal GuardedCameraFailureRecord(GuardedCameraFailureDiagnostics owner,ulong frame,ulong generation,int ordinal)
        {this.owner=owner;this.frame=frame;this.generation=generation;this.ordinal=ordinal;}
        public void Bind(CameraLocalRestoration value,OverlayCanvasScope scope)
        {
            restoration=value;overlays=scope;
            if(scope!=null && value!=null)scope.TrackGuardedCameraOverlap(value);
        }
        public void Failed(CameraLocalRestoration value,string failure,string phase="external_unclassified")
        {restoration=value;reason=failure;notificationPhase=phase;if(overlays!=null && value!=null)overlays.TrackGuardedCameraOverlap(value);}
        internal string Serialize(CameraRestorationPoseDiagnostics publisher,string run)
        {
            var cached=new CameraRestorationPoseRecord(publisher,null,frame,generation,ordinal,"");
            cached.SelectLocalMode("guarded-local");
            if(restoration!=null)
            {
                cached.SelectLocalRestoration(restoration);
                cached.Observe("camera_state_saved");cached.Observe("before_camera_state_dispose");cached.Observe("after_camera_state_dispose");
            }
            string overlap=overlays==null?"overlay_handover: null\n":overlays.CachedGuardedCameraHandover();
            return "owned_run_id: "+run+"\nprivate_guarded_failure: true\nfailure_reason: "+SafeReason(reason)+
                "\nevidence_source: existing_guard_caches_only\nnew_engine_queries_for_failure: 0\n"+
                "owned_pose_context_scales_matrices: unavailable_not_sampled_at_owned_eye\n"+
                "guard_operands: saved_owned_before_camera_state_dispose_after_camera_state_dispose\n"+
                "eligibility_pose_arithmetic: exact_raw_float_components_with_original_short_circuit\n"+
                "post_world_arithmetic: original_Unity_Vector3_and_Quaternion_equality\n"+
                "capture_aggregate_comparison: separate_not_invoked_by_failure_evidence\n"+
                "ownership_limit: sampled_identity_and_exact_operands_do_not_prove_writer_or_exclude_ABA\n"+
                "failure_notification_phase: "+SafeReason(notificationPhase)+"\npublication_phase: "+SafeReason(publicationPhase)+"\n"+
                "successful_constructor_scope_order_contract: overlay_dispose_then_camera_guard_then_final_overlay_rebuild\n"+
                "constructor_refusal_order_contract: notification_then_outer_overlay_cleanup_attempt\n"+
                "failure_evidence_changes_restoration_policy: false\ncached_record_policy_field_describes_selected_mode: true\n"+overlap+cached.ToPrivateYaml();
        }
        static string SafeReason(string value)
        {
            if(value==null)return "unavailable";
            if(value.Length>128)throw new InvalidOperationException("Failure reason exceeds bound.");
            foreach(char c in value)if(!(c>='a'&&c<='z') && c!='_')throw new InvalidOperationException("Invalid failure vocabulary.");
            return value;
        }
        public void Publish()
        {if(published || reason==null)return;published=true;owner.Publish(this);}
        internal void PublishAfterPairUnwind() {publicationPhase="injector_outer_finally_after_cleanup_unwind";Publish();}
    }
}
