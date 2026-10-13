using System;

namespace Ugevr
{
    // Engine-independent rigid transforms. Positions are in meters until ComposeEye.
    public struct Position
    {
        public double X, Y, Z;
        public Position(double x, double y, double z) { X = x; Y = y; Z = z; }
        public static Position operator +(Position a, Position b) { return new Position(a.X + b.X, a.Y + b.Y, a.Z + b.Z); }
        public static Position operator -(Position a, Position b) { return new Position(a.X - b.X, a.Y - b.Y, a.Z - b.Z); }
        public static Position operator *(Position a, double scale) { return new Position(a.X * scale, a.Y * scale, a.Z * scale); }
    }

    public struct Orientation
    {
        public double X, Y, Z, W;
        public Orientation(double x, double y, double z, double w) { X = x; Y = y; Z = z; W = w; }
        public static Orientation Identity { get { return new Orientation(0, 0, 0, 1); } }
        public Orientation Normalized()
        {
            double length = Math.Sqrt(X * X + Y * Y + Z * Z + W * W);
            if (length < 1e-12 || Double.IsNaN(length) || Double.IsInfinity(length)) throw new ArgumentException("Invalid orientation.");
            return new Orientation(X / length, Y / length, Z / length, W / length);
        }
        public Orientation Inverse() { var n = Normalized(); return new Orientation(-n.X, -n.Y, -n.Z, n.W); }
        public static Orientation operator *(Orientation a, Orientation b)
        {
            return new Orientation(a.W*b.X+a.X*b.W+a.Y*b.Z-a.Z*b.Y,
                a.W*b.Y-a.X*b.Z+a.Y*b.W+a.Z*b.X,
                a.W*b.Z+a.X*b.Y-a.Y*b.X+a.Z*b.W,
                a.W*b.W-a.X*b.X-a.Y*b.Y-a.Z*b.Z).Normalized();
        }
        public Position Rotate(Position v)
        {
            var q = Normalized();
            var cross = new Position(q.Y*v.Z-q.Z*v.Y, q.Z*v.X-q.X*v.Z, q.X*v.Y-q.Y*v.X);
            var cross2 = new Position(q.Y*cross.Z-q.Z*cross.Y, q.Z*cross.X-q.X*cross.Z, q.X*cross.Y-q.Y*cross.X);
            return v + cross*(2*q.W) + cross2*2;
        }
        public static Orientation Yaw(double radians) { return new Orientation(0, Math.Sin(radians/2), 0, Math.Cos(radians/2)); }
    }

    public struct RigidPose
    {
        public Position Position;
        public Orientation Rotation;
        public RigidPose(Position position, Orientation rotation) { Position = position; Rotation = rotation.Normalized(); }
    }

    public sealed class TrackingOrigin
    {
        public Position Center;
        public Orientation Yaw = Orientation.Identity;
        public bool Initialized;
        // Full position and yaw are explicit independent operations. The default first
        // sample captures both, but changing the game camera never recenters tracking.
        public void RecenterPosition(Position head) { Center = head; Initialized = true; }
        public void RecenterYaw(Orientation head) { Yaw = TrackingMath.Upright(head); }
        public void RecenterHeight(Position head) { Center.Y = head.Y; Initialized = true; }
    }

    public static class TrackingMath
    {
        public static Orientation Upright(Orientation rotation)
        {
            Position forward = rotation.Rotate(new Position(0, 0, 1));
            if (forward.X*forward.X + forward.Z*forward.Z < 1e-12)
            {
                // A camera aimed vertically has no horizontal forward. Its right axis
                // still determines yaw, including the straight-up/down singularity.
                Position right = rotation.Rotate(new Position(1, 0, 0));
                return Orientation.Yaw(Math.Atan2(-right.Z, right.X));
            }
            return Orientation.Yaw(Math.Atan2(forward.X, forward.Z));
        }

        public static RigidPose ComposeEye(RigidPose anchor, RigidPose head, RigidPose eyeToHead,
            TrackingOrigin origin, double unitsPerMeter, bool retainGameTilt)
        {
            if (Double.IsNaN(unitsPerMeter) || Double.IsInfinity(unitsPerMeter) || unitsPerMeter <= 0)
                throw new ArgumentOutOfRangeException("unitsPerMeter");
            Orientation game = retainGameTilt ? anchor.Rotation : Upright(anchor.Rotation);
            Orientation reference = game * origin.Yaw.Inverse();
            Position tracked = head.Position - origin.Center + head.Rotation.Rotate(eyeToHead.Position);
            return new RigidPose(anchor.Position + reference.Rotate(tracked * unitsPerMeter),
                reference * head.Rotation * eyeToHead.Rotation);
        }

        // Convert an OpenVR 3x4 matrix using S M S, S=diag(1,1,-1).
        // The quaternion is extracted from the complete orthonormal rotation.
        public static RigidPose FromOpenVr(double[] m)
        {
            if (m == null || m.Length != 12) throw new ArgumentException("Expected a 3x4 matrix.");
            for (int i = 0; i < m.Length; ++i)
                if (Double.IsNaN(m[i]) || Double.IsInfinity(m[i])) throw new ArgumentException("Nonfinite pose.");
            double a=m[0], b=m[1], c=-m[2], d=m[4], e=m[5], f=-m[6], g=-m[8], h=-m[9], j=m[10];
            double x, y, z, w, s;
            if (a+e+j > 0) { s=Math.Sqrt(a+e+j+1)*2; w=s/4; x=(h-f)/s; y=(c-g)/s; z=(d-b)/s; }
            else if (a>e && a>j) { s=Math.Sqrt(1+a-e-j)*2; w=(h-f)/s; x=s/4; y=(b+d)/s; z=(c+g)/s; }
            else if (e>j) { s=Math.Sqrt(1+e-a-j)*2; w=(c-g)/s; x=(b+d)/s; y=s/4; z=(f+h)/s; }
            else { s=Math.Sqrt(1+j-a-e)*2; w=(d-b)/s; x=(c+g)/s; y=(f+h)/s; z=s/4; }
            return new RigidPose(new Position(m[3], m[7], -m[11]), new Orientation(x,y,z,w));
        }
    }
}
