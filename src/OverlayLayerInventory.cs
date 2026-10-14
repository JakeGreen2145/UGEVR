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
        public int DiscoveredTransformCount {get;private set;}
        public int ScannedTransforms {get;private set;}
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
        public static OverlayLayerInventory Capture(int requestedLayer,int scanLimit=DefaultScanLimit)
        {
            ValidateScanLimit(scanLimit);
            var result=new OverlayLayerInventory {ScanLimit=scanLimit};
            for(int layer=0;layer<32;++layer)
            {
                string name=LayerMask.LayerToName(layer) ?? "";
                result.names[layer]=name.Length>128?name.Substring(0,128):name;
            }
            // Unity allocates the complete result before this inspection bound applies.
            UnityEngine.Object[] discovered=Resources.FindObjectsOfTypeAll(typeof(Transform));
            result.DiscoveredTransformCount=discovered.Length;
            foreach(Transform transform in discovered)
            {
                if(result.ScannedTransforms>=scanLimit)
                    throw new OverlayLayerSelectionException("UI layer selection exceeds the configured "+scanLimit.ToString(CultureInfo.InvariantCulture)+" transform inspection limit; inventory is incomplete.",result,requestedLayer);
                ++result.ScannedTransforms;
                if(transform==null || !transform.gameObject.scene.IsValid() || !transform.gameObject.scene.isLoaded) continue;
                int layer=transform.gameObject.layer;
                if(layer<0 || layer>31) throw new OverlayLayerSelectionException("Loaded scene object has an invalid layer.",result,requestedLayer);
                ++result.counts[layer]; ++result.LoadedSceneObjects; result.UsedMask|=1u<<layer;
            }
            result.Complete=true;return result;
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
            body.Append("schema_version: 1\nrun_id: ").Append(Scalar(runId,64)).Append("\nprocess_id: ").Append(processId.ToString(CultureInfo.InvariantCulture));
            body.Append("\nrequested_layer: ").Append(requestedLayer.ToString(CultureInfo.InvariantCulture)).Append("\nreason: ").Append(Scalar(reason,512));
            body.Append("\nscan_complete: ").Append(Complete?"true":"false").Append("\nscanned_transforms: ").Append(ScannedTransforms.ToString(CultureInfo.InvariantCulture));
            body.Append("\nscan_limit: ").Append(ScanLimit.ToString(CultureInfo.InvariantCulture)).Append("\ndiscovered_transform_count: ").Append(DiscoveredTransformCount.ToString(CultureInfo.InvariantCulture));
            body.Append("\nloaded_scene_objects: ").Append(LoadedSceneObjects.ToString(CultureInfo.InvariantCulture)).Append("\nused_mask: ").Append(UsedMask.ToString(CultureInfo.InvariantCulture));
            body.Append("\nincludes_inactive: true\nlayers:\n");
            for(int layer=0;layer<32;++layer)
                body.Append("  - layer: ").Append(layer.ToString(CultureInfo.InvariantCulture)).Append("\n    name: ").Append(Scalar(names[layer],128)).Append("\n    occupied_count: ").Append(counts[layer].ToString(CultureInfo.InvariantCulture)).Append('\n');
            return body.ToString();
        }
    }
}
