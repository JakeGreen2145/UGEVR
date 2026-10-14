using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Ugevr
{
    public sealed class HierarchyInventoryProbe : IDisposable
    {
        readonly string directory,runId;
        readonly int processId;
        readonly Func<GameObject,HierarchyInventoryComparison> collect;
        readonly Func<GameObject> createSentinel;
        readonly Action<GameObject> makePersistent,destroySentinel;
        sealed class OwnedSentinel {public GameObject Object;public int Id;public bool Created,PersistentReturned,DestroyRequested,DestroyObserved;public string CleanupError="";}
        readonly List<OwnedSentinel> owned=new List<OwnedSentinel>();
        bool disposed;
        int number;long nextPoll;
        public int RequestsConsumed {get {return number;}}
        public string LastDiagnosticError {get;private set;}
        public HierarchyInventoryProbe(string sessionDirectory,string runId,int processId,Func<GameObject,HierarchyInventoryComparison> collect=null,
            Func<GameObject> createSentinel=null,Action<GameObject> makePersistent=null,Action<GameObject> destroySentinel=null)
        {
            Guid id;try {id=new Guid(runId);} catch {throw new ArgumentException("Invalid hierarchy diagnostic run identity.");}
            if(!Path.IsPathRooted(sessionDirectory) || processId<=0) throw new ArgumentException("Invalid hierarchy diagnostic session.");
            directory=Path.Combine(Path.GetFullPath(sessionDirectory),"UnityVR-evidence");this.runId=id.ToString("D");this.processId=processId;
            this.collect=collect ?? delegate(GameObject anchor) {return HierarchyInventoryComparison.Collect(anchor);};
            this.createSentinel=createSentinel ?? delegate {return new GameObject("UGEVR hierarchy diagnostic sentinel");};
            this.makePersistent=makePersistent ?? delegate(GameObject value) {UnityEngine.Object.DontDestroyOnLoad(value);};
            this.destroySentinel=destroySentinel ?? delegate(GameObject value) {UnityEngine.Object.Destroy(value);};
            for(int i=1;i<=2;++i) if(File.Exists(Result(i)) || File.Exists(Claim(i))) number=i;
        }
        string Result(int value) {return Path.Combine(directory,"hierarchy-inventory-result-"+value+".yaml");}
        string Claim(int value) {return Path.Combine(directory,"hierarchy-inventory-request.claimed-"+value);}
        public void Tick(bool explicitlyActivated,GameObject anchor,int gameFrame,DateTime utc,long ticks)
        {
            if(disposed || !explicitlyActivated || number>=2 || ticks<nextPoll) return;
            nextPoll=ticks+System.Diagnostics.Stopwatch.Frequency/4;
            string temporary=null;
            try
            {
                string request=Path.Combine(directory,"hierarchy-inventory-request");if(!File.Exists(request)) return;
                RejectReparse(directory);RejectReparse(request);File.Move(request,Claim(number+1));++number;
                byte[] bytes=new byte[65];int count=0;
                using(var stream=new FileStream(Claim(number),FileMode.Open,FileAccess.Read,FileShare.ReadWrite | FileShare.Delete))
                {while(count<bytes.Length) {int read=stream.Read(bytes,count,bytes.Length-count);if(read==0) break;count+=read;}}
                bool valid=false;try {valid=count<=64 && new UTF8Encoding(false,true).GetString(bytes,0,count).Trim()=="compare=1";} catch(DecoderFallbackException) {}
                HierarchyInventoryComparison comparison=null;string status=valid?"complete":"invalid_request";
                OwnedSentinel sentinel=null;bool originalValidLoaded=false,originalQueryError=false;int originalIdentity=0;
                if(valid)
                {
                    try {if(anchor!=null) {var originalScene=anchor.scene;originalIdentity=originalScene.GetHashCode();originalValidLoaded=originalScene.IsValid() && originalScene.isLoaded;}} catch {originalQueryError=true;}
                    sentinel=new OwnedSentinel();owned.Add(sentinel);
                    try
                    {
                        sentinel.Object=createSentinel();
                        if(sentinel.Object==null) {status="sentinel_creation_failed";}
                        else
                        {
                            sentinel.Created=true;sentinel.Id=sentinel.Object.GetInstanceID();sentinel.Object.hideFlags=HideFlags.None;
                            makePersistent(sentinel.Object);sentinel.PersistentReturned=true;
                            try {comparison=collect(sentinel.Object);if(comparison==null) status="collector_failed";}
                            catch {status="collector_failed";}
                        }
                    }
                    catch {status=sentinel.Created?"sentinel_persistence_failed":"sentinel_creation_failed";}
                    finally {Retire(sentinel);}
                }
                var body=new StringBuilder();
                HierarchyInventoryComparison.Add(body,"schema_version",2);HierarchyInventoryComparison.Add(body,"run_id",runId);HierarchyInventoryComparison.Add(body,"process_id",processId);
                HierarchyInventoryComparison.Add(body,"request_number",number);HierarchyInventoryComparison.Add(body,"status",status);HierarchyInventoryComparison.Add(body,"game_frame",gameFrame);
                HierarchyInventoryComparison.Add(body,"started_utc",utc.ToString("o",CultureInfo.InvariantCulture));HierarchyInventoryComparison.Add(body,"finished_utc",DateTime.UtcNow.ToString("o",CultureInfo.InvariantCulture));
                HierarchyInventoryComparison.Add(body,"scope",sentinel!=null && sentinel.Created?"before camera/UI borrowing; one owned diagnostic root added before sequential collection":"before camera/UI borrowing; no comparison population claimed");
                HierarchyInventoryComparison.Add(body,"sentinel_creation_attempted",valid);
                HierarchyInventoryComparison.Add(body,"original_population_untouched",false);
                HierarchyInventoryComparison.Add(body,"original_anchor_scene_identity_hash",originalIdentity);HierarchyInventoryComparison.Add(body,"original_anchor_valid_loaded",originalValidLoaded);HierarchyInventoryComparison.Add(body,"original_anchor_query_error",originalQueryError);
                HierarchyInventoryComparison.Add(body,"sentinel_created",sentinel!=null && sentinel.Created);HierarchyInventoryComparison.Add(body,"sentinel_instance_id",sentinel==null?0:sentinel.Id);
                HierarchyInventoryComparison.Add(body,"sentinel_persistence_api_returned",sentinel!=null && sentinel.PersistentReturned);
                HierarchyInventoryComparison.Add(body,"sentinel_present_in_resource_set",comparison!=null && sentinel!=null && comparison.ContainsResourceId(sentinel.Id));
                HierarchyInventoryComparison.Add(body,"sentinel_present_in_hierarchy_set",comparison!=null && sentinel!=null && comparison.ContainsHierarchyId(sentinel.Id));
                HierarchyInventoryComparison.Add(body,"sentinel_destroy_requested",sentinel!=null && sentinel.DestroyRequested);HierarchyInventoryComparison.Add(body,"sentinel_destroy_observed_at_write",sentinel!=null && sentinel.DestroyObserved);
                HierarchyInventoryComparison.Add(body,"sentinel_cleanup_error",sentinel==null?"":sentinel.CleanupError);
                if(comparison!=null) body.Append(comparison.Yaml());
                else {HierarchyInventoryComparison.Add(body,"comparison_exact",false);HierarchyInventoryComparison.Add(body,"production_guard_replaced",false);HierarchyInventoryComparison.Add(body,"gate_eligible",false);}
                RejectReparse(directory);temporary=Result(number)+"."+Guid.NewGuid().ToString("N")+".tmp";
                File.WriteAllText(temporary,body.ToString(),new UTF8Encoding(false));File.Move(temporary,Result(number));LastDiagnosticError=null;
            }
            catch {LastDiagnosticError="hierarchy_diagnostic_unavailable";}
            finally {if(temporary!=null) try {if(File.Exists(temporary)) File.Delete(temporary);} catch {}}
        }
        void Retire(OwnedSentinel sentinel)
        {
            if(sentinel==null || !sentinel.Created) return;
            if(sentinel.Object==null) {sentinel.DestroyObserved=true;return;}
            if(sentinel.DestroyRequested) return;
            try
            {
                // Destroy only the exact created object, and preserve unexpected game-owned additions.
                if(sentinel.Object.transform.childCount!=0) {sentinel.CleanupError="sentinel_has_unowned_children";return;}
                Component[] components=sentinel.Object.GetComponents<Component>();
                if(components.Length!=1 || !(components[0] is Transform)) {sentinel.CleanupError="sentinel_has_unowned_components";return;}
                destroySentinel(sentinel.Object);sentinel.DestroyRequested=true;sentinel.CleanupError="";sentinel.DestroyObserved=sentinel.Object==null;
            }
            catch {sentinel.CleanupError="sentinel_destroy_request_failed";}
        }
        public void Dispose()
        {
            if(disposed) return;disposed=true;
            foreach(OwnedSentinel sentinel in owned)
            {
                Retire(sentinel);
                if(sentinel.Created && sentinel.Object!=null && !sentinel.DestroyRequested) LastDiagnosticError=sentinel.CleanupError;
            }
        }
        static void RejectReparse(string path)
        {for(string part=path;part!=null;part=Path.GetDirectoryName(part)) if((File.GetAttributes(part)&FileAttributes.ReparsePoint)!=0) throw new IOException("Hierarchy diagnostic path crosses a reparse point.");}
    }
}
