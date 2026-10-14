namespace UnityEngine.UI
{
    // Controlled public API model. Native clip/shader behavior is not simulated.
    public interface IClippable {}
    public interface IMaterialModifier {}
    public interface IMeshModifier {}
    public class Graphic : UnityEngine.Component
    {
        public static UnityEngine.Material Default=new UnityEngine.Material();
        public static int DefaultQueries;
        public static UnityEngine.Material defaultGraphicMaterial {get {++DefaultQueries;return Default;}}
        public bool isActiveAndEnabled {get;set;}=true;
        public UnityEngine.Canvas canvas {get;set;}
        public UnityEngine.CanvasRenderer canvasRenderer {get;set;}
        public UnityEngine.RectTransform rectTransform {get;set;}
        public UnityEngine.Material material {get;set;}=Default;
    }
    public class Image : Graphic,IClippable,IMaterialModifier {}
    public class Text : Graphic,IClippable,IMaterialModifier {}
    public class Mask : UnityEngine.Component {}
    public class RectMask2D : UnityEngine.Component {}
}
