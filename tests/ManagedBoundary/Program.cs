using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using Ugevr;
using UnityEngine;

class Program
{
    static int checks;
    static void Assert(bool value,string message) { if (!value) throw new Exception(message); ++checks; }
    static void Near(double actual,double expected,string message) { Assert(Math.Abs(actual-expected)<1e-8,message+": actual="+actual+", expected="+expected); }
    static Orientation XRotation(double degrees) { double angle=degrees*Math.PI/360; return new Orientation(Math.Sin(angle),0,0,Math.Cos(angle)); }
    static RigidPose Rigid(Position position,Orientation rotation) { return new RigidPose(position,rotation); }
    static void Main(string[] args)
    {
        if (args.Length>0 && args[0]=="producer") { Produce(args[1],args[2]); return; }
        if (args.Length>0 && args[0]=="lock-status") { LockStatus(args[1],args[2]); return; }
        MathChecks(); RestoreChecks(); TrackingReacquisitionChecks(); OverlayChecks(); PanelGeometryChecks(); SessionChecks();
        Console.WriteLine("Managed boundary checks passed: "+checks);
    }
    static void TrackingReacquisitionChecks()
    {
        var tracking=new TrackingReacquisition(5);
        Assert(tracking.Observe(false,101,NativePoseCallbackStatus.InvalidPose,0)==PoseCallbackDisposition.AwaitCallback,"Errors from an unacknowledged old frame are not classified as the current pose");
        Assert(tracking.Observe(true,0,NativePoseCallbackStatus.InvalidPose,10)==PoseCallbackDisposition.RetryPose,"Successful wait with invalid HMD starts fresh-pose reacquisition");
        Assert(tracking.Recovering && !tracking.Paused,"Reacquisition retains the runtime and remains distinct from pause");
        Assert(tracking.Observe(true,0,NativePoseCallbackStatus.InvalidPose,12)==PoseCallbackDisposition.RetryPose,"Repeated invalid poses permit fresh frames within one deadline");
        Assert(tracking.Observe(true,0,NativePoseCallbackStatus.Success,14)==PoseCallbackDisposition.ReadPose,"A native valid result permits reading before the deadline");
        Assert(tracking.Recovering,"Native validity alone does not reset recovery before managed pose validation");
        Assert(tracking.Observe(true,0,NativePoseCallbackStatus.InvalidPose,14.9)==PoseCallbackDisposition.RetryPose,"Managed invalid flags after a valid native wait keep the original deadline");
        Assert(tracking.Observe(false,0,NativePoseCallbackStatus.Success,15)==PoseCallbackDisposition.PauseTracking,"Recovery deadline pauses even while a callback is still pending");
        Assert(tracking.Paused && tracking.Observe(true,0,NativePoseCallbackStatus.Success,15.1)==PoseCallbackDisposition.PauseTracking,"Late valid poses cannot restart a paused window without explicit retry");
        bool prematureConfirmation=false;
        try { tracking.ConfirmValidPose(); } catch (InvalidOperationException) { prematureConfirmation=true; }
        Assert(prematureConfirmation,"Paused state cannot promote a sample into rendering or recentering");
        tracking.Reset();
        Assert(!tracking.Paused && !tracking.Recovering,"An explicit retry opens a fresh tracking window");
        Assert(tracking.Observe(true,0,NativePoseCallbackStatus.Success,20)==PoseCallbackDisposition.ReadPose,"Explicit retry can accept restored tracking");
        tracking.ConfirmValidPose();
        Assert(!tracking.Recovering,"Both valid samples complete recovery");
        tracking.Observe(true,0,NativePoseCallbackStatus.InvalidPose,30);
        tracking.Observe(true,0,NativePoseCallbackStatus.Success,31); tracking.ConfirmValidPose();
        Assert(tracking.Observe(true,0,NativePoseCallbackStatus.InvalidPose,40)==PoseCallbackDisposition.RetryPose,"A later independent interruption starts its own deadline");
        Assert(tracking.Observe(true,0,NativePoseCallbackStatus.Success,45)==PoseCallbackDisposition.PauseTracking,"A valid callback arriving at the bounded deadline requires manual retry");
        foreach(int error in new[] {-1,-2,-10,-11,-12,-14,-15,-16,-17,101})
        {
            tracking.Reset();
            Assert(tracking.Observe(true,0,(NativePoseCallbackStatus)error,0)==PoseCallbackDisposition.Fail,"Non-pose bridge error retains rejection: "+error);
        }
        tracking.Reset();
        Assert(tracking.Observe(true,101,NativePoseCallbackStatus.InvalidPose,0)==PoseCallbackDisposition.Fail,"Invalid-pose status cannot hide an actual OpenVR wait error");
        Assert(tracking.Observe(true,-1,NativePoseCallbackStatus.InvalidPose,0)==PoseCallbackDisposition.Fail,"Invalid-pose status cannot hide a wait that was not performed");
        Assert(!tracking.Recovering,"Rejected runtime errors do not enter reacquisition");
        bool invalidTimeout=false;
        try { new TrackingReacquisition(Double.NaN); } catch (ArgumentOutOfRangeException) { invalidTimeout=true; }
        Assert(invalidTimeout,"Tracking deadline requires a finite positive interval");
    }
    static void MathChecks()
    {
        var identity=Orientation.Identity;
        var origin=new TrackingOrigin();
        origin.RecenterPosition(new Position(2,1,3)); origin.RecenterYaw(identity);
        var head=Rigid(new Position(3,2,5),identity);
        var anchor=Rigid(new Position(10,0,20),Orientation.Yaw(Math.PI/2)*XRotation(30));
        double cant=10*Math.PI/180;
        var eye=Rigid(new Position(.03,0,0),Orientation.Yaw(cant));
        var composed=TrackingMath.ComposeEye(anchor,head,eye,origin,2,false);
        Near(composed.Position.X,14,"Yaw-only anchor translates Z into X");
        Near(composed.Position.Y,2,"Game pitch does not tilt physical translation");
        Near(composed.Position.Z,17.94,"Eye separation and head translation use the same scale");
        var forward=composed.Rotation.Rotate(new Position(0,0,1));
        Near(forward.X,Math.Cos(cant),"Full canted eye rotation reaches world orientation");
        Near(forward.Y,0,"Game pitch removed"); Near(forward.Z,-Math.Sin(cant),"Canted eye sign");
        var opposite=TrackingMath.ComposeEye(anchor,head,Rigid(new Position(-.03,0,0),Orientation.Yaw(-cant)),origin,2,false);
        Near(opposite.Position.Z-composed.Position.Z,.12,"IPD scales in meters");
        Near(opposite.Rotation.Rotate(new Position(0,0,1)).Z,Math.Sin(cant),"Opposite cant stays distinct");
        var tiltedHead=Rigid(new Position(2,1,3),XRotation(20));
        var live=TrackingMath.ComposeEye(anchor,tiltedHead,Rigid(new Position(0,0,0),identity),origin,1,false);
        Near(live.Rotation.Rotate(new Position(0,0,1)).Y,-Math.Sin(20*Math.PI/180),"Tracked head pitch remains live");
        var retained=TrackingMath.ComposeEye(anchor,tiltedHead,Rigid(new Position(0,0,0),identity),origin,1,true);
        Near(retained.Rotation.Rotate(new Position(0,0,1)).Y,-Math.Sin(50*Math.PI/180),"Explicit game tilt composes");
        origin.RecenterHeight(new Position(9,7,8));
        Near(origin.Center.X,2,"Height recenter preserves X"); Near(origin.Center.Y,7,"Height recenter changes Y"); Near(origin.Center.Z,3,"Height recenter preserves Z");
        origin.RecenterYaw(Orientation.Yaw(Math.PI/2));
        Near(origin.Center.Y,7,"Yaw recenter preserves position");
        var recentered=TrackingMath.ComposeEye(Rigid(new Position(0,0,0),identity),Rigid(origin.Center,Orientation.Yaw(Math.PI/2)),Rigid(new Position(0,0,0),identity),origin,1,false);
        Near(recentered.Rotation.Rotate(new Position(0,0,1)).Z,1,"Recenter yaw cancels reference yaw");
        double s=Math.Sin(Math.PI/6),c=Math.Cos(Math.PI/6);
        var converted=TrackingMath.FromOpenVr(new[] {c,0,s,1,0,1,0,2,-s,0,c,3});
        Near(converted.Position.Z,-3,"OpenVR position basis conversion");
        Near(converted.Rotation.Rotate(new Position(0,0,1)).X,-s,"OpenVR rotation basis conversion");
        var halfTurn=TrackingMath.FromOpenVr(new double[] {-1,0,0,0,0,1,0,0,0,0,-1,0});
        Near(halfTurn.Rotation.Rotate(new Position(0,0,1)).Z,-1,"Matrix conversion handles 180-degree branch");
        bool badScale=false;
        try { TrackingMath.ComposeEye(anchor,head,eye,origin,Double.NaN,false); } catch (ArgumentOutOfRangeException) { badScale=true; }
        Assert(badScale,"Nonfinite scale rejected");
    }
    static void RestoreChecks()
    {
        var camera=new Camera(); var target=new RenderTexture();
        camera.transform.position=new Vector3(4,5,6); camera.transform.rotation=new Quaternion(0,0,0,1);
        camera.targetTexture=target; camera.projectionMatrix=new Matrix4x4 {Marker=7}; camera.worldToCameraMatrix=new Matrix4x4 {Marker=8};
        camera.rect=new Rect {x=.1f,y=.2f,width=.3f,height=.4f}; camera.useOcclusionCulling=true;
        camera.backgroundColor=new Color {r=.2f,g=.3f,b=.4f,a=.5f}; camera.clearFlags=CameraClearFlags.Skybox;
        CameraState scope=null;
        try
        {
            using (scope=new CameraState(camera))
            {
                camera.transform.position=new Vector3(9,9,9); camera.transform.rotation=new Quaternion(1,0,0,0);
                camera.targetTexture=new RenderTexture(); camera.projectionMatrix=new Matrix4x4 {Marker=99}; camera.worldToCameraMatrix=new Matrix4x4 {Marker=98};
                camera.rect=new Rect(); camera.useOcclusionCulling=false; camera.backgroundColor=new Color(); camera.clearFlags=CameraClearFlags.Nothing;
                throw new InvalidOperationException("Simulated eye-render failure");
            }
        }
        catch (InvalidOperationException) { }
        Assert(camera.transform.position.x==4 && camera.transform.position.y==5 && camera.transform.position.z==6,"Position restored after failed render");
        Assert(camera.transform.rotation.w==1,"Rotation restored"); Assert(System.Object.ReferenceEquals(camera.targetTexture,target),"Target restored");
        Assert(camera.projectionMatrix.Marker==7 && camera.worldToCameraMatrix.Marker==8,"Projection and view restored");
        Assert(camera.rect.x==.1f && camera.rect.y==.2f && camera.rect.width==.3f && camera.rect.height==.4f,"Viewport restored");
        Assert(camera.useOcclusionCulling,"Occlusion restored"); Assert(camera.backgroundColor.a==.5f && camera.backgroundColor.b==.4f,"Background restored");
        Assert(camera.clearFlags==CameraClearFlags.Skybox,"Clear flags restored");
        camera.transform.position=new Vector3(100,0,0); scope.Dispose(); Assert(camera.transform.position.x==100,"Repeated Dispose does not undo subsequent game state");
        var automatic=new Camera();
        using (new CameraState(automatic))
        {
            automatic.projectionMatrix=new Matrix4x4 {Marker=99}; automatic.worldToCameraMatrix=new Matrix4x4 {Marker=98};
        }
        Assert(automatic.AutomaticProjection && automatic.AutomaticView,"Automatic camera matrices remain automatic after restoration");
        automatic.transform.position=new Vector3(42,0,0);
        Assert(automatic.worldToCameraMatrix.Marker==54,"Restored automatic view still follows subsequent game movement");
    }
    static void SessionChecks()
    {
        string directory=Path.Combine(Path.GetTempPath(),"UGEVR managed boundary "+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string id=Guid.NewGuid().ToString("D");
            var consumer=new InjectorSession(id,directory,4321);
            DateTime now=DateTime.UtcNow;
            ExternalProducer(directory,"run_id="+id+"\nrevision=1\nenabled=true\nrecenter=yaw\n");
            var command=consumer.Poll(now); Assert(command!=null && command.Revision==1 && command.Enabled && command.Recenter=="yaw","External producer command received");
            Assert(consumer.AcknowledgedRevision==0,"Receive does not acknowledge actual application");
            consumer.Acknowledge(command); Assert(consumer.AcknowledgedRevision==1,"Actual apply acknowledges revision");
            Assert(consumer.Poll(now.AddSeconds(1))==null,"Repeated command has no side effect");
            ExternalProducer(directory,"run_id="+Guid.NewGuid()+"\nrevision=2\nenabled=false\n");
            Assert(consumer.Poll(now.AddSeconds(2))==null && consumer.LastError.Contains("run_id"),"Wrong-run command rejected");
            ExternalProducer(directory,"run_id="+id+"\nrevision=2\nenabled=false\nexecute=anything\n");
            Assert(consumer.Poll(now.AddSeconds(3))==null && consumer.LastError.Contains("Unsupported"),"Unknown command key rejected without acknowledgment");
            ExternalProducer(directory,"run_id="+id+"\nrevision=2\nenabled=maybe\n");
            Assert(consumer.Poll(now.AddSeconds(4))==null && consumer.AcknowledgedRevision==1,"Invalid boolean remains pending");
            ExternalProducer(directory,"run_id="+id+"\nrevision=2\nenabled=false\n");
            var disable=consumer.Poll(now.AddSeconds(5)); Assert(disable!=null && !disable.Enabled,"External producer disable received"); consumer.Acknowledge(disable);
            ExternalProducer(directory,"run_id="+id+"\nrevision=1\nenabled=true\n");
            Assert(consumer.Poll(now.AddSeconds(6))==null && consumer.AcknowledgedRevision==2,"Stale revision cannot reenable");
            consumer.LastError="failure's detail\nsecond line";
            consumer.Publish(now,"2017.4.40f1",true,true,false,9,true);
            string status=File.ReadAllText(Path.Combine(directory,"injector-status.yaml"));
            Assert(status.Contains("schema_version: 1\n") && status.Contains("process_id: 4321\n"),"Versioned process-associated status");
            Assert(status.Contains("run_id: '"+id+"'\n") && status.Contains("acknowledged_revision: 2\n"),"Status has actual run and acknowledgment");
            Assert(status.Contains("last_error: 'failure''s detail second line'\n"),"Diagnostic scalars cannot inject YAML keys");
            Assert(status.Contains("rendering_active: false\n") && status.Contains("submitted_pairs: 9\n"),"Disabled status still has heartbeat and counters");
            consumer.Publish(now.AddSeconds(2),"2017.4.40f1",true,true,false,10,false);
            Assert(File.ReadAllText(Path.Combine(directory,"injector-status.yaml")).Contains("submitted_pairs: 10\n"),"Disabled heartbeat refreshes");
            Assert(Directory.GetFiles(directory,"*.tmp").Length==0,"Atomic status leaves no temporary files");
            ContentionChecks(consumer,directory,now);
            ExternalProducer(directory,new string('x',4097));
            Assert(consumer.Poll(now.AddSeconds(7))==null && consumer.LastError.Contains("4096"),"Oversized commands rejected");
        }
        finally { Directory.Delete(directory,true); }
    }
    static Canvas Overlay(string name,int layer)
    {
        var rect=new RectTransform {anchorMin=new Vector2(.1f,.2f),anchorMax=new Vector2(.8f,.9f),pivot=new Vector2(.3f,.4f),sizeDelta=new Vector2(3840,2160),
            anchoredPosition3D=new Vector3(2,3,4),localPosition=new Vector3(5,6,7),localScale=new Vector3(1,2,3),localRotation=new Quaternion(0,0,0,1)};
        var canvas=new Canvas {name=name,renderMode=RenderMode.ScreenSpaceOverlay,Rects=new[]{rect},Renderers=new[]{new CanvasRenderer()},planeDistance=100,
            sortingOrder=10,sortingLayerID=9,pixelPerfect=true,overridePixelPerfect=true,additionalShaderChannels=AdditionalCanvasShaderChannels.TexCoord1,overrideSorting=true,scaleFactor=2,referencePixelsPerUnit=200};
        canvas.gameObject.layer=layer;canvas.Renderers[0].gameObject.layer=layer;
        Canvas.Instances.Add(canvas);return canvas;
    }
    static void OverlayChecks()
    {
        Canvas.Instances.Clear(); Canvas.ForceAction=null;
        var overlay=Overlay("menu",5); var second=Overlay("loading",8);
        var world=Overlay("world",6); world.renderMode=RenderMode.WorldSpace;
        var cameraUi=Overlay("background",7);cameraUi.renderMode=RenderMode.ScreenSpaceCamera;
        var disabled=Overlay("disabled",5);disabled.isActiveAndEnabled=false;
        var otherDisplay=Overlay("other display",5);otherDisplay.targetDisplay=1;
        var nonroot=Overlay("nested",5);nonroot.isRootCanvas=false;
        var camera=new Camera {cullingMask=~(1<<8)};
        var originalCamera=new Camera(); overlay.worldCamera=originalCamera;
        Canvas.ForceAction=delegate {
            if (overlay.renderMode==RenderMode.WorldSpace)
            {
                overlay.Rects[0].sizeDelta=new Vector2(1399,1399); overlay.Rects[0].localScale=new Vector3(99,99,99);
                overlay.scaleFactor=4;overlay.referencePixelsPerUnit=99;overlay.sortingOrder=99;
                overlay.overridePixelPerfect=false;overlay.additionalShaderChannels=AdditionalCanvasShaderChannels.Normal;
            }
        };
        var scope=PanelScope(camera,1,2,false);
        try
        {
            using (scope) { scope.ApplyEye(); Assert(scope.SelectedCount==2,"Only active root overlays on the selected display are borrowed");
                Assert(overlay.renderMode==RenderMode.WorldSpace && overlay.worldCamera==camera,"Overlay common world panel binding applied");
                Assert(overlay.planeDistance==2,"Physical UI plane distance applied");Assert(camera.cullingMask==~(1<<8),"Excluded UI layers are not implicitly included");
                Assert(world.renderMode==RenderMode.WorldSpace && cameraUi.renderMode==RenderMode.ScreenSpaceCamera,"World and existing camera UI modes unchanged");
                throw new InvalidOperationException("Failed second-eye render"); }
        }
        catch (InvalidOperationException) { }
        Assert(overlay.renderMode==RenderMode.ScreenSpaceOverlay && overlay.worldCamera==originalCamera && overlay.planeDistance==100,"Canvas mode/camera/distance restored after render failure");
        Assert(overlay.scaleFactor==2 && overlay.referencePixelsPerUnit==200 && overlay.sortingOrder==10 && overlay.sortingLayerID==9 && overlay.pixelPerfect && overlay.overrideSorting,"Canvas scale and sort state restored");
        Assert(overlay.overridePixelPerfect && overlay.additionalShaderChannels==AdditionalCanvasShaderChannels.TexCoord1,"Canvas pixel override and shader channels restored");
        Assert(overlay.Rects[0].sizeDelta.Equals(new Vector2(3840,2160)) && overlay.Rects[0].localScale.Equals(new Vector3(1,2,3)),"Conversion layout changes restored");
        Assert(overlay.Rects[0].anchorMin.Equals(new Vector2(.1f,.2f)) && overlay.Rects[0].localPosition.Equals(new Vector3(5,6,7)) && overlay.Rects[0].localRotation.w==1,"RectTransform anchors and local pose restored");
        Assert(scope.RestorationConflicts==0,"Restoration verifies saved values");
        Assert(scope.DiagnosticJson().Contains("\"excludedOverlayLayers\":256"),"Excluded layer selection recorded");
        Canvas.ForceAction=null;
        using (var include=PanelScope(camera,1,2,true)) { include.ApplyEye(); Assert(camera.cullingMask==-1,"Explicit layer remedy includes selected renderer layers"); }
        Assert(camera.cullingMask==~(1<<8),"Explicit layer remedy restores camera mask");
        using (var clipped=PanelScope(camera,1,.1,false)) {Assert(clipped.PlaneDistance>camera.nearClipPlane && clipped.PlaneDistance<camera.farClipPlane,"UI plane is strictly inside clipping");}
        var failScope=PanelScope(camera,1,2,false);failScope.ApplyEye();
        overlay.ModeSetter=delegate(RenderMode value) {if (value==RenderMode.ScreenSpaceOverlay) throw new Exception("Restoration setter failure");};
        failScope.Dispose();
        Assert(failScope.RestorationConflicts>0 && failScope.RestorationError.Contains("setter failure"),"Restoration conflict reported");
        Assert(second.renderMode==RenderMode.ScreenSpaceOverlay && second.planeDistance==100,"One setter failure does not stop other canvas restoration");
        Assert(overlay.worldCamera==originalCamera && overlay.planeDistance==100,"One setter failure does not stop remaining properties");
        overlay.ModeSetter=null;overlay.renderMode=RenderMode.ScreenSpaceOverlay;
        var destroyed=PanelScope(camera,1,2,false);destroyed.ApplyEye();second.Destroyed=true;second.Rects[0].Destroyed=true;destroyed.Dispose();
        Assert(destroyed.DestroyedObjects==2,"Destroyed native objects skipped and reported");
        Assert(overlay.renderMode==RenderMode.ScreenSpaceOverlay,"Destroyed objects do not prevent surviving state restoration");
        int updates=0;
        Canvas.ForceAction=delegate {if (++updates==1) {overlay.Rects[0].sizeDelta=new Vector2(7,7);throw new Exception("Canvas update callback failed");}};
        var updateFailure=PanelScope(camera,1,2,false);
        try {using (updateFailure) updateFailure.ApplyEye();} catch (Exception) { }
        Assert(overlay.renderMode==RenderMode.ScreenSpaceOverlay && overlay.Rects[0].sizeDelta.Equals(new Vector2(3840,2160)),"Canvas update failure still restores mode and layout");
        Assert(updateFailure.RestorationConflicts==0,"Successful restoration after callback failure is distinct from failed rendering");
        Canvas.Instances.Clear();Canvas.ForceAction=null;
        for (int i=0;i<65;++i) Overlay("root"+i,5);
        bool limited=false;
        try {new OverlayCanvasScope(camera,1,2,false);} catch (InvalidOperationException) {limited=true;}
        Assert(limited,"Oversized root selection fails before conversion");
        Assert(Canvas.Instances.TrueForAll(canvas=>canvas.renderMode==RenderMode.ScreenSpaceOverlay),"Limit failure leaves original modes intact");
        Canvas.Instances.Clear();
    }
    static OverlayCanvasScope PanelScope(Camera camera,double units,double distance,bool layers)
    {
        var scope=new OverlayCanvasScope(camera,units,distance,layers);
        scope.SetCommonHead(new RigidPose(new Position(0,0,0),Orientation.Identity));
        return scope;
    }
    static void PanelGeometryChecks()
    {
        var center=OverlayPanelGeometry.Place(new RigidPose(new Position(0,0,0),Orientation.Identity),2,1.8,16.0/9,.5,.5);
        Near(center.Position.X,0,"Common panel center X");Near(center.Position.Y,0,"Common panel center Y");Near(center.Position.Z,2,"Common panel physical distance");
        Near(OverlayPanelGeometry.PixelScale(1.8,1920,2),.00046875,"Panel scale accounts for logical pixel width and uniform parent");
        double leftRay=.028803436/2,rightRay=-.028803436/2;
        double uLeft=(leftRay+1.667044)/(1.21214163+1.667044);
        double uRight=(rightRay+1.21058965)/(1.66956735+1.21058965);
        Assert(uLeft>.58 && uRight<.42,"One shared center projects to different normalized pixels with asymmetric frustums");
        Near((uLeft*(1.21214163+1.667044)-1.667044)*2-.028803436,0,"Left projection reconstructs shared panel center");
        Near((uRight*(1.66956735+1.21058965)-1.21058965)*2+.028803436,0,"Right projection reconstructs same shared panel center");
        var offsetPivot=OverlayPanelGeometry.Place(new RigidPose(new Position(10,0,0),Orientation.Yaw(Math.PI/2)),2,1.8,16.0/9,0,0);
        Near(offsetPivot.Position.X,12,"Head-forward panel follows yaw");Near(offsetPivot.Position.Y,-.50625,"Panel pivot retains desktop aspect");Near(offsetPivot.Position.Z,.9,"Pivot uses head-local horizontal extent");
        Canvas.Instances.Clear();Canvas.ForceAction=null;
        var overlay=Overlay("coupled",5);
        var rect=overlay.Rects[0]; rect.name="SpecialFeaturesMask";rect.AnchorReference=new Vector3(0,24.6f,0);rect.anchoredPosition3D=new Vector3(0,2.3f,0);
        var canonical=rect.anchoredPosition3D;var local=rect.localPosition;
        rect.localPosition=local;
        Assert(rect.anchoredPosition3D.y!=canonical.y,"Fixture reproduces float cancellation when localPosition overwrites anchoredPosition");
        Near(rect.anchoredPosition3D.y,2.299999237060547,"Fixture reproduces observed Firewatch mismatch");
        rect.anchoredPosition3D=canonical;
        var originalWorld=rect.position;
        var camera=new Camera();var scope=PanelScope(camera,1,2,false);
        using (scope)
        {
            scope.ApplyEye(0);var first=rect.position;var firstSize=rect.sizeDelta;var firstScale=rect.localScale;
            camera.transform.position=new Vector3(.03f,0,0);scope.ApplyEye(1);
            Assert(rect.position.Equals(first) && rect.sizeDelta.Equals(firstSize) && rect.localScale.Equals(firstScale),"Both eyes observe identical root geometry");
            Assert(rect.sizeDelta.x==3840 && rect.sizeDelta.y==2160,"Common panel preserves desktop layout aspect");
        }
        Assert(rect.anchoredPosition3D.Equals(canonical),"Canonical anchoredPosition3D restores exact original value");
        Assert(rect.position.Equals(originalWorld),"Restoring canonical layout also restores root world position");
        Assert(scope.RestorationConflicts==0,"Coupled-field restoration does not introduce false failure");
        Canvas.ForceAction=delegate {if(overlay.renderMode==RenderMode.WorldSpace) rect.DerivedLocalReadOffset=new Vector3(0,.000002f,0);};
        var roundoff=PanelScope(camera,1,2,false);using(roundoff) roundoff.ApplyEye();
        Assert(roundoff.RestorationConflicts==0 && roundoff.DerivedPositionRoundoff==1,"A representational derived-position ULP is recorded separately");
        Assert(roundoff.DiagnosticJson().Contains("\"acceptedDerivedRoundoff\":true"),"Accepted derived roundoff retains exact diagnostic values");
        rect.DerivedLocalReadOffset=new Vector3();
        Canvas.ForceAction=delegate {if(overlay.renderMode==RenderMode.WorldSpace) rect.DerivedLocalReadOffset=new Vector3(0,1,0);};
        var changed=PanelScope(camera,1,2,false);using(changed) changed.ApplyEye();
        Assert(changed.RestorationConflicts>0 && changed.DiagnosticJson().Contains("\"field\":\"localPosition\""),"A real derived layout change beyond ULP tolerance fails with exact field diagnostics");
        rect.DerivedLocalReadOffset=new Vector3();Canvas.ForceAction=null;
        rect.parent=new Transform {lossyScale=new Vector3(1,2,1)};
        bool parentRejected=false;
        try {PanelScope(camera,1,2,false);} catch (InvalidOperationException) {parentRejected=true;}
        Assert(parentRejected && overlay.renderMode==RenderMode.ScreenSpaceOverlay,"Nonuniform parent rejects before mutation instead of stretching physical UI");
        Canvas.Instances.Clear();Canvas.ForceAction=null;
    }
    static void ExternalProducer(string directory,string content)
    {
        var start=new ProcessStartInfo("dotnet");
        start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add("producer"); start.ArgumentList.Add(directory); start.ArgumentList.Add(Convert.ToBase64String(Encoding.UTF8.GetBytes(content)));
        start.UseShellExecute=false; start.CreateNoWindow=true;
        using (var process=Process.Start(start)) { process.WaitForExit(); Assert(process.ExitCode==0,"External producer exited successfully"); }
    }
    static void Produce(string directory,string base64)
    {
        string target=Path.Combine(directory,"command.txt"),temporary=target+".producer.tmp";
        File.WriteAllBytes(temporary,Convert.FromBase64String(base64));
        File.Move(temporary,target,true);
    }
    static Process ExternalStatusLock(string directory,string mode)
    {
        var start=new ProcessStartInfo("dotnet");
        start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add("lock-status"); start.ArgumentList.Add(directory); start.ArgumentList.Add(mode);
        start.UseShellExecute=false; start.CreateNoWindow=true; start.RedirectStandardOutput=true; start.RedirectStandardInput=true;
        var process=Process.Start(start);
        Assert(process.StandardOutput.ReadLine()=="locked","Separate process owns a status replacement lock");
        return process;
    }
    static void ContentionChecks(InjectorSession consumer,string directory,DateTime now)
    {
        if (Environment.OSVersion.Platform!=PlatformID.Win32NT) return;
        string path=Path.Combine(directory,"injector-status.yaml"),previous=File.ReadAllText(path);
        using (var blocker=ExternalStatusLock(directory,"hold"))
        {
            try
            {
                var timer=Stopwatch.StartNew();
                consumer.Publish(now.AddSeconds(3),"2017.4.40f1",true,true,false,11,true);
                timer.Stop();
                Assert(timer.ElapsedMilliseconds<500,"Replacement retries are bounded while another process retains its lock");
                Assert(consumer.LastError.StartsWith("status: "),"Exhausted rename retries report contention without throwing");
                Assert(File.ReadAllText(path)==previous,"Failed atomic replacement preserves prior complete heartbeat");
                Assert(Directory.GetFiles(directory,"*.tmp").Length==0,"Failed replacement removes temporary file");
            }
            finally { blocker.StandardInput.WriteLine("release"); blocker.WaitForExit(); }
        }
        consumer.Publish(now.AddSeconds(4),"2017.4.40f1",true,true,false,12,true);
        Assert(File.ReadAllText(path).Contains("submitted_pairs: 12\n") && consumer.LastError=="","Next heartbeat recovers after transient contention");
        using (var blocker=ExternalStatusLock(directory,"release-on-temp"))
        {
            consumer.Publish(now.AddSeconds(5),"2017.4.40f1",true,true,false,13,true);
            blocker.WaitForExit();
            Assert(blocker.ExitCode==0,"External reader releases lock after observing replacement temporary file");
            Assert(File.ReadAllText(path).Contains("submitted_pairs: 13\n") && consumer.LastError=="","Atomic retry succeeds when reader releases during retry window");
        }
    }
    static void LockStatus(string directory,string mode)
    {
        using (var input=new FileStream(Path.Combine(directory,"injector-status.yaml"),FileMode.Open,FileAccess.Read,FileShare.Read))
        {
            Console.WriteLine("locked"); Console.Out.Flush();
            if (mode=="hold") Console.ReadLine();
            else
            {
                var timer=Stopwatch.StartNew();
                while (Directory.GetFiles(directory,"injector-status.yaml.*.tmp").Length==0)
                {
                    if (timer.ElapsedMilliseconds>2000) throw new TimeoutException("No replacement attempt observed.");
                    Thread.Sleep(1);
                }
                Thread.Sleep(10);
            }
        }
    }
}
