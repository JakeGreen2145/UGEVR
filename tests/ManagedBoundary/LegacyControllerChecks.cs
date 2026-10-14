using System;
using System.Runtime.InteropServices;
using System.Reflection;
using System.IO;
using System.Text;
using Ugevr;
using Valve.VR;

static class LegacyControllerChecks
{
    sealed class Runtime
    {
        public int AxisKind=2,StateCalls,RoleCalls,AxisCalls,StringCalls;
        public string Serial="fixture",Model="fixture-model";
        public bool StringError,IdentityChange,ModelError,ModelChange,NonfinitePose,MissingStringReader,MissingStateReader,MissingAxisReader;
        public ETrackedPropertyError AxisError=ETrackedPropertyError.TrackedProp_Success;
        public bool PoseValid=true,Connected=true,RoleChange,StateSuccess=true,PropertyError;
        public ulong Pressed=1UL<<7,Touched;
        public uint StringLength=8;
        public float X=0.3f,Y=-0.4f;
        public float PoseX,PoseY,PoseZ;
        public VRControllerAxis_t[] ExtraAxes=new VRControllerAxis_t[4];
        public int[] AxisKinds;
        public ETrackedPropertyError[] AxisPropertyErrors;
        public IVRSystem Table()
        {
            var t=new IVRSystem();
            t.GetTrackedDeviceIndexForControllerRole=delegate(ETrackedControllerRole role) {++RoleCalls;return RoleChange && RoleCalls>1?4u:3u;};
            t.GetControllerRoleForTrackedDeviceIndex=delegate(uint index) {return ETrackedControllerRole.RightHand;};
            t.GetTrackedDeviceClass=delegate(uint index) {return ETrackedDeviceClass.Controller;};
            t.GetInt32TrackedDeviceProperty=delegate(uint index,ETrackedDeviceProperty property,ref ETrackedPropertyError error)
            {++AxisCalls;error=AxisPropertyErrors!=null?AxisPropertyErrors[(int)property-(int)ETrackedDeviceProperty.Prop_Axis0Type_Int32]:PropertyError?ETrackedPropertyError.TrackedProp_UnknownProperty:AxisError;return AxisKinds==null?AxisKind:AxisKinds[(int)property-(int)ETrackedDeviceProperty.Prop_Axis0Type_Int32];};
            t.GetStringTrackedDeviceProperty=delegate(uint index,ETrackedDeviceProperty property,StringBuilder value,uint capacity,ref ETrackedPropertyError error)
            {
                ++StringCalls;bool model=property==ETrackedDeviceProperty.Prop_ModelNumber_String;
                error=StringError || (model && ModelError)?ETrackedPropertyError.TrackedProp_UnknownProperty:ETrackedPropertyError.TrackedProp_Success;
                value.Append(model?(ModelChange && StateCalls>0?"replacement-model":Model):(IdentityChange && StateCalls>0?"replacement":Serial));return StringLength;
            };
            t.GetControllerStateWithPose=delegate(ETrackingUniverseOrigin origin,uint index,ref VRControllerState_t state,uint size,ref TrackedDevicePose_t pose)
            {
                ++StateCalls;if(size!=(uint)Marshal.SizeOf(typeof(VRControllerState_t)) || origin!=ETrackingUniverseOrigin.TrackingUniverseStanding) throw new Exception("Incorrect public ABI arguments.");
                state=new VRControllerState_t {unPacketNum=17,ulButtonPressed=Pressed,ulButtonTouched=Touched,rAxis0=new VRControllerAxis_t {x=X,y=Y},rAxis1=ExtraAxes[0],rAxis2=ExtraAxes[1],rAxis3=ExtraAxes[2],rAxis4=ExtraAxes[3]};
                pose=new TrackedDevicePose_t {bDeviceIsConnected=Connected,bPoseIsValid=PoseValid,mDeviceToAbsoluteTracking=new HmdMatrix34_t {m0=1,m5=1,m10=1,m3=NonfinitePose?float.NaN:PoseX,m7=PoseY,m11=PoseZ}};return StateSuccess;
            };if(MissingStringReader) t.GetStringTrackedDeviceProperty=null;if(MissingStateReader) t.GetControllerStateWithPose=null;if(MissingAxisReader) t.GetInt32TrackedDeviceProperty=null;return t;
        }
    }
    static LegacyRightSample Read(Runtime runtime,bool buttonOnly=false,bool observedAxis=false,ControllerIdentityCapture capture=null)
    {
        var table=runtime.Table();IntPtr pointer=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(IVRSystem)));
        try {Marshal.StructureToPtr(table,pointer,false);var system=new CVRSystem(pointer);var sample=OuterGamepadCompatibilityProfile.ReadSafely(system,observedAxis?OuterGamepadCompatibilityProfile.ObservedAxis0Name:buttonOnly?OuterGamepadCompatibilityProfile.ButtonOnlyWakeName:OuterGamepadCompatibilityProfile.WakeName);if(capture!=null) capture.Capture(sample.IdentityDiagnostics,sample.Available,false,system);GC.KeepAlive(table);return sample;}
        finally {Marshal.DestroyStructure(pointer,typeof(IVRSystem));Marshal.FreeHGlobal(pointer);}
    }
    public static void Run(Action<bool,string> check)
    {
        var r=new Runtime();var s=Read(r);
        check(s.Available && s.A && s.Packet==17 && s.Index==3 && s.Identity=="fixture" && Math.Abs(s.X-0.3)<1e-6 && Math.Abs(s.Y+0.4)<1e-6 && r.StateCalls==1 && r.RoleCalls==2,"Real vendored CVRSystem ABI reader jointly polls fresh state/pose and rechecks the right role without SDK initialization");
        r=new Runtime {Pressed=1UL<<2,Touched=1UL<<7};s=Read(r);
        check(s.Available && !s.A,"Explicit legacy A profile uses SDK bit7 pressed state, not touch or the Index-controller bit2 alias");
        foreach(int kind in new[]{0,1,3,99}) {r=new Runtime {AxisKind=kind};s=Read(r);check(!s.Available && r.StateCalls==0,"Nonjoystick axis metadata does not become a guessed thumbstick: "+kind);}
        r=new Runtime {PropertyError=true};s=Read(r);check(!s.Available && s.UnavailableReason=="axis0_metadata_unavailable" && r.StateCalls==0,"Property errors refuse input before state polling");
        r=new Runtime {StringLength=258};s=Read(r);check(!s.Available && r.StateCalls==0,"Oversized serial identity is not truncated into a valid controller identity");
        r=new Runtime {Serial=OuterGamepadCompatibilityProfile.ButtonOnlySerial,Model=OuterGamepadCompatibilityProfile.ButtonOnlyModel,PropertyError=true,X=float.NaN,Y=float.PositiveInfinity};s=Read(r,true);
        check(s.Available && s.A && !s.AxesAvailable && s.X==0 && s.Y==0 && r.AxisCalls==0 && r.StateCalls==1 && r.RoleCalls==2 && r.StringCalls==4,"Restricted received button profile validates exact identity/pose and ignores all unknown or nonfinite axes without querying axis properties");
        r=new Runtime {Serial=OuterGamepadCompatibilityProfile.ButtonOnlySerial,Model=OuterGamepadCompatibilityProfile.ButtonOnlyModel,Pressed=0,Touched=128};s=Read(r,true);
        check(s.Available && !s.A && s.X==0 && s.Y==0,"Observed PC touched128 without pressed128 never substitutes for A in the button-only profile");
        UnityEngine.InputSystem.InputSystem.Reset();
        var diagnostic=new OpenVrGamepadDiagnostic(delegate {return new PublicOwnedGamepad(Assembly.GetExecutingAssembly(),false,OuterGamepadCompatibilityProfile.LayoutPrefix,OuterGamepadCompatibilityProfile.ButtonFor(OuterGamepadCompatibilityProfile.ButtonOnlyWakeName));},5,0.2,"refuse",1);
        r=new Runtime {Serial=OuterGamepadCompatibilityProfile.ButtonOnlySerial,Model=OuterGamepadCompatibilityProfile.ButtonOnlyModel,PropertyError=true,Pressed=0,X=float.NaN,Y=float.PositiveInfinity};
        diagnostic.Tick(true,true,1,Read(r,true),0);r.Pressed=128;diagnostic.Tick(true,true,1,Read(r,true),0.1);UnityEngine.InputSystem.InputSystem.NormalUpdate();
        var owned=UnityEngine.InputSystem.Gamepad.current;
        check(owned.buttonWest.isPressed && !owned.buttonSouth.isPressed && owned.leftStick.Value.x==0 && owned.leftStick.Value.y==0 && r.AxisCalls==0,"Restricted actual SDK read path forwards observed pressed128 to West only and cannot deliver unknown-axis movement");
        r.Pressed=0;diagnostic.Tick(true,true,1,Read(r,true),0.2);UnityEngine.InputSystem.InputSystem.NormalUpdate();
        check(!owned.buttonWest.isPressed && diagnostic.Queues==2,"Button-only observed A release is queued once through normal engine input without invented axis output");diagnostic.Cancel("test_end",0.3);
        foreach(string fault in new[]{"serial","model","model_spacing","property_error","model_error","oversized","invalid_pose","disconnected","sdk_false","role_changed","identity_changed","nonfinite_pose"})
        {
            r=new Runtime {Serial=OuterGamepadCompatibilityProfile.ButtonOnlySerial,Model=OuterGamepadCompatibilityProfile.ButtonOnlyModel};
            if(fault=="serial") r.Serial="physical-controller";if(fault=="model") r.Model="other";if(fault=="model_spacing") r.Model="DeckardMP";
            if(fault=="property_error") r.StringError=true;if(fault=="model_error") r.ModelError=true;if(fault=="oversized") r.StringLength=258;
            if(fault=="invalid_pose") r.PoseValid=false;if(fault=="disconnected") r.Connected=false;if(fault=="sdk_false") r.StateSuccess=false;
            if(fault=="role_changed") r.RoleChange=true;if(fault=="identity_changed") r.IdentityChange=true;if(fault=="nonfinite_pose") r.NonfinitePose=true;
            s=Read(r,true);check(!s.Available && r.AxisCalls==0,"Button-only identity/role/pose failure cannot authorize device creation or guessed axes: "+fault);
        }
        r=new Runtime {Serial=OuterGamepadCompatibilityProfile.ButtonOnlySerial,Model=OuterGamepadCompatibilityProfile.ButtonOnlyModel,PropertyError=true,X=0.3464567f,Y=0.5984252f};s=Read(r,false,true);
        check(s.Available && s.A && s.AxesAvailable && s.AxesSource=="observed_transport_axis0" && s.Axis0Metadata=="TrackedProp_UnknownProperty" && !s.Axis0Type.HasValue && s.X==(double)0.3464567f && s.Y==(double)0.5984252f && r.AxisCalls==1,"Explicit observed transport preserves received quantized raw axis0 with UnknownProperty diagnostics without claiming a joystick declaration");
        r=new Runtime {Serial=OuterGamepadCompatibilityProfile.ButtonOnlySerial,Model=OuterGamepadCompatibilityProfile.ButtonOnlyModel,PropertyError=true,AxisKind=3};s=Read(r,false,true);
        check(s.Available && !s.Axis0Type.HasValue,"A returned integer accompanying UnknownProperty is not treated as a successful type declaration");
        r=new Runtime {Serial=OuterGamepadCompatibilityProfile.ButtonOnlySerial,Model=OuterGamepadCompatibilityProfile.ButtonOnlyModel,AxisKind=2};s=Read(r,false,true);
        check(s.Available && s.Axis0Type==2 && s.Axis0Metadata=="TrackedProp_Success" && s.AxesSource=="observed_transport_axis0","A compatible known type retains the explicitly selected transport label and raw metadata");
        foreach(int kind in new[]{0,1,3,99})
        {
            r=new Runtime {Serial=OuterGamepadCompatibilityProfile.ButtonOnlySerial,Model=OuterGamepadCompatibilityProfile.ButtonOnlyModel,AxisKind=kind};s=Read(r,false,true);
            check(!s.Available && s.UnavailableReason=="observed_transport_axis0_metadata_conflict" && s.Axis0Type==kind && r.StateCalls==0,"The observed transport cannot override a known conflicting axis type: "+kind);
        }
        foreach(var error in new[]{ETrackedPropertyError.TrackedProp_WrongDataType,ETrackedPropertyError.TrackedProp_WrongDeviceClass,ETrackedPropertyError.TrackedProp_BufferTooSmall})
        {
            r=new Runtime {Serial=OuterGamepadCompatibilityProfile.ButtonOnlySerial,Model=OuterGamepadCompatibilityProfile.ButtonOnlyModel,AxisError=error};s=Read(r,false,true);
            check(!s.Available && s.UnavailableReason=="observed_transport_axis0_metadata_fault" && s.Axis0Metadata==error.ToString() && !s.Axis0Type.HasValue && r.StateCalls==0,"A property failure other than observed UnknownProperty cannot authorize transport axes: "+error);
        }
        foreach(float value in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity,1.0001f,-1.0001f})
        {
            r=new Runtime {Serial=OuterGamepadCompatibilityProfile.ButtonOnlySerial,Model=OuterGamepadCompatibilityProfile.ButtonOnlyModel,PropertyError=true,X=value};s=Read(r,false,true);
            check(!s.Available && !s.AxesAvailable && s.UnavailableReason=="observed_transport_axis0_invalid","Nonfinite or out-of-range raw axis X cannot reach the game: "+value);
            r=new Runtime {Serial=OuterGamepadCompatibilityProfile.ButtonOnlySerial,Model=OuterGamepadCompatibilityProfile.ButtonOnlyModel,PropertyError=true,Y=value};s=Read(r,false,true);
            check(!s.Available && !s.AxesAvailable && s.UnavailableReason=="observed_transport_axis0_invalid","Nonfinite or out-of-range raw axis Y cannot reach the game: "+value);
        }
        foreach(string fault in new[]{"serial","model","identity_changed","model_changed","invalid_pose","disconnected","sdk_false","role_changed","nonfinite_pose"})
        {
            r=new Runtime {Serial=OuterGamepadCompatibilityProfile.ButtonOnlySerial,Model=OuterGamepadCompatibilityProfile.ButtonOnlyModel,PropertyError=true};
            if(fault=="serial") r.Serial="different";if(fault=="model") r.Model="DeckardMP";if(fault=="identity_changed") r.IdentityChange=true;if(fault=="model_changed") r.ModelChange=true;
            if(fault=="invalid_pose") r.PoseValid=false;if(fault=="disconnected") r.Connected=false;if(fault=="sdk_false") r.StateSuccess=false;if(fault=="role_changed") r.RoleChange=true;if(fault=="nonfinite_pose") r.NonfinitePose=true;
            s=Read(r,false,true);check(!s.Available && !s.AxesAvailable,"Observed raw axis evidence never bypasses identity, role or valid pose guards: "+fault);
        }
        UnityEngine.InputSystem.InputSystem.Reset();
        diagnostic=new OpenVrGamepadDiagnostic(delegate {return new PublicOwnedGamepad(Assembly.GetExecutingAssembly(),false,OuterGamepadCompatibilityProfile.LayoutPrefix,OuterGamepadCompatibilityProfile.ButtonFor(OuterGamepadCompatibilityProfile.ObservedAxis0Name));},5,0.2,"refuse",1);
        r=new Runtime {Serial=OuterGamepadCompatibilityProfile.ButtonOnlySerial,Model=OuterGamepadCompatibilityProfile.ButtonOnlyModel,PropertyError=true,Pressed=0,X=0.3464567f,Y=0.5984252f};
        diagnostic.Tick(true,true,1,Read(r,false,true),0);
        check(UnityEngine.InputSystem.InputSystem.Adds==0 && diagnostic.Reason=="right_controller_not_neutral","An observed transported nonneutral axis cannot arm the first owned window");
        r.X=r.Y=0;diagnostic.Tick(true,true,1,Read(r,false,true),0.1);r.Pressed=128;r.X=0.3464567f;r.Y=0.5984252f;diagnostic.Tick(true,true,1,Read(r,false,true),0.2);UnityEngine.InputSystem.InputSystem.NormalUpdate();
        owned=UnityEngine.InputSystem.Gamepad.current;
        // Independently calculated expected vector from the received PC values and configured .2 radial deadzone.
        check(owned.buttonWest.isPressed && !owned.buttonSouth.isPressed && Math.Abs(owned.leftStick.Value.x-0.307811806)<1e-6 && Math.Abs(owned.leftStick.Value.y-0.531674929)<1e-6,"Quantized PC axis0 passes the actual public queue with the configured radial result, without replacing it with requested headset values");
        r.Pressed=0;r.X=r.Y=0;diagnostic.Tick(true,true,1,Read(r,false,true),0.3);UnityEngine.InputSystem.InputSystem.NormalUpdate();
        check(!owned.buttonWest.isPressed && owned.leftStick.Value.x==0 && owned.leftStick.Value.y==0 && diagnostic.Queues==2,"Observed transport zero releases the owned West and movement state once");
        r.PropertyError=false;r.AxisKind=1;diagnostic.Tick(true,true,1,Read(r,false,true),0.4);UnityEngine.InputSystem.InputSystem.NormalUpdate();diagnostic.Tick(true,true,1,Read(r,false,true),0.5);
        check(diagnostic.Complete && diagnostic.ReleaseObserved && !owned.added && UnityEngine.InputSystem.InputSystem.Removes==1,"A newly conflicting metadata declaration ends and cleans up the started transport window");
        r=new Runtime {Serial=OuterGamepadCompatibilityProfile.ButtonOnlySerial,Model=OuterGamepadCompatibilityProfile.ButtonOnlyModel,MissingStringReader=true};s=Read(r,true);
        check(!s.Available && s.UnavailableReason=="reader_exception:System.NullReferenceException" && r.StateCalls==0,"The actual shipping safe-read boundary categorizes a missing SDK callback instead of silently returning an unexplained invalid sample");
        foreach(string fault in new[]{"invalid_pose","disconnected","sdk_false","role_changed","nonfinite_pose"})
        {
            r=new Runtime {Serial=OuterGamepadCompatibilityProfile.ButtonOnlySerial,Model=OuterGamepadCompatibilityProfile.ButtonOnlyModel};
            if(fault=="invalid_pose") r.PoseValid=false;if(fault=="disconnected") r.Connected=false;if(fault=="sdk_false") r.StateSuccess=false;if(fault=="role_changed") r.RoleChange=true;if(fault=="nonfinite_pose") r.NonfinitePose=true;
            s=Read(r,true);string expected=fault=="invalid_pose"?"controller_pose_invalid":fault=="disconnected"?"controller_disconnected":fault=="sdk_false"?"controller_state_read_failed":fault=="role_changed"?"right_role_changed":"controller_pose_nonfinite";
            check(!s.Available && s.UnavailableReason==expected,"Shipping reader distinguishes the exact guarded source failure: "+fault);
        }
        r=new Runtime {Serial=OuterGamepadCompatibilityProfile.ButtonOnlySerial,Model=OuterGamepadCompatibilityProfile.ButtonOnlyModel};s=Read(r,false,true);
        var snapshot=s.IdentityDiagnostics;
        check(s.Available && snapshot.Index==3 && snapshot.DeviceClass==ETrackedDeviceClass.Controller && snapshot.Role==ETrackedControllerRole.RightHand && snapshot.PostIndex==3 && snapshot.PostClass==ETrackedDeviceClass.Controller && snapshot.PostRole==ETrackedControllerRole.RightHand && snapshot.Stage=="complete" && snapshot.Verdict=="accepted","In-process identity diagnostics retain the selected and post-state public class/role/index without changing admission pins");
        check(snapshot.InitialSerial.RawValue==r.Serial && snapshot.InitialModel.RawValue==r.Model && snapshot.PostSerial.RawValue==r.Serial && snapshot.PostModel.RawValue==r.Model && snapshot.InitialSerial.RequiredBytes==8 && snapshot.InitialSerial.InitialCapacity==257 && snapshot.InitialSerial.ReturnedCapacity>=snapshot.InitialSerial.Length && snapshot.InitialSerial.Length==r.Serial.Length && snapshot.InitialSerial.Error==ETrackedPropertyError.TrackedProp_Success && snapshot.InitialSerial.Returned,"SDK text/count/error/builder observations are captured before validation at both identity stages");
        foreach(string fault in new[]{"empty_serial","empty_model","zero_count_nonempty_text","error_with_text","oversized_count","different_serial","different_model","post_serial","post_model","exception"})
        {
            r=new Runtime {Serial=OuterGamepadCompatibilityProfile.ButtonOnlySerial,Model=OuterGamepadCompatibilityProfile.ButtonOnlyModel};
            if(fault=="empty_serial") r.Serial="";if(fault=="empty_model") r.Model="";if(fault=="zero_count_nonempty_text") r.StringLength=0;if(fault=="error_with_text") r.StringError=true;if(fault=="oversized_count") r.StringLength=258;
            if(fault=="different_serial") r.Serial="other-private-device";if(fault=="different_model") r.Model="DeckardMP";if(fault=="post_serial") r.IdentityChange=true;if(fault=="post_model") r.ModelChange=true;if(fault=="exception") r.MissingStringReader=true;
            s=Read(r,false,true);snapshot=s.IdentityDiagnostics;
            check(!s.Available && snapshot!=null && snapshot.InitialSerial!=null && snapshot.InitialModel!=null,"Each rejected identity result remains observable with both initial properties captured: "+fault);
            if(fault=="empty_serial") check(snapshot.InitialSerial.RawValue=="" && snapshot.InitialSerial.Length==0 && snapshot.InitialSerial.Verdict=="empty_builder","An empty builder is distinct from a mismatching nonempty identity");
            if(fault=="empty_model") check(snapshot.InitialModel.Verdict=="empty_builder" && snapshot.InitialSerial.RawValue==OuterGamepadCompatibilityProfile.ButtonOnlySerial,"Empty model does not erase the independent serial result");
            if(fault=="zero_count_nonempty_text") check(snapshot.InitialSerial.RawValue==r.Serial && snapshot.InitialSerial.RequiredBytes==0 && snapshot.InitialSerial.Length==r.Serial.Length && snapshot.InitialSerial.Verdict=="zero_required_bytes" && r.AxisCalls==0 && r.StateCalls==0,"Needed-zero with valid-looking text is preserved distinctly and never relaxes the primary guard");
            if(fault=="error_with_text") check(snapshot.InitialSerial.RawValue==r.Serial && snapshot.InitialSerial.Error==ETrackedPropertyError.TrackedProp_UnknownProperty && snapshot.InitialSerial.Verdict=="property_error","Raw text accompanying an SDK property error remains private evidence rather than an accepted identity");
            if(fault=="oversized_count") check(snapshot.InitialSerial.RequiredBytes==258 && snapshot.InitialSerial.Verdict=="required_bytes_exceed_buffer" && snapshot.InitialSerial.RawValue==r.Serial,"Required-byte overflow is retained without truncating it into accepted identity");
            if(fault=="different_serial" || fault=="different_model") check(snapshot.Stage=="initial_identity_validation" && snapshot.Verdict=="identity_disagreement" && r.AxisCalls==0 && r.StateCalls==0,"An accepted property result with different identity is distinguished from SDK/buffer rejection: "+fault);
            if(fault=="post_serial" || fault=="post_model") check(snapshot.Stage=="post_identity_validation" && snapshot.Verdict=="identity_disagreement" && snapshot.PostSerial!=null && snapshot.PostModel!=null && r.StringCalls==4,"Both post-state properties remain captured even when the first one disagrees: "+fault);
            if(fault=="exception") check(!snapshot.InitialSerial.Returned && !snapshot.InitialSerial.RequiredBytes.HasValue && snapshot.InitialSerial.ExceptionType=="System.NullReferenceException" && snapshot.InitialSerial.Verdict=="reader_exception","A thrown SDK wrapper has no fabricated byte-count result and retains its typed exception");
        }
        UnityEngine.InputSystem.InputSystem.Reset();
        diagnostic=new OpenVrGamepadDiagnostic(delegate {return new PublicOwnedGamepad(Assembly.GetExecutingAssembly(),false,OuterGamepadCompatibilityProfile.LayoutPrefix,OwnedGamepadButton.West);},5,0.2,"refuse",1);
        r=new Runtime {Serial=OuterGamepadCompatibilityProfile.ButtonOnlySerial,Model=OuterGamepadCompatibilityProfile.ButtonOnlyModel,Pressed=0};diagnostic.Tick(true,true,1,Read(r,true),0);
        r.IdentityChange=true;s=Read(r,true);var endingSnapshot=s.IdentityDiagnostics;diagnostic.Tick(true,true,1,s,0.1);UnityEngine.InputSystem.InputSystem.NormalUpdate();
        r.IdentityChange=false;diagnostic.Tick(true,true,1,Read(r,true),0.2);diagnostic.Tick(true,true,1,Read(r,true),0.3);
        check(diagnostic.Complete && Object.ReferenceEquals(diagnostic.LastIdentitySnapshot,endingSnapshot) && endingSnapshot.InitialSerial.RawValue=="replacement" && diagnostic.NativeUnavailableReason=="button_only_controller_identity_mismatch","The terminating private identity snapshot survives release, Complete and later valid reads without changing rejection");
        string privateDirectory=Path.Combine(Path.GetFullPath(".local-tools/identity-checks"),Guid.NewGuid().ToString("N"));var capture=new ControllerIdentityCapture(privateDirectory);
        for(int i=0;i<100;++i) capture.Capture(new ControllerIdentitySnapshot {Index=uint.MaxValue,Verdict="index_out_of_range"},false,false);
        check(capture.Attempts==0 && !Directory.Exists(privateDirectory),"Disconnected polls without an attempted identity property cannot spend private capture records before the first real mismatch");
        for(int i=0;i<100;++i) capture.Capture(endingSnapshot,false,false);
        check(capture.Attempts==7 && Directory.GetFiles(privateDirectory).Length==7,"Repeated diagnostic polling cannot produce unbounded private files or consume the successful-read reservation");
        capture.Capture(endingSnapshot,true,false);capture.Capture(endingSnapshot,true,false);capture.Capture(endingSnapshot,false,true);capture.Capture(endingSnapshot,false,true);
        check(capture.Attempts==9 && Directory.GetFiles(privateDirectory).Length==9 && capture.FirstSuccessAttempted && capture.TerminalAttempted && capture.LastError=="","Eight private records include a reserved first success, with exactly one additional reserved terminal snapshot");
        string terminalPath=Path.Combine(privateDirectory,"input-identity-terminal.yaml"),terminalText=File.ReadAllText(terminalPath);
        var collisionCapture=new ControllerIdentityCapture(privateDirectory);collisionCapture.Capture(endingSnapshot,false,true);
        check(collisionCapture.LastError=="System.IO.IOException" && File.ReadAllText(terminalPath)==terminalText,"Private capture uses CreateNew and preserves an existing terminal evidence file on collision");
        var escaped=new ControllerIdentitySnapshot {InitialSerial=new StringPropertyObservation {RawValue="private\nidentity\0value",Returned=true,RequiredBytes=23,Length=22,InitialCapacity=257,ReturnedCapacity=257}};
        string escapedText=escaped.ToPrivateYaml();
        check(escapedText.Contains("private\\u000aidentity\\u0000value") && escapedText.Contains("private_device_identity: true") && !escapedText.Contains("private\nidentity"),"Private raw identity text is explicitly marked and escaped without injecting YAML/log structure");
        r=new Runtime {Serial="app-view-controller",Model="app-view-model"};s=Read(r,false,true);
        check(!s.Available && s.IdentityDiagnostics.IdentityRejected && s.IdentityDiagnostics.RejectedState==null && r.StateCalls==0 && r.AxisCalls==0 && r.StringCalls==2,"Identity rejection with private capture disabled performs zero passive state or axis-type reads");
        string passiveDirectory=Path.Combine(Path.GetFullPath(".local-tools/identity-checks"),Guid.NewGuid().ToString("N"));capture=new ControllerIdentityCapture(passiveDirectory);
        r=new Runtime {Serial="app-view-controller",Model="app-view-model",PoseX=0.27f,PoseY=1.23f,PoseZ=-0.41f,Pressed=128,Touched=256,X=0.35f,Y=0.6f,AxisKinds=new[]{2,3,0,1,99},AxisPropertyErrors=new[]{ETrackedPropertyError.TrackedProp_Success,ETrackedPropertyError.TrackedProp_Success,ETrackedPropertyError.TrackedProp_UnknownProperty,ETrackedPropertyError.TrackedProp_WrongDataType,ETrackedPropertyError.TrackedProp_Success}};
        for(int i=0;i<4;++i) r.ExtraAxes[i]=new VRControllerAxis_t {x=i+0.1f,y=-i-0.2f};
        s=Read(r,false,true,capture);var passive=s.IdentityDiagnostics.RejectedState;
        check(!s.Available && s.UnavailableReason=="observed_transport_controller_identity_mismatch" && s.X==0 && s.Y==0 && !s.A && s.IdentityDiagnostics.Verdict=="identity_disagreement" && passive.StateResult==true && passive.Connected==true && passive.PoseValid==true,"Opt-in passive SDK output never becomes accepted input or changes the original identity rejection");
        check(passive.Matrix.Length==12 && passive.MatrixFinite==true && Math.Abs(passive.Matrix[3]-0.27)<1e-6 && Math.Abs(passive.Matrix[7]-1.23)<1e-6 && Math.Abs(passive.Matrix[11]+0.41)<1e-6 && passive.Packet==17 && passive.Pressed==128 && passive.Touched==256 && passive.Axes.Length==10 && Math.Abs(passive.Axes[0]-0.35)<1e-6 && Math.Abs(passive.Axes[9]+3.2)<1e-6,"A rejected controller's entire matrix, packet, masks and all five raw XY axes remain independently inspectable");
        check(r.StateCalls==1 && r.AxisCalls==5 && r.StringCalls==4 && passive.PostRightIndex==3 && passive.PostClass==ETrackedDeviceClass.Controller && passive.PostRole==ETrackedControllerRole.RightHand && passive.PostSerial.RawValue==r.Serial && passive.PostModel.RawValue==r.Model,"One captured rejection performs one joint read, five metadata reads and complete post-state public context/identity rechecks");
        check(passive.AxisTypes[0]==2 && passive.AxisTypes[1]==3 && !passive.AxisTypes[2].HasValue && !passive.AxisTypes[3].HasValue && passive.AxisTypes[4]==99 && passive.AxisRawReturns[2]==0 && passive.AxisErrors[2]==ETrackedPropertyError.TrackedProp_UnknownProperty,"Passive metadata preserves all raw returns/errors while successful unknown enum integers remain raw and failed properties never imply a type");
        string passiveText=File.ReadAllText(Path.Combine(passiveDirectory,"input-identity-1.yaml"));
        check(passiveText.Contains("diagnostic_only: true") && passiveText.Contains("authorizes_input: false") && passiveText.Contains("raw_outputs_valid: true") && passiveText.Contains("raw_axes_0_to_4_xy:") && passiveText.Contains("passive_axis_type_properties:"),"Private passive evidence explicitly states its nonauthorizing role and contains the complete raw SDK fields");
        for(int i=0;i<20;++i) s=Read(r,false,true,capture);
        check(capture.Attempts==7 && r.StateCalls==7 && r.AxisCalls==35 && r.StringCalls==56,"Exhausted capture slots cannot continue passive API reads despite continued rejected polling");
        capture=new ControllerIdentityCapture(Path.Combine(Path.GetFullPath(".local-tools/identity-checks"),Guid.NewGuid().ToString("N")));
        r=new Runtime {Serial="app-view-controller",Model="app-view-model",Pressed=ulong.MaxValue,Touched=1UL<<63};s=Read(r,false,true,capture);
        check(s.IdentityDiagnostics.ToPrivateYaml().Contains("pressed: 18446744073709551615") && s.IdentityDiagnostics.ToPrivateYaml().Contains("touched: 9223372036854775808"),"Passive button masks preserve all unsigned64 bits without overflow or lossy signed conversion");
        foreach(string fault in new[]{"sdk_false","invalid_pose","disconnected","nonfinite","index_changed","identity_changed","state_exception","axis_exception"})
        {
            capture=new ControllerIdentityCapture(Path.Combine(Path.GetFullPath(".local-tools/identity-checks"),Guid.NewGuid().ToString("N")));
            r=new Runtime {Serial="app-view-controller",Model="app-view-model"};
            if(fault=="sdk_false") r.StateSuccess=false;if(fault=="invalid_pose") r.PoseValid=false;if(fault=="disconnected") r.Connected=false;if(fault=="nonfinite") r.NonfinitePose=true;
            if(fault=="index_changed") r.RoleChange=true;if(fault=="identity_changed") r.IdentityChange=true;if(fault=="state_exception") r.MissingStateReader=true;if(fault=="axis_exception") r.MissingAxisReader=true;
            s=Read(r,false,true,capture);passive=s.IdentityDiagnostics.RejectedState;
            check(!s.Available && s.IdentityDiagnostics.IdentityRejected && passive!=null && passive.PostSerial!=null && passive.PostModel!=null,"Passive failures or context changes preserve original rejection and independent post-state results: "+fault);
            if(fault=="sdk_false") check(passive.StateResult==false && s.IdentityDiagnostics.ToPrivateYaml().Contains("raw_outputs_valid: false"),"SDK false is recorded honestly and raw outputs are never marked valid");
            if(fault=="invalid_pose") check(passive.PoseValid==false,"Passive invalid-pose flag remains invalid");if(fault=="disconnected") check(passive.Connected==false,"Passive disconnected flag is not promoted");
            if(fault=="nonfinite") check(passive.MatrixFinite==false && s.IdentityDiagnostics.ToPrivateYaml().Contains("\"NaN\""),"Nonfinite passive matrix elements remain visible and are serialized without replacing them by zero");
            if(fault=="index_changed") check(passive.PostRightIndex==4 && passive.QueriedIndex==3,"Post-role index change is distinguished from the index actually queried for state");
            if(fault=="identity_changed") check(passive.PostSerial.RawValue=="replacement" && s.IdentityDiagnostics.InitialSerial.RawValue=="app-view-controller","Passive post-state identity cannot erase the original mismatch evidence");
            if(fault=="state_exception") check(!passive.StateResult.HasValue && passive.StateException=="System.NullReferenceException" && passive.Matrix==null && passive.Packet==null,"A passive SDK wrapper exception has no fabricated successful state or pose payload");
            if(fault=="axis_exception") check(!passive.AxisRawReturns[0].HasValue && passive.AxisExceptions[0]=="System.NullReferenceException" && passive.AxisTypes[0]==null,"Passive axis exceptions are recorded separately from metadata returns and types");
        }
        foreach(string reason in new[]{"invalid_pose","disconnected","sdk_false","role_changed"})
        {
            r=new Runtime();if(reason=="invalid_pose") r.PoseValid=false;if(reason=="disconnected") r.Connected=false;if(reason=="sdk_false") r.StateSuccess=false;if(reason=="role_changed") r.RoleChange=true;
            s=Read(r);check(!s.Available && r.StateCalls==1,"Joint state/pose or hand-identity failure rejects the sample: "+reason);
        }
    }
}
