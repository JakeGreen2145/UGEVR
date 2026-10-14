using System;
using Valve.VR;

namespace Ugevr
{
    public sealed class RewiredFiveInputDiagnostic
    {
        readonly Func<IOwnedFiveRewiredInput> factory;
        readonly double duration,deadzone;
        readonly bool captureReadback;
        IOwnedFiveRewiredInput backend;
        RewiredPairSample candidate,identity;
        ulong candidateGeneration,generation;
        double startedAt,lastTick,nextReadback;
        bool started,finished,hasTick;
        public const int RequiredNeutralReads=3;
        public int NeutralReads {get;private set;}
        public string Status {get;private set;}
        public string Reason {get;private set;}
        public RewiredFailureInfo Failure {get;private set;}
        public RewiredActionReadback Readback {get;private set;}
        public int ReadbackSamples {get;private set;}
        public double ReadbackAtSeconds {get;private set;}
        public int AddAttempts {get;private set;}
        public int RemoveAttempts {get;private set;}
        public bool Complete {get {return finished;}}
        public int AppliedUpdates {get {return backend==null?0:backend.AppliedUpdates;}}
        public string CleanupStatus {get {return backend==null?"not_created":backend.CleanupStatus;}}
        public RewiredFiveInputDiagnostic(Func<IOwnedFiveRewiredInput> factory,double seconds,double deadzone,string conflictPolicy,bool captureReadback=false)
        {
            if(factory==null || !Finite(seconds) || seconds<5 || seconds>300 || !Finite(deadzone) || deadzone<.1 || deadzone>.8 || conflictPolicy!="refuse") throw new ArgumentException("Five-channel input requires duration5..300, deadzone.1...8 and refuse policy.");
            this.factory=factory;duration=seconds;this.deadzone=deadzone;this.captureReadback=captureReadback;Status="waiting_neutral";Reason="";
        }
        static bool Finite(double v) {return !Double.IsNaN(v) && !Double.IsInfinity(v);}
        static bool Hand(RewiredHandSample h,ETrackedControllerRole role,string model)
        {return h.Index<OpenVR.k_unMaxTrackedDeviceCount && h.Role==role && !String.IsNullOrEmpty(h.Identity) && h.Identity.Length<=256 && h.Model==model && h.Signature==OpenVrRewiredPairReader.Signature && Finite(h.X) && Finite(h.Y) && Math.Abs(h.X)<=1 && Math.Abs(h.Y)<=1;}
        static bool Valid(RewiredPairSample p,double now)
        {return p.Available && p.Profile==PublicOwnedRewiredInput.FiveChannelProfile && Hand(p.Left,ETrackedControllerRole.LeftHand,OpenVrRewiredPairReader.LeftModel) && Hand(p.Right,ETrackedControllerRole.RightHand,OpenVrRewiredPairReader.RightModel) && p.Left.Index!=p.Right.Index && p.Left.Identity!=p.Right.Identity && Finite(p.Right.Trigger) && p.Right.Trigger>=0 && p.Right.Trigger<=1 && Finite(p.StartedAt) && Finite(p.CompletedAt) && p.CompletedAt>=p.StartedAt && p.CompletedAt-p.StartedAt<=OpenVrRewiredPairReader.MaximumQuerySeconds && now>=p.CompletedAt && now-p.CompletedAt<=OpenVrRewiredPairReader.MaximumQuerySeconds;}
        static bool Same(RewiredHandSample a,RewiredHandSample b) {return a.Index==b.Index && a.Role==b.Role && a.Identity==b.Identity && a.Model==b.Model && a.Signature==b.Signature;}
        static bool Same(RewiredPairSample a,RewiredPairSample b) {return Same(a.Left,b.Left) && Same(a.Right,b.Right) && a.Profile==b.Profile;}
        static bool Packets(RewiredPairSample old,RewiredPairSample current) {return unchecked(current.Left.Packet-old.Left.Packet)<=0x7fffffffu && unchecked(current.Right.Packet-old.Right.Packet)<=0x7fffffffu;}
        bool Neutral(RewiredPairSample p) {return p.Left.Pressed==0 && p.Right.Pressed==0 && p.Right.Trigger==0 && Math.Sqrt(p.Left.X*p.Left.X+p.Left.Y*p.Left.Y)<=deadzone && Math.Sqrt(p.Right.X*p.Right.X+p.Right.Y*p.Right.Y)<=deadzone;}
        void Reset(string reason) {NeutralReads=0;candidate=new RewiredPairSample();candidateGeneration=0;Reason=reason;}
        void Map(double x,double y,out double ox,out double oy)
        {double length=Math.Sqrt(x*x+y*y);ox=oy=0;if(length<=deadzone) return;double scale=Math.Min(1,(length-deadzone)/(1-deadzone))/length;ox=x*scale;oy=y*scale;}
        public void Tick(bool enabled,bool focused,ulong runtimeGeneration,RewiredPairSample input,double now)
        {
            if(finished) return;
            if(!Finite(now) || hasTick && now<lastTick) {if(started) Cancel("clock_invalid",lastTick);else Reset("clock_invalid");return;}
            hasTick=true;lastTick=now;
            if(!enabled || !focused) {if(started) Cancel(!enabled?"session_disabled":"focus_lost",now);else Reset(!enabled?"session_disabled":"focus_lost");return;}
            if(!started)
            {
                if(runtimeGeneration==0 || !Valid(input,now) || !Neutral(input)) {Reset(!Valid(input,now)?input.UnavailableReason ?? "pair_source_unavailable":!Neutral(input)?"pair_not_neutral":"runtime_generation_unavailable");return;}
                if(NeutralReads!=0 && (input.CompletedAt<=candidate.CompletedAt || input.StartedAt<candidate.CompletedAt)) {Reset("pair_query_not_new");return;}
                if(NeutralReads!=0 && Same(candidate,input) && candidateGeneration==runtimeGeneration && !Packets(candidate,input)) {Reset("pair_packet_regressed");return;}
                if(NeutralReads==0 || !Same(candidate,input) || candidateGeneration!=runtimeGeneration) NeutralReads=1;else ++NeutralReads;
                candidate=input;candidateGeneration=runtimeGeneration;Reason="neutral_pair_admission_pending";
                if(NeutralReads<RequiredNeutralReads) return;
                started=true;startedAt=now;identity=input;generation=runtimeGeneration;
                try
                {
                    backend=factory();if(backend==null || !String.IsNullOrEmpty(backend.UnavailableReason)) {CaptureFailure(null,"factory","backend_unavailable");Cancel(backend==null?"backend_unavailable":backend.UnavailableReason,now);return;}
                    ++AddAttempts;backend.Add();backend.SetDesired(new RewiredFiveValues(),now+.5);Status="active";Reason="";CaptureReadback(now);
                }
                catch(Exception error) {CaptureFailure(error,"add_five_channel_input","public_api_exception");Cancel("public_creation_failed",now);}return;
            }
            if(now-startedAt>=duration) {Cancel("window_deadline",now);return;}
            if(generation!=runtimeGeneration) {Cancel("runtime_generation_changed",now);return;}
            var monitor=backend as IRewiredRuntimeMonitor;
            if(monitor!=null) try {monitor.CheckRuntime();}catch(Exception error) {CaptureFailure(error,"runtime_monitor","public_runtime_monitor_failed");Cancel(String.IsNullOrEmpty(backend.FaultReason)?"public_runtime_monitor_failed":backend.FaultReason,now);return;}
            if(!Valid(input,now)) {Cancel("pair_source_unavailable:"+(input.UnavailableReason ?? "validation_failed"),now);return;}
            if(!Same(identity,input)) {Cancel("pair_source_identity_or_context_changed",now);return;}
            if(input.CompletedAt<=identity.CompletedAt || input.StartedAt<identity.CompletedAt) {Cancel("pair_query_not_new",now);return;}
            if(!Packets(identity,input)) {Cancel("pair_packet_regressed",now);return;}identity=input;
            if(!String.IsNullOrEmpty(backend.FaultReason)) {Cancel(backend.FaultReason,now);return;}
            var values=new RewiredFiveValues {Trigger=input.Right.Trigger};Map(input.Left.X,input.Left.Y,out values.MoveX,out values.MoveY);Map(input.Right.X,input.Right.Y,out values.LookX,out values.LookY);
            try {backend.SetDesired(values,Math.Min(startedAt+duration,now+.5));CaptureReadback(now);}catch(Exception error) {CaptureFailure(error,"set_five_channel_input","public_api_exception");Cancel("public_state_failed",now);}
        }
        void CaptureReadback(double now)
        {
            if(!captureReadback || now<nextReadback) return;nextReadback=now+1;var reader=backend as IRewiredActionReadback;if(reader==null) return;
            try {Readback=reader.CaptureReadback();}catch {Readback=new RewiredActionReadback {ChannelCount=5,FailedFields=8191};}
            ReadbackAtSeconds=now;++ReadbackSamples;
        }
        void CaptureFailure(Exception error,string stage,string category)
        {if(Failure!=null) return;var d=backend as IRewiredFailureDiagnostics;Failure=d!=null && d.LastFailure!=null?d.LastFailure:RewiredFailureInfo.Capture(error,stage,category);}
        public void Cancel(string reason,double now)
        {
            if(finished) return;Reason=reason;finished=true;
            if(backend!=null)
            {
                try {backend.SetDesired(new RewiredFiveValues(),now);}catch {}
                for(int i=0;i<2 && backend.NeedsCleanup;++i) {++RemoveAttempts;try {backend.Remove();}catch {}}
            }
            Status=backend!=null && backend.NeedsCleanup?"cleanup_unconfirmed":"complete";
        }
    }
}
