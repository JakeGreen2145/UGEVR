// Only the members borrowed by CameraState are modeled. This fixture verifies
// restoration, not native Unity object destruction or Camera.Render behavior.
namespace UnityEngine
{
    public struct Vector3 { public float x,y,z; public Vector3(float x,float y,float z) { this.x=x;this.y=y;this.z=z; } }
    public struct Quaternion { public float x,y,z,w; public Quaternion(float x,float y,float z,float w) { this.x=x;this.y=y;this.z=z;this.w=w; } }
    public struct Matrix4x4 { public float Marker; public float this[int i] { get { return i==0?Marker:0; } } }
    public struct Rect { public float x,y,width,height; }
    public struct Color { public float r,g,b,a; }
    public class RenderTexture { }
    public enum CameraClearFlags { Skybox=1, Color=2, Depth=3, Nothing=4 }
    public class Transform { public Vector3 position; public Quaternion rotation; }
    public class Camera
    {
        public Transform transform=new Transform();
        public RenderTexture targetTexture;
        Matrix4x4 projection,view;
        public bool AutomaticProjection=true,AutomaticView=true;
        public Matrix4x4 projectionMatrix { get { return AutomaticProjection?new Matrix4x4 {Marker=11}:projection; } set { projection=value;AutomaticProjection=false; } }
        public Matrix4x4 worldToCameraMatrix { get { return AutomaticView?new Matrix4x4 {Marker=12+transform.position.x}:view; } set { view=value;AutomaticView=false; } }
        public void ResetProjectionMatrix() { AutomaticProjection=true; }
        public void ResetWorldToCameraMatrix() { AutomaticView=true; }
        public Rect rect;
        public bool useOcclusionCulling;
        public Color backgroundColor;
        public CameraClearFlags clearFlags;
    }
}
