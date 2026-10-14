using System;
using System.Globalization;
using System.IO;
using System.Text;
using Valve.VR;

namespace Ugevr
{
    public sealed class StringPropertyObservation
    {
        public string Stage,RawValue,ExceptionType;
        public ETrackedPropertyError Error;
        public uint? RequiredBytes;
        public int InitialCapacity,ReturnedCapacity,Length;
        public const uint RequestedBufferBytes=257;
        public bool Returned,RawTruncated;
        public bool Accepted {get {return Returned && Error==ETrackedPropertyError.TrackedProp_Success && RequiredBytes>0 && RequiredBytes<=RequestedBufferBytes && Length>0 && Length<=256;}}
        public string AcceptedValue {get {return Accepted?RawValue:null;}}
        public string Verdict
        {
            get
            {
                if(!Returned) return "reader_exception";
                if(Error!=ETrackedPropertyError.TrackedProp_Success) return "property_error";
                if(RequiredBytes==0) return "zero_required_bytes";
                if(RequiredBytes>RequestedBufferBytes) return "required_bytes_exceed_buffer";
                if(Length==0) return "empty_builder";
                if(Length>256) return "builder_length_exceeds_bound";
                return "accepted_result";
            }
        }
        public static StringPropertyObservation Read(CVRSystem system,uint index,ETrackedDeviceProperty property,string stage)
        {
            var value=new StringBuilder((int)RequestedBufferBytes);
            var result=new StringPropertyObservation {Stage=stage,InitialCapacity=value.Capacity};
            var error=ETrackedPropertyError.TrackedProp_Success;
            try
            {
                result.RequiredBytes=system.GetStringTrackedDeviceProperty(index,property,value,RequestedBufferBytes,ref error);
                result.Returned=true;
            }
            catch(Exception failure) {result.ExceptionType=failure.GetType().FullName;}
            result.Error=error;result.Length=value.Length;result.ReturnedCapacity=value.Capacity;
            result.RawTruncated=value.Length>256;result.RawValue=value.ToString(0,Math.Min(256,value.Length));
            return result;
        }
    }
    public sealed class ControllerIdentitySnapshot
    {
        public string Stage="selected_index",Verdict="not_validated",ExceptionType;
        public uint? Index,PostIndex;
        public ETrackedDeviceClass? DeviceClass,PostClass;
        public ETrackedControllerRole? Role,PostRole;
        public StringPropertyObservation InitialSerial,InitialModel,PostSerial,PostModel;
        static string Scalar(string value)
        {
            var b=new StringBuilder("\"");value=value??"";
            // Values are private. Escape control characters so they cannot become log/YAML structure.
            foreach(char c in value)
            {
                if(c=='\\' || c=='\"') {b.Append('\\');b.Append(c);}
                else if(c<32 || c==127) b.Append("\\u"+((int)c).ToString("x4",CultureInfo.InvariantCulture));
                else b.Append(c);
            }
            return b.Append('"').ToString();
        }
        static string Number<T>(T? value) where T:struct {return value.HasValue?Convert.ToInt64(value.Value,CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture):"null";}
        static void Property(StringBuilder b,string name,StringPropertyObservation value)
        {
            if(value==null) {b.Append(name+": null\n");return;}
            b.Append(name+":\n  stage: "+Scalar(value.Stage)+"\n  returned: "+value.Returned.ToString().ToLowerInvariant()+
                "\n  raw_value: "+Scalar(value.RawValue)+"\n  raw_truncated: "+value.RawTruncated.ToString().ToLowerInvariant()+
                "\n  error_name: "+Scalar(value.Error.ToString())+"\n  error_code: "+((int)value.Error).ToString(CultureInfo.InvariantCulture)+
                "\n  required_bytes: "+Number(value.RequiredBytes)+"\n  requested_buffer_bytes: "+StringPropertyObservation.RequestedBufferBytes+
                "\n  initial_builder_capacity: "+value.InitialCapacity+"\n  returned_builder_capacity: "+value.ReturnedCapacity+
                "\n  builder_length: "+value.Length+"\n  result_verdict: "+Scalar(value.Verdict)+"\n  exception_type: "+Scalar(value.ExceptionType)+"\n");
        }
        public string ToPrivateYaml()
        {
            var b=new StringBuilder("schema_version: 1\nprivate_device_identity: true\n");
            b.Append("stage: "+Scalar(Stage)+"\nverdict: "+Scalar(Verdict)+"\nexception_type: "+Scalar(ExceptionType)+
                "\nselected_index: "+Number(Index)+"\nselected_class_name: "+Scalar(DeviceClass.HasValue?DeviceClass.Value.ToString():null)+
                "\nselected_class_code: "+Number(DeviceClass)+"\nselected_role_name: "+Scalar(Role.HasValue?Role.Value.ToString():null)+
                "\nselected_role_code: "+Number(Role)+"\npost_index: "+Number(PostIndex)+
                "\npost_class_name: "+Scalar(PostClass.HasValue?PostClass.Value.ToString():null)+"\npost_class_code: "+Number(PostClass)+
                "\npost_role_name: "+Scalar(PostRole.HasValue?PostRole.Value.ToString():null)+"\npost_role_code: "+Number(PostRole)+"\n");
            Property(b,"initial_serial",InitialSerial);Property(b,"initial_model",InitialModel);
            Property(b,"post_serial",PostSerial);Property(b,"post_model",PostModel);
            return b.ToString();
        }
    }
    public sealed class ControllerIdentityCapture
    {
        readonly string directory;
        int ordinaryAttempts;
        public bool FirstSuccessAttempted {get;private set;}
        public bool TerminalAttempted {get;private set;}
        public int Attempts {get;private set;}
        public string LastError {get;private set;}
        public ControllerIdentityCapture(string evidenceDirectory)
        {
            if(String.IsNullOrEmpty(evidenceDirectory) || !Path.IsPathRooted(evidenceDirectory)) throw new ArgumentException("Owned absolute session evidence directory required.");
            directory=Path.GetFullPath(evidenceDirectory);LastError="";
        }
        public void Capture(ControllerIdentitySnapshot snapshot,bool successful,bool terminal)
        {
            if(snapshot==null) return;
            if(!terminal && !successful && snapshot.InitialSerial==null && snapshot.InitialModel==null && snapshot.PostSerial==null && snapshot.PostModel==null) return;
            string name;
            if(terminal) {if(TerminalAttempted) return;TerminalAttempted=true;name="input-identity-terminal.yaml";}
            else if(successful && !FirstSuccessAttempted) {FirstSuccessAttempted=true;name="input-identity-first-success.yaml";}
            else {if(ordinaryAttempts>=7) return;name="input-identity-"+(++ordinaryAttempts)+".yaml";}
            ++Attempts;
            try
            {
                byte[] bytes=new UTF8Encoding(false).GetBytes(snapshot.ToPrivateYaml());
                if(bytes.Length>16384) throw new InvalidOperationException("Identity evidence exceeds 16384 bytes.");
                Directory.CreateDirectory(directory);
                using(var output=new FileStream(Path.Combine(directory,name),FileMode.CreateNew,FileAccess.Write,FileShare.Read)) output.Write(bytes,0,bytes.Length);
                LastError="";
            }
            catch(Exception failure) {LastError=failure.GetType().FullName;}
        }
    }
}
