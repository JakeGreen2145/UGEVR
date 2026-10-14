using System;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Text;
using Ugevr;
using Valve.VR;

static class LegacyControllerChecks
{
    sealed class Runtime
    {
        public int AxisKind=2,StateCalls,RoleCalls,AxisCalls,StringCalls;
        public string Serial="fixture",Model="fixture-model";
        public bool StringError,IdentityChange,ModelError,ModelChange,NonfinitePose,MissingStringReader;
        public ETrackedPropertyError AxisError=ETrackedPropertyError.TrackedProp_Success;
        public bool PoseValid=true,Connected=true,RoleChange,StateSuccess=true,PropertyError;
        public ulong Pressed=1UL<<7,Touched;
        public uint StringLength=8;
        public float X=0.3f,Y=-0.4f;
        public IVRSystem Table()
        {
            var t=new IVRSystem();
            t.GetTrackedDeviceIndexForControllerRole=delegate(ETrackedControllerRole role) {++RoleCalls;return RoleChange && RoleCalls>1?4u:3u;};
            t.GetControllerRoleForTrackedDeviceIndex=delegate(uint index) {return ETrackedControllerRole.RightHand;};
            t.GetTrackedDeviceClass=delegate(uint index) {return ETrackedDeviceClass.Controller;};
            t.GetInt32TrackedDeviceProperty=delegate(uint index,ETrackedDeviceProperty property,ref ETrackedPropertyError error)
            {++AxisCalls;error=PropertyError?ETrackedPropertyError.TrackedProp_UnknownProperty:AxisError;return AxisKind;};
            t.GetStringTrackedDeviceProperty=delegate(uint index,ETrackedDeviceProperty property,StringBuilder value,uint capacity,ref ETrackedPropertyError error)
            {
                ++StringCalls;bool model=property==ETrackedDeviceProperty.Prop_ModelNumber_String;
                error=StringError || (model && ModelError)?ETrackedPropertyError.TrackedProp_UnknownProperty:ETrackedPropertyError.TrackedProp_Success;
                value.Append(model?(ModelChange && StateCalls>0?"replacement-model":Model):(IdentityChange && StateCalls>0?"replacement":Serial));return StringLength;
            };
            t.GetControllerStateWithPose=delegate(ETrackingUniverseOrigin origin,uint index,ref VRControllerState_t state,uint size,ref TrackedDevicePose_t pose)
            {
                ++StateCalls;if(size!=(uint)Marshal.SizeOf(typeof(VRControllerState_t)) || origin!=ETrackingUniverseOrigin.TrackingUniverseStanding) throw new Exception("Incorrect public ABI arguments.");
                state=new VRControllerState_t {unPacketNum=17,ulButtonPressed=Pressed,ulButtonTouched=Touched,rAxis0=new VRControllerAxis_t {x=X,y=Y}};
                pose=new TrackedDevicePose_t {bDeviceIsConnected=Connected,bPoseIsValid=PoseValid,mDeviceToAbsoluteTracking=new HmdMatrix34_t {m0=1,m5=1,m10=1,m3=NonfinitePose?float.NaN:0}};return StateSuccess;
            };if(MissingStringReader) t.GetStringTrackedDeviceProperty=null;return t;
        }
    }
    static LegacyRightSample Read(Runtime runtime,bool buttonOnly=false,bool observedAxis=false)
    {
        var table=runtime.Table();IntPtr pointer=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(IVRSystem)));
        try {Marshal.StructureToPtr(table,pointer,false);var system=new CVRSystem(pointer);var sample=OuterGamepadCompatibilityProfile.ReadSafely(system,observedAxis?OuterGamepadCompatibilityProfile.ObservedAxis0Name:buttonOnly?OuterGamepadCompatibilityProfile.ButtonOnlyWakeName:OuterGamepadCompatibilityProfile.WakeName);GC.KeepAlive(table);return sample;}
        finally {Marshal.DestroyStructure(pointer,typeof(IVRSystem));Marshal.FreeHGlobal(pointer);}
    }
    public static void Run(Action<bool,string> check)
    {
        var r=new Runtime();var s=Read(r);
        check(s.Available && s.A && s.Packet==17 && s.Index==3 && s.Identity=="fixture" && Math.Abs(s.X-0.3)<1e-6 && Math.Abs(s.Y+0.4)<1e-6 && r.StateCalls==1 && r.RoleCalls==2,"Real vendored CVRSystem ABI reader jointly polls fresh state/pose and rechecks the right role without SDK initialization");
        r=new Runtime {Pressed=1UL<<2,Touched=1UL<<7};s=Read(r);
        check(s.Available && !s.A,"Explicit legacy A profile uses SDK bit7 pressed state, not touch or the Index-controller bit2 alias");
        foreach(int kind in new[]{0,1,3,99}) {r=new Runtime {AxisKind=kind};s=Read(r);check(!s.Available && r.StateCalls==0,"Nonjoystick axis metadata does not become a guessed thumbstick: "+kind);}
        r=new Runtime {PropertyError=true};s=Read(r);check(!s.Available && s.UnavailableReason=="axis0_metadata_unavailable" && r.StateCalls==0,"Property errors refuse input before state polling");
        r=new Runtime {StringLength=258};s=Read(r);check(!s.Available && r.StateCalls==0,"Oversized serial identity is not truncated into a valid controller identity");
        r=new Runtime {Serial=OuterGamepadCompatibilityProfile.ButtonOnlySerial,Model=OuterGamepadCompatibilityProfile.ButtonOnlyModel,PropertyError=true,X=float.NaN,Y=float.PositiveInfinity};s=Read(r,true);
        check(s.Available && s.A && !s.AxesAvailable && s.X==0 && s.Y==0 && r.AxisCalls==0 && r.StateCalls==1 && r.RoleCalls==2 && r.StringCalls==4,"Restricted received button profile validates exact identity/pose and ignores all unknown or nonfinite axes without querying axis properties");
        r=new Runtime {Serial=OuterGamepadCompatibilityProfile.ButtonOnlySerial,Model=OuterGamepadCompatibilityProfile.ButtonOnlyModel,Pressed=0,Touched=128};s=Read(r,true);
        check(s.Available && !s.A && s.X==0 && s.Y==0,"Observed PC touched128 without pressed128 never substitutes for A in the button-only profile");
        UnityEngine.InputSystem.InputSystem.Reset();
        var diagnostic=new OpenVrGamepadDiagnostic(delegate {return new PublicOwnedGamepad(Assembly.GetExecutingAssembly(),false,OuterGamepadCompatibilityProfile.LayoutPrefix,OuterGamepadCompatibilityProfile.ButtonFor(OuterGamepadCompatibilityProfile.ButtonOnlyWakeName));},5,0.2,"refuse",1);
        r=new Runtime {Serial=OuterGamepadCompatibilityProfile.ButtonOnlySerial,Model=OuterGamepadCompatibilityProfile.ButtonOnlyModel,PropertyError=true,Pressed=0,X=float.NaN,Y=float.PositiveInfinity};
        diagnostic.Tick(true,true,1,Read(r,true),0);r.Pressed=128;diagnostic.Tick(true,true,1,Read(r,true),0.1);UnityEngine.InputSystem.InputSystem.NormalUpdate();
        var owned=UnityEngine.InputSystem.Gamepad.current;
        check(owned.buttonWest.isPressed && !owned.buttonSouth.isPressed && owned.leftStick.Value.x==0 && owned.leftStick.Value.y==0 && r.AxisCalls==0,"Restricted actual SDK read path forwards observed pressed128 to West only and cannot deliver unknown-axis movement");
        r.Pressed=0;diagnostic.Tick(true,true,1,Read(r,true),0.2);UnityEngine.InputSystem.InputSystem.NormalUpdate();
        check(!owned.buttonWest.isPressed && diagnostic.Queues==2,"Button-only observed A release is queued once through normal engine input without invented axis output");diagnostic.Cancel("test_end",0.3);
        foreach(string fault in new[]{"serial","model","model_spacing","property_error","model_error","oversized","invalid_pose","disconnected","sdk_false","role_changed","identity_changed","nonfinite_pose"})
        {
            r=new Runtime {Serial=OuterGamepadCompatibilityProfile.ButtonOnlySerial,Model=OuterGamepadCompatibilityProfile.ButtonOnlyModel};
            if(fault=="serial") r.Serial="physical-controller";if(fault=="model") r.Model="other";if(fault=="model_spacing") r.Model="DeckardMP";
            if(fault=="property_error") r.StringError=true;if(fault=="model_error") r.ModelError=true;if(fault=="oversized") r.StringLength=258;
            if(fault=="invalid_pose") r.PoseValid=false;if(fault=="disconnected") r.Connected=false;if(fault=="sdk_false") r.StateSuccess=false;
            if(fault=="role_changed") r.RoleChange=true;if(fault=="identity_changed") r.IdentityChange=true;if(fault=="nonfinite_pose") r.NonfinitePose=true;
            s=Read(r,true);check(!s.Available && r.AxisCalls==0,"Button-only identity/role/pose failure cannot authorize device creation or guessed axes: "+fault);
        }
        r=new Runtime {Serial=OuterGamepadCompatibilityProfile.ButtonOnlySerial,Model=OuterGamepadCompatibilityProfile.ButtonOnlyModel,PropertyError=true,X=0.3464567f,Y=0.5984252f};s=Read(r,false,true);
        check(s.Available && s.A && s.AxesAvailable && s.AxesSource=="observed_transport_axis0" && s.Axis0Metadata=="TrackedProp_UnknownProperty" && !s.Axis0Type.HasValue && s.X==(double)0.3464567f && s.Y==(double)0.5984252f && r.AxisCalls==1,"Explicit observed transport preserves received quantized raw axis0 with UnknownProperty diagnostics without claiming a joystick declaration");
        r=new Runtime {Serial=OuterGamepadCompatibilityProfile.ButtonOnlySerial,Model=OuterGamepadCompatibilityProfile.ButtonOnlyModel,PropertyError=true,AxisKind=3};s=Read(r,false,true);
        check(s.Available && !s.Axis0Type.HasValue,"A returned integer accompanying UnknownProperty is not treated as a successful type declaration");
        r=new Runtime {Serial=OuterGamepadCompatibilityProfile.ButtonOnlySerial,Model=OuterGamepadCompatibilityProfile.ButtonOnlyModel,AxisKind=2};s=Read(r,false,true);
        check(s.Available && s.Axis0Type==2 && s.Axis0Metadata=="TrackedProp_Success" && s.AxesSource=="observed_transport_axis0","A compatible known type retains the explicitly selected transport label and raw metadata");
        foreach(int kind in new[]{0,1,3,99})
        {
            r=new Runtime {Serial=OuterGamepadCompatibilityProfile.ButtonOnlySerial,Model=OuterGamepadCompatibilityProfile.ButtonOnlyModel,AxisKind=kind};s=Read(r,false,true);
            check(!s.Available && s.UnavailableReason=="observed_transport_axis0_metadata_conflict" && s.Axis0Type==kind && r.StateCalls==0,"The observed transport cannot override a known conflicting axis type: "+kind);
        }
        foreach(var error in new[]{ETrackedPropertyError.TrackedProp_WrongDataType,ETrackedPropertyError.TrackedProp_WrongDeviceClass,ETrackedPropertyError.TrackedProp_BufferTooSmall})
        {
            r=new Runtime {Serial=OuterGamepadCompatibilityProfile.ButtonOnlySerial,Model=OuterGamepadCompatibilityProfile.ButtonOnlyModel,AxisError=error};s=Read(r,false,true);
            check(!s.Available && s.UnavailableReason=="observed_transport_axis0_metadata_fault" && s.Axis0Metadata==error.ToString() && !s.Axis0Type.HasValue && r.StateCalls==0,"A property failure other than observed UnknownProperty cannot authorize transport axes: "+error);
        }
        foreach(float value in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity,1.0001f,-1.0001f})
        {
            r=new Runtime {Serial=OuterGamepadCompatibilityProfile.ButtonOnlySerial,Model=OuterGamepadCompatibilityProfile.ButtonOnlyModel,PropertyError=true,X=value};s=Read(r,false,true);
            check(!s.Available && !s.AxesAvailable && s.UnavailableReason=="observed_transport_axis0_invalid","Nonfinite or out-of-range raw axis X cannot reach the game: "+value);
            r=new Runtime {Serial=OuterGamepadCompatibilityProfile.ButtonOnlySerial,Model=OuterGamepadCompatibilityProfile.ButtonOnlyModel,PropertyError=true,Y=value};s=Read(r,false,true);
            check(!s.Available && !s.AxesAvailable && s.UnavailableReason=="observed_transport_axis0_invalid","Nonfinite or out-of-range raw axis Y cannot reach the game: "+value);
        }
        foreach(string fault in new[]{"serial","model","identity_changed","model_changed","invalid_pose","disconnected","sdk_false","role_changed","nonfinite_pose"})
        {
            r=new Runtime {Serial=OuterGamepadCompatibilityProfile.ButtonOnlySerial,Model=OuterGamepadCompatibilityProfile.ButtonOnlyModel,PropertyError=true};
            if(fault=="serial") r.Serial="different";if(fault=="model") r.Model="DeckardMP";if(fault=="identity_changed") r.IdentityChange=true;if(fault=="model_changed") r.ModelChange=true;
            if(fault=="invalid_pose") r.PoseValid=false;if(fault=="disconnected") r.Connected=false;if(fault=="sdk_false") r.StateSuccess=false;if(fault=="role_changed") r.RoleChange=true;if(fault=="nonfinite_pose") r.NonfinitePose=true;
            s=Read(r,false,true);check(!s.Available && !s.AxesAvailable,"Observed raw axis evidence never bypasses identity, role or valid pose guards: "+fault);
        }
        UnityEngine.InputSystem.InputSystem.Reset();
        diagnostic=new OpenVrGamepadDiagnostic(delegate {return new PublicOwnedGamepad(Assembly.GetExecutingAssembly(),false,OuterGamepadCompatibilityProfile.LayoutPrefix,OuterGamepadCompatibilityProfile.ButtonFor(OuterGamepadCompatibilityProfile.ObservedAxis0Name));},5,0.2,"refuse",1);
        r=new Runtime {Serial=OuterGamepadCompatibilityProfile.ButtonOnlySerial,Model=OuterGamepadCompatibilityProfile.ButtonOnlyModel,PropertyError=true,Pressed=0,X=0.3464567f,Y=0.5984252f};
        diagnostic.Tick(true,true,1,Read(r,false,true),0);
        check(UnityEngine.InputSystem.InputSystem.Adds==0 && diagnostic.Reason=="right_controller_not_neutral","An observed transported nonneutral axis cannot arm the first owned window");
        r.X=r.Y=0;diagnostic.Tick(true,true,1,Read(r,false,true),0.1);r.Pressed=128;r.X=0.3464567f;r.Y=0.5984252f;diagnostic.Tick(true,true,1,Read(r,false,true),0.2);UnityEngine.InputSystem.InputSystem.NormalUpdate();
        owned=UnityEngine.InputSystem.Gamepad.current;
        // Independently calculated expected vector from the received PC values and configured .2 radial deadzone.
        check(owned.buttonWest.isPressed && !owned.buttonSouth.isPressed && Math.Abs(owned.leftStick.Value.x-0.307811806)<1e-6 && Math.Abs(owned.leftStick.Value.y-0.531674929)<1e-6,"Quantized PC axis0 passes the actual public queue with the configured radial result, without replacing it with requested headset values");
        r.Pressed=0;r.X=r.Y=0;diagnostic.Tick(true,true,1,Read(r,false,true),0.3);UnityEngine.InputSystem.InputSystem.NormalUpdate();
        check(!owned.buttonWest.isPressed && owned.leftStick.Value.x==0 && owned.leftStick.Value.y==0 && diagnostic.Queues==2,"Observed transport zero releases the owned West and movement state once");
        r.PropertyError=false;r.AxisKind=1;diagnostic.Tick(true,true,1,Read(r,false,true),0.4);UnityEngine.InputSystem.InputSystem.NormalUpdate();diagnostic.Tick(true,true,1,Read(r,false,true),0.5);
        check(diagnostic.Complete && diagnostic.ReleaseObserved && !owned.added && UnityEngine.InputSystem.InputSystem.Removes==1,"A newly conflicting metadata declaration ends and cleans up the started transport window");
        r=new Runtime {Serial=OuterGamepadCompatibilityProfile.ButtonOnlySerial,Model=OuterGamepadCompatibilityProfile.ButtonOnlyModel,MissingStringReader=true};s=Read(r,true);
        check(!s.Available && s.UnavailableReason=="reader_exception:System.NullReferenceException" && r.StateCalls==0,"The actual shipping safe-read boundary categorizes a missing SDK callback instead of silently returning an unexplained invalid sample");
        foreach(string fault in new[]{"invalid_pose","disconnected","sdk_false","role_changed","nonfinite_pose"})
        {
            r=new Runtime {Serial=OuterGamepadCompatibilityProfile.ButtonOnlySerial,Model=OuterGamepadCompatibilityProfile.ButtonOnlyModel};
            if(fault=="invalid_pose") r.PoseValid=false;if(fault=="disconnected") r.Connected=false;if(fault=="sdk_false") r.StateSuccess=false;if(fault=="role_changed") r.RoleChange=true;if(fault=="nonfinite_pose") r.NonfinitePose=true;
            s=Read(r,true);string expected=fault=="invalid_pose"?"controller_pose_invalid":fault=="disconnected"?"controller_disconnected":fault=="sdk_false"?"controller_state_read_failed":fault=="role_changed"?"right_role_changed":"controller_pose_nonfinite";
            check(!s.Available && s.UnavailableReason==expected,"Shipping reader distinguishes the exact guarded source failure: "+fault);
        }
        foreach(string reason in new[]{"invalid_pose","disconnected","sdk_false","role_changed"})
        {
            r=new Runtime();if(reason=="invalid_pose") r.PoseValid=false;if(reason=="disconnected") r.Connected=false;if(reason=="sdk_false") r.StateSuccess=false;if(reason=="role_changed") r.RoleChange=true;
            s=Read(r);check(!s.Available && r.StateCalls==1,"Joint state/pose or hand-identity failure rejects the sample: "+reason);
        }
    }
}
