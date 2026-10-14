using System;

namespace Ugevr
{
    public static class OverlayPanelGeometry
    {
        // One common physical panel, independent of either eye's frustum.
        public static RigidPose Place(RigidPose head,double distance,double width,double aspect,double pivotX,double pivotY)
        {
            if (!Finite(distance) || !Finite(width) || !Finite(aspect) || distance<=0 || width<=0 || aspect<=0 || !Finite(pivotX) || !Finite(pivotY))
                throw new ArgumentException("A panel needs finite positive dimensions and a finite pivot.");
            var offset=new Position((pivotX-.5)*width,(pivotY-.5)*width/aspect,distance);
            return new RigidPose(head.Position+head.Rotation.Rotate(offset),head.Rotation);
        }
        public static double PixelScale(double worldWidth,double pixelWidth,double parentScale)
        {
            if (!Finite(worldWidth) || !Finite(pixelWidth) || !Finite(parentScale) || worldWidth<=0 || pixelWidth<=0 || parentScale<=0)
                throw new ArgumentException("A panel needs finite positive scale inputs.");
            return worldWidth/pixelWidth/parentScale;
        }
        static bool Finite(double value) {return !Double.IsNaN(value) && !Double.IsInfinity(value);}
    }
}
