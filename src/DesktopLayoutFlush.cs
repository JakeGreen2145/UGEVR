using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Ugevr
{
    // Explicit fidelity fallback. Settle the game's pending desktop layout before
    // the injector selects a camera or owns any render/canvas state.
    public sealed class DesktopLayoutFlush
    {
        const int CanvasLimit=64,CanvasScanLimit=4096,RectLimit=8192;
        sealed class Stage
        {
            public UiLayoutDiagnostics Inputs;
            public string Error;
            public int QueriedCanvases,InspectedCanvases,RootCount,RectCount,InspectedRectEntries,DroppedRoots,DroppedRects,SkippedRectInventories;
        }
        Stage before,after;
        public bool Enabled {get;private set;}
        public bool Completed {get;private set;}

        public static DesktopLayoutFlush BeforeCameraSelection(bool enabled,bool captureInputs,string selector,Func<Camera> selectCamera,out Camera selectedCamera)
        {
            var result=new DesktopLayoutFlush {Enabled=enabled};
            if(enabled)
            {
                bool targeted=captureInputs && !String.IsNullOrEmpty(selector);
                if(targeted) result.before=Capture(selector);
                // No selected camera, base anchor or canvas/rectangle restoration
                // snapshot exists yet. Callbacks may change the current scene.
                Canvas.ForceUpdateCanvases();
                result.Completed=true;
                if(targeted) result.after=Capture(selector);
            }
            selectedCamera=selectCamera();
            return result;
        }

        static Stage Capture(string selector)
        {
            var stage=new Stage {Inputs=new UiLayoutDiagnostics(selector)};
            // Whitespace-only selection must never enable full sampling here.
            if(!stage.Inputs.Filtered) {stage.Error="An explicit runtime root selector is required for flush diagnostics.";return stage;}
            try
            {
                UnityEngine.Object[] inventory=UnityEngine.Object.FindObjectsOfType(typeof(Canvas));
                stage.QueriedCanvases=inventory.Length;
                var seen=new HashSet<int>();
                int bound=Math.Min(inventory.Length,CanvasScanLimit);
                for(int i=0;i<bound;++i)
                {
                    ++stage.InspectedCanvases;
                    var canvas=inventory[i] as Canvas;
                    if(canvas==null || !canvas.isActiveAndEnabled || !canvas.isRootCanvas || canvas.renderMode!=RenderMode.ScreenSpaceOverlay ||
                        !canvas.gameObject.scene.IsValid() || !canvas.gameObject.scene.isLoaded) continue;
                    if(stage.RootCount==CanvasLimit) {++stage.DroppedRoots;continue;}
                    ++stage.RootCount;
                    if(stage.InspectedRectEntries==RectLimit) {++stage.SkippedRectInventories;continue;}
                    RectTransform[] rectangles=canvas.GetComponentsInChildren<RectTransform>(true);
                    for(int j=0;j<rectangles.Length;++j)
                    {
                        if(stage.InspectedRectEntries==RectLimit) {stage.DroppedRects+=rectangles.Length-j;break;}
                        ++stage.InspectedRectEntries;
                        RectTransform rect=rectangles[j];if(rect==null || !seen.Add(rect.GetInstanceID())) continue;
                        ++stage.RectCount;
                        stage.Inputs.RegisterSnapshot(rect,canvas,rect.GetInstanceID(),rect.name);
                    }
                }
                stage.Inputs.FinishAdmission();
                stage.Inputs.Capture(0);
            }
            catch(Exception error) {stage.Error=error.Message;}
            return stage;
        }

        static string Quote(string value)
        {
            if(value==null) return "null";if(value.Length>256) value=value.Substring(0,256);
            var text=new StringBuilder("\"");
            foreach(char c in value) {if(c=='\"' || c=='\\') text.Append('\\').Append(c);else if(c<32) text.Append("\\u").Append(((int)c).ToString("x4"));else text.Append(c);}
            return text.Append('"').ToString();
        }
        static string StageJson(Stage stage)
        {
            if(stage==null) return "null";
            return "{\"error\":"+Quote(stage.Error)+",\"canvasLimit\":"+CanvasLimit+",\"canvasScanLimit\":"+CanvasScanLimit+",\"rectLimit\":"+RectLimit+
                ",\"queriedCanvasCount\":"+stage.QueriedCanvases+",\"inspectedCanvasCount\":"+stage.InspectedCanvases+",\"uninspectedCanvasCount\":"+(stage.QueriedCanvases-stage.InspectedCanvases)+
                ",\"overlayRootCount\":"+stage.RootCount+",\"rectCount\":"+stage.RectCount+",\"droppedRoots\":"+stage.DroppedRoots+",\"droppedRects\":"+stage.DroppedRects+
                ",\"inspectedRectEntries\":"+stage.InspectedRectEntries+
                ",\"skippedRootRectInventories\":"+stage.SkippedRectInventories+
                ",\"summary\":"+stage.Inputs.Summary()+",\"selection\":"+stage.Inputs.Selection()+",\"selectedContexts\":"+stage.Inputs.SelectedContexts()+"}";
        }
        // Only the existing requested capture/failure serialization calls this.
        // Ordinary frames discard their sampled immutable values without hashing.
        public string DiagnosticJson()
        {
            return "{\"enabled\":"+(Enabled?"true":"false")+",\"completed\":"+(Completed?"true":"false")+
                ",\"beforeDesktopFlush\":"+StageJson(before)+",\"afterDesktopFlush\":"+StageJson(after)+"}";
        }
    }
}
