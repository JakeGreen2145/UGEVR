using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Ugevr;
using Valve.VR;

static class RewiredSourceObservationChecks
{
    sealed class Runtime : IDisposable
    {
        IVRSystem table;
        IntPtr pointer;
        public CVRSystem System;
        public int States,Properties;
        public bool StateFalse,MissingState,MissingProperty,ContextChange,UnknownMetadata,InvalidPose,NonfinitePose;
        public int[] Kinds={2,3,3,0,0};
        public Runtime()
        {
            table=new IVRSystem();
            table.GetTrackedDeviceIndexForControllerRole=delegate(ETrackedControllerRole role){return ContextChange && States>0?4u:3u;};
            table.GetTrackedDeviceClass=delegate(uint index){return ContextChange && States>0?ETrackedDeviceClass.GenericTracker:ETrackedDeviceClass.Controller;};
            table.GetControllerRoleForTrackedDeviceIndex=delegate(uint index){return ContextChange && States>0?ETrackedControllerRole.LeftHand:ETrackedControllerRole.RightHand;};
            table.GetInt32TrackedDeviceProperty=delegate(uint index,ETrackedDeviceProperty property,ref ETrackedPropertyError error)
            {++Properties;error=UnknownMetadata?ETrackedPropertyError.TrackedProp_UnknownProperty:ETrackedPropertyError.TrackedProp_Success;return Kinds[(int)property-(int)ETrackedDeviceProperty.Prop_Axis0Type_Int32];};
            table.GetStringTrackedDeviceProperty=delegate(uint index,ETrackedDeviceProperty property,StringBuilder value,uint capacity,ref ETrackedPropertyError error)
            {if(capacity!=257 || value.Capacity!=257) throw new Exception("Bounded property API shape.");error=ETrackedPropertyError.TrackedProp_Success;value.Append(property==ETrackedDeviceProperty.Prop_SerialNumber_String?"invented-private-fixture":"fixture-model");return (uint)value.Length+1;};
            table.GetControllerStateWithPose=delegate(ETrackingUniverseOrigin origin,uint index,ref VRControllerState_t state,uint size,ref TrackedDevicePose_t pose)
            {
                ++States;if(index!=3 || origin!=ETrackingUniverseOrigin.TrackingUniverseStanding || size!=(uint)Marshal.SizeOf(typeof(VRControllerState_t))) throw new Exception("Joint ABI arguments.");
                state=new VRControllerState_t {unPacketNum=19,ulButtonPressed=UInt64.MaxValue,ulButtonTouched=1UL<<63,
                    rAxis0=new VRControllerAxis_t {x=.35f,y=.6f},rAxis1=new VRControllerAxis_t {x=.7f,y=.1f},rAxis2=new VRControllerAxis_t {x=.2f,y=.3f},rAxis3=new VRControllerAxis_t {x=-.4f,y=.5f},rAxis4=new VRControllerAxis_t {x=.8f,y=-.9f}};
                pose=new TrackedDevicePose_t {bDeviceIsConnected=true,bPoseIsValid=!InvalidPose,mDeviceToAbsoluteTracking=new HmdMatrix34_t {m0=1,m5=1,m10=1,m3=NonfinitePose?Single.NaN:.27f,m7=1.23f,m11=-.41f}};
                return !StateFalse;
            };
            Bind();
        }
        public void Bind()
        {
            if(pointer!=IntPtr.Zero) {Marshal.DestroyStructure(pointer,typeof(IVRSystem));Marshal.FreeHGlobal(pointer);}
            var current=table;if(MissingState)current.GetControllerStateWithPose=null;if(MissingProperty)current.GetInt32TrackedDeviceProperty=null;
            pointer=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(IVRSystem)));Marshal.StructureToPtr(current,pointer,false);System=new CVRSystem(pointer);
        }
        public void Dispose() {GC.KeepAlive(table);Marshal.DestroyStructure(pointer,typeof(IVRSystem));Marshal.FreeHGlobal(pointer);}
    }
    static DateTime Utc=new DateTime(2026,10,14,8,0,0,DateTimeKind.Utc);
    static string PathOf(string root,string file) {return Path.Combine(root,"UnityVR-evidence",file);}
    static void Request(string root,string run,string id=null)
    {Directory.CreateDirectory(Path.Combine(root,"UnityVR-evidence"));File.WriteAllText(PathOf(root,"rewired-source-request"),"run_id="+run+"\nrequest_id="+(id??Guid.NewGuid().ToString("D"))+"\n");}
    static ControllerIdentitySnapshot Sample() {return new ControllerIdentitySnapshot {Stage="fixture",Verdict="observed"};}
    public static void Run(Action<bool,string> check)
    {
        using(var runtime=new Runtime())
        {
            var snapshot=RewiredSourceReader.Read(runtime.System);string yaml=snapshot.ToPrivateYaml();
            check(snapshot.Verdict=="observed" && snapshot.RejectedState.AxisTypes[1]==3 && snapshot.RejectedState.AxisTypes[2]==3 && runtime.States==1,"Passive public SDK observation records two Trigger axes without resolving their semantics");
            check(snapshot.Index==3 && snapshot.DeviceClass==ETrackedDeviceClass.Controller && snapshot.Role==ETrackedControllerRole.RightHand && snapshot.PostIndex==3 && snapshot.PostRole==ETrackedControllerRole.RightHand,"Passive record contains both query and post-poll public controller context");
            check(snapshot.PostSerial.Accepted && snapshot.PostModel.Accepted && snapshot.RejectedState.Axes.Length==10 && snapshot.RejectedState.Matrix.Length==12 && runtime.Properties==6,"Passive record preserves bounded private properties, all raw axes, matrix and metadata read outcomes");
            check(yaml.Contains("pressed: 18446744073709551615") && yaml.Contains("touched: 9223372036854775808") && yaml.Contains("raw_outputs_valid: true"),"Serialization preserves the full unsigned button/touch bitfields without signed conversion");
            runtime.StateFalse=true;snapshot=RewiredSourceReader.Read(runtime.System);yaml=snapshot.ToPrivateYaml();
            check(snapshot.Verdict=="joint_state_false" && yaml.Contains("raw_outputs_valid: false") && snapshot.RejectedState.Pressed==UInt64.MaxValue && snapshot.RejectedState.Packet==19,"SDK false retains returned raw outputs while marking them invalid instead of inventing neutral input");
            runtime.StateFalse=false;runtime.NonfinitePose=true;snapshot=RewiredSourceReader.Read(runtime.System);yaml=snapshot.ToPrivateYaml();
            check(snapshot.Verdict=="pose_nonfinite" && yaml.Contains("\"NaN\"") && snapshot.RejectedState.MatrixFinite==false,"Nonfinite pose values remain explicit escaped data and never become malformed YAML numeric values");
            runtime.NonfinitePose=false;runtime.InvalidPose=true;snapshot=RewiredSourceReader.Read(runtime.System);check(snapshot.Verdict=="pose_invalid" && snapshot.RejectedState.PoseValid==false,"Invalid pose is recorded honestly without refusing the private observation itself");
            runtime.InvalidPose=false;runtime.UnknownMetadata=true;snapshot=RewiredSourceReader.Read(runtime.System);
            check(snapshot.RejectedState.AxisTypes[0]==null && snapshot.RejectedState.AxisRawReturns[0]==2 && snapshot.RejectedState.AxisErrors[0]==ETrackedPropertyError.TrackedProp_UnknownProperty,"Metadata errors preserve raw return plus null accepted type rather than converting UnknownProperty to joystick");
            runtime.UnknownMetadata=false;runtime.ContextChange=true;runtime.States=0;snapshot=RewiredSourceReader.Read(runtime.System);
            check(snapshot.Verdict=="context_changed" && snapshot.Index==3 && snapshot.PostIndex==4 && snapshot.PostClass==ETrackedDeviceClass.GenericTracker && snapshot.PostRole==ETrackedControllerRole.LeftHand,"Index/class/role changes within the joint observation remain visible as disagreement");
            runtime.ContextChange=false;runtime.MissingState=true;runtime.Bind();snapshot=RewiredSourceReader.Read(runtime.System);
            check(snapshot.Verdict=="joint_state_exception" && snapshot.RejectedState.StateResult==null && snapshot.RejectedState.Axes==null && snapshot.RejectedState.StateException!=null,"Missing SDK joint delegate produces an exception category with absent outputs instead of a false neutral sample");
            runtime.MissingState=false;runtime.MissingProperty=true;runtime.Bind();snapshot=RewiredSourceReader.Read(runtime.System);
            check(snapshot.RejectedState.AxisExceptions[0]!=null && snapshot.RejectedState.AxisRawReturns[0]==null && snapshot.RejectedState.AxisTypes[0]==null,"Missing property API records failure per bounded axis query");
        }
        check(RewiredSourceReader.Read(null).Verdict=="runtime_unavailable","Unavailable runtime is an explicit passive reason without SDK initialization");
        var escaped=Sample();escaped.Stage="fixture\"\\\n\u0085\u2028\u2029";
        string escapedYaml=escaped.ToPrivateYaml();
        check(escapedYaml.Contains("\\u0085") && escapedYaml.Contains("\\u2028") && escapedYaml.Contains("\\u2029") && escapedYaml.Contains("\\u000a"),"Private SDK strings escape Unicode line separators and controls so they cannot create record structure");
        foreach(string invalid in new[]{"relative", "empty-run", "zero-process", "clock-bound"})
        {
            bool refused=false;try {new RewiredSourceObservation(invalid=="relative"?"relative":Path.GetTempPath(),invalid=="empty-run"?Guid.Empty.ToString("D"):Guid.NewGuid().ToString("D"),invalid=="zero-process"?0:17,invalid=="clock-bound"?Int64.MaxValue:1000,Sample);}catch(ArgumentException){refused=true;}
            check(refused,"Invalid controller ownership or clock bounds refuse observer construction: "+invalid);
        }
        if(OperatingSystem.IsWindows())
        {
            string container=Path.Combine(Path.GetTempPath(),"ugevr-source-path-"+Guid.NewGuid().ToString("N")),target=Path.Combine(container,"target"),link=Path.Combine(container,"link");Directory.CreateDirectory(target);
            try
            {
                // A directory junction needs no administrator privilege. Both names are contained fixture paths.
                var command=new System.Diagnostics.ProcessStartInfo("cmd.exe") {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};command.ArgumentList.Add("/c");command.ArgumentList.Add("mklink");command.ArgumentList.Add("/J");command.ArgumentList.Add(link);command.ArgumentList.Add(target);
                using(var process=System.Diagnostics.Process.Start(command)) {process.StandardOutput.ReadToEnd();process.StandardError.ReadToEnd();process.WaitForExit();check(process.ExitCode==0,"Controlled fixture creates a contained junction for the real filesystem ownership boundary");}
                bool refused=false;try {new RewiredSourceObservation(link,Guid.NewGuid().ToString("D"),17,1000,Sample);}catch(IOException){refused=true;}
                check(refused,"A real reparse ancestor refuses the passive observer before any request or SDK work");
            }
            finally {if(Directory.Exists(link))Directory.Delete(link);Directory.Delete(container,true);}
        }
        string root=Path.Combine(Path.GetTempPath(),"ugevr-rewired-source-"+Guid.NewGuid().ToString("N")),run=Guid.NewGuid().ToString("D");Directory.CreateDirectory(root);
        try
        {
            int reads=0;var observer=new RewiredSourceObservation(root,run,17,1000,delegate {++reads;return Sample();});Request(root,run);
            observer.Tick(false,true,true,1,Utc,0);observer.Tick(false,true,true,1,Utc,1000);
            check(reads==0 && observer.Windows==0 && File.Exists(PathOf(root,"rewired-source-request")),"Default-off leaves requests untouched and never reads the SDK");
            observer.Tick(true,false,true,1,Utc,1100);observer.Tick(true,true,false,1,Utc,1200);
            check(reads==0 && observer.Windows==0,"Disabled or unfocused state cannot claim a window or produce a record");
            var thread=new Thread(()=>observer.Tick(true,true,true,1,Utc,1250));thread.Start();thread.Join();
            check(observer.LastError=="owner_thread_required" && reads==0 && observer.Windows==0,"Other-thread calls perform no request mutation or SDK read");
            observer.Tick(true,true,true,1,Utc,1300);
            check(observer.Active && observer.Windows==1 && observer.Samples==1 && reads==1 && File.Exists(PathOf(root,"rewired-source-request.claimed-1")),"Fresh explicit request claims one window and samples on its owner thread");
            for(int tick=1301;tick<1350;++tick)observer.Tick(true,true,true,1,Utc,tick);
            check(reads==1,"Many Unity Updates within the 50ms interval cannot exceed 20Hz");
            observer.Tick(true,true,true,2,Utc,1350);observer.Tick(true,true,false,2,Utc,2000);observer.Tick(true,false,true,2,Utc,3000);
            check(reads==2 && observer.Samples==2,"Unfocused and disabled ticks create no samples while generation changes remain passive evidence");
            observer.Tick(true,true,false,2,Utc,16300);
            string result=PathOf(root,"rewired-source-window-1.yaml"),body=File.ReadAllText(result);
            check(!observer.Active && observer.Reason=="elapsed_deadline" && reads==2 && body.Contains("runtime_generation: 2") && body.Contains("authorizes_input: false"),"Paused time cannot extend the elapsed deadline; a complete result contains only admitted observations");
            Request(root,run);observer.Tick(true,true,true,2,Utc,17000);
            for(int tick=17050;tick<32000;tick+=50)observer.Tick(true,true,true,2,Utc,tick);
            observer.Tick(true,true,true,2,Utc,32000);
            check(observer.Samples==300 && observer.Windows==2 && !observer.Active && new FileInfo(PathOf(root,"rewired-source-window-2.yaml")).Length<RewiredSourceObservation.MaximumWindowBytes,"A full 15-second window is capped at exactly 300 records and bounded bytes");
            Request(root,run);observer.Tick(true,true,true,2,Utc,33000);observer.Cancel("lifecycle_end",Utc,33100);
            check(observer.Windows==3 && !observer.Active && File.Exists(PathOf(root,"rewired-source-window-3.yaml")),"Lifecycle cancellation publishes a complete bounded stream for its final window");
            observer=new RewiredSourceObservation(root,run,17,1000,delegate {++reads;return Sample();});Request(root,run);int oldReads=reads;observer.Tick(true,true,true,2,Utc,40000);
            check(observer.Windows==3 && reads==oldReads && File.Exists(PathOf(root,"rewired-source-request")),"Retained claim/result identities enforce three windows across component recreation");
            check(Directory.GetFiles(Path.Combine(root,"UnityVR-evidence"),"*.tmp").Length==0,"Successful publication leaves no partial temporary result");
        }
        finally {Directory.Delete(root,true);}
        foreach(string fault in new[]{"stale","replay","wrong-run","oversized-request","duplicate-key","reader-throw","record-bound","invalid-unicode","collision","regressed-clock","opt-out"})
        {
            root=Path.Combine(Path.GetTempPath(),"ugevr-rewired-source-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);run=Guid.NewGuid().ToString("D");int reads=0;string nonce=Guid.NewGuid().ToString("D");
            try
            {
                if(fault=="stale")Request(root,run,nonce);
                var observer=new RewiredSourceObservation(root,run,17,1000,delegate {++reads;if(fault=="reader-throw")throw new InvalidOperationException("fixture");return new ControllerIdentitySnapshot {Verdict="observed",Stage=fault=="record-bound"?new string('x',20000):fault=="invalid-unicode"?"\ud800":"fixture"};});
                if(fault!="stale")Request(root,fault=="wrong-run"?Guid.NewGuid().ToString("D"):run,nonce);
                if(fault=="oversized-request")File.WriteAllText(PathOf(root,"rewired-source-request"),new string('x',257));
                if(fault=="duplicate-key")File.AppendAllText(PathOf(root,"rewired-source-request"),"request_id="+nonce+"\n");
                observer.Tick(true,true,true,1,Utc,1000);
                if(fault=="stale") {check(reads==0 && observer.Windows==0 && observer.Reason=="stale_request_at_startup","A file already present when the component starts cannot authorize a fresh window");Request(root,run);observer.Tick(true,true,true,1,Utc,1300);check(reads==1 && observer.Active,"A new nonce after startup replaces the stale request without restarting the observer");}
                if(fault=="replay") {observer.Cancel("first_end",Utc,1100);observer=new RewiredSourceObservation(root,run,17,1000,delegate {++reads;return Sample();});Request(root,run,nonce);observer.Tick(true,true,true,1,Utc,2000);check(observer.Reason=="request_id_reused" && reads==1 && !observer.Active,"A claimed nonce cannot replay after component recreation");}
                if(fault=="wrong-run" || fault=="oversized-request" || fault=="duplicate-key")check(reads==0 && !observer.Active && observer.Status=="request_rejected","Malformed or foreign request performs no SDK read: "+fault);
                if(fault=="reader-throw" || fault=="record-bound" || fault=="invalid-unicode")
                {
                    string body=File.ReadAllText(PathOf(root,"rewired-source-window-1.yaml"));
                    check(!observer.Active && observer.Samples==0 && body.Contains("samples: 0") && !body.Contains("sample_number:"),"A failed or oversized serialization publishes no partial sample: "+fault);
                }
                if(fault=="collision") {File.WriteAllText(PathOf(root,"rewired-source-window-1.yaml"),"sentinel");observer.Cancel("end",Utc,1100);check(File.ReadAllText(PathOf(root,"rewired-source-window-1.yaml"))=="sentinel" && observer.Status=="output_unconfirmed","Publishing never overwrites an existing result and reports the collision");}
                if(fault=="regressed-clock") {observer.Tick(true,true,true,1,Utc,999);check(!observer.Active && observer.Reason=="clock_regressed" && reads==1,"Clock regression closes the window without another source read");}
                if(fault=="opt-out") {observer.Tick(false,true,true,1,Utc,1100);check(!observer.Active && observer.Reason=="opt_in_removed" && reads==1,"Removing explicit opt-in closes the window without another record");}
                observer.Cancel("cleanup",Utc,2200);
                check(Directory.GetFiles(Path.Combine(root,"UnityVR-evidence"),"*.tmp").Length==0,"Fault path leaves no partial result file: "+fault);
            }
            finally {Directory.Delete(root,true);}
        }
    }
}
