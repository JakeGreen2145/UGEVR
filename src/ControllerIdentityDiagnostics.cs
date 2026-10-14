using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Valve.VR;

namespace Ugevr
{
    // Separate passive output. It never supplies LegacyRightSample or authorizes input.
    public sealed class RejectedControllerObservation
    {
        public string CapturedUtc,StateException,IndexException,ClassException,RoleException;
        public uint QueriedIndex;
        public int? PreAxis0RawReturn,PreAxis0Type;
        public ETrackedPropertyError? PreAxis0Error;
        public string PreAxis0Exception;
        public bool? StateResult,Connected,PoseValid,MatrixFinite;
        public uint? Packet,PostRightIndex;
        public ulong? Pressed,Touched;
        public double[] Matrix,Axes;
        public readonly int?[] AxisRawReturns=new int?[5],AxisTypes=new int?[5];
        public readonly ETrackedPropertyError?[] AxisErrors=new ETrackedPropertyError?[5];
        public readonly string[] AxisExceptions=new string[5];
        public ETrackedDeviceClass? PostClass;
        public ETrackedControllerRole? PostRole;
        public StringPropertyObservation PostSerial,PostModel;
        public static RejectedControllerObservation Read(CVRSystem system,uint index)
        {
            var result=new RejectedControllerObservation {QueriedIndex=index,CapturedUtc=DateTime.UtcNow.ToString("o",CultureInfo.InvariantCulture)};
            var preError=ETrackedPropertyError.TrackedProp_Success;
            try
            {
                int raw=system.GetInt32TrackedDeviceProperty(index,ETrackedDeviceProperty.Prop_Axis0Type_Int32,ref preError);
                result.PreAxis0RawReturn=raw;if(preError==ETrackedPropertyError.TrackedProp_Success) result.PreAxis0Type=raw;
            }
            catch(Exception failure) {result.PreAxis0Exception=failure.GetType().FullName;}
            result.PreAxis0Error=preError;
            var state=new VRControllerState_t();var pose=new TrackedDevicePose_t();
            try
            {
                result.StateResult=system.GetControllerStateWithPose(ETrackingUniverseOrigin.TrackingUniverseStanding,index,ref state,(uint)Marshal.SizeOf(typeof(VRControllerState_t)),ref pose);
                result.Connected=pose.bDeviceIsConnected;result.PoseValid=pose.bPoseIsValid;
                var m=pose.mDeviceToAbsoluteTracking;
                result.Matrix=new double[]{m.m0,m.m1,m.m2,m.m3,m.m4,m.m5,m.m6,m.m7,m.m8,m.m9,m.m10,m.m11};
                result.MatrixFinite=true;foreach(double value in result.Matrix) if(Double.IsNaN(value) || Double.IsInfinity(value)) result.MatrixFinite=false;
                result.Packet=state.unPacketNum;result.Pressed=state.ulButtonPressed;result.Touched=state.ulButtonTouched;
                result.Axes=new double[]{state.rAxis0.x,state.rAxis0.y,state.rAxis1.x,state.rAxis1.y,state.rAxis2.x,state.rAxis2.y,state.rAxis3.x,state.rAxis3.y,state.rAxis4.x,state.rAxis4.y};
            }
            catch(Exception error) {result.StateException=error.GetType().FullName;}
            for(int axis=0;axis<5;++axis)
            {
                var error=ETrackedPropertyError.TrackedProp_Success;
                try
                {
                    int raw=system.GetInt32TrackedDeviceProperty(index,(ETrackedDeviceProperty)((int)ETrackedDeviceProperty.Prop_Axis0Type_Int32+axis),ref error);
                    result.AxisRawReturns[axis]=raw;
                    if(error==ETrackedPropertyError.TrackedProp_Success) result.AxisTypes[axis]=raw;
                }
                catch(Exception failure) {result.AxisExceptions[axis]=failure.GetType().FullName;}
                result.AxisErrors[axis]=error;
            }
            try {result.PostRightIndex=system.GetTrackedDeviceIndexForControllerRole(ETrackedControllerRole.RightHand);} catch(Exception error) {result.IndexException=error.GetType().FullName;}
            try {result.PostClass=system.GetTrackedDeviceClass(index);} catch(Exception error) {result.ClassException=error.GetType().FullName;}
            try {result.PostRole=system.GetControllerRoleForTrackedDeviceIndex(index);} catch(Exception error) {result.RoleException=error.GetType().FullName;}
            result.PostSerial=StringPropertyObservation.Read(system,index,ETrackedDeviceProperty.Prop_SerialNumber_String,"passive_post_serial");
            result.PostModel=StringPropertyObservation.Read(system,index,ETrackedDeviceProperty.Prop_ModelNumber_String,"passive_post_model");
            return result;
        }
    }
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
        public string QueryIndexException,QueryClassException,QueryRoleException;
        public uint? Index,PostIndex;
        public ETrackedDeviceClass? DeviceClass,PostClass;
        public ETrackedControllerRole? Role,PostRole;
        public StringPropertyObservation InitialSerial,InitialModel,PostSerial,PostModel;
        public bool IdentityRejected;
        public RejectedControllerObservation RejectedState;
        internal static string Scalar(string value)
        {
            var b=new StringBuilder("\"");value=value??"";
            // Values are private. Escape control characters so they cannot become log/YAML structure.
            foreach(char c in value)
            {
                if(c=='\\' || c=='\"') {b.Append('\\');b.Append(c);}
                else if(c<32 || c==127 || c==0x85 || c==0x2028 || c==0x2029) b.Append("\\u"+((int)c).ToString("x4",CultureInfo.InvariantCulture));
                else b.Append(c);
            }
            return b.Append('"').ToString();
        }
        static string Number<T>(T? value) where T:struct {return value.HasValue?Convert.ToInt64(value.Value,CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture):"null";}
        static string Unsigned<T>(T? value) where T:struct {return value.HasValue?Convert.ToUInt64(value.Value,CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture):"null";}
        static string Boolean(bool? value) {return value.HasValue?value.Value.ToString().ToLowerInvariant():"null";}
        static string Array(double[] values)
        {
            if(values==null) return "null";
            var b=new StringBuilder("[");
            for(int i=0;i<values.Length;++i)
            {
                if(i>0) b.Append(", ");string value=values[i].ToString("R",CultureInfo.InvariantCulture);
                b.Append(Double.IsNaN(values[i]) || Double.IsInfinity(values[i])?Scalar(value):value);
            }
            return b.Append(']').ToString();
        }
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
                "\nquery_index_exception: "+Scalar(QueryIndexException)+"\nquery_class_exception: "+Scalar(QueryClassException)+"\nquery_role_exception: "+Scalar(QueryRoleException)+
                "\nselected_index: "+Number(Index)+"\nselected_class_name: "+Scalar(DeviceClass.HasValue?DeviceClass.Value.ToString():null)+
                "\nselected_class_code: "+Number(DeviceClass)+"\nselected_role_name: "+Scalar(Role.HasValue?Role.Value.ToString():null)+
                "\nselected_role_code: "+Number(Role)+"\npost_index: "+Number(PostIndex)+
                "\npost_class_name: "+Scalar(PostClass.HasValue?PostClass.Value.ToString():null)+"\npost_class_code: "+Number(PostClass)+
                "\npost_role_name: "+Scalar(PostRole.HasValue?PostRole.Value.ToString():null)+"\npost_role_code: "+Number(PostRole)+"\n");
            Property(b,"initial_serial",InitialSerial);Property(b,"initial_model",InitialModel);
            Property(b,"post_serial",PostSerial);Property(b,"post_model",PostModel);
            var passive=RejectedState;
            b.Append("identity_rejected: "+IdentityRejected.ToString().ToLowerInvariant()+"\npassive_rejected_state: "+(passive==null?"null\n":"\n"));
            if(passive!=null)
            {
                b.Append("  diagnostic_only: true\n  authorizes_input: false\n  capture_time_utc: "+Scalar(passive.CapturedUtc)+
                    "\n  queried_index: "+passive.QueriedIndex+"\n  origin: standing\n  pre_state_axis0_returned: "+passive.PreAxis0RawReturn.HasValue.ToString().ToLowerInvariant()+
                    "\n  pre_state_axis0_raw_return: "+Number(passive.PreAxis0RawReturn)+"\n  pre_state_axis0_type: "+Number(passive.PreAxis0Type)+
                    "\n  pre_state_axis0_error_name: "+Scalar(passive.PreAxis0Error.HasValue?passive.PreAxis0Error.Value.ToString():null)+
                    "\n  pre_state_axis0_error_code: "+Number(passive.PreAxis0Error)+"\n  pre_state_axis0_exception: "+Scalar(passive.PreAxis0Exception)+
                    "\n  state_result: "+Boolean(passive.StateResult)+
                    "\n  raw_outputs_valid: "+(passive.StateResult==true).ToString().ToLowerInvariant()+"\n  state_exception: "+Scalar(passive.StateException)+
                    "\n  connected: "+Boolean(passive.Connected)+"\n  pose_valid: "+Boolean(passive.PoseValid)+"\n  matrix_finite: "+Boolean(passive.MatrixFinite)+
                    "\n  matrix_3x4_row_major: "+Array(passive.Matrix)+"\n  packet: "+Unsigned(passive.Packet)+"\n  pressed: "+Unsigned(passive.Pressed)+
                    "\n  touched: "+Unsigned(passive.Touched)+"\n  raw_axes_0_to_4_xy: "+Array(passive.Axes)+
                    "\n  post_right_index: "+Number(passive.PostRightIndex)+"\n  post_class_at_queried_index: "+Number(passive.PostClass)+
                    "\n  post_role_at_queried_index: "+Number(passive.PostRole)+"\n  post_index_exception: "+Scalar(passive.IndexException)+
                    "\n  post_class_exception: "+Scalar(passive.ClassException)+"\n  post_role_exception: "+Scalar(passive.RoleException)+"\n");
                Property(b,"passive_post_serial",passive.PostSerial);Property(b,"passive_post_model",passive.PostModel);
                b.Append("passive_axis_type_properties:\n");
                for(int axis=0;axis<5;++axis)
                {
                    b.Append("  axis"+axis+":\n    returned: "+passive.AxisRawReturns[axis].HasValue.ToString().ToLowerInvariant()+
                        "\n    raw_return: "+Number(passive.AxisRawReturns[axis])+"\n    type_value: "+Number(passive.AxisTypes[axis])+
                        "\n    error_name: "+Scalar(passive.AxisErrors[axis].HasValue?passive.AxisErrors[axis].Value.ToString():null)+
                        "\n    error_code: "+Number(passive.AxisErrors[axis])+"\n    exception_type: "+Scalar(passive.AxisExceptions[axis])+"\n");
                }
            }
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
        {Capture(snapshot,successful,terminal,null);}
        public void Capture(ControllerIdentitySnapshot snapshot,bool successful,bool terminal,CVRSystem system)
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
                // Only an opted-in capture with an available record slot performs extra reads.
                if(system!=null && snapshot.IdentityRejected && snapshot.RejectedState==null && snapshot.Index.HasValue && snapshot.Index.Value<OpenVR.k_unMaxTrackedDeviceCount)
                    snapshot.RejectedState=RejectedControllerObservation.Read(system,snapshot.Index.Value);
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
