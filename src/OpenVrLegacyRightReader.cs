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
        {return ReadRestricted(system,expectedSerial,expectedModel,false);}
        public static LegacyRightSample ReadObservedTransportAxis0(CVRSystem system,string expectedSerial,string expectedModel)
        {return ReadRestricted(system,expectedSerial,expectedModel,true);}
        static LegacyRightSample ReadRestricted(CVRSystem system,string expectedSerial,string expectedModel,bool includeAxes)
        {
            var sample=new LegacyRightSample {UnavailableReason="runtime_unavailable",Axis0Metadata="not_queried",IdentityDiagnostics=new ControllerIdentitySnapshot()};
            try {return ReadRestrictedBody(system,expectedSerial,expectedModel,includeAxes,ref sample);}
            catch(Exception error)
            {
                sample.UnavailableReason="reader_exception:"+error.GetType().FullName;
                sample.IdentityDiagnostics.ExceptionType=error.GetType().FullName;sample.IdentityDiagnostics.Verdict="reader_exception";
                return sample;
            }
        }
        static LegacyRightSample ReadRestrictedBody(CVRSystem system,string expectedSerial,string expectedModel,bool includeAxes,ref LegacyRightSample sample)
        {
            if(system==null) return sample;
            var snapshot=sample.IdentityDiagnostics;
            string prefix=includeAxes?"observed_transport":"button_only";
            if(String.IsNullOrEmpty(expectedSerial) || String.IsNullOrEmpty(expectedModel)) {sample.UnavailableReason=prefix+"_identity_pin_missing";return sample;}
            snapshot.Stage="selected_index";uint index=system.GetTrackedDeviceIndexForControllerRole(ETrackedControllerRole.RightHand);snapshot.Index=index;
            sample.UnavailableReason="right_role_unavailable";
            if(index>=OpenVR.k_unMaxTrackedDeviceCount) {snapshot.Verdict="index_out_of_range";return sample;}
            snapshot.Stage="selected_class";snapshot.DeviceClass=system.GetTrackedDeviceClass(index);
            snapshot.Stage="selected_role";snapshot.Role=system.GetControllerRoleForTrackedDeviceIndex(index);
            if(snapshot.DeviceClass!=ETrackedDeviceClass.Controller || snapshot.Role!=ETrackedControllerRole.RightHand) {snapshot.Verdict="class_or_role_disagreement";return sample;}
            sample.UnavailableReason=prefix+"_controller_identity_mismatch";
            snapshot.Stage="initial_serial";snapshot.InitialSerial=StringPropertyObservation.Read(system,index,ETrackedDeviceProperty.Prop_SerialNumber_String,snapshot.Stage);
            snapshot.Stage="initial_model";snapshot.InitialModel=StringPropertyObservation.Read(system,index,ETrackedDeviceProperty.Prop_ModelNumber_String,snapshot.Stage);
            snapshot.Stage="initial_identity_validation";
            string serial=snapshot.InitialSerial.AcceptedValue,model=snapshot.InitialModel.AcceptedValue;
            if(!snapshot.InitialSerial.Returned || !snapshot.InitialModel.Returned)
            {snapshot.Verdict="reader_exception";sample.UnavailableReason="reader_exception:"+(snapshot.InitialSerial.ExceptionType??snapshot.InitialModel.ExceptionType);return sample;}
            if(!snapshot.InitialSerial.Accepted || !snapshot.InitialModel.Accepted) {snapshot.Verdict="property_result_rejected";return sample;}
            if(!String.Equals(serial,expectedSerial,StringComparison.Ordinal) || !String.Equals(model,expectedModel,StringComparison.Ordinal)) {snapshot.Verdict="identity_disagreement";return sample;}
            snapshot.Verdict="initial_identity_matched";
            if(includeAxes)
            {
                snapshot.Stage="axis0_metadata";
                var error=ETrackedPropertyError.TrackedProp_Success;
                int kind=system.GetInt32TrackedDeviceProperty(index,ETrackedDeviceProperty.Prop_Axis0Type_Int32,ref error);
                sample.Axis0Metadata=error.ToString();if(error==ETrackedPropertyError.TrackedProp_Success) sample.Axis0Type=kind;
                if(error==ETrackedPropertyError.TrackedProp_Success && kind!=(int)EVRControllerAxisType.k_eControllerAxis_Joystick)
                {sample.UnavailableReason="observed_transport_axis0_metadata_conflict";return sample;}
                if(error!=ETrackedPropertyError.TrackedProp_Success && error!=ETrackedPropertyError.TrackedProp_UnknownProperty)
                {sample.UnavailableReason="observed_transport_axis0_metadata_fault";return sample;}
            }
            snapshot.Stage="state_pose";
            var state=new VRControllerState_t();var pose=new TrackedDevicePose_t();
            sample.UnavailableReason="controller_pose_state_unavailable";
            if(!system.GetControllerStateWithPose(ETrackingUniverseOrigin.TrackingUniverseStanding,index,ref state,(uint)Marshal.SizeOf(typeof(VRControllerState_t)),ref pose)) {sample.UnavailableReason="controller_state_read_failed";return sample;}
            if(!pose.bDeviceIsConnected) {sample.UnavailableReason="controller_disconnected";return sample;}
            if(!pose.bPoseIsValid) {sample.UnavailableReason="controller_pose_invalid";return sample;}
            var m=pose.mDeviceToAbsoluteTracking;
            foreach(double v in new double[]{m.m0,m.m1,m.m2,m.m3,m.m4,m.m5,m.m6,m.m7,m.m8,m.m9,m.m10,m.m11})
                if(Double.IsNaN(v) || Double.IsInfinity(v)) {sample.UnavailableReason="controller_pose_nonfinite";return sample;}
            snapshot.Stage="post_index";snapshot.PostIndex=system.GetTrackedDeviceIndexForControllerRole(ETrackedControllerRole.RightHand);
            snapshot.Stage="post_class";snapshot.PostClass=system.GetTrackedDeviceClass(index);
            snapshot.Stage="post_role";snapshot.PostRole=system.GetControllerRoleForTrackedDeviceIndex(index);
            if(snapshot.PostIndex!=index || snapshot.PostClass!=ETrackedDeviceClass.Controller || snapshot.PostRole!=ETrackedControllerRole.RightHand) {snapshot.Verdict="class_or_role_changed";sample.UnavailableReason="right_role_changed";return sample;}
            // Capture both post-state properties even if the serial result disagrees.
            sample.UnavailableReason=prefix+"_controller_identity_changed";
            snapshot.Stage="post_serial";snapshot.PostSerial=StringPropertyObservation.Read(system,index,ETrackedDeviceProperty.Prop_SerialNumber_String,snapshot.Stage);
            snapshot.Stage="post_model";snapshot.PostModel=StringPropertyObservation.Read(system,index,ETrackedDeviceProperty.Prop_ModelNumber_String,snapshot.Stage);
            snapshot.Stage="post_identity_validation";
            if(!snapshot.PostSerial.Returned || !snapshot.PostModel.Returned)
            {snapshot.Verdict="reader_exception";sample.UnavailableReason="reader_exception:"+(snapshot.PostSerial.ExceptionType??snapshot.PostModel.ExceptionType);return sample;}
            if(!snapshot.PostSerial.Accepted || !snapshot.PostModel.Accepted) {snapshot.Verdict="property_result_rejected";return sample;}
            if(snapshot.PostSerial.AcceptedValue!=serial || snapshot.PostModel.AcceptedValue!=model) {snapshot.Verdict="identity_disagreement";return sample;}
            if(includeAxes)
            {
                snapshot.Stage="axis0_state_validation";
                sample.UnavailableReason="observed_transport_axis0_invalid";
                if(!BoundedAxis(state.rAxis0.x) || !BoundedAxis(state.rAxis0.y)) return sample;
                sample.AxesAvailable=true;sample.AxesSource="observed_transport_axis0";sample.X=state.rAxis0.x;sample.Y=state.rAxis0.y;
            }
            snapshot.Stage="complete";snapshot.Verdict="accepted";
            sample.Available=true;sample.UnavailableReason="";sample.Index=index;sample.Identity=serial;sample.Packet=state.unPacketNum;
            sample.A=(state.ulButtonPressed & (1UL<<(int)EVRButtonId.k_EButton_A))!=0;
            // Button-only keeps axes unavailable and X/Y zero, even for nonfinite native samples.
            return sample;
        }
        static bool BoundedAxis(float value) {return !Single.IsNaN(value) && !Single.IsInfinity(value) && Math.Abs(value)<=1;}
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
            sample.Axis0Metadata=error.ToString();if(error==ETrackedPropertyError.TrackedProp_Success) sample.Axis0Type=kind;
            sample.UnavailableReason=error==ETrackedPropertyError.TrackedProp_Success?"axis0_not_joystick":"axis0_metadata_unavailable";
            if(error!=ETrackedPropertyError.TrackedProp_Success || kind!=(int)EVRControllerAxisType.k_eControllerAxis_Joystick) return sample;
            var serial=new StringBuilder(257);error=ETrackedPropertyError.TrackedProp_Success;
            uint needed=system.GetStringTrackedDeviceProperty(index,ETrackedDeviceProperty.Prop_SerialNumber_String,serial,257,ref error);
            sample.UnavailableReason="controller_identity_unavailable";
            if(error!=ETrackedPropertyError.TrackedProp_Success || needed==0 || needed>257 || serial.Length==0 || serial.Length>256) return sample;
            var state=new VRControllerState_t();var pose=new TrackedDevicePose_t();
            sample.UnavailableReason="controller_pose_state_unavailable";
            if(!system.GetControllerStateWithPose(ETrackingUniverseOrigin.TrackingUniverseStanding,index,ref state,(uint)Marshal.SizeOf(typeof(VRControllerState_t)),ref pose)) {sample.UnavailableReason="controller_state_read_failed";return sample;}
            if(!pose.bDeviceIsConnected) {sample.UnavailableReason="controller_disconnected";return sample;}
            if(!pose.bPoseIsValid) {sample.UnavailableReason="controller_pose_invalid";return sample;}
            var m=pose.mDeviceToAbsoluteTracking;
            foreach(double v in new double[]{m.m0,m.m1,m.m2,m.m3,m.m4,m.m5,m.m6,m.m7,m.m8,m.m9,m.m10,m.m11})
                if(Double.IsNaN(v) || Double.IsInfinity(v)) {sample.UnavailableReason="controller_pose_nonfinite";return sample;}
            // Recheck the role after the joint pose/state sample to reject an observed hand reassignment.
            if(system.GetTrackedDeviceIndexForControllerRole(ETrackedControllerRole.RightHand)!=index || system.GetControllerRoleForTrackedDeviceIndex(index)!=ETrackedControllerRole.RightHand) {sample.UnavailableReason="right_role_changed";return sample;}
            sample.Available=true;sample.UnavailableReason="";sample.Index=index;sample.Identity=serial.ToString();sample.Packet=state.unPacketNum;
            sample.A=(state.ulButtonPressed & (1UL<<(int)EVRButtonId.k_EButton_A))!=0;
            sample.AxesAvailable=true;sample.AxesSource="axis0_joystick";sample.X=state.rAxis0.x;sample.Y=state.rAxis0.y;return sample;
        }
    }
}
