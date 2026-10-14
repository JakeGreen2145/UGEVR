using System;

namespace Ugevr
{
    public struct RewiredRightSample
    {
        public bool Available;
        public uint Index,Packet;
        public int TriggerAxis;
        public string Identity,UnavailableReason;
        public double X,Y,Trigger;
    }
    public interface IOwnedRewiredInput
    {
        string UnavailableReason {get;}
        string FaultReason {get;}
        string CleanupStatus {get;}
        bool NeedsCleanup {get;}
        int AppliedUpdates {get;}
        void Add();
        void SetDesired(double x,double y,double trigger,double expires);
        void Remove();
    }
    public interface IRewiredRuntimeMonitor {void CheckRuntime();}
    // One explicitly selected window. A source sample is permission to feed only until its expiry.
    public sealed class RewiredInputDiagnostic
    {
        readonly Func<IOwnedRewiredInput> factory;
        readonly double duration,deadzone;
        IOwnedRewiredInput backend;
        RewiredRightSample identity;
        double startedAt,lastTick;
        ulong generation;
        bool started,finished;
        public string Status {get;private set;}
        public string Reason {get;private set;}
        public RewiredFailureInfo Failure {get;private set;}
        public int AddAttempts {get;private set;}
        public int RemoveAttempts {get;private set;}
        public bool Complete {get {return finished;}}
        public int AppliedUpdates {get {return backend==null?0:backend.AppliedUpdates;}}
        public string CleanupStatus {get {return backend==null?"not_created":backend.CleanupStatus;}}
        public RewiredInputDiagnostic(Func<IOwnedRewiredInput> factory,double seconds,double deadzone,string conflictPolicy)
        {
            if(factory==null || !Finite(seconds) || seconds<5 || seconds>300 || !Finite(deadzone) || deadzone<.1 || deadzone>.8 || conflictPolicy!="refuse")
                throw new ArgumentException("Rewired diagnostic requires duration 5..300, deadzone .1...8 and explicit refuse policy.");
            this.factory=factory;duration=seconds;this.deadzone=deadzone;Status="waiting_neutral";Reason="";
        }
        static bool Finite(double value) {return !Double.IsNaN(value) && !Double.IsInfinity(value);}
        static bool Valid(RewiredRightSample sample)
        {return sample.Available && !String.IsNullOrEmpty(sample.Identity) && sample.TriggerAxis>=1 && sample.TriggerAxis<=4 && Finite(sample.X) && Finite(sample.Y) && Finite(sample.Trigger) && Math.Abs(sample.X)<=1 && Math.Abs(sample.Y)<=1 && sample.Trigger>=0 && sample.Trigger<=1;}
        public void Tick(bool enabled,bool focused,ulong runtimeGeneration,RewiredRightSample input,double now)
        {
            if(finished) return;
            if(!Finite(now) || (started && now<lastTick)) {Cancel("clock_invalid",lastTick);return;}lastTick=now;
            if(!enabled || !focused) {if(started) Cancel(!enabled?"session_disabled":"focus_lost",now);return;}
            if(!started)
            {
                if(!Valid(input)) {Reason=input.UnavailableReason ?? "right_source_unavailable";return;}
                if(Math.Sqrt(input.X*input.X+input.Y*input.Y)>deadzone || input.Trigger!=0) {Reason="right_source_not_neutral";return;}
                started=true;startedAt=now;identity=input;generation=runtimeGeneration;
                string creationStage="factory";
                try
                {
                    backend=factory();
                    if(backend==null || !String.IsNullOrEmpty(backend.UnavailableReason)) {CaptureFailure(null,creationStage,"backend_unavailable");Cancel(backend==null?"backend_unavailable":backend.UnavailableReason,now);return;}
                    creationStage="add";++AddAttempts;backend.Add();Status="active";Reason="";
                    creationStage="initial_desired_state";
                    backend.SetDesired(0,0,0,now+.5);
                }
                catch(Exception error) {CaptureFailure(error,creationStage,"public_api_exception");Cancel("public_creation_failed",now);}
                return;
            }
            if(now-startedAt>=duration) {Cancel("window_deadline",now);return;}
            if(runtimeGeneration!=generation) {Cancel("runtime_generation_changed",now);return;}
            var monitor=backend as IRewiredRuntimeMonitor;
            if(monitor!=null)
            {
                try {monitor.CheckRuntime();}
                catch(Exception error) {CaptureFailure(error,"runtime_monitor","public_runtime_monitor_failed");Cancel(String.IsNullOrEmpty(backend.FaultReason)?"public_runtime_monitor_failed":backend.FaultReason,now);return;}
            }
            if(!Valid(input)) {Cancel("right_source_unavailable",now);return;}
            if(input.Index!=identity.Index || input.Identity!=identity.Identity || input.TriggerAxis!=identity.TriggerAxis) {Cancel("right_source_identity_or_axis_changed",now);return;}
            if(unchecked(input.Packet-identity.Packet)>0x7fffffffu) {Cancel("source_packet_regressed",now);return;}identity.Packet=input.Packet;
            if(!String.IsNullOrEmpty(backend.FaultReason)) {Cancel(backend.FaultReason,now);return;}
            double x=input.X,y=input.Y,magnitude=Math.Sqrt(x*x+y*y);
            if(magnitude<=deadzone) x=y=0;
            else {double scale=Math.Min(1,(magnitude-deadzone)/(1-deadzone))/magnitude;x*=scale;y*=scale;}
            try {backend.SetDesired(x,y,input.Trigger,Math.Min(startedAt+duration,now+.5));}
            catch(Exception error) {CaptureFailure(error,"set_desired_state","public_api_exception");Cancel("public_state_failed",now);}
        }
        void CaptureFailure(Exception error,string stage,string category)
        {
            if(Failure!=null) return;var diagnostics=backend as IRewiredFailureDiagnostics;
            Failure=diagnostics!=null && diagnostics.LastFailure!=null?diagnostics.LastFailure:RewiredFailureInfo.Capture(error,stage,category);
        }
        public void Cancel(string reason,double now)
        {
            if(finished) return;Reason=reason;finished=true;
            if(backend!=null)
            {
                try {backend.SetDesired(0,0,0,now);} catch { }
                for(int i=0;i<2 && backend.NeedsCleanup;++i) {++RemoveAttempts;try {backend.Remove();} catch { }}
            }
            Status=backend!=null && backend.NeedsCleanup?"cleanup_unconfirmed":"complete";
        }
    }
    public sealed class RewiredOwnershipSnapshot
    {
        public object[] Nodes;
        public string Values;
        public static bool Same(RewiredOwnershipSnapshot expected,RewiredOwnershipSnapshot current)
        {
            if(expected==null || current==null || expected.Nodes==null || current.Nodes==null || expected.Values!=current.Values || expected.Nodes.Length!=current.Nodes.Length) return false;
            for(int i=0;i<expected.Nodes.Length;++i) if(!Object.ReferenceEquals(expected.Nodes[i],current.Nodes[i])) return false;
            return true;
        }
    }
}
