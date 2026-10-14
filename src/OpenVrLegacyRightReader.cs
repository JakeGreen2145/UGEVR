using System;
using System.Runtime.InteropServices;
using System.Text;
using Valve.VR;

namespace Ugevr
{
    public static class OpenVrLegacyRightReader
    {
        // Restricted observed Windows transport. Unknown axes are never queried or mapped.
        public static LegacyRightSample ReadButtonOnly(CVRSystem system,string expectedSerial,string expectedModel)
        {
            var sample=new LegacyRightSample {UnavailableReason="runtime_unavailable"};if(system==null) return sample;
            if(String.IsNullOrEmpty(expectedSerial) || String.IsNullOrEmpty(expectedModel)) {sample.UnavailableReason="button_only_identity_pin_missing";return sample;}
            uint index=system.GetTrackedDeviceIndexForControllerRole(ETrackedControllerRole.RightHand);
            sample.UnavailableReason="right_role_unavailable";
            if(index>=OpenVR.k_unMaxTrackedDeviceCount || system.GetTrackedDeviceClass(index)!=ETrackedDeviceClass.Controller ||
                system.GetControllerRoleForTrackedDeviceIndex(index)!=ETrackedControllerRole.RightHand) return sample;
            sample.UnavailableReason="button_only_controller_identity_mismatch";
            string serial=ReadIdentity(system,index,ETrackedDeviceProperty.Prop_SerialNumber_String);
            string model=ReadIdentity(system,index,ETrackedDeviceProperty.Prop_ModelNumber_String);
            if(!String.Equals(serial,expectedSerial,StringComparison.Ordinal) || !String.Equals(model,expectedModel,StringComparison.Ordinal)) return sample;
            var state=new VRControllerState_t();var pose=new TrackedDevicePose_t();
            sample.UnavailableReason="controller_pose_state_unavailable";
            if(!system.GetControllerStateWithPose(ETrackingUniverseOrigin.TrackingUniverseStanding,index,ref state,(uint)Marshal.SizeOf(typeof(VRControllerState_t)),ref pose) ||
                !pose.bDeviceIsConnected || !pose.bPoseIsValid) return sample;
            var m=pose.mDeviceToAbsoluteTracking;
            foreach(double v in new double[]{m.m0,m.m1,m.m2,m.m3,m.m4,m.m5,m.m6,m.m7,m.m8,m.m9,m.m10,m.m11})
                if(Double.IsNaN(v) || Double.IsInfinity(v)) return sample;
            if(system.GetTrackedDeviceIndexForControllerRole(ETrackedControllerRole.RightHand)!=index || system.GetControllerRoleForTrackedDeviceIndex(index)!=ETrackedControllerRole.RightHand) return sample;
            // Recheck the pinned properties too, so an observed device replacement cannot be adopted.
            sample.UnavailableReason="button_only_controller_identity_changed";
            if(ReadIdentity(system,index,ETrackedDeviceProperty.Prop_SerialNumber_String)!=serial || ReadIdentity(system,index,ETrackedDeviceProperty.Prop_ModelNumber_String)!=model) return sample;
            sample.Available=true;sample.UnavailableReason="";sample.Index=index;sample.Identity=serial;sample.Packet=state.unPacketNum;
            sample.A=(state.ulButtonPressed & (1UL<<(int)EVRButtonId.k_EButton_A))!=0;
            // AxesAvailable stays false and X/Y stay zero, even for nonfinite native axis samples.
            return sample;
        }
        static string ReadIdentity(CVRSystem system,uint index,ETrackedDeviceProperty property)
        {
            var value=new StringBuilder(257);var error=ETrackedPropertyError.TrackedProp_Success;
            uint needed=system.GetStringTrackedDeviceProperty(index,property,value,257,ref error);
            return error==ETrackedPropertyError.TrackedProp_Success && needed>0 && needed<=257 && value.Length>0 && value.Length<=256?value.ToString():null;
        }
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
            sample.AxesAvailable=true;sample.X=state.rAxis0.x;sample.Y=state.rAxis0.y;return sample;
        }
    }
}
