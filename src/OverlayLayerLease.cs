using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ugevr
{
    public sealed class OverlayLayerLease : IDisposable
    {
        sealed class Entry {public GameObject Object;public int Layer;public bool Attempted;}
        readonly List<Entry> entries=new List<Entry>();
        readonly Dictionary<int,GameObject> owned=new Dictionary<int,GameObject>();
        readonly List<string> errors=new List<string>();
        readonly int sceneScanLimit;
        bool applied,disposed;
        public readonly int Layer;
        public int Mask {get {return 1<<Layer;}}
        public int ObjectCount {get {return entries.Count;}}
        public int DestroyedObjects {get;private set;}
        public string[] Errors {get {return errors.ToArray();}}
        public OverlayLayerLease(IEnumerable<GameObject> objects,int requestedLayer=-1,int sceneScanLimit=OverlayLayerInventory.DefaultScanLimit)
        {
            OverlayLayerInventory.ValidateScanLimit(sceneScanLimit);
            this.sceneScanLimit=sceneScanLimit;
            if(requestedLayer!=-1 && (requestedLayer<8 || requestedLayer>31))
                throw new ArgumentOutOfRangeException("requestedLayer","UI isolation layer must be -1 for automatic selection or 8 through 31.");
            OverlayLayerInventory inventory=OverlayLayerInventory.Capture(requestedLayer,sceneScanLimit);
            Layer=-1;
            if(requestedLayer!=-1)
            {
                if(inventory.OccupiedCount(requestedLayer)!=0)
                    throw new OverlayLayerSelectionException("Explicit UI isolation layer is occupied by loaded scene objects, including inactive objects.",inventory,requestedLayer);
                Layer=requestedLayer;
            }
            else for(int layer=31;layer>=8;--layer)
                if(inventory.OccupiedCount(layer)==0 && String.IsNullOrEmpty(inventory.Name(layer))) {Layer=layer;break;}
            if(Layer<0) throw new OverlayLayerSelectionException("No unused unnamed scene layer is available for after-effects UI capture.",inventory,requestedLayer);
            foreach(GameObject gameObject in objects)
            {
                if(gameObject==null || owned.ContainsKey(gameObject.GetInstanceID())) continue;
                if(entries.Count>=8192) throw new OverlayLayerSelectionException("UI layer selection exceeds 8192 objects; use an explicit profile selection.",inventory,requestedLayer);
                entries.Add(new Entry {Object=gameObject,Layer=gameObject.layer});
                owned.Add(gameObject.GetInstanceID(),gameObject);
            }
        }
        public void Apply()
        {
            if(disposed) throw new ObjectDisposedException("OverlayLayerLease");
            CheckExclusive(Resources.FindObjectsOfTypeAll(typeof(Transform)));
            applied=true;
            foreach(Entry entry in entries) if(entry.Object!=null) {entry.Attempted=true;entry.Object.layer=Layer;}
        }
        public void ValidateExclusive()
        {
            CheckExclusive(Resources.FindObjectsOfTypeAll(typeof(Transform)));
            CheckExclusive(Resources.FindObjectsOfTypeAll(typeof(Renderer)));
            CheckExclusive(Resources.FindObjectsOfTypeAll(typeof(CanvasRenderer)));
        }
        void CheckExclusive(UnityEngine.Object[] objects)
        {
            int scanned=0;
            foreach(UnityEngine.Object item in objects)
            {
                if(++scanned>sceneScanLimit) throw new InvalidOperationException("UI layer validation exceeds the configured "+sceneScanLimit+" object inspection limit. The UI pass was skipped.");
                var component=item as Component;
                GameObject owner=null;
                bool ownedAlive=component!=null && owned.TryGetValue(component.gameObject.GetInstanceID(),out owner) && owner!=null;
                if(component!=null && component.gameObject.scene.IsValid() && component.gameObject.scene.isLoaded && component.gameObject.layer==Layer && !ownedAlive)
                    throw new InvalidOperationException("An unowned scene object entered the isolated UI layer: "+component.name+". The UI pass was skipped.");
            }
        }
        public void Dispose()
        {
            if(disposed) return;disposed=true;if(!applied) return;
            RecordExclusiveFailure("before layer restoration");
            foreach(Entry entry in entries)
            {
                if(!entry.Attempted) continue;
                if(entry.Object==null) {++DestroyedObjects;continue;}
                try
                {
                    if(entry.Object.layer==entry.Layer) continue;
                    if(entry.Object.layer!=Layer)
                    {
                        if(errors.Count<64) errors.Add("UI object layer changed while borrowed: "+entry.Object.name+". Its newer layer was preserved.");
                        continue;
                    }
                    entry.Object.layer=entry.Layer;
                    if(entry.Object.layer!=entry.Layer && errors.Count<64) errors.Add("UI object layer did not restore: "+entry.Object.name);
                }
                catch(Exception error) {if(errors.Count<64) errors.Add("UI layer restore: "+error.Message);}
            }
            RecordExclusiveFailure("after layer restoration");
        }
        void RecordExclusiveFailure(string stage)
        {
            try {ValidateExclusive();}
            catch(Exception error) {if(errors.Count<64) errors.Add("UI layer exclusivity "+stage+": "+error.Message);}
        }
    }
}
