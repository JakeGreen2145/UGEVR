using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
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
            assert(selected.gameObject.layer==5,"Unloaded asset GameObjects do not occupy a scene isolation layer");
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
            var scope=new OverlayCanvasScope(camera,1,2,false);scope.EnableWallTiming();scope.SetCommonHead(new RigidPose(new Position(0,0,0),Orientation.Identity));scope.UseAfterEffectsCamera(new Camera(),20);
            scope.ApplyEye();scope.RenderGameEye(0);scope.Dispose();
            var rebuiltNewcomer=new Transform();rebuiltNewcomer.gameObject.layer=20;rebuiltNewcomer.gameObject.activeSelf=false;
            Canvas.ForceAction=delegate {Resources.Extras.Add(rebuiltNewcomer);};
            scope.RebuildRestoredGeometry();Canvas.ForceAction=null;
            assert(scope.RestorationConflicts>0 && canvas.gameObject.layer==5 && rebuiltNewcomer.gameObject.layer==20,"Final desktop rebuild revalidates exclusive layer and preserves a new game-owned object");
            assert(scope.DiagnosticJson().Contains("\"wallTiming\":{") && scope.DiagnosticJson().Contains("after desktop restoration") && scope.DiagnosticJson().Contains("\"complete\":false"),"Restoration failure evidence retains bounded timing for the fresh final exclusivity query");
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
            bool capped=false;var capTiming=new OverlayWallTiming();
            try {OverlayLayerInventory.Capture(20,100000,capTiming);}
            catch(OverlayLayerSelectionException error)
            {
                capped=!error.Inventory.Complete && error.Inventory.ScannedGameObjects==100000;
                string incomplete=error.Inventory.DiagnosticYaml("run",17,20,error.Message);
                assert(error.Inventory.DiscoveredGameObjectCount==100001 && error.Inventory.ScanLimit==100000 && incomplete.Contains("discovered_gameobject_count: 100001") && incomplete.Contains("inspection_object_kind: 'GameObject'") && incomplete.Contains("schema_version: 2") && !incomplete.Contains("transform") && incomplete.Contains("scan_limit: 100000") && incomplete.Contains("scan_complete: false"),"Incomplete GameObject inventory records its object kind, complete query length and configured inspection limit");
                assert(incomplete.Contains("query_wall_ticks:") && incomplete.Contains("inspection_wall_ticks:") && capTiming.DiagnosticJson().Contains("\"complete\":false"),"Cap failure retains opt-in query and partial-inspection wall timing");
            }
            assert(capped,"Oversized layer inventory is explicitly incomplete and cannot prove an unused candidate");
            bool defaultLeaseRejected=false;
            try {new OverlayLayerLease(new[]{selected.gameObject},20);} catch(OverlayLayerSelectionException error) {defaultLeaseRejected=!error.Inventory.Complete && error.Inventory.ScanLimit==100000;}
            assert(defaultLeaseRejected && selected.gameObject.layer==5,"Default lease rejects an incomplete inventory before selecting or mutating the candidate layer");
            OverlayLayerInventory expanded=OverlayLayerInventory.Capture(20,100001);
            assert(expanded.Complete && expanded.ScannedGameObjects==100001 && expanded.DiscoveredGameObjectCount==100001 && expanded.ScanLimit==100001 && expanded.OccupiedCount(4)==100001,"Explicit larger bound inspects the entire returned GameObject inventory");
            using(var expandedLease=new OverlayLayerLease(new[]{selected.gameObject},20,100001))
            {
                expandedLease.Apply();expandedLease.ValidateExclusive();
                assert(selected.gameObject.layer==20,"Larger bound applies to both inventory and GameObject exclusivity checks");
            }
            assert(selected.gameObject.layer==5,"Expanded-bound lease restores the selected layer");
            Resources.Extras.RemoveAt(Resources.Extras.Count-1);
            var lateBeyondDefault=new Transform();lateBeyondDefault.gameObject.layer=20;lateBeyondDefault.gameObject.activeSelf=false;
            using(var expandedLease=new OverlayLayerLease(new[]{selected.gameObject},20,100001))
            {
                expandedLease.Apply();Resources.Extras.Add(lateBeyondDefault);
                bool collision=false;try {expandedLease.ValidateExclusive();} catch(InvalidOperationException error) {collision=error.Message.Contains("unowned");}
                assert(collision && lateBeyondDefault.gameObject.layer==20,"Larger bound still rejects an inactive unowned object beyond the default inspection limit");
            }
            assert(selected.gameObject.layer==5 && lateBeyondDefault.gameObject.layer==20,"Late collision restores only owned layers with the expanded bound");
            Resources.Extras.Clear();
            foreach(Component component in new Component[]{new Renderer(),new CanvasRenderer()})
            {
                component.gameObject.layer=4;
                using(var typedLease=new OverlayLayerLease(new[]{selected.gameObject},20,100001))
                {
                    typedLease.Apply();
                    for(int i=0;i<100001;++i) Resources.Extras.Add(component);
                    typedLease.ValidateExclusive();
                    assert(selected.gameObject.layer==20,"Explicit scan limit permits a complete "+component.GetType().Name+" exclusivity query");
                    Resources.Extras.Add(component);
                    bool queryCapped=false;try {typedLease.ValidateExclusive();} catch(InvalidOperationException error) {queryCapped=error.Message.Contains("inspection limit");}
                    assert(queryCapped,"The same configured bound rejects an oversized "+component.GetType().Name+" exclusivity query");
                    Resources.Extras.Clear();
                }
                assert(selected.gameObject.layer==5,"Oversized typed query leaves owned layer cleanup intact");
            }
            OverlayLayerInventory maximum=OverlayLayerInventory.Capture(20,1000000);
            assert(maximum.Complete && maximum.ScanLimit==1000000,"Maximum explicit scene scan limit is accepted");
            foreach(int invalidLimit in new[]{Int32.MinValue,0,99999,1000001,Int32.MaxValue})
            {
                bool inventoryRejected=false,leaseRejected=false;
                try {OverlayLayerInventory.Capture(20,invalidLimit);} catch(ArgumentOutOfRangeException) {inventoryRejected=true;}
                try {new OverlayLayerLease(new[]{selected.gameObject},20,invalidLimit);} catch(ArgumentOutOfRangeException) {leaseRejected=true;}
                assert(inventoryRejected && leaseRejected && selected.gameObject.layer==5,"Inventory and lease reject invalid scene scan bound "+invalidLimit+" before mutation");
            }
            foreach(Component component in new Component[]{new Transform(),new Renderer(),new CanvasRenderer()})
            {
                foreach(bool active in new[]{false,true})
                {
                    using(var lease=new OverlayLayerLease(new[]{selected.gameObject},20))
                    {
                        lease.Apply();component.gameObject.layer=20;component.gameObject.activeSelf=active;Resources.Extras.Add(component);
                        bool collision=false;try {lease.ValidateExclusive();} catch(InvalidOperationException error) {collision=error.Message.Contains("unowned");}
                        assert(collision,"Single GameObject inventory rejects a late "+(active?"active ":"inactive ")+component.GetType().Name+" owner");
                        Resources.Extras.Clear();
                    }
                    assert(component.gameObject.layer==20 && selected.gameObject.layer==5,"Collision restores owned state without changing the new object's layer");
                }
            }
            var direct=new GameObject();direct.layer=20;direct.activeSelf=false;
            using(var lease=new OverlayLayerLease(new[]{selected.gameObject},20))
            {
                lease.Apply();Resources.Extras.Add(direct);
                bool collision=false;try {lease.ValidateExclusive();} catch(InvalidOperationException) {collision=true;}
                assert(collision,"An inactive direct GameObject with no fixture component is still an exclusivity collision");
                Resources.Extras.Clear();
            }
            var offLayer=new GameObject();offLayer.layer=4;Resources.Extras.Add(offLayer);
            using(var lease=new OverlayLayerLease(new[]{selected.gameObject},20))
            {
                lease.Apply();offLayer.SceneQueries=0;offLayer.InstanceIdQueries=0;Resources.QueryTypes.Clear();
                lease.ValidateExclusive();
                assert(Resources.QueryTypes.Count==1 && Resources.QueryTypes[0]==typeof(GameObject),"Each exclusivity boundary performs exactly one fresh GameObject inventory query");
                assert(offLayer.SceneQueries==0 && offLayer.InstanceIdQueries==0,"Objects on other layers avoid scene and instance-ID getter calls during exclusivity inspection");
            }
            Resources.Extras.Clear();
            var destroyedOwned=new GameObject();destroyedOwned.layer=5;
            var destroyedLease=new OverlayLayerLease(new[]{destroyedOwned},20);destroyedLease.Apply();
            int oldId=destroyedOwned.GetInstanceID();destroyedOwned.Destroyed=true;
            var reused=new GameObject {InstanceIdOverride=oldId};reused.layer=20;Resources.Extras.Add(reused);
            bool destroyedCollision=false;try {destroyedLease.ValidateExclusive();} catch(InvalidOperationException) {destroyedCollision=true;}
            destroyedLease.Dispose();
            assert(destroyedCollision && destroyedLease.DestroyedObjects==1 && reused.layer==20,"Destroyed owned object does not authorize an unrelated new object reusing its instance ID");
            Resources.Extras.Clear();
            var timing=new OverlayWallTiming();
            OverlayLayerInventory measured=OverlayLayerInventory.Capture(20,100000,timing);
            using(var lease=new OverlayLayerLease(new[]{selected.gameObject},20,100000,timing)) {lease.Apply();lease.ValidateExclusive("measured boundary");}
            var timedCamera=new Camera();timing.Render(timedCamera,0,false);timedCamera.targetTexture=new RenderTexture();timing.Render(timedCamera,0,true);
            string timingJson=timing.DiagnosticJson();
            assert(measured.WallTimingEnabled && measured.DiagnosticYaml("run",17,20,"measured").Contains("wall_clock_frequency:") && timingJson.Contains("measured boundary") && timingJson.Contains("\"gameReturned\":true") && timingJson.Contains("\"uiReturned\":true") && timedCamera.targetTexture==null,"Opt-in timing separates inventory inspections and returning game/UI renders without changing target cleanup");
            assert(!maximum.WallTimingEnabled && !maximum.DiagnosticYaml("run",17,20,"unmeasured").Contains("wall_clock_frequency:"),"Wall timing remains absent when diagnostics are disabled");
            timedCamera.RenderAction=delegate {throw new InvalidOperationException("controlled render failure");};timedCamera.targetTexture=new RenderTexture();
            bool renderFailure=false;try {timing.Render(timedCamera,1,true);} catch(InvalidOperationException) {renderFailure=true;}
            assert(renderFailure && timedCamera.targetTexture==null && timing.DiagnosticJson().Contains("\"uiAttempted\":true,\"uiReturned\":false"),"Timing records a failed UI render while retaining exception propagation and target cleanup");
            for(int i=0;i<20;++i) timing.RecordScan("bounded",1,1,true,0,0);
            assert(timing.DiagnosticJson().Contains("\"scanCapacity\":16") && !timing.DiagnosticJson().Contains("\"droppedScans\":0"),"Per-pair scan timing evidence has a fixed record capacity");
        }
        finally {Canvas.Instances.Clear();Canvas.ForceAction=null;Resources.Extras.Clear();Resources.QueryTypes.Clear();LayerMask.Name=null;}
    }
}
