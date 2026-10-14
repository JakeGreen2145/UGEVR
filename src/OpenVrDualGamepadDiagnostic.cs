using System;
using Valve.VR;

namespace Ugevr
{
    public struct OwnedDualPadState
    {
        public OwnedPadState Pad;
        public double LookX,LookY;
        public bool Neutral {get {return Pad.Neutral && Math.Abs(LookX)<.001 && Math.Abs(LookY)<.001;}}
    }
    public interface IOwnedDualGamepad : IOwnedGamepad
    {
        OwnedDualPadState ReadDual();
        void QueueDual(bool down,double moveX,double moveY,double lookX,double lookY);
    }
    public sealed class OpenVrDualGamepadDiagnostic
    {
        readonly Func<IOwnedDualGamepad> factory;
        readonly double duration,deadzone;
        readonly bool preferOwned;
        IOwnedDualGamepad pad;
        LegacyPairSample candidate,source;
        ulong candidateGeneration,generation;
        double start,lastTick,releaseDeadline,lastMoveX,lastMoveY,lastLookX,lastLookY;
        bool started,ending,finished,lastButton;
        public string Status {get;private set;}
        public string Reason {get;private set;}
        public int NeutralReads {get;private set;}
        public const int RequiredNeutralReads=3;
        public int AddAttempts {get;private set;}
        public int Queues {get;private set;}
        public int AppliedSamples {get;private set;}
        public int ReleaseAttempts {get;private set;}
        public int RemoveAttempts {get;private set;}
        public bool ReleaseObserved {get;private set;}
        public bool Complete {get {return finished;}}
        public string CleanupStatus {get {return pad==null?"not_created":pad.CleanupStatus;}}
        public OpenVrDualGamepadDiagnostic(Func<IOwnedDualGamepad> factory,double seconds,double deadzone,string conflictPolicy)
        {
            if(factory==null || !Finite(seconds) || seconds<5 || seconds>300 || !Finite(deadzone) || deadzone<.1 || deadzone>.8 || (conflictPolicy!="refuse" && conflictPolicy!="prefer-owned")) throw new ArgumentException("Select bounded dual input duration5..300, deadzone.1...8 and an explicit current-device policy.");
            this.factory=factory;duration=seconds;this.deadzone=deadzone;preferOwned=conflictPolicy=="prefer-owned";Status="waiting_neutral";Reason="";
        }
        static bool Finite(double value) {return !Double.IsNaN(value) && !Double.IsInfinity(value);}
        static bool ValidHand(LegacyHandSample hand,ETrackedControllerRole role)
        {return hand.Available && hand.Index<OpenVR.k_unMaxTrackedDeviceCount && hand.Role==role && !String.IsNullOrEmpty(hand.Identity) && hand.Identity.Length<=256 && hand.Axis0Type==(int)EVRControllerAxisType.k_eControllerAxis_Joystick && Finite(hand.X) && Finite(hand.Y) && Math.Abs(hand.X)<=1 && Math.Abs(hand.Y)<=1;}
        static bool Valid(LegacyPairSample pair)
        {return pair.Available && ValidHand(pair.Left,ETrackedControllerRole.LeftHand) && ValidHand(pair.Right,ETrackedControllerRole.RightHand) && pair.Left.Index!=pair.Right.Index && pair.Left.Identity!=pair.Right.Identity;}
        static bool Same(LegacyHandSample a,LegacyHandSample b) {return a.Index==b.Index && a.Identity==b.Identity && a.Role==b.Role && a.Axis0Type==b.Axis0Type;}
        static bool Same(LegacyPairSample a,LegacyPairSample b) {return Same(a.Left,b.Left) && Same(a.Right,b.Right);}
        bool Neutral(LegacyPairSample input)
        {return input.Left.Pressed==0 && input.Right.Pressed==0 && Math.Sqrt(input.Left.X*input.Left.X+input.Left.Y*input.Left.Y)<=deadzone && Math.Sqrt(input.Right.X*input.Right.X+input.Right.Y*input.Right.Y)<=deadzone;}
        void ResetAdmission() {NeutralReads=0;candidate=new LegacyPairSample();candidateGeneration=0;}
        void Map(double inputX,double inputY,out double x,out double y)
        {double magnitude=Math.Sqrt(inputX*inputX+inputY*inputY);x=y=0;if(magnitude<=deadzone) return;double scale=Math.Min(1,(magnitude-deadzone)/(1-deadzone))/magnitude;x=inputX*scale;y=inputY*scale;}
        public void Tick(bool enabled,bool focused,ulong runtimeGeneration,LegacyPairSample input,double now)
        {
            if(finished) return;
            if(!Finite(now) || (started && now<lastTick)) {End("clock_invalid",lastTick,true);return;}lastTick=now;
            if(ending) {Drain(now);return;}
            if(!enabled || !focused) {if(started) End(!enabled?"session_disabled":"focus_lost",now,false);else ResetAdmission();return;}
            if(!started)
            {
                if(!Valid(input) || !Neutral(input)) {Reason=!Valid(input)?input.UnavailableReason ?? "pair_unavailable":"pair_not_neutral";ResetAdmission();return;}
                if(NeutralReads==0 || candidateGeneration!=runtimeGeneration || !Same(candidate,input)) {candidate=input;candidateGeneration=runtimeGeneration;NeutralReads=1;}else ++NeutralReads;
                if(NeutralReads<RequiredNeutralReads) {Reason="neutral_pair_admission_pending";return;}
                started=true;start=now;source=input;generation=runtimeGeneration;
                try
                {
                    pad=factory();if(pad==null || !String.IsNullOrEmpty(pad.UnavailableReason)) {End(pad==null?"backend_unavailable":pad.UnavailableReason,now,true);return;}
                    ++AddAttempts;pad.Add(preferOwned);var initial=pad.ReadDual();
                    if(!initial.Pad.Added || !initial.Pad.Enabled || !initial.Pad.Current || !initial.Neutral) {End("owned_device_not_ready_or_neutral",now,true);return;}
                    Status="active";Reason="";
                }
                catch {End("public_device_creation_failed",now,true);}return;
            }
            if(now-start>=duration) {End("window_deadline",now,false);return;}
            if(runtimeGeneration!=generation) {End("runtime_generation_changed",now,false);return;}
            if(!Valid(input)) {End("pair_unavailable:"+(input.UnavailableReason ?? "validation_failed"),now,false);return;}
            if(!Same(source,input)) {End("hand_identity_or_context_changed",now,false);return;}
            if(unchecked(input.Left.Packet-source.Left.Packet)>0x7fffffffu || unchecked(input.Right.Packet-source.Right.Packet)>0x7fffffffu) {End("hand_packet_regressed",now,false);return;}source=input;
            try
            {
                var current=pad.ReadDual();if(!current.Pad.Added || !current.Pad.Enabled || !current.Pad.Current) {End("owned_device_or_current_changed",now,true);return;}
                if(Queues>0 && current.Pad.ButtonDown==lastButton && Math.Abs(current.Pad.X-lastMoveX)<.001 && Math.Abs(current.Pad.Y-lastMoveY)<.001 && Math.Abs(current.LookX-lastLookX)<.001 && Math.Abs(current.LookY-lastLookY)<.001) ++AppliedSamples;
                double moveX,moveY,lookX,lookY;Map(input.Left.X,input.Left.Y,out moveX,out moveY);Map(input.Right.X,input.Right.Y,out lookX,out lookY);
                bool button=(input.Right.Pressed&(1UL<<(int)EVRButtonId.k_EButton_A))!=0;
                if(button==lastButton && moveX==lastMoveX && moveY==lastMoveY && lookX==lastLookX && lookY==lastLookY) return;
                lastButton=button;lastMoveX=moveX;lastMoveY=moveY;lastLookX=lookX;lastLookY=lookY;
                pad.QueueDual(button,moveX,moveY,lookX,lookY);++Queues;
            }
            catch {End("public_queue_or_read_failed",now,true);}
        }
        void End(string reason,double now,bool immediate)
        {
            if(finished) return;if(!ending) {ending=true;Reason=reason;Status="releasing";releaseDeadline=now+2;}
            if(pad==null || !pad.NeedsCleanup) {Finish();return;}if(!pad.HasOwnedDevice) {Remove();return;}
            try {if(!pad.ReadDual().Pad.Current) immediate=true;}catch {immediate=true;}
            if(ReleaseAttempts<2) {++ReleaseAttempts;try {pad.QueueDual(false,0,0,0,0);}catch {}}
            if(immediate) Remove();
        }
        void Drain(double now)
        {
            if(pad==null || !pad.HasOwnedDevice) {Remove();return;}
            try {var state=pad.ReadDual();if(!state.Pad.Added || !state.Pad.Current) {Remove();return;}if(state.Neutral) {ReleaseObserved=true;Remove();return;}}catch {}
            if(now>=releaseDeadline) {if(ReleaseAttempts<2) {++ReleaseAttempts;try {pad.QueueDual(false,0,0,0,0);}catch {}}Remove();}
        }
        void Remove()
        {if(pad!=null && pad.NeedsCleanup) for(int i=0;i<2 && pad.NeedsCleanup && RemoveAttempts<2;++i) {++RemoveAttempts;try {pad.Remove();}catch {}}Finish();}
        void Finish() {finished=true;Status=pad!=null && pad.NeedsCleanup?"cleanup_unconfirmed":"complete";}
        public void Pause(string reason,double now) {if(finished) return;if(started) End(reason,now,true);else {ResetAdmission();Reason=reason;Status="waiting_neutral";}}
        public void Cancel(string reason,double now) {if(finished) return;if(started) End(reason,now,true);else {Reason=reason;finished=true;Status="complete";}}
    }
}
