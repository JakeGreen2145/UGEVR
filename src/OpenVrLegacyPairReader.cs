using System;
using System.Runtime.InteropServices;
using System.Text;
using Valve.VR;

namespace Ugevr
{
    public struct LegacyHandSample
    {
        public bool Available;
        public uint Index,Packet;
        public ulong Pressed;
        public ETrackedControllerRole Role;
        public string Identity;
        public int Axis0Type;
        public double X,Y;
    }
    public struct LegacyPairSample
    {
        public bool Available;
        public LegacyHandSample Left,Right;
        public string UnavailableReason;
    }
    // No SDK initialization. Successful publication requires both completed hand reads
    // and a final pair context check; these sequential queries are not an atomic snapshot.
    public static class OpenVrLegacyPairReader
    {
        public static LegacyPairSample ReadSafely(CVRSystem system)
        {
            try {return Read(system);}
            catch(Exception error) {return new LegacyPairSample {UnavailableReason="reader_exception:"+error.GetType().FullName};}
        }
        public static LegacyPairSample Read(CVRSystem system)
        {
            var pair=new LegacyPairSample {UnavailableReason="runtime_unavailable"};if(system==null) return pair;
            uint left=system.GetTrackedDeviceIndexForControllerRole(ETrackedControllerRole.LeftHand),right=system.GetTrackedDeviceIndexForControllerRole(ETrackedControllerRole.RightHand);
            if(left==right || !RoleContext(system,ETrackedControllerRole.LeftHand,left) || !RoleContext(system,ETrackedControllerRole.RightHand,right)) {pair.UnavailableReason="initial_pair_context_unavailable";return pair;}
            string reason;
            if(!Hand(system,ETrackedControllerRole.LeftHand,left,out pair.Left,out reason)) {pair.UnavailableReason="left:"+reason;return pair;}
            if(!Hand(system,ETrackedControllerRole.RightHand,right,out pair.Right,out reason)) {pair.UnavailableReason="right:"+reason;return pair;}
            if(pair.Left.Index==pair.Right.Index || pair.Left.Identity==pair.Right.Identity) {pair.UnavailableReason="pair_identity_ambiguous";return pair;}
            if(!Context(system,pair.Left) || !Context(system,pair.Right) || !RoleContext(system,pair.Left.Role,left) || !RoleContext(system,pair.Right.Role,right)) {pair.UnavailableReason="pair_context_changed";return pair;}
            pair.Available=true;pair.UnavailableReason="";return pair;
        }
        static bool Hand(CVRSystem system,ETrackedControllerRole role,uint index,out LegacyHandSample sample,out string reason)
        {
            sample=new LegacyHandSample();reason="role_unavailable";
            if(!RoleContext(system,role,index)) return false;
            var state=new VRControllerState_t();var pose=new TrackedDevicePose_t();reason="joint_state_pose_unavailable";
            if(!system.GetControllerStateWithPose(ETrackingUniverseOrigin.TrackingUniverseStanding,index,ref state,(uint)Marshal.SizeOf(typeof(VRControllerState_t)),ref pose) || !pose.bDeviceIsConnected || !pose.bPoseIsValid) return false;
            var m=pose.mDeviceToAbsoluteTracking;
            foreach(float value in new[]{m.m0,m.m1,m.m2,m.m3,m.m4,m.m5,m.m6,m.m7,m.m8,m.m9,m.m10,m.m11}) if(Single.IsNaN(value) || Single.IsInfinity(value)) {reason="pose_nonfinite";return false;}
            var error=ETrackedPropertyError.TrackedProp_Success;
            int kind=system.GetInt32TrackedDeviceProperty(index,ETrackedDeviceProperty.Prop_Axis0Type_Int32,ref error);reason="axis0_metadata_unavailable";
            if(error!=ETrackedPropertyError.TrackedProp_Success) return false;
            if(kind!=(int)EVRControllerAxisType.k_eControllerAxis_Joystick) {reason="axis0_not_joystick";return false;}
            string serial;reason="serial_unavailable";if(!Serial(system,index,out serial)) return false;
            sample=new LegacyHandSample {Index=index,Role=role,Identity=serial,Axis0Type=kind};reason="context_changed";
            if(!Context(system,sample)) return false;
            if(!FiniteAxis(state.rAxis0.x) || !FiniteAxis(state.rAxis0.y)) {reason="axis0_invalid";return false;}
            sample.Available=true;sample.Packet=state.unPacketNum;sample.Pressed=state.ulButtonPressed;sample.X=state.rAxis0.x;sample.Y=state.rAxis0.y;return true;
        }
        static bool Context(CVRSystem system,LegacyHandSample sample)
        {
            if(!RoleContext(system,sample.Role,sample.Index)) return false;
            var error=ETrackedPropertyError.TrackedProp_Success;
            if(system.GetInt32TrackedDeviceProperty(sample.Index,ETrackedDeviceProperty.Prop_Axis0Type_Int32,ref error)!=sample.Axis0Type || error!=ETrackedPropertyError.TrackedProp_Success) return false;
            string serial;return Serial(system,sample.Index,out serial) && serial==sample.Identity;
        }
        static bool RoleContext(CVRSystem system,ETrackedControllerRole role,uint index)
        {return index<OpenVR.k_unMaxTrackedDeviceCount && system.GetTrackedDeviceIndexForControllerRole(role)==index && system.GetTrackedDeviceClass(index)==ETrackedDeviceClass.Controller && system.GetControllerRoleForTrackedDeviceIndex(index)==role;}
        static bool Serial(CVRSystem system,uint index,out string serial)
        {
            serial=null;var text=new StringBuilder(257);var error=ETrackedPropertyError.TrackedProp_Success;
            uint needed=system.GetStringTrackedDeviceProperty(index,ETrackedDeviceProperty.Prop_SerialNumber_String,text,257,ref error);
            if(error!=ETrackedPropertyError.TrackedProp_Success || needed==0 || needed>257 || text.Length==0 || text.Length>256) return false;
            serial=text.ToString();return true;
        }
        static bool FiniteAxis(float value) {return !Single.IsNaN(value) && !Single.IsInfinity(value) && Math.Abs(value)<=1;}
    }
}
