using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Ugevr
{
    public sealed class HierarchyInventoryProbe
    {
        readonly string directory,runId;
        readonly int processId;
        readonly Func<GameObject,HierarchyInventoryComparison> collect;
        int number;long nextPoll;
        public int RequestsConsumed {get {return number;}}
        public string LastDiagnosticError {get;private set;}
        public HierarchyInventoryProbe(string sessionDirectory,string runId,int processId,Func<GameObject,HierarchyInventoryComparison> collect=null)
        {
            Guid id;try {id=new Guid(runId);} catch {throw new ArgumentException("Invalid hierarchy diagnostic run identity.");}
            if(!Path.IsPathRooted(sessionDirectory) || processId<=0) throw new ArgumentException("Invalid hierarchy diagnostic session.");
            directory=Path.Combine(Path.GetFullPath(sessionDirectory),"UnityVR-evidence");this.runId=id.ToString("D");this.processId=processId;
            this.collect=collect ?? delegate(GameObject anchor) {return HierarchyInventoryComparison.Collect(anchor);};
            for(int i=1;i<=2;++i) if(File.Exists(Result(i)) || File.Exists(Claim(i))) number=i;
        }
        string Result(int value) {return Path.Combine(directory,"hierarchy-inventory-result-"+value+".yaml");}
        string Claim(int value) {return Path.Combine(directory,"hierarchy-inventory-request.claimed-"+value);}
        public void Tick(bool explicitlyActivated,GameObject anchor,int gameFrame,DateTime utc,long ticks)
        {
            if(!explicitlyActivated || number>=2 || ticks<nextPoll) return;
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
                if(valid) {try {comparison=collect(anchor);if(comparison==null) status="collector_failed";} catch {status="collector_failed";}}
                var body=new StringBuilder();
                HierarchyInventoryComparison.Add(body,"schema_version",1);HierarchyInventoryComparison.Add(body,"run_id",runId);HierarchyInventoryComparison.Add(body,"process_id",processId);
                HierarchyInventoryComparison.Add(body,"request_number",number);HierarchyInventoryComparison.Add(body,"status",status);HierarchyInventoryComparison.Add(body,"game_frame",gameFrame);
                HierarchyInventoryComparison.Add(body,"started_utc",utc.ToString("o",CultureInfo.InvariantCulture));HierarchyInventoryComparison.Add(body,"finished_utc",DateTime.UtcNow.ToString("o",CultureInfo.InvariantCulture));
                HierarchyInventoryComparison.Add(body,"scope","untouched desktop state before an eye pair; sequential diagnostic only");
                if(comparison!=null) body.Append(comparison.Yaml());
                else {HierarchyInventoryComparison.Add(body,"comparison_exact",false);HierarchyInventoryComparison.Add(body,"production_guard_replaced",false);HierarchyInventoryComparison.Add(body,"gate_eligible",false);}
                RejectReparse(directory);temporary=Result(number)+"."+Guid.NewGuid().ToString("N")+".tmp";
                File.WriteAllText(temporary,body.ToString(),new UTF8Encoding(false));File.Move(temporary,Result(number));LastDiagnosticError=null;
            }
            catch {LastDiagnosticError="hierarchy_diagnostic_unavailable";}
            finally {if(temporary!=null) try {if(File.Exists(temporary)) File.Delete(temporary);} catch {}}
        }
        static void RejectReparse(string path)
        {for(string part=path;part!=null;part=Path.GetDirectoryName(part)) if((File.GetAttributes(part)&FileAttributes.ReparsePoint)!=0) throw new IOException("Hierarchy diagnostic path crosses a reparse point.");}
    }
}
