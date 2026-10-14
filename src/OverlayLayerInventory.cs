using System;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace Ugevr
{
    public sealed class OverlayLayerSelectionException : InvalidOperationException
    {
        public readonly OverlayLayerInventory Inventory;
        public readonly int RequestedLayer;
        public OverlayLayerSelectionException(string message,OverlayLayerInventory inventory,int requestedLayer) : base(message)
        { Inventory=inventory;RequestedLayer=requestedLayer; }
    }
    public sealed class OverlayLayerInventory
    {
        public const int DefaultScanLimit=100000;
        public const int MaximumScanLimit=1000000;
        readonly string[] names=new string[32];
        readonly int[] counts=new int[32];
        public int ScanLimit {get;private set;}
        public int DiscoveredGameObjectCount {get;private set;}
        public int ScannedGameObjects {get;private set;}
        public bool WallTimingEnabled {get;private set;}
        public long QueryWallTicks {get;private set;}
        public long InspectionWallTicks {get;private set;}
        public int LoadedSceneObjects {get;private set;}
        public bool Complete {get;private set;}
        public uint UsedMask {get;private set;}
        public string Name(int layer) {return names[layer];}
        public int OccupiedCount(int layer) {return counts[layer];}
        public static void ValidateScanLimit(int scanLimit)
        {
            if(scanLimit<DefaultScanLimit || scanLimit>MaximumScanLimit)
                throw new ArgumentOutOfRangeException("scanLimit","UI scene scan limit must be 100000 through 1000000.");
        }
        public static OverlayLayerInventory Capture(int requestedLayer,int scanLimit=DefaultScanLimit,OverlayWallTiming timing=null)
        {
            ValidateScanLimit(scanLimit);
            var result=new OverlayLayerInventory {ScanLimit=scanLimit,WallTimingEnabled=timing!=null};
            for(int layer=0;layer<32;++layer)
            {
                string name=LayerMask.LayerToName(layer) ?? "";
                result.names[layer]=name.Length>128?name.Substring(0,128):name;
            }
            // Unity allocates the complete result before this inspection bound applies.
            long start=timing==null?0:OverlayWallTiming.Timestamp();
            UnityEngine.Object[] discovered=Resources.FindObjectsOfTypeAll(typeof(GameObject));
            if(timing!=null) result.QueryWallTicks=OverlayWallTiming.Timestamp()-start;
            result.DiscoveredGameObjectCount=discovered.Length;
            start=timing==null?0:OverlayWallTiming.Timestamp();
            try
            {
                foreach(GameObject gameObject in discovered)
                {
                    if(result.ScannedGameObjects>=scanLimit)
                        throw new OverlayLayerSelectionException("UI layer selection exceeds the configured "+scanLimit.ToString(CultureInfo.InvariantCulture)+" GameObject inspection limit; inventory is incomplete.",result,requestedLayer);
                    ++result.ScannedGameObjects;
                    if(gameObject==null) continue;
                    var scene=gameObject.scene;
                    if(!scene.IsValid() || !scene.isLoaded) continue;
                    int layer=gameObject.layer;
                    if(layer<0 || layer>31) throw new OverlayLayerSelectionException("Loaded scene object has an invalid layer.",result,requestedLayer);
                    ++result.counts[layer]; ++result.LoadedSceneObjects; result.UsedMask|=1u<<layer;
                }
                result.Complete=true;return result;
            }
            finally
            {
                if(timing!=null)
                {
                    result.InspectionWallTicks=OverlayWallTiming.Timestamp()-start;
                    timing.RecordScan("inventory",discovered.Length,result.ScannedGameObjects,result.Complete,result.QueryWallTicks,result.InspectionWallTicks);
                }
            }
        }
        static string Scalar(string text,int limit)
        {
            text=text ?? "";
            var safe=new StringBuilder();
            for(int i=0;i<text.Length && i<limit;++i)
            {
                char character=text[i];
                if(Char.IsControl(character) || character=='\u2028' || character=='\u2029' || character=='\ufffe' || character=='\uffff') safe.Append(' ');
                else if(Char.IsHighSurrogate(character))
                {
                    if(i+1<text.Length && i+1<limit && Char.IsLowSurrogate(text[i+1])) safe.Append(character).Append(text[++i]);
                    else safe.Append('\ufffd');
                }
                else if(Char.IsLowSurrogate(character)) safe.Append('\ufffd');
                else if(character=='\'') safe.Append("''");
                else safe.Append(character);
            }
            return "'"+safe+"'";
        }
        public string DiagnosticYaml(string runId,int processId,int requestedLayer,string reason)
        {
            var body=new StringBuilder();
            body.Append("schema_version: 2\nrun_id: ").Append(Scalar(runId,64)).Append("\nprocess_id: ").Append(processId.ToString(CultureInfo.InvariantCulture));
            body.Append("\nrequested_layer: ").Append(requestedLayer.ToString(CultureInfo.InvariantCulture)).Append("\nreason: ").Append(Scalar(reason,512));
            body.Append("\ninspection_object_kind: 'GameObject'\nscan_complete: ").Append(Complete?"true":"false").Append("\nscanned_gameobjects: ").Append(ScannedGameObjects.ToString(CultureInfo.InvariantCulture));
            body.Append("\nscan_limit: ").Append(ScanLimit.ToString(CultureInfo.InvariantCulture)).Append("\ndiscovered_gameobject_count: ").Append(DiscoveredGameObjectCount.ToString(CultureInfo.InvariantCulture));
            if(WallTimingEnabled) body.Append("\nwall_clock_frequency: ").Append(System.Diagnostics.Stopwatch.Frequency.ToString(CultureInfo.InvariantCulture))
                .Append("\nquery_wall_ticks: ").Append(QueryWallTicks.ToString(CultureInfo.InvariantCulture)).Append("\ninspection_wall_ticks: ").Append(InspectionWallTicks.ToString(CultureInfo.InvariantCulture));
            body.Append("\nloaded_scene_objects: ").Append(LoadedSceneObjects.ToString(CultureInfo.InvariantCulture)).Append("\nused_mask: ").Append(UsedMask.ToString(CultureInfo.InvariantCulture));
            body.Append("\nincludes_inactive: true\nlayers:\n");
            for(int layer=0;layer<32;++layer)
                body.Append("  - layer: ").Append(layer.ToString(CultureInfo.InvariantCulture)).Append("\n    name: ").Append(Scalar(names[layer],128)).Append("\n    occupied_count: ").Append(counts[layer].ToString(CultureInfo.InvariantCulture)).Append('\n');
            return body.ToString();
        }
    }
}
