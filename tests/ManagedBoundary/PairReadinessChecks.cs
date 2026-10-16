using System;
using System.IO;
using System.Text;
using System.Threading;
using Ugevr;
using Valve.VR;

static class PairReadinessChecks
{
    static readonly DateTime Utc=new DateTime(2026,10,16,19,0,0,DateTimeKind.Utc);
    static StringPropertyObservation Text(string value)
    {return new StringPropertyObservation {RawValue=value,Returned=true,RequiredBytes=(uint)value.Length+1,Length=value.Length};}
    static PairSourceHand Hand(uint index,ETrackedControllerRole role)
    {
        Func<PairSourceContext> context=()=>new PairSourceContext {RoleIndex=index,QueriedIndex=index,ClassAtQueriedIndex=ETrackedDeviceClass.Controller,RoleAtQueriedIndex=role};
        var h=new PairSourceHand {ExpectedRole=role,Initial=context(),Post=context(),Final=context(),PreSerial=Text("serial-"+index),PostSerial=Text("serial-"+index),PreModel=Text("model-"+index),PostModel=Text("model-"+index),StateResult=true,Connected=true,PoseValid=true,MatrixFinite=true,AxesFinite=true,Pressed=0,Touched=0,Packet=1,Matrix=new double[]{1,0,0,index,0,1,0,1,0,0,1,0},Axes=new double[10]};
        for(int i=0;i<5;++i){h.AxisTypes[i]=h.AxisRawReturns[i]=i==0?2:i<3?3:0;h.AxisErrors[i]=ETrackedPropertyError.TrackedProp_Success;}
        return h;
    }
    static RewiredPairSourceSnapshot Sample()
    {return new RewiredPairSourceSnapshot {Verdict="observed",PairContextConsistent=true,PairIdentityConsistent=true,Left=Hand(3,ETrackedControllerRole.LeftHand),Right=Hand(4,ETrackedControllerRole.RightHand)};}
    sealed class Fixture:IDisposable
    {
        public readonly string Root=Path.Combine(Path.GetTempPath(),"ugevr-pair-ready-"+Guid.NewGuid().ToString("N")),Run=Guid.NewGuid().ToString("D");
        public long Now;public ulong Generation=7;public int Calls;public Func<RewiredPairSourceSnapshot> Read;public Func<long> Clock;
        public RewiredPairSourceReadiness Value;
        public string File(string name){return Path.Combine(Root,"UnityVR-evidence",name);}
        public Fixture(bool stale=false)
        {
            Directory.CreateDirectory(Path.Combine(Root,"UnityVR-evidence"));Read=()=>Sample();if(stale)Request();
            Value=Create();
        }
        public RewiredPairSourceReadiness Create(){return new RewiredPairSourceReadiness(Root,Run,42,1000,delegate {++Calls;return Read();},()=>Clock==null?Now:Clock(),()=>Generation);}
        public void Request(string extra="",string run=null){System.IO.File.WriteAllText(File(RewiredPairSourceReadiness.RequestName),"run_id="+(run??Run)+"\nrequest_id="+Guid.NewGuid().ToString("D")+"\n"+extra);}
        public bool Tick(long now,bool opt=true,bool enabled=true,bool focus=true,bool strict=false){Now=now;return Value.Tick(opt,enabled,focus,Generation,Utc.AddMilliseconds(now),now,strict);}
        public string Output {get {return System.IO.File.ReadAllText(File(RewiredPairSourceReadiness.ResultName));}}
        public void Dispose()
        {
            string p=Path.GetFullPath(Root),temp=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
            if(Path.GetDirectoryName(p).TrimEnd(Path.DirectorySeparatorChar)!=temp || !Path.GetFileName(p).StartsWith("ugevr-pair-ready-",StringComparison.Ordinal))throw new IOException("Fixture cleanup path");
            Directory.Delete(p,true);
        }
    }
    public static void Run(Action<bool,string> check)
    {
        using(var f=new Fixture())
        {
            f.Tick(0);f.Tick(100);check(f.Calls==0 && !f.Value.Attempted,"Readiness opt-in alone never polls the SDK or creates evidence");
            f.Request();f.Tick(200,false);check(f.Calls==0 && !f.Value.Attempted,"Disabled readiness preserves even a pending request without SDK queries");
            f.Tick(250);f.Tick(299);check(f.Calls==1,"Readiness cannot exceed20Hz or catch up reads in one tick");
            f.Tick(300);f.Tick(350);f.Tick(400);
            check(f.Value.Status=="ready" && f.Calls==4 && f.Value.Reads==4,"Exactly one primer and three fresh neutral verifications establish sampled readiness");
            string raw=f.Output;check(raw.Contains("runtime_generation: 7") && raw.Contains("read_role: unqualified_primer") && raw.Contains("verification_attempted: 3") && raw.Contains("strict_windows_consumed: 0") && raw.Contains("qualifies_strict_window: false"),"Raw output binds generation and labels readiness separate from strict/gameplay qualification");
            f.Request();f.Tick(450);f.Value=f.Create();f.Tick(500);check(f.Calls==4 && f.Output==raw,"Replay and component recreation cannot consume another readiness allowance or overwrite evidence");
            var strict=new RewiredPairSourceObservation(f.Root,f.Run,42,1000,()=>Sample());
            check(strict.Windows==0 && !strict.Active,"Readiness consumes none of the existing three strict windows");
        }
        using(var f=new Fixture())
        {
            f.Read=()=> {var s=Sample();if(f.Calls==1){s.Left.PreSerial=Text("old-left");s.Left.PreModel=Text("Deckard MP");s.PairIdentityConsistent=false;s.Verdict="pair_identity_unavailable_or_changed";}return s;};
            f.Request();foreach(long n in new long[]{0,50,100,150})f.Tick(n);
            check(f.Value.Status=="ready" && f.Output.Contains("old-left") && f.Output.Contains("Deckard MP") && f.Output.Contains("pair_identity_unavailable_or_changed"),"Primer's actual pre/post identity disagreement remains raw and explicitly unqualified");
        }
        foreach(string fault in new[]{"serial","model","context","metadata","pressed","touched","axis","pose","property","pair","reuse"})
        using(var f=new Fixture())
        {
            RewiredPairSourceSnapshot prior=null;
            f.Read=()=> {var s=Sample();if(f.Calls==1){prior=s;return s;}if(fault=="reuse")return prior;
                if(fault=="serial")s.Left.PreSerial=s.Left.PostSerial=Text("new-left");if(fault=="model")s.Right.PreModel=s.Right.PostModel=Text("new-model");
                if(fault=="context")s.Left.Final.RoleIndex=9;if(fault=="metadata")s.Left.AxisTypes[0]=s.Left.AxisRawReturns[0]=1;
                if(fault=="pressed")s.Right.Pressed=1;if(fault=="touched")s.Left.Touched=1;if(fault=="axis")s.Right.Axes[3]=.1;
                if(fault=="pose")s.Left.Matrix[0]=Double.NaN;if(fault=="property")s.Right.PreSerial.Returned=false;if(fault=="pair")s.PairIdentityConsistent=false;return s;};
            f.Request();f.Tick(0);f.Tick(50);f.Tick(100);f.Tick(150);
            check(f.Value.Status=="refused" && f.Calls==2 && f.Output.Contains("reads_attempted: 2") && f.Output.Contains("not_attempted_remainder: 2"),"Invalid verification retains both attempted raw snapshots and stops further reads: "+fault);
        }
        foreach(string fault in new[]{"post-property","neutral","state","context","metadata"})
        using(var f=new Fixture())
        {
            f.Read=()=> {var s=Sample();if(fault=="post-property")s.Left.PostSerial.Returned=false;if(fault=="neutral")s.Right.Axes[0]=.1;if(fault=="state")s.Left.StateResult=false;if(fault=="context")s.Left.Post.RoleAtQueriedIndex=ETrackedControllerRole.RightHand;if(fault=="metadata")s.Left.AxisErrors[0]=ETrackedPropertyError.TrackedProp_UnknownProperty;return s;};
            f.Request();f.Tick(0);f.Tick(50);check(f.Calls==1 && f.Value.Reason=="primer_unusable" && f.Output.Contains("not_attempted_remainder: 3"),"Unusable primer never authorizes verification: "+fault);
        }
        foreach(string fault in new[]{"disabled","focus","generation","deadline","regress","cancel","opt-in"})
        using(var f=new Fixture())
        {
            f.Request();f.Tick(100);
            if(fault=="generation")f.Generation=8;
            if(fault=="cancel")f.Value.Cancel("component_disabled",Utc,110);
            else f.Tick(fault=="deadline"?1100:fault=="regress"?99:150,fault!="opt-in",fault!="disabled",fault!="focus");
            f.Tick(1200);check(f.Calls==1 && f.Value.Status=="refused" && f.Output.Contains("reads_attempted: 1"),"Cancellation or lost capture context stops the one-shot immediately: "+fault);
        }
        using(var f=new Fixture())
        {f.Read=()=> {f.Now=1001;return Sample();};f.Request();f.Tick(0);check(f.Calls==1 && f.Value.Reason=="query_deadline_or_clock" && f.Output.Contains("sdk_completed_ticks: 1001"),"Fresh injected completion clock refuses a blocking SDK callback rather than using incoming Tick time");}
        using(var f=new Fixture())
        {f.Read=()=> {f.Generation=8;return Sample();};f.Request();f.Tick(0);check(f.Value.Reason=="runtime_generation_changed" && f.Output.Contains("runtime_generation_after: 8"),"A generation change inside the reader is retained and refused");}
        using(var f=new Fixture())
        {int clocks=0;f.Clock=()=>++clocks==18?1001:f.Now;f.Request();foreach(long n in new long[]{0,50,100,150})f.Tick(n);check(f.Calls==4 && f.Value.Reason=="serialization_deadline_or_clock" && f.Output.Contains("status: refused"),"A fresh clock after complete encoding cannot turn a late serialized artifact into ready");}
        using(var f=new Fixture())
        {f.Read=()=> {if(f.Calls==2)throw new IOException("controlled reader failure");return Sample();};f.Request();f.Tick(0);f.Tick(50);f.Tick(100);check(f.Calls==2 && f.Output.Contains("reads_attempted: 2") && f.Output.Contains("snapshot: null") && f.Output.Contains("System.IO.IOException"),"A throwing verification records its actual attempt and unavailable raw fields rather than claiming it was unattempted");}
        using(var f=new Fixture(true))
        {f.Tick(0);check(f.Calls==0 && f.Value.Reason=="stale_request_at_startup","A startup-existing request cannot implicitly prime identities");f.Request();f.Tick(50);check(f.Calls==1,"A distinct fresh run-bound request is required after startup");}
        foreach(string extra in new[]{"other=yes\n","run_id=duplicate\n","request_id=duplicate\n"})
        using(var f=new Fixture())
        {f.Request(extra);f.Tick(0);check(f.Calls==0 && f.Value.Attempted && f.Value.Status=="refused","Unknown/duplicate request fields consume the one-shot without SDK reads");}
        using(var f=new Fixture())
        {f.Request(run:Guid.NewGuid().ToString("D"));f.Tick(0);check(f.Calls==0 && f.Value.Status=="refused","Foreign run requests cannot prime the SDK");}
        using(var f=new Fixture())
        {f.Request();System.IO.File.WriteAllText(f.File("rewired-pair-source-request"),"strict request raw");bool hold=f.Tick(0);check(hold && f.Calls==0 && f.Value.Reason=="strict_readiness_overlap" && System.IO.File.ReadAllText(f.File("pair-ready.strict-conflict"))=="strict request raw" && !System.IO.File.Exists(f.File("rewired-pair-source-request")),"Simultaneous requests refuse and preserve strict request bytes without sampling either source");}
        using(var f=new Fixture())
        {f.Request();check(f.Tick(0,strict:true) && f.Calls==0 && f.Value.Reason=="strict_readiness_overlap","An active strict window refuses readiness before a query");}
        using(var f=new Fixture())
        {f.Request();f.Tick(0);System.IO.File.WriteAllText(f.File("rewired-pair-source-request"),"later strict");f.Tick(50);check(f.Calls==1 && f.Value.Reason=="strict_readiness_overlap","A strict request arriving during readiness stops further readiness reads");}
        using(var f=new Fixture())
        {System.IO.File.WriteAllText(f.File(RewiredPairSourceReadiness.ResultName),"prior");f.Value=f.Create();f.Request();f.Tick(0);check(f.Calls==0 && f.Output=="prior","Preexisting output is neither overwritten nor treated as fresh readiness");}
        using(var f=new Fixture())
        {f.Request();Directory.CreateDirectory(f.File(RewiredPairSourceReadiness.ResultName));foreach(long n in new long[]{0,50,100,150})f.Tick(n);check(f.Value.Status=="output_unconfirmed" && f.Value.Attempted && Directory.Exists(f.File(RewiredPairSourceReadiness.ResultName)),"Publication collision preserves existing content and consumes the one-shot without another read sequence");}
        using(var f=new Fixture())
        {f.Request(new string('x',257));f.Tick(0);check(f.Calls==0 && f.Value.Status=="request_rejected","Oversized request is bounded before claim or SDK query");}
        using(var f=new Fixture())
        {var max=RewiredPairSourceChecks.MaximumSnapshot();f.Read=()=>max;f.Request();f.Tick(0);check(new FileInfo(f.File(RewiredPairSourceReadiness.ResultName)).Length<RewiredPairSourceReadiness.MaximumBytes,"Conservative maximum escaped raw primer remains within the separate bounded artifact");}
    }
}
