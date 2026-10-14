using System;

namespace Ugevr
{
    // Only compiled, reviewed channel orders. No external mutable action arrays.
    public sealed class RewiredChannelDescriptor
    {
        readonly bool five;
        RewiredChannelDescriptor(bool five) {this.five=five;}
        public static readonly RewiredChannelDescriptor Legacy=new RewiredChannelDescriptor(false);
        public static readonly RewiredChannelDescriptor Five=new RewiredChannelDescriptor(true);
        public int Count {get {return five?5:3;}}
        public int ActionId(int index)
        {
            if(index<0 || index>=Count) throw new ArgumentOutOfRangeException("index");
            if(five) return index<4?29+index:36;
            return index<2?31+index:36;
        }
        public string ActionName(int index)
        {
            int action=ActionId(index);
            switch(action) {case 29:return "MoveXAxis";case 30:return "MoveYAxis";case 31:return "LookXAxis_Stick";case 32:return "LookYAxis_Stick";default:return "DialogDownButton";}
        }
    }
    public struct RewiredFiveValues
    {
        public double MoveX,MoveY,LookX,LookY,Trigger;
        public bool Valid
        {
            get {return Signed(MoveX) && Signed(MoveY) && Signed(LookX) && Signed(LookY) && Signed(Trigger) && Trigger>=0;}
        }
        static bool Signed(double value) {return !Double.IsNaN(value) && !Double.IsInfinity(value) && Math.Abs(value)<=1;}
    }
    public interface IOwnedFiveRewiredInput : IOwnedRewiredInput
    {
        void SetDesired(RewiredFiveValues values,double expires);
    }
}
