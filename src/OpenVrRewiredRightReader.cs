using System;
using System.Runtime.InteropServices;
using System.Text;
using Valve.VR;

namespace Ugevr
{
    public static class OpenVrRewiredRightReader
    {
        public static RewiredRightSample Read(CVRSystem system)
        {
            var sample=new RewiredRightSample {UnavailableReason="runtime_unavailable"};if(system==null) return sample;
            uint index=system.GetTrackedDeviceIndexForControllerRole(ETrackedControllerRole.RightHand);
            sample.UnavailableReason="right_role_unavailable";
            if(index>=OpenVR.k_unMaxTrackedDeviceCount || system.GetTrackedDeviceClass(index)!=ETrackedDeviceClass.Controller || system.GetControllerRoleForTrackedDeviceIndex(index)!=ETrackedControllerRole.RightHand) return sample;
            // Query joint state before properties that some application contexts expose only after a poll.
            // No sample is published until all metadata, identity and post-poll role guards pass.
            var state=new VRControllerState_t();var pose=new TrackedDevicePose_t();sample.UnavailableReason="joint_pose_state_unavailable";
            if(!system.GetControllerStateWithPose(ETrackingUniverseOrigin.TrackingUniverseStanding,index,ref state,(uint)Marshal.SizeOf(typeof(VRControllerState_t)),ref pose) || !pose.bDeviceIsConnected || !pose.bPoseIsValid) return sample;
            var m=pose.mDeviceToAbsoluteTracking;
            foreach(float poseValue in new[]{m.m0,m.m1,m.m2,m.m3,m.m4,m.m5,m.m6,m.m7,m.m8,m.m9,m.m10,m.m11}) if(Single.IsNaN(poseValue) || Single.IsInfinity(poseValue)) return sample;
            int trigger=-1;
            for(int axis=0;axis<5;++axis)
            {
                ETrackedPropertyError error=ETrackedPropertyError.TrackedProp_Success;
                int kind=system.GetInt32TrackedDeviceProperty(index,(ETrackedDeviceProperty)((int)ETrackedDeviceProperty.Prop_Axis0Type_Int32+axis),ref error);
                sample.UnavailableReason="axis_metadata_unavailable";if(error!=ETrackedPropertyError.TrackedProp_Success) return sample;
                if(kind<(int)EVRControllerAxisType.k_eControllerAxis_None || kind>(int)EVRControllerAxisType.k_eControllerAxis_Trigger) {sample.UnavailableReason="axis_metadata_unsupported";return sample;}
                if(axis==0 && kind!=(int)EVRControllerAxisType.k_eControllerAxis_Joystick) {sample.UnavailableReason="axis0_not_joystick";return sample;}
                if(kind==(int)EVRControllerAxisType.k_eControllerAxis_Trigger) {if(trigger!=-1) {sample.UnavailableReason="trigger_axis_ambiguous";return sample;}trigger=axis;}
            }
            sample.UnavailableReason="trigger_axis_unavailable";if(trigger<1) return sample;
            var serial=new StringBuilder(257);ETrackedPropertyError serialError=ETrackedPropertyError.TrackedProp_Success;
            uint needed=system.GetStringTrackedDeviceProperty(index,ETrackedDeviceProperty.Prop_SerialNumber_String,serial,257,ref serialError);
            sample.UnavailableReason="identity_unavailable";if(serialError!=ETrackedPropertyError.TrackedProp_Success || needed==0 || needed>257 || serial.Length==0 || serial.Length>256) return sample;
            if(system.GetTrackedDeviceIndexForControllerRole(ETrackedControllerRole.RightHand)!=index || system.GetControllerRoleForTrackedDeviceIndex(index)!=ETrackedControllerRole.RightHand) return sample;
            double value=trigger==1?state.rAxis1.x:trigger==2?state.rAxis2.x:trigger==3?state.rAxis3.x:state.rAxis4.x;
            sample.UnavailableReason="analog_state_invalid";
            if(Double.IsNaN(value) || Double.IsInfinity(value) || value<0 || value>1 || Single.IsNaN(state.rAxis0.x) || Single.IsInfinity(state.rAxis0.x) || Single.IsNaN(state.rAxis0.y) || Single.IsInfinity(state.rAxis0.y) || Math.Abs(state.rAxis0.x)>1 || Math.Abs(state.rAxis0.y)>1) return sample;
            sample.Available=true;sample.UnavailableReason="";sample.Index=index;sample.Identity=serial.ToString();sample.Packet=state.unPacketNum;sample.TriggerAxis=trigger;sample.X=state.rAxis0.x;sample.Y=state.rAxis0.y;sample.Trigger=value;return sample;
        }
    }
}
