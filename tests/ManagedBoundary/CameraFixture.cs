// Only the members borrowed by CameraState are modeled. This fixture verifies
// restoration, not native Unity object destruction or Camera.Render behavior.
namespace UnityEngine
{
    public class Object
    {
        static int nextId;
        readonly int id=++nextId;
        public bool Destroyed;
        public int? InstanceIdOverride;
        public string name="fixture";
        public int GetInstanceID() { return InstanceIdOverride ?? id; }
        public static Object[] FindObjectsOfType(System.Type type) { return type==typeof(Canvas)?Canvas.Instances.ToArray():new Object[0]; }
        public static bool operator ==(Object a,Object b) { return (System.Object.ReferenceEquals(a,null) || a.Destroyed)?(System.Object.ReferenceEquals(b,null) || b.Destroyed):System.Object.ReferenceEquals(a,b); }
        public static bool operator !=(Object a,Object b) { return !(a==b); }
        public override bool Equals(object other) { return System.Object.ReferenceEquals(this,other); }
        public override int GetHashCode() { return id; }
    }
    public struct Scene {public bool Valid,isLoaded;public bool IsValid() {return Valid;}}
    public class GameObject : Object
    {
        int savedLayer;
        public System.Action<int> LayerSetter;
        public bool activeSelf=true;
        public int layer {get {return savedLayer;} set {if(LayerSetter!=null) LayerSetter(value);savedLayer=value;}}
        public Scene scene=new Scene {Valid=true,isLoaded=true};
        public bool activeInHierarchy=true;
    }
    public class Component : Object
    {
        public GameObject gameObject=new GameObject();
        public Component[] AttachedComponents=new Component[0];
        public int ComponentInventoryQueries;
        public T[] GetComponents<T>()
        {
            ++ComponentInventoryQueries;
            var found=new System.Collections.Generic.List<T>();
            foreach(Component component in AttachedComponents) if(component is T) found.Add((T)(object)component);
            return found.ToArray();
        }
    }
    public class Renderer : Component {public bool enabled=true;}
    public static class Resources
    {
        public static System.Collections.Generic.List<Object> Extras=new System.Collections.Generic.List<Object>();
        public static Object[] FindObjectsOfTypeAll(System.Type type)
        {
            var found=new System.Collections.Generic.List<Object>();
            foreach(var canvas in Canvas.Instances)
            {
                if(type==typeof(Transform)) found.AddRange(canvas.Rects);
                if(type==typeof(CanvasRenderer)) found.AddRange(canvas.Renderers);
            }
            foreach(var item in Extras) if(type.IsInstanceOfType(item)) found.Add(item);
            return found.ToArray();
        }
    }
    public static class LayerMask {public static System.Func<int,string> Name;public static string LayerToName(int layer) {return Name==null?"":Name(layer);} }
    public struct Vector2 { public float x,y; public Vector2(float x,float y) {this.x=x;this.y=y;} }
    public class RectOffset {public int left,right,top,bottom;}
    public struct Vector3 { public float x,y,z; public Vector3(float x,float y,float z) { this.x=x;this.y=y;this.z=z; } }
    public struct Quaternion { public float x,y,z,w; public Quaternion(float x,float y,float z,float w) { this.x=x;this.y=y;this.z=z;this.w=w; } }
    public struct Matrix4x4 { public float Marker; public float this[int i] { get { return i==0?Marker:0; } } }
    public struct Rect { public float x,y,width,height; }
    public struct Color { public float r,g,b,a;public static Color clear {get {return new Color();}} }
    public class RenderTexture { }
    public enum CameraClearFlags { Skybox=1, Color=2, Depth=3, Nothing=4 }
    public class Transform : Component
    {
        public Vector3 localScale;
        public virtual Vector3 position {get {return localPosition;} set {localPosition=value;} }
        public virtual Vector3 localPosition {get;set;}
        public Quaternion rotation {get {return localRotation;} set {localRotation=value;} }
        Quaternion savedLocalRotation;
        public System.Func<Quaternion,Quaternion> LocalRotationSetter;
        public Quaternion localRotation {get {return savedLocalRotation;} set {savedLocalRotation=LocalRotationSetter==null?value:LocalRotationSetter(value);} }
        public Transform parent;
        public Vector3 lossyScale=new Vector3(1,1,1);
    }
    public class RectTransform : Transform
    {
        public Vector2 anchorMin,anchorMax,pivot,sizeDelta;
        Vector3 anchored,local;
        public Vector3 AnchorReference;
        public Vector3 WorldParentOffset;
        public Vector3 DerivedLocalReadOffset;
        public Vector3 anchoredPosition3D {get {return anchored;} set {anchored=value;local=new Vector3(value.x+AnchorReference.x,value.y+AnchorReference.y,value.z+AnchorReference.z);} }
        public override Vector3 localPosition {get {return new Vector3(local.x+DerivedLocalReadOffset.x,local.y+DerivedLocalReadOffset.y,local.z+DerivedLocalReadOffset.z);} set {local=value;anchored=new Vector3(value.x-AnchorReference.x,value.y-AnchorReference.y,value.z-AnchorReference.z);} }
        public override Vector3 position
        {
            get {var value=localPosition;return new Vector3(value.x+WorldParentOffset.x,value.y+WorldParentOffset.y,value.z+WorldParentOffset.z);}
            set {localPosition=new Vector3(value.x-WorldParentOffset.x,value.y-WorldParentOffset.y,value.z-WorldParentOffset.z);}
        }
        public Rect rect {get {return new Rect {width=sizeDelta.x,height=sizeDelta.y};} }
        public enum Axis {Horizontal,Vertical}
        public void SetSizeWithCurrentAnchors(Axis axis,float value) {if(axis==Axis.Horizontal) sizeDelta.x=value;else sizeDelta.y=value;}
    }
    public class CanvasRenderer : Component { }
    public enum RenderMode {ScreenSpaceOverlay,ScreenSpaceCamera,WorldSpace}
    public enum AdditionalCanvasShaderChannels {None=0,TexCoord1=1,Normal=2}
    public class Canvas : Component
    {
        public static System.Collections.Generic.List<Canvas> Instances=new System.Collections.Generic.List<Canvas>();
        public static System.Action ForceAction;
        public static void ForceUpdateCanvases() { if (ForceAction!=null) ForceAction(); }
        public bool isActiveAndEnabled=true,isRootCanvas=true;
        public int targetDisplay,sortingOrder,sortingLayerID;
        public bool overrideSorting,pixelPerfect,overridePixelPerfect;
        public AdditionalCanvasShaderChannels additionalShaderChannels;
        public float planeDistance,scaleFactor=1,referencePixelsPerUnit=100;
        public Camera worldCamera;
        public System.Action<RenderMode> ModeSetter;
        RenderMode mode;
        public RenderMode renderMode {get {return mode;} set {if (ModeSetter!=null) ModeSetter(value);mode=value;} }
        public RectTransform[] Rects=new RectTransform[0];
        public Transform transform { get {return Rects.Length==0?null:Rects[0];} }
        public Rect pixelRect=new Rect {width=3840,height=2160};
        public CanvasRenderer[] Renderers=new CanvasRenderer[0];
        public Canvas[] NestedCanvases;
        public Renderer[] WorldRenderers=new Renderer[0];
        public T[] GetComponentsInChildren<T>(bool inactive) { return (T[])(object)(typeof(T)==typeof(Canvas)?(System.Array)(NestedCanvases??new[]{this}):typeof(T)==typeof(RectTransform)?(System.Array)Rects:typeof(T)==typeof(Renderer)?(System.Array)WorldRenderers:Renderers); }
    }
    public class Camera : Component
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
        public int cullingMask=-1,targetDisplay;
        public float nearClipPlane=1,farClipPlane=2500;
        public bool enabled;
        public RenderingPath renderingPath;
        public System.Action RenderAction;
        public void Render() {if(RenderAction!=null) RenderAction();}
        public void CopyFrom(Camera source) {targetTexture=source.targetTexture;projectionMatrix=source.projectionMatrix;worldToCameraMatrix=source.worldToCameraMatrix;rect=source.rect;cullingMask=source.cullingMask;clearFlags=source.clearFlags;nearClipPlane=source.nearClipPlane;farClipPlane=source.farClipPlane;}
    }
    public enum RenderingPath {Forward,DeferredShading}
}
