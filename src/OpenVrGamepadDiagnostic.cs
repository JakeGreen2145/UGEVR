using System;

namespace Ugevr
{
    public struct LegacyRightSample
    {
        public bool Available,A,AxesAvailable;
        public uint Index,Packet;
        public string Identity;
        public string UnavailableReason;
        public double X,Y;
    }
    public struct OwnedPadState
    {
        public bool Added,Enabled,Current,ButtonDown;
        public double X,Y;
        public bool Neutral {get {return !ButtonDown && Math.Abs(X)<0.001 && Math.Abs(Y)<0.001;}}
    }
    public interface IOwnedGamepad
    {
        string UnavailableReason {get;}
        bool HasOwnedDevice {get;}
        bool NeedsCleanup {get;}
        void Add(bool preferOwned);
        OwnedPadState Read();
        void Queue(bool down,double x,double y);
        // Removes only the captured reference. Current restoration is conditional.
        void Remove();
        string CleanupStatus {get;}
    }
    // One explicitly activated, bounded diagnostic window per component lifetime.
    public sealed class OpenVrGamepadDiagnostic
    {
        readonly Func<IOwnedGamepad> factory;
        readonly double duration,deadzone;
        readonly bool preferOwned;
        IOwnedGamepad pad;
        LegacyRightSample source;
        ulong generation;
        double start,lastTick,releaseDeadline;
        bool started,ending,finished,lastButton;
        double lastX,lastY;
        public string Status {get;private set;}
        public string Reason {get;private set;}
        public int AddAttempts {get;private set;}
        public int Queues {get;private set;}
        public int ReleaseAttempts {get;private set;}
        public int RemoveAttempts {get;private set;}
        public int AppliedSamples {get;private set;}
        public bool ReleaseObserved {get;private set;}
        public string CleanupStatus {get {return pad==null?"not_created":pad.CleanupStatus;}}
        public bool Complete {get {return finished;}}
        public OpenVrGamepadDiagnostic(Func<IOwnedGamepad> factory,double seconds,double deadzone,string conflictPolicy)
        {
            if(factory==null || !Finite(seconds) || seconds<5 || seconds>300 || !Finite(deadzone) || deadzone<0.1 || deadzone>0.8)
                throw new ArgumentException("Invalid bounded gamepad diagnostic profile.");
            if(conflictPolicy!="refuse" && conflictPolicy!="prefer-owned") throw new ArgumentException("Select refuse or prefer-owned explicitly.");
            this.factory=factory;duration=seconds;this.deadzone=deadzone;preferOwned=conflictPolicy=="prefer-owned";Status="waiting_neutral";Reason="";
        }
        static bool Finite(double x) {return !Double.IsNaN(x) && !Double.IsInfinity(x);}
        static bool Valid(LegacyRightSample s) {return s.Available && !String.IsNullOrEmpty(s.Identity) && Finite(s.X) && Finite(s.Y) && Math.Abs(s.X)<=1 && Math.Abs(s.Y)<=1;}
        public void Tick(bool enabled,bool focused,ulong runtimeGeneration,LegacyRightSample input,double now)
        {
            if(finished) return;
            if(!Finite(now)) {End("clock_invalid",lastTick,true);return;}
            if(started && now<lastTick) {End("clock_regressed",lastTick,true);return;}
            lastTick=now;
            if(ending) {Drain(now);return;}
            if(!enabled || !focused)
            {
                if(started) End(!enabled?"session_disabled":"focus_lost",now,false);
                return;
            }
            if(!started)
            {
                if(!Valid(input)) {Reason=input.UnavailableReason ?? "right_controller_unavailable";return;}
                // Activation must be zero under the same radial mapping used below.
                if(input.A || Math.Sqrt(input.X*input.X+input.Y*input.Y)>deadzone) {Reason="right_controller_not_neutral";return;}
                started=true;start=now;source=input;generation=runtimeGeneration;
                try
                {
                    pad=factory();
                    if(pad==null || !String.IsNullOrEmpty(pad.UnavailableReason)) {End(pad==null?"backend_unavailable":pad.UnavailableReason,now,true);return;}
                    ++AddAttempts;pad.Add(preferOwned);
                    var initial=pad.Read();
                    if(!initial.Added || !initial.Enabled || !initial.Current) {End("owned_device_not_ready",now,true);return;}
                    Status="active";
                    Reason="";
                }
                catch {End("device_add_failed",now,true);}
                return;
            }
            if(now-start>=duration) {End("window_deadline",now,false);return;}
            if(runtimeGeneration!=generation) {End("runtime_generation_changed",now,false);return;}
            if(!Valid(input)) {End("controller_unavailable",now,false);return;}
            if(input.Index!=source.Index || input.Identity!=source.Identity) {End("controller_identity_changed",now,false);return;}
            if(unchecked(input.Packet-source.Packet)>0x7fffffffu) {End("controller_packet_regressed",now,false);return;}
            source.Packet=input.Packet;
            try
            {
                var current=pad.Read();
                if(!current.Added || !current.Enabled) {End("owned_device_removed_or_disabled",now,true);return;}
                if(!current.Current) {End("current_device_conflict",now,true);return;}
                if(Queues>0 && current.ButtonDown==lastButton && Math.Abs(current.X-lastX)<0.001 && Math.Abs(current.Y-lastY)<0.001) ++AppliedSamples;
                double x=input.X,y=input.Y,mag=Math.Sqrt(x*x+y*y);
                if(mag<=deadzone) x=y=0;
                else {double scale=Math.Min(1,(mag-deadzone)/(1-deadzone))/mag;x*=scale;y*=scale;}
                if(input.A==lastButton && x==lastX && y==lastY) return;
                // Record the desired state before the call: a throwing queue may have enqueued it.
                lastButton=input.A;lastX=x;lastY=y;
                pad.Queue(input.A,x,y);++Queues;
            }
            catch {End("public_input_queue_or_read_failed",now,true);}
        }
        void End(string reason,double now,bool immediate)
        {
            if(finished) return;
            if(!ending) {ending=true;Reason=reason;Status="releasing";releaseDeadline=now+2;}
            if(pad==null || !pad.NeedsCleanup) {Finish();return;}
            if(!pad.HasOwnedDevice) {Remove();return;}
            // A neutral queued event can itself make its pad current. Preserve an observed
            // takeover by removing this device before that queued event can be processed.
            try {if(!pad.Read().Current) immediate=true;} catch {immediate=true;}
            if(ReleaseAttempts<2)
            {
                ++ReleaseAttempts;
                try {pad.Queue(false,0,0);} catch { }
            }
            if(immediate) Remove();
        }
        void Drain(double now)
        {
            if(pad==null || !pad.HasOwnedDevice) {Remove();return;}
            try
            {
                var state=pad.Read();
                if(!state.Added) {Remove();return;}
                if(!state.Current) {Remove();return;}
                // At least one subsequent Unity Update is required; the queue itself is no acknowledgement.
                if(state.Neutral) {ReleaseObserved=true;Remove();return;}
            }
            catch { }
            if(now>=releaseDeadline)
            {
                if(ReleaseAttempts<2) {++ReleaseAttempts;try {pad.Queue(false,0,0);} catch { }}
                Remove();
            }
        }
        void Remove()
        {
            if(pad!=null && pad.NeedsCleanup && RemoveAttempts<2)
            {
                ++RemoveAttempts;try {pad.Remove();} catch { }
                if(pad.NeedsCleanup && RemoveAttempts<2) {++RemoveAttempts;try {pad.Remove();} catch { }}
            }
            Finish();
        }
        void Finish() {finished=true;Status=pad!=null && pad.NeedsCleanup?"cleanup_unconfirmed":"complete";}
        // A reversible VR pause cannot spend a window that has not created anything yet.
        public void Pause(string reason,double now)
        {
            if(finished) return;
            if(started) End(reason,now,true);
            else {Reason=reason;Status="waiting_neutral";}
        }
        public void Cancel(string reason,double now) {if(!finished && started) End(reason,now,true);else if(!started) {Reason=reason;finished=true;Status="complete";}}
    }
}
