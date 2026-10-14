using System;
using System.IO;
using System.Threading;

namespace Ugevr
{
    // Pose-only request ownership. One pending claim, no content parsing or scene queries.
    // Moving the sentinel gives this pair its exact request; later original-path writes
    // remain separate requests instead of being consumed by a completed eye pair.
    public sealed class CameraPoseCaptureRequests : IDisposable
    {
        readonly string directory,request;
        readonly int ownerThread=Thread.CurrentThread.ManagedThreadId;
        string pending;
        CameraPoseCaptureRequest active;
        bool disposed;
        public string LastError {get;private set;}
        public CameraPoseCaptureRequests(string evidenceDirectory)
        {
            if(String.IsNullOrEmpty(evidenceDirectory) || !Path.IsPathRooted(evidenceDirectory)) throw new ArgumentException("Absolute private evidence directory required.");
            directory=Path.GetFullPath(evidenceDirectory);request=Path.Combine(directory,"capture-request");LastError="";
        }
        public CameraPoseCaptureRequest BeforePair(bool enabled,bool capacityAvailable=true)
        {
            if(!enabled || disposed || ownerThread!=Thread.CurrentThread.ManagedThreadId) return null;
            if(active!=null) {LastError="pose_capture_pair_already_active";return null;}
            if(!capacityAvailable) {LastError=pending!=null || File.Exists(request)?"pose_capture_budget_exhausted":"";return null;}
            try
            {
                CameraRestorationPoseDiagnostics.CheckPath(directory);
                if(pending!=null && !File.Exists(pending)) {LastError="pose_capture_claim_missing";pending=null;return null;}
                if(pending==null)
                {
                    if(!File.Exists(request)) {LastError="";return null;}
                    if((File.GetAttributes(request)&FileAttributes.ReparsePoint)!=0) throw new IOException("Capture request reparse point.");
                    string claim=Path.Combine(directory,"capture-request.camera-pose-"+Guid.NewGuid().ToString("N")+".claimed");
                    File.Move(request,claim);pending=claim;
                }
                active=new CameraPoseCaptureRequest(this,pending);LastError="";return active;
            }
            catch(Exception error) {LastError=CameraRestorationPoseDiagnostics.Failure(error);return null;}
        }
        internal bool CanConsume(CameraPoseCaptureRequest token)
        {return !disposed && ownerThread==Thread.CurrentThread.ManagedThreadId && Object.ReferenceEquals(active,token) && pending==token.RequestPath;}
        internal void Consumed(CameraPoseCaptureRequest token)
        {
            if(!CanConsume(token)) throw new InvalidOperationException("Capture request ownership changed.");
            // CaptureRequestAdmission successfully deleted this exact claimed path.
            if(File.Exists(pending)) throw new InvalidOperationException("Capture request deletion unconfirmed.");
            pending=null;
        }
        internal void EndPair(CameraPoseCaptureRequest token)
        {
            if(ownerThread!=Thread.CurrentThread.ManagedThreadId || !Object.ReferenceEquals(active,token)) return;
            active=null;ReturnPending();
        }
        void ReturnPending()
        {
            if(pending==null) return;
            try
            {
                CameraRestorationPoseDiagnostics.CheckPath(directory);
                if(!File.Exists(pending)) {LastError="pose_capture_claim_missing";pending=null;return;}
                // Never overwrite a new request. Retain the one owned claim for next pair.
                if(File.Exists(request)) return;
                File.Move(pending,request);pending=null;
            }
            catch(Exception error) {LastError=CameraRestorationPoseDiagnostics.Failure(error);}
        }
        public void Dispose()
        {
            if(disposed || ownerThread!=Thread.CurrentThread.ManagedThreadId) return;
            disposed=true;
            if(active!=null) {active.Dispose();active=null;}
            ReturnPending();
            // A colliding new request is preserved; an older retained claim is also
            // preserved on teardown, never adopted/deleted by another owner instance.
            if(pending!=null) LastError="pose_capture_pending_claim_preserved";
        }
    }
    public sealed class CameraPoseCaptureRequest : IDisposable
    {
        readonly CameraPoseCaptureRequests owner;
        bool ended,consumed;
        public readonly string RequestPath;
        internal CameraPoseCaptureRequest(CameraPoseCaptureRequests owner,string path) {this.owner=owner;RequestPath=path;}
        public bool TryConsume(CaptureRequestAdmission admission,bool poseRecordAdmitted)
        {
            // Verify liveness before deletion, including destruction during an eye callback.
            if(!poseRecordAdmitted || ended || consumed || !owner.CanConsume(this)) return false;
            if(!admission.TryConsume(RequestPath)) return false;
            owner.Consumed(this);consumed=true;return true;
        }
        public void Dispose() {if(ended) return;ended=true;owner.EndPair(this);}
    }
}
