using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Valve.VR;

namespace Ugevr
{
    public sealed class PairSourceContext
    {
        public uint? RoleIndex,QueriedIndex;
        public ETrackedDeviceClass? ClassAtQueriedIndex;
        public ETrackedControllerRole? RoleAtQueriedIndex;
        public string IndexException,ClassException,RoleException;
    }
    public sealed class PairSourceHand
    {
        public ETrackedControllerRole ExpectedRole;
        public PairSourceContext Initial,Post,Final;
        public StringPropertyObservation PreSerial,PreModel,PostSerial,PostModel;
        public bool? StateResult,Connected,PoseValid,MatrixFinite,AxesFinite;
        public uint? Packet;
        public ulong? Pressed,Touched;
        public double[] Matrix,Axes;
        public string StateException;
        public readonly int?[] AxisRawReturns=new int?[5],AxisTypes=new int?[5];
        public readonly ETrackedPropertyError?[] AxisErrors=new ETrackedPropertyError?[5];
        public readonly string[] AxisExceptions=new string[5];
    }
    // Raw public SDK evidence. No input sample/backend/admission contract is implemented.
    public sealed class RewiredPairSourceSnapshot
    {
        public string Verdict="runtime_unavailable";
        public PairSourceHand Left,Right;
        public bool? PairContextConsistent,PairIdentityConsistent;
        static void Context(StringBuilder b,string name,PairSourceContext value)
        {
            if(value==null) {b.Append(name+": null\n");return;}
            foreach(string error in new[]{value.IndexException,value.ClassException,value.RoleException})if(error!=null && error.Length>128)throw new InvalidOperationException("Paired context string bound exceeded.");
            b.Append(name+":\n  role_index: "+ControllerIdentitySnapshot.Number(value.RoleIndex)+"\n  queried_index: "+ControllerIdentitySnapshot.Number(value.QueriedIndex)+"\n  class_at_queried_index: "+ControllerIdentitySnapshot.Number(value.ClassAtQueriedIndex)+"\n  role_at_queried_index: "+ControllerIdentitySnapshot.Number(value.RoleAtQueriedIndex)+"\n  index_exception: "+ControllerIdentitySnapshot.Scalar(value.IndexException)+"\n  class_exception: "+ControllerIdentitySnapshot.Scalar(value.ClassException)+"\n  role_exception: "+ControllerIdentitySnapshot.Scalar(value.RoleException)+"\n");
        }
        static void Property(StringBuilder b,string name,StringPropertyObservation value)
        {
            if(value==null) {b.Append(name+": null\n");return;}
            if(value.RawValue!=null && value.RawValue.Length>256 || value.Stage!=null && value.Stage.Length>64 || value.ExceptionType!=null && value.ExceptionType.Length>128)throw new InvalidOperationException("Paired property observation string bound exceeded.");
            b.Append(name+":\n  stage: "+ControllerIdentitySnapshot.Scalar(value.Stage)+"\n  returned: "+value.Returned.ToString().ToLowerInvariant()+"\n  raw_value: "+ControllerIdentitySnapshot.Scalar(value.RawValue)+"\n  raw_truncated: "+value.RawTruncated.ToString().ToLowerInvariant()+"\n  error_name: "+(value.Returned?ControllerIdentitySnapshot.Scalar(value.Error.ToString()):"null")+"\n  error_code: "+(value.Returned?((int)value.Error).ToString(CultureInfo.InvariantCulture):"null")+"\n  required_bytes: "+ControllerIdentitySnapshot.Number(value.RequiredBytes)+"\n  requested_buffer_bytes: 257\n  initial_builder_capacity: "+value.InitialCapacity+"\n  returned_builder_capacity: "+value.ReturnedCapacity+"\n  builder_length: "+value.Length+"\n  result_verdict: "+ControllerIdentitySnapshot.Scalar(value.Verdict)+"\n  exception_type: "+ControllerIdentitySnapshot.Scalar(value.ExceptionType)+"\n");
        }
        static string HandYaml(PairSourceHand value)
        {
            if(value==null)return "null\n";
            if(value.Matrix!=null && value.Matrix.Length!=12 || value.Axes!=null && value.Axes.Length!=10) throw new InvalidOperationException("Paired observation array shape changed.");
            if(value.StateException!=null && value.StateException.Length>128)throw new InvalidOperationException("Paired state string bound exceeded.");
            var b=new StringBuilder("expected_role: "+ControllerIdentitySnapshot.Number<ETrackedControllerRole>(value.ExpectedRole)+"\n");
            Context(b,"initial_context",value.Initial);Context(b,"post_context",value.Post);Context(b,"final_context",value.Final);
            Property(b,"pre_serial",value.PreSerial);Property(b,"pre_model",value.PreModel);
            Property(b,"post_serial",value.PostSerial);Property(b,"post_model",value.PostModel);
            b.Append("origin: standing\nstate_result: "+ControllerIdentitySnapshot.Boolean(value.StateResult)+"\nraw_outputs_valid: "+(value.StateResult==true).ToString().ToLowerInvariant()+"\nstate_exception: "+ControllerIdentitySnapshot.Scalar(value.StateException)+"\nconnected: "+ControllerIdentitySnapshot.Boolean(value.Connected)+"\npose_valid: "+ControllerIdentitySnapshot.Boolean(value.PoseValid)+"\nmatrix_finite: "+ControllerIdentitySnapshot.Boolean(value.MatrixFinite)+"\nmatrix_3x4_row_major: "+ControllerIdentitySnapshot.Array(value.Matrix)+"\naxes_finite: "+ControllerIdentitySnapshot.Boolean(value.AxesFinite)+"\npacket: "+ControllerIdentitySnapshot.Unsigned(value.Packet)+"\npressed: "+ControllerIdentitySnapshot.Unsigned(value.Pressed)+"\ntouched: "+ControllerIdentitySnapshot.Unsigned(value.Touched)+"\nraw_axes_0_to_4_xy: "+ControllerIdentitySnapshot.Array(value.Axes)+"\naxis_type_properties:\n");
            for(int axis=0;axis<5;++axis)
            {
                if(value.AxisExceptions[axis]!=null && value.AxisExceptions[axis].Length>128)throw new InvalidOperationException("Paired metadata string bound exceeded.");
                b.Append("  axis"+axis+":\n    returned: "+value.AxisRawReturns[axis].HasValue.ToString().ToLowerInvariant()+"\n    raw_return: "+ControllerIdentitySnapshot.Number(value.AxisRawReturns[axis])+"\n    type_value: "+ControllerIdentitySnapshot.Number(value.AxisTypes[axis])+"\n    error_code: "+ControllerIdentitySnapshot.Number(value.AxisErrors[axis])+"\n    error_name: "+ControllerIdentitySnapshot.Scalar(value.AxisErrors[axis].HasValue?value.AxisErrors[axis].Value.ToString():null)+"\n    exception_type: "+ControllerIdentitySnapshot.Scalar(value.AxisExceptions[axis])+"\n");
            }
            return b.ToString();
        }
        public string ToPrivateYaml()
        {
            if(Verdict==null || Verdict.Length>64)throw new InvalidOperationException("Paired verdict string bound exceeded.");
            var b=new StringBuilder("schema_version: 2\nprivate_device_identity: true\nauthorizes_input: false\nverdict: "+ControllerIdentitySnapshot.Scalar(Verdict)+"\npair_context_consistent: "+ControllerIdentitySnapshot.Boolean(PairContextConsistent)+"\npair_identity_consistent: "+ControllerIdentitySnapshot.Boolean(PairIdentityConsistent)+"\n");
            b.Append("left:\n");foreach(string line in HandYaml(Left).Split('\n')) if(line.Length!=0)b.Append("  "+line+"\n");
            b.Append("right:\n");foreach(string line in HandYaml(Right).Split('\n')) if(line.Length!=0)b.Append("  "+line+"\n");
            return b.ToString();
        }
    }
    public static class RewiredPairSourceReader
    {
        static string Failure(Exception error) {string name=error.GetType().FullName;return name!=null && name.Length<=128?name:"exception_type_exceeds_bound";}
        static PairSourceContext Context(CVRSystem system,ETrackedControllerRole role,uint? queried)
        {
            var result=new PairSourceContext();
            try {result.RoleIndex=system.GetTrackedDeviceIndexForControllerRole(role);}catch(Exception error) {result.IndexException=Failure(error);}
            uint? index=queried??result.RoleIndex;
            result.QueriedIndex=index;
            if(index.HasValue && index.Value<OpenVR.k_unMaxTrackedDeviceCount)
            {
                try {result.ClassAtQueriedIndex=system.GetTrackedDeviceClass(index.Value);}catch(Exception error){result.ClassException=Failure(error);}
                try {result.RoleAtQueriedIndex=system.GetControllerRoleForTrackedDeviceIndex(index.Value);}catch(Exception error){result.RoleException=Failure(error);}
            }
            return result;
        }
        static StringPropertyObservation Text(CVRSystem system,uint index,ETrackedDeviceProperty property,string phase)
        {var value=StringPropertyObservation.Read(system,index,property,phase);if(value.ExceptionType!=null && value.ExceptionType.Length>128)value.ExceptionType="exception_type_exceeds_bound";return value;}
        static void ReadHand(CVRSystem system,PairSourceHand hand)
        {
            uint? index=hand.Initial.RoleIndex;if(!index.HasValue || index.Value>=OpenVR.k_unMaxTrackedDeviceCount)return;
            hand.PreSerial=Text(system,index.Value,ETrackedDeviceProperty.Prop_SerialNumber_String,"pre_serial");
            hand.PreModel=Text(system,index.Value,ETrackedDeviceProperty.Prop_ModelNumber_String,"pre_model");
            var state=new VRControllerState_t();var pose=new TrackedDevicePose_t();
            try
            {
                hand.StateResult=system.GetControllerStateWithPose(ETrackingUniverseOrigin.TrackingUniverseStanding,index.Value,ref state,(uint)Marshal.SizeOf(typeof(VRControllerState_t)),ref pose);
                hand.Connected=pose.bDeviceIsConnected;hand.PoseValid=pose.bPoseIsValid;
                var m=pose.mDeviceToAbsoluteTracking;hand.Matrix=new double[]{m.m0,m.m1,m.m2,m.m3,m.m4,m.m5,m.m6,m.m7,m.m8,m.m9,m.m10,m.m11};
                hand.MatrixFinite=true;foreach(double value in hand.Matrix)if(Double.IsNaN(value) || Double.IsInfinity(value))hand.MatrixFinite=false;
                hand.Packet=state.unPacketNum;hand.Pressed=state.ulButtonPressed;hand.Touched=state.ulButtonTouched;
                hand.Axes=new double[]{state.rAxis0.x,state.rAxis0.y,state.rAxis1.x,state.rAxis1.y,state.rAxis2.x,state.rAxis2.y,state.rAxis3.x,state.rAxis3.y,state.rAxis4.x,state.rAxis4.y};
                hand.AxesFinite=true;foreach(double value in hand.Axes)if(Double.IsNaN(value) || Double.IsInfinity(value))hand.AxesFinite=false;
            }
            catch(Exception error){hand.StateException=Failure(error);}
            for(int axis=0;axis<5;++axis)
            {
                var error=ETrackedPropertyError.TrackedProp_Success;
                try {int raw=system.GetInt32TrackedDeviceProperty(index.Value,(ETrackedDeviceProperty)((int)ETrackedDeviceProperty.Prop_Axis0Type_Int32+axis),ref error);hand.AxisRawReturns[axis]=raw;hand.AxisErrors[axis]=error;if(error==ETrackedPropertyError.TrackedProp_Success)hand.AxisTypes[axis]=raw;}
                catch(Exception failure){hand.AxisExceptions[axis]=Failure(failure);}
            }
            hand.Post=Context(system,hand.ExpectedRole,index);
            hand.PostSerial=Text(system,index.Value,ETrackedDeviceProperty.Prop_SerialNumber_String,"post_serial");
            hand.PostModel=Text(system,index.Value,ETrackedDeviceProperty.Prop_ModelNumber_String,"post_model");
        }
        static bool Complete(PairSourceContext value) {return value!=null && value.RoleIndex.HasValue && value.ClassAtQueriedIndex.HasValue && value.RoleAtQueriedIndex.HasValue;}
        static bool Same(PairSourceContext a,PairSourceContext b) {return a.RoleIndex==b.RoleIndex && a.ClassAtQueriedIndex==b.ClassAtQueriedIndex && a.RoleAtQueriedIndex==b.RoleAtQueriedIndex;}
        static bool IdentityRead(PairSourceHand hand) {return hand.PreSerial!=null && hand.PreModel!=null && hand.PostSerial!=null && hand.PostModel!=null && hand.PreSerial.Accepted && hand.PreModel.Accepted && hand.PostSerial.Accepted && hand.PostModel.Accepted;}
        public static RewiredPairSourceSnapshot Read(CVRSystem system)
        {
            var result=new RewiredPairSourceSnapshot();if(system==null)return result;
            result.Left=new PairSourceHand {ExpectedRole=ETrackedControllerRole.LeftHand,Initial=Context(system,ETrackedControllerRole.LeftHand,null)};
            result.Right=new PairSourceHand {ExpectedRole=ETrackedControllerRole.RightHand,Initial=Context(system,ETrackedControllerRole.RightHand,null)};
            ReadHand(system,result.Left);ReadHand(system,result.Right);
            result.Left.Final=Context(system,ETrackedControllerRole.LeftHand,result.Left.Initial.RoleIndex);result.Right.Final=Context(system,ETrackedControllerRole.RightHand,result.Right.Initial.RoleIndex);
            var left=result.Left;var right=result.Right;
            if(Complete(left.Initial) && Complete(left.Post) && Complete(left.Final) && Complete(right.Initial) && Complete(right.Post) && Complete(right.Final))
                result.PairContextConsistent=left.Initial.RoleIndex!=right.Initial.RoleIndex && left.Initial.ClassAtQueriedIndex==ETrackedDeviceClass.Controller && right.Initial.ClassAtQueriedIndex==ETrackedDeviceClass.Controller && left.Initial.RoleAtQueriedIndex==left.ExpectedRole && right.Initial.RoleAtQueriedIndex==right.ExpectedRole && Same(left.Initial,left.Post) && Same(left.Initial,left.Final) && Same(right.Initial,right.Post) && Same(right.Initial,right.Final);
            if(IdentityRead(left) && IdentityRead(right))result.PairIdentityConsistent=left.PreSerial.RawValue==left.PostSerial.RawValue && left.PreModel.RawValue==left.PostModel.RawValue && right.PreSerial.RawValue==right.PostSerial.RawValue && right.PreModel.RawValue==right.PostModel.RawValue && left.PreSerial.RawValue!=right.PreSerial.RawValue;
            bool metadata=true;for(int axis=0;axis<5;++axis)if(!left.AxisTypes[axis].HasValue || !right.AxisTypes[axis].HasValue)metadata=false;
            result.Verdict=result.PairContextConsistent!=true?"pair_context_unavailable_or_changed":result.PairIdentityConsistent!=true?"pair_identity_unavailable_or_changed":left.StateResult!=true || right.StateResult!=true?"joint_state_unavailable_or_false":left.Connected!=true || right.Connected!=true?"controller_disconnected":left.PoseValid!=true || right.PoseValid!=true?"pose_invalid":left.MatrixFinite!=true || right.MatrixFinite!=true?"pose_nonfinite":left.AxesFinite!=true || right.AxesFinite!=true?"axes_nonfinite":!metadata?"observed_with_metadata_unavailable":"observed";
            return result;
        }
    }
    public sealed class RewiredPairSourceObservation
    {
        // Conservative escaped payload measured at 53438 bytes, plus a fixed envelope.
        public const int MaximumRecordBytes=65536;
        public const int MaximumWindows=RewiredSourceObservation.MaximumWindows,MaximumSamples=RewiredSourceObservation.MaximumSamples,MaximumWindowBytes=RewiredSourceObservation.MaximumWindowBytes;
        readonly RewiredSourceObservation window;
        public RewiredPairSourceObservation(string directory,string runId,int processId,long frequency,Func<RewiredPairSourceSnapshot> read)
        {
            if(read==null)throw new ArgumentException("Paired observation needs a reader.");
            window=new RewiredSourceObservation(directory,runId,processId,frequency,delegate {var snapshot=read();return snapshot==null?null:new PassiveSourceRecord {Yaml=snapshot.ToPrivateYaml(),Reason=snapshot.Verdict};},true);
        }
        public int Windows {get {return window.Windows;}}
        public int Samples {get {return window.Samples;}}
        public bool Active {get {return window.Active;}}
        public string Status {get {return window.Status;}}
        public string Reason {get {return window.Reason;}}
        public string LastError {get {return window.LastError;}}
        public void Tick(bool optIn,bool enabled,bool focused,ulong generation,DateTime utc,long tick) {window.Tick(optIn,enabled,focused,generation,utc,tick);}
        public void Cancel(string reason,DateTime utc,long tick) {window.Cancel(reason,utc,tick);}
    }
}
