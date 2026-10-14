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
        bool applied,disposed;
        public readonly int Layer;
        public int Mask {get {return 1<<Layer;}}
        public int ObjectCount {get {return entries.Count;}}
        public int DestroyedObjects {get;private set;}
        public string[] Errors {get {return errors.ToArray();}}
        public OverlayLayerLease(IEnumerable<GameObject> objects)
        {
            int used=0,scanned=0;
            foreach(Transform transform in Resources.FindObjectsOfTypeAll(typeof(Transform)))
            {
                if(++scanned>100000) throw new InvalidOperationException("UI layer selection exceeds 100000 loaded transforms; use an explicit engine adapter.");
                if(transform!=null && transform.gameObject.scene.IsValid() && transform.gameObject.scene.isLoaded) used|=1<<transform.gameObject.layer;
            }
            Layer=-1;
            for(int layer=31;layer>=8;--layer)
                if((used & (1<<layer))==0 && String.IsNullOrEmpty(LayerMask.LayerToName(layer))) {Layer=layer;break;}
            if(Layer<0) throw new InvalidOperationException("No unused unnamed scene layer is available for after-effects UI capture.");
            foreach(GameObject gameObject in objects)
            {
                if(gameObject==null || owned.ContainsKey(gameObject.GetInstanceID())) continue;
                if(entries.Count>=8192) throw new InvalidOperationException("UI layer selection exceeds 8192 objects; use an explicit profile selection.");
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
            CheckExclusive(Resources.FindObjectsOfTypeAll(typeof(Renderer)));
            CheckExclusive(Resources.FindObjectsOfTypeAll(typeof(CanvasRenderer)));
        }
        void CheckExclusive(UnityEngine.Object[] objects)
        {
            int scanned=0;
            foreach(UnityEngine.Object item in objects)
            {
                if(++scanned>100000) throw new InvalidOperationException("UI layer validation exceeds 100000 objects; use an explicit engine adapter.");
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
        }
    }
}
