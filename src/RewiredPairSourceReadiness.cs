using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using Valve.VR;

namespace Ugevr
{
    // Separate raw, read-only preadmission evidence. Never supplies an input sample.
    public sealed class RewiredPairSourceReadiness
    {
        public const int MaximumReads=4,MaximumBytes=270336;
        public const string RequestName="pair-ready.req",ClaimName="pair-ready.claim",ResultName="pair-ready.yaml";
        readonly string directory,run;
        readonly int pid,thread;
        readonly long frequency,interval;
        readonly Func<RewiredPairSourceSnapshot> read;
        readonly Func<long> clock;
        readonly Func<ulong> currentGeneration;
        readonly UTF8Encoding utf8=new UTF8Encoding(false,true);
        readonly List<string> raw=new List<string>();
        readonly List<RewiredPairSourceSnapshot> snapshots=new List<RewiredPairSourceSnapshot>();
        sealed class HandBaseline
        {
            readonly uint index;
            readonly string serial,model;
            readonly int[] metadata=new int[5];
            public HandBaseline(PairSourceHand hand)
            {index=hand.Initial.RoleIndex.Value;serial=hand.PostSerial.RawValue;model=hand.PostModel.RawValue;for(int i=0;i<5;++i)metadata[i]=hand.AxisTypes[i].Value;}
            public bool Matches(PairSourceHand hand)
            {if(index!=hand.Initial.RoleIndex || serial!=hand.PostSerial.RawValue || model!=hand.PostModel.RawValue)return false;for(int i=0;i<5;++i)if(metadata[i]!=hand.AxisTypes[i])return false;return true;}
        }
        HandBaseline leftBaseline,rightBaseline;
        string startupRequest,id;
        long start,deadline,next,last;
        ulong generation;
        bool used,disposed,block,clockRegressed;
        public bool Active {get;private set;}
        public bool Attempted {get {return used;}}
        public int Reads {get {return raw.Count;}}
        public string Status {get;private set;}
        public string Reason {get;private set;}
        public string LastError {get;private set;}
        string PathFor(string name) {return Path.Combine(directory,name);}
        public RewiredPairSourceReadiness(string sessionDirectory,string runId,int processId,long ticksPerSecond,
            Func<RewiredPairSourceSnapshot> reader,Func<long> tick,Func<ulong> runtimeGeneration)
        {
            Guid parsed;try {parsed=new Guid(runId);}catch {throw new ArgumentException("Readiness needs an owned run.");}
            if(parsed==Guid.Empty || processId<=0 || String.IsNullOrEmpty(sessionDirectory) || !Path.IsPathRooted(sessionDirectory) || ticksPerSecond<20 || ticksPerSecond>Int64.MaxValue/4 || reader==null || tick==null || runtimeGeneration==null)
                throw new ArgumentException("Readiness needs owned paths and bounded clocks.");
            directory=Path.Combine(Path.GetFullPath(sessionDirectory),"UnityVR-evidence");run=parsed.ToString("D");pid=processId;
            frequency=ticksPerSecond;interval=(frequency+19)/20;read=reader;clock=tick;currentGeneration=runtimeGeneration;thread=Thread.CurrentThread.ManagedThreadId;
            Safe(directory);used=File.Exists(PathFor(ClaimName)) || File.Exists(PathFor(ResultName));
            if(File.Exists(PathFor(RequestName)))startupRequest=ReadRequest(PathFor(RequestName));
            Status=used?"already_attempted":"waiting_request";Reason=LastError="";
        }
        static bool Empty(string value) {return String.IsNullOrEmpty(value);}
        static string Error(Exception error) {string name=error.GetType().FullName;return name!=null && name.Length<=128?name:"exception_type_exceeds_bound";}
        static bool Finite(double value) {return !Double.IsNaN(value) && !Double.IsInfinity(value);}
        static bool Property(StringPropertyObservation value) {return value!=null && value.Accepted && !value.RawTruncated && Empty(value.ExceptionType) && !Empty(value.RawValue) && value.RawValue.Length<=256;}
        static bool Context(PairSourceContext c,uint index,ETrackedControllerRole role)
        {return c!=null && c.RoleIndex==index && c.QueriedIndex==index && c.ClassAtQueriedIndex==ETrackedDeviceClass.Controller && c.RoleAtQueriedIndex==role && Empty(c.IndexException) && Empty(c.ClassException) && Empty(c.RoleException);}
        static bool Hand(PairSourceHand h,ETrackedControllerRole role,bool verification)
        {
            if(h==null || h.ExpectedRole!=role || h.Initial==null || !h.Initial.RoleIndex.HasValue || h.Initial.RoleIndex.Value>=OpenVR.k_unMaxTrackedDeviceCount)return false;
            uint index=h.Initial.RoleIndex.Value;
            if(!Context(h.Initial,index,role) || !Context(h.Post,index,role) || !Context(h.Final,index,role) || h.StateResult!=true || h.Connected!=true || h.PoseValid!=true || h.MatrixFinite!=true || h.AxesFinite!=true || !Empty(h.StateException) || h.Pressed!=0 || h.Touched!=0 || !h.Packet.HasValue || h.Matrix==null || h.Matrix.Length!=12 || h.Axes==null || h.Axes.Length!=10)return false;
            foreach(double value in h.Matrix)if(!Finite(value))return false;
            foreach(double value in h.Axes)if(!Finite(value) || value!=0)return false;
            for(int i=0;i<5;++i)if(!h.AxisTypes[i].HasValue || h.AxisTypes[i]<0 || h.AxisTypes[i]>3 || h.AxisRawReturns[i]!=h.AxisTypes[i] || h.AxisErrors[i]!=ETrackedPropertyError.TrackedProp_Success || !Empty(h.AxisExceptions[i]))return false;
            if(!Property(h.PostSerial) || !Property(h.PostModel))return false;
            return !verification || Property(h.PreSerial) && Property(h.PreModel) && h.PreSerial.RawValue==h.PostSerial.RawValue && h.PreModel.RawValue==h.PostModel.RawValue;
        }
        bool ObserveClock(long value)
        {if(value<last){clockRegressed=true;return false;}last=value;return true;}
        static bool Usable(RewiredPairSourceSnapshot s,bool verification)
        {return s!=null && s.PairContextConsistent==true && Hand(s.Left,ETrackedControllerRole.LeftHand,verification) && Hand(s.Right,ETrackedControllerRole.RightHand,verification) && s.Left.Initial.RoleIndex!=s.Right.Initial.RoleIndex && s.Left.PostSerial.RawValue!=s.Right.PostSerial.RawValue && (!verification || s.PairIdentityConsistent==true && s.Verdict=="observed");}
        public bool Tick(bool optIn,bool enabled,bool focused,ulong runtime,DateTime utc,long tick,bool strictActive)
        {
            if(thread!=Thread.CurrentThread.ManagedThreadId){LastError="owner_thread_required";return block || Active;}
            if(disposed)return block || Active;
            if(!optIn){if(Active)Finish("opt_in_removed",utc,tick);return block || Active;}
            if(!Active && used){if(File.Exists(PathFor(RequestName))){Status="refused";Reason="request_replay_refused";}return block;}
            if(!Active && !File.Exists(PathFor(RequestName)))return block;
            try
            {
                if(!Active)
                {
                    string text=ReadRequest(PathFor(RequestName));
                    if(text==startupRequest){Reason="stale_request_at_startup";return block || Active;}
                    Safe(PathFor(ClaimName));File.Move(PathFor(RequestName),PathFor(ClaimName));used=true;
                    if(ReadRequest(PathFor(ClaimName))!=text)throw new FormatException("Readiness request changed.");
                    Parse(text);start=last=tick;
                    if(tick<0 || tick>Int64.MaxValue-frequency)throw new FormatException("Readiness clock unavailable.");
                    deadline=tick+frequency;next=tick;generation=runtime;Active=true;Status="observing";
                }
                string strict=PathFor("rewired-pair-source-request");
                if(strictActive || File.Exists(strict))
                {
                    block=true;
                    if(File.Exists(strict)){Safe(strict);ReadRequest(strict);File.Move(strict,PathFor("pair-ready.strict-conflict"));}
                    Finish("strict_readiness_overlap",utc,tick);return true;
                }
                if(!ObserveClock(tick)){Finish("clock_regressed",utc,tick);return block || Active;}
                if(tick>=deadline){Finish("readiness_deadline",utc,tick);return block || Active;}
                if(!enabled || !focused || runtime==0 || runtime!=generation || currentGeneration()!=generation)
                {Finish(!enabled?"session_disabled":!focused?"focus_lost":"runtime_generation_changed",utc,tick);return block || Active;}
                if(tick<next)return block || Active;
                long began=clock();
                if(!ObserveClock(began) || began>=deadline){Finish("query_clock_invalid",utc,began);return block || Active;}
                RewiredPairSourceSnapshot sample=null;string readError=null;
                try {sample=read();}catch(Exception error){LastError=readError=Error(error);}
                long sdkEnded=clock();ObserveClock(sdkEnded);ulong after=currentGeneration();
                int number=raw.Count;string body;
                try {body=sample==null?"snapshot: null\n":sample.ToPrivateYaml();}
                catch(Exception error){LastError=readError=Error(error);body="snapshot: null\nraw_serialization_unavailable: true\n";}
                body+="reader_exception: "+ControllerIdentitySnapshot.Scalar(readError)+"\n";
                long ended=clock();ObserveClock(ended);
                string entry="---\nread_number: "+number+"\nread_role: "+(number==0?"unqualified_primer":"verification")+"\nqualifies_strict_window: false\nstarted_ticks: "+began+"\nsdk_completed_ticks: "+sdkEnded+"\nserialized_ticks: "+ended+"\nruntime_generation_before: "+generation+"\nruntime_generation_after: "+after+"\ncapture_utc: '"+utc.ToString("o",CultureInfo.InvariantCulture)+"'\n"+body;
                if(utf8.GetByteCount(entry)>65536)throw new InvalidOperationException("Readiness raw record bound exceeded.");
                raw.Add(entry);snapshots.Add(sample);
                if(readError!=null){Finish("reader_or_serialization_exception",utc,ended);return block;}
                ended=clock();ObserveClock(ended);
                if(clockRegressed || last>=deadline || after!=generation || currentGeneration()!=generation){Finish(after!=generation || currentGeneration()!=generation?"runtime_generation_changed":"query_deadline_or_clock",utc,ended);return block || Active;}
                for(int prior=0;prior<number;++prior)if(Object.ReferenceEquals(snapshots[prior],sample)){Finish("snapshot_not_fresh",utc,ended);return block || Active;}
                if(!Usable(sample,number>0)){Finish(number==0?"primer_unusable":"verification_invalid",utc,ended);return block || Active;}
                if(number==0){leftBaseline=new HandBaseline(sample.Left);rightBaseline=new HandBaseline(sample.Right);}
                else if(!leftBaseline.Matches(sample.Left) || !rightBaseline.Matches(sample.Right))
                {Finish("verification_identity_context_metadata_changed",utc,ended);return block || Active;}
                if(raw.Count==MaximumReads){Finish("ready",utc,ended);return block || Active;}
                next=ended+interval;
            }
            catch(Exception error)
            {
                LastError=Error(error);
                // A claimed request is one shot even if parsing/publication fails.
                if(used)Finish("readiness_exception",utc,tick);else {Status="request_rejected";Reason="request_unavailable";}
            }
            return block || Active;
        }
        public void Cancel(string reason,DateTime utc,long tick)
        {if(thread!=Thread.CurrentThread.ManagedThreadId){LastError="owner_thread_required";return;}if(Active)Finish(reason,utc,tick);disposed=true;}
        void Finish(string reason,DateTime utc,long tick)
        {
            Active=false;Status=reason=="ready"?"ready":"refused";Reason=reason;
            string temporary=PathFor(".pr-"+Guid.NewGuid().ToString("N").Substring(0,8)+".tmp");
            try
            {
                var b=new StringBuilder("---\nschema_version: 1\nobserver_kind: paired_source_readiness\nprivate_device_identity: true\nauthorizes_input: false\nqualifies_strict_window: false\ngameplay_credit_seconds: 0\nrun_id: '"+run+"'\nrequest_id: "+ControllerIdentitySnapshot.Scalar(id)+"\nprocess_id: "+pid+"\nruntime_generation: "+generation+"\nmaximum_reads: 4\nmaximum_rate_hz: 20\nmaximum_duration_seconds: 1\nmonotonic_frequency: "+frequency+"\nstart_ticks: "+start+"\ndeadline_ticks: "+deadline+"\n");
                foreach(string record in raw)b.Append(record);
                string headerAndRecords=b.ToString();
                if(reason=="ready")
                {
                    tick=clock();
                    ObserveClock(tick);
                    if(clockRegressed || last>=deadline || currentGeneration()!=generation){Status="refused";Reason=reason=currentGeneration()!=generation?"runtime_generation_changed":"serialization_deadline_or_clock";}
                }
                Func<string> tail=delegate {return "---\nstatus: "+Status+"\nreason: "+ControllerIdentitySnapshot.Scalar(reason)+"\nreads_attempted: "+raw.Count+"\nverification_attempted: "+Math.Max(0,raw.Count-1)+"\nnot_attempted_remainder: "+(MaximumReads-raw.Count)+"\nstrict_windows_consumed: 0\nruntime_generation: "+generation+"\nend_ticks: "+tick+"\nfinished_utc: '"+utc.ToString("o",CultureInfo.InvariantCulture)+"'\n";};
                byte[] bytes=utf8.GetBytes(headerAndRecords+tail());
                if(reason=="ready")
                {
                    long finalClock=clock();ulong finalGeneration=currentGeneration();
                    ObserveClock(finalClock);
                    if(clockRegressed || last>=deadline || finalGeneration!=generation)
                    {tick=finalClock;Status="refused";Reason=reason=finalGeneration!=generation?"runtime_generation_changed":"serialization_deadline_or_clock";bytes=utf8.GetBytes(headerAndRecords+tail());}
                }
                if(bytes.Length>MaximumBytes)throw new IOException("Readiness artifact bound exceeded.");
                Safe(directory);Directory.CreateDirectory(directory);Safe(temporary);Safe(PathFor(ResultName));
                using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))output.Write(bytes,0,bytes.Length);
                File.Move(temporary,PathFor(ResultName));
            }
            catch(Exception error){Status="output_unconfirmed";LastError=Error(error);}
            finally {try {if(File.Exists(temporary))File.Delete(temporary);}catch {}}
        }
        string ReadRequest(string path)
        {
            Safe(path);using(var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
            {var bytes=new byte[257];int total=0,count;while(total<bytes.Length && (count=input.Read(bytes,total,bytes.Length-total))>0)total+=count;if(total>256)throw new FormatException("Readiness request exceeds bound.");return utf8.GetString(bytes,0,total);}
        }
        void Parse(string text)
        {
            string requestedRun=null;id=null;
            foreach(string line in text.Split('\n'))
            {if(line.Trim().Length==0)continue;int at=line.IndexOf('=');if(at<=0)throw new FormatException("Expected readiness key=value.");string key=line.Substring(0,at).Trim(),value=line.Substring(at+1).Trim();if(key=="run_id" && requestedRun==null)requestedRun=value;else if(key=="request_id" && id==null)id=value;else throw new FormatException("Unsupported readiness field.");}
            Guid parsed;try {parsed=new Guid(id);}catch {throw new FormatException("Readiness request identity missing.");}
            if(requestedRun!=run || parsed==Guid.Empty)throw new FormatException("Readiness request identity mismatch.");id=parsed.ToString("D");
        }
        static void Safe(string path)
        {
            for(string p=path;p!=null;p=Path.GetDirectoryName(p))
            {FileAttributes flags;try {flags=File.GetAttributes(p);}catch(FileNotFoundException){continue;}catch(DirectoryNotFoundException){continue;}if((flags&FileAttributes.ReparsePoint)!=0)throw new IOException("Readiness cannot cross a reparse point.");}
        }
    }
}
