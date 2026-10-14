using System;
using System.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Threading;
using Ugevr;
using Valve.VR;

static class RewiredPairSourceChecks
{
    sealed class Runtime:IDisposable
    {
        IVRSystem table;IntPtr pointer;public CVRSystem System;
        public readonly List<string> Calls=new List<string>();
        readonly Dictionary<string,int> texts=new Dictionary<string,int>();
        public int States;
        public bool IdentityChange,DuplicateIdentity,RightRoleChanged,InitialClassMismatch,FinalContextChange,LeftUnavailable,FalseLeft,DisconnectedLeft,InvalidRight,NonfinitePose,NonfiniteAxis,UnknownMetadata,MissingText,MissingState,MissingMetadata,ZeroRequired,OversizedRequired;
        public Runtime()
        {
            table=new IVRSystem();
            table.GetTrackedDeviceIndexForControllerRole=delegate(ETrackedControllerRole role){Calls.Add("index:"+role);return role==ETrackedControllerRole.LeftHand?(LeftUnavailable?OpenVR.k_unTrackedDeviceIndexInvalid:FinalContextChange && States>=2?6u:3u):4u;};
            table.GetTrackedDeviceClass=delegate(uint index){Calls.Add("class:"+index);return index==3 && (InitialClassMismatch || FinalContextChange && States>=2)?ETrackedDeviceClass.GenericTracker:ETrackedDeviceClass.Controller;};
            table.GetControllerRoleForTrackedDeviceIndex=delegate(uint index){Calls.Add("role:"+index);return index==3 || RightRoleChanged && States>=2?ETrackedControllerRole.LeftHand:ETrackedControllerRole.RightHand;};
            table.GetStringTrackedDeviceProperty=delegate(uint index,ETrackedDeviceProperty property,StringBuilder value,uint capacity,ref ETrackedPropertyError error)
            {
                if(capacity!=257 || value.Capacity!=257)throw new Exception("Property ABI buffer.");
                string key=index+":"+property;Calls.Add("text:"+key);int count;texts.TryGetValue(key,out count);texts[key]=++count;
                error=ETrackedPropertyError.TrackedProp_Success;value.Append(DuplicateIdentity && property==ETrackedDeviceProperty.Prop_SerialNumber_String?"invented-private-shared-serial":(property==ETrackedDeviceProperty.Prop_SerialNumber_String?"invented-private-serial-":"fixture-model-")+index+(IdentityChange && count>1?"-changed":""));return ZeroRequired?0u:OversizedRequired?258u:(uint)value.Length+1;
            };
            table.GetControllerStateWithPose=delegate(ETrackingUniverseOrigin origin,uint index,ref VRControllerState_t state,uint size,ref TrackedDevicePose_t pose)
            {
                Calls.Add("state:"+index);++States;if(origin!=ETrackingUniverseOrigin.TrackingUniverseStanding || size!=(uint)Marshal.SizeOf(typeof(VRControllerState_t)) || index!=3 && index!=4)throw new Exception("Joint ABI.");
                state=new VRControllerState_t {unPacketNum=index==3?11u:22u,ulButtonPressed=UInt64.MaxValue,ulButtonTouched=1UL<<63,rAxis0=new VRControllerAxis_t {x=index==3?.35f:-.4f,y=.6f},rAxis1=new VRControllerAxis_t {x=.7f,y=.1f},rAxis2=new VRControllerAxis_t {x=.2f,y=.3f},rAxis3=new VRControllerAxis_t {x=-.4f,y=.5f},rAxis4=new VRControllerAxis_t {x=.8f,y=NonfiniteAxis?Single.PositiveInfinity:-.9f}};
                pose=new TrackedDevicePose_t {bDeviceIsConnected=!(DisconnectedLeft && index==3),bPoseIsValid=!(InvalidRight && index==4),mDeviceToAbsoluteTracking=new HmdMatrix34_t {m0=1,m5=1,m10=1,m3=NonfinitePose?Single.NaN:index,m7=1.23f,m11=-.41f}};
                return !(FalseLeft && index==3);
            };
            table.GetInt32TrackedDeviceProperty=delegate(uint index,ETrackedDeviceProperty property,ref ETrackedPropertyError error){Calls.Add("axis:"+index+":"+property);error=UnknownMetadata?ETrackedPropertyError.TrackedProp_UnknownProperty:ETrackedPropertyError.TrackedProp_Success;int axis=(int)property-(int)ETrackedDeviceProperty.Prop_Axis0Type_Int32;return axis==0?2:index==4 && (axis==1 || axis==2)?3:0;};
            Bind();
        }
        public void Bind() {if(pointer!=IntPtr.Zero){Marshal.DestroyStructure(pointer,typeof(IVRSystem));Marshal.FreeHGlobal(pointer);}var current=table;if(MissingText)current.GetStringTrackedDeviceProperty=null;if(MissingState)current.GetControllerStateWithPose=null;if(MissingMetadata)current.GetInt32TrackedDeviceProperty=null;pointer=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(IVRSystem)));Marshal.StructureToPtr(current,pointer,false);System=new CVRSystem(pointer);}
        public void Dispose(){GC.KeepAlive(table);Marshal.DestroyStructure(pointer,typeof(IVRSystem));Marshal.FreeHGlobal(pointer);}
    }
    static readonly DateTime Utc=new DateTime(2026,10,14,12,0,0,DateTimeKind.Utc);
    static string Evidence(string root,string file){return Path.Combine(root,"UnityVR-evidence",file);}
    static void Request(string root,string run,string id=null){Directory.CreateDirectory(Path.Combine(root,"UnityVR-evidence"));File.WriteAllText(Evidence(root,"rewired-pair-source-request"),"run_id="+run+"\nrequest_id="+(id??Guid.NewGuid().ToString("D"))+"\n");}
    static void Cleanup(string root)
    {string resolved=Path.GetFullPath(root),temporary=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);Guid id;string name=Path.GetFileName(resolved);if(Path.GetDirectoryName(resolved).TrimEnd(Path.DirectorySeparatorChar)!=temporary || !name.StartsWith("ugevr-pair-source-",StringComparison.Ordinal) || !Guid.TryParseExact(name.Substring("ugevr-pair-source-".Length),"N",out id))throw new IOException("Unexpected fixture cleanup path");Directory.Delete(resolved,true);}
    static ETrackedPropertyError LongestError()
    {ETrackedPropertyError result=ETrackedPropertyError.TrackedProp_Success;foreach(ETrackedPropertyError error in Enum.GetValues(typeof(ETrackedPropertyError)))if(error.ToString().Length>result.ToString().Length)result=error;return result;}
    static StringPropertyObservation MaximumProperty()
    {return new StringPropertyObservation {Stage=new string('\u0001',64),RawValue=new string('\u0001',256),ExceptionType=new string('\u0001',128),Returned=true,Error=LongestError(),RequiredBytes=UInt32.MaxValue,Length=Int32.MaxValue,InitialCapacity=Int32.MaxValue,ReturnedCapacity=Int32.MaxValue};}
    static PairSourceContext MaximumContext()
    {return new PairSourceContext {RoleIndex=UInt32.MaxValue,QueriedIndex=UInt32.MaxValue,ClassAtQueriedIndex=(ETrackedDeviceClass)Int32.MinValue,RoleAtQueriedIndex=(ETrackedControllerRole)Int32.MinValue,IndexException=new string('\u0001',128),ClassException=new string('\u0001',128),RoleException=new string('\u0001',128)};}
    public static RewiredPairSourceSnapshot MaximumSnapshot()
    {
        Func<PairSourceHand> hand=()=> {var value=new PairSourceHand {Initial=MaximumContext(),Post=MaximumContext(),Final=MaximumContext(),PreSerial=MaximumProperty(),PreModel=MaximumProperty(),PostSerial=MaximumProperty(),PostModel=MaximumProperty(),StateResult=true,Connected=true,PoseValid=true,MatrixFinite=true,AxesFinite=true,StateException=new string('\u0001',128),Packet=UInt32.MaxValue,Pressed=UInt64.MaxValue,Touched=UInt64.MaxValue,Axes=new double[10],Matrix=new double[12]};for(int i=0;i<12;++i)value.Matrix[i]=Double.MinValue;for(int i=0;i<10;++i)value.Axes[i]=Double.MinValue;for(int i=0;i<5;++i){value.AxisRawReturns[i]=value.AxisTypes[i]=Int32.MinValue;value.AxisErrors[i]=LongestError();value.AxisExceptions[i]=new string('\u0001',128);}return value;};
        return new RewiredPairSourceSnapshot {Verdict=new string('\u0001',64),Left=hand(),Right=hand()};
    }
    public static void Run(Action<bool,string> check)
    {
        int budget=new UTF8Encoding(false,true).GetByteCount(MaximumSnapshot().ToPrivateYaml());
        Console.WriteLine("Conservative escaped paired payload bytes: "+budget);
        check(budget>16384 && budget<65536-1024,"Measured maximum bounded escaped pair payload needs a separate record cap while leaving room for its fixed envelope");
        using(var runtime=new Runtime())
        {
            var pair=RewiredPairSourceReader.Read(runtime.System);string yaml=pair.ToPrivateYaml();
            check(pair.Verdict=="observed" && pair.PairContextConsistent==true && pair.PairIdentityConsistent==true && runtime.States==2,"Actual vendored SDK pair records both controllers without creating an input backend");
            check(pair.Left.Initial.RoleIndex==3 && pair.Right.Initial.RoleIndex==4 && pair.Left.Post.RoleIndex==3 && pair.Right.Final.RoleIndex==4 && pair.Left.Packet==11 && pair.Right.Packet==22,"Independent hand contexts and state packets retain their actual queried indices");
            check(!Object.ReferenceEquals(pair.Left.PreSerial,pair.Left.PostSerial) && !Object.ReferenceEquals(pair.Left.PreModel,pair.Left.PostModel) && !Object.ReferenceEquals(pair.Left.PostSerial,pair.Right.PostSerial),"Before and after property observations never alias another phase or hand");
            check(runtime.Calls.IndexOf("text:3:Prop_SerialNumber_String")<runtime.Calls.IndexOf("state:3") && runtime.Calls.LastIndexOf("text:3:Prop_SerialNumber_String")>runtime.Calls.IndexOf("state:3") && runtime.Calls.IndexOf("text:4:Prop_ModelNumber_String")<runtime.Calls.IndexOf("state:4"),"Both identity brackets execute fresh public reads on opposite sides of each hand's joint poll");
            check(pair.Left.Axes.Length==10 && pair.Right.Matrix.Length==12 && pair.Right.AxisTypes[1]==3 && pair.Right.AxisTypes[2]==3 && pair.Left.AxisTypes[1]==0,"Passive pair metadata retains differing hand signatures and both Trigger declarations without selecting semantics");
            check(yaml.Contains("pressed: 18446744073709551615") && yaml.Contains("touched: 9223372036854775808") && yaml.Contains("authorizes_input: false") && yaml.Contains("invented-private-serial-3"),"Private paired records preserve full unsigned bitfields and label their identities/input scope");
        }
        foreach(string fault in new[]{"identity","duplicate-identity","role-changed","class-mismatch","final-context","left-unavailable","false-left","disconnected","pose-invalid","pose-nonfinite","axis-nonfinite","metadata-error","text-missing","state-missing","metadata-missing","zero-required","oversized-required"})
        using(var runtime=new Runtime())
        {
            runtime.IdentityChange=fault=="identity";runtime.FinalContextChange=fault=="final-context";runtime.LeftUnavailable=fault=="left-unavailable";runtime.FalseLeft=fault=="false-left";runtime.DisconnectedLeft=fault=="disconnected";runtime.InvalidRight=fault=="pose-invalid";runtime.NonfinitePose=fault=="pose-nonfinite";runtime.NonfiniteAxis=fault=="axis-nonfinite";runtime.UnknownMetadata=fault=="metadata-error";runtime.MissingText=fault=="text-missing";runtime.MissingState=fault=="state-missing";runtime.MissingMetadata=fault=="metadata-missing";runtime.ZeroRequired=fault=="zero-required";runtime.OversizedRequired=fault=="oversized-required";runtime.Bind();
            runtime.DuplicateIdentity=fault=="duplicate-identity";runtime.RightRoleChanged=fault=="role-changed";runtime.InitialClassMismatch=fault=="class-mismatch";
            var pair=RewiredPairSourceReader.Read(runtime.System);string yaml=pair.ToPrivateYaml();
            check(pair.Verdict!="observed" && yaml.Contains("authorizes_input: false"),"Passive fault remains an explicit observed reason without input admission: "+fault);
            if(fault=="identity")check(pair.Left.PreSerial.RawValue!=pair.Left.PostSerial.RawValue && pair.PairIdentityConsistent==false,"Fresh differing initial/post identities are preserved rather than overwritten");
            if(fault=="duplicate-identity")check(pair.PairIdentityConsistent==false && pair.Left.PreSerial.RawValue==pair.Right.PostSerial.RawValue,"Duplicate observed identities remain explicit disagreement, without inventing a left device identity");
            if(fault=="role-changed" || fault=="class-mismatch")check(pair.PairContextConsistent==false,"Reported controller class/role disagreement remains independent of successful raw state polling: "+fault);
            if(fault=="final-context")check(pair.Left.Post.RoleIndex==3 && pair.Left.Final.RoleIndex==6 && pair.Left.Final.QueriedIndex==3 && pair.PairContextConsistent==false,"Complete pair recheck detects a left-context change occurring during the right read");
            if(fault=="left-unavailable")check(pair.Left.Axes==null && pair.Left.PreSerial==null && pair.Right.Axes!=null && runtime.States==1,"An unavailable hand does not query invalid indices or erase the independently observed other hand");
            if(fault=="false-left")check(pair.Left.StateResult==false && pair.Left.Pressed==UInt64.MaxValue && yaml.Contains("raw_outputs_valid: false"),"SDK false retains raw returned fields and never invents valid neutral input");
            if(fault=="pose-nonfinite" || fault=="axis-nonfinite")check(yaml.Contains(fault=="pose-nonfinite"?"\"NaN\"":"\"Infinity\""),"Nonfinite raw values remain explicitly quoted private evidence: "+fault);
            if(fault=="metadata-error")check(pair.Left.AxisRawReturns[0]==2 && pair.Left.AxisTypes[0]==null && pair.Left.AxisErrors[0]==ETrackedPropertyError.TrackedProp_UnknownProperty,"Property failure retains raw integer and error separately from accepted type");
            if(fault=="metadata-missing")check(pair.Left.AxisErrors[0]==null && pair.Left.AxisRawReturns[0]==null && pair.Left.AxisExceptions[0]!=null,"Thrown metadata query leaves the API error result unknown instead of reporting default Success");
            if(fault=="state-missing")check(pair.Left.StateResult==null && pair.Left.Matrix==null && pair.Left.StateException!=null,"Missing joint API leaves unavailable fields null with a bounded exception category");
            if(fault=="text-missing")check(!pair.Left.PreSerial.Returned && pair.PairIdentityConsistent==null && yaml.Contains("error_code: null"),"Missing text API cannot turn its default error storage into a successful reported result");
            if(fault=="zero-required" || fault=="oversized-required")check(!pair.Left.PreSerial.Accepted && pair.PairIdentityConsistent==null && pair.Left.PreSerial.RawValue!=null,"Bounded string API result disagreement remains raw/private and unavailable: "+fault);
        }
        check(RewiredPairSourceReader.Read(null).Verdict=="runtime_unavailable","Pair reader reports unavailable runtime without creating another SDK client");
        WindowChecks(check);
    }
    static void WindowChecks(Action<bool,string> check)
    {
        string root=Path.Combine(Path.GetTempPath(),"ugevr-pair-source-"+Guid.NewGuid().ToString("N")),run=Guid.NewGuid().ToString("D");Directory.CreateDirectory(root);
        try
        {
            using(var runtime=new Runtime())
            {
                Rewired.ReInput.Reset();var gated=new RewiredPairSourceObservation(root,run,17,1000,()=>RewiredPairSourceReader.Read(runtime.System));Request(root,run);
                gated.Tick(false,true,true,1,Utc,0);gated.Tick(true,false,true,1,Utc,100);gated.Tick(true,true,false,1,Utc,200);
                check(runtime.Calls.Count==0 && runtime.States==0,"Actual vendored SDK delegates receive zero calls when paired observation is off, disabled or unfocused");
                gated.Tick(true,true,true,1,Utc,300);gated.Cancel("end",Utc,350);
                check(runtime.Calls.Count==38 && runtime.States==2 && Rewired.ReInput.controllers.CustomControllers.Count==0 && Rewired.ReInput.Data.Definitions.Count==0 && Rewired.ReInput.Subscribers==0,"One explicit passive pair sample uses the same SDK table for38 bounded queries and creates no game input resources");
            }
        }
        finally{Cleanup(root);}
        root=Path.Combine(Path.GetTempPath(),"ugevr-pair-source-"+Guid.NewGuid().ToString("N"));run=Guid.NewGuid().ToString("D");Directory.CreateDirectory(root);
        try
        {
            int reads=0;var observer=new RewiredPairSourceObservation(root,run,17,1000,()=>{++reads;return new RewiredPairSourceSnapshot();});Request(root,run);
            observer.Tick(false,true,true,1,Utc,0);observer.Tick(true,false,true,1,Utc,100);observer.Tick(true,true,false,1,Utc,200);
            var thread=new Thread(()=>observer.Tick(true,true,true,1,Utc,250));thread.Start();thread.Join();
            check(reads==0 && observer.Windows==0 && observer.LastError=="owner_thread_required" && File.Exists(Evidence(root,"rewired-pair-source-request")),"Default-off, pause, focus loss and other-thread paths perform no read or request claim");
            observer.Tick(true,true,true,1,Utc,300);for(int tick=301;tick<350;++tick)observer.Tick(true,true,true,1,Utc,tick);
            check(reads==1 && observer.Samples==1 && observer.Active,"Fresh paired request permits one read per50ms without catch-up");
            observer.Tick(true,false,true,1,Utc,1000);observer.Tick(true,true,false,1,Utc,15300);
            string file=Evidence(root,"rewired-pair-source-window-1.yaml"),body=File.ReadAllText(file);
            check(!observer.Active && reads==1 && body.Contains("schema_version: 2") && body.Contains("observer_kind: paired") && body.Contains("authorizes_input: false") && observer.Reason=="elapsed_deadline","Paused paired window preserves the elapsed deadline and publishes complete private schema2 output");
            for(int window=2;window<=3;++window){Request(root,run);long start=window*20000;observer.Tick(true,true,true,1,Utc,start);for(long tick=start+50;tick<start+15000;tick+=50)observer.Tick(true,true,true,1,Utc,tick);observer.Tick(true,true,true,1,Utc,start+15000);check(observer.Samples==300 && new FileInfo(Evidence(root,"rewired-pair-source-window-"+window+".yaml")).Length<RewiredPairSourceObservation.MaximumWindowBytes,"Nominal paired window caps at300 complete records and bounded file bytes: "+window);}
            observer=new RewiredPairSourceObservation(root,run,17,1000,()=>{++reads;return new RewiredPairSourceSnapshot();});Request(root,run);int prior=reads;observer.Tick(true,true,true,1,Utc,80000);check(observer.Windows==3 && reads==prior,"Paired claims enforce three windows across component recreation");
            check(!File.Exists(Evidence(root,"rewired-source-window-1.yaml")),"Paired evidence cannot overwrite or consume the independent existing right-only stream namespace");
        }
        finally{Cleanup(root);}
        if(OperatingSystem.IsWindows())
        {
            root=Path.Combine(Path.GetTempPath(),"ugevr-pair-source-"+Guid.NewGuid().ToString("N"));string target=Path.Combine(root,"target"),link=Path.Combine(root,"link");Directory.CreateDirectory(target);
            try
            {
                var command=new System.Diagnostics.ProcessStartInfo("cmd.exe") {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};command.ArgumentList.Add("/c");command.ArgumentList.Add("mklink");command.ArgumentList.Add("/J");command.ArgumentList.Add(link);command.ArgumentList.Add(target);
                using(var process=System.Diagnostics.Process.Start(command)){process.StandardOutput.ReadToEnd();process.StandardError.ReadToEnd();process.WaitForExit();check(process.ExitCode==0,"Owned fixture creates a contained junction for the paired observer path check");}
                int reads=0;bool refused=false;try {new RewiredPairSourceObservation(link,run,17,1000,()=>{++reads;return new RewiredPairSourceSnapshot();});}catch(IOException){refused=true;}
                check(refused && reads==0,"Paired observer rejects a real reparse ancestor before request or SDK work");
            }
            finally{if(Directory.Exists(link))Directory.Delete(link);Cleanup(root);}
        }
        foreach(string fault in new[]{"stale","wrong-run","bad-request","replay","serialization","unicode","collision","regression","opt-out","window-bytes"})
        {
            root=Path.Combine(Path.GetTempPath(),"ugevr-pair-source-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);run=Guid.NewGuid().ToString("D");string nonce=Guid.NewGuid().ToString("D");int reads=0;
            try
            {
                if(fault=="stale")Request(root,run,nonce);
                var observer=new RewiredPairSourceObservation(root,run,17,1000,()=>{++reads;return fault=="window-bytes"?MaximumSnapshot():new RewiredPairSourceSnapshot {Verdict=fault=="serialization"?new string('x',65):fault=="unicode"?"\ud800":"runtime_unavailable"};});
                if(fault!="stale")Request(root,fault=="wrong-run"?Guid.NewGuid().ToString("D"):run,nonce);if(fault=="bad-request")File.AppendAllText(Evidence(root,"rewired-pair-source-request"),"unexpected=value\n");
                observer.Tick(true,true,true,1,Utc,1000);
                if(fault=="stale" || fault=="wrong-run" || fault=="bad-request")check(reads==0 && !observer.Active,"Stale, foreign or malformed paired request never invokes SDK reader: "+fault);
                if(fault=="serialization" || fault=="unicode"){string body=File.ReadAllText(Evidence(root,"rewired-pair-source-window-1.yaml"));check(observer.Samples==0 && !body.Contains("sample_number:") && body.Contains("samples: 0"),"Failed paired serialization produces no malformed partial record: "+fault);}
                if(fault=="collision"){File.WriteAllText(Evidence(root,"rewired-pair-source-window-1.yaml"),"sentinel");observer.Cancel("end",Utc,1100);check(observer.Status=="output_unconfirmed" && File.ReadAllText(Evidence(root,"rewired-pair-source-window-1.yaml"))=="sentinel","Paired result publication never overwrites an existing file");}
                if(fault=="regression"){observer.Tick(true,true,true,1,Utc,999);check(!observer.Active && reads==1 && observer.Reason=="clock_regressed","Regressing paired window clock terminates without another read");}
                if(fault=="opt-out"){observer.Tick(false,true,true,1,Utc,1100);check(!observer.Active && reads==1 && observer.Reason=="opt_in_removed","Paired opt-out ends capture without any additional SDK query");}
                if(fault=="replay"){observer.Cancel("end",Utc,1100);observer=new RewiredPairSourceObservation(root,run,17,1000,()=>{++reads;return new RewiredPairSourceSnapshot();});Request(root,run,nonce);observer.Tick(true,true,true,1,Utc,2000);check(reads==1 && observer.Reason=="request_id_reused","Paired request nonce cannot replay after recreation");}
                if(fault=="window-bytes"){for(long tick=1050;observer.Active && tick<16000;tick+=50)observer.Tick(true,true,true,1,Utc,tick);check(!observer.Active && observer.Samples<300 && observer.Reason=="record_bound_exceeded" && new FileInfo(Evidence(root,"rewired-pair-source-window-1.yaml")).Length<=RewiredPairSourceObservation.MaximumWindowBytes,"Large but valid paired records end honestly at the unchanged5MiB window cap rather than truncating samples");}
                observer.Cancel("cleanup",Utc,20000);check(Directory.GetFiles(Path.Combine(root,"UnityVR-evidence"),"*.tmp").Length==0,"Paired fault publication leaves no partial temporary file: "+fault);
            }
            finally{Cleanup(root);}
        }
    }
}
