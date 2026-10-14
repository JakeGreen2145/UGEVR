using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ugevr
{
    // Diagnostic only. These sets never authorize an isolated UI layer.
    public sealed class HierarchyInventoryComparison
    {
        public const int MaximumInspections=1000000;
        const int SceneLimit=256,SampleLimit=64;
        readonly HashSet<int> resourceIds=new HashSet<int>(),hierarchyIds=new HashSet<int>();
        readonly List<int> missing=new List<int>(),extra=new List<int>();
        public int Limit,ResourceDiscovered,ResourceInspected,ResourceDuplicates,ResourceNulls;
        public int ListedScenes,TraversedScenes,RootsInspected,TransformEntriesInspected,HierarchyDuplicates,HierarchyNulls;
        public long RootsDiscovered,TransformEntriesDiscovered;
        public int AnchorSceneIdentityHash,MissingCount,ExtraCount;
        public bool ResourceComplete,HierarchyComplete,AnchorValidLoaded,AnchorIncludedSeparately,SceneListStable;
        public long ResourceQueryTicks,ResourceInspectionTicks,HierarchyQueryTicks,HierarchyInspectionTicks,DifferenceTicks,TotalCollectionTicks;
        public string ResourceError="",HierarchyError="",SceneStateError="",ScenesBefore="",ScenesAfter="";
        public bool Exact {get {return ResourceComplete && HierarchyComplete;}}
        public bool Equal {get {return Exact && MissingCount==0 && ExtraCount==0;}}
        public static HierarchyInventoryComparison Collect(GameObject persistentAnchor,int limit=MaximumInspections)
        {
            if(limit<1 || limit>MaximumInspections) throw new ArgumentOutOfRangeException("limit");
            long collectionStart=Stopwatch.GetTimestamp();
            var result=new HierarchyInventoryComparison {Limit=limit};
            try {result.ScenesBefore=SceneState(persistentAnchor);} catch {result.SceneStateError="before_scene_state_unavailable";}
            result.ReadResources();result.ReadHierarchy(persistentAnchor);
            try {result.ScenesAfter=SceneState(persistentAnchor);} catch {result.SceneStateError="after_scene_state_unavailable";}
            result.SceneListStable=result.SceneStateError.Length==0 && result.ScenesBefore==result.ScenesAfter;
            long start=Stopwatch.GetTimestamp();
            foreach(int id in result.resourceIds) if(!result.hierarchyIds.Contains(id)) {++result.MissingCount;if(result.missing.Count<SampleLimit) result.missing.Add(id);}
            foreach(int id in result.hierarchyIds) if(!result.resourceIds.Contains(id)) {++result.ExtraCount;if(result.extra.Count<SampleLimit) result.extra.Add(id);}
            result.DifferenceTicks=Stopwatch.GetTimestamp()-start;
            result.TotalCollectionTicks=Stopwatch.GetTimestamp()-collectionStart;
            return result;
        }
        static string SceneState(GameObject anchor)
        {
            int count=SceneManager.sceneCount;if(count<0 || count>SceneLimit) throw new InvalidOperationException();
            var text=new StringBuilder();
            for(int i=0;i<count;++i) AppendScene(text,SceneManager.GetSceneAt(i));
            text.Append("anchor:");if(anchor!=null) AppendScene(text,anchor.scene);else text.Append("unavailable");
            return text.ToString();
        }
        static void AppendScene(StringBuilder text,Scene scene)
        {
            text.Append(scene.GetHashCode()).Append('/');bool valid=scene.IsValid();text.Append(valid?'1':'0');
            if(valid) {bool loaded=scene.isLoaded;text.Append(loaded?'1':'0').Append('/').Append(loaded?scene.rootCount:0);}
            text.Append(';');
        }
        void ReadResources()
        {
            long start=Stopwatch.GetTimestamp();
            UnityEngine.Object[] objects;
            try {objects=Resources.FindObjectsOfTypeAll(typeof(GameObject));ResourceDiscovered=objects.Length;}
            catch {ResourceError="resource_query_failed";return;}
            finally {ResourceQueryTicks=Stopwatch.GetTimestamp()-start;}
            start=Stopwatch.GetTimestamp();
            try
            {
                foreach(GameObject item in objects)
                {
                    if(ResourceInspected>=Limit) {ResourceError="resource_inspection_cap";return;}++ResourceInspected;
                    if(item==null) {++ResourceNulls;continue;}
                    Scene scene=item.scene;if(!scene.IsValid() || !scene.isLoaded) continue;
                    if(!resourceIds.Add(item.GetInstanceID())) ++ResourceDuplicates;
                }
                ResourceComplete=true;
            }
            catch {ResourceError="resource_inspection_failed";}
            finally {ResourceInspectionTicks=Stopwatch.GetTimestamp()-start;}
        }
        void ReadHierarchy(GameObject anchor)
        {
            try
            {
                var scenes=new List<Scene>();var seenScenes=new HashSet<Scene>();
                long start=Stopwatch.GetTimestamp();
                try
                {
                    ListedScenes=SceneManager.sceneCount;if(ListedScenes<0 || ListedScenes>SceneLimit) {HierarchyError="scene_cap";return;}
                    for(int i=0;i<ListedScenes;++i)
                    {
                        Scene scene=SceneManager.GetSceneAt(i);
                        if(scene.IsValid() && scene.isLoaded && seenScenes.Add(scene)) scenes.Add(scene);
                    }
                    if(anchor==null) {HierarchyError="persistent_anchor_unavailable";return;}
                    Scene persistent=anchor.scene;AnchorSceneIdentityHash=persistent.GetHashCode();AnchorValidLoaded=persistent.IsValid() && persistent.isLoaded;
                    if(!AnchorValidLoaded) {HierarchyError="persistent_anchor_scene_unavailable";return;}
                    if(seenScenes.Add(persistent)) {scenes.Add(persistent);AnchorIncludedSeparately=true;}
                }
                finally {HierarchyQueryTicks+=Stopwatch.GetTimestamp()-start;}
                foreach(Scene scene in scenes)
                {
                    GameObject[] roots;start=Stopwatch.GetTimestamp();
                    try {roots=scene.GetRootGameObjects();} finally {HierarchyQueryTicks+=Stopwatch.GetTimestamp()-start;}
                    ++TraversedScenes;
                    RootsDiscovered+=roots.Length;if(RootsDiscovered>Limit) {HierarchyError="root_inspection_cap";return;}
                    foreach(GameObject root in roots)
                    {
                        ++RootsInspected;if(root==null) {++HierarchyNulls;continue;}
                        Transform[] transforms;start=Stopwatch.GetTimestamp();
                        try {transforms=root.GetComponentsInChildren<Transform>(true);} finally {HierarchyQueryTicks+=Stopwatch.GetTimestamp()-start;}
                        TransformEntriesDiscovered+=transforms.Length;if(TransformEntriesDiscovered>Limit) {HierarchyError="transform_inspection_cap";return;}
                        start=Stopwatch.GetTimestamp();
                        try
                        {
                            foreach(Transform transform in transforms)
                            {
                                ++TransformEntriesInspected;
                                if(transform==null) {++HierarchyNulls;continue;}
                                GameObject item=transform.gameObject;if(item==null) {++HierarchyNulls;continue;}
                                Scene actual=item.scene;if(!actual.IsValid() || !actual.isLoaded) continue;
                                if(!hierarchyIds.Add(item.GetInstanceID())) ++HierarchyDuplicates;
                            }
                        }
                        finally {HierarchyInspectionTicks+=Stopwatch.GetTimestamp()-start;}
                    }
                }
                HierarchyComplete=true;
            }
            catch {HierarchyError="hierarchy_query_or_inspection_failed";}
        }
        public string Yaml()
        {
            var text=new StringBuilder();
            Add(text,"comparison_exact",Exact);Add(text,"set_equal",Exact?(object)Equal:"unavailable");Add(text,"sequential_only",true);Add(text,"production_guard_replaced",false);Add(text,"gate_eligible",false);
            Add(text,"inspection_limit",Limit);Add(text,"listed_scene_limit",SceneLimit);Add(text,"native_allocation_bounded",false);Add(text,"clock_frequency",Stopwatch.Frequency);Add(text,"total_collection_ticks",TotalCollectionTicks);
            Add(text,"resource_complete",ResourceComplete);Add(text,"resource_error",ResourceError);Add(text,"resource_discovered",ResourceDiscovered);Add(text,"resource_inspected",ResourceInspected);Add(text,"resource_loaded_unique",resourceIds.Count);Add(text,"resource_duplicates",ResourceDuplicates);Add(text,"resource_null_or_destroyed",ResourceNulls);
            Add(text,"resource_query_ticks",ResourceQueryTicks);Add(text,"resource_inspection_ticks",ResourceInspectionTicks);
            Add(text,"hierarchy_complete",HierarchyComplete);Add(text,"hierarchy_error",HierarchyError);Add(text,"listed_scene_count",ListedScenes);Add(text,"traversed_scene_count",TraversedScenes);Add(text,"anchor_scene_identity_hash",AnchorSceneIdentityHash);Add(text,"anchor_valid_loaded",AnchorValidLoaded);Add(text,"anchor_included_separately",AnchorIncludedSeparately);
            Add(text,"roots_discovered",RootsDiscovered);Add(text,"roots_inspected",RootsInspected);Add(text,"transform_entries_discovered",TransformEntriesDiscovered);Add(text,"transform_entries_inspected",TransformEntriesInspected);Add(text,"hierarchy_loaded_unique",hierarchyIds.Count);Add(text,"hierarchy_duplicates",HierarchyDuplicates);Add(text,"hierarchy_null_or_destroyed",HierarchyNulls);
            Add(text,"hierarchy_query_ticks",HierarchyQueryTicks);Add(text,"hierarchy_inspection_ticks",HierarchyInspectionTicks);Add(text,"difference_ticks",DifferenceTicks);
            Add(text,"scene_list_stable",SceneListStable);Add(text,"scene_state_error",SceneStateError);Add(text,"scenes_before",ScenesBefore);Add(text,"scenes_after",ScenesAfter);
            Add(text,"missing_from_hierarchy_count",MissingCount);Add(text,"extra_in_hierarchy_count",ExtraCount);Add(text,"difference_counts_complete",Exact);Add(text,"difference_sample_limit",SampleLimit);
            Sample(text,"missing_ids",missing);Sample(text,"extra_ids",extra);Add(text,"missing_ids_not_serialized",MissingCount-missing.Count);Add(text,"extra_ids_not_serialized",ExtraCount-extra.Count);
            return text.ToString();
        }
        static void Sample(StringBuilder text,string key,List<int> values)
        {text.Append(key).Append(": [");for(int i=0;i<values.Count;++i) {if(i!=0) text.Append(", ");text.Append(values[i].ToString(CultureInfo.InvariantCulture));}text.Append("]\n");}
        internal static void Add(StringBuilder text,string key,object value)
        {
            text.Append(key).Append(": ");
            if(value is bool) text.Append((bool)value?"true":"false");
            else if(value is string) text.Append('\'').Append(((string)value).Replace("'","''").Replace("\r"," ").Replace("\n"," ")).Append('\'');
            else text.Append(Convert.ToString(value,CultureInfo.InvariantCulture));text.Append('\n');
        }
    }
}
