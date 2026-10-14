using System;
using System.IO;
using System.Threading;
using Ugevr;
using UnityEngine;

static class CameraRestorationPoseChecks
{
    sealed class ThrowingTransform : Transform {public override Vector3 position {get {throw new InvalidOperationException("private-message-must-not-appear");} set {}}}
    static string Request(string root) {string request=Path.Combine(root,"capture-request");File.WriteAllText(request,"");return request;}
    static void RemoveFixture(string root)
    {
        string temp=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        string resolved=Path.GetFullPath(root);Guid value;
        string name=Path.GetFileName(resolved),prefix="ugevr-camera-pose-";
        if(!resolved.StartsWith(temp,StringComparison.OrdinalIgnoreCase) || !name.StartsWith(prefix,StringComparison.Ordinal) || !Guid.TryParseExact(name.Substring(prefix.Length),"N",out value))throw new Exception("Unsafe fixture cleanup.");
        Directory.Delete(resolved,true);
    }
    public static void Run(Action<bool,string> check)
    {
        string root=Path.Combine(Path.GetTempPath(),"ugevr-camera-pose-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var camera=new Camera();var parent=new Transform();camera.transform.parent=parent;
            camera.transform.position=new Vector3(1.25f,-2.5f,3.75f);camera.transform.rotation=new Quaternion(.1f,.2f,.3f,.9f);
            var owner=new CameraRestorationPoseDiagnostics(root);string request=Request(root);
            using(new CameraState(camera)){}
            check(camera.InstanceIdQueries==0 && camera.transform.InstanceIdQueries==0,"Normal CameraState construction and disposal perform no diagnostic identity queries");
            check(owner.Begin(false,request,camera,1)==null && camera.InstanceIdQueries==0 && camera.transform.InstanceIdQueries==0 && owner.Attempts==0,"Disabled camera diagnostics perform no pose or identity queries and consume no budget");
            var wrong=new Thread(()=>check(owner.Begin(true,request,camera,1)==null,"Foreign thread cannot begin camera evidence"));wrong.Start();wrong.Join();
            var record=owner.Begin(true,request,camera,77,null,null,3,4200);
            check(record!=null && File.Exists(request),"Passive diagnosis admits an existing capture request without consuming it");
            camera.transform.position=new Vector3(4,5,6); // The restore snapshot differs from the earlier anchor.
            using(var restore=new CameraState(camera,record))
            {
                camera.transform.position=new Vector3(7,8,9);record.Observe("after_left_render");
                camera.transform.position=new Vector3(10,11,12);record.Observe("after_right_render");
            }
            check(camera.transform.position.x==4 && camera.transform.rotation.w==.9f,"Diagnostic preserves the actual CameraState restoration behavior");
            camera.transform.parent=new Transform();camera.transform.position=new Vector3(13,14,15);record.Observe("after_canvas_flush");
            record.Observe("after_current_context");
            camera.transform.position=new Vector3(16,17,18);record.Observe("after_eye_captures");record.CaptureRequestConsumed=true;record.ExistingGameCameraRestored=false;
            string body=record.ToPrivateYaml();record.Publish();
            check(body.Contains("vr_frame_id: 77") && body.Contains("runtime_generation: 3") && body.Contains("rendered_pair_ordinal: 4200") && body.Contains("existing_game_camera_restored: false") && body.Contains("capture_request_consumed: true"),"Private diagnostic retains the original aggregate result and request/frame/generation association");
            check(body.Contains("world_position: [1.25, -2.5, 3.75]") && body.Contains("world_position: [4, 5, 6]") && body.Contains("world_position: [16, 17, 18]"),"Actual anchor, saved restoration state and final capture pose are independently serialized");
            check(body.Contains("parent_instance_id: "+parent.GetInstanceID()) && body.Contains("parent_instance_id: "+camera.transform.parent.GetInstanceID()),"Parent identity changes remain distinct private observations");
            string[] stages={"anchor_before_scope","camera_state_saved","after_left_render","after_right_render","after_camera_state_dispose","after_canvas_flush","after_current_context","after_eye_captures"};int prior=-1;
            foreach(string stage in stages){int index=body.IndexOf("stage: "+stage,StringComparison.Ordinal);check(index>prior,"Actual controlled rendering/restoration path orders pose stage "+stage);prior=index;}
            check(!body.Contains("fixture") && body.Contains("local_position:") && body.Contains("local_rotation_xyzw:") && body.Contains("world_rotation_xyzw:"),"Whole poses are recorded without object names or game getters");
            check(File.ReadAllText(Path.Combine(root,"camera-restoration-pose-1.yaml"))==body && new FileInfo(Path.Combine(root,"camera-restoration-pose-1.yaml")).Length<CameraRestorationPoseDiagnostics.MaximumBytes,"Whole private capture publishes under its fixed byte bound");
            camera.transform.position=new Vector3(20,21,22);record.Observe("after_eye_captures");record.Publish();
            check(File.ReadAllText(Path.Combine(root,"camera-restoration-pose-1.yaml"))==body,"Published camera evidence cannot append stages or overwrite its file");
            camera.transform.position=new Vector3(float.NaN,float.PositiveInfinity,0);camera.transform.parent=null;
            var nonfinite=owner.Begin(true,request,camera,78);string nonfiniteBody=nonfinite.ToPrivateYaml();nonfinite.Publish();
            check(nonfiniteBody.Contains("world_position: ['NaN', 'Infinity', 0]") && nonfiniteBody.Contains("world_position_finite: false") && nonfiniteBody.Contains("has_parent: false\n    parent_instance_id: null"),"Nonfinite values and absent parents are explicit rather than repaired");
            var destroyed=new Camera {Destroyed=true};var missing=owner.Begin(true,request,destroyed,79);missing.Publish();
            string missingBody=File.ReadAllText(Path.Combine(root,"camera-restoration-pose-3.yaml"));
            check(missingBody.Contains("query_error: camera_unavailable") && missingBody.Contains("world_position: null"),"Destroyed camera yields unavailable pose evidence without stopping capture policy");
            var throwing=new Camera {transform=new ThrowingTransform()};var failed=owner.Begin(true,request,throwing,80);failed.Publish();
            string failedBody=File.ReadAllText(Path.Combine(root,"camera-restoration-pose-4.yaml"));
            check(failedBody.Contains("query_error: diagnostic_query_or_serialization_failure") && !failedBody.Contains("private-message"),"Read failure is bounded and sanitized without an invented pose");
            var collision=owner.Begin(true,request,camera,81);File.WriteAllText(Path.Combine(root,"camera-restoration-pose-5.yaml"),"sentinel");collision.Publish();
            check(File.ReadAllText(Path.Combine(root,"camera-restoration-pose-5.yaml"))=="sentinel" && owner.LastError=="io_failure" && Directory.GetFiles(root,"*.tmp").Length==0,"Output collision preserves existing evidence and removes only its own temporary file");
            for(int i=6;i<=8;++i){var bounded=owner.Begin(true,request,camera,(ulong)i);for(int stage=0;stage<20;++stage)bounded.Observe("after_left_render");string boundedBody=bounded.ToPrivateYaml();bounded.Publish();check(boundedBody.Split(new[]{"  - stage:"},StringSplitOptions.None).Length-1==CameraRestorationPoseDiagnostics.MaximumStages,"Camera sample caps extra stages without unbounded observations");}
            check(owner.Begin(true,request,camera,9)==null && owner.Attempts==8,"A launch permits at most eight camera pose captures");
            var recreated=new CameraRestorationPoseDiagnostics(root);check(recreated.Begin(true,request,camera,10)==null && recreated.Attempts==8,"Existing evidence keeps recreation within the same eight-file budget");
            File.Delete(request);check(new CameraRestorationPoseDiagnostics(root).Begin(true,request,camera,11)==null,"No capture request means no pose evidence");
            check(Directory.GetFiles(root,"camera-restoration-pose-*.yaml").Length==8,"Only the eight owned bounded evidence files are produced");
            string failureRoot=Path.Combine(root,"failure");Directory.CreateDirectory(failureRoot);string failureRequest=Request(failureRoot);
            var failureOwner=new CameraRestorationPoseDiagnostics(failureRoot);var serialization=failureOwner.Begin(true,failureRequest,camera,99);serialization.EyeCaptureAttempts=3;serialization.Publish();
            check(failureOwner.LastError=="diagnostic_query_or_serialization_failure" && Directory.GetFiles(failureRoot,"camera-restoration-pose-*.yaml").Length==0 && Directory.GetFiles(failureRoot,"*.tmp").Length==0,"Failed whole-record serialization produces no partial diagnostic file and retains a sanitized failure");
            var missingStages=failureOwner.Begin(true,failureRequest,camera,100);missingStages.Publish();string incomplete=File.ReadAllText(Path.Combine(failureRoot,"camera-restoration-pose-2.yaml"));
            check(!incomplete.Contains("stage: after_camera_state_dispose") && !incomplete.Contains("stage: after_eye_captures") && incomplete.Contains("existing_game_camera_restored: null") && incomplete.Contains("eye_captures_returned: false"),"Interrupted rendering leaves later stages and aggregate unavailable rather than claiming restoration or capture completion");
            camera.transform=new RectTransform {WorldParentOffset=new Vector3(100,200,300)};camera.transform.position=new Vector3(101,202,303);
            var savedAnchor=failureOwner.Begin(true,failureRequest,camera,101,new Vector3(-1,-2,-3),new Quaternion(0,0,0,1));
            using(new CameraState(camera,savedAnchor)){}
            string separated=savedAnchor.ToPrivateYaml();savedAnchor.Publish();
            check(separated.Contains("world_position: [-1, -2, -3]") && separated.Contains("world_position: [101, 202, 303]") && separated.Contains("local_position: [1, 2, 3]"),"Actual earlier anchor, saved world pose and parent-relative local pose stay distinct without normalized substitution");
        }
        finally {RemoveFixture(root);}
    }
}
