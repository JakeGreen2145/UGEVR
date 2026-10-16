namespace UnityEngine.UI
{
    // Controlled public API model. Native clip/shader behavior is not simulated.
    public interface IClippable {}
    public interface IMaterialModifier {}
    public interface IMeshModifier {}
    public class Graphic : UnityEngine.Behaviour
    {
        public static UnityEngine.Material Default=new UnityEngine.Material();
        public static int DefaultQueries;
        public static UnityEngine.Material defaultGraphicMaterial {get {++DefaultQueries;return Default;}}
        public new bool isActiveAndEnabled {get {return base.isActiveAndEnabled;} set {base.isActiveAndEnabled=value;}}
        UnityEngine.Color savedColor;
        public int ColorQueries;
        public UnityEngine.Color color {get {++ColorQueries;return savedColor;} set {savedColor=value;}}
        public int LazyOrMaterialQueries;
        UnityEngine.Canvas savedCanvas;UnityEngine.CanvasRenderer savedRenderer;UnityEngine.RectTransform savedRect;
        UnityEngine.Material savedMaterial=Default;
        public UnityEngine.Canvas canvas {get {++LazyOrMaterialQueries;return savedCanvas;} set {savedCanvas=value;}}
        public UnityEngine.CanvasRenderer canvasRenderer {get {++LazyOrMaterialQueries;return savedRenderer;} set {savedRenderer=value;}}
        public UnityEngine.RectTransform rectTransform {get {++LazyOrMaterialQueries;return savedRect;} set {savedRect=value;}}
        public UnityEngine.Material material {get {++LazyOrMaterialQueries;return savedMaterial;} set {savedMaterial=value;}}
    }
    public class Image : Graphic,IClippable,IMaterialModifier
    {
        UnityEngine.Sprite savedSprite;
        public int SpriteQueries;
        public UnityEngine.Sprite sprite {get {++SpriteQueries;return savedSprite;} set {savedSprite=value;}}
    }
    public class Text : Graphic,IClippable,IMaterialModifier {}
    public class Mask : UnityEngine.Component {}
    public class RectMask2D : UnityEngine.Component {}
}
