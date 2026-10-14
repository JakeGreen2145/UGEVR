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
        public bool StringError,IdentityChange,ModelError,NonfinitePose;
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
            {++AxisCalls;error=PropertyError?ETrackedPropertyError.TrackedProp_UnknownProperty:ETrackedPropertyError.TrackedProp_Success;return AxisKind;};
            t.GetStringTrackedDeviceProperty=delegate(uint index,ETrackedDeviceProperty property,StringBuilder value,uint capacity,ref ETrackedPropertyError error)
            {
                ++StringCalls;bool model=property==ETrackedDeviceProperty.Prop_ModelNumber_String;
                error=StringError || (model && ModelError)?ETrackedPropertyError.TrackedProp_UnknownProperty:ETrackedPropertyError.TrackedProp_Success;
                value.Append(model?Model:(IdentityChange && StateCalls>0?"replacement":Serial));return StringLength;
            };
            t.GetControllerStateWithPose=delegate(ETrackingUniverseOrigin origin,uint index,ref VRControllerState_t state,uint size,ref TrackedDevicePose_t pose)
            {
                ++StateCalls;if(size!=(uint)Marshal.SizeOf(typeof(VRControllerState_t)) || origin!=ETrackingUniverseOrigin.TrackingUniverseStanding) throw new Exception("Incorrect public ABI arguments.");
                state=new VRControllerState_t {unPacketNum=17,ulButtonPressed=Pressed,ulButtonTouched=Touched,rAxis0=new VRControllerAxis_t {x=X,y=Y}};
                pose=new TrackedDevicePose_t {bDeviceIsConnected=Connected,bPoseIsValid=PoseValid,mDeviceToAbsoluteTracking=new HmdMatrix34_t {m0=1,m5=1,m10=1,m3=NonfinitePose?float.NaN:0}};return StateSuccess;
            };return t;
        }
    }
    static LegacyRightSample Read(Runtime runtime,bool buttonOnly=false)
    {
        var table=runtime.Table();IntPtr pointer=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(IVRSystem)));
        try {Marshal.StructureToPtr(table,pointer,false);var system=new CVRSystem(pointer);var sample=buttonOnly?OuterGamepadCompatibilityProfile.Read(system,OuterGamepadCompatibilityProfile.ButtonOnlyWakeName):OpenVrLegacyRightReader.Read(system);GC.KeepAlive(table);return sample;}
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
        var diagnostic=new OpenVrGamepadDiagnostic(delegate {return new PublicOwnedGamepad(Assembly.GetExecutingAssembly(),false,OuterGamepadCompatibilityProfile.LayoutPrefix,OuterGamepadCompatibilityProfile.ButtonFor(OuterGamepadCompatibilityProfile.ButtonOnlyWakeName));},5,0.2,"refuse");
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
        foreach(string reason in new[]{"invalid_pose","disconnected","sdk_false","role_changed"})
        {
            r=new Runtime();if(reason=="invalid_pose") r.PoseValid=false;if(reason=="disconnected") r.Connected=false;if(reason=="sdk_false") r.StateSuccess=false;if(reason=="role_changed") r.RoleChange=true;
            s=Read(r);check(!s.Available && r.StateCalls==1,"Joint state/pose or hand-identity failure rejects the sample: "+reason);
        }
    }
}
