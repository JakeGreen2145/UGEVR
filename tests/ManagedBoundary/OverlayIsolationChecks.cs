using System;
using System.Collections.Generic;
using UnityEngine;
using Ugevr;

static class OverlayIsolationChecks
{
    public static void Run(Action<bool,string> assert)
    {
        Canvas.Instances.Clear();Resources.Extras.Clear();LayerMask.Name=layer=>"Named layer "+layer;
        var selected=new Transform();selected.gameObject.layer=5;Resources.Extras.Add(selected);
        try
        {
            bool automaticRejected=false;OverlayLayerInventory inventory=null;
            try {new OverlayLayerLease(new[]{selected.gameObject});}
            catch(OverlayLayerSelectionException error) {automaticRejected=true;inventory=error.Inventory;}
            assert(automaticRejected && selected.gameObject.layer==5,"Automatic layer policy still refuses named-only project layers before mutation");
            assert(inventory.Complete && inventory.OccupiedCount(5)==1 && inventory.OccupiedCount(20)==0,"Complete inventory separates named-but-unused layers from occupied layers");
            string yaml=inventory.DiagnosticYaml("run",17,-1,"no eligible layer");
            int layerRows=0;foreach(string line in yaml.Split('\n')) if(line.StartsWith("  - layer: ")) ++layerRows;
            assert(layerRows==32 && yaml.Contains("scan_complete: true") && yaml.Contains("includes_inactive: true") && yaml.Contains("occupied_count: 0"),"Selection failure diagnostic has a fixed 32-layer inventory without object names");
            using(var named=new OverlayLayerLease(new[]{selected.gameObject},20))
            {
                assert(named.Layer==20,"Explicit unused named layer is accepted without renaming it");
                named.Apply();assert(selected.gameObject.layer==20,"Explicit named layer borrows only the selected object");
                named.ValidateExclusive();
            }
            assert(selected.gameObject.layer==5 && LayerMask.LayerToName(20)=="Named layer 20","Explicit named layer and project name restore unchanged");
            foreach(int boundary in new[]{8,31})
            {
                using(var allowed=new OverlayLayerLease(new[]{selected.gameObject},boundary)) {allowed.Apply();assert(allowed.Layer==boundary && allowed.Mask==(1<<boundary),"Explicit isolation range accepts boundary "+boundary);}
            }
            var inactive=new Transform();inactive.gameObject.layer=20;inactive.gameObject.activeSelf=false;Resources.Extras.Add(inactive);
            bool inactiveRejected=false;
            try {new OverlayLayerLease(new[]{selected.gameObject},20);}
            catch(OverlayLayerSelectionException error) {inactiveRejected=error.Inventory.OccupiedCount(20)==1;}
            assert(inactiveRejected && selected.gameObject.layer==5 && inactive.gameObject.layer==20,"Inactive loaded GameObject occupies the explicit layer and blocks borrowing");
            Resources.Extras.Remove(inactive);
            foreach(int invalid in new[]{-2,0,7,32,Int32.MaxValue})
            {
                bool rejected=false;try {new OverlayLayerLease(new[]{selected.gameObject},invalid);} catch(ArgumentOutOfRangeException) {rejected=true;}
                assert(rejected && selected.gameObject.layer==5,"Isolation layer range rejects "+invalid+" before mutation");
            }
            var asset=new Transform();asset.gameObject.layer=20;asset.gameObject.scene=new Scene {Valid=false,isLoaded=false};Resources.Extras.Add(asset);
            using(var ignoresAsset=new OverlayLayerLease(new[]{selected.gameObject},20)) {ignoresAsset.Apply();ignoresAsset.ValidateExclusive();}
            assert(selected.gameObject.layer==5,"Unloaded asset transforms do not occupy a scene isolation layer");
            Resources.Extras.Remove(asset);
            var raced=new OverlayLayerLease(new[]{selected.gameObject},20);
            var newcomer=new Transform();newcomer.gameObject.layer=20;newcomer.gameObject.activeSelf=false;Resources.Extras.Add(newcomer);
            bool raceRejected=false;try {raced.Apply();} catch(InvalidOperationException) {raceRejected=true;} finally {raced.Dispose();}
            assert(raceRejected && selected.gameObject.layer==5,"Inactive newcomer before apply rejects explicit layer before any borrowed mutation");
            Resources.Extras.Remove(newcomer);
            var activeLease=new OverlayLayerLease(new[]{selected.gameObject},20);activeLease.Apply();Resources.Extras.Add(newcomer);
            bool lateRejected=false;try {activeLease.ValidateExclusive();} catch(InvalidOperationException) {lateRejected=true;} finally {activeLease.Dispose();}
            assert(lateRejected && selected.gameObject.layer==5 && newcomer.gameObject.layer==20 && activeLease.Errors.Length>0,"Non-renderer inactive newcomer during a UI pass fails exclusivity while owned state restores");
            Resources.Extras.Remove(newcomer);
            var changed=new OverlayLayerLease(new[]{selected.gameObject},20);changed.Apply();selected.gameObject.layer=12;changed.Dispose();
            assert(selected.gameObject.layer==12 && changed.Errors.Length==1,"Explicit-layer cleanup preserves newer game-owned layer changes");
            selected.gameObject.layer=5;
            var restoreNewcomer=new Transform();restoreNewcomer.gameObject.layer=20;
            var restoreLease=new OverlayLayerLease(new[]{selected.gameObject},20);restoreLease.Apply();
            selected.gameObject.LayerSetter=layer=>{if(layer==5) Resources.Extras.Add(restoreNewcomer);};
            restoreLease.Dispose();selected.gameObject.LayerSetter=null;
            assert(selected.gameObject.layer==5 && restoreNewcomer.gameObject.layer==20 && restoreLease.Errors.Length>0,"A newcomer during restoration is reported and never moved or deleted");
            Resources.Extras.Remove(restoreNewcomer);
            Canvas.Instances.Clear();Resources.Extras.Clear();
            var rect=new RectTransform {localScale=new Vector3(1,1,1),sizeDelta=new Vector2(3840,2160),pivot=new Vector2(.5f,.5f)};
            rect.gameObject.layer=5;
            var canvas=new Canvas {name="selected overlay",renderMode=RenderMode.ScreenSpaceOverlay,Rects=new[]{rect},Renderers=new CanvasRenderer[0]};
            canvas.gameObject=rect.gameObject;Canvas.Instances.Add(canvas);
            var camera=new Camera {nearClipPlane=.3f,farClipPlane=100,cullingMask=-1};
            var scope=new OverlayCanvasScope(camera,1,2,false);scope.SetCommonHead(new RigidPose(new Position(0,0,0),Orientation.Identity));scope.UseAfterEffectsCamera(new Camera(),20);
            scope.ApplyEye();scope.Dispose();
            var rebuiltNewcomer=new Transform();rebuiltNewcomer.gameObject.layer=20;rebuiltNewcomer.gameObject.activeSelf=false;
            Canvas.ForceAction=delegate {Resources.Extras.Add(rebuiltNewcomer);};
            scope.RebuildRestoredGeometry();Canvas.ForceAction=null;
            assert(scope.RestorationConflicts>0 && canvas.gameObject.layer==5 && rebuiltNewcomer.gameObject.layer==20,"Final desktop rebuild revalidates exclusive layer and preserves a new game-owned object");
            Canvas.Instances.Clear();Resources.Extras.Clear();
            LayerMask.Name=layer=>new string('x',1000);
            OverlayLayerInventory boundedNames=OverlayLayerInventory.Capture(20);
            assert(boundedNames.Name(20).Length==128,"Layer diagnostic names are bounded independently of project metadata");
            string boundedReason=boundedNames.DiagnosticYaml("run",17,20,new string('r',2000));
            assert(!boundedReason.Contains(new string('r',513)),"Layer diagnostic reason is bounded to 512 characters");
            LayerMask.Name=layer=>"'\0\t\n\u0085\u2028";
            string controls=OverlayLayerInventory.Capture(20).DiagnosticYaml("run\0",17,20,"reason\0\t\r\n\u0085\u2029'");
            assert(controls.IndexOf('\0')<0 && controls.IndexOf('\t')<0 && controls.IndexOf('\r')<0 && controls.IndexOf('\u0085')<0 && controls.IndexOf('\u2028')<0 && controls.IndexOf('\u2029')<0 && controls.Contains("''"),"Layer inventory YAML normalizes control characters and escapes single quotes");
            LayerMask.Name=layer=>"\ud800\ufffe\uffff";
            string unicode=OverlayLayerInventory.Capture(20).DiagnosticYaml("run",17,20,"valid");
            assert(unicode.IndexOf('\ud800')<0 && unicode.IndexOf('\ufffe')<0 && unicode.IndexOf('\uffff')<0,"Layer inventory YAML cannot contain lone surrogates or forbidden Unicode noncharacters");
            Resources.Extras.Clear();var repeated=new Transform();repeated.gameObject.layer=4;
            for(int i=0;i<100001;++i) Resources.Extras.Add(repeated);
            bool capped=false;
            try {OverlayLayerInventory.Capture(20);}
            catch(OverlayLayerSelectionException error) {capped=!error.Inventory.Complete && error.Inventory.ScannedTransforms==100000;}
            assert(capped,"Oversized layer inventory is explicitly incomplete and cannot prove an unused candidate");
        }
        finally {Canvas.Instances.Clear();Canvas.ForceAction=null;Resources.Extras.Clear();LayerMask.Name=null;}
    }
}
