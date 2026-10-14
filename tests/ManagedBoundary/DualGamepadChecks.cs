using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Ugevr;
using Valve.VR;
using Hub=UnityEngine.InputSystem.InputSystem;
using Pad=UnityEngine.InputSystem.Gamepad;

static class DualGamepadChecks
{
    sealed class Runtime
    {
        public string Fault="";
        public uint StateCalls,LeftSerialCalls,RightSerialCalls;
        public bool[] Polled=new bool[3];
        public bool PropertyBeforeJoint;
        public IVRSystem Table()
        {
            var t=new IVRSystem();
            t.GetTrackedDeviceIndexForControllerRole=delegate(ETrackedControllerRole role) {return Fault=="duplicate_index"?1u:Fault=="left_index_during_right" && StateCalls==2 && role==ETrackedControllerRole.LeftHand?3u:role==ETrackedControllerRole.LeftHand?1u:2u;};
            t.GetTrackedDeviceClass=delegate(uint index) {return Fault=="left_class_during_right" && StateCalls==2 && index==1?ETrackedDeviceClass.GenericTracker:ETrackedDeviceClass.Controller;};
            t.GetControllerRoleForTrackedDeviceIndex=delegate(uint index) {return Fault=="wrong_role" && index==2?ETrackedControllerRole.LeftHand:index==1?ETrackedControllerRole.LeftHand:ETrackedControllerRole.RightHand;};
            t.GetControllerStateWithPose=delegate(ETrackingUniverseOrigin origin,uint index,ref VRControllerState_t state,uint size,ref TrackedDevicePose_t pose)
            {
                if(origin!=ETrackingUniverseOrigin.TrackingUniverseStanding || size!=(uint)Marshal.SizeOf(typeof(VRControllerState_t))) throw new Exception("Wrong ABI.");
                ++StateCalls;Polled[index]=true;state=new VRControllerState_t {unPacketNum=17,rAxis0=new VRControllerAxis_t {x=index==1?.3f:-.4f,y=index==1?.5f:.6f}};
                if(Fault=="bad_axis" && index==2) state.rAxis0.x=float.NaN;
                pose=new TrackedDevicePose_t {bDeviceIsConnected=!(Fault=="disconnected" && index==2),bPoseIsValid=!(Fault=="pose_invalid" && index==1),mDeviceToAbsoluteTracking=new HmdMatrix34_t {m0=1,m5=1,m10=1}};
                if(Fault=="nonfinite_pose" && index==2) pose.mDeviceToAbsoluteTracking.m11=float.PositiveInfinity;
                return !(Fault=="state_false" && index==2);
            };
            t.GetInt32TrackedDeviceProperty=delegate(uint index,ETrackedDeviceProperty property,ref ETrackedPropertyError error)
            {if(!Polled[index]) PropertyBeforeJoint=true;error=Fault=="metadata_unknown" && index==2?ETrackedPropertyError.TrackedProp_UnknownProperty:ETrackedPropertyError.TrackedProp_Success;return (Fault=="metadata_trigger" && index==1) || (Fault=="left_metadata_during_right" && index==1 && StateCalls==2)?3:2;};
            t.GetStringTrackedDeviceProperty=delegate(uint index,ETrackedDeviceProperty property,StringBuilder value,uint size,ref ETrackedPropertyError error)
            {
                if(!Polled[index]) PropertyBeforeJoint=true;if(size!=257 || property!=ETrackedDeviceProperty.Prop_SerialNumber_String) throw new Exception("Wrong identity property/bound.");
                if(index==1) ++LeftSerialCalls;else ++RightSerialCalls;error=ETrackedPropertyError.TrackedProp_Success;
                value.Append(Fault=="duplicate_serial"?"same":Fault=="left_serial_during_right" && index==1 && StateCalls==2?"replacement":index==1?"left-fixture":"right-fixture");
                return Fault=="oversized_serial" && index==2?258u:14u;
            };
            if(Fault=="missing_state") t.GetControllerStateWithPose=null;
            return t;
        }
    }
    static LegacyPairSample Read(Runtime runtime)
    {
        var table=runtime.Table();var pointer=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(IVRSystem)));
        try {Marshal.StructureToPtr(table,pointer,false);var result=OpenVrLegacyPairReader.ReadSafely(new CVRSystem(pointer));GC.KeepAlive(table);return result;}
        finally {Marshal.FreeHGlobal(pointer);}
    }
    static PublicOwnedGamepad Backend(bool dual=true) {return new PublicOwnedGamepad(Assembly.GetExecutingAssembly(),false,OuterGamepadCompatibilityProfile.LayoutPrefix,OwnedGamepadButton.West,dual);}
    static LegacyPairSample Pair()
    {return new LegacyPairSample {Available=true,Left=new LegacyHandSample {Available=true,Index=1,Role=ETrackedControllerRole.LeftHand,Identity="left",Axis0Type=2,Packet=17},Right=new LegacyHandSample {Available=true,Index=2,Role=ETrackedControllerRole.RightHand,Identity="right",Axis0Type=2,Packet=17}};}
    static OpenVrDualGamepadDiagnostic Policy(double deadzone=.2) {return new OpenVrDualGamepadDiagnostic(()=>Backend(),5,deadzone,"refuse");}
    static void Admit(OpenVrDualGamepadDiagnostic d,LegacyPairSample p) {d.Tick(true,true,1,p,0);d.Tick(true,true,1,p,.1);d.Tick(true,true,1,p,.2);}
    public static void Run(Action<bool,string> check)
    {
        var r=new Runtime();var sample=Read(r);
        check(sample.Available && sample.Left.Index==1 && sample.Right.Index==2 && sample.Left.Role==ETrackedControllerRole.LeftHand && sample.Right.Role==ETrackedControllerRole.RightHand && r.StateCalls==2 && r.LeftSerialCalls==3 && r.RightSerialCalls==3 && !r.PropertyBeforeJoint,"Pair reader uses two distinct role-checked joint samples, declared joystick metadata and bounded serial checks around the entire pair");
        check(Math.Abs(sample.Left.X-.3)<1e-6 && Math.Abs(sample.Right.Y-.6)<1e-6,"Pair reader preserves independent physical left and right axis values");
        foreach(string fault in new[]{"duplicate_index","wrong_role","state_false","disconnected","pose_invalid","nonfinite_pose","bad_axis","metadata_unknown","metadata_trigger","oversized_serial","duplicate_serial","left_index_during_right","left_class_during_right","left_serial_during_right","left_metadata_during_right","missing_state"})
        {r=new Runtime {Fault=fault};sample=Read(r);check(!sample.Available,"Pair reader refuses incomplete, ambiguous or changing whole-pair context: "+fault);}
        check(!OpenVrLegacyPairReader.ReadSafely(null).Available && !OuterGamepadCompatibilityProfile.ReadDualSafely(null,OuterGamepadCompatibilityProfile.WakeName).Available,"Unavailable runtime and single-hand profile cannot publish a dual pair");
        check(OuterGamepadCompatibilityProfile.SupportsDual(OuterGamepadCompatibilityProfile.DualName) && !OuterGamepadCompatibilityProfile.Supports(OuterGamepadCompatibilityProfile.DualName) && !OuterGamepadCompatibilityProfile.SupportsDual(OuterGamepadCompatibilityProfile.ObservedAxis0Name),"Dual profile is explicit and cannot accidentally enter the existing single-hand policy");
        Hub.Reset();var b=Backend();b.Add(false);b.QueueDual(true,.25,-.5,.75,-.9);
        var queued=Hub.Pending[0].Item2;
        check(queued.buttons==1u<<11 && queued.leftStick.x==.25f && queued.leftStick.y==-.5f && queued.rightStick.x==.75f && queued.rightStick.y==-.9f,"Public typed queue maps enum West independently of OpenVR A and writes both verified stick fields");
        check(b.ReadDual().Neutral,"Queued state is not acknowledged until the normal public engine update");Hub.NormalUpdate();var state=b.ReadDual();
        check(state.Pad.ButtonDown && state.Pad.X==.25 && Math.Abs(state.LookY+.9)<1e-6 && !state.Neutral,"Public right-stick getter observes the original engine state without forcing an input update");
        b.QueueDual(false,0,0,0,0);Hub.NormalUpdate();check(b.ReadDual().Neutral,"Neutral release includes both sticks and the selected button");b.Remove();
        check(Hub.Devices.Count==1 && Hub.Layouts.Count==0 && Hub.DeviceObservers==0 && Hub.LayoutObservers==0,"Dual device cleanup retains keyboard and removes only the owned device/layout observers");
        Hub.Reset();b=Backend(false);bool rejected=false;try {b.ReadDual();}catch(InvalidOperationException) {rejected=true;}check(rejected && Hub.Adds==0 && Hub.Registers==0,"Single-hand backend cannot silently resolve or use the new dual state");b.Add(false);b.Queue(true,.3,.4);check(Hub.Pending[0].Item2.rightStick.x==0 && Hub.Pending[0].Item2.rightStick.y==0,"Existing single-hand queued state remains unchanged");b.Remove();
        foreach(string fault in new[]{"disabled","unfocused","held_left","held_right","diagonal","nondefault_deadzone","missing_left","duplicate","wrong_axis"})
        {
            Hub.Reset();var p=Pair();var d=Policy(fault=="nondefault_deadzone"?.1:.2);
            if(fault=="held_left") p.Left.Pressed=128;if(fault=="held_right") p.Right.Pressed=128;if(fault=="diagonal") {p.Right.X=.19;p.Right.Y=.19;}if(fault=="nondefault_deadzone") p.Left.X=.11;if(fault=="missing_left") p.Left.Available=false;if(fault=="duplicate") p.Left.Identity=p.Right.Identity;if(fault=="wrong_axis") p.Right.Axis0Type=3;
            for(int i=0;i<4;++i) d.Tick(fault!="disabled",fault!="unfocused",1,p,i*.1);
            check(d.AddAttempts==0 && Hub.Registers==0,"No owned registration before stable neutral admissible pair: "+fault);
        }
        Hub.Reset();var pair=Pair();var policy=Policy();policy.Tick(true,true,1,pair,0);policy.Tick(true,true,1,pair,.1);pair.Left.Identity="replacement";policy.Tick(true,true,1,pair,.2);check(policy.NeutralReads==1 && Hub.Adds==0,"Changing either identity resets the three-read pair admission");policy.Tick(true,true,2,pair,.3);check(policy.NeutralReads==1 && Hub.Adds==0,"Generation change resets pre-admission stability");policy.Cancel("test_end",.4);
        Hub.Reset();pair=Pair();policy=Policy();Admit(policy,pair);check(policy.Status=="active" && Hub.Adds==1,"Exactly three stable neutral pair reads create one owned pad");pair.Left.X=.6;pair.Right.Y=-.6;pair.Right.Pressed=128;policy.Tick(true,true,1,pair,.3);queued=Hub.Pending[0].Item2;
        check(Math.Abs(queued.leftStick.x-.5)<1e-6 && Math.Abs(queued.rightStick.y+.5)<1e-6 && queued.buttons==1u<<11 && policy.Queues==1,"Each hand receives the configured radial deadzone while right A routes through ordinary West");Hub.NormalUpdate();policy.Tick(true,true,1,pair,.4);check(policy.Queues==1 && policy.AppliedSamples==1,"Unchanged held dual state does not generate repeated events and normal engine acknowledgement covers both sticks");
        pair.Right.Pressed=0;pair.Left.Pressed=128;policy.Tick(true,true,1,pair,.5);check(Hub.Pending[0].Item2.buttons==0,"Left A never becomes the selected right-hand action");Hub.NormalUpdate();pair.Left.Identity="foreign";policy.Tick(true,true,1,pair,.6);check(!policy.Complete && policy.ReleaseAttempts==1,"Admitted hand replacement ends the window and queues complete neutral release");Hub.NormalUpdate();policy.Tick(true,true,1,pair,.7);check(policy.Complete && policy.ReleaseObserved && Hub.Devices.Count==1,"Release is observed on both sticks before owned removal");
        foreach(string fault in new[]{"missing_right","role","index","metadata","generation","packet","focus","disabled","expiry","clock"})
        {
            Hub.Reset();pair=Pair();policy=Policy();Admit(policy,pair);pair.Right.X=.7;policy.Tick(true,true,1,pair,.3);Hub.NormalUpdate();
            if(fault=="missing_right") pair.Right.Available=false;if(fault=="role") pair.Left.Role=ETrackedControllerRole.RightHand;if(fault=="index") pair.Left.Index=4;if(fault=="metadata") pair.Left.Axis0Type=0;if(fault=="packet") pair.Right.Packet=16;
            policy.Tick(fault!="disabled",fault!="focus",fault=="generation"?2UL:1UL,pair,fault=="expiry"?5.3:fault=="clock"?.1:.4);Hub.NormalUpdate();policy.Tick(true,true,1,pair,5.4);
            check(policy.Complete && Hub.Adds==1 && Hub.Devices.Count==1 && Hub.Pending.Count==0,"Post-admission fault cannot hand over and releases/removes the owned dual controls: "+fault);
        }
        Hub.Reset();pair=Pair();policy=Policy();Admit(policy,pair);pair.Right.X=.8;policy.Tick(true,true,1,pair,.3);Hub.NormalUpdate();var other=new Pad {added=true,enabled=true,layout="physical"};Hub.Devices.Add(other);other.MakeCurrent();policy.Tick(true,true,1,pair,.4);Hub.NormalUpdate();check(policy.Complete && Object.ReferenceEquals(Pad.current,other) && Hub.Devices.Contains(other),"External current-device takeover preserves the other device despite queued neutral release");
        Hub.Reset();var prior=new Pad {added=true,enabled=true,layout="physical"};Hub.Devices.Add(prior);prior.MakeCurrent();pair=Pair();policy=Policy();Admit(policy,pair);check(policy.Complete && policy.Queues==0 && Object.ReferenceEquals(Pad.current,prior) && Hub.Devices.Count==2,"Explicit refuse policy preserves an existing ordinary gamepad without borrowing it");
        policy=new OpenVrDualGamepadDiagnostic(()=>Backend(),5,.2,"prefer-owned");Admit(policy,pair);check(policy.Status=="active" && !Object.ReferenceEquals(Pad.current,prior),"Explicit prefer-owned selection temporarily selects a separate honest device");policy.Cancel("end",.3);check(Object.ReferenceEquals(Pad.current,prior) && Hub.Devices.Count==2,"Owned cleanup restores the still-present admitted previous current device");
        Hub.Reset();pair=Pair();policy=Policy();Admit(policy,pair);pair.Right.X=.8;policy.Tick(true,true,1,pair,.3);Hub.NormalUpdate();Hub.RemoveAlwaysThrow=true;policy.Cancel("failure",.4);check(policy.Complete && policy.Status=="cleanup_unconfirmed" && policy.RemoveAttempts==2,"Removal failures are bounded and remain honestly unconfirmed");Hub.RemoveAlwaysThrow=false;Hub.Reset();
        foreach(string fault in new[]{"add_after_effect","queue_after_effect","layout_replaced","removed","right_still_held"})
        {
            Hub.Reset();pair=Pair();policy=Policy();if(fault=="add_after_effect") Hub.AddThenThrow=true;Admit(policy,pair);
            if(fault!="add_after_effect") {pair.Right.X=.6;if(fault=="queue_after_effect") Hub.QueueThenThrow=true;policy.Tick(true,true,1,pair,.3);Hub.QueueThenThrow=false;Hub.NormalUpdate();}
            if(fault=="layout_replaced") Hub.Replace(Pad.current.layout);
            if(fault=="removed") Hub.RemoveDevice(Pad.current);
            if(fault=="right_still_held") {policy.Tick(true,true,1,pair,5.3);policy.Tick(true,true,1,pair,5.4);check(!policy.Complete && !policy.ReleaseObserved,"Right stick must become neutral before release can be acknowledged");}
            policy.Cancel("end",5.5);Hub.NormalUpdate();check(policy.Complete && Hub.Devices.Count==1 && Hub.DeviceObservers==0,"Bounded dual cleanup handles ownership/error paths: "+fault);
            if(fault=="layout_replaced") check(Hub.Layouts.Count==1,"Foreign layout replacement is preserved during dual cleanup");
        }
        foreach(double invalid in new[]{double.NaN,4,301}) {bool threw=false;try {new OpenVrDualGamepadDiagnostic(()=>Backend(),invalid,.2,"refuse");}catch(ArgumentException) {threw=true;}check(threw,"Dual duration bound rejects invalid value before factory");}
    }
}
