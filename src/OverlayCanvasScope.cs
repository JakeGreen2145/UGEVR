using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Reflection;
using UnityEngine;

namespace Ugevr
{
    // Experimental screen-space adapter. It borrows only explicitly selected
    // root overlay canvases during the existing synchronized eye pair.
    public sealed class OverlayCanvasScope : IDisposable
    {
        const int RootLimit=64,RectLimit=8192;
        readonly List<CanvasState> canvases=new List<CanvasState>();
        readonly List<CanvasState> inactiveCanvases=new List<CanvasState>();
        readonly List<RectState> rects=new List<RectState>();
        readonly List<string> errors=new List<string>();
        readonly List<string> rectMismatches=new List<string>();
        readonly UiLayoutDiagnostics layoutInputs;
        readonly List<int> layoutContextIds=new List<int>();
        string layoutDiagnosticError;
        readonly string[] appliedEyes=new string[2];
        readonly string[] afterRenderEyes=new string[2];
        readonly List<string> geometryChanges=new List<string>();
        readonly List<GameObject> isolatedObjects=new List<GameObject>();
        readonly Camera camera;
        readonly bool includeLayers;
        OverlayLayerLease layerLease;
        Camera uiCamera;
        readonly double unitsPerMeter;
        readonly double panelWidthMeters;
        RigidPose commonHead;
        bool commonHeadSet;
        bool disposed,applied;
        string verificationPhase="scope-dispose";
        public readonly float PlaneDistance;
        public readonly int OriginalMask;
        public int AppliedMask { get; private set; }
        public int OverlayLayerMask { get; private set; }
        public int DestroyedObjects { get; private set; }
        public int RestorationConflicts { get; private set; }
        public int DerivedPositionRoundoff { get; private set; }
        public int RotationRoundoff { get; private set; }
        public int UnexpectedGeometryChanges { get; private set; }
        public int SelectedCameraRectRestorations { get; private set; }
        public int InactiveCanvasValidationPasses {get;private set;}
        public int SelectedCount { get { return canvases.Count; } }
        public int IsolatedMask {get {return layerLease==null?0:layerLease.Mask;}}
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
            public RectTransform Root;
            public float Width,Height,ParentScale;
            public Vector2 Pivot;
            public string OriginalUiContext;
        }
        sealed class RectState
        {
            public RectTransform Rect;
            public string Name;
            public int Id;
            public Vector2 AnchorMin,AnchorMax,Pivot,Size;
            public Vector3 AnchoredPosition,LocalPosition,LocalScale;
            public Quaternion LocalRotation;
        }

        public OverlayCanvasScope(Camera camera,double unitsPerMeter,double planeDistanceMeters,bool includeLayers,double panelWidthMeters=1.8,bool captureLayoutInputs=false,string layoutInputRootRectIds=null)
        {
            if(captureLayoutInputs) layoutInputs=new UiLayoutDiagnostics(layoutInputRootRectIds);
            this.camera=camera; this.includeLayers=includeLayers;
            this.unitsPerMeter=unitsPerMeter;this.panelWidthMeters=panelWidthMeters;
            OriginalMask=camera.cullingMask; AppliedMask=OriginalMask;
            double near=camera.nearClipPlane,far=camera.farClipPlane;
            if (Double.IsNaN(planeDistanceMeters) || Double.IsInfinity(planeDistanceMeters) || planeDistanceMeters<=0 || far<=near)
                throw new ArgumentException("UI plane distance needs a finite positive distance and valid camera clipping.");
            double margin=Math.Min((far-near)*.1,Math.Max(.0001,unitsPerMeter*.01));
            PlaneDistance=(float)Math.Max(near+margin,Math.Min(far-margin,planeDistanceMeters*unitsPerMeter));
            OverlayPanelGeometry.PixelScale(panelWidthMeters,1,1);
            var seen=new HashSet<int>();
            var inactiveSeen=new HashSet<int>();
            foreach (Canvas canvas in UnityEngine.Object.FindObjectsOfType(typeof(Canvas)))
            {
                if (canvas==null || !canvas.isActiveAndEnabled || !canvas.isRootCanvas || canvas.renderMode!=RenderMode.ScreenSpaceOverlay || canvas.targetDisplay!=camera.targetDisplay) continue;
                if (canvases.Count==RootLimit) throw new InvalidOperationException("Overlay UI exceeds 64 active roots; an explicit profile selection is required.");
                RectTransform root=canvas.transform as RectTransform;
                if (root==null || root.rect.width<=0 || root.rect.height<=0) throw new InvalidOperationException("Overlay "+canvas.name+" has no usable desktop rectangle.");
                float parentScale=1;
                if (root.parent!=null)
                {
                    Vector3 scale=root.parent.lossyScale;
                    float tolerance=Math.Max(.000001f,Math.Abs(scale.x)*.00001f);
                    if (scale.x<=0 || Math.Abs(scale.x-scale.y)>tolerance || Math.Abs(scale.x-scale.z)>tolerance)
                        throw new InvalidOperationException("Overlay "+canvas.name+" needs an explicit parent adapter: only positive uniform parent scale is supported by the common panel.");
                    parentScale=scale.x;
                }
                canvases.Add(new CanvasState {Canvas=canvas,Name=canvas.name,Id=canvas.GetInstanceID(),Mode=canvas.renderMode,
                    WorldCamera=canvas.worldCamera,Distance=canvas.planeDistance,ScaleFactor=canvas.scaleFactor,ReferencePixels=canvas.referencePixelsPerUnit,
                    SortingOrder=canvas.sortingOrder,SortingLayer=canvas.sortingLayerID,TargetDisplay=canvas.targetDisplay,
                    OverrideSorting=canvas.overrideSorting,PixelPerfect=canvas.pixelPerfect,OverridePixelPerfect=canvas.overridePixelPerfect,ShaderChannels=canvas.additionalShaderChannels,
                    Root=root,Width=root.rect.width,Height=root.rect.height,Pivot=root.pivot,ParentScale=parentScale,OriginalUiContext=UiContext(canvas)});
                OverlayLayerMask|=1<<canvas.gameObject.layer;
                isolatedObjects.Add(canvas.gameObject);
                foreach (CanvasRenderer renderer in canvas.GetComponentsInChildren<CanvasRenderer>(true))
                    if (renderer!=null) {OverlayLayerMask|=1<<renderer.gameObject.layer;isolatedObjects.Add(renderer.gameObject);}
                foreach(Canvas nested in canvas.GetComponentsInChildren<Canvas>(true))
                {
                    if(nested==null) continue;
                    if(nested!=canvas && nested.isRootCanvas)
                    {
                        if(!nested.isActiveAndEnabled)
                        {
                            if(inactiveSeen.Add(nested.GetInstanceID()))
                            {
                                if(inactiveCanvases.Count==RootLimit) throw new InvalidOperationException("Overlay UI exceeds 64 protected inactive canvas boundaries; use an explicit UI adapter.");
                                if(HasActiveWorldRenderer(nested))
                                    throw new InvalidOperationException("Inactive canvas "+BoundedName(nested.name)+" contains an active world renderer; its hierarchy needs an explicit UI adapter.");
                                inactiveCanvases.Add(SnapshotInactiveCanvas(nested));
                            }
                        }
                        else
                        {
                            throw new InvalidOperationException("Overlay "+BoundedName(canvas.name)+" contains independent canvas "+BoundedName(nested.name)+
                                " id="+nested.GetInstanceID()+" mode="+nested.renderMode+" active="+nested.isActiveAndEnabled+" display="+nested.targetDisplay+
                                "; an explicit UI adapter is required before borrowing its hierarchy.");
                        }
                    }
                    isolatedObjects.Add(nested.gameObject);
                }
                foreach (RectTransform rect in canvas.GetComponentsInChildren<RectTransform>(true))
                {
                    if (rect==null || !seen.Add(rect.GetInstanceID())) continue;
                    if (rects.Count==RectLimit) throw new InvalidOperationException("Overlay UI exceeds 8192 RectTransforms; an explicit profile selection is required.");
                    var saved=new RectState {Rect=rect,Name=rect.name,Id=rect.GetInstanceID(),AnchorMin=rect.anchorMin,AnchorMax=rect.anchorMax,Pivot=rect.pivot,Size=rect.sizeDelta,
                        AnchoredPosition=rect.anchoredPosition3D,LocalPosition=rect.localPosition,LocalScale=rect.localScale,LocalRotation=rect.localRotation};
                    rects.Add(saved);
                    if(layoutInputs!=null) layoutInputs.RegisterSnapshot(rect,canvas,saved.Id,saved.Name);
                }
            }
            if(layoutInputs!=null) layoutInputs.FinishAdmission();
            CaptureLayoutPhase(0);
        }

        public void ApplyEye(int eye=0)
        {
            if (disposed) throw new ObjectDisposedException("OverlayCanvasScope");
            if (canvases.Count==0) return;
            if (!commonHeadSet) throw new InvalidOperationException("Common UI head pose must be set before rendering either eye.");
            if (!applied) PrepareCommonPanel();
            if(eye>=0 && eye<2) CaptureLayoutPhase(eye==0?1:3);
            if (eye>=0 && eye<2) appliedEyes[eye]=AppliedCanvasJson();
        }
        public void SetCommonHead(RigidPose head) {if (applied) throw new InvalidOperationException("Common panel pose cannot change between eyes.");commonHead=head;commonHeadSet=true;}
        public void UseAfterEffectsCamera(Camera ui,int isolationLayer=-1)
        {
            if(applied) throw new InvalidOperationException("UI pass selection cannot change between eyes.");
            if(canvases.Count==0) return;
            layerLease=new OverlayLayerLease(isolatedObjects,isolationLayer);uiCamera=ui;
        }
        public void ValidateLayerExclusive() {if(layerLease!=null) layerLease.ValidateExclusive();}
        public void ValidateInactiveCanvases(string stage)
        {
            foreach(CanvasState state in inactiveCanvases)
            {
                Canvas canvas=state.Canvas;
                if(canvas==null) continue;
                string reason;
                try {reason=canvas.isActiveAndEnabled?"became active":(!CanvasSettingsEqual(state)?"changed its protected settings":HasActiveWorldRenderer(canvas)?"contains an active world renderer":null);}
                catch(Exception queryError) {reason="could not be validated: "+BoundedName(queryError.Message);}
                if(reason==null) continue;
                string error="Protected inactive canvas "+BoundedName(state.Name)+" id="+state.Id+" mode="+state.Mode+" "+reason+" during "+stage+"; the eye pair cannot be submitted. Its current activity is preserved.";
                Conflict(error);
                throw new InvalidOperationException(error);
            }
            ++InactiveCanvasValidationPasses;
        }
        public void RecordAfterRender(int eye)
        {
            if (eye<0 || eye>1 || !applied) return;
            CaptureLayoutPhase(eye==0?2:4);
            afterRenderEyes[eye]=AppliedCanvasJson();
            if (afterRenderEyes[eye]!=appliedEyes[eye])
            {
                ++UnexpectedGeometryChanges;
                if (geometryChanges.Count<2) geometryChanges.Add("eye "+eye+" canvas state changed during Camera.Render; compare appliedEyes and afterRenderEyes.");
            }
        }
        void PrepareCommonPanel()
        {
            applied=true;
            ValidateInactiveCanvases("before UI borrowing");
            if(layerLease!=null) layerLease.Apply();
            AppliedMask=layerLease!=null?OriginalMask & ~layerLease.Mask:(includeLayers?OriginalMask | OverlayLayerMask:OriginalMask);
            camera.cullingMask=AppliedMask;
            foreach (CanvasState state in canvases)
            {
                if (state.Canvas==null) continue;
                state.Canvas.renderMode=RenderMode.WorldSpace;
                state.Canvas.worldCamera=uiCamera ?? camera;
                state.Canvas.planeDistance=PlaneDistance;
            }
            Canvas.ForceUpdateCanvases();
            foreach (CanvasState state in canvases)
            {
                if (state.Canvas==null || state.Root==null) continue;
                // Keep the desktop's logical aspect and dimensions. No eye viewport
                // participates in layout or physical placement.
                state.Root.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,state.Width);
                state.Root.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,state.Height);
                double width=panelWidthMeters*unitsPerMeter;
                RigidPose panel=OverlayPanelGeometry.Place(commonHead,PlaneDistance,width,state.Width/state.Height,state.Pivot.x,state.Pivot.y);
                state.Root.position=new Vector3((float)panel.Position.X,(float)panel.Position.Y,(float)panel.Position.Z);
                state.Root.rotation=new Quaternion((float)panel.Rotation.X,(float)panel.Rotation.Y,(float)panel.Rotation.Z,(float)panel.Rotation.W);
                float scale=(float)OverlayPanelGeometry.PixelScale(width,state.Width,state.ParentScale);
                state.Root.localScale=new Vector3(scale,scale,scale);
            }
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
            if(layerLease!=null)
            {
                layerLease.Dispose();DestroyedObjects+=layerLease.DestroyedObjects;
                foreach(string error in layerLease.Errors) Conflict(error);
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
                Attempt(delegate {rect.localRotation=state.LocalRotation;},"rect rotation");
                Attempt(delegate {rect.localScale=state.LocalScale;},"rect scale");
                // Anchored and local position are coupled. Assigning local XY after
                // anchored XY subtracts the anchor reference again and loses bits.
                // Restore the authoritative anchored value once, including Z.
                Attempt(delegate {rect.anchoredPosition3D=state.AnchoredPosition;},"rect anchored position");
                Attempt(delegate {
                    Verify(state,"anchorMin",Values(state.AnchorMin),Values(rect.anchorMin));
                    Verify(state,"anchorMax",Values(state.AnchorMax),Values(rect.anchorMax));
                    Verify(state,"pivot",Values(state.Pivot),Values(rect.pivot));
                    Verify(state,"sizeDelta",Values(state.Size),Values(rect.sizeDelta));
                    Verify(state,"anchoredPosition3D",Values(state.AnchoredPosition),Values(rect.anchoredPosition3D));
                    Verify(state,"localPosition",Values(state.LocalPosition),Values(rect.localPosition),true);
                    Verify(state,"localScale",Values(state.LocalScale),Values(rect.localScale));
                    VerifyRotation(state,Values(state.LocalRotation),Values(rect.localRotation));
                },"verify rect restoration "+state.Name);
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
            CaptureLayoutPhase(5);
        }
        void CaptureLayoutPhase(int phase)
        {
            if(layoutInputs==null) return;
            try {layoutInputs.Capture(phase);}
            catch(Exception diagnosticError) {layoutDiagnosticError=BoundedName(diagnosticError.Message);}
        }
        void NoteLayoutContext(int rectId)
        {
            if(layoutInputs!=null && layoutContextIds.Count<64 && !layoutContextIds.Contains(rectId)) layoutContextIds.Add(rectId);
        }
        static CanvasState SnapshotInactiveCanvas(Canvas canvas)
        {
            return new CanvasState {Canvas=canvas,Name=canvas.name,Id=canvas.GetInstanceID(),Mode=canvas.renderMode,
                WorldCamera=canvas.worldCamera,Distance=canvas.planeDistance,ScaleFactor=canvas.scaleFactor,ReferencePixels=canvas.referencePixelsPerUnit,
                SortingOrder=canvas.sortingOrder,SortingLayer=canvas.sortingLayerID,TargetDisplay=canvas.targetDisplay,
                OverrideSorting=canvas.overrideSorting,PixelPerfect=canvas.pixelPerfect,OverridePixelPerfect=canvas.overridePixelPerfect,ShaderChannels=canvas.additionalShaderChannels};
        }
        static bool HasActiveWorldRenderer(Canvas canvas)
        {
            Renderer[] renderers=canvas.GetComponentsInChildren<Renderer>(true);
            if(renderers.Length>RectLimit) throw new InvalidOperationException("Inactive canvas renderer query exceeds 8192 objects; use an explicit UI adapter.");
            foreach(Renderer renderer in renderers)
                if(renderer!=null && renderer.enabled && renderer.gameObject.activeInHierarchy) return true;
            return false;
        }
        static bool CanvasSettingsEqual(CanvasState state)
        {
            Canvas canvas=state.Canvas;
            return canvas.renderMode==state.Mode && canvas.worldCamera==state.WorldCamera && canvas.planeDistance==state.Distance &&
                canvas.overrideSorting==state.OverrideSorting && canvas.sortingLayerID==state.SortingLayer && canvas.sortingOrder==state.SortingOrder &&
                canvas.targetDisplay==state.TargetDisplay && canvas.pixelPerfect==state.PixelPerfect && canvas.overridePixelPerfect==state.OverridePixelPerfect &&
                canvas.additionalShaderChannels==state.ShaderChannels && canvas.scaleFactor==state.ScaleFactor && canvas.referencePixelsPerUnit==state.ReferencePixels;
        }
        public void RebuildRestoredGeometry()
        {
            if(!disposed || !applied) return;
            // Called after the surrounding CameraState restored the desktop camera.
            verificationPhase="after-camera-restore";
            RectTransform cameraRect=camera==null?null:camera.transform as RectTransform;
            if(cameraRect!=null)
            {
                foreach(RectState state in rects)
                {
                    if(state.Rect==null || state.Rect!=cameraRect) continue;
                    // CameraState writes world position/rotation after this scope
                    // restored local UI values. World-to-local float cancellation
                    // can lose a small anchored offset. Restore its saved canonical
                    // local pose last; no changed local value is accepted as roundoff.
                    Attempt(delegate {cameraRect.localRotation=state.LocalRotation;},"final selected camera rect rotation "+state.Name);
                    Attempt(delegate {cameraRect.anchoredPosition3D=state.AnchoredPosition;},"final selected camera rect anchored position "+state.Name);
                    ++SelectedCameraRectRestorations;
                    break;
                }
            }
            CaptureLayoutPhase(6);
            Attempt(delegate {Canvas.ForceUpdateCanvases();},"final desktop canvas rebuild");
            CaptureLayoutPhase(7);
            if(layerLease!=null) Attempt(delegate {layerLease.ValidateExclusive();},"isolated UI layer exclusivity after desktop restoration");
            foreach(RectState state in rects)
            {
                if(state.Rect==null) continue;
                Attempt(delegate {
                    Verify(state,"anchorMin",Values(state.AnchorMin),Values(state.Rect.anchorMin));
                    Verify(state,"anchorMax",Values(state.AnchorMax),Values(state.Rect.anchorMax));
                    Verify(state,"pivot",Values(state.Pivot),Values(state.Rect.pivot));
                    Verify(state,"sizeDelta",Values(state.Size),Values(state.Rect.sizeDelta));
                    Verify(state,"anchoredPosition3D",Values(state.AnchoredPosition),Values(state.Rect.anchoredPosition3D));
                    Verify(state,"localPosition",Values(state.LocalPosition),Values(state.Rect.localPosition),true);
                    Verify(state,"localScale",Values(state.LocalScale),Values(state.Rect.localScale));
                    VerifyRotation(state,Values(state.LocalRotation),Values(state.Rect.localRotation));
                },"verify final desktop rectangle "+state.Name);
            }
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
        static string BoundedName(string value) {return value==null?"<unnamed>":value.Substring(0,Math.Min(64,value.Length));}
        static string Number(float value) { return Single.IsNaN(value) || Single.IsInfinity(value)?"null":value.ToString("R",CultureInfo.InvariantCulture); }
        static string UiContext(Canvas canvas)
        {
            var text=new StringBuilder("[");int count=0,scanned=0;
            foreach(Component component in canvas.GetComponents<Component>())
            {
                if(++scanned>64) break;
                if(component==null) continue;
                string type=component.GetType().FullName;
                string[] properties;
                if(type=="UnityEngine.UI.CanvasScaler") properties=new[]{"enabled","uiScaleMode","referenceResolution","screenMatchMode","matchWidthOrHeight","scaleFactor","referencePixelsPerUnit","dynamicPixelsPerUnit"};
                else if(type=="UnityEngine.UI.GraphicRaycaster") properties=new[]{"enabled","ignoreReversedGraphics","blockingObjects","blockingMask","eventCamera"};
                else continue;
                if(count++>0) text.Append(',');text.Append("{\"type\":").Append(Quote(type));
                foreach(string name in properties)
                {
                    text.Append(',').Append(Quote(name)).Append(':');
                    try
                    {
                        PropertyInfo property=component.GetType().GetProperty(name,BindingFlags.Public|BindingFlags.Instance);
                        object value=property==null?null:property.GetValue(component,null);
                        var engineObject=value as UnityEngine.Object;
                        text.Append(value==null?"null":Quote(engineObject!=null?engineObject.name:Convert.ToString(value,CultureInfo.InvariantCulture)));
                    }
                    catch {text.Append("null");}
                }
                text.Append('}');
            }
            return text.Append(']').ToString();
        }
        static float[] Values(Vector2 value) { return new[]{value.x,value.y}; }
        static float[] Values(Vector3 value) { return new[]{value.x,value.y,value.z}; }
        static float[] Values(Quaternion value) { return new[]{value.x,value.y,value.z,value.w}; }
        static string Numbers(float[] values)
        {
            var text=new StringBuilder("[");
            for (int i=0;i<values.Length;++i) {if (i>0) text.Append(',');text.Append(Number(values[i]));}
            return text.Append(']').ToString();
        }
        static double Ulp(float value)
        {
            int bits=BitConverter.ToInt32(BitConverter.GetBytes(Math.Abs(value)),0);
            int exponent=(bits>>23)&255;
            return exponent==0?Math.Pow(2,-149):Math.Pow(2,exponent-127-23);
        }
        void Verify(RectState state,string field,float[] expected,float[] observed,bool derivedPosition=false)
        {
            bool mismatch=false,finite=true,accepted=true;double maximum=0,tolerance=0;
            for (int i=0;i<expected.Length;++i)
            {
                mismatch|=expected[i]!=observed[i];
                finite&=!Single.IsNaN(expected[i]) && !Single.IsInfinity(expected[i]) && !Single.IsNaN(observed[i]) && !Single.IsInfinity(observed[i]);
                double componentDelta=Math.Abs((double)expected[i]-observed[i]);
                double componentTolerance=derivedPosition?4*Math.Max(Ulp(expected[i]),Ulp(observed[i])):0;
                maximum=Math.Max(maximum,componentDelta);tolerance=Math.Max(tolerance,componentTolerance);
                accepted&=componentDelta<=componentTolerance;
            }
            if (!mismatch) return;
            NoteLayoutContext(state.Id);
            string delta=finite?maximum.ToString("R",CultureInfo.InvariantCulture):"null";
            string name=state.Name==null?"":state.Name.Substring(0,Math.Min(64,state.Name.Length));
            accepted&=finite;
            if (accepted) ++DerivedPositionRoundoff;
            else Conflict("Rect "+name+" id="+state.Id+" "+field+" expected="+Numbers(expected)+" observed="+Numbers(observed)+" maxDelta="+delta);
            if (rectMismatches.Count<64) rectMismatches.Add("{\"name\":"+Quote(name)+",\"instanceId\":"+state.Id+",\"field\":"+Quote(field)+
                ",\"verificationPhase\":"+Quote(verificationPhase)+",\"expected\":"+Numbers(expected)+",\"observed\":"+Numbers(observed)+",\"finite\":"+(finite?"true":"false")+
                ",\"maxAbsoluteDelta\":"+delta+",\"acceptedTolerance\":"+tolerance.ToString("R",CultureInfo.InvariantCulture)+",\"acceptedDerivedRoundoff\":"+(accepted?"true":"false")+"}");
        }
        void VerifyRotation(RectState state,float[] expected,float[] observed)
        {
            const double unitTolerance=.000002;
            double expectedNorm=0,observedNorm=0,dot=0;bool finite=true,changed=false;
            for (int i=0;i<4;++i)
            {
                finite&=!Single.IsNaN(expected[i]) && !Single.IsInfinity(expected[i]) && !Single.IsNaN(observed[i]) && !Single.IsInfinity(observed[i]);
                expectedNorm+=(double)expected[i]*expected[i];observedNorm+=(double)observed[i]*observed[i];dot+=(double)expected[i]*observed[i];
                changed|=expected[i]!=observed[i];
            }
            bool unit=finite && Math.Abs(expectedNorm-1)<=unitTolerance && Math.Abs(observedNorm-1)<=unitTolerance;
            if (!changed && unit) return;
            NoteLayoutContext(state.Id);
            bool signAligned=dot<0,accepted=unit;double maximum=0,tolerance=0;
            for (int i=0;i<4;++i)
            {
                float aligned=signAligned?-observed[i]:observed[i];
                double componentDelta=Math.Abs((double)expected[i]-aligned);
                double componentTolerance=unit?4*Math.Max(Ulp(expected[i]),Ulp(aligned)):0;
                maximum=Math.Max(maximum,componentDelta);tolerance=Math.Max(tolerance,componentTolerance);
                accepted&=componentDelta<=componentTolerance;
            }
            string name=state.Name==null?"":state.Name.Substring(0,Math.Min(64,state.Name.Length));
            string delta=finite?maximum.ToString("R",CultureInfo.InvariantCulture):"null";
            if (accepted) ++RotationRoundoff;
            else Conflict("Rect "+name+" id="+state.Id+" localRotation expected="+Numbers(expected)+" observed="+Numbers(observed)+" unit="+unit+" maxDelta="+delta);
            if (rectMismatches.Count<64) rectMismatches.Add("{\"name\":"+Quote(name)+",\"instanceId\":"+state.Id+",\"field\":\"localRotation\",\"expected\":"+Numbers(expected)+
                ",\"verificationPhase\":"+Quote(verificationPhase)+",\"observed\":"+Numbers(observed)+",\"finite\":"+(finite?"true":"false")+",\"unitQuaternion\":"+(unit?"true":"false")+
                ",\"expectedNormSquared\":"+(finite?expectedNorm.ToString("R",CultureInfo.InvariantCulture):"null")+
                ",\"observedNormSquared\":"+(finite?observedNorm.ToString("R",CultureInfo.InvariantCulture):"null")+
                ",\"unitNormSquaredTolerance\":0.000002,\"hemisphereAligned\":"+(signAligned?"true":"false")+",\"maxAbsoluteDelta\":"+delta+
                ",\"acceptedTolerance\":"+tolerance.ToString("R",CultureInfo.InvariantCulture)+",\"acceptedRotationRoundoff\":"+(accepted?"true":"false")+"}");
        }
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
                    .Append(",\"engineUiContext\":").Append(UiContext(canvas))
                    .Append(",\"pixelRect\":[").Append(Number(canvas.pixelRect.x)).Append(',').Append(Number(canvas.pixelRect.y)).Append(',')
                    .Append(Number(canvas.pixelRect.width)).Append(',').Append(Number(canvas.pixelRect.height)).Append(']');
                if (root!=null) text.Append(",\"rootPosition\":").Append(Vector(root.position)).Append(",\"rootRotation\":[")
                    .Append(Number(root.rotation.x)).Append(',').Append(Number(root.rotation.y)).Append(',').Append(Number(root.rotation.z)).Append(',').Append(Number(root.rotation.w))
                    .Append("],\"rootLocalScale\":").Append(Vector(root.localScale)).Append(",\"rootWorldScale\":").Append(Vector(root.lossyScale))
                    .Append(",\"rootSize\":[").Append(Number(root.sizeDelta.x)).Append(',').Append(Number(root.sizeDelta.y)).Append(']')
                    .Append(",\"rootRectSize\":[").Append(Number(root.rect.width)).Append(',').Append(Number(root.rect.height)).Append(']');
                text.Append('}');
            }
            return text.Append(']').ToString();
        }
        public string DiagnosticJson()
        {
            var text=new StringBuilder("{\"adapter\":\"experimental-overlay-common-panel\",\"panelWidthMeters\":").Append(panelWidthMeters.ToString("R",CultureInfo.InvariantCulture))
                .Append(",\"planeDistance\":").Append(PlaneDistance.ToString("R",CultureInfo.InvariantCulture))
                .Append(",\"commonHeadPosition\":").Append(Vector(new Vector3((float)commonHead.Position.X,(float)commonHead.Position.Y,(float)commonHead.Position.Z)))
                .Append(",\"layout\":\"original desktop logical dimensions and aspect\"")
                .Append(",\"afterImageEffects\":").Append(layerLease==null?"false":"true")
                .Append(",\"isolatedLayer\":").Append(layerLease==null?-1:layerLease.Layer)
                .Append(",\"isolatedObjectCount\":").Append(layerLease==null?0:layerLease.ObjectCount)
                .Append(",\"protectedInactiveCanvasCount\":").Append(inactiveCanvases.Count)
                .Append(",\"inactiveCanvasValidationPasses\":").Append(InactiveCanvasValidationPasses)
                .Append(",\"originalMask\":").Append(OriginalMask).Append(",\"appliedMask\":").Append(AppliedMask)
                .Append(",\"overlayLayerMask\":").Append(OverlayLayerMask).Append(",\"excludedOverlayLayers\":").Append(OverlayLayerMask & ~AppliedMask)
                .Append(",\"includeOverlayLayers\":").Append(includeLayers?"true":"false").Append(",\"selectedCount\":").Append(canvases.Count)
                .Append(",\"rectCount\":").Append(rects.Count).Append(",\"destroyedObjects\":").Append(DestroyedObjects)
                .Append(",\"restorationConflicts\":").Append(RestorationConflicts).Append(",\"derivedPositionRoundoff\":").Append(DerivedPositionRoundoff)
                .Append(",\"selectedCameraRectRestorations\":").Append(SelectedCameraRectRestorations)
                .Append(",\"rotationRoundoff\":").Append(RotationRoundoff).Append(",\"unexpectedGeometryChanges\":").Append(UnexpectedGeometryChanges).Append(",\"geometryChangeNotes\":[");
            for(int i=0;i<geometryChanges.Count;++i) {if(i>0)text.Append(',');text.Append(Quote(geometryChanges[i]));}
            text.Append("],\"errors\":[");
            for (int i=0;i<errors.Count;++i) { if (i>0) text.Append(','); text.Append(Quote(errors[i])); }
            text.Append("],\"rectMismatches\":[").Append(String.Join(",",rectMismatches.ToArray())).Append("],\"selectedRoots\":[");
            for (int i=0;i<canvases.Count;++i)
            {
                if (i>0) text.Append(','); var state=canvases[i];
                text.Append("{\"name\":").Append(Quote(state.Name)).Append(",\"instanceId\":").Append(state.Id)
                    .Append(",\"originalMode\":").Append(Quote(state.Mode.ToString())).Append(",\"originalPlaneDistance\":").Append(Number(state.Distance))
                    .Append(",\"sortingOrder\":").Append(state.SortingOrder).Append(",\"originalEngineUiContext\":").Append(state.OriginalUiContext)
                    .Append(",\"restoredEngineUiContext\":").Append(state.Canvas==null?"null":UiContext(state.Canvas)).Append('}');
            }
            text.Append("],\"layoutInputDiagnostic\":");
            if(layoutInputs==null) text.Append("null");
            else
            {
                text.Append("{\"summary\":").Append(layoutInputs.Summary()).Append(",\"selection\":").Append(layoutInputs.Selection())
                    .Append(",\"selectedContexts\":").Append(layoutInputs.SelectedContexts()).Append(",\"error\":").Append(Quote(layoutDiagnosticError)).Append(",\"mismatchContexts\":[");
                for(int i=0;i<layoutContextIds.Count;++i) {if(i>0) text.Append(',');text.Append(layoutInputs.Context(layoutContextIds[i]));}
                text.Append("]}");
            }
            text.Append(",\"protectedInactiveCanvases\":[");
            for(int i=0;i<inactiveCanvases.Count;++i)
            {
                if(i>0) text.Append(',');var state=inactiveCanvases[i];var canvas=state.Canvas;
                text.Append("{\"name\":").Append(Quote(state.Name)).Append(",\"instanceId\":").Append(state.Id)
                    .Append(",\"originalMode\":").Append(Quote(state.Mode.ToString())).Append(",\"activityPolicy\":\"must remain inactive; never toggled\"")
                    .Append(",\"destroyed\":").Append(canvas==null?"true":"false")
                    .Append(",\"activeAfterRestore\":").Append(canvas!=null && canvas.isActiveAndEnabled?"true":"false")
                    .Append(",\"settingsRetained\":").Append(canvas==null?"null":CanvasSettingsEqual(state)?"true":"false").Append('}');
            }
            return text.Append("],\"appliedEyes\":[").Append(appliedEyes[0] ?? "null").Append(',').Append(appliedEyes[1] ?? "null")
                .Append("],\"afterRenderEyes\":[").Append(afterRenderEyes[0] ?? "null").Append(',').Append(afterRenderEyes[1] ?? "null").Append("]}").ToString();
        }
    }
}
