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
        MathChecks(); ProjectionChecks(); RestoreChecks(); TrackingReacquisitionChecks(); OverlayChecks(); PanelGeometryChecks(); RotationRoundoffChecks(); AfterEffectsChecks(); CameraRectOrderChecks(); InactiveCanvasChecks(); SessionChecks(); KeyboardProbeChecks();
        UiLayoutDiagnosticChecks.Run(Assert);
        OverlayIsolationChecks.Run(Assert);
        HierarchyInventoryChecks.Run(Assert);
        NativeUiDiagnosticsChecks.Run(Assert);
        EngineInputExperimentChecks.Run(Assert);
        Console.WriteLine("Managed boundary checks passed: "+checks);
    }
    static void ProjectionChecks()
    {
        // Independent Steam Frame GetProjectionMatrix observations at near=1,
        // far=2500. XY coefficients do not depend on depth API conventions.
        double[][] observations={
            new[]{-1.667044,1.2121416,-1.7561004,1.157068,.6946409,-.15799691,.68653774,-.20562917},
            new[]{-1.2105896,1.6695673,-1.7206812,1.177319,.69440657,.15935856,.69013107,-.18749554}
        };
        foreach (double[] data in observations)
        {
            EyeFrustum f=EyeProjection.FromOpenVrRaw(data[0],data[1],data[2],data[3],1,2500);
            double sx=2*f.Near/(f.Right-f.Left), ox=(f.Right+f.Left)/(f.Right-f.Left);
            double sy=2*f.Near/(f.Top-f.Bottom), oy=(f.Top+f.Bottom)/(f.Top-f.Bottom);
            Assert(Math.Abs(sx-data[4])<1e-7 && Math.Abs(ox-data[5])<1e-7,"Asymmetric horizontal projection matches independent runtime matrix");
            Assert(Math.Abs(sy-data[6])<1e-7 && Math.Abs(oy-data[7])<1e-7,"Asymmetric vertical projection matches independent runtime matrix");
            Assert(Math.Abs(-oy+data[7])<1e-7,"Head-forward ray has the runtime's vertical NDC location");
            Near(sy*f.Bottom/f.Near-oy,-1,"Lower clipping edge maps to NDC -1");
            Near(sy*f.Top/f.Near-oy,1,"Upper clipping edge maps to NDC +1");
            EyeFrustum scaled=EyeProjection.FromOpenVrRaw(data[0],data[1],data[2],data[3],.3,100);
            Near((scaled.Top+scaled.Bottom)/(scaled.Top-scaled.Bottom),oy,"Game near clip preserves vertical asymmetry");
        }
        EyeFrustum symmetric=EyeProjection.FromOpenVrRaw(-1,1,-1,1,.5,50);
        Near(symmetric.Bottom,-.5,"Symmetric headset lower bound is unchanged");
        Near(symmetric.Top,.5,"Symmetric headset upper bound is unchanged");
        foreach (double[] bad in new[]{new[]{Double.NaN,1.0,-1,1,.3,100},new[]{-1.0,1,Double.PositiveInfinity,1,.3,100},new[]{1.0,1,-1,1,.3,100},new[]{-1.0,1,1,-1,.3,100},new[]{-1.0,1,-1,1,0,100},new[]{-1.0,1,-1,1,.3,.3},new[]{-1e38,1e38,-1,1,1e38,2e38}})
        {
            bool rejected=false;
            try { EyeProjection.FromOpenVrRaw(bad[0],bad[1],bad[2],bad[3],bad[4],bad[5]); } catch(ArgumentException) { rejected=true; }
            Assert(rejected,"Invalid projection bounds fail before camera rendering");
        }
    }
    sealed class ProbeReader : IKeyboardEReader
    {
        public int Reads;
        public string UnavailableReason { get; set; }
        public bool ThrowOnRead;
        public KeyboardESample Read()
        {
            ++Reads;
            if (ThrowOnRead) throw new InvalidOperationException("controlled read failure");
            return new KeyboardESample { DevicePresent=true,DeviceEnabled=true,Down=Reads==1,Press=Reads==1,Release=Reads==2 };
        }
    }
    static void KeyboardProbeChecks()
    {
        string root=Path.Combine(Path.GetTempPath(),"ugevr-keyboard-probe-"+Guid.NewGuid().ToString("N"));
        string evidence=Path.Combine(root,"UnityVR-evidence"); Directory.CreateDirectory(evidence);
        string request=Path.Combine(evidence,"input-probe-request");
        DateTime utc=new DateTime(2026,1,1,0,0,0,DateTimeKind.Utc);
        string run=Guid.NewGuid().ToString("D");
        var reader=new ProbeReader(); int factories=0;
        var probe=new KeyboardEProbe(root,run,17,1000,delegate { ++factories; return reader; });
        try
        {
            File.WriteAllText(request,"duration_seconds=4\n");
            probe.Tick(false,true,utc,0);
            Assert(factories==0 && reader.Reads==0 && File.Exists(request),"Input probe opt-out leaves sentinel untouched and executes no engine getter");
            probe.Tick(true,false,utc,0);
            Assert(factories==1 && probe.Active && reader.Reads==0,"Unfocused input probe starts a bounded window without observing keys");
            probe.Tick(true,true,utc.AddSeconds(1),1000);
            probe.Tick(true,true,utc.AddSeconds(2),2000);
            probe.Tick(true,false,utc.AddSeconds(3),3000);
            probe.Tick(true,true,utc.AddSeconds(4),4000);
            string result=File.ReadAllText(Path.Combine(evidence,"input-probe-result-1.yaml"));
            Assert(!probe.Active && reader.Reads==2 && probe.RequestsConsumed==1,"Four-second deadline excludes a fifth sample and clears reader state");
            Assert(result.Contains("run_id: '"+run+"'") && result.Contains("process_id: 17") && result.Contains("monotonic_deadline_ticks: 4000") && result.Contains("frame_count: 4") && result.Contains("focused_frame_count: 2"),"Probe result correlates identity and actual bounded frame window");
            Assert(result.Contains("down_seen: true") && result.Contains("press_seen: true") && result.Contains("release_seen: true") && result.Contains("input_receipt: 'unknown'") && result.Contains("application_focused_all_frames: false"),"Probe aggregates only focused fixed-key observations without claiming game receipt");
            probe.Tick(true,true,utc.AddSeconds(5),5000);
            Assert(reader.Reads==2 && factories==1,"Consumed request never replays and idle updates execute no input getters");
            File.WriteAllText(request,"duration_seconds=4\nkey=Space\n");
            probe.Tick(true,true,utc.AddSeconds(6),6000);
            string invalid=File.ReadAllText(Path.Combine(evidence,"input-probe-result-2.yaml"));
            Assert(invalid.Contains("status: 'invalid_request'") && factories==1,"Arbitrary key or field request is rejected before reader construction");
            File.WriteAllText(request,"duration_seconds=4\n");
            probe.Tick(true,true,utc.AddSeconds(7),7000);
            int beforeCancel=reader.Reads;
            probe.Cancel(utc.AddMilliseconds(7500),7500);
            probe.Tick(true,true,utc.AddSeconds(8),8000);
            Assert(!probe.Active && reader.Reads==beforeCancel && File.ReadAllText(Path.Combine(evidence,"input-probe-result-3.yaml")).Contains("status: 'cancelled'"),"Lifecycle cancellation writes partial result and stops getter calls");
            File.WriteAllText(request,"duration_seconds=4\n");
            probe.Tick(true,true,utc.AddSeconds(9),9000);
            probe.Tick(false,true,utc.AddMilliseconds(9500),9500);
            Assert(!probe.Active && File.ReadAllText(Path.Combine(evidence,"input-probe-result-4.yaml")).Contains("capture_opt_in_removed"),"Removing capture opt-in cancels the active window");
            File.WriteAllText(request,"duration_seconds=4\n"); int count=reader.Reads;
            probe.Tick(true,true,utc.AddSeconds(10),10000);
            Assert(reader.Reads==count && File.Exists(request) && probe.RequestsConsumed==4,"Four-request run limit preserves later sentinel without observing input");
            var restarted=new KeyboardEProbe(root,run,17,1000,delegate { ++factories; return reader; });
            restarted.Tick(true,true,utc.AddSeconds(11),11000);
            Assert(restarted.RequestsConsumed==4 && File.Exists(request) && reader.Reads==count,"Component restart preserves the same run's request limit");
            Assert(Directory.GetFiles(evidence,"*.tmp").Length==0,"Atomic input result writes leave no temporary file");
        }
        finally { Directory.Delete(Path.GetFullPath(root),true); }

        string unavailableRoot=Path.Combine(Path.GetTempPath(),"ugevr-keyboard-probe-unavailable-"+Guid.NewGuid().ToString("N"));
        string unavailableEvidence=Path.Combine(unavailableRoot,"UnityVR-evidence"); Directory.CreateDirectory(unavailableEvidence);
        try
        {
            var unavailableReader=new ProbeReader { UnavailableReason="assembly_not_loaded" };
            var unavailable=new KeyboardEProbe(unavailableRoot,run,17,1000,delegate { return unavailableReader; });
            File.WriteAllText(Path.Combine(unavailableEvidence,"input-probe-request"),"duration_seconds=4\n");
            unavailable.Tick(true,true,utc,0);
            Assert(!unavailable.Active && unavailableReader.Reads==0 && File.ReadAllText(Path.Combine(unavailableEvidence,"input-probe-result-1.yaml")).Contains("status: 'unavailable'"),"Missing InputSystem is a diagnostic result without key calls or VR failure");
            var throwingReader=new ProbeReader { ThrowOnRead=true };
            var throwing=new KeyboardEProbe(unavailableRoot,run,17,1000,delegate { return throwingReader; });
            File.WriteAllText(Path.Combine(unavailableEvidence,"input-probe-request"),"duration_seconds=4\n");
            throwing.Tick(true,true,utc,0);
            Assert(!throwing.Active && throwingReader.Reads==1 && File.ReadAllText(Path.Combine(unavailableEvidence,"input-probe-result-2.yaml")).Contains("status: 'error'"),"Public getter failure ends only the diagnostic and records a bounded error");
            int oversizedFactories=0;
            var oversized=new KeyboardEProbe(unavailableRoot,run,17,1000,delegate { ++oversizedFactories; return throwingReader; });
            File.WriteAllText(Path.Combine(unavailableEvidence,"input-probe-request"),new string('x',65536));
            oversized.Tick(true,true,utc,0);
            Assert(oversizedFactories==0 && File.ReadAllText(Path.Combine(unavailableEvidence,"input-probe-result-3.yaml")).Contains("status: 'invalid_request'"),"Oversized sentinel is rejected with a bounded read before engine getter resolution");
            var malformed=new KeyboardEProbe(unavailableRoot,run,17,1000,delegate { ++oversizedFactories; return throwingReader; });
            File.WriteAllText(Path.Combine(unavailableEvidence,"input-probe-request"),"duration_seconds=5");
            malformed.Tick(true,true,utc,0);
            Assert(oversizedFactories==0 && File.ReadAllText(Path.Combine(unavailableEvidence,"input-probe-result-4.yaml")).Contains("status: 'invalid_request'"),"Requested duration cannot extend the four-second window");
        }
        finally { Directory.Delete(Path.GetFullPath(unavailableRoot),true); }
        Guid verified=new Guid("eb31f34a-975f-46d1-9e8a-f468772f04b5");
        Assert(PublicKeyboardEReader.MatchesIdentity(new AssemblyName("Unity.InputSystem, Version=1.4.1.0"),verified),"Receiver identity accepts only verified InputSystem package artifact");
        Assert(!PublicKeyboardEReader.MatchesIdentity(new AssemblyName("Unity.InputSystem, Version=1.5.0.0"),verified) && !PublicKeyboardEReader.MatchesIdentity(new AssemblyName("Game.InputManager, Version=1.4.1.0"),verified) && !PublicKeyboardEReader.MatchesIdentity(new AssemblyName("Unity.InputSystem, Version=1.4.1.0"),Guid.Empty),"Different version, game assembly or unverified artifact is unavailable");
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
        rect.gameObject=canvas.gameObject;
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
            scope.RecordAfterRender(1);
            Assert(scope.UnexpectedGeometryChanges==0,"Stable post-render root state is recorded separately");
            rect.sizeDelta=new Vector2(1,1);scope.RecordAfterRender(1);
            Assert(scope.UnexpectedGeometryChanges==1 && scope.DiagnosticJson().Contains("\"afterRenderEyes\""),"Changed root geometry during rendering is observable");
            rect.sizeDelta=firstSize;
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
    static Quaternion NormalizeQuaternion(Quaternion value)
    {
        double norm=Math.Sqrt((double)value.x*value.x+(double)value.y*value.y+(double)value.z*value.z+(double)value.w*value.w);
        return new Quaternion((float)(value.x/norm),(float)(value.y/norm),(float)(value.z/norm),(float)(value.w/norm));
    }
    static void RotationRoundoffChecks()
    {
        Canvas.Instances.Clear();Canvas.ForceAction=null;
        var overlay=Overlay("menu",5);var rect=overlay.Rects[0];rect.name="spinner_save";
        var original=new Quaternion(0,0,-.651493669f,-.758654f);rect.localRotation=original;
        var camera=new Camera();var scope=PanelScope(camera,1,2,false);
        rect.LocalRotationSetter=NormalizeQuaternion;
        using(scope) scope.ApplyEye();
        Near(rect.localRotation.z,-.6514937281608582,"Quaternion setter reproduces observed spinner Z normalization");
        Near(rect.localRotation.w,-.7586540579795837,"Quaternion setter reproduces observed spinner W normalization");
        Assert(scope.RestorationConflicts==0 && scope.RotationRoundoff==1,"One-ULP unit-quaternion normalization accepted separately");
        Assert(scope.DiagnosticJson().Contains("\"acceptedRotationRoundoff\":true") && scope.DiagnosticJson().Contains("\"unitQuaternion\":true"),"Quaternion roundoff records raw values and unit checks");
        rect.LocalRotationSetter=null;rect.localRotation=original;
        var sign=PanelScope(camera,1,2,false);
        rect.LocalRotationSetter=value=>new Quaternion(-value.x,-value.y,-value.z,-value.w);
        using(sign) sign.ApplyEye();
        Assert(sign.RestorationConflicts==0 && sign.DiagnosticJson().Contains("\"hemisphereAligned\":true"),"Equivalent quaternion sign is not a real rotation change");
        foreach(var invalid in new[]{new Quaternion(0,0,0,0),new Quaternion(0,0,.5f,.5f),new Quaternion(float.NaN,0,0,1)})
        {
            rect.LocalRotationSetter=null;rect.localRotation=original;var rejected=PanelScope(camera,1,2,false);
            rect.LocalRotationSetter=value=>invalid;
            using(rejected) rejected.ApplyEye();
            Assert(rejected.RestorationConflicts>0 && rejected.RotationRoundoff==0,"Zero, nonunit and nonfinite quaternion are rejected");
            Assert(rejected.DiagnosticJson().Contains("\"acceptedRotationRoundoff\":false"),"Invalid quaternion remains a recorded restoration failure");
        }
        rect.LocalRotationSetter=null;rect.localRotation=original;var angle=PanelScope(camera,1,2,false);
        rect.LocalRotationSetter=value=>new Quaternion(0,0,(float)Math.Sin(.5),(float)Math.Cos(.5));
        using(angle) angle.ApplyEye();
        Assert(angle.RestorationConflicts>0 && angle.RotationRoundoff==0,"Real unit-quaternion angle change rejected");
        Canvas.Instances.Clear();Canvas.ForceAction=null;
    }
    static void AfterEffectsChecks()
    {
        Canvas.Instances.Clear();Canvas.ForceAction=null;Resources.Extras.Clear();LayerMask.Name=null;
        var overlay=Overlay("menu",5);var originalWorld=Overlay("world",7);originalWorld.renderMode=RenderMode.WorldSpace;
        var used=new Transform();used.gameObject.layer=31;Resources.Extras.Add(used);
        LayerMask.Name=layer=>layer==30?"Reserved":null;
        var scene=new Camera {enabled=true,targetTexture=new RenderTexture(),renderingPath=RenderingPath.DeferredShading,cullingMask=-1,clearFlags=CameraClearFlags.Skybox};
        var ui=new Camera();var originalTarget=scene.targetTexture;
        var scope=PanelScope(scene,1,2,false);scope.UseAfterEffectsCamera(ui);
        var order=new System.Collections.Generic.List<string>();
        using(scope)
        {
            scope.ApplyEye(0);
            Assert(scope.IsolatedMask==1<<29,"Private layer avoids loaded scene objects and named project layers");
            Assert(overlay.gameObject.layer==29 && overlay.Renderers[0].gameObject.layer==29,"Only selected root and UI renderer objects move to private layer");
            Assert(originalWorld.gameObject.layer==7 && originalWorld.renderMode==RenderMode.WorldSpace,"Original world-space UI remains unchanged");
            Assert(scene.cullingMask==~(1<<29),"Game eye excludes only unused private UI layer");
            OverlayCameraPass.Prepare(scene,ui,scope.IsolatedMask);
            scene.RenderAction=delegate {order.Add("world-effects");};
            ui.RenderAction=delegate {
                order.Add("ui-no-effects");Assert(ui.targetTexture==originalTarget,"UI pass writes the same eye target");
                Assert(ui.clearFlags==CameraClearFlags.Depth && ui.renderingPath==RenderingPath.Forward && !ui.enabled,"Private UI camera clears depth and stays disabled for normal rendering");
                Assert(ui.cullingMask==1<<29 && !ui.useOcclusionCulling,"UI pass draws only private UI, without scene occlusion");
            };
            scene.Render();scope.ValidateLayerExclusive();OverlayCameraPass.Render(ui);scope.RecordAfterRender(0);
            Assert(order.Count==2 && order[0]=="world-effects" && order[1]=="ui-no-effects","UI follows the original game render and effects");
            Assert(ui.targetTexture==null && scene.targetTexture==originalTarget,"Private camera releases borrowed target without changing game target");
        }
        Assert(overlay.gameObject.layer==5 && overlay.Renderers[0].gameObject.layer==5 && scene.cullingMask==-1,"All owned layers and camera mask restore");
        Assert(scope.RestorationConflicts==0 && scope.DiagnosticJson().Contains("\"afterImageEffects\":true"),"After-effects ownership and scope are recorded");
        bool rebuilt=false;
        Canvas.ForceAction=delegate {rebuilt=true;Assert(overlay.renderMode==RenderMode.ScreenSpaceOverlay && overlay.gameObject.layer==5 && scene.cullingMask==-1,"Final rebuild runs in restored desktop/layer context");};
        scope.RebuildRestoredGeometry();
        Assert(rebuilt && scope.RestorationConflicts==0,"Restored layout rebuild is verified before returning to desktop input");
        Canvas.ForceAction=null;
        var failing=PanelScope(scene,1,2,false);failing.UseAfterEffectsCamera(ui);
        try {using(failing) {failing.ApplyEye();OverlayCameraPass.Prepare(scene,ui,failing.IsolatedMask);ui.RenderAction=delegate {throw new Exception("UI render failure");};OverlayCameraPass.Render(ui);}} catch(Exception) { }
        Assert(ui.targetTexture==null && overlay.gameObject.layer==5 && overlay.renderMode==RenderMode.ScreenSpaceOverlay,"UI render failure releases target and restores borrowed canvas/layers");
        var collision=PanelScope(scene,1,2,false);collision.UseAfterEffectsCamera(ui);
        bool rejected=false;
        using(collision)
        {
            collision.ApplyEye();var unowned=new Renderer();unowned.gameObject.layer=29;Resources.Extras.Add(unowned);
            try {collision.ValidateLayerExclusive();} catch(InvalidOperationException) {rejected=true;}
        }
        Assert(rejected && overlay.gameObject.layer==5,"Unowned renderer entering the private layer rejects UI pass with restoration");
        Resources.Extras.Clear();
        var changed=new OverlayLayerLease(new[]{overlay.gameObject});
        changed.Apply();overlay.gameObject.layer=12;changed.Dispose();
        Assert(overlay.gameObject.layer==12 && changed.Errors.Length==1,"Cleanup preserves and reports a newer game-owned layer change");
        overlay.gameObject.layer=5;
        var partial=PanelScope(scene,1,2,false);partial.UseAfterEffectsCamera(ui);
        overlay.Renderers[0].gameObject.LayerSetter=layer=>{if(layer==31) throw new Exception("layer assignment rejected");};
        bool layerFailed=false;
        try {using(partial) {partial.ApplyEye();}} catch(Exception) {layerFailed=true;}
        overlay.Renderers[0].gameObject.LayerSetter=null;
        Assert(layerFailed && overlay.gameObject.layer==5 && overlay.Renderers[0].gameObject.layer==5 && partial.RestorationConflicts==0,"Partial layer assignment failure restores already borrowed objects without inventing conflicts on untouched objects");
        var raced=PanelScope(scene,1,2,false);raced.UseAfterEffectsCamera(ui);
        var newcomer=new Transform();newcomer.gameObject.layer=31;Resources.Extras.Add(newcomer);
        bool layerRaced=false;
        try {using(raced) {raced.ApplyEye();}} catch(InvalidOperationException) {layerRaced=true;}
        Assert(layerRaced && overlay.gameObject.layer==5,"A new loaded object occupying the private layer before apply rejects before mutation");
        Resources.Extras.Clear();LayerMask.Name=layer=>"named";
        bool unavailable=false;
        try {var noLayer=PanelScope(scene,1,2,false);noLayer.UseAfterEffectsCamera(ui);} catch(InvalidOperationException) {unavailable=true;}
        Assert(unavailable && overlay.gameObject.layer==5,"No eligible layer fails before mutating game objects");
        overlay.NestedCanvases=new[]{overlay,originalWorld};
        bool mixedRejected=false;string mixedError="";
        try {PanelScope(scene,1,2,false);} catch(InvalidOperationException error) {mixedRejected=true;mixedError=error.Message;}
        Assert(mixedRejected && originalWorld.gameObject.layer==7 && originalWorld.renderMode==RenderMode.WorldSpace,"Independent world-space canvas inside a selected hierarchy requires an adapter before any mutation");
        Assert(mixedError.Contains("independent canvas world") && mixedError.Contains("mode=WorldSpace active=True display=0"),"Nested canvas guard names the offending engine object and bounded mode/activity/display context");
        overlay.NestedCanvases=null;
        Canvas.Instances.Clear();Canvas.ForceAction=null;Resources.Extras.Clear();LayerMask.Name=null;
    }
    static void CameraRectOrderChecks()
    {
        Canvas.Instances.Clear();Canvas.ForceAction=null;
        var overlay=Overlay("loading",5);
        var cameraRect=new RectTransform {name="LoadingCamera",WorldParentOffset=new Vector3(1920,1080,-935.307434f)};
        cameraRect.localRotation=new Quaternion(0,0,0,1);cameraRect.localScale=new Vector3(1,1,1);
        var original=new Vector3(.000183105469f,.0000305175781f,-935.307434f);
        cameraRect.anchoredPosition3D=original;
        overlay.Rects=new[]{overlay.Rects[0],cameraRect};
        var camera=new Camera {transform=cameraRect};
        Vector3 originalWorld=cameraRect.position;
        cameraRect.position=originalWorld;
        Assert(cameraRect.anchoredPosition3D.y==0 && original.y!=0,"Restoring a rounded 1080-unit world Y cancels the recorded tiny local offset");
        cameraRect.anchoredPosition3D=original;
        var scope=PanelScope(camera,1,2,false);
        using(var restore=new CameraState(camera))
        using(scope)
        {
            camera.transform.position=new Vector3(0,0,0);
            scope.ApplyEye();
        }
        Assert(scope.RestorationConflicts==0 && cameraRect.anchoredPosition3D.y==0,"Outer CameraState changes a descendant camera's local offset after the UI scope restored it exactly");
        scope.RebuildRestoredGeometry();
        Assert(scope.RestorationConflicts==0 && cameraRect.anchoredPosition3D.y==original.y,"Selected camera rectangle is restored canonically after outer camera restoration, without accepting a changed local value");
        Assert(cameraRect.position.y==originalWorld.y,"Canonical local restoration preserves the original rounded camera world position");
        Assert(scope.SelectedCameraRectRestorations==1 && scope.DerivedPositionRoundoff==0,"Shared camera/UI ownership repair is recorded separately from any tolerance allowance");
        var changed=PanelScope(camera,1,2,false);
        using(var restore=new CameraState(camera)) using(changed) {camera.transform.position=new Vector3(0,0,0);changed.ApplyEye();}
        Canvas.ForceAction=delegate {cameraRect.anchoredPosition3D=new Vector3(original.x,0,original.z);};
        changed.RebuildRestoredGeometry();
        Assert(changed.RestorationConflicts==2 && changed.DiagnosticJson().Contains("\"verificationPhase\":\"after-camera-restore\""),"A final canvas rebuild changing the canonical near-zero position still fails strict restoration and identifies its phase");
        Canvas.Instances.Clear();Canvas.ForceAction=null;
    }
    static void InactiveCanvasChecks()
    {
        Canvas.Instances.Clear();Canvas.ForceAction=null;Resources.Extras.Clear();LayerMask.Name=null;
        var root=Overlay("pause",5);var child=Overlay("Blocker",7);
        child.renderMode=RenderMode.WorldSpace;child.isActiveAndEnabled=false;child.worldCamera=new Camera();
        var originalChildCamera=child.worldCamera;var originalChildPosition=child.Rects[0].anchoredPosition3D;
        root.NestedCanvases=new[]{root,child};root.Rects=new[]{root.Rects[0],child.Rects[0]};root.Renderers=new[]{root.Renderers[0],child.Renderers[0]};
        var camera=new Camera();var ui=new Camera();
        var scope=PanelScope(camera,1,2,false);scope.UseAfterEffectsCamera(ui);
        using(scope)
        {
            scope.ApplyEye();scope.ValidateInactiveCanvases("after game render");scope.ValidateInactiveCanvases("after UI render");
            Assert(scope.SelectedCount==1 && child.renderMode==RenderMode.WorldSpace && child.worldCamera==originalChildCamera && !child.isActiveAndEnabled,"Inactive independent canvas is protected without conversion, reassignment or activation");
            Assert(child.gameObject.layer==31 && child.Renderers[0].gameObject.layer==31,"Inactive descendant objects share the selected ancestor's temporary layer ownership");
            scope.ApplyEye(1);scope.RecordAfterRender(1);
        }
        scope.RebuildRestoredGeometry();scope.ValidateInactiveCanvases("after desktop restoration");
        Assert(scope.RestorationConflicts==0 && child.gameObject.layer==7 && child.Renderers[0].gameObject.layer==7 && child.Rects[0].anchoredPosition3D.Equals(originalChildPosition),"Protected descendant layers and rectangle state restore exactly");
        Assert(scope.InactiveCanvasValidationPasses==4 && scope.DiagnosticJson().Contains("\"protectedInactiveCanvasCount\":1") && scope.DiagnosticJson().Contains("\"settingsRetained\":true"),"Protected inactive boundaries and validation passes are recorded once");
        foreach(string stage in new[]{"before UI borrowing","after game render","after UI render","after desktop restoration"})
        {
            child.isActiveAndEnabled=false;var activated=PanelScope(camera,1,2,false);activated.UseAfterEffectsCamera(ui);
            bool submitted=false,rejected=false;
            try
            {
                using(activated)
                {
                    if(stage=="before UI borrowing") child.isActiveAndEnabled=true;
                    activated.ApplyEye();
                    if(stage!="before UI borrowing" && stage!="after desktop restoration") child.isActiveAndEnabled=true;
                    if(stage!="after desktop restoration") activated.ValidateInactiveCanvases(stage);
                }
                activated.RebuildRestoredGeometry();
                if(stage=="after desktop restoration") child.isActiveAndEnabled=true;
                activated.ValidateInactiveCanvases(stage);submitted=true;
            }
            catch(InvalidOperationException) {rejected=true;}
            Assert(rejected && !submitted && activated.RestorationConflicts==1 && activated.RestorationError.Contains(stage),"Activation at "+stage+" rejects submission with a bounded diagnostic");
            Assert(root.renderMode==RenderMode.ScreenSpaceOverlay && child.renderMode==RenderMode.WorldSpace && child.isActiveAndEnabled && child.gameObject.layer==7,"Activation rejection restores owned state while preserving the game's newer activity");
        }
        child.isActiveAndEnabled=false;
        var settings=PanelScope(camera,1,2,false);
        bool settingsRejected=false;
        try {using(settings) {settings.ApplyEye();child.sortingOrder=11;settings.ValidateInactiveCanvases("after UI render");}} catch(InvalidOperationException) {settingsRejected=true;}
        Assert(settingsRejected && child.sortingOrder==11 && settings.RestorationConflicts==1,"Protected settings change rejects submission without overwriting a game-owned change");
        child.sortingOrder=10;
        child.WorldRenderers=new[]{new Renderer()};
        bool activeRendererRejected=false;
        try {PanelScope(camera,1,2,false);} catch(InvalidOperationException) {activeRendererRejected=true;}
        Assert(activeRendererRejected && root.renderMode==RenderMode.ScreenSpaceOverlay,"Disabled canvas with an active world renderer is rejected before borrowing");
        child.WorldRenderers[0].enabled=false;var rendererRace=PanelScope(camera,1,2,false);
        bool rendererRaceRejected=false;
        try {using(rendererRace) {rendererRace.ApplyEye();child.WorldRenderers[0].enabled=true;rendererRace.ValidateInactiveCanvases("after game render");}} catch(InvalidOperationException) {rendererRaceRejected=true;}
        Assert(rendererRaceRejected && child.WorldRenderers[0].enabled && root.renderMode==RenderMode.ScreenSpaceOverlay,"World renderer activation is rejected while preserving its newer enabled state");
        child.WorldRenderers=new Renderer[0];child.renderMode=RenderMode.ScreenSpaceCamera;child.isActiveAndEnabled=true;
        bool activeCameraRejected=false;
        try {PanelScope(camera,1,2,false);} catch(InvalidOperationException) {activeCameraRejected=true;}
        Assert(activeCameraRejected && child.renderMode==RenderMode.ScreenSpaceCamera,"Initially active camera-space nested canvas still requires an explicit adapter");
        child.isActiveAndEnabled=false;var resized=PanelScope(camera,1,2,false);
        bool lateQueryRejected=false;
        try {using(resized) {resized.ApplyEye();child.WorldRenderers=new Renderer[8193];resized.ValidateInactiveCanvases("after UI render");}} catch(InvalidOperationException) {lateQueryRejected=true;}
        Assert(lateQueryRejected && resized.RestorationConflicts==1 && root.renderMode==RenderMode.ScreenSpaceOverlay,"A renderer query growing beyond the bound after render rejects submission and records the validation failure");
        bool queryRejected=false;
        try {PanelScope(camera,1,2,false);} catch(InvalidOperationException) {queryRejected=true;}
        Assert(queryRejected && root.renderMode==RenderMode.ScreenSpaceOverlay,"Oversized inactive renderer inventory fails before mutation");
        root.NestedCanvases=new Canvas[66];root.NestedCanvases[0]=root;
        for(int i=1;i<66;++i) root.NestedCanvases[i]=new Canvas {name="inactive "+i,renderMode=RenderMode.WorldSpace,isActiveAndEnabled=false};
        bool boundaryLimit=false;
        try {PanelScope(camera,1,2,false);} catch(InvalidOperationException error) {boundaryLimit=error.Message.Contains("64 protected inactive canvas");}
        Assert(boundaryLimit && root.renderMode==RenderMode.ScreenSpaceOverlay,"More than 64 protected inactive boundaries fail before mutation");
        Canvas.Instances.Clear();Canvas.ForceAction=null;Resources.Extras.Clear();LayerMask.Name=null;
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
