using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace Ugevr
{
    public sealed class SessionCommand
    {
        public long Revision;
        public bool Enabled;
        public string Recenter;
    }

    // Flat protocol only. No YAML library, dynamic code, file inclusion or object binding.
    public sealed class InjectorSession
    {
        readonly string directory;
        readonly string runId;
        readonly int processId;
        DateTime nextPoll = DateTime.MinValue, nextStatus = DateTime.MinValue;
        public long AcknowledgedRevision;
        public string LastError = "";
        public bool Available { get { return directory != null; } }
        public InjectorSession(string runId, string directory, int processId)
        {
            Guid parsed;
            if (String.IsNullOrEmpty(runId) && String.IsNullOrEmpty(directory)) return;
            if (!TryGuid(runId, out parsed) || String.IsNullOrEmpty(directory) || !Path.IsPathRooted(directory))
                throw new ArgumentException("A session needs a GUID and an absolute directory.");
            this.runId = parsed.ToString("D");
            this.directory = Path.GetFullPath(directory);
            this.processId = processId;
            Directory.CreateDirectory(this.directory);
        }

        public SessionCommand Poll(DateTime now)
        {
            if (!Available || now < nextPoll) return null;
            nextPoll = now.AddMilliseconds(250);
            string path = Path.Combine(directory, "command.txt");
            if (!File.Exists(path)) return null;
            try
            {
                byte[] bytes=new byte[4097];
                int count=0;
                using (var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite | FileShare.Delete))
                {
                    while (count<bytes.Length)
                    {
                        int read=input.Read(bytes,count,bytes.Length-count);
                        if (read==0) break;
                        count+=read;
                    }
                }
                if (count>4096) throw new FormatException("Command exceeds 4096 bytes.");
                string content=new UTF8Encoding(false,true).GetString(bytes,0,count);
                string[] lines=content.Split('\n');
                string id = null, revision = null, enabled = null, recenter = null;
                foreach (string line in lines)
                {
                    if (line.Trim().Length == 0) continue;
                    int equals = line.IndexOf('=');
                    if (equals <= 0) throw new FormatException("Expected key=value.");
                    string key=line.Substring(0,equals).Trim(), value=line.Substring(equals+1).Trim();
                    switch (key)
                    {
                        case "run_id": if (id != null) throw new FormatException("Duplicate run_id."); id=value; break;
                        case "revision": if (revision != null) throw new FormatException("Duplicate revision."); revision=value; break;
                        case "enabled": if (enabled != null) throw new FormatException("Duplicate enabled."); enabled=value; break;
                        case "recenter": if (recenter != null) throw new FormatException("Duplicate recenter."); recenter=value; break;
                        default: throw new FormatException("Unsupported command key: " + key);
                    }
                }
                Guid parsed;
                if (!TryGuid(id, out parsed) || parsed.ToString("D") != runId) throw new FormatException("Command run_id does not match this run.");
                long number;
                if (!Int64.TryParse(revision, NumberStyles.None, CultureInfo.InvariantCulture, out number) || number <= 0)
                    throw new FormatException("Revision must be a positive integer.");
                if (number <= AcknowledgedRevision) return null;
                bool active;
                if (enabled == "true") active=true; else if (enabled == "false") active=false; else throw new FormatException("enabled must be true or false.");
                if (recenter != null && recenter != "true" && recenter != "position" && recenter != "yaw" && recenter != "height" && recenter != "false")
                    throw new FormatException("Unsupported recenter operation.");
                return new SessionCommand { Revision=number, Enabled=active, Recenter=recenter == "false" ? null : recenter };
            }
            catch (Exception ex) { LastError="command: " + ex.Message; return null; }
        }

        static bool TryGuid(string value, out Guid result)
        {
            try { result=new Guid(value); return true; }
            catch { result=Guid.Empty; return false; }
        }
        public void Acknowledge(SessionCommand command) { AcknowledgedRevision=command.Revision; LastError=""; }
        static string Scalar(string value) { return "'" + (value ?? "").Replace("'", "''").Replace("\r", " ").Replace("\n", " ") + "'"; }
        public void Publish(DateTime now, string unityVersion, bool engineReady, bool runtimeReady, bool renderingActive, ulong submittedPairs, bool force)
        {
            if (!Available || (!force && now < nextStatus)) return;
            nextStatus=now.AddSeconds(1);
            if (LastError.StartsWith("status: ")) LastError="";
            string body = "schema_version: 1\nrun_id: " + Scalar(runId) + "\nprocess_id: " + processId.ToString(CultureInfo.InvariantCulture) +
                "\nunity_version: " + Scalar(unityVersion) + "\nengine_ready: " + engineReady.ToString().ToLowerInvariant() +
                "\nruntime: 'openvr'\nruntime_ready: " + runtimeReady.ToString().ToLowerInvariant() +
                "\nrendering_active: " + renderingActive.ToString().ToLowerInvariant() +
                "\nsubmitted_pairs: " + submittedPairs.ToString(CultureInfo.InvariantCulture) +
                "\nacknowledged_revision: " + AcknowledgedRevision.ToString(CultureInfo.InvariantCulture) +
                "\nlast_error: " + Scalar(LastError) + "\n";
            string destination=Path.Combine(directory,"injector-status.yaml"), temporary=destination+"."+Guid.NewGuid().ToString("N")+".tmp";
            try
            {
                File.WriteAllText(temporary,body,new UTF8Encoding(false));
                ReplaceStatus(temporary,destination);
            }
            catch (Exception ex) { LastError="status: " + ex.Message; }
            finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch { } }
        }
        static void ReplaceStatus(string temporary,string destination)
        {
            // Windows readers/AV can briefly deny delete sharing. Retry only atomic
            // replacement; exhaustion leaves the previous complete heartbeat intact.
            for (int attempt=0;;++attempt)
            {
                try
                {
                    if (File.Exists(destination)) File.Replace(temporary,destination,null); else File.Move(temporary,destination);
                    return;
                }
                catch (IOException) { if (attempt>=3) throw; }
                catch (UnauthorizedAccessException) { if (attempt>=3) throw; }
                Thread.Sleep(5 << attempt);
            }
        }
    }
}
