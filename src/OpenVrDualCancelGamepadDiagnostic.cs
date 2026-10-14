using System;
using Valve.VR;

namespace Ugevr
{
    public struct OwnedDualCancelPadState
    {
        public OwnedDualPadState Dual;
        public bool EastDown;
        public bool Neutral {get {return !EastDown && Dual.Neutral;}}
    }
    public interface IOwnedDualCancelGamepad : IOwnedDualGamepad
    {
        OwnedDualCancelPadState ReadCancel();
        void QueueCancel(bool west,bool east,double moveX,double moveY,double lookX,double lookY);
    }
    public sealed class OpenVrDualCancelGamepadDiagnostic
    {
        readonly Func<IOwnedDualCancelGamepad> factory;
        readonly double duration,deadzone;
        readonly bool preferOwned;
        IOwnedDualCancelGamepad pad;
        LegacyCancelPairSample candidate,source;
        ulong candidateGeneration,generation;
        double start,lastTick,releaseDeadline,lastMoveX,lastMoveY,lastLookX,lastLookY;
        bool hasTick,started,ending,finished,lastWest,lastEast;
        public const int RequiredNeutralReads=3;
        public string Status {get;private set;}
        public string Reason {get;private set;}
        public int NeutralReads {get;private set;}
        public int AddAttempts {get;private set;}
        public int Queues {get;private set;}
        public int AppliedSamples {get;private set;}
        public int ReleaseAttempts {get;private set;}
        public int RemoveAttempts {get;private set;}
        public bool ReleaseObserved {get;private set;}
        public bool Complete {get {return finished;}}
        public string CleanupStatus {get {return pad==null?"not_created":pad.CleanupStatus;}}
        public OpenVrDualCancelGamepadDiagnostic(Func<IOwnedDualCancelGamepad> factory,double seconds,double deadzone,string conflictPolicy)
        {
            if(factory==null || !Finite(seconds) || seconds<5 || seconds>300 || !Finite(deadzone) || deadzone<.1 || deadzone>.8 || (conflictPolicy!="refuse" && conflictPolicy!="prefer-owned"))throw new ArgumentException("Select bounded duration, radial deadzone and explicit current-device policy.");
            this.factory=factory;duration=seconds;this.deadzone=deadzone;preferOwned=conflictPolicy=="prefer-owned";Status="waiting_neutral";Reason="";
        }
        static bool Finite(double v) {return !Double.IsNaN(v) && !Double.IsInfinity(v);}
        static bool ValidHand(LegacyHandSample h,ETrackedControllerRole role)
        {return h.Available && h.Index<OpenVR.k_unMaxTrackedDeviceCount && h.Role==role && !String.IsNullOrEmpty(h.Identity) && h.Identity.Length<=256 && h.Axis0Type==2 && Finite(h.X) && Finite(h.Y) && Math.Abs(h.X)<=1 && Math.Abs(h.Y)<=1;}
        static bool Valid(LegacyCancelPairSample s)
        {return s.Pair.Available && ValidHand(s.Pair.Left,ETrackedControllerRole.LeftHand) && ValidHand(s.Pair.Right,ETrackedControllerRole.RightHand) && s.Pair.Left.Index!=s.Pair.Right.Index && s.Pair.Left.Identity!=s.Pair.Right.Identity && s.LeftModel==OuterDualCancelSource.LeftModel && s.RightModel==OuterDualCancelSource.RightModel && s.LeftSignature==OuterDualCancelSource.Signature && s.RightSignature==OuterDualCancelSource.Signature;}
        static bool SameHand(LegacyHandSample a,LegacyHandSample b) {return a.Index==b.Index && a.Identity==b.Identity && a.Role==b.Role && a.Axis0Type==b.Axis0Type;}
        static bool Same(LegacyCancelPairSample a,LegacyCancelPairSample b)
        {return SameHand(a.Pair.Left,b.Pair.Left) && SameHand(a.Pair.Right,b.Pair.Right) && a.LeftModel==b.LeftModel && a.RightModel==b.RightModel && a.LeftSignature==b.LeftSignature && a.RightSignature==b.RightSignature;}
        static bool Regressed(LegacyCancelPairSample a,LegacyCancelPairSample b)
        {return unchecked(b.Pair.Left.Packet-a.Pair.Left.Packet)>0x7fffffffu || unchecked(b.Pair.Right.Packet-a.Pair.Right.Packet)>0x7fffffffu;}
        bool Neutral(LegacyCancelPairSample s)
        {return s.Pair.Left.Pressed==0 && s.Pair.Right.Pressed==0 && Math.Sqrt(s.Pair.Left.X*s.Pair.Left.X+s.Pair.Left.Y*s.Pair.Left.Y)<=deadzone && Math.Sqrt(s.Pair.Right.X*s.Pair.Right.X+s.Pair.Right.Y*s.Pair.Right.Y)<=deadzone;}
        void ResetAdmission() {NeutralReads=0;candidate=new LegacyCancelPairSample();candidateGeneration=0;}
        void Map(double ix,double iy,out double x,out double y)
        {double m=Math.Sqrt(ix*ix+iy*iy);x=y=0;if(m<=deadzone)return;double scale=Math.Min(1,(m-deadzone)/(1-deadzone))/m;x=ix*scale;y=iy*scale;}
        public void Tick(bool enabled,bool focused,ulong runtimeGeneration,LegacyCancelPairSample input,double now)
        {
            if(finished)return;
            if(!Finite(now) || now<0 || hasTick && now<lastTick) {if(started)End("clock_invalid",lastTick,true);else {ResetAdmission();Reason="clock_invalid";}return;}hasTick=true;lastTick=now;
            if(ending){Drain(now);return;}
            if(!enabled || !focused){if(started)End(!enabled?"session_disabled":"focus_lost",now,false);else ResetAdmission();return;}
            if(!started)
            {
                if(!Valid(input) || !Neutral(input)){Reason=!Valid(input)?input.Pair.UnavailableReason ?? "cancel_pair_invalid":"pair_not_neutral";ResetAdmission();return;}
                if(NeutralReads>0 && candidateGeneration==runtimeGeneration && Same(candidate,input) && Regressed(candidate,input)){ResetAdmission();Reason="admission_packet_regressed";return;}
                if(NeutralReads==0 || candidateGeneration!=runtimeGeneration || !Same(candidate,input)){NeutralReads=1;candidateGeneration=runtimeGeneration;}else ++NeutralReads;candidate=input;
                if(NeutralReads<RequiredNeutralReads){Reason="neutral_pair_admission_pending";return;}
                started=true;start=now;source=input;generation=runtimeGeneration;
                try
                {
                    pad=factory();if(pad==null || !String.IsNullOrEmpty(pad.UnavailableReason)){End(pad==null?"backend_unavailable":pad.UnavailableReason,now,true);return;}
                    ++AddAttempts;pad.Add(preferOwned);var state=pad.ReadCancel();
                    if(!state.Dual.Pad.Added || !state.Dual.Pad.Enabled || !state.Dual.Pad.Current || !state.Neutral){End("owned_device_not_ready_or_neutral",now,true);return;}Status="active";Reason="";
                }
                catch {End("public_device_creation_failed",now,true);}return;
            }
            if(now-start>=duration){End("window_deadline",now,false);return;}
            if(runtimeGeneration!=generation){End("runtime_generation_changed",now,false);return;}
            if(!Valid(input)){End("cancel_pair_unavailable:"+(input.Pair.UnavailableReason ?? "validation_failed"),now,false);return;}
            if(!Same(source,input)){End("hand_identity_or_context_changed",now,false);return;}
            if(Regressed(source,input)){End("hand_packet_regressed",now,false);return;}source=input;
            try
            {
                var state=pad.ReadCancel();if(!state.Dual.Pad.Added || !state.Dual.Pad.Enabled || !state.Dual.Pad.Current){End("owned_device_or_current_changed",now,true);return;}
                if(Queues>0 && state.Dual.Pad.ButtonDown==lastWest && state.EastDown==lastEast && Math.Abs(state.Dual.Pad.X-lastMoveX)<.001 && Math.Abs(state.Dual.Pad.Y-lastMoveY)<.001 && Math.Abs(state.Dual.LookX-lastLookX)<.001 && Math.Abs(state.Dual.LookY-lastLookY)<.001)++AppliedSamples;
                double mx,my,lx,ly;Map(input.Pair.Left.X,input.Pair.Left.Y,out mx,out my);Map(input.Pair.Right.X,input.Pair.Right.Y,out lx,out ly);
                bool west=(input.Pair.Right.Pressed&OuterDualCancelSource.WestPressedMask)!=0,east=(input.Pair.Right.Pressed&OuterDualCancelSource.EastPressedMask)!=0;
                if(west==lastWest && east==lastEast && mx==lastMoveX && my==lastMoveY && lx==lastLookX && ly==lastLookY)return;
                lastWest=west;lastEast=east;lastMoveX=mx;lastMoveY=my;lastLookX=lx;lastLookY=ly;pad.QueueCancel(west,east,mx,my,lx,ly);++Queues;
            }
            catch {End("public_queue_or_read_failed",now,true);}
        }
        void End(string reason,double now,bool immediate)
        {
            if(finished)return;if(!ending){ending=true;Reason=reason;Status="releasing";releaseDeadline=now+2;}
            if(pad==null || !pad.NeedsCleanup){Finish();return;}if(!pad.HasOwnedDevice){Remove();return;}
            try {if(!pad.ReadCancel().Dual.Pad.Current)immediate=true;}catch {immediate=true;}
            if(ReleaseAttempts<2){++ReleaseAttempts;try {pad.QueueCancel(false,false,0,0,0,0);}catch {}}
            if(immediate)Remove();
        }
        void Drain(double now)
        {
            if(pad==null || !pad.HasOwnedDevice){Remove();return;}
            try {var s=pad.ReadCancel();if(!s.Dual.Pad.Added || !s.Dual.Pad.Current){Remove();return;}if(s.Neutral){ReleaseObserved=true;Remove();return;}}catch {}
            if(now>=releaseDeadline){if(ReleaseAttempts<2){++ReleaseAttempts;try {pad.QueueCancel(false,false,0,0,0,0);}catch {}}Remove();}
        }
        void Remove() {if(pad!=null && pad.NeedsCleanup)for(int i=0;i<2 && pad.NeedsCleanup && RemoveAttempts<2;++i){++RemoveAttempts;try {pad.Remove();}catch {}}Finish();}
        void Finish() {finished=true;Status=pad!=null && pad.NeedsCleanup?"cleanup_unconfirmed":"complete";}
        public void Pause(string reason,double now) {if(finished)return;if(started)End(reason,now,false);else {ResetAdmission();Reason=reason;Status="waiting_neutral";}}
        public void Cancel(string reason,double now) {if(finished)return;if(started)End(reason,now,true);else {Reason=reason;finished=true;Status="complete";}}
    }
}
