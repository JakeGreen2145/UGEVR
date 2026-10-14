using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Valve.VR;

namespace Ugevr
{
    public struct RewiredHandSample
    {
        public uint Index,Packet;
        public ETrackedControllerRole Role;
        public string Identity,Model,Signature;
        public ulong Pressed;
        public double X,Y,Trigger;
    }
    public struct RewiredPairSample
    {
        public bool Available;
        public string Profile,UnavailableReason;
        public RewiredHandSample Left,Right;
        public double StartedAt,CompletedAt;
    }
    // Independently queried in the game's SDK context. Sequential reads are not atomic.
    public static class OpenVrRewiredPairReader
    {
        public const string LeftModel="Oculus Quest2 (Left Controller)",RightModel="Oculus Quest2 (Right Controller)",Signature="2,3,3,0,0";
        public const double MaximumQuerySeconds=.25;
        public static RewiredPairSample ReadSafely(CVRSystem system)
        {return ReadSafely(system,delegate {return Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency;});}
        public static RewiredPairSample ReadSafely(CVRSystem system,Func<double> clock)
        {
            try {return Read(system,clock);}
            catch(Exception error) {return new RewiredPairSample {UnavailableReason="paired_reader_exception:"+error.GetType().FullName};}
        }
        static bool Finite(double value) {return !Double.IsNaN(value) && !Double.IsInfinity(value);}
        static bool Context(CVRSystem system,ETrackedControllerRole role,uint index)
        {return index<OpenVR.k_unMaxTrackedDeviceCount && system.GetTrackedDeviceIndexForControllerRole(role)==index && system.GetTrackedDeviceClass(index)==ETrackedDeviceClass.Controller && system.GetControllerRoleForTrackedDeviceIndex(index)==role;}
        static bool Text(CVRSystem system,uint index,ETrackedDeviceProperty property,out string value)
        {
            value=null;var text=new StringBuilder(257);var error=ETrackedPropertyError.TrackedProp_Success;
            uint needed=system.GetStringTrackedDeviceProperty(index,property,text,257,ref error);
            if(error!=ETrackedPropertyError.TrackedProp_Success || needed==0 || needed>257 || text.Length==0 || text.Length>256) return false;
            value=text.ToString();return true;
        }
        static bool Metadata(CVRSystem system,uint index)
        {
            int[] expected={2,3,3,0,0};bool valid=true;
            for(int i=0;i<5;++i)
            {
                var error=ETrackedPropertyError.TrackedProp_Success;
                int value=system.GetInt32TrackedDeviceProperty(index,(ETrackedDeviceProperty)((int)ETrackedDeviceProperty.Prop_Axis0Type_Int32+i),ref error);
                if(error!=ETrackedPropertyError.TrackedProp_Success || value!=expected[i]) valid=false;
            }
            return valid;
        }
        static bool Properties(CVRSystem system,ref RewiredHandSample hand)
        {
            string serial,model;bool a=Text(system,hand.Index,ETrackedDeviceProperty.Prop_SerialNumber_String,out serial);
            bool b=Text(system,hand.Index,ETrackedDeviceProperty.Prop_ModelNumber_String,out model);
            bool metadata=Metadata(system,hand.Index);hand.Identity=serial;hand.Model=model;hand.Signature=metadata?Signature:null;
            return a && b && metadata && model==(hand.Role==ETrackedControllerRole.LeftHand?LeftModel:RightModel);
        }
        static bool Joint(CVRSystem system,ref RewiredHandSample hand)
        {
            var state=new VRControllerState_t();var pose=new TrackedDevicePose_t();
            if(!system.GetControllerStateWithPose(ETrackingUniverseOrigin.TrackingUniverseStanding,hand.Index,ref state,(uint)Marshal.SizeOf(typeof(VRControllerState_t)),ref pose) || !pose.bDeviceIsConnected || !pose.bPoseIsValid) return false;
            var m=pose.mDeviceToAbsoluteTracking;
            foreach(float v in new[]{m.m0,m.m1,m.m2,m.m3,m.m4,m.m5,m.m6,m.m7,m.m8,m.m9,m.m10,m.m11}) if(!Finite(v)) return false;
            if(!Finite(state.rAxis0.x) || !Finite(state.rAxis0.y) || Math.Abs(state.rAxis0.x)>1 || Math.Abs(state.rAxis0.y)>1) return false;
            if(hand.Role==ETrackedControllerRole.RightHand && (!Finite(state.rAxis1.x) || state.rAxis1.x<0 || state.rAxis1.x>1)) return false;
            hand.Packet=state.unPacketNum;hand.Pressed=state.ulButtonPressed;hand.X=state.rAxis0.x;hand.Y=state.rAxis0.y;hand.Trigger=hand.Role==ETrackedControllerRole.RightHand?state.rAxis1.x:0;return true;
        }
        static bool Repeat(CVRSystem system,RewiredHandSample expected)
        {
            if(!Context(system,expected.Role,expected.Index)) return false;
            var fresh=expected;return Properties(system,ref fresh) && fresh.Identity==expected.Identity && fresh.Model==expected.Model && fresh.Signature==expected.Signature && Context(system,expected.Role,expected.Index);
        }
        public static RewiredPairSample Read(CVRSystem system,Func<double> clock)
        {
            var pair=new RewiredPairSample {UnavailableReason="runtime_unavailable"};if(system==null || clock==null) return pair;
            double started=clock();pair.UnavailableReason="query_clock_invalid";if(!Finite(started)) return pair;
            uint left=system.GetTrackedDeviceIndexForControllerRole(ETrackedControllerRole.LeftHand),right=system.GetTrackedDeviceIndexForControllerRole(ETrackedControllerRole.RightHand);
            pair.UnavailableReason="initial_pair_context_unavailable";
            if(left==right || !Context(system,ETrackedControllerRole.LeftHand,left) || !Context(system,ETrackedControllerRole.RightHand,right)) return pair;
            var l=new RewiredHandSample {Index=left,Role=ETrackedControllerRole.LeftHand};var r=new RewiredHandSample {Index=right,Role=ETrackedControllerRole.RightHand};
            bool leftProperties=Properties(system,ref l),rightProperties=Properties(system,ref r);
            if(!Context(system,l.Role,l.Index) || !Context(system,r.Role,r.Index)) return pair;
            // Valid roles may poll once to prime emulated application properties. The
            // failed initial bracket still refuses this read, even after successful polls.
            pair.UnavailableReason="joint_pair_unavailable";if(!Joint(system,ref l) || !Joint(system,ref r)) return pair;
            pair.UnavailableReason="initial_pair_properties_unavailable";if(!leftProperties || !rightProperties) return pair;
            pair.UnavailableReason="pair_identity_ambiguous";if(l.Identity==r.Identity) return pair;
            pair.UnavailableReason="post_pair_properties_changed";if(!Repeat(system,l) || !Repeat(system,r)) return pair;
            // A left identity can change while the right properties are read. Repeat
            // both public property sets and finish with both role/class contexts.
            pair.UnavailableReason="final_pair_properties_changed";if(!Repeat(system,l) || !Repeat(system,r) || !Context(system,l.Role,l.Index) || !Context(system,r.Role,r.Index)) return pair;
            double completed=clock();pair.UnavailableReason="query_clock_invalid";
            if(!Finite(completed) || completed<started || completed-started>MaximumQuerySeconds) return pair;
            pair.Available=true;pair.UnavailableReason="";pair.Profile=PublicOwnedRewiredInput.FiveChannelProfile;pair.Left=l;pair.Right=r;pair.StartedAt=started;pair.CompletedAt=completed;return pair;
        }
    }
}
