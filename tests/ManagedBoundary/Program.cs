using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
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
        MathChecks(); RestoreChecks(); SessionChecks();
        Console.WriteLine("Managed boundary checks passed: "+checks);
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
        Assert(camera.transform.rotation.w==1,"Rotation restored"); Assert(Object.ReferenceEquals(camera.targetTexture,target),"Target restored");
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
            ExternalProducer(directory,new string('x',4097));
            Assert(consumer.Poll(now.AddSeconds(7))==null && consumer.LastError.Contains("4096"),"Oversized commands rejected");
        }
        finally { Directory.Delete(directory,true); }
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
}
