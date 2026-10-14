using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace Ugevr
{
    // Experimental screen-space adapter. It borrows only explicitly selected
    // root overlay canvases during the existing synchronized eye pair.
    public sealed class OverlayCanvasScope : IDisposable
    {
        const int RootLimit=64,RectLimit=8192;
        readonly List<CanvasState> canvases=new List<CanvasState>();
        readonly List<RectState> rects=new List<RectState>();
        readonly List<string> errors=new List<string>();
        readonly string[] appliedEyes=new string[2];
        readonly Camera camera;
        readonly bool includeLayers;
        bool disposed,applied;
        public readonly float PlaneDistance;
        public readonly int OriginalMask;
        public int AppliedMask { get; private set; }
        public int OverlayLayerMask { get; private set; }
        public int DestroyedObjects { get; private set; }
        public int RestorationConflicts { get; private set; }
        public int SelectedCount { get { return canvases.Count; } }
        public string RestorationError { get { return String.Join("; ",errors.ToArray()); } }

        sealed class CanvasState
        {
            public Canvas Canvas;
            public string Name;
            public int Id;
            public RenderMode Mode;
            public Camera WorldCamera;
            public float Distance,ScaleFactor,ReferencePixels;
            public int SortingOrder,SortingLayer,TargetDisplay;
            public bool OverrideSorting,PixelPerfect,OverridePixelPerfect;
            public AdditionalCanvasShaderChannels ShaderChannels;
        }
        sealed class RectState
        {
            public RectTransform Rect;
            public Vector2 AnchorMin,AnchorMax,Pivot,Size;
            public Vector3 AnchoredPosition,LocalPosition,LocalScale;
            public Quaternion LocalRotation;
        }

        public OverlayCanvasScope(Camera camera,double unitsPerMeter,double planeDistanceMeters,bool includeLayers)
        {
            this.camera=camera; this.includeLayers=includeLayers;
            OriginalMask=camera.cullingMask; AppliedMask=OriginalMask;
            double near=camera.nearClipPlane,far=camera.farClipPlane;
            if (Double.IsNaN(planeDistanceMeters) || Double.IsInfinity(planeDistanceMeters) || planeDistanceMeters<=0 || far<=near)
                throw new ArgumentException("UI plane distance needs a finite positive distance and valid camera clipping.");
            double margin=Math.Min((far-near)*.1,Math.Max(.0001,unitsPerMeter*.01));
            PlaneDistance=(float)Math.Max(near+margin,Math.Min(far-margin,planeDistanceMeters*unitsPerMeter));
            var seen=new HashSet<int>();
            foreach (Canvas canvas in UnityEngine.Object.FindObjectsOfType(typeof(Canvas)))
            {
                if (canvas==null || !canvas.isActiveAndEnabled || !canvas.isRootCanvas || canvas.renderMode!=RenderMode.ScreenSpaceOverlay || canvas.targetDisplay!=camera.targetDisplay) continue;
                if (canvases.Count==RootLimit) throw new InvalidOperationException("Overlay UI exceeds 64 active roots; an explicit profile selection is required.");
                canvases.Add(new CanvasState {Canvas=canvas,Name=canvas.name,Id=canvas.GetInstanceID(),Mode=canvas.renderMode,
                    WorldCamera=canvas.worldCamera,Distance=canvas.planeDistance,ScaleFactor=canvas.scaleFactor,ReferencePixels=canvas.referencePixelsPerUnit,
                    SortingOrder=canvas.sortingOrder,SortingLayer=canvas.sortingLayerID,TargetDisplay=canvas.targetDisplay,
                    OverrideSorting=canvas.overrideSorting,PixelPerfect=canvas.pixelPerfect,OverridePixelPerfect=canvas.overridePixelPerfect,ShaderChannels=canvas.additionalShaderChannels});
                OverlayLayerMask|=1<<canvas.gameObject.layer;
                foreach (CanvasRenderer renderer in canvas.GetComponentsInChildren<CanvasRenderer>(true))
                    if (renderer!=null) OverlayLayerMask|=1<<renderer.gameObject.layer;
                foreach (RectTransform rect in canvas.GetComponentsInChildren<RectTransform>(true))
                {
                    if (rect==null || !seen.Add(rect.GetInstanceID())) continue;
                    if (rects.Count==RectLimit) throw new InvalidOperationException("Overlay UI exceeds 8192 RectTransforms; an explicit profile selection is required.");
                    rects.Add(new RectState {Rect=rect,AnchorMin=rect.anchorMin,AnchorMax=rect.anchorMax,Pivot=rect.pivot,Size=rect.sizeDelta,
                        AnchoredPosition=rect.anchoredPosition3D,LocalPosition=rect.localPosition,LocalScale=rect.localScale,LocalRotation=rect.localRotation});
                }
            }
        }

        public void ApplyEye(int eye=0)
        {
            if (disposed) throw new ObjectDisposedException("OverlayCanvasScope");
            if (canvases.Count==0) return;
            applied=true;
            AppliedMask=includeLayers?OriginalMask | OverlayLayerMask:OriginalMask;
            camera.cullingMask=AppliedMask;
            foreach (CanvasState state in canvases)
            {
                if (state.Canvas==null) continue;
                state.Canvas.renderMode=RenderMode.ScreenSpaceCamera;
                state.Canvas.worldCamera=camera;
                state.Canvas.planeDistance=PlaneDistance;
            }
            Canvas.ForceUpdateCanvases();
            if (eye>=0 && eye<2) appliedEyes[eye]=AppliedCanvasJson();
        }

        void Attempt(Action action,string name)
        {
            try { action(); }
            catch (Exception error) { Conflict(name+": "+error.Message); }
        }
        void Conflict(string text)
        {
            ++RestorationConflicts;
            if (errors.Count<64) errors.Add(text.Length>256?text.Substring(0,256):text);
        }
        public void Dispose()
        {
            if (disposed) return; disposed=true;
            if (!applied) return;
            foreach (CanvasState state in canvases)
            {
                Canvas canvas=state.Canvas;
                if (canvas==null) { ++DestroyedObjects; continue; }
                Attempt(delegate {canvas.renderMode=state.Mode;},state.Name+" render mode");
                Attempt(delegate {canvas.worldCamera=state.WorldCamera;},state.Name+" world camera");
                Attempt(delegate {canvas.planeDistance=state.Distance;},state.Name+" plane distance");
                Attempt(delegate {canvas.overrideSorting=state.OverrideSorting;},state.Name+" sorting override");
                Attempt(delegate {canvas.sortingLayerID=state.SortingLayer;},state.Name+" sorting layer");
                Attempt(delegate {canvas.sortingOrder=state.SortingOrder;},state.Name+" sorting order");
                Attempt(delegate {canvas.targetDisplay=state.TargetDisplay;},state.Name+" display");
                Attempt(delegate {canvas.pixelPerfect=state.PixelPerfect;},state.Name+" pixel perfect");
                Attempt(delegate {canvas.overridePixelPerfect=state.OverridePixelPerfect;},state.Name+" pixel perfect override");
                Attempt(delegate {canvas.additionalShaderChannels=state.ShaderChannels;},state.Name+" shader channels");
                Attempt(delegate {canvas.scaleFactor=state.ScaleFactor;},state.Name+" scale factor");
                Attempt(delegate {canvas.referencePixelsPerUnit=state.ReferencePixels;},state.Name+" reference pixels");
            }
            Attempt(delegate { Canvas.ForceUpdateCanvases(); },"restore canvas geometry");
            foreach (RectState state in rects)
            {
                RectTransform rect=state.Rect;
                if (rect==null) { ++DestroyedObjects; continue; }
                Attempt(delegate {rect.anchorMin=state.AnchorMin;},"rect anchor min");
                Attempt(delegate {rect.anchorMax=state.AnchorMax;},"rect anchor max");
                Attempt(delegate {rect.pivot=state.Pivot;},"rect pivot");
                Attempt(delegate {rect.sizeDelta=state.Size;},"rect size");
                Attempt(delegate {rect.anchoredPosition3D=state.AnchoredPosition;},"rect anchored position");
                Attempt(delegate {rect.localRotation=state.LocalRotation;},"rect rotation");
                Attempt(delegate {rect.localScale=state.LocalScale;},"rect scale");
                Attempt(delegate {rect.localPosition=state.LocalPosition;},"rect local position");
                Attempt(delegate {
                    if (!rect.anchorMin.Equals(state.AnchorMin) || !rect.anchorMax.Equals(state.AnchorMax) || !rect.pivot.Equals(state.Pivot) ||
                        !rect.sizeDelta.Equals(state.Size) || !rect.anchoredPosition3D.Equals(state.AnchoredPosition) ||
                        !rect.localPosition.Equals(state.LocalPosition) || !rect.localScale.Equals(state.LocalScale) || !rect.localRotation.Equals(state.LocalRotation))
                        Conflict("RectTransform did not retain its restored values.");
                },"verify rect restoration");
            }
            foreach (CanvasState state in canvases)
            {
                if (state.Canvas==null) continue;
                Attempt(delegate {
                    Canvas canvas=state.Canvas;
                    if (canvas.renderMode!=state.Mode || canvas.worldCamera!=state.WorldCamera || canvas.planeDistance!=state.Distance ||
                        canvas.overrideSorting!=state.OverrideSorting || canvas.sortingLayerID!=state.SortingLayer || canvas.sortingOrder!=state.SortingOrder ||
                        canvas.targetDisplay!=state.TargetDisplay || canvas.pixelPerfect!=state.PixelPerfect || canvas.overridePixelPerfect!=state.OverridePixelPerfect ||
                        canvas.additionalShaderChannels!=state.ShaderChannels || canvas.scaleFactor!=state.ScaleFactor || canvas.referencePixelsPerUnit!=state.ReferencePixels)
                        Conflict(state.Name+" canvas did not retain its restored values.");
                },state.Name+" verify restoration");
            }
            if (camera!=null) Attempt(delegate {camera.cullingMask=OriginalMask;},"restore camera UI layers");
        }
        static string Quote(string value)
        {
            if (value==null) return "null";
            if (value.Length>256) value=value.Substring(0,256);
            var text=new StringBuilder("\"");
            foreach (char c in value)
            {
                if (c=='"' || c=='\\') text.Append('\\').Append(c);
                else if (c<32) text.Append("\\u").Append(((int)c).ToString("x4"));
                else text.Append(c);
            }
            return text.Append('"').ToString();
        }
        static string Number(float value) { return Single.IsNaN(value) || Single.IsInfinity(value)?"null":value.ToString("R",CultureInfo.InvariantCulture); }
        static string Vector(Vector3 v) { return "["+Number(v.x)+","+Number(v.y)+","+Number(v.z)+"]"; }
        string AppliedCanvasJson()
        {
            var text=new StringBuilder("[");
            for (int i=0;i<canvases.Count;++i)
            {
                if (i>0) text.Append(','); Canvas canvas=canvases[i].Canvas;
                if (canvas==null) {text.Append("null");continue;}
                RectTransform root=canvas.transform as RectTransform;
                text.Append("{\"name\":").Append(Quote(canvases[i].Name)).Append(",\"mode\":").Append(Quote(canvas.renderMode.ToString()))
                    .Append(",\"worldCamera\":").Append(canvas.worldCamera==null?"null":Quote(canvas.worldCamera.name))
                    .Append(",\"planeDistance\":").Append(Number(canvas.planeDistance)).Append(",\"scaleFactor\":").Append(Number(canvas.scaleFactor))
                    .Append(",\"pixelRect\":[").Append(Number(canvas.pixelRect.x)).Append(',').Append(Number(canvas.pixelRect.y)).Append(',')
                    .Append(Number(canvas.pixelRect.width)).Append(',').Append(Number(canvas.pixelRect.height)).Append(']');
                if (root!=null) text.Append(",\"rootPosition\":").Append(Vector(root.position)).Append(",\"rootRotation\":[")
                    .Append(Number(root.rotation.x)).Append(',').Append(Number(root.rotation.y)).Append(',').Append(Number(root.rotation.z)).Append(',').Append(Number(root.rotation.w))
                    .Append("],\"rootLocalScale\":").Append(Vector(root.localScale)).Append(",\"rootSize\":[").Append(Number(root.sizeDelta.x)).Append(',').Append(Number(root.sizeDelta.y)).Append(']');
                text.Append('}');
            }
            return text.Append(']').ToString();
        }
        public string DiagnosticJson()
        {
            var text=new StringBuilder("{\"adapter\":\"experimental-overlay-camera\",\"planeDistance\":").Append(PlaneDistance.ToString("R",CultureInfo.InvariantCulture))
                .Append(",\"originalMask\":").Append(OriginalMask).Append(",\"appliedMask\":").Append(AppliedMask)
                .Append(",\"overlayLayerMask\":").Append(OverlayLayerMask).Append(",\"excludedOverlayLayers\":").Append(OverlayLayerMask & ~AppliedMask)
                .Append(",\"includeOverlayLayers\":").Append(includeLayers?"true":"false").Append(",\"selectedCount\":").Append(canvases.Count)
                .Append(",\"rectCount\":").Append(rects.Count).Append(",\"destroyedObjects\":").Append(DestroyedObjects)
                .Append(",\"restorationConflicts\":").Append(RestorationConflicts).Append(",\"errors\":[");
            for (int i=0;i<errors.Count;++i) { if (i>0) text.Append(','); text.Append(Quote(errors[i])); }
            text.Append("],\"selectedRoots\":[");
            for (int i=0;i<canvases.Count;++i)
            {
                if (i>0) text.Append(','); var state=canvases[i];
                text.Append("{\"name\":").Append(Quote(state.Name)).Append(",\"instanceId\":").Append(state.Id)
                    .Append(",\"originalMode\":").Append(Quote(state.Mode.ToString())).Append(",\"originalPlaneDistance\":").Append(Number(state.Distance))
                    .Append(",\"sortingOrder\":").Append(state.SortingOrder).Append('}');
            }
            return text.Append("],\"appliedEyes\":[").Append(appliedEyes[0] ?? "null").Append(',').Append(appliedEyes[1] ?? "null").Append("]}").ToString();
        }
    }
}
