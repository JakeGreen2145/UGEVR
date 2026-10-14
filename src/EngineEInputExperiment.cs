using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Ugevr
{
    public struct EngineESnapshot
    {
        public bool Added,Enabled,OriginalIsCurrent,Down,Press,Release;
        public int ControlCount,OtherHeldCount;
    }
    public interface IEngineEKeyboard
    {
        string UnavailableReason {get;}
        EngineESnapshot Read();
        // Release builds a fresh non-E state snapshot on this original device.
        int Queue(bool down);
    }
    public sealed class EngineEInputExperiment
    {
        readonly string directory,runId;
        readonly int processId;
        readonly long frequency;
        readonly Func<IEngineEKeyboard> createKeyboard;
        readonly Func<long> queuedClock;
        IEngineEKeyboard keyboard;
        int number,frames,releaseAttempts,preservedOther;
        bool active,downAttempted,downQueued,releaseQueued,downSeen,pressSeen,releaseSeen,releaseObserved;
        long started,downAttemptAt,downAt,releaseAttemptAt,releaseAt,holdDeadline,observationDeadline,nextPoll;
        DateTime startedUtc;
        string terminationReason;
        public bool Active {get {return active;}}
        public int RequestsConsumed {get {return number;}}
        public EngineEInputExperiment(string sessionDirectory,string runId,int processId,long frequency,Func<IEngineEKeyboard> createKeyboard,Func<long> queuedClock=null)
        {
            Guid id;try {id=new Guid(runId);} catch {throw new ArgumentException("Invalid experiment run identity.");}
            if(!Path.IsPathRooted(sessionDirectory) || processId<=0 || frequency<2 || frequency>Int64.MaxValue/4 || createKeyboard==null) throw new ArgumentException("Invalid engine input experiment session.");
            this.directory=Path.Combine(Path.GetFullPath(sessionDirectory),"UnityVR-evidence");this.runId=id.ToString("D");this.processId=processId;this.frequency=frequency;this.createKeyboard=createKeyboard;this.queuedClock=queuedClock;
            for(int i=1;i<=2;++i) if(File.Exists(Result(i)) || File.Exists(Claim(i))) number=i;
        }
        string Result(int i) {return Path.Combine(directory,"engine-input-experiment-result-"+i+".yaml");}
        string Claim(int i) {return Path.Combine(directory,"engine-input-experiment-request.claimed-"+i);}
        public void Tick(bool activated,bool focused,DateTime utc,long ticks)
        {
            if(!activated) {if(active) Cancel(utc,ticks,"activation_removed");return;}
            try
            {
                if(!active)
                {
                    if(number>=2 || ticks<nextPoll) return;
                    nextPoll=ticks+frequency/4;
                    string request=Path.Combine(directory,"engine-input-experiment-request");if(!File.Exists(request)) return;
                    RejectReparse(directory);RejectReparse(request);File.Move(request,Claim(number+1));++number;Reset(utc,ticks);active=true;
                    byte[] bytes=new byte[65];int count=0;
                    using(var stream=new FileStream(Claim(number),FileMode.Open,FileAccess.Read,FileShare.ReadWrite | FileShare.Delete))
                    {
                        while(count<bytes.Length) {int read=stream.Read(bytes,count,bytes.Length-count);if(read==0) break;count+=read;}
                    }
                    string body=count<=64?new UTF8Encoding(false,true).GetString(bytes,0,count).Replace("\r\n","\n").Trim():"";
                    if(body!="key=E\nduration_ms=500") {Finish("refused","fixed_e_500ms_required",utc,ticks);return;}
                    if(!focused) {Finish("refused","game_not_focused",utc,ticks);return;}
                    keyboard=createKeyboard();
                    if(keyboard==null || !String.IsNullOrEmpty(keyboard.UnavailableReason)) {Finish("unavailable",keyboard==null?"keyboard_unavailable":keyboard.UnavailableReason,utc,ticks);return;}
                    EngineESnapshot baseline=keyboard.Read();
                    if(!baseline.Added || !baseline.Enabled || !baseline.OriginalIsCurrent || baseline.ControlCount<=0 || baseline.ControlCount>256)
                    {Finish("unavailable","original_keyboard_not_ready",utc,ticks);return;}
                    if(baseline.Down || baseline.OtherHeldCount!=0) {Finish("refused","baseline_not_neutral",utc,ticks);return;}
                    downAttemptAt=QueueTime(ticks);downAttempted=true;
                    keyboard.Queue(true);downQueued=true;downAt=QueueTime(ticks);holdDeadline=downAt+frequency/2;observationDeadline=holdDeadline+2*frequency;
                    return;
                }
                EngineESnapshot observed=keyboard.Read();++frames;downSeen|=observed.Down;pressSeen|=observed.Press;releaseSeen|=observed.Release;
                if(releaseQueued && (observed.Release || (downSeen && !observed.Down))) releaseObserved=true;
                if(ticks<started) {Cancel(utc,ticks,"monotonic_clock_regressed");return;}
                if(!releaseQueued && (ticks>=holdDeadline || !focused || !observed.OriginalIsCurrent || !observed.Added))
                {
                    terminationReason=!focused?"focus_lost":!observed.OriginalIsCurrent?"current_keyboard_changed":!observed.Added?"original_keyboard_removed":"hold_deadline";
                    TryRelease(ticks);
                }
                if(releaseQueued && releaseObserved) {Finish("complete",terminationReason,utc,ticks);return;}
                if(ticks>=observationDeadline)
                {
                    if(!releaseQueued) TryRelease(ticks);
                    Finish("release_unobserved",terminationReason ?? "observation_deadline",utc,ticks);
                }
            }
            catch
            {
                if(active)
                {
                    if(downAttempted) TryRelease(ticks);
                    Finish("error","engine_public_api_failed",utc,ticks);
                }
            }
        }
        void Reset(DateTime utc,long ticks)
        {
            keyboard=null;frames=releaseAttempts=preservedOther=0;downAttempted=downQueued=releaseQueued=downSeen=pressSeen=releaseSeen=releaseObserved=false;
            started=ticks;startedUtc=utc;downAttemptAt=downAt=releaseAttemptAt=releaseAt=holdDeadline=observationDeadline=0;terminationReason=null;
        }
        void TryRelease(long ticks)
        {
            if(!downAttempted || releaseQueued || releaseAttempts>=2 || keyboard==null) return;
            ++releaseAttempts;releaseAttemptAt=QueueTime(ticks);
            try {preservedOther=keyboard.Queue(false);releaseQueued=true;releaseAt=QueueTime(ticks);} catch { }
        }
        long QueueTime(long fallback)
        {
            // Product code supplies Stopwatch.GetTimestamp; controlled checks can supply their clock.
            try {return queuedClock==null?fallback:queuedClock();} catch {return fallback;}
        }
        public void Cancel(DateTime utc,long ticks,string reason)
        {
            if(!active) return;
            TryRelease(ticks);Finish("cancelled",reason,utc,ticks);
        }
        void Finish(string status,string reason,DateTime utc,long ticks)
        {
            ticks=Math.Max(ticks,QueueTime(ticks));
            active=false;keyboard=null;
            string path=Result(number),temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try
            {
                RejectReparse(directory);
                var body=new StringBuilder();
                Add(body,"schema_version",1);Add(body,"run_id",runId);Add(body,"process_id",processId);Add(body,"experiment_number",number);Add(body,"status",status);Add(body,"reason",reason ?? "");
                Add(body,"engine_synthetic",true);Add(body,"physical_forwarding",false);Add(body,"gate_eligible",false);Add(body,"game_wake","unknown");Add(body,"input_receipt","unknown");Add(body,"physical_e_origin","unknown");Add(body,"non_e_preservation","current_state_snapshot_not_atomic_merge");
                Add(body,"requested_hold_ms",500);Add(body,"runtime_applied_hold_ms","unknown");Add(body,"monotonic_frequency",frequency);Add(body,"started_ticks",started);Add(body,"down_attempt_started_ticks",downAttemptAt);Add(body,"down_queued_ticks",downAt);Add(body,"hold_deadline_ticks",holdDeadline);Add(body,"last_release_attempt_started_ticks",releaseAttemptAt);Add(body,"release_queued_ticks",releaseAt);Add(body,"finished_ticks",ticks);
                Add(body,"hold_elapsed_ms",downQueued?1000.0*((releaseQueued?releaseAt:ticks)-downAt)/frequency:0);Add(body,"hold_overshoot_ms",releaseQueued?Math.Max(0,1000.0*(releaseAt-holdDeadline)/frequency):0);
                Add(body,"started_utc",startedUtc.ToString("o",CultureInfo.InvariantCulture));Add(body,"finished_utc",utc.ToString("o",CultureInfo.InvariantCulture));Add(body,"observed_frames",frames);
                Add(body,"down_attempted",downAttempted);Add(body,"down_queued",downQueued);Add(body,"down_queue_outcome",downQueued?"returned_success":downAttempted?"unknown":"not_attempted");Add(body,"release_queued",releaseQueued);Add(body,"down_observed",downSeen);Add(body,"press_observed",pressSeen);Add(body,"release_edge_observed",releaseSeen);Add(body,"release_state_observed",releaseObserved);Add(body,"cleanup_observed",releaseQueued && releaseObserved);Add(body,"release_attempts",releaseAttempts);Add(body,"preserved_non_e_count",preservedOther);
                File.WriteAllText(temporary,body.ToString(),new UTF8Encoding(false));File.Move(temporary,path);
            }
            catch { }
            finally {try {if(File.Exists(temporary)) File.Delete(temporary);} catch {}}
        }
        static void Add(StringBuilder body,string key,object value)
        {
            body.Append(key).Append(": ");
            if(value is bool) body.Append((bool)value?"true":"false");
            else if(value is string) body.Append('\'').Append(((string)value).Replace("'","''").Replace("\r"," ").Replace("\n"," ")).Append('\'');
            else body.Append(Convert.ToString(value,CultureInfo.InvariantCulture));body.Append('\n');
        }
        static void RejectReparse(string path)
        {for(string part=path;part!=null;part=Path.GetDirectoryName(part)) if((File.GetAttributes(part)&FileAttributes.ReparsePoint)!=0) throw new IOException("Experiment path crosses a reparse point.");}
    }
}
