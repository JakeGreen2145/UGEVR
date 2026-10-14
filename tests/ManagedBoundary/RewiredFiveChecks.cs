using System;
using System.Runtime.InteropServices;
using System.Text;
using Ugevr;
using Valve.VR;

static class RewiredFiveChecks
{
    sealed class Runtime
    {
        public string Fault="";
        public int States,Properties,Metadata;
        public bool Primed;
        public double Clock;
        public float LX,LY,RX,RY,Trigger;
        public uint LeftPacket=1,RightPacket=1;
        public ulong LeftPressed=0,RightPressed=0;
        public IVRSystem Table()
        {
            var t=new IVRSystem();
            t.GetTrackedDeviceIndexForControllerRole=delegate(ETrackedControllerRole role) {return Fault=="invalid_index"?64u:Fault=="duplicate_index"?1u:role==ETrackedControllerRole.LeftHand?2u:1u;};
            t.GetTrackedDeviceClass=delegate(uint index) {return Fault=="initial_class" || Fault=="post_class" && States>0?ETrackedDeviceClass.HMD:ETrackedDeviceClass.Controller;};
            t.GetControllerRoleForTrackedDeviceIndex=delegate(uint index) {return Fault=="initial_role" || Fault=="post_role" && States>0?ETrackedControllerRole.Invalid:index==2?ETrackedControllerRole.LeftHand:ETrackedControllerRole.RightHand;};
            t.GetStringTrackedDeviceProperty=delegate(uint index,ETrackedDeviceProperty property,StringBuilder value,uint capacity,ref ETrackedPropertyError error)
            {
                if(capacity!=257 || index!=1 && index!=2) throw new Exception("Unexpected bounded pair property query.");
                if(Fault=="throw_property") throw new InvalidOperationException("Controlled property fault.");
                ++Properties;bool model=property==ETrackedDeviceProperty.Prop_ModelNumber_String;
                error=Fault=="property_error" || Fault=="bootstrap" && !Primed?ETrackedPropertyError.TrackedProp_UnknownProperty:ETrackedPropertyError.TrackedProp_Success;
                string text=model?index==2?OpenVrRewiredPairReader.LeftModel:OpenVrRewiredPairReader.RightModel:Fault=="duplicate_serial"?"same":"private-hand-"+index;
                if(Fault=="wrong_left_model" && index==2 && model) text=OpenVrRewiredPairReader.RightModel;
                if(Fault=="serial_changed" && States>0 && !model || Fault=="model_changed" && States>0 && model || Fault=="left_changes_during_right_post" && Properties>8 && index==2 && !model) text="replacement";
                if(Fault=="bootstrap_model" && !Primed && model) text="Deckard MP";
                if(Fault!="empty_identity") value.Append(text);return Fault=="oversized_identity"?258u:Fault=="zero_identity"?0u:(uint)(text.Length+1);
            };
            t.GetInt32TrackedDeviceProperty=delegate(uint index,ETrackedDeviceProperty property,ref ETrackedPropertyError error)
            {
                ++Metadata;error=Fault=="metadata_unknown" || Fault=="bootstrap" && !Primed?ETrackedPropertyError.TrackedProp_UnknownProperty:ETrackedPropertyError.TrackedProp_Success;
                int axis=(int)property-(int)ETrackedDeviceProperty.Prop_Axis0Type_Int32;
                if(axis<0 || axis>4) throw new Exception("Unexpected metadata.");
                return Fault=="post_metadata" && States>0 || Fault=="left_metadata" && index==2?0:axis==0?2:axis<3?3:0;
            };
            t.GetControllerStateWithPose=delegate(ETrackingUniverseOrigin origin,uint index,ref VRControllerState_t state,uint size,ref TrackedDevicePose_t pose)
            {
                if(size!=(uint)Marshal.SizeOf(typeof(VRControllerState_t)) || origin!=ETrackingUniverseOrigin.TrackingUniverseStanding) throw new Exception("Wrong SDK state ABI.");
                ++States;Primed=true;if(Fault=="throw_joint") throw new InvalidOperationException("Controlled joint fault.");
                state=new VRControllerState_t {unPacketNum=index==2?LeftPacket:RightPacket,ulButtonPressed=index==2?LeftPressed:RightPressed,rAxis0=new VRControllerAxis_t {x=index==2?LX:RX,y=index==2?LY:RY},rAxis1=new VRControllerAxis_t {x=Trigger},rAxis2=new VRControllerAxis_t {x=.9f}};
                pose=new TrackedDevicePose_t {bDeviceIsConnected=Fault!="disconnected",bPoseIsValid=Fault!="invalid_pose",mDeviceToAbsoluteTracking=new HmdMatrix34_t {m0=1,m5=1,m10=Fault=="nonfinite_pose"?Single.NaN:1}};
                if(Fault=="query_long") Clock+=.2;if(Fault=="query_regressed") Clock-=.01;
                return Fault!="joint_false" && !(Fault=="right_joint_false" && index==1);
            };return t;
        }
    }
    static RewiredPairSample Read(Runtime r)
    {
        r.States=r.Properties=r.Metadata=0;var table=r.Table();IntPtr pointer=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(IVRSystem)));
        try {Marshal.StructureToPtr(table,pointer,false);var value=OpenVrRewiredPairReader.ReadSafely(new CVRSystem(pointer),()=>r.Clock);GC.KeepAlive(table);return value;}finally {Marshal.FreeHGlobal(pointer);}
    }
    sealed class Backend : IOwnedFiveRewiredInput,IRewiredActionReadback
    {
        public int Adds,Removes,Sets,Readbacks;
        public bool Added,Preserve,ThrowAdd,ThrowSet;
        public RewiredFiveValues Values;
        public double Expires;
        public string UnavailableReason {get;set;}
        public string FaultReason {get;set;}
        public string CleanupStatus {get {return Added?"owned_active":"owned_resources_removed";}}
        public bool NeedsCleanup {get {return Added;}}
        public int AppliedUpdates {get {return Sets;}}
        public void Add() {++Adds;Added=true;if(ThrowAdd) throw new InvalidOperationException();}
        public void SetDesired(double x,double y,double t,double e) {throw new Exception("Legacy setter must not receive paired state.");}
        public void SetDesired(RewiredFiveValues value,double expires) {++Sets;Values=value;Expires=expires;if(ThrowSet) throw new InvalidOperationException();}
        public void Remove() {++Removes;if(!Preserve) Added=false;}
        public RewiredActionReadback CaptureReadback() {++Readbacks;return new RewiredActionReadback {ChannelCount=5};}
    }
    static PublicOwnedRewiredInput Adapter(Func<double> clock,Func<bool> focused=null)
    {return new PublicOwnedRewiredInput(typeof(Rewired.ReInput).Assembly,new Rewired.InputManager_Base(),clock,focused??(()=>true),false,RewiredChannelDescriptor.Five);}
    static RewiredPairSample Sample(double now,uint packet=1)
    {
        return new RewiredPairSample {Available=true,Profile=PublicOwnedRewiredInput.FiveChannelProfile,StartedAt=now,CompletedAt=now,Left=new RewiredHandSample {Index=2,Packet=packet,Role=ETrackedControllerRole.LeftHand,Identity="left",Model=OpenVrRewiredPairReader.LeftModel,Signature=OpenVrRewiredPairReader.Signature},Right=new RewiredHandSample {Index=1,Packet=packet,Role=ETrackedControllerRole.RightHand,Identity="right",Model=OpenVrRewiredPairReader.RightModel,Signature=OpenVrRewiredPairReader.Signature}};
    }
    static void Admit(RewiredFiveInputDiagnostic d)
    {d.Tick(true,true,1,Sample(0),0);d.Tick(true,true,1,Sample(.01),.01);d.Tick(true,true,1,Sample(.02),.02);}
    public static void Run(Action<bool,string> check)
    {
        var r=new Runtime {LX=.35f,LY=.6f,RX=-.6f,RY=.1f,Trigger=.5984252f};var sample=Read(r);
        check(sample.Available && sample.Left.X==r.LX && sample.Right.Trigger==r.Trigger && r.States==2 && r.Properties==12 && r.Metadata==30,"Both joint reads publish only after independent initial/post/final complete property brackets and exact repeated signatures");
        foreach(string fault in new[]{"invalid_index","duplicate_index","initial_class","initial_role","property_error","empty_identity","oversized_identity","zero_identity","wrong_left_model","duplicate_serial","serial_changed","model_changed","left_changes_during_right_post","metadata_unknown","post_metadata","left_metadata","post_class","post_role","disconnected","invalid_pose","nonfinite_pose","joint_false","right_joint_false","throw_joint","throw_property","query_long","query_regressed"})
        {
            r=new Runtime {Fault=fault};sample=Read(r);check(!sample.Available && sample.Left.X==0 && sample.Right.Trigger==0,"Rejected pair never publishes partial hand output: "+fault);
            if(fault=="invalid_index" || fault=="duplicate_index" || fault=="initial_class" || fault=="initial_role") check(r.States==0,"Foreign initial context never polls joint state: "+fault);
        }
        foreach(string fault in new[]{"bootstrap","bootstrap_model"})
        {r=new Runtime {Fault=fault};check(!Read(r).Available && r.States==2,"Role-valid initial properties may prime without publication: "+fault);check(Read(r).Available,"Next stable pair establishes its own complete brackets after priming: "+fault);}
        foreach(float value in new[]{Single.NaN,Single.PositiveInfinity,-1.01f,1.01f}) foreach(int channel in new[]{0,1,2,3})
        {r=new Runtime();if(channel==0)r.LX=value;if(channel==1)r.LY=value;if(channel==2)r.RX=value;if(channel==3)r.RY=value;check(!Read(r).Available,"Every signed paired source component refuses invalid raw bounds: "+channel);}
        foreach(float value in new[]{Single.NaN,Single.PositiveInfinity,-.01f,1.01f}) {r=new Runtime {Trigger=value};check(!Read(r).Available,"Observed right axis1X rejects invalid trigger without normalization");}
        var b=new Backend();var d=new RewiredFiveInputDiagnostic(()=>b,15,.2,"refuse");d.Tick(false,true,1,Sample(0),0);d.Tick(true,true,1,Sample(.01),.01);d.Tick(true,true,1,Sample(.02),.02);check(b.Adds==0,"Disabled state does not count toward three neutral owner updates");d.Tick(true,true,1,Sample(.03),.03);check(b.Adds==1,"Three fresh neutral pair reads admit one compiled five-channel backend");
        var moving=Sample(.1,2);moving.Left.X=.6;moving.Left.Y=-.4;moving.Right.X=-.6;moving.Right.Y=.4;moving.Right.Trigger=.7;d.Tick(true,true,1,moving,.1);
        check(b.Values.MoveX>0 && b.Values.MoveY<0 && b.Values.LookX<0 && b.Values.LookY>0 && b.Values.Trigger==.7 && Math.Abs(b.Expires-.6)<1e-9 && b.Readbacks==0,"Independent radial sticks and raw trigger retain signs with500ms expiry and no default readback queries");
        foreach(string fault in new[]{"left_identity","right_identity","left_role","left_model","right_signature","left_packet","right_packet","missing","old_query","generation","focus","clock"})
        {
            b=new Backend();d=new RewiredFiveInputDiagnostic(()=>b,15,.2,"refuse");Admit(d);moving=Sample(.1);ulong generation=1;bool focus=true;double now=.1;
            if(fault=="left_identity")moving.Left.Identity="replacement";if(fault=="right_identity")moving.Right.Identity="replacement";if(fault=="left_role")moving.Left.Role=ETrackedControllerRole.RightHand;if(fault=="left_model")moving.Left.Model="unknown";if(fault=="right_signature")moving.Right.Signature="2,3,0,0,0";if(fault=="left_packet")moving.Left.Packet=0;if(fault=="right_packet")moving.Right.Packet=0;if(fault=="missing")moving.Available=false;if(fault=="old_query")moving.StartedAt=moving.CompletedAt=-1;if(fault=="generation")generation=2;if(fault=="focus")focus=false;if(fault=="clock")now=.001;
            d.Tick(true,focus,generation,moving,now);check(d.Complete && b.Removes==1 && b.Values.MoveX==0 && b.Values.LookY==0 && b.Values.Trigger==0,"Any admitted hand/session/source fault releases all five channels: "+fault);d.Tick(true,true,1,Sample(.2),.2);check(b.Adds==1,"Completed pair never recreates or resubscribes: "+fault);
        }
        foreach(string held in new[]{"left_diagonal","right_diagonal","trigger","left_button","right_button"})
        {
            b=new Backend();d=new RewiredFiveInputDiagnostic(()=>b,15,.1,"refuse");moving=Sample(0);
            if(held=="left_diagonal")moving.Left.X=moving.Left.Y=.095;if(held=="right_diagonal")moving.Right.X=moving.Right.Y=.095;if(held=="trigger")moving.Right.Trigger=Double.Epsilon;if(held=="left_button")moving.Left.Pressed=1;if(held=="right_button")moving.Right.Pressed=1;
            for(int i=0;i<3;++i) {moving.StartedAt=moving.CompletedAt=i*.01;d.Tick(true,true,1,moving,i*.01);}check(b.Adds==0 && d.NeutralReads==0,"Both radial sticks, exact trigger zero and all pressed buttons protect initial admission: "+held);
        }
        b=new Backend();d=new RewiredFiveInputDiagnostic(()=>b,5,.2,"refuse",true);Admit(d);check(b.Readbacks==1,"Five-channel fixed readback captures once after creation when opted in");for(int i=3;i<50;++i)d.Tick(true,true,1,Sample(i*.01),(i*.01));check(b.Readbacks==1,"Repeated normal updates do not exceed1Hz public readback bound");d.Tick(true,true,1,Sample(6),6);check(d.Complete && b.Removes==1 && d.Reason=="window_deadline","Five-channel window ends and removes at bounded deadline");
        b=new Backend {Preserve=true};d=new RewiredFiveInputDiagnostic(()=>b,15,.2,"refuse");Admit(d);d.Cancel("stop",.1);check(d.Status=="cleanup_unconfirmed" && b.Removes==2,"Unknown ownership cleanup stays bounded and disclosed");
        foreach(bool addFault in new[]{true,false})
        {b=new Backend {ThrowAdd=addFault,ThrowSet=!addFault};d=new RewiredFiveInputDiagnostic(()=>b,15,.2,"refuse");Admit(d);check(d.Complete && b.Removes==1,"Partial creation or desired-state failure still performs bounded owned cleanup");}
        b=new Backend();d=new RewiredFiveInputDiagnostic(()=>b,15,.2,"refuse");d.Tick(true,true,1,Sample(0),0);d.Tick(true,true,1,Sample(0),.01);check(d.NeutralReads==0 && b.Adds==0,"Replayed query interval cannot become three fresh neutral reads");
        b=new Backend();d=new RewiredFiveInputDiagnostic(()=>b,15,.2,"refuse");moving=Sample(0,uint.MaxValue);d.Tick(true,true,1,moving,0);moving=Sample(.01,0);d.Tick(true,true,1,moving,.01);moving=Sample(.02,0);d.Tick(true,true,1,moving,.02);check(b.Adds==1,"Independent wrap-aware packet rules allow new neutral samples across packet wrap");
        moving=Sample(.03,0);d.Tick(true,true,1,moving,.03);check(!d.Complete,"Repeated SDK packets are permitted with freshly completed bounded queries");
        moving=Sample(.03,0);d.Tick(true,true,1,moving,.04);check(d.Complete && b.Removes==1,"An old complete query cannot refresh an admitted source expiry");
        Rewired.ReInput.Reset();double clock=0;var adapter=Adapter(()=>clock);adapter.Add();var device=Rewired.ReInput.controllers.CustomControllers[0];var values=new RewiredFiveValues {MoveX=.3,MoveY=-.4,LookX=-.5,LookY=.6,Trigger=.7};adapter.SetDesired(values,1);values.MoveX=.99;clock=.1;Rewired.ReInput.Pulse();var rb=adapter.CaptureReadback();
        check(device.Values.Length==5 && device.Values[0]==.3f && device.Values[4]==.7f && rb.PlayerAction29==device.Values[0] && rb.PlayerAction30==device.Values[1] && rb.PlayerAction31==device.Values[2] && rb.PlayerAction32==device.Values[3] && rb.PlayerAction36==device.Values[4] && rb.FailedFields==0,"Actual public fixture aggregates exactly ordered actions29/30/31/32/36 and copies desired struct");
        check(Rewired.ReInput.players.SystemPlayer.controllers.maps.Content[0].enabled && Rewired.ReInput.Subscribers==1,"Five-channel map uses the admitted owned enabled-child transition and one ordinary public callback");
        clock=1;Rewired.ReInput.Pulse();check(Array.TrueForAll(device.Values,v=>v==0) && adapter.FaultReason=="source_expired_or_clock_invalid","Callback expiry releases every intact owned channel");adapter.Remove();check(!adapter.NeedsCleanup && Rewired.ReInput.Data.Deletes==1,"Intact five-channel resources remove through exact owned public lifecycle");
        foreach(int channel in new[]{0,1,2,3,4}) foreach(string change in new[]{"definition_axis","runtime_axis","calibration","curve","identifier","map"})
        {
            Rewired.ReInput.Reset();clock=0;adapter=Adapter(()=>clock);adapter.Add();device=Rewired.ReInput.controllers.CustomControllers[0];adapter.SetDesired(new RewiredFiveValues {MoveX=.3,MoveY=.4,LookX=.5,LookY=.6,Trigger=.7},1);clock=.1;Rewired.ReInput.Pulse();
            if(change=="definition_axis")Rewired.ReInput.Data.Definitions[0].axes[channel].invert=true;if(change=="runtime_axis")device.Axes[channel].pollingDeadZone=.2f;if(change=="calibration")device.calibrationMap.Axes[channel].invert=true;if(change=="curve")device.calibrationMap.Axes[channel].sensitivityCurve.postWrapMode=1;if(change=="identifier")device.AxisElementIdentifiers[channel].name="changed";if(change=="map")Rewired.ReInput.players.SystemPlayer.controllers.maps.Content[0].Entries[channel].actionId=99;
            clock=1;Rewired.ReInput.Pulse();try {adapter.Remove();}catch {}
            check(adapter.NeedsCleanup && Rewired.ReInput.controllers.Destroys==0 && Rewired.ReInput.Data.Deletes==0,"Changes to any of five original channels preserve uncertain resources: "+channel+"/"+change);
            check(change=="map"?Array.TrueForAll(device.Values,v=>v==0):device.Values[4]==.7f,"Only intact original channels can receive release after changed mappings/configuration: "+channel+"/"+change);
        }
        Rewired.ReInput.Reset();clock=0;adapter=Adapter(()=>clock);adapter.Add();bool invalid=false;try {adapter.SetDesired(new RewiredFiveValues {MoveX=Double.NaN},1);}catch(ArgumentException){invalid=true;}check(invalid,"Caller cannot replace complete desired state with invalid values");adapter.Remove();Rewired.ReInput.Reset();
        foreach(string xmlFault in new[]{"missing","duplicate","numeric","nested"})
        {
            Rewired.ReInput.Reset();clock=0;Rewired.ControllerMap.XmlTransform=xml=>xmlFault=="missing"?xml.Replace("<enabled>false</enabled>",""):xmlFault=="duplicate"?xml.Replace("<enabled>false</enabled>","<enabled>false</enabled><enabled>false</enabled>"):xmlFault=="numeric"?xml.Replace("<enabled>false</enabled>","<enabled>0</enabled>"):xml.Replace("<enabled>false</enabled>","<wrapper><enabled>false</enabled></wrapper>");
            adapter=Adapter(()=>clock);bool refused=false;try {adapter.Add();}catch {refused=true;}check(refused && Rewired.ReInput.Subscribers==0,"Five-entry activation does not mask missing/duplicate/numeric/nested serialized enabled fields: "+xmlFault);try {adapter.Remove();}catch {}
        }
        Rewired.ReInput.Reset();clock=0;adapter=Adapter(()=>clock);adapter.Add();Rewired.Player.NonfiniteActionRead=true;rb=adapter.CaptureReadback();check(rb.PlayerAction29==null && rb.PlayerAction30==null && rb.PlayerAction31==null && rb.PlayerAction32==null && rb.PlayerAction36==null && rb.NonfiniteFields!=0,"Unavailable/nonfinite fixed five-action observations never become zero values");Rewired.Player.NonfiniteActionRead=false;adapter.Remove();Rewired.ReInput.Reset();
    }
}
