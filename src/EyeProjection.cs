using System;

namespace Ugevr
{
    public struct EyeFrustum
    {
        public readonly double Left,Right,Bottom,Top,Near,Far;
        public EyeFrustum(double left,double right,double bottom,double top,double near,double far)
        { Left=left;Right=right;Bottom=bottom;Top=top;Near=near;Far=far; }
    }
    public static class EyeProjection
    {
        static bool Finite(double value) { return !Double.IsNaN(value) && !Double.IsInfinity(value); }
        public static EyeFrustum FromOpenVrRaw(double left,double right,double rawTop,double rawBottom,double near,double far)
        {
            if (!Finite(left) || !Finite(right) || !Finite(rawTop) || !Finite(rawBottom) ||
                !Finite(near) || !Finite(far) || left>=right || rawTop>=rawBottom || near<=0 || far<=near)
                throw new ArgumentException("Eye tangents and clipping must be finite and ordered, with 0 < near < far.");
            // OpenVR rawTop is the -Y edge, rawBottom is +Y. Tracking's Z
            // reflection does not negate Y. Unity converts GPU depth/orientation.
            double l=left*near,r=right*near,b=rawTop*near,t=rawBottom*near;
            if (!Finite(l) || !Finite(r) || !Finite(b) || !Finite(t) ||
                Math.Abs(l)>Single.MaxValue || Math.Abs(r)>Single.MaxValue || Math.Abs(b)>Single.MaxValue || Math.Abs(t)>Single.MaxValue)
                throw new ArgumentException("Eye near-plane bounds exceed Unity float precision.");
            return new EyeFrustum(l,r,b,t,near,far);
        }
    }
}
