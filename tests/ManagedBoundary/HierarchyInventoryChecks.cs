using System;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using Ugevr;

static class HierarchyInventoryChecks
{
    static Scene SceneWith(int handle,GameObject root,params GameObject[] children)
    {
        var scene=new Scene {handle=handle,Valid=true,isLoaded=true,Roots=new[]{root}};
        var transforms=new Transform[children.Length+1];transforms[0]=new Transform {gameObject=root};root.scene=scene;
        for(int i=0;i<children.Length;++i) {children[i].scene=scene;transforms[i+1]=new Transform {gameObject=children[i]};}
        root.HierarchyTransforms=transforms;return scene;
    }
    static void Reset() {Resources.Extras.Clear();Resources.QueryTypes.Clear();Canvas.Instances.Clear();SceneManager.Scenes.Clear();}
    public static void Run(Action<bool,string> assert)
    {
        try
        {
            Reset();var root=new GameObject();var inactive=new GameObject {activeSelf=false};var scene=SceneWith(1,root,inactive);SceneManager.Scenes.Add(scene);Resources.Extras.Add(root);Resources.Extras.Add(inactive);
            var same=HierarchyInventoryComparison.Collect(root);
            assert(same.Exact && same.Equal && same.SceneListStable && same.TraversedScenes==1 && same.ResourceInspected==2 && same.TransformEntriesInspected==2,"Fresh hierarchy comparison matches loaded root and inactive non-renderer IDs");
            assert(root.LastIncludeInactive==true && same.Yaml().Contains("production_guard_replaced: false") && same.Yaml().Contains("sequential_only: true") && same.Yaml().Contains("gate_eligible: false"),"Hierarchy diagnostic requests inactive children and cannot authorize a production guard");
            root.hideFlags=HideFlags.HideAndDontSave;inactive.hideFlags=HideFlags.DontSave;
            assert(HierarchyInventoryComparison.Collect(root).Equal,"Comparison does not impose a hidden or DontSave exclusion when roots return those objects");
            var persistent=new GameObject();var persistentChild=new GameObject {activeSelf=false};SceneWith(99,persistent,persistentChild);Resources.Extras.Add(persistent);Resources.Extras.Add(persistentChild);
            var withPersistent=HierarchyInventoryComparison.Collect(persistent);
            assert(withPersistent.Exact && withPersistent.Equal && withPersistent.AnchorIncludedSeparately && withPersistent.AnchorSceneIdentityHash==99 && withPersistent.TraversedScenes==2,"Owned anchor scene supplements the listed scenes for the persistent-scene hypothesis");
            var omitted=new GameObject {scene=scene,activeSelf=false};Resources.Extras.Add(omitted);
            var mismatch=HierarchyInventoryComparison.Collect(persistent);
            assert(mismatch.Exact && !mismatch.Equal && mismatch.MissingCount==1 && mismatch.ExtraCount==0 && mismatch.Yaml().Contains(omitted.GetInstanceID().ToString()),"A loaded resource object absent from root traversal remains an explicit missing-ID result");
            Resources.Extras.Remove(omitted);
            var unloaded=new GameObject();var unloadedScene=SceneWith(2,unloaded);unloadedScene.isLoaded=false;unloaded.scene=unloadedScene;SceneManager.Scenes.Add(unloadedScene);Resources.Extras.Add(unloaded);
            var invalid=new GameObject {scene=new Scene {handle=3,Valid=false,isLoaded=true}};Resources.Extras.Add(invalid);
            assert(HierarchyInventoryComparison.Collect(persistent).Equal,"Both comparison paths use the same valid-and-loaded scene predicate for assets and unloading states");
            var unlistedLoaded=new GameObject();SceneWith(4,unlistedLoaded);Resources.Extras.Add(unlistedLoaded);
            assert(HierarchyInventoryComparison.Collect(persistent).MissingCount==1,"A valid loaded object from an unlisted non-anchor scene is diagnosed rather than assumed absent");Resources.Extras.Remove(unlistedLoaded);
            SceneManager.Scenes.Add(scene);int queries=root.HierarchyQueries;
            assert(HierarchyInventoryComparison.Collect(persistent).Equal && root.HierarchyQueries==queries+1,"Repeated listed scene handles do not multiply root traversal");
            Reset();root=new GameObject();inactive=new GameObject {activeSelf=false};scene=SceneWith(1,root,inactive);SceneManager.Scenes.Add(scene);Resources.Extras.Add(root);Resources.Extras.Add(inactive);
            root.HierarchyTransforms=new[]{root.HierarchyTransforms[0],root.HierarchyTransforms[1],root.HierarchyTransforms[1]};
            var duplicate=HierarchyInventoryComparison.Collect(root);
            assert(duplicate.Exact && duplicate.Equal && duplicate.HierarchyDuplicates==1,"Exact comparison deduplicates IDs and separately reports repeated hierarchy entries");
            var capped=HierarchyInventoryComparison.Collect(root,2);
            assert(capped.ResourceComplete && !capped.HierarchyComplete && !capped.Exact && capped.TransformEntriesDiscovered==3 && capped.HierarchyError=="transform_inspection_cap" && capped.Yaml().Contains("difference_counts_complete: false"),"Oversized returned hierarchy arrays retain discovered counts and cannot produce an exact comparison");
            Resources.Extras.Add(inactive);capped=HierarchyInventoryComparison.Collect(root,2);
            assert(!capped.ResourceComplete && capped.ResourceDiscovered==3 && capped.ResourceInspected==2 && capped.ResourceError=="resource_inspection_cap","Resource inspection cap applies to returned entries even when IDs repeat");
            assert(!HierarchyInventoryComparison.Collect(null).HierarchyComplete,"Missing owned persistent anchor prevents a completed hierarchy comparison");
            var invalidAnchor=new GameObject {scene=new Scene {handle=77,Valid=false,isLoaded=false}};
            assert(HierarchyInventoryComparison.Collect(invalidAnchor).HierarchyError=="persistent_anchor_scene_unavailable","An invalid persistent anchor scene is not silently omitted");
            root.HierarchyQueryAction=delegate {throw new InvalidOperationException();};
            assert(!HierarchyInventoryComparison.Collect(root).HierarchyComplete,"Native hierarchy-query failure remains incomplete");root.HierarchyQueryAction=null;
            foreach(int invalidLimit in new[]{0,-1,1000001}) {bool refused=false;try {HierarchyInventoryComparison.Collect(root,invalidLimit);} catch(ArgumentOutOfRangeException) {refused=true;}assert(refused,"Hierarchy comparison rejects invalid inspection cap "+invalidLimit);}
            Reset();root=new GameObject();scene=SceneWith(1,root);scene.Roots=new[]{root,root,root};SceneManager.Scenes.Add(scene);Resources.Extras.Add(root);
            var rootCapped=HierarchyInventoryComparison.Collect(root,2);
            assert(!rootCapped.HierarchyComplete && rootCapped.RootsDiscovered==3 && rootCapped.RootsInspected==0 && rootCapped.HierarchyError=="root_inspection_cap","Root-entry cap is explicit before hierarchy traversal");
            for(int i=2;i<=257;++i) SceneManager.Scenes.Add(new Scene {handle=i,Valid=true,isLoaded=false});
            assert(HierarchyInventoryComparison.Collect(root).HierarchyError=="scene_cap","Listed scene cap prevents unbounded scene enumeration");
            Reset();root=new GameObject();scene=SceneWith(1,root);SceneManager.Scenes.Add(scene);Resources.Extras.Add(root);var newChild=new GameObject {scene=scene};
            root.HierarchyQueryAction=delegate {root.HierarchyTransforms=new[]{new Transform {gameObject=root},new Transform {gameObject=newChild}};};
            var changed=HierarchyInventoryComparison.Collect(root);
            assert(changed.Exact && !changed.Equal && changed.ExtraCount==1 && changed.Yaml().Contains("sequential_only: true"),"Mutation between collections appears as an extra ID and is never treated as an atomic snapshot");
            root.HierarchyQueryAction=delegate {SceneManager.Scenes.Add(new Scene {handle=2,Valid=true,isLoaded=false});};
            assert(!HierarchyInventoryComparison.Collect(root).SceneListStable,"Scene-list changes during comparison are reported independently of set equality");
            Reset();root=new GameObject();scene=SceneWith(1,root);SceneManager.Scenes.Add(scene);Resources.Extras.Add(root);
            for(int i=0;i<70;++i) Resources.Extras.Add(new GameObject {scene=scene});
            var bounded=HierarchyInventoryComparison.Collect(root);
            assert(bounded.Exact && bounded.MissingCount==70 && bounded.Yaml().Contains("missing_ids_not_serialized: 6"),"Full-set difference counts stay exact while serialized runtime-ID samples are bounded to 64");
            FileChecks(assert,root);
            SentinelChecks(assert);
        }
        finally {Reset();}
    }
    static void FileChecks(Action<bool,string> assert,GameObject anchor)
    {
        string session=Path.Combine(Path.GetTempPath(),"UGEVR-hierarchy-check-"+Guid.NewGuid().ToString("N"));string evidence=Path.Combine(session,"UnityVR-evidence");Directory.CreateDirectory(evidence);
        string run=Guid.NewGuid().ToString("D"),request=Path.Combine(evidence,"hierarchy-inventory-request");int calls=0,creations=0;
        Func<GameObject,HierarchyInventoryComparison> collector=delegate(GameObject owner) {++calls;return HierarchyInventoryComparison.Collect(owner);};
        Func<GameObject> create=delegate {++creations;return new GameObject();};
        var probe=new HierarchyInventoryProbe(session,run,17,collector,create);long clock=System.Diagnostics.Stopwatch.Frequency;
        assert(creations==0,"Constructing the hierarchy diagnostic does not create a sentinel");
        try
        {
            File.WriteAllText(request,"compare=1",new UTF8Encoding(false));probe.Tick(false,anchor,1,DateTime.UtcNow,clock);
            assert(calls==0 && creations==0 && probe.RequestsConsumed==0 && File.Exists(request),"Disabled hierarchy diagnostic leaves the request untouched and creates no sentinel or inventory");
            probe.Tick(true,anchor,1,DateTime.UtcNow,clock);string result1=Path.Combine(evidence,"hierarchy-inventory-result-1.yaml");string body=File.ReadAllText(result1);
            assert(calls==1 && probe.RequestsConsumed==1 && !File.Exists(request) && body.Contains(run) && body.Contains("process_id: 17") && body.Contains("game_frame: 1") && body.Contains("production_guard_replaced: false"),"One explicit durable request records session-bound comparison evidence before the pair");
            var restarted=new HierarchyInventoryProbe(session,run,17,collector,create);File.WriteAllBytes(request,new byte[]{0xff});restarted.Tick(true,anchor,2,DateTime.UtcNow,clock*2);
            assert(calls==1 && creations==1 && restarted.RequestsConsumed==2 && File.ReadAllText(Path.Combine(evidence,"hierarchy-inventory-result-2.yaml")).Contains("invalid_request"),"Malformed UTF-8 consumes a durable slot without creating a sentinel or inventory");
            File.WriteAllText(request,"compare=1",new UTF8Encoding(false));var capped=new HierarchyInventoryProbe(session,run,17,collector,create);capped.Tick(true,anchor,3,DateTime.UtcNow,clock*3);
            assert(calls==1 && creations==1 && capped.RequestsConsumed==2 && File.Exists(request) && File.ReadAllText(result1)==body,"Component restart preserves the two-trial cap without sentinel creation or evidence clobbering");
            string blocked=Path.Combine(evidence,"hierarchy-inventory-result-2.yaml");File.Delete(blocked);File.Delete(Path.Combine(evidence,"hierarchy-inventory-request.claimed-2"));File.Delete(request);
            var writer=new HierarchyInventoryProbe(session,run,17,delegate(GameObject owner) {File.WriteAllText(blocked,"external result");return HierarchyInventoryComparison.Collect(owner);});
            File.WriteAllText(request,"compare=1",new UTF8Encoding(false));writer.Tick(true,anchor,4,DateTime.UtcNow,clock*4);
            assert(File.ReadAllText(blocked)=="external result" && writer.LastDiagnosticError!=null && writer.RequestsConsumed==2,"Diagnostic writer preserves an externally created result and retains the claimed slot on failure");
        }
        finally
        {
            // Only exact files created by this controlled fixture are removed.
            foreach(string file in Directory.GetFiles(evidence)) File.Delete(file);
            Directory.Delete(evidence);Directory.Delete(session);
        }
    }
    static void SentinelChecks(Action<bool,string> assert)
    {
        for(int scenario=0;scenario<8;++scenario)
        {
            Reset();var gameRoot=new GameObject();SceneManager.Scenes.Add(SceneWith(1,gameRoot));Resources.Extras.Add(gameRoot);
            var original=new GameObject {hideFlags=HideFlags.HideAndDontSave,scene=new Scene {handle=0,Valid=false,isLoaded=false}};Resources.Extras.Add(original);
            string session=Path.Combine(Path.GetTempPath(),"UGEVR-sentinel-check-"+Guid.NewGuid().ToString("N"));string evidence=Path.Combine(session,"UnityVR-evidence");Directory.CreateDirectory(evidence);
            GameObject sentinel=null;int created=0,persistent=0,collected=0,destroyed=0;HierarchyInventoryProbe probe=null;
            try
            {
                Func<GameObject> create=delegate
                {
                    ++created;if(scenario==1) throw new InvalidOperationException("creation failed");
                    sentinel=new GameObject {hideFlags=HideFlags.HideAndDontSave,scene=new Scene {handle=0,Valid=false,isLoaded=false}};Resources.Extras.Add(sentinel);return sentinel;
                };
                Action<GameObject> persist=delegate(GameObject value)
                {++persistent;if(scenario==2) throw new InvalidOperationException("persistence failed");if(scenario!=3) SceneWith(-12,value);};
                Func<GameObject,HierarchyInventoryComparison> collect=delegate(GameObject value)
                {
                    ++collected;if(scenario==4) throw new InvalidOperationException("query failed");
                    if(scenario==6) value.transform.childCount=1;
                    if(scenario==7) value.AdditionalComponents=new Component[]{new Renderer {gameObject=value}};
                    return HierarchyInventoryComparison.Collect(value);
                };
                Action<GameObject> destroy=delegate(GameObject value)
                {assert(System.Object.ReferenceEquals(value,sentinel) && !System.Object.ReferenceEquals(value,original),"Cleanup targets only the exact created diagnostic object");++destroyed;if(scenario==5 && destroyed==1) throw new InvalidOperationException("destroy failed");if(scenario!=0) value.Destroyed=true;};
                probe=new HierarchyInventoryProbe(session,Guid.NewGuid().ToString("D"),17,collect,create,persist,destroy);
                File.WriteAllText(Path.Combine(evidence,"hierarchy-inventory-request"),"compare=1",new UTF8Encoding(false));
                probe.Tick(true,original,7,DateTime.UtcNow,System.Diagnostics.Stopwatch.Frequency);
                string body=File.ReadAllText(Path.Combine(evidence,"hierarchy-inventory-result-1.yaml"));
                assert(created==1 && probe.RequestsConsumed==1 && !original.Destroyed && !gameRoot.Destroyed,"Valid diagnostic request preserves original game objects in scenario "+scenario);
                if(scenario==0)
                {
                    assert(persistent==1 && collected==1 && destroyed==1 && sentinel.hideFlags==HideFlags.None && body.Contains("original_anchor_valid_loaded: false") && body.Contains("sentinel_present_in_resource_set: true") && body.Contains("sentinel_present_in_hierarchy_set: true") && body.Contains("set_equal: true"),"Owned ordinary persistent sentinel permits comparison despite an invalid hidden plugin anchor");
                    assert(body.Contains("original_population_untouched: false") && body.Contains("sentinel_destroy_requested: true") && body.Contains("sentinel_destroy_observed_at_write: false"),"Evidence records the population change and distinguishes deferred destruction from observation");
                }
                if(scenario==1) assert(persistent==0 && collected==0 && destroyed==0 && body.Contains("sentinel_creation_failed") && body.Contains("sentinel_created: false"),"Creation failure consumes a request without inventing an owned anchor or running collection");
                if(scenario==2) assert(collected==0 && destroyed==1 && body.Contains("sentinel_persistence_failed") && body.Contains("sentinel_persistence_api_returned: false"),"Failed DontDestroyOnLoad setup cleans up the created object without running collection");
                if(scenario==3) assert(collected==1 && destroyed==1 && body.Contains("persistent_anchor_scene_unavailable") && body.Contains("comparison_exact: false") && body.Contains("anchor_valid_loaded: false"),"A returned persistence call with an invalid scene remains a failed hierarchy comparison");
                if(scenario==4) assert(collected==1 && destroyed==1 && body.Contains("collector_failed") && body.Contains("sentinel_destroy_requested: true"),"Collector exception still requests owned sentinel cleanup");
                if(scenario==5) assert(destroyed==1 && !sentinel.Destroyed && body.Contains("sentinel_destroy_requested: false") && body.Contains("sentinel_destroy_request_failed"),"Failed cleanup is retained in diagnostic evidence without claiming destruction");
                if(scenario==6 || scenario==7) assert(destroyed==0 && !sentinel.Destroyed && body.Contains(scenario==6?"sentinel_has_unowned_children":"sentinel_has_unowned_components"),"Cleanup preserves unexpected game-owned sentinel additions");
                probe.Dispose();probe.Dispose();
                if(scenario==5) assert(destroyed==2 && sentinel.Destroyed,"Teardown retries a failed request against the same owned object");
                if(scenario==0) assert(destroyed==1 && !sentinel.Destroyed,"Teardown does not issue duplicate requests or claim deferred destruction completed");
                if(scenario==6 || scenario==7) assert(destroyed==0 && probe.LastDiagnosticError!=null && !original.Destroyed && !gameRoot.Destroyed,"Teardown reports preserved additions without deleting game objects");
                File.WriteAllText(Path.Combine(evidence,"hierarchy-inventory-request"),"compare=1",new UTF8Encoding(false));probe.Tick(true,original,8,DateTime.UtcNow,2*System.Diagnostics.Stopwatch.Frequency);
                assert(created==1,"Disposed diagnostic cannot create another sentinel");
            }
            finally
            {
                if(probe!=null) probe.Dispose();
                foreach(string file in Directory.GetFiles(evidence)) File.Delete(file);Directory.Delete(evidence);Directory.Delete(session);
            }
        }
    }
}
