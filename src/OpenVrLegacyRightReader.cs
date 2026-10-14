using System;
using System.Runtime.InteropServices;
using System.Text;
using Valve.VR;

namespace Ugevr
{
    public static class OpenVrLegacyRightReader
    {
        // Explicit test profile. A is the SDK k_EButton_A bit; controller families can map differently.
        public static LegacyRightSample Read(CVRSystem system)
        {
            var sample=new LegacyRightSample {UnavailableReason="runtime_unavailable"};if(system==null) return sample;
            uint index=system.GetTrackedDeviceIndexForControllerRole(ETrackedControllerRole.RightHand);
            sample.UnavailableReason="right_role_unavailable";
            if(index>=OpenVR.k_unMaxTrackedDeviceCount || system.GetTrackedDeviceClass(index)!=ETrackedDeviceClass.Controller ||
                system.GetControllerRoleForTrackedDeviceIndex(index)!=ETrackedControllerRole.RightHand) return sample;
            ETrackedPropertyError error=ETrackedPropertyError.TrackedProp_Success;
            int kind=system.GetInt32TrackedDeviceProperty(index,ETrackedDeviceProperty.Prop_Axis0Type_Int32,ref error);
            sample.UnavailableReason=error==ETrackedPropertyError.TrackedProp_Success?"axis0_not_joystick":"axis0_metadata_unavailable";
            if(error!=ETrackedPropertyError.TrackedProp_Success || kind!=(int)EVRControllerAxisType.k_eControllerAxis_Joystick) return sample;
            var serial=new StringBuilder(257);error=ETrackedPropertyError.TrackedProp_Success;
            uint needed=system.GetStringTrackedDeviceProperty(index,ETrackedDeviceProperty.Prop_SerialNumber_String,serial,257,ref error);
            sample.UnavailableReason="controller_identity_unavailable";
            if(error!=ETrackedPropertyError.TrackedProp_Success || needed==0 || needed>257 || serial.Length==0 || serial.Length>256) return sample;
            var state=new VRControllerState_t();var pose=new TrackedDevicePose_t();
            sample.UnavailableReason="controller_pose_state_unavailable";
            if(!system.GetControllerStateWithPose(ETrackingUniverseOrigin.TrackingUniverseStanding,index,ref state,(uint)Marshal.SizeOf(typeof(VRControllerState_t)),ref pose) ||
                !pose.bDeviceIsConnected || !pose.bPoseIsValid) return sample;
            var m=pose.mDeviceToAbsoluteTracking;
            foreach(double v in new double[]{m.m0,m.m1,m.m2,m.m3,m.m4,m.m5,m.m6,m.m7,m.m8,m.m9,m.m10,m.m11})
                if(Double.IsNaN(v) || Double.IsInfinity(v)) return sample;
            // Recheck the role after the joint pose/state sample to reject an observed hand reassignment.
            if(system.GetTrackedDeviceIndexForControllerRole(ETrackedControllerRole.RightHand)!=index || system.GetControllerRoleForTrackedDeviceIndex(index)!=ETrackedControllerRole.RightHand) return sample;
            sample.Available=true;sample.UnavailableReason="";sample.Index=index;sample.Identity=serial.ToString();sample.Packet=state.unPacketNum;
            sample.A=(state.ulButtonPressed & (1UL<<(int)EVRButtonId.k_EButton_A))!=0;
            sample.X=state.rAxis0.x;sample.Y=state.rAxis0.y;return sample;
        }
    }
}
