using System;
using System.Runtime.InteropServices;
using System.Text;
using Ugevr;
using Valve.VR;

static class LegacyControllerChecks
{
    sealed class Runtime
    {
        public int AxisKind=2,StateCalls,RoleCalls;
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
            {error=PropertyError?ETrackedPropertyError.TrackedProp_UnknownProperty:ETrackedPropertyError.TrackedProp_Success;return AxisKind;};
            t.GetStringTrackedDeviceProperty=delegate(uint index,ETrackedDeviceProperty property,StringBuilder value,uint capacity,ref ETrackedPropertyError error)
            {error=ETrackedPropertyError.TrackedProp_Success;value.Append("fixture");return StringLength;};
            t.GetControllerStateWithPose=delegate(ETrackingUniverseOrigin origin,uint index,ref VRControllerState_t state,uint size,ref TrackedDevicePose_t pose)
            {
                ++StateCalls;if(size!=(uint)Marshal.SizeOf(typeof(VRControllerState_t)) || origin!=ETrackingUniverseOrigin.TrackingUniverseStanding) throw new Exception("Incorrect public ABI arguments.");
                state=new VRControllerState_t {unPacketNum=17,ulButtonPressed=Pressed,ulButtonTouched=Touched,rAxis0=new VRControllerAxis_t {x=X,y=Y}};
                pose=new TrackedDevicePose_t {bDeviceIsConnected=Connected,bPoseIsValid=PoseValid,mDeviceToAbsoluteTracking=new HmdMatrix34_t {m0=1,m5=1,m10=1}};return StateSuccess;
            };return t;
        }
    }
    static LegacyRightSample Read(Runtime runtime)
    {
        var table=runtime.Table();IntPtr pointer=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(IVRSystem)));
        try {Marshal.StructureToPtr(table,pointer,false);var system=new CVRSystem(pointer);var sample=OpenVrLegacyRightReader.Read(system);GC.KeepAlive(table);return sample;}
        finally {Marshal.DestroyStructure(pointer,typeof(IVRSystem));Marshal.FreeHGlobal(pointer);}
    }
    public static void Run(Action<bool,string> check)
    {
        var r=new Runtime();var s=Read(r);
        check(s.Available && s.South && s.Packet==17 && s.Index==3 && s.Identity=="fixture" && Math.Abs(s.X-0.3)<1e-6 && Math.Abs(s.Y+0.4)<1e-6 && r.StateCalls==1 && r.RoleCalls==2,"Real vendored CVRSystem ABI reader jointly polls fresh state/pose and rechecks the right role without SDK initialization");
        r=new Runtime {Pressed=1UL<<2,Touched=1UL<<7};s=Read(r);
        check(s.Available && !s.South,"Explicit legacy A profile uses SDK bit7 pressed state, not touch or the Index-controller bit2 alias");
        foreach(int kind in new[]{0,1,3,99}) {r=new Runtime {AxisKind=kind};s=Read(r);check(!s.Available && r.StateCalls==0,"Nonjoystick axis metadata does not become a guessed thumbstick: "+kind);}
        r=new Runtime {PropertyError=true};s=Read(r);check(!s.Available && s.UnavailableReason=="axis0_metadata_unavailable" && r.StateCalls==0,"Property errors refuse input before state polling");
        r=new Runtime {StringLength=258};s=Read(r);check(!s.Available && r.StateCalls==0,"Oversized serial identity is not truncated into a valid controller identity");
        foreach(string reason in new[]{"invalid_pose","disconnected","sdk_false","role_changed"})
        {
            r=new Runtime();if(reason=="invalid_pose") r.PoseValid=false;if(reason=="disconnected") r.Connected=false;if(reason=="sdk_false") r.StateSuccess=false;if(reason=="role_changed") r.RoleChange=true;
            s=Read(r);check(!s.Available && r.StateCalls==1,"Joint state/pose or hand-identity failure rejects the sample: "+reason);
        }
    }
}
