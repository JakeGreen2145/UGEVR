using System;
using System.Runtime.InteropServices;
using System.Text;
using Ugevr;
using Valve.VR;

static class ObservedRewiredChecks
{
    sealed class Runtime
    {
        public string Fault="";
        public int States,Metadata,SerialReads,ModelReads;
        public bool Primed;
        public float X,Y,Trigger,OtherTrigger=.9f;
        public int[] Signature={2,3,3,0,0};
        public IVRSystem Table()
        {
            var t=new IVRSystem();
            t.GetTrackedDeviceIndexForControllerRole=delegate(ETrackedControllerRole role) {return Fault=="initial_index"?64u:Fault=="changed_index" && States>0?4u:3u;};
            t.GetTrackedDeviceClass=delegate(uint index) {return Fault=="initial_class" || Fault=="changed_class" && States>0?ETrackedDeviceClass.HMD:ETrackedDeviceClass.Controller;};
            t.GetControllerRoleForTrackedDeviceIndex=delegate(uint index) {return Fault=="initial_role" || Fault=="changed_role" && States>0?ETrackedControllerRole.LeftHand:ETrackedControllerRole.RightHand;};
            t.GetStringTrackedDeviceProperty=delegate(uint index,ETrackedDeviceProperty property,StringBuilder value,uint capacity,ref ETrackedPropertyError error)
            {
                if(Fault=="throw_initial_identity") throw new InvalidOperationException("Controlled SDK text exception.");
                bool model=property==ETrackedDeviceProperty.Prop_ModelNumber_String;
                if(!model && property!=ETrackedDeviceProperty.Prop_SerialNumber_String || capacity!=257 || index!=3) throw new Exception("Wrong identity call.");
                if(model) ++ModelReads;else ++SerialReads;
                error=Fault=="identity_error" || Fault=="initial_identity_unknown" && States==0 || Fault=="bootstrap_unknown" && !Primed?ETrackedPropertyError.TrackedProp_UnknownProperty:ETrackedPropertyError.TrackedProp_Success;
                value.Append(Fault=="empty_identity"?"":model?Fault=="bootstrap_model" && !Primed?"Deckard MP":Fault=="wrong_model" || Fault=="changed_model" && States>0?"Oculus Quest 2 (Right Controller)":OpenVrRewiredRightReader.ObservedTriggerModel:Fault=="changed_serial" && States>0?"replacement":"app-view-private-serial");
                return Fault=="zero_count"?0u:Fault=="oversized_count"?258u:32u;
            };
            t.GetControllerStateWithPose=delegate(ETrackingUniverseOrigin origin,uint index,ref VRControllerState_t state,uint size,ref TrackedDevicePose_t pose)
            {
                if(Fault=="throw_joint") throw new InvalidOperationException("Controlled SDK joint exception.");
                if(origin!=ETrackingUniverseOrigin.TrackingUniverseStanding || index!=3 || size!=(uint)Marshal.SizeOf(typeof(VRControllerState_t))) throw new Exception("Wrong joint ABI.");
                ++States;Primed=true;state=new VRControllerState_t {unPacketNum=17,ulButtonPressed=1UL<<33,rAxis0=new VRControllerAxis_t {x=X,y=Y},rAxis1=new VRControllerAxis_t {x=Trigger},rAxis2=new VRControllerAxis_t {x=OtherTrigger},rAxis3=new VRControllerAxis_t {x=.8f},rAxis4=new VRControllerAxis_t {x=.7f}};
                pose=new TrackedDevicePose_t {bDeviceIsConnected=Fault!="disconnected",bPoseIsValid=Fault!="invalid_pose",mDeviceToAbsoluteTracking=new HmdMatrix34_t {m0=1,m5=1,m10=1,m11=Fault=="nonfinite_pose"?float.NaN:0}};return Fault!="state_false";
            };
            t.GetInt32TrackedDeviceProperty=delegate(uint index,ETrackedDeviceProperty property,ref ETrackedPropertyError error)
            {
                if(States!=1 || SerialReads!=1 && SerialReads!=2 || ModelReads!=1 && ModelReads!=2) throw new Exception("Signature must follow bracketed initial identity and joint read.");
                ++Metadata;error=Fault=="metadata_unknown"?ETrackedPropertyError.TrackedProp_UnknownProperty:ETrackedPropertyError.TrackedProp_Success;
                int axis=(int)property-(int)ETrackedDeviceProperty.Prop_Axis0Type_Int32;return Fault=="metadata_changed" && Metadata>5?0:Signature[axis];
            };
            return t;
        }
    }
    static RewiredRightSample Read(Runtime r,string profile=null)
    {
        r.States=r.Metadata=r.SerialReads=r.ModelReads=0;
        var table=r.Table();var pointer=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(IVRSystem)));
        try {Marshal.StructureToPtr(table,pointer,false);var sample=OpenVrRewiredRightReader.Read(new CVRSystem(pointer),profile??PublicOwnedRewiredInput.ObservedTriggerProfile);GC.KeepAlive(table);return sample;}
        finally {Marshal.FreeHGlobal(pointer);}
    }
    public static void Run(Action<bool,string> check)
    {
        var r=new Runtime {X=.35f,Y=.6f,Trigger=.5984252095f};var sample=Read(r);
        check(sample.Available && sample.TriggerAxis==1 && sample.Trigger==r.Trigger && sample.X==r.X && sample.Y==r.Y && r.States==1 && r.Metadata==10 && r.SerialReads==2 && r.ModelReads==2,"Explicit observed trigger profile brackets the joint read with independently returned serial/model and repeated exact successful metadata");
        check(sample.Trigger!=r.OtherTrigger,"The independently observed finger channel is axis1 X despite another declared Trigger axis and other raw values");
        check(PublicOwnedRewiredInput.SupportsProfile(PublicOwnedRewiredInput.Profile) && PublicOwnedRewiredInput.SupportsProfile(PublicOwnedRewiredInput.ObservedTriggerProfile) && !PublicOwnedRewiredInput.SupportsProfile("none") && !OpenVrRewiredRightReader.Read(null,"none").Available,"Profile helper supports only explicit routes and unsupported selection performs no SDK query");
        foreach(string fault in new[]{"initial_index","initial_class","initial_role","identity_error","initial_identity_unknown","empty_identity","zero_count","oversized_count","wrong_model","state_false","disconnected","invalid_pose","nonfinite_pose","metadata_unknown","metadata_changed","changed_index","changed_class","changed_role","changed_serial","changed_model"})
        {
            r=new Runtime {Fault=fault};sample=Read(r);check(!sample.Available && sample.Trigger==0 && sample.X==0 && sample.Y==0,"Observed semantic profile cannot publish rejected output: "+fault);
            if(fault=="initial_index" || fault=="initial_class" || fault=="initial_role") check(r.States==0 && r.Metadata==0,"Invalid initial public context cannot poll a foreign or unavailable device: "+fault);
            if(fault=="initial_identity_unknown" || fault=="wrong_model" || fault=="identity_error" || fault=="empty_identity" || fault=="zero_count" || fault=="oversized_count") check(r.States==1 && r.Metadata==0,"Role-scoped bootstrap poll cannot promote a failed initial identity/model bracket: "+fault);
            if(fault=="state_false" || fault=="disconnected" || fault=="invalid_pose" || fault=="nonfinite_pose") check(r.Metadata==0 && r.SerialReads==1 && r.ModelReads==1,"Invalid joint state never becomes post-poll metadata authorization: "+fault);
        }
        foreach(string bootstrap in new[]{"bootstrap_unknown","bootstrap_model"})
        {r=new Runtime {Fault=bootstrap};sample=Read(r);check(!sample.Available && r.States==1 && r.Metadata==0 && r.SerialReads==1 && r.ModelReads==1,"First bootstrap joint poll enables properties without forwarding initial unknown/different model: "+bootstrap);sample=Read(r);check(sample.Available && r.SerialReads==2 && r.ModelReads==2 && r.Metadata==10,"Next stable read independently admits both successful sides after role-scoped bootstrap: "+bootstrap);}
        foreach(string fault in new[]{"throw_initial_identity","throw_joint"}) {r=new Runtime {Fault=fault};bool threw=false;try {Read(r);}catch(InvalidOperationException) {threw=true;}check(threw && r.States==0 && r.Metadata==0,"SDK exceptions propagate to the existing caller refusal without bootstrap forwarding: "+fault);}
        for(int axis=0;axis<5;++axis) {r=new Runtime();r.Signature[axis]=r.Signature[axis]==0?3:0;check(!Read(r).Available,"Every successful declared axis kind is pinned for the observed profile: "+axis);}
        foreach(float invalid in new[]{float.NaN,float.PositiveInfinity,-.01f,1.001f}) {r=new Runtime {Trigger=invalid};check(!Read(r).Available,"Raw finger trigger must be finite in [0,1] without clamp/normalization");}
        foreach(float invalid in new[]{float.NaN,float.NegativeInfinity,-1.001f,1.001f}) {r=new Runtime {X=invalid};check(!Read(r).Available,"Mapped stick X must be finite in [-1,1]");r=new Runtime {Y=invalid};check(!Read(r).Available,"Mapped stick Y must be finite in [-1,1]");}
        foreach(float boundary in new[]{0f,1f}) {r=new Runtime {Trigger=boundary};sample=Read(r);check(sample.Available && sample.Trigger==boundary,"Observed finger trigger retains exact permitted endpoint");}
        // Original route still rejects the duplicated declarations before serial publication.
        var oldTable=new IVRSystem();oldTable.GetTrackedDeviceIndexForControllerRole=delegate {return 3;};oldTable.GetTrackedDeviceClass=delegate {return ETrackedDeviceClass.Controller;};oldTable.GetControllerRoleForTrackedDeviceIndex=delegate {return ETrackedControllerRole.RightHand;};
        oldTable.GetControllerStateWithPose=delegate(ETrackingUniverseOrigin o,uint i,ref VRControllerState_t s,uint n,ref TrackedDevicePose_t p) {p=new TrackedDevicePose_t {bDeviceIsConnected=true,bPoseIsValid=true};return true;};
        oldTable.GetInt32TrackedDeviceProperty=delegate(uint i,ETrackedDeviceProperty p,ref ETrackedPropertyError e) {e=ETrackedPropertyError.TrackedProp_Success;return p==ETrackedDeviceProperty.Prop_Axis0Type_Int32?2:3;};
        var ptr=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(IVRSystem)));try {Marshal.StructureToPtr(oldTable,ptr,false);sample=OpenVrRewiredRightReader.Read(new CVRSystem(ptr),PublicOwnedRewiredInput.Profile);GC.KeepAlive(oldTable);check(!sample.Available && sample.UnavailableReason=="trigger_axis_ambiguous","Existing unique-trigger route retains its duplicate refusal through the new overload");}finally {Marshal.FreeHGlobal(ptr);}
        Rewired.ReInput.Reset();double now=0;var backend=new PublicOwnedRewiredInput(typeof(Rewired.ReInput).Assembly,new Rewired.InputManager_Base(),()=>now,()=>true,false);var d=new RewiredInputDiagnostic(()=>backend,15,.2,"refuse");r=new Runtime();sample=Read(r);d.Tick(true,true,1,sample,now);check(d.Status=="active" && d.AddAttempts==1,"A fresh exact zero observed source admits the existing ownership policy");
        var device=Rewired.ReInput.controllers.CustomControllers[0];now=.1;r.Trigger=.5984252095f;r.X=.6f;sample=Read(r);d.Tick(true,true,1,sample,now);Rewired.ReInput.Pulse();check(Math.Abs(device.Values[0]-.5)<1e-6 && device.Values[2]==r.Trigger && backend.AppliedUpdates>0,"Observed raw axis1 feeds only ordinary public look and trigger mapped channels at the engine input event");
        now=.2;r.Fault="changed_model";sample=Read(r);d.Tick(true,true,1,sample,now);check(d.Complete && device.Values[0]==0 && device.Values[2]==0 && Rewired.ReInput.controllers.CustomControllers.Count==0 && Rewired.ReInput.Subscribers==0,"Later model disagreement terminates and releases only the admitted public controller");
        Rewired.ReInput.Reset();backend=new PublicOwnedRewiredInput(typeof(Rewired.ReInput).Assembly,new Rewired.InputManager_Base(),()=>now,()=>true,false);d=new RewiredInputDiagnostic(()=>backend,15,.2,"refuse");d.Tick(true,true,1,new RewiredRightSample {UnavailableReason="passive_snapshot_only"},now);check(d.AddAttempts==0 && Rewired.ReInput.controllers.CustomControllers.Count==0,"Passive records without a newly bracketed Available sample cannot authorize runtime creation");
    }
}
