using System;
using Ugevr;

// Independent homogeneous-matrix oracle. Expected values never use the domain
// quaternion conversion, multiplication, rotation or ComposeEye implementation.
static class TrackingMatrixOracleChecks
{
    static double[,] Identity()
    {var m=new double[4,4];for(int i=0;i<4;++i)m[i,i]=1;return m;}
    static double[,] Product(double[,] a,double[,] b)
    {var m=new double[4,4];for(int i=0;i<4;++i)for(int j=0;j<4;++j)for(int k=0;k<4;++k)m[i,j]+=a[i,k]*b[k,j];return m;}
    static double[,] Rotation(int axis,double degrees)
    {
        var m=Identity();double a=degrees*Math.PI/180,c=Math.Cos(a),s=Math.Sin(a);
        int x=(axis+1)%3,y=(axis+2)%3;m[x,x]=m[y,y]=c;m[x,y]=-s;m[y,x]=s;return m;
    }
    static double[,] Pose(double x,double y,double z,double pitch,double yaw,double roll)
    {var m=Product(Rotation(1,yaw),Product(Rotation(0,pitch),Rotation(2,roll)));m[0,3]=x;m[1,3]=y;m[2,3]=z;return m;}
    static double[,] Basis(double[,] raw)
    {var s=Identity();s[2,2]=-1;return Product(s,Product(raw,s));}
    static double[] Raw(double[,] m)
    {var values=new double[12];for(int i=0;i<3;++i)for(int j=0;j<4;++j)values[i*4+j]=m[i,j];return values;}
    static void Close(Action<bool,string> check,double a,double b,string label)
    {check(!Double.IsNaN(a)&&Math.Abs(a-b)<1e-9,label+": "+a+" versus "+b);}
    static void CheckPose(Action<bool,string> check,RigidPose actual,double[,] expected,string label)
    {
        Close(check,actual.Position.X,expected[0,3],label+" position X");Close(check,actual.Position.Y,expected[1,3],label+" position Y");Close(check,actual.Position.Z,expected[2,3],label+" position Z");
        for(int axis=0;axis<3;++axis)
        {
            var basis=axis==0?new Position(1,0,0):axis==1?new Position(0,1,0):new Position(0,0,1);
            Position rotated=actual.Rotation.Rotate(basis);
            Close(check,rotated.X,expected[0,axis],label+" rotation column "+axis+" X");Close(check,rotated.Y,expected[1,axis],label+" rotation column "+axis+" Y");Close(check,rotated.Z,expected[2,axis],label+" rotation column "+axis+" Z");
        }
    }
    public static void Run(Action<bool,string> check)
    {
        // All quaternion extraction branches, noncommuting rotations and both
        // canted eyes are covered by raw right-handed OpenVR matrices.
        foreach(var angles in new[]{new[]{17d,31d,-23d},new[]{170d,12d,8d},new[]{4d,174d,11d},new[]{9d,15d,178d}})
        {
            var raw=Pose(1.2,-.7,3.1,angles[0],angles[1],angles[2]);
            CheckPose(check,TrackingMath.FromOpenVr(Raw(raw)),Basis(raw),"OpenVR full matrix basis");
        }
        double[,] anchor=Basis(Pose(10,3,-7,34,53,-29)),head=Basis(Pose(.8,1.7,-.4,-21,37,16));
        var anchorPose=TrackingMath.FromOpenVr(Raw(Pose(10,3,-7,34,53,-29)));
        var headPose=TrackingMath.FromOpenVr(Raw(Pose(.8,1.7,-.4,-21,37,16)));
        var origin=new TrackingOrigin();origin.RecenterPosition(new Position(.3,1.1,.2));origin.RecenterYaw(Orientation.Yaw(.27));
        foreach(double scale in new[]{.125,1d,100d})foreach(bool tilt in new[]{false,true})foreach(int eye in new[]{-1,1})
        {
            var rawEye=Pose(eye*.032,.003,-.009,eye*3.5,eye*11.0,-eye*2.0);var eyePose=TrackingMath.FromOpenVr(Raw(rawEye));
            double[,] game=anchor;
            if(!tilt)
            {
                double yaw=Math.Atan2(anchor[0,2],anchor[2,2])*180/Math.PI;game=Rotation(1,yaw);
                for(int i=0;i<3;++i)game[i,3]=anchor[i,3];
            }
            var offset=Identity();offset[0,3]=-.3;offset[1,3]=-1.1;offset[2,3]=-.2;
            var metric=Product(offset,Product(head,Basis(rawEye)));
            for(int i=0;i<3;++i)metric[i,3]*=scale;
            var expected=Product(game,Product(Rotation(1,-.27*180/Math.PI),metric));
            var actual=TrackingMath.ComposeEye(anchorPose,headPose,eyePose,origin,scale,tilt);
            CheckPose(check,actual,expected,"Full HMD/cant/anchor scale "+scale+" tilt "+tilt+" eye "+eye);
        }
        foreach(double scale in new[]{0d,-1d,Double.NaN,Double.PositiveInfinity,Double.NegativeInfinity})
        {
            bool rejected=false;try{TrackingMath.ComposeEye(anchorPose,headPose,headPose,origin,scale,false);}catch(ArgumentOutOfRangeException){rejected=true;}
            check(rejected,"Every nonpositive/nonfinite tracking scale is rejected");
        }
    }
}
