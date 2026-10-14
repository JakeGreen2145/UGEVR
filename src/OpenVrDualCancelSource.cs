using System;
using Valve.VR;

namespace Ugevr
{
    public struct LegacyCancelPairSample
    {
        public LegacyPairSample Pair;
        public string LeftModel,RightModel;
        public int LeftSignature,RightSignature;
    }
    public static class OuterDualCancelSource
    {
        public const string LeftModel="Oculus Quest2 (Left Controller)",RightModel="Oculus Quest2 (Right Controller)";
        public const int Signature=0x00030302;
        public const ulong WestPressedMask=128,EastPressedMask=2;
        static bool Role(CVRSystem system,PairSourceHand hand)
        {
            uint index=hand.Initial.RoleIndex.Value;
            return system.GetTrackedDeviceIndexForControllerRole(hand.ExpectedRole)==index && system.GetTrackedDeviceClass(index)==ETrackedDeviceClass.Controller && system.GetControllerRoleForTrackedDeviceIndex(index)==hand.ExpectedRole;
        }
        static bool FinalPins(CVRSystem system,PairSourceHand hand,string model)
        {
            if(!Role(system,hand))return false;
            uint index=hand.Initial.RoleIndex.Value;
            var serial=StringPropertyObservation.Read(system,index,ETrackedDeviceProperty.Prop_SerialNumber_String,"cancel_final_serial");
            var currentModel=StringPropertyObservation.Read(system,index,ETrackedDeviceProperty.Prop_ModelNumber_String,"cancel_final_model");
            if(!serial.Accepted || !currentModel.Accepted || serial.RawValue!=hand.PreSerial.RawValue || currentModel.RawValue!=model)return false;
            for(int axis=0;axis<5;++axis)
            {
                var error=ETrackedPropertyError.TrackedProp_Success;
                int kind=system.GetInt32TrackedDeviceProperty(index,(ETrackedDeviceProperty)((int)ETrackedDeviceProperty.Prop_Axis0Type_Int32+axis),ref error);
                if(error!=ETrackedPropertyError.TrackedProp_Success || kind!=(axis==0?2:axis<3?3:0))return false;
            }
            return Role(system,hand);
        }
        static bool Pinned(PairSourceHand hand,string model)
        {
            if(hand.PreModel.RawValue!=model || hand.PostModel.RawValue!=model)return false;
            for(int axis=0;axis<5;++axis)if(hand.AxisTypes[axis]!=(axis==0?2:axis<3?3:0))return false;
            foreach(double value in hand.Axes)if(Math.Abs(value)>1)return false;
            return true;
        }
        public static LegacyCancelPairSample ReadSafely(CVRSystem system)
        {
            var result=new LegacyCancelPairSample {Pair=new LegacyPairSample {UnavailableReason="cancel_pair_runtime_unavailable"}};
            if(system==null)return result;
            try
            {
                // Shared SDK-only observation has independent pre/post text, joint polls,
                // successful metadata and final contexts. It has no Rewired backend calls.
                var snapshot=RewiredPairSourceReader.Read(system);
                if(snapshot.Verdict!="observed") {result.Pair.UnavailableReason="cancel_pair:"+snapshot.Verdict;return result;}
                if(!Pinned(snapshot.Left,LeftModel) || !Pinned(snapshot.Right,RightModel)) {result.Pair.UnavailableReason="cancel_pair_source_pin_mismatch";return result;}
                if(!FinalPins(system,snapshot.Left,LeftModel) || !FinalPins(system,snapshot.Right,RightModel) || !Role(system,snapshot.Left) || !Role(system,snapshot.Right)) {result.Pair.UnavailableReason="cancel_pair_final_pins_changed";return result;}
                result.Pair=new LegacyPairSample {Available=true,UnavailableReason="",Left=Hand(snapshot.Left),Right=Hand(snapshot.Right)};
                result.LeftModel=LeftModel;result.RightModel=RightModel;result.LeftSignature=result.RightSignature=Signature;
                return result;
            }
            catch {result.Pair.UnavailableReason="cancel_pair_reader_exception";return result;}
        }
        static LegacyHandSample Hand(PairSourceHand hand)
        {return new LegacyHandSample {Available=true,Index=hand.Initial.RoleIndex.Value,Role=hand.ExpectedRole,Identity=hand.PreSerial.RawValue,Axis0Type=2,Packet=(uint)hand.Packet.Value,Pressed=hand.Pressed.Value,X=hand.Axes[0],Y=hand.Axes[1]};}
    }
}
