using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace Ugevr
{
    public struct KeyboardESample
    {
        public bool DevicePresent, DeviceEnabled, Down, Press, Release;
    }
    public interface IKeyboardEReader
    {
        KeyboardESample Read();
        string UnavailableReason { get; }
    }

    // Only the independently inspected InputSystem 1.4.1 Outer Wilds artifact is supported here.
    public sealed class PublicKeyboardEReader : IKeyboardEReader
    {
        static readonly Guid ExpectedModule=new Guid("eb31f34a-975f-46d1-9e8a-f468772f04b5");
        readonly PropertyInfo current, eKey, enabled, down, press, release;
        readonly string unavailable;
        public string UnavailableReason { get { return unavailable; } }
        PublicKeyboardEReader(string reason) { unavailable=reason; }
        PublicKeyboardEReader(Assembly assembly)
        {
            Type keyboard=assembly.GetType("UnityEngine.InputSystem.Keyboard",true);
            Type key=assembly.GetType("UnityEngine.InputSystem.Controls.KeyControl",true);
            Type button=assembly.GetType("UnityEngine.InputSystem.Controls.ButtonControl",true);
            Type device=assembly.GetType("UnityEngine.InputSystem.InputDevice",true);
            current=Getter(keyboard,"current",keyboard,true);
            eKey=Getter(keyboard,"eKey",key,false);
            enabled=Getter(device,"enabled",typeof(bool),false);
            down=Getter(button,"isPressed",typeof(bool),false);
            press=Getter(button,"wasPressedThisFrame",typeof(bool),false);
            release=Getter(button,"wasReleasedThisFrame",typeof(bool),false);
            unavailable="";
        }
        static PropertyInfo Getter(Type type,string name,Type result,bool isStatic)
        {
            PropertyInfo property=type.GetProperty(name,BindingFlags.Public | BindingFlags.DeclaredOnly | (isStatic?BindingFlags.Static:BindingFlags.Instance));
            if (property==null || property.PropertyType!=result || property.GetIndexParameters().Length!=0)
                throw new InvalidOperationException("Unexpected public InputSystem property shape.");
            MethodInfo getter=property.GetGetMethod(false);
            if (getter==null || getter.IsStatic!=isStatic || getter.IsVirtual)
                throw new InvalidOperationException("Unexpected public InputSystem getter shape.");
            return property;
        }
        public static bool MatchesIdentity(AssemblyName name,Guid module)
        {
            byte[] token=name.GetPublicKeyToken();
            return name.Name=="Unity.InputSystem" && name.Version!=null && name.Version.Equals(new Version(1,4,1,0)) &&
                (token==null || token.Length==0) && module==ExpectedModule;
        }
        public static IKeyboardEReader Create()
        {
            try
            {
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (assembly.GetName().Name!="Unity.InputSystem") continue;
                    if (!MatchesIdentity(assembly.GetName(),assembly.ManifestModule.ModuleVersionId)) return new PublicKeyboardEReader("assembly_identity_mismatch");
                    return new PublicKeyboardEReader(assembly);
                }
                return new PublicKeyboardEReader("assembly_not_loaded");
            }
            catch { return new PublicKeyboardEReader("public_api_shape_unavailable"); }
        }
        public KeyboardESample Read()
        {
            KeyboardESample sample=new KeyboardESample();
            if (unavailable.Length!=0) return sample;
            object keyboard=current.GetValue(null,null);
            if (keyboard==null) return sample;
            sample.DevicePresent=true;
            sample.DeviceEnabled=(bool)enabled.GetValue(keyboard,null);
            if (!sample.DeviceEnabled) return sample;
            object key=eKey.GetValue(keyboard,null);
            if (key==null) return sample;
            sample.Down=(bool)down.GetValue(key,null);
            sample.Press=(bool)press.GetValue(key,null);
            sample.Release=(bool)release.GetValue(key,null);
            return sample;
        }
    }

    public sealed class KeyboardEProbe
    {
        readonly string directory, runId;
        readonly int processId;
        readonly long frequency;
        readonly Func<IKeyboardEReader> createReader;
        IKeyboardEReader reader;
        int number, frames, focusedFrames;
        long start, deadline, nextPoll;
        DateTime startedUtc;
        bool active, present, enabled, downSeen, pressSeen, releaseSeen, focusSeen, lastFocus;
        public bool Active { get { return active; } }
        public int RequestsConsumed { get { return number; } }
        public string LastDiagnosticError { get; private set; }
        public KeyboardEProbe(string sessionDirectory,string runId,int processId,long frequency,Func<IKeyboardEReader> createReader)
        {
            Guid parsed;
            try { parsed=new Guid(runId); } catch { throw new ArgumentException("Invalid input probe run identity."); }
            if (!Path.IsPathRooted(sessionDirectory) || processId<=0 || frequency<=0 || frequency>Int64.MaxValue/4 || createReader==null)
                throw new ArgumentException("Invalid input probe session.");
            this.directory=Path.Combine(Path.GetFullPath(sessionDirectory),"UnityVR-evidence");
            this.runId=parsed.ToString("D"); this.processId=processId; this.frequency=frequency; this.createReader=createReader;
            // A component restart must not reset the run's four-request limit.
            for (int i=1;i<=4;++i)
                if (File.Exists(ResultPath(i)) || File.Exists(ClaimPath(i))) number=i;
        }
        string ResultPath(int value) { return Path.Combine(directory,"input-probe-result-"+value+".yaml"); }
        string ClaimPath(int value) { return Path.Combine(directory,"input-probe-request.claimed-"+value); }
        public void Tick(bool captureOptIn,bool applicationFocused,DateTime utc,long monotonicTicks)
        {
            if (!captureOptIn)
            {
                if (active) Finish("cancelled","capture_opt_in_removed",utc,monotonicTicks);
                return;
            }
            try
            {
                if (!active)
                {
                    if (number>=4 || monotonicTicks<nextPoll) return;
                    nextPoll=monotonicTicks+frequency/4;
                    string request=Path.Combine(directory,"input-probe-request");
                    if (!File.Exists(request)) return;
                    RejectReparse(directory); RejectReparse(request);
                    string claimed=ClaimPath(number+1);
                    File.Move(request,claimed); ++number; active=true;
                    frames=focusedFrames=0; present=enabled=downSeen=pressSeen=releaseSeen=focusSeen=lastFocus=false;
                    start=monotonicTicks; deadline=start+4*frequency; startedUtc=utc;
                    string content;
                    using (var stream=new FileStream(claimed,FileMode.Open,FileAccess.Read,FileShare.ReadWrite | FileShare.Delete))
                    {
                        byte[] bytes=new byte[65]; int count=0;
                        while (count<bytes.Length)
                        {
                            int read=stream.Read(bytes,count,bytes.Length-count);
                            if (read==0) break; count+=read;
                        }
                        if (count>64) { Finish("invalid_request","fixed_duration_required",utc,monotonicTicks); return; }
                        content=new UTF8Encoding(false,true).GetString(bytes,0,count);
                    }
                    if (content.Trim()!="duration_seconds=4") { Finish("invalid_request","fixed_duration_required",utc,monotonicTicks); return; }
                    reader=createReader();
                    if (reader==null || !String.IsNullOrEmpty(reader.UnavailableReason)) { Finish("unavailable",reader==null?"reader_unavailable":reader.UnavailableReason,utc,monotonicTicks); return; }
                    active=true;
                }
                if (monotonicTicks<start) { Finish("clock_error","monotonic_clock_regressed",utc,monotonicTicks); return; }
                if (monotonicTicks>=deadline) { Finish("complete","",utc,monotonicTicks); return; }
                ++frames; lastFocus=applicationFocused; focusSeen|=applicationFocused;
                // Observe keys only in focused game frames, through the game's engine state.
                if (!applicationFocused) return;
                ++focusedFrames;
                KeyboardESample sample=reader.Read();
                present|=sample.DevicePresent; enabled|=sample.DeviceEnabled;
                downSeen|=sample.Down; pressSeen|=sample.Press; releaseSeen|=sample.Release;
            }
            catch
            {
                if (active) Finish("error",reader==null?"probe_setup_unavailable":"public_input_read_failed",utc,monotonicTicks);
                else LastDiagnosticError="input_probe_request_unavailable";
            }
        }
        public void Cancel(DateTime utc,long monotonicTicks) { if (active) Finish("cancelled","game_lifecycle_end",utc,monotonicTicks); }
        void Finish(string status,string reason,DateTime utc,long monotonicTicks)
        {
            active=false; reader=null;
            string path=ResultPath(number), temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try
            {
                RejectReparse(directory);
                var body=new StringBuilder();
                Add(body,"schema_version",1); Add(body,"run_id",runId); Add(body,"process_id",processId); Add(body,"probe_number",number);
                Add(body,"status",status); Add(body,"reason",reason); Add(body,"key","E"); Add(body,"backend","Unity.InputSystem"); Add(body,"backend_version","1.4.1.0");
                Add(body,"requested_duration_seconds",4); Add(body,"monotonic_frequency",frequency); Add(body,"monotonic_start_ticks",start); Add(body,"monotonic_deadline_ticks",deadline); Add(body,"monotonic_end_ticks",monotonicTicks);
                Add(body,"duration_seconds",Math.Min(4,Math.Max(0,(monotonicTicks-start)/(double)frequency))); Add(body,"started_utc",startedUtc.ToString("o",CultureInfo.InvariantCulture)); Add(body,"finished_utc",utc.ToString("o",CultureInfo.InvariantCulture));
                Add(body,"frame_count",frames); Add(body,"focused_frame_count",focusedFrames); Add(body,"application_focused",lastFocus); Add(body,"application_focused_seen",focusSeen); Add(body,"application_focused_all_frames",frames>0 && frames==focusedFrames);
                Add(body,"device_present",present); Add(body,"device_enabled",enabled); Add(body,"down_seen",downSeen); Add(body,"press_seen",pressSeen); Add(body,"release_seen",releaseSeen); Add(body,"input_receipt","unknown");
                File.WriteAllText(temporary,body.ToString(),new UTF8Encoding(false));
                for (int attempt=0;;++attempt)
                {
                    try { if (File.Exists(path)) File.Replace(temporary,path,null); else File.Move(temporary,path); break; }
                    catch (Exception error) { if (attempt>=10 || !(error is IOException || error is UnauthorizedAccessException)) throw; System.Threading.Thread.Sleep(10); }
                }
                LastDiagnosticError="";
            }
            catch { LastDiagnosticError="input_probe_result_unavailable"; }
            finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch { } }
        }
        static void Add(StringBuilder body,string key,object value)
        {
            body.Append(key).Append(": ");
            if (value is bool) body.Append(((bool)value)?"true":"false");
            else if (value is string) body.Append('\'').Append(((string)value).Replace("'","''").Replace("\r"," ").Replace("\n"," ")).Append('\'');
            else body.Append(Convert.ToString(value,CultureInfo.InvariantCulture));
            body.Append('\n');
        }
        static void RejectReparse(string path)
        {
            for (string part=path; part!=null; part=Path.GetDirectoryName(part))
                if ((File.GetAttributes(part)&FileAttributes.ReparsePoint)!=0) throw new IOException("Input probe cannot cross a reparse point.");
        }
    }
}
