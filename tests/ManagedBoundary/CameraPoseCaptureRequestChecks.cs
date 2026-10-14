using System;
using System.IO;
using System.Threading;
using UnityEngine;
using Ugevr;

static class CameraPoseCaptureRequestChecks
{
    static void Clean(string directory)
    {
        string path=Path.GetFullPath(directory),root=Path.GetFullPath(Path.GetTempPath());Guid id;
        string name=new DirectoryInfo(path).Name,prefix="ugevr-pose-request-";
        if(!path.StartsWith(root,StringComparison.OrdinalIgnoreCase) || !name.StartsWith(prefix,StringComparison.Ordinal) || !Guid.TryParseExact(name.Substring(prefix.Length),"N",out id)) throw new Exception("Unsafe test cleanup.");
        Directory.Delete(path,true);
    }
    public static void Run(Action<bool,string> check)
    {
        string directory=Path.Combine(Path.GetTempPath(),"ugevr-pose-request-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        string request=Path.Combine(directory,"capture-request");
        try
        {
            var camera=new Camera();var diagnostics=new CameraRestorationPoseDiagnostics(directory);var owner=new CameraPoseCaptureRequests(directory);
            var late=owner.BeforePair(true);var deferred=CaptureRequestAdmission.BeforePair(false,late!=null,true,request);
            File.WriteAllText(request,"late-first");
            check(late==null && !deferred.TryConsume(request) && File.Exists(request) && camera.InstanceIdQueries==0 && diagnostics.Attempts==0,"Pose-enabled late request defers without image/pose/native-UI queries in that pair");
            using(var admitted=owner.BeforePair(true))
            {
                check(admitted!=null && !File.Exists(request) && File.ReadAllText(admitted.RequestPath)=="late-first","Next pair owns the exact retained request before any eye begins");
                var token=CaptureRequestAdmission.BeforePair(false,true,true,admitted.RequestPath);
                var record=diagnostics.Begin(true,admitted.RequestPath,camera,4200,null,null,1,4200);
                check(record!=null && !token.CaptureNativeUiState,"Pose admission does not force native UI sampling");
                using(var restore=new CameraState(camera,record)) {record.Observe("after_left_render");record.Observe("after_right_render");}
                record.Observe("after_canvas_flush");record.Observe("after_current_context");
                check(admitted.TryConsume(token,record!=null),"Only the pre-admitted pose request is consumed after the successful pair");
                record.CaptureRequestConsumed=true;record.EyeCaptureAttempts=2;record.EyeCapturesReturned=true;record.ExistingGameCameraRestored=false;record.Observe("after_eye_captures");record.Publish();
                string result=File.ReadAllText(Path.Combine(directory,"camera-restoration-pose-1.yaml"));
                check(result.Contains("rendered_pair_ordinal: 4200") && result.Contains("capture_request_consumed: true") && result.Contains("eye_captures_returned: true") && result.Contains("existing_game_camera_restored: false"),"Diagnostic and image completion share the admitted frame and preserve its false restoration observation");
            }
            check(!File.Exists(request) && Directory.GetFiles(directory,"*.claimed").Length==0,"Completed pose request leaves no owned claim behind");
            File.WriteAllText(request,"first");
            using(var first=owner.BeforePair(true))
            {
                File.WriteAllText(request,"second");
                check(File.ReadAllText(first.RequestPath)=="first","A new original-path request cannot replace the pair's owned request");
                var token=CaptureRequestAdmission.BeforePair(false,true,false,first.RequestPath);check(first.TryConsume(token,true),"First claimed request can complete independently of a new request");
            }
            check(File.ReadAllText(request)=="second","Successful pair preserves the later request for the next pair");
            using(var second=owner.BeforePair(true))
            {check(File.ReadAllText(second.RequestPath)=="second","Next pair admits the separate newer request");var token=CaptureRequestAdmission.BeforePair(false,true,false,second.RequestPath);check(second.TryConsume(token,true),"Second claimed request completes with its live admission token");}
            File.WriteAllText(request,"interrupted");
            CameraRestorationPoseRecord interrupted;
            using(var token=owner.BeforePair(true))
            {
                interrupted=diagnostics.Begin(true,token.RequestPath,camera,4201);using(new CameraState(camera,interrupted)) {interrupted.Observe("after_left_render");}
                interrupted.Publish();
            }
            string partial=File.ReadAllText(Path.Combine(directory,"camera-restoration-pose-2.yaml"));
            check(File.ReadAllText(request)=="interrupted" && partial.Contains("capture_request_consumed: false") && partial.Contains("eye_captures_returned: false") && partial.Contains("existing_game_camera_restored: null") && !partial.Contains("stage: after_eye_captures"),"Interrupted pair records unavailable later stages and returns its exact request for retry");
            using(var first=owner.BeforePair(true)) {File.WriteAllText(request,"newer");}
            check(File.ReadAllText(request)=="newer" && Directory.GetFiles(directory,"*.claimed").Length==1,"Interrupted pair keeps at most one owned claim without overwriting a new request");
            using(var retained=owner.BeforePair(true))
            {check(File.ReadAllText(retained.RequestPath)=="interrupted","Retained older owned request receives the next pair before the newer original-path request");var token=CaptureRequestAdmission.BeforePair(true,true,true,retained.RequestPath);check(token.CaptureNativeUiState && retained.TryConsume(token,true),"Combined native UI and pose admission use the same exact claimed request");}
            check(File.ReadAllText(request)=="newer","Retained-claim completion preserves the next request");
            check(owner.BeforePair(false)==null && File.ReadAllText(request)=="newer","Disabled diagnostics neither claim nor consume pending image requests");
            int beforeQueries=camera.InstanceIdQueries;check(owner.BeforePair(true,false)==null && owner.LastError=="pose_capture_budget_exhausted" && File.Exists(request) && camera.InstanceIdQueries==beforeQueries,"Exhausted pose budget preserves requests without additional camera queries or undiagnosed images");
            var foreign=new Thread(()=>check(owner.BeforePair(true)==null,"Wrong thread cannot admit a pose capture request"));foreign.Start();foreign.Join();
            using(var unadmitted=owner.BeforePair(true))
            {check(diagnostics.Begin(false,unadmitted.RequestPath,camera,4202)==null,"Pose record opt-out cannot create evidence");check(!unadmitted.TryConsume(CaptureRequestAdmission.BeforePair(false,true,false,unadmitted.RequestPath),false) && File.Exists(unadmitted.RequestPath),"Missing pose record refuses consumption even with an eligible image token");}
            check(File.ReadAllText(request)=="newer","Pair without a pose record retains its admitted request");
            if(Environment.OSVersion.Platform==PlatformID.Win32NT)
            {
                using(var token=owner.BeforePair(true))
                {
                    bool failed=false;using(var locked=new FileStream(token.RequestPath,FileMode.Open,FileAccess.Read,FileShare.None))
                    {try {token.TryConsume(CaptureRequestAdmission.BeforePair(false,true,true,token.RequestPath),true);}catch(IOException){failed=true;}}
                    check(failed && File.Exists(token.RequestPath),"Failed claimed-request deletion cannot claim image or pose completion");
                }
                check(File.ReadAllText(request)=="newer","Cleared file error restores the same request for a new eligible pair");
            }
            File.Delete(request);File.WriteAllText(request,"teardown-old");
            var teardown=owner.BeforePair(true);var teardownAdmission=CaptureRequestAdmission.BeforePair(false,true,true,teardown.RequestPath);
            File.WriteAllText(request,"teardown-new");owner.Dispose();
            check(!teardown.TryConsume(teardownAdmission,true) && File.ReadAllText(teardown.RequestPath)=="teardown-old" && File.ReadAllText(request)=="teardown-new","Teardown during a pair refuses consumption before deletion and preserves both owned and newer requests");
            check(owner.BeforePair(true)==null && owner.LastError=="pose_capture_pending_claim_preserved","Disposed owner cannot reacquire an interrupted claim");
            var replacement=new CameraPoseCaptureRequests(directory);
            using(var latest=replacement.BeforePair(true))
            {
                check(File.ReadAllText(latest.RequestPath)=="teardown-new" && File.ReadAllText(teardown.RequestPath)=="teardown-old","A new owner claims the newer request without adopting an old retained claim");
                var latestAdmission=CaptureRequestAdmission.BeforePair(false,true,false,latest.RequestPath);
                bool foreignConsumed=true;var foreignConsumer=new Thread(()=>foreignConsumed=latest.TryConsume(latestAdmission,true));foreignConsumer.Start();foreignConsumer.Join();
                check(!foreignConsumed && File.Exists(latest.RequestPath),"Wrong thread cannot delete an admitted request");
                check(latest.TryConsume(latestAdmission,true) && !latest.TryConsume(latestAdmission,true),"A live admitted request consumes once only");
            }
            replacement.Dispose();File.Delete(request);
            var original=CaptureRequestAdmission.BeforePair(false,true,true,request);File.WriteAllText(request,"ordinary");
            check(original.TryConsume(request),"Flag-off ordinary image capture continues accepting a late request");
            var native=CaptureRequestAdmission.BeforePair(true,true,true,request);File.WriteAllText(request,"native");
            check(!native.TryConsume(request) && File.Exists(request),"Flag-off native UI admission retains its existing pre-pair deferral");
        }
        finally {Clean(directory);}
    }
}
