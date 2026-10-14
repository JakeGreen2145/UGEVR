using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using Valve.VR;

namespace Ugevr
{
    // Passive SDK evidence only. This type has no input backend or admission result.
    public static class RewiredSourceReader
    {
        public static ControllerIdentitySnapshot Read(CVRSystem system)
        {
            var result=new ControllerIdentitySnapshot {Stage="rewired_source_query",Verdict="runtime_unavailable"};
            if(system==null) return result;
            try {result.Index=system.GetTrackedDeviceIndexForControllerRole(ETrackedControllerRole.RightHand);}
            catch(Exception error) {result.Verdict="right_index_exception";result.QueryIndexException=result.ExceptionType=error.GetType().FullName;return result;}
            if(result.Index>=OpenVR.k_unMaxTrackedDeviceCount) {result.Verdict="right_role_unavailable";return result;}
            try {result.DeviceClass=system.GetTrackedDeviceClass(result.Index.Value);}
            catch(Exception error) {result.QueryClassException=result.ExceptionType=error.GetType().FullName;result.Verdict="query_class_exception";}
            try {result.Role=system.GetControllerRoleForTrackedDeviceIndex(result.Index.Value);}
            catch(Exception error) {result.QueryRoleException=result.ExceptionType=error.GetType().FullName;result.Verdict="query_role_exception";}
            // Reuse the bounded raw observation, not a source sample that can authorize input.
            result.RejectedState=RejectedControllerObservation.Read(system,result.Index.Value);
            var state=result.RejectedState;
            result.PostIndex=state.PostRightIndex;result.PostClass=state.PostClass;result.PostRole=state.PostRole;
            result.PostSerial=state.PostSerial;result.PostModel=state.PostModel;
            if(result.ExceptionType!=null) return result;
            result.Verdict=state.StateResult==null?"joint_state_exception":state.StateResult==false?"joint_state_false":
                state.Connected!=true?"controller_disconnected":state.PoseValid!=true?"pose_invalid":state.MatrixFinite!=true?"pose_nonfinite":
                state.PostRightIndex!=result.Index || state.PostClass!=result.DeviceClass || state.PostRole!=result.Role?"context_changed":"observed";
            return result;
        }
    }

    public sealed class RewiredSourceObservation
    {
        public const int MaximumWindows=3,MaximumSamples=300,MaximumRecordBytes=16384,MaximumWindowBytes=5*1024*1024;
        readonly string directory,runId;
        readonly int processId,ownerThread;
        readonly long frequency,interval;
        readonly Func<ControllerIdentitySnapshot> read;
        readonly List<byte[]> records=new List<byte[]>();
        readonly List<string> usedIds=new List<string>();
        readonly UTF8Encoding encoding=new UTF8Encoding(false,true);
        string startupRequest,requestId;
        long start,deadline,nextSample,nextRequest,lastTick;
        int bytes;
        bool active,disposed;
        public int Windows {get;private set;}
        public int Samples {get;private set;}
        public bool Active {get {return active;}}
        public string Status {get;private set;}
        public string Reason {get;private set;}
        public string LastError {get;private set;}
        public RewiredSourceObservation(string sessionDirectory,string runId,int processId,long frequency,Func<ControllerIdentitySnapshot> read)
        {
            Guid parsed;
            try {parsed=new Guid(runId);} catch {throw new ArgumentException("Observation needs a controller run identity.");}
            if(parsed==Guid.Empty || String.IsNullOrEmpty(sessionDirectory) || !Path.IsPathRooted(sessionDirectory) || processId<=0 || frequency<20 || frequency>Int64.MaxValue/16 || read==null)
                throw new ArgumentException("Observation needs an absolute owned session, process and bounded clock.");
            directory=Path.Combine(Path.GetFullPath(sessionDirectory),"UnityVR-evidence");this.runId=parsed.ToString("D");this.processId=processId;this.frequency=frequency;
            interval=(frequency+19)/20;this.read=read;ownerThread=Thread.CurrentThread.ManagedThreadId;Status="waiting_request";Reason=LastError="";
            RejectReparse(directory);
            // Existing claims survive component recreation and consume the same launch budget.
            for(int n=1;n<=MaximumWindows;++n)
            {
                if(File.Exists(Claim(n)) || File.Exists(Result(n)))
                {
                    Windows=n;
                    if(File.Exists(Claim(n))) try {string id;ParseRequest(ReadRequest(Claim(n)),out id);usedIds.Add(id);} catch { }
                }
            }
            if(File.Exists(Request)) startupRequest=ReadRequest(Request);
        }
        string Request {get {return Path.Combine(directory,"rewired-source-request");}}
        string Claim(int number) {return Path.Combine(directory,"rewired-source-request.claimed-"+number);}
        string Result(int number) {return Path.Combine(directory,"rewired-source-window-"+number+".yaml");}
        static bool FiniteTick(long tick) {return tick>=0;}
        public void Tick(bool optIn,bool enabled,bool focused,ulong generation,DateTime utc,long tick)
        {
            if(Thread.CurrentThread.ManagedThreadId!=ownerThread) {LastError="owner_thread_required";return;}
            if(disposed) return;
            if(!FiniteTick(tick) || active && tick<lastTick) {if(active) Finish("clock_regressed",utc,tick);else LastError="clock_invalid";return;}
            lastTick=tick;
            if(!optIn) {if(active) Finish("opt_in_removed",utc,tick);return;}
            if(active && tick>=deadline) {Finish("elapsed_deadline",utc,tick);return;}
            // Paused time is never added to the deadline. No SDK calls or request claim while paused.
            if(!enabled || !focused) {if(active) Reason=!enabled?"session_disabled":"focus_lost";return;}
            try
            {
                if(!active)
                {
                    if(Windows>=MaximumWindows || tick<nextRequest) return;
                    nextRequest=tick+frequency/4;
                    if(!File.Exists(Request)) return;
                    RejectReparse(Request);string content=ReadRequest(Request);
                    if(content==startupRequest) {Reason="stale_request_at_startup";return;}
                    if(tick>Int64.MaxValue-15*frequency) {LastError="clock_deadline_overflow";return;}
                    string expected=content;File.Move(Request,Claim(Windows+1));++Windows;
                    // Parse the claimed file again. A replacement before Move cannot authorize a different request.
                    content=ReadRequest(Claim(Windows));
                    if(content!=expected) throw new FormatException("Observation request changed while being claimed.");
                    string id;ParseRequest(content,out id);
                    if(usedIds.Contains(id)) {Status="request_rejected";Reason="request_id_reused";return;}
                    usedIds.Add(id);requestId=id;records.Clear();bytes=Samples=0;
                    start=tick;deadline=tick+15*frequency;nextSample=tick;active=true;Status="observing";Reason="";
                }
                if(tick<nextSample) return;
                if(Samples>=MaximumSamples) {Finish("sample_limit",utc,tick);return;}
                // Schedule from now, never catch up with several reads in one Unity Update.
                nextSample=tick+interval;
                ControllerIdentitySnapshot snapshot=read();
                if(snapshot==null) throw new InvalidOperationException("Observation reader returned no record.");
                string body="---\nsample_number: "+(Samples+1)+"\nmonotonic_ticks: "+tick+"\nruntime_generation: "+generation+
                    "\ncapture_utc: '"+utc.ToString("o",CultureInfo.InvariantCulture)+"'\n"+snapshot.ToPrivateYaml();
                byte[] record=encoding.GetBytes(body);
                if(record.Length>MaximumRecordBytes || bytes+record.Length>MaximumWindowBytes-4096) {Finish("record_bound_exceeded",utc,tick);return;}
                records.Add(record);bytes+=record.Length;++Samples;Reason=snapshot.Verdict;
            }
            catch(Exception error)
            {
                LastError=error.GetType().FullName;
                if(active) Finish("observation_failed",utc,tick);else {Status="request_rejected";Reason="request_invalid_or_unavailable";}
            }
        }
        public void Cancel(string reason,DateTime utc,long tick)
        {
            if(Thread.CurrentThread.ManagedThreadId!=ownerThread) {LastError="owner_thread_required";return;}
            if(active) Finish(reason,utc,tick);disposed=true;
        }
        void Finish(string reason,DateTime utc,long tick)
        {
            active=false;Status="complete";Reason=reason;
            string temporary=Result(Windows)+"."+Guid.NewGuid().ToString("N")+".tmp";
            try
            {
                RejectReparse(directory);Directory.CreateDirectory(directory);RejectReparse(directory);
                // Serialize the complete stream before touching an output file. Move never overwrites a prior result.
                using(var body=new MemoryStream())
                {
                    byte[] header=encoding.GetBytes("---\nschema_version: 1\nprivate_device_identity: true\nauthorizes_input: false\nrun_id: '"+runId+"'\nrequest_id: '"+requestId+"'\nprocess_id: "+processId+"\nwindow_number: "+Windows+"\nduration_seconds: 15\nmaximum_samples: 300\nmaximum_sample_rate_hz: 20\nmonotonic_frequency: "+frequency+"\nstart_ticks: "+start+"\ndeadline_ticks: "+deadline+"\n");
                    body.Write(header,0,header.Length);foreach(byte[] record in records) body.Write(record,0,record.Length);
                    byte[] summary=encoding.GetBytes("---\nstatus: complete\nreason: "+ControllerIdentitySnapshot.Scalar(reason)+"\nsamples: "+Samples+"\nend_ticks: "+tick+"\nfinished_utc: '"+utc.ToString("o",CultureInfo.InvariantCulture)+"'\n");body.Write(summary,0,summary.Length);
                    if(body.Length>MaximumWindowBytes) throw new InvalidOperationException("Observation exceeds window byte bound.");
                    using(var file=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)) body.WriteTo(file);
                }
                File.Move(temporary,Result(Windows));
            }
            catch(Exception error) {Status="output_unconfirmed";LastError=error.GetType().FullName;}
            finally {records.Clear();bytes=0;try {if(File.Exists(temporary)) File.Delete(temporary);} catch { }}
        }
        string ReadRequest(string path)
        {
            RejectReparse(path);
            using(var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite | FileShare.Delete))
            {
                byte[] buffer=new byte[257];int count=0;
                while(count<buffer.Length) {int n=input.Read(buffer,count,buffer.Length-count);if(n==0) break;count+=n;}
                if(count>256) throw new FormatException("Observation request exceeds 256 bytes.");
                return encoding.GetString(buffer,0,count);
            }
        }
        void ParseRequest(string content,out string id)
        {
            string run=null;id=null;
            foreach(string line in content.Split('\n'))
            {
                if(line.Trim().Length==0) continue;int equals=line.IndexOf('=');if(equals<=0) throw new FormatException("Expected key=value.");
                string key=line.Substring(0,equals).Trim(),value=line.Substring(equals+1).Trim();
                if(key=="run_id" && run==null) run=value;else if(key=="request_id" && id==null) id=value;else throw new FormatException("Unsupported or repeated observation field.");
            }
            Guid parsed;if(run!=runId || !TryGuid(id,out parsed) || parsed==Guid.Empty) throw new FormatException("Observation request identity mismatch.");id=parsed.ToString("D");
        }
        static bool TryGuid(string value,out Guid guid) {try {guid=new Guid(value);return true;}catch {guid=Guid.Empty;return false;}}
        static void RejectReparse(string path)
        {
            for(string part=path;part!=null;part=Path.GetDirectoryName(part))
            {
                FileAttributes attributes;
                try {attributes=File.GetAttributes(part);}
                catch(FileNotFoundException) {continue;}
                catch(DirectoryNotFoundException) {continue;}
                if((attributes&FileAttributes.ReparsePoint)!=0) throw new IOException("Observation cannot cross a reparse point.");
            }
        }
    }
}
