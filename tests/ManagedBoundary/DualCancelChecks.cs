using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Ugevr;
using Valve.VR;
using Hub=UnityEngine.InputSystem.InputSystem;
using Pad=UnityEngine.InputSystem.Gamepad;

static class DualCancelChecks
{
    sealed class Runtime : IDisposable
    {
        public string Fault="";
        public int States,Axes,Texts;
        public bool AxisBeforeState;
        public ulong RightPressed,LeftPressed;
        public float LeftX,LeftY,RightX,RightY;
        bool leftPolled,rightPolled;
        IVRSystem table;
        IntPtr pointer;
        public CVRSystem System;
        public Runtime()
        {
            table.GetTrackedDeviceIndexForControllerRole=delegate(ETrackedControllerRole role){return Fault=="duplicate_index"?1u:Fault=="final_index" && States>=2 && role==ETrackedControllerRole.LeftHand?3u:role==ETrackedControllerRole.LeftHand?1u:2u;};
            table.GetTrackedDeviceClass=delegate(uint i){return Fault=="class" || Fault=="final_class" && States>=2 && i==1?ETrackedDeviceClass.GenericTracker:ETrackedDeviceClass.Controller;};
            table.GetControllerRoleForTrackedDeviceIndex=delegate(uint i){return Fault=="role" && i==2?ETrackedControllerRole.LeftHand:i==1?ETrackedControllerRole.LeftHand:ETrackedControllerRole.RightHand;};
            table.GetStringTrackedDeviceProperty=delegate(uint i,ETrackedDeviceProperty property,StringBuilder text,uint size,ref ETrackedPropertyError error)
            {
                ++Texts;if(size!=257 || text.Capacity!=257)throw new Exception("Wrong public text ABI.");error=Fault=="text_error"?ETrackedPropertyError.TrackedProp_UnknownProperty:ETrackedPropertyError.TrackedProp_Success;
                bool model=property==ETrackedDeviceProperty.Prop_ModelNumber_String;
                string value=model?(i==1?OuterDualCancelSource.LeftModel:OuterDualCancelSource.RightModel):Fault=="duplicate_serial"?"shared-fixture":"private-fixture-"+i;
                if(model && Fault=="model")value="other";
                if(i==1 && States>=2 && Fault==(model?"final_model":"final_serial"))value+="changed";
                if(Fault!="empty_text")text.Append(value);
                return Fault=="oversize_text"?258u:Fault=="zero_required"?0u:(uint)text.Length+1;
            };
            table.GetControllerStateWithPose=delegate(ETrackingUniverseOrigin origin,uint i,ref VRControllerState_t state,uint size,ref TrackedDevicePose_t pose)
            {
                if(origin!=ETrackingUniverseOrigin.TrackingUniverseStanding || size!=(uint)Marshal.SizeOf(typeof(VRControllerState_t)))throw new Exception("Wrong joint-state ABI.");
                ++States;if(i==1)leftPolled=true;else rightPolled=true;
                state=new VRControllerState_t {unPacketNum=(uint)States,ulButtonPressed=i==1?LeftPressed:RightPressed,rAxis0=new VRControllerAxis_t {x=i==1?LeftX:RightX,y=i==1?LeftY:RightY}};
                pose=new TrackedDevicePose_t {bDeviceIsConnected=Fault!="disconnected",bPoseIsValid=Fault!="invalid_pose",mDeviceToAbsoluteTracking=new HmdMatrix34_t {m0=1,m5=1,m10=1}};
                if(Fault=="nonfinite_pose")pose.mDeviceToAbsoluteTracking.m3=float.NaN;
                if(Fault=="nonfinite_axis")state.rAxis4.y=float.PositiveInfinity;
                if(Fault=="bounded_axis")state.rAxis0.x=1.1f;
                return Fault!="state_false";
            };
            table.GetInt32TrackedDeviceProperty=delegate(uint i,ETrackedDeviceProperty property,ref ETrackedPropertyError error)
            {
                ++Axes;bool polled=i==1?leftPolled:rightPolled;if(!polled)AxisBeforeState=true;
                error=!polled || Fault=="metadata_error"?ETrackedPropertyError.TrackedProp_UnknownProperty:ETrackedPropertyError.TrackedProp_Success;
                int axis=(int)property-(int)ETrackedDeviceProperty.Prop_Axis0Type_Int32;
                return Fault=="signature" || Fault=="final_metadata" && i==1 && States>=2?0:axis==0?2:axis<3?3:0;
            };
            Bind();
        }
        public void Bind()
        {
            if(pointer!=IntPtr.Zero){Marshal.DestroyStructure(pointer,typeof(IVRSystem));Marshal.FreeHGlobal(pointer);}
            var t=table;if(Fault=="missing_state")t.GetControllerStateWithPose=null;if(Fault=="missing_metadata")t.GetInt32TrackedDeviceProperty=null;if(Fault=="missing_text")t.GetStringTrackedDeviceProperty=null;
            pointer=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(IVRSystem)));Marshal.StructureToPtr(t,pointer,false);System=new CVRSystem(pointer);
        }
        public void Dispose(){GC.KeepAlive(table);Marshal.DestroyStructure(pointer,typeof(IVRSystem));Marshal.FreeHGlobal(pointer);}
    }
    static PublicOwnedGamepad Backend(bool cancel=true)
    {return new PublicOwnedGamepad(Assembly.GetExecutingAssembly(),false,OuterGamepadCompatibilityProfile.LayoutPrefix,OwnedGamepadButton.West,true,cancel);}
    static LegacyCancelPairSample Sample(uint packet=10)
    {
        return new LegacyCancelPairSample {LeftModel=OuterDualCancelSource.LeftModel,RightModel=OuterDualCancelSource.RightModel,LeftSignature=OuterDualCancelSource.Signature,RightSignature=OuterDualCancelSource.Signature,
            Pair=new LegacyPairSample {Available=true,Left=new LegacyHandSample {Available=true,Index=1,Identity="left-fixture",Role=ETrackedControllerRole.LeftHand,Axis0Type=2,Packet=packet},Right=new LegacyHandSample {Available=true,Index=2,Identity="right-fixture",Role=ETrackedControllerRole.RightHand,Axis0Type=2,Packet=packet}}};
    }
    static OpenVrDualCancelGamepadDiagnostic Policy(string conflict="refuse") {return new OpenVrDualCancelGamepadDiagnostic(()=>Backend(),5,.2,conflict);}
    static void Admit(OpenVrDualCancelGamepadDiagnostic p,LegacyCancelPairSample s) {p.Tick(true,true,1,s,0);p.Tick(true,true,1,s,.1);p.Tick(true,true,1,s,.2);}
    public static void Run(Action<bool,string> check)
    {
        using(var runtime=new Runtime())
        {
            var s=OuterDualCancelSource.ReadSafely(runtime.System);
            check(s.Pair.Available && runtime.States==2 && runtime.Axes==20 && runtime.Texts==12 && !runtime.AxisBeforeState,"Actual vendored cancel reader performs both joint polls before metadata and independently repeats all five pins and identities");
            Hub.Reset();var policy=Policy();
            for(int i=0;i<3;++i)policy.Tick(true,true,1,OuterDualCancelSource.ReadSafely(runtime.System),i*.1);
            check(policy.Status=="active" && Hub.Adds==1,"Actual neutral SDK pair admits exactly one owned cancel device after three reads");
            runtime.RightPressed=2;runtime.LeftPressed=0;runtime.LeftX=.35f;runtime.LeftY=.6f;runtime.RightX=-.6f;runtime.RightY=.4f;
            policy.Tick(true,true,1,OuterDualCancelSource.ReadSafely(runtime.System),.3);
            check(Hub.Pending.Count==1 && Hub.Pending[0].Item2.buttons==32u && !Pad.current.buttonEast.isPressed,"Received SDK B2 queues one complete typed GPAD East32 event without premature processed-state acknowledgement");
            Hub.NormalUpdate();var owned=Pad.current;
            check(owned.buttonEast.isPressed && !owned.buttonWest.isPressed && !owned.buttonSouth.isPressed && owned.leftStick.Value.x>0 && owned.leftStick.Value.y>0 && owned.rightStick.Value.x<0 && owned.rightStick.Value.y>0,"Normal engine update exposes independent East, left movement and right look, without South or a keyboard write");
            runtime.RightPressed=130;policy.Tick(true,true,1,OuterDualCancelSource.ReadSafely(runtime.System),.4);Hub.NormalUpdate();
            check(owned.buttonEast.isPressed && owned.buttonWest.isPressed && policy.AppliedSamples==1,"A and B coexist independently and acknowledgement covers previous East and both sticks");
            runtime.RightPressed=128;policy.Tick(true,true,1,OuterDualCancelSource.ReadSafely(runtime.System),.5);Hub.NormalUpdate();
            check(!owned.buttonEast.isPressed && owned.buttonWest.isPressed,"Releasing B retains A/West through a single whole-state event");
            runtime.RightPressed=2;policy.Tick(true,true,1,OuterDualCancelSource.ReadSafely(runtime.System),.6);Hub.NormalUpdate();
            check(owned.buttonEast.isPressed && !owned.buttonWest.isPressed,"Independent West release retains East");
            runtime.RightPressed=0;runtime.LeftX=runtime.LeftY=runtime.RightX=runtime.RightY=0;policy.Tick(true,true,1,OuterDualCancelSource.ReadSafely(runtime.System),.7);Hub.NormalUpdate();
            check(!owned.buttonEast.isPressed && !owned.buttonWest.isPressed && owned.leftStick.Value.x==0 && owned.rightStick.Value.x==0,"Complete source release returns both buttons and sticks to neutral without touching the original keyboard");
            policy.Tick(false,true,1,Sample(),.8);check(!policy.ReleaseObserved && policy.Status=="releasing","Disable queues neutral before reporting processed release");Hub.NormalUpdate();policy.Tick(false,true,1,Sample(),.9);
            check(policy.Complete && policy.ReleaseObserved && policy.RemoveAttempts==1 && Hub.Devices.Count==1 && Hub.Devices.Contains(Hub.Keyboard),"A later ordinary update confirms East/West/stick release before exact owned removal");
            policy.Tick(true,true,1,Sample(),1);check(Hub.Adds==1,"Ended cancel window never creates another device");
        }
        foreach(string fault in new[]{"duplicate_index","class","role","model","duplicate_serial","text_error","empty_text","oversize_text","zero_required","state_false","invalid_pose","disconnected","nonfinite_pose","nonfinite_axis","bounded_axis","metadata_error","signature","final_index","final_class","final_model","final_serial","final_metadata","missing_state","missing_metadata","missing_text"})
        {
            using(var runtime=new Runtime()){runtime.Fault=fault;runtime.Bind();Hub.Reset();var policy=Policy();var s=OuterDualCancelSource.ReadSafely(runtime.System);Admit(policy,s);check(!s.Pair.Available && policy.AddAttempts==0 && Hub.Queues==0,"Rejected actual SDK source cannot add or forward cancel: "+fault);}
        }
        foreach(string held in new[]{"left_other","right_other","b","diagonal"})
        {
            Hub.Reset();var s=Sample();var p=Policy();p.Tick(true,true,1,s,0);
            if(held=="left_other")s.Pair.Left.Pressed=1UL<<63;if(held=="right_other")s.Pair.Right.Pressed=1UL<<63;if(held=="b")s.Pair.Right.Pressed=2;if(held=="diagonal"){s.Pair.Left.X=.15;s.Pair.Left.Y=.15;}
            p.Tick(true,true,1,s,.1);check(p.NeutralReads==0 && p.AddAttempts==0,"Held source or radial nonneutral state resets cancel admission: "+held);
            s=Sample();p.Tick(true,true,1,s,.2);p.Tick(true,true,1,s,.3);check(p.AddAttempts==0,"Two post-reset reads do not silently satisfy three-neutral admission");p.Tick(true,true,1,s,.4);check(p.AddAttempts==1,"Three new neutral reads admit after held source release");p.Cancel("end",.5);Hub.NormalUpdate();
        }
        foreach(string change in new[]{"model","signature","index","identity","role","generation","packet","focus","disabled","clock"})
        {
            Hub.Reset();var s=Sample();var p=Policy();p.Tick(true,true,1,s,0);
            if(change=="model")s.LeftModel="other";if(change=="signature")s.RightSignature=0;if(change=="index")s.Pair.Left.Index=4;if(change=="identity")s.Pair.Left.Identity="new";if(change=="role")s.Pair.Right.Role=ETrackedControllerRole.LeftHand;if(change=="packet")s.Pair.Right.Packet=9;
            p.Tick(change!="disabled",change!="focus",change=="generation"?2UL:1UL,s,change=="clock"?-.1:.1);
            check(p.AddAttempts==0 && p.NeutralReads<=1,"Admission requires a new consecutive stable context after change: "+change);p.Cancel("end",.2);
        }
        Hub.Reset();var wrap=Policy();var sample=Sample(uint.MaxValue);wrap.Tick(true,true,1,sample,0);sample=Sample(0);wrap.Tick(true,true,1,sample,.1);sample=Sample(1);wrap.Tick(true,true,1,sample,.2);check(wrap.Status=="active","Normal unsigned packet wrap is admitted");wrap.Cancel("end",.3);Hub.NormalUpdate();
        foreach(string change in new[]{"source","model","signature","identity","index","role","packet","generation","focus","disabled","clock","deadline"})
        {
            Hub.Reset();sample=Sample();var p=Policy();Admit(p,sample);sample.Pair.Right.Pressed=2;p.Tick(true,true,1,sample,.3);Hub.NormalUpdate();
            if(change=="source")sample.Pair.Available=false;if(change=="model")sample.LeftModel="different";if(change=="signature")sample.RightSignature=0;if(change=="identity")sample.Pair.Left.Identity="other";if(change=="index")sample.Pair.Right.Index=3;if(change=="role")sample.Pair.Left.Role=ETrackedControllerRole.RightHand;if(change=="packet")sample.Pair.Right.Packet=9;
            p.Tick(change!="disabled",change!="focus",change=="generation"?2UL:1UL,sample,change=="deadline"?5.3:change=="clock"?.1:.4);Hub.NormalUpdate();p.Tick(false,true,1,sample,5.4);
            check(p.Complete && Hub.Adds==1 && Hub.Devices.Count==1 && Hub.Pending.Count==0,"Active source/clock/window loss releases both buttons and removes only owned resources: "+change);
        }
        Hub.Reset();var backend=Backend();backend.Add(false);backend.QueueCancel(false,true,0,0,0,0);Hub.NormalUpdate();var current=Pad.current;
        var foreign=new Pad {added=true,enabled=true,layout="physical"};Hub.Devices.Add(foreign);foreign.MakeCurrent();bool rejected=false;try {backend.QueueCancel(false,true,0,0,0,0);}catch {rejected=true;}
        check(rejected && Hub.Pending.Count==0,"East-only state is nonneutral and cannot bypass external Current conflict");backend.QueueCancel(false,false,0,0,0,0);Hub.NormalUpdate();foreign.MakeCurrent();backend.Remove();check(Object.ReferenceEquals(Pad.current,foreign) && Hub.Devices.Contains(foreign),"Full neutral release and removal preserve external Current");
        Hub.Reset();backend=Backend(false);backend.Add(false);Pad.current.ThrowEastRead=true;backend.QueueDual(true,0,0,0,0);check(backend.ReadDual().Pad.Added,"Old dual backend does not resolve or read additional East control");Pad.current.ThrowEastRead=false;Hub.NormalUpdate();backend.Remove();
        Hub.Reset();var policyFailure=Policy();Admit(policyFailure,Sample());Pad.current.ThrowEastRead=true;policyFailure.Tick(true,true,1,Sample(),.3);check(policyFailure.Complete && !policyFailure.ReleaseObserved && policyFailure.Reason=="public_queue_or_read_failed","Unavailable East readback ends input without inventing processed neutral release");Hub.NormalUpdate();
        Hub.Reset();var stuck=Policy();Admit(stuck,Sample());sample=Sample();sample.Pair.Right.Pressed=2;stuck.Tick(true,true,1,sample,.3);Hub.NormalUpdate();stuck.Pause("session_disabled",.4);Hub.NormalUpdate();Pad.current.buttonEast.isPressed=true;stuck.Tick(false,true,1,sample,.5);
        check(!stuck.ReleaseObserved && !stuck.Complete && stuck.RemoveAttempts==0,"East still pressed blocks complete neutral release even when West and both sticks are zero");stuck.Tick(false,true,1,sample,2.5);Hub.NormalUpdate();check(stuck.Complete && !stuck.ReleaseObserved && stuck.ReleaseAttempts==2 && stuck.RemoveAttempts==1,"Release deadline bounds East-stuck cleanup without claiming processed release");
        Hub.Reset();var graceful=Policy();Admit(graceful,Sample());sample=Sample();sample.Pair.Right.Pressed=130;graceful.Tick(true,true,1,sample,.3);Hub.NormalUpdate();graceful.Pause("session_disabled",.4);check(!graceful.Complete && !graceful.ReleaseObserved,"Ordinary reversible pause allows the engine to process full neutral before removal");Hub.NormalUpdate();graceful.Tick(false,true,2,sample,.5);check(graceful.Complete && graceful.ReleaseObserved,"Ordinary paused policy drains processed East/West/stick state without requiring a fresh SDK input");
        Hub.Reset();var waiting=Policy();waiting.Tick(true,true,1,Sample(),0);waiting.Pause("session_disabled",.1);check(!waiting.Complete && waiting.NeutralReads==0 && waiting.AddAttempts==0,"Pre-window pause consumes no owned input window");waiting.Tick(true,true,2,Sample(),.2);waiting.Tick(true,true,2,Sample(),.3);waiting.Tick(true,true,2,Sample(),.4);check(waiting.Status=="active" && Hub.Adds==1,"Explicit enable can admit the first window after a prior pause");waiting.Cancel("lifecycle",.5);Hub.NormalUpdate();
        Hub.Reset();var takeover=Policy();Admit(takeover,Sample());sample=Sample();sample.Pair.Right.Pressed=2;takeover.Tick(true,true,1,sample,.3);Hub.NormalUpdate();foreign=new Pad {added=true,enabled=true,layout="physical"};Hub.Devices.Add(foreign);foreign.MakeCurrent();takeover.Tick(true,true,1,sample,.4);Hub.NormalUpdate();check(takeover.Complete && !takeover.ReleaseObserved && Object.ReferenceEquals(Pad.current,foreign) && Hub.Devices.Contains(foreign),"Active East source ends on external Current takeover and cannot reselect the removed owned device");
        Hub.Reset();var failure=Policy();Admit(failure,Sample());Hub.RemoveAlwaysThrow=true;failure.Cancel("lifecycle",.3);check(failure.Complete && failure.Status=="cleanup_unconfirmed" && failure.RemoveAttempts==2,"Cancel-profile removal failures stay bounded and explicitly unconfirmed");Hub.Reset();
        var prior=new Pad {added=true,enabled=true,layout="physical"};Hub.Devices.Add(prior);prior.MakeCurrent();var refusedPolicy=Policy();Admit(refusedPolicy,Sample());check(refusedPolicy.Complete && refusedPolicy.Queues==0 && Hub.Adds==0 && Object.ReferenceEquals(Pad.current,prior),"Default conflict refusal does not create a cancel device or change a physical current gamepad");
        var preferred=Policy("prefer-owned");Admit(preferred,Sample());sample=Sample();sample.Pair.Right.Pressed=2;preferred.Tick(true,true,1,sample,.3);Hub.NormalUpdate();preferred.Pause("session_disabled",.4);Hub.NormalUpdate();preferred.Tick(false,true,1,sample,.5);check(preferred.Complete && preferred.ReleaseObserved && Object.ReferenceEquals(Pad.current,prior) && Hub.Devices.Contains(prior),"Explicit prefer-owned cancel restores the still-added prior current only after processed neutral removal");
        Hub.Reset();Hub.AddThenThrow=true;var partial=Policy();Admit(partial,Sample());Hub.NormalUpdate();check(partial.Complete && partial.AddAttempts==1 && partial.Queues==0 && Hub.Devices.Count==1 && Hub.DeviceObservers==0 && Hub.LayoutObservers==0,"Partial public creation failure cleans only its proven owned outcome and cannot forward B");
        Hub.Reset();var queuedFault=Policy();Admit(queuedFault,Sample());sample=Sample();sample.Pair.Right.Pressed=2;Hub.QueueThenThrow=true;queuedFault.Tick(true,true,1,sample,.3);Hub.QueueThenThrow=false;Hub.NormalUpdate();check(queuedFault.Complete && !queuedFault.ReleaseObserved && Hub.Devices.Count==1 && queuedFault.Reason=="public_queue_or_read_failed","Thrown public queue after an effect ends the window and cannot pretend its removed device processed neutral");
        foreach(bool activation in new[]{false,true})
        {
            Hub.Reset();int reads=0;var binding=new GamepadDiagnosticBinding(activation,true,false,false,OuterGamepadCompatibilityProfile.DualCancelName,5,.2,"refuse",3,()=>throw new Exception("old factory"),()=>throw new Exception("old factory"),()=>throw new Exception("old read"),()=>throw new Exception("old read"),()=>Backend(),()=>{++reads;return Sample();});
            binding.Tick(true,true,1,0);binding.Tick(true,true,1,.1);binding.Tick(true,true,1,.2);
            check(activation?binding.CancelDual!=null && binding.Dual==null && binding.Single==null && reads==3 && Hub.Adds==1:binding.CancelDual==null && reads==0 && Hub.Adds==0,"Explicit cancel selection exclusively routes its backend; default off has zero work");binding.Cancel("end",.3);Hub.NormalUpdate();
        }
        Hub.Reset();bool wrongNeutral=false;try {new GamepadDiagnosticBinding(true,true,false,false,OuterGamepadCompatibilityProfile.DualCancelName,5,.2,"refuse",2,null,null,null,null,()=>Backend(),()=>Sample());}catch(ArgumentException){wrongNeutral=true;}check(wrongNeutral && Hub.Adds==0,"Cancel profile rejects configured neutral count other than exactly three");
        foreach(string conflict in new[]{"session","rewired","engine"})
        {
            bool refused=false;try {new GamepadDiagnosticBinding(true,conflict!="session",conflict=="rewired",conflict=="engine",OuterGamepadCompatibilityProfile.DualCancelName,5,.2,"refuse",3,null,null,null,null,()=>Backend(),()=>Sample());}catch(ArgumentException){refused=true;}check(refused && Hub.Adds==0,"Explicit cancel activation refuses missing session or another input experiment: "+conflict);
        }
        int foreignReads=0;var threadBinding=new GamepadDiagnosticBinding(true,true,false,false,OuterGamepadCompatibilityProfile.DualCancelName,5,.2,"refuse",3,null,null,null,null,()=>Backend(),()=>{++foreignReads;return Sample();});var worker=new Thread(()=>{threadBinding.Tick(true,true,1,0);threadBinding.Pause("foreign",0);threadBinding.Cancel("foreign",0);});worker.Start();worker.Join();check(foreignReads==0 && Hub.Adds==0 && threadBinding.LastError=="owner_thread_required","Foreign thread cannot poll, add, queue or tear down cancel input");threadBinding.Cancel("end",.1);
        check(!OuterGamepadCompatibilityProfile.Supports(OuterGamepadCompatibilityProfile.DualCancelName) && !OuterGamepadCompatibilityProfile.SupportsDual(OuterGamepadCompatibilityProfile.DualCancelName) && !OuterGamepadCompatibilityProfile.ReadDualCancelSafely(null,OuterGamepadCompatibilityProfile.DualName).Pair.Available,"No legacy profile silently gains the observed B mapping");
        Hub.Reset();
    }
}
