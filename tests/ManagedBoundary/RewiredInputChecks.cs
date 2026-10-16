using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Ugevr;
using Valve.VR;

namespace UnityEngine
{
    public class Behaviour : Component {public bool isActiveAndEnabled=true;}
    public static class Application {public static bool isFocused=true;}
}
static class RewiredInputChecks
{
    sealed class Backend : IOwnedRewiredInput
    {
        public bool Added,Preserve,ThrowAdd,ThrowRemove,ThrowSet;
        public int Adds,Removes,Sets;
        public double X,Y,Trigger,Expires;
        public string UnavailableReason {get;set;}
        public string FaultReason {get;set;}
        public string CleanupStatus {get;private set;}
        public int AppliedUpdates {get;set;}
        public bool NeedsCleanup {get {return Added;}}
        public void Add() {++Adds;Added=true;if(ThrowAdd) throw new InvalidOperationException("partial creation");}
        public void SetDesired(double x,double y,double t,double expires) {++Sets;X=x;Y=y;Trigger=t;Expires=expires;if(ThrowSet) throw new InvalidOperationException("feed failed");}
        public void Remove() {++Removes;if(ThrowRemove) throw new InvalidOperationException("remove failed");if(!Preserve) Added=false;CleanupStatus=Preserve?"game_additions_preserved":"removed";}
    }
    class Shape
    {
        public void Axis(int index,float value) {}
        private void Hidden(int index,float value) {}
        public int Wrong(int index,float value) {return 1;}
        public static void Static(int index,float value) {}
    }
    sealed class Runtime
    {
        public int[] Kinds={2,3,0,0,0};
        public int MetadataCalls,StateCalls,RoleCalls,IdentityCalls;
        public bool Valid=true,Connected=true,StateResult=true,RoleChange,MetadataError,RequireJointPoll,InitialRoleMissing,WrongClass,IdentityError,EmptyIdentity,NonfinitePose;
        public readonly System.Collections.Generic.List<string> Calls=new System.Collections.Generic.List<string>();
        public float Trigger=.7f,X=.3f,Y=-.4f;
        public IVRSystem Table()
        {
            var t=new IVRSystem();
            t.GetTrackedDeviceIndexForControllerRole=delegate(ETrackedControllerRole role){++RoleCalls;return InitialRoleMissing?OpenVR.k_unMaxTrackedDeviceCount:RoleChange && RoleCalls>1?4u:3u;};
            t.GetControllerRoleForTrackedDeviceIndex=delegate(uint index){return ETrackedControllerRole.RightHand;};
            t.GetTrackedDeviceClass=delegate(uint index){return WrongClass?ETrackedDeviceClass.HMD:ETrackedDeviceClass.Controller;};
            t.GetInt32TrackedDeviceProperty=delegate(uint index,ETrackedDeviceProperty property,ref ETrackedPropertyError error){++MetadataCalls;Calls.Add("metadata");error=MetadataError || RequireJointPoll && StateCalls==0?ETrackedPropertyError.TrackedProp_UnknownProperty:ETrackedPropertyError.TrackedProp_Success;int axis=(int)property-(int)ETrackedDeviceProperty.Prop_Axis0Type_Int32;if(axis<0 || axis>=5) throw new Exception("Unexpected axis property.");return Kinds[axis];};
            t.GetStringTrackedDeviceProperty=delegate(uint index,ETrackedDeviceProperty property,StringBuilder value,uint capacity,ref ETrackedPropertyError error){++IdentityCalls;Calls.Add("identity");error=IdentityError || RequireJointPoll && StateCalls==0?ETrackedPropertyError.TrackedProp_UnknownProperty:ETrackedPropertyError.TrackedProp_Success;if(!EmptyIdentity)value.Append("rewired-fixture");return 16;};
            t.GetControllerStateWithPose=delegate(ETrackingUniverseOrigin origin,uint index,ref VRControllerState_t state,uint size,ref TrackedDevicePose_t pose)
            {++StateCalls;Calls.Add("joint");if(size!=(uint)Marshal.SizeOf(typeof(VRControllerState_t))) throw new Exception("State ABI size.");state=new VRControllerState_t {unPacketNum=11,ulButtonPressed=1UL<<7,rAxis0=new VRControllerAxis_t {x=X,y=Y},rAxis1=new VRControllerAxis_t {x=Trigger},rAxis2=new VRControllerAxis_t {x=Trigger},rAxis3=new VRControllerAxis_t {x=Trigger},rAxis4=new VRControllerAxis_t {x=Trigger}};pose=new TrackedDevicePose_t {bDeviceIsConnected=Connected,bPoseIsValid=Valid,mDeviceToAbsoluteTracking=new HmdMatrix34_t {m0=1,m5=1,m10=NonfinitePose?Single.NaN:1}};return StateResult;};return t;
        }
    }
    static RewiredRightSample Read(Runtime runtime)
    {
        var table=runtime.Table();IntPtr pointer=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(IVRSystem)));
        try {Marshal.StructureToPtr(table,pointer,false);var sample=OpenVrRewiredRightReader.Read(new CVRSystem(pointer));GC.KeepAlive(table);return sample;}
        finally {Marshal.DestroyStructure(pointer,typeof(IVRSystem));Marshal.FreeHGlobal(pointer);}
    }
    static RewiredRightSample Neutral() {return new RewiredRightSample {Available=true,Identity="right-controller",Index=3,Packet=1,TriggerAxis=1};}
    public static void Run(Action<bool,string> check)
    {
        int factories=0;var backend=new Backend();var diagnostic=new RewiredInputDiagnostic(delegate {++factories;return backend;},15,.2,"refuse");
        diagnostic.Tick(false,true,1,Neutral(),0);diagnostic.Tick(true,false,1,Neutral(),1);var held=Neutral();held.Trigger=.9;diagnostic.Tick(true,true,1,held,2);
        check(factories==0 && backend.Adds==0,"Disabled, unfocused or nonneutral Rewired admission performs no public backend creation");
        foreach(double dz in new[]{.1,.2,.8})
        {
            var neutralBackend=new Backend();var neutralDiagnostic=new RewiredInputDiagnostic(()=>neutralBackend,15,dz,"refuse");var outside=Neutral();outside.X=outside.Y=dz*.95;neutralDiagnostic.Tick(true,true,1,outside,0);
            check(neutralBackend.Adds==0,"A diagonal outside the configured radial deadzone cannot arm: "+dz);
            outside.X=dz;outside.Y=0;outside.Trigger=Double.Epsilon;neutralDiagnostic.Tick(true,true,1,outside,.1);
            check(neutralBackend.Adds==0,"Even the smallest nonzero raw trigger cannot arm: "+dz);
            outside.Trigger=0;neutralDiagnostic.Tick(true,true,1,outside,.2);neutralDiagnostic.Tick(true,true,1,outside,.3);
            check(neutralBackend.Adds==1 && neutralBackend.X==0 && neutralBackend.Y==0 && neutralBackend.Trigger==0,"Configured radial boundary and exactly released trigger admit only zero output: "+dz);
        }
        diagnostic.Tick(true,true,1,Neutral(),3);check(backend.Adds==1 && backend.X==0 && backend.Trigger==0 && diagnostic.Status=="active","Fresh neutral source admits exactly one owned Rewired controller");
        var moving=Neutral();moving.Packet=2;moving.X=.6;moving.Y=-.4;moving.Trigger=.8;diagnostic.Tick(true,true,1,moving,3.1);
        check(backend.X>0 && backend.Y<0 && backend.Trigger==.8 && Math.Abs(backend.Expires-3.6)<1e-8,"Rewired desired state uses both look axes and analog trigger with bounded source freshness");
        diagnostic.Tick(true,true,1,moving,18);check(diagnostic.Complete && backend.Removes==1 && backend.X==0 && backend.Trigger==0 && !backend.NeedsCleanup,"Bounded window releases only the owned backend before removal");
        diagnostic.Cancel("again",19);check(backend.Removes==1,"Completed Rewired window does not repeat teardown");
        foreach(string reason in new[]{"generation","identity","axis","packet","focus","missing","clock"})
        {
            backend=new Backend();diagnostic=new RewiredInputDiagnostic(()=>backend,15,.2,"refuse");diagnostic.Tick(true,true,1,Neutral(),1);var sample=Neutral();ulong generation=1;bool focus=true;double now=2;
            if(reason=="generation") generation=2;if(reason=="identity") sample.Identity="other";if(reason=="axis") sample.TriggerAxis=2;if(reason=="packet") sample.Packet=0;if(reason=="focus") focus=false;if(reason=="missing") sample.Available=false;if(reason=="clock") now=.5;
            diagnostic.Tick(true,focus,generation,sample,now);check(diagnostic.Complete && backend.Removes==1 && backend.Trigger==0,"Source/session change releases the owned Rewired input: "+reason);
        }
        backend=new Backend {ThrowAdd=true};diagnostic=new RewiredInputDiagnostic(()=>backend,15,.2,"refuse");diagnostic.Tick(true,true,1,Neutral(),0);check(diagnostic.Complete && backend.Removes==1 && !backend.Added,"Partial public creation failure still attempts exact owned cleanup");
        backend=new Backend {ThrowSet=true};diagnostic=new RewiredInputDiagnostic(()=>backend,15,.2,"refuse");diagnostic.Tick(true,true,1,Neutral(),0);check(diagnostic.Complete && backend.Removes==1,"Throwing public feed still cancels the diagnostic and attempts owned removal");
        backend=new Backend {Preserve=true};diagnostic=new RewiredInputDiagnostic(()=>backend,15,.2,"refuse");diagnostic.Tick(true,true,1,Neutral(),0);diagnostic.Cancel("stop",1);
        check(diagnostic.Status=="cleanup_unconfirmed" && backend.Removes==2 && backend.Added && backend.Trigger==0,"Game additions are preserved and bounded cleanup attempts do not become a false success");
        backend=new Backend {ThrowRemove=true};diagnostic=new RewiredInputDiagnostic(()=>backend,15,.2,"refuse");diagnostic.Tick(true,true,1,Neutral(),0);diagnostic.Cancel("stop",1);check(diagnostic.Status=="cleanup_unconfirmed" && backend.Removes==2,"Public removal failure remains unconfirmed after two bounded attempts");
        foreach(double duration in new[]{Double.NaN,Double.PositiveInfinity,4,301}) {bool failed=false;try {new RewiredInputDiagnostic(()=>new Backend(),duration,.2,"refuse");} catch(ArgumentException){failed=true;}check(failed,"Invalid Rewired duration is refused before any backend creation");}
        bool conflictRefused=false;try {new RewiredInputDiagnostic(()=>new Backend(),15,.2,"prefer-owned");} catch(ArgumentException){conflictRefused=true;}check(conflictRefused,"Prototype refuses an unsupported conflict override");
        check(PublicOwnedRewiredInput.FeedAllowed(true,false,1,0,1.5) && !PublicOwnedRewiredInput.FeedAllowed(true,false,1.5,0,1.5) && !PublicOwnedRewiredInput.FeedAllowed(true,false,.5,1,2) && !PublicOwnedRewiredInput.FeedAllowed(true,false,Double.NaN,0,2) && !PublicOwnedRewiredInput.FeedAllowed(false,false,1,0,2),"Input-source callback freshness independently refuses expiry, clock regression, nonfinite time and lost focus");
        object definition=new object(),map=new object(),device=new object(),entry=new object();var expected=new RewiredOwnershipSnapshot {Nodes=new[]{definition,map,device,entry},Values="exact scalar fingerprint"};
        check(RewiredOwnershipSnapshot.Same(expected,new RewiredOwnershipSnapshot {Nodes=new[]{definition,map,device,entry},Values=expected.Values}),"Owned snapshot comparison accepts the same references and values");
        check(!RewiredOwnershipSnapshot.Same(expected,new RewiredOwnershipSnapshot {Nodes=new[]{definition,map,device,new object()},Values=expected.Values}),"ID reuse or replaced map entries cannot prove object ownership");
        check(!RewiredOwnershipSnapshot.Same(expected,new RewiredOwnershipSnapshot {Nodes=new[]{definition,map,device,entry,new object()},Values=expected.Values}),"New game-owned map entries or assignments prevent destructive cleanup");
        check(!RewiredOwnershipSnapshot.Same(expected,new RewiredOwnershipSnapshot {Nodes=expected.Nodes,Values="game changed map/definition"}),"A changed public mapping fingerprint prevents destructive cleanup");
        Type owner=typeof(Shape);Type[] arguments={typeof(int),typeof(float)};
        check(PublicOwnedRewiredInput.PublicMethodShape(owner.GetMethod("Axis"),owner,"Axis",typeof(void),arguments) && !PublicOwnedRewiredInput.PublicMethodShape(owner.GetMethod("Hidden",BindingFlags.NonPublic|BindingFlags.Instance),owner,"Hidden",typeof(void),arguments) && !PublicOwnedRewiredInput.PublicMethodShape(owner.GetMethod("Static"),owner,"Static",typeof(void),arguments) && !PublicOwnedRewiredInput.PublicMethodShape(owner.GetMethod("Wrong"),owner,"Wrong",typeof(void),arguments),"Public API admission rejects private, static and incorrectly typed mutators");
        Guid id=Guid.NewGuid();check(!PublicOwnedRewiredInput.MatchesIdentity("Rewired_Core",id,"a","Rewired_Core",id,"b") && !PublicOwnedRewiredInput.MatchesIdentity("other",id,"a","Rewired_Core",id,"a") && !PublicOwnedRewiredInput.MatchesIdentity("Rewired_Core",Guid.NewGuid(),"a","Rewired_Core",id,"a"),"A same-named unpinned assembly cannot activate the Rewired profile");
        check(!String.IsNullOrEmpty(PublicOwnedRewiredInput.Create().UnavailableReason),"Shipped Rewired profile refuses missing/unpinned assemblies without target loading");
        PublicLifecycleChecks(check);
        var runtime=new Runtime();var read=Read(runtime);check(read.Available && read.TriggerAxis==1 && Math.Abs(read.Trigger-.7)<1e-6 && runtime.MetadataCalls==5 && runtime.StateCalls==1 && runtime.RoleCalls==2 && runtime.Calls[0]=="joint","Vendored OpenVR ABI polls joint state after initial role/class guards and before bounded axis metadata");
        runtime=new Runtime {RequireJointPoll=true};read=Read(runtime);
        check(read.Available && runtime.StateCalls==1 && runtime.IdentityCalls==1 && String.Join(",",runtime.Calls)=="joint,metadata,metadata,metadata,metadata,metadata,identity","Properties exposed only after a joint poll are validated in that same public SDK context before publication");
        foreach(string reason in new[]{"sdk-false","disconnected","pose-invalid","pose-nonfinite","initial-role","initial-class"})
        {
            runtime=new Runtime {RequireJointPoll=true};if(reason=="sdk-false")runtime.StateResult=false;if(reason=="disconnected")runtime.Connected=false;if(reason=="pose-invalid")runtime.Valid=false;if(reason=="pose-nonfinite")runtime.NonfinitePose=true;if(reason=="initial-role")runtime.InitialRoleMissing=true;if(reason=="initial-class")runtime.WrongClass=true;
            read=Read(runtime);check(!read.Available && runtime.MetadataCalls==0 && runtime.IdentityCalls==0 && runtime.StateCalls==(reason.StartsWith("initial-")?0:1),"Rejected initial context or joint state cannot reach metadata, identity or output: "+reason);
        }
        foreach(string reason in new[]{"metadata-still-unknown","duplicate-trigger","identity-error","identity-empty","post-role-change"})
        {
            runtime=new Runtime {RequireJointPoll=true};if(reason=="metadata-still-unknown")runtime.MetadataError=true;if(reason=="duplicate-trigger")runtime.Kinds[2]=3;if(reason=="identity-error")runtime.IdentityError=true;if(reason=="identity-empty")runtime.EmptyIdentity=true;if(reason=="post-role-change")runtime.RoleChange=true;
            read=Read(runtime);check(!read.Available && runtime.StateCalls==1,"A successful first joint poll never bypasses the remaining source guard: "+reason);
        }
        foreach(int axis in new[]{2,3,4}) {runtime=new Runtime {Kinds=new[]{2,0,0,0,0}};runtime.Kinds[axis]=3;read=Read(runtime);check(read.Available && read.TriggerAxis==axis,"Trigger selection follows public metadata rather than assuming a fixed index");}
        foreach(string reason in new[]{"missing","duplicate","trackpad","metadata","unknown-kind","pose","role","analog"})
        {
            runtime=new Runtime();if(reason=="missing") runtime.Kinds[1]=0;if(reason=="duplicate")runtime.Kinds[2]=3;if(reason=="trackpad")runtime.Kinds[0]=1;if(reason=="metadata")runtime.MetadataError=true;if(reason=="unknown-kind")runtime.Kinds[4]=88;if(reason=="pose")runtime.Valid=false;if(reason=="role")runtime.RoleChange=true;if(reason=="analog")runtime.Trigger=Single.NaN;
            read=Read(runtime);check(!read.Available,"Ambiguous, unsupported or invalid controller state is refused: "+reason);
        }
    }
    static PublicOwnedRewiredInput Adapter(Func<double> clock)
    {return new PublicOwnedRewiredInput(typeof(Rewired.ReInput).Assembly,new Rewired.InputManager_Base(),clock,()=>true,false);}
    static void PublicLifecycleChecks(Action<bool,string> check)
    {
        Rewired.ReInput.Reset();double now=0;var adapter=Adapter(()=>now);adapter.Add();
        var data=Rewired.ReInput.Data;var device=Rewired.ReInput.controllers.CustomControllers[0];
        check(data.Definitions.Count==1 && data.Definitions[0].axisCount==3 && data.Maps.Count==1 && data.Maps[0].categoryId==0 && data.Maps[0].customControllerUid==2 && data.Maps[0].actionElementMaps[2].actionId==36 && data.Maps[0].actionElementMaps[2].elementType==Rewired.ControllerElementType.Axis,"Public reflection backend creates an owned three-axis definition and explicit look/trigger map with evidenced argument order");
        check(Rewired.ReInput.players.SystemPlayer.controllers.Controllers.Contains(device) && Rewired.ReInput.players.SystemPlayer.controllers.maps.Content.Count==1 && Rewired.ReInput.Subscribers==1,"Public reflection backend explicitly assigns only the system player and subscribes one owned callback");
        adapter.SetDesired(.4,-.3,.9,1);now=.1;Rewired.ReInput.Pulse();
        check(Math.Abs(device.Values[0]-.4)<1e-6 && Math.Abs(device.Values[1]+.3)<1e-6 && Math.Abs(device.Values[2]-.9)<1e-6 && adapter.AppliedUpdates==1,"Public input-update event feeds the mapped axis indices rather than invoking a game interaction callback");
        now=1;Rewired.ReInput.Pulse();check(device.Values[0]==0 && device.Values[2]==0 && adapter.FaultReason=="source_expired_or_clock_invalid","Event callback independently releases owned input at source expiry even without a subsequent driver tick");
        adapter.Remove();check(!adapter.NeedsCleanup && data.Definitions.Count==0 && data.Maps.Count==0 && Rewired.ReInput.controllers.CustomControllers.Count==0 && Rewired.ReInput.players.SystemPlayer.controllers.Controllers.Count==0 && Rewired.ReInput.players.SystemPlayer.controllers.maps.Content.Count==0 && Rewired.ReInput.Subscribers==0,"Exact owned public definitions, map, assignment, device and callback are all removed");
        foreach(string conflict in new[]{"definition","device","joystick","action"})
        {
            Rewired.ReInput.Reset();data=Rewired.ReInput.Data;
            if(conflict=="definition")data.Definitions.Add(new Rewired.Data.CustomController_Editor {id=99});
            if(conflict=="device")Rewired.ReInput.controllers.CustomControllers.Add(new Rewired.CustomController(99,99));
            if(conflict=="joystick")Rewired.ReInput.controllers.JoystickContent.Add(new Rewired.Controller(99));if(conflict=="action")data.BadAction=true;
            adapter=Adapter(()=>0);bool refused=false;try{adapter.Add();}catch(Exception){refused=true;}
            check(refused && !adapter.NeedsCleanup && Rewired.ReInput.Subscribers==0 && data.Maps.Count==0,"Public preflight refuses an incompatible or conflicting baseline before mutation: "+conflict);
        }
        foreach(string change in new[]{"editor-entry","editor-container","runtime-entry-replaced","runtime-map","other-player","axis","axis-id","renamed-definition","duplicate-assignment","id-reuse","tag","axis-range","axis-invert","axis-deadzone","axis-zero","axis-min","axis-max","axis-no-calibration","axis-sensitivity-type","axis-sensitivity","curve-value","curve-weight","curve-wrap","curve-keys-cap","curve-reference","info-format","info-polling","info-special","info-deadzone","info-reference","identifier-name","identifier-positive","identifier-negative","identifier-id","identifier-type","identifier-compound"})
        {
            Rewired.ReInput.Reset();adapter=Adapter(()=>0);adapter.Add();data=Rewired.ReInput.Data;device=Rewired.ReInput.controllers.CustomControllers[0];
            object addition=new object();
            if(change=="editor-entry")data.Maps[0].actionElementMaps.Add(new Rewired.ActionElementMap(99,Rewired.ControllerElementType.Button,9,Rewired.Pole.Positive,Rewired.AxisRange.Full,false));
            if(change=="editor-container")data.Maps[0].actionElementMaps=new System.Collections.Generic.List<Rewired.ActionElementMap>(data.Maps[0].actionElementMaps);
            if(change=="runtime-entry-replaced") {var entries=Rewired.ReInput.players.SystemPlayer.controllers.maps.Content[0].Entries;var old=entries[0];entries[0]=new Rewired.ActionElementMap(old.actionId,old.elementType,old.elementIdentifierId,old.axisContribution,old.axisRange,old.invert);}
            if(change=="runtime-map")Rewired.ReInput.players.SystemPlayer.controllers.maps.Content.Add(new Rewired.ControllerMap {controllerId=device.id});
            if(change=="other-player")Rewired.ReInput.players.AllPlayers[0].controllers.Controllers.Add(device);
            if(change=="axis")data.Definitions[0].AddAxis();if(change=="axis-id")data.Definitions[0].axes[0].Id=9;if(change=="renamed-definition")data.Definitions[0].name="game changed";
            var axis=data.Definitions[0].axes[0];var identifier=data.Definitions[0].elementIdentifiers[0];
            if(change=="tag")device.tag="game retagged";
            if(change=="axis-range")axis.range=Rewired.AxisRange.Positive;if(change=="axis-invert")axis.invert=true;if(change=="axis-deadzone")axis.deadZone=.1f;if(change=="axis-zero")axis.zero=.1f;if(change=="axis-min")axis.min=-.9f;if(change=="axis-max")axis.max=.9f;if(change=="axis-no-calibration")axis.doNotCalibrateRange=true;if(change=="axis-sensitivity-type")axis.sensitivityType=1;if(change=="axis-sensitivity")axis.sensitivity=1.0000001f;
            if(change=="curve-value" || change=="curve-weight") {var keys=axis.sensitivityCurve.keys;var key=keys[0];if(change=="curve-value")key.value=.4f;else key.inWeight=.4f;keys[0]=key;axis.sensitivityCurve.keys=keys;}
            if(change=="curve-wrap")axis.sensitivityCurve.postWrapMode=1;if(change=="curve-keys-cap")axis.sensitivityCurve.keys=new UnityEngine.Keyframe[65];if(change=="curve-reference")axis.sensitivityCurve=new UnityEngine.AnimationCurve();
            if(change=="info-format")axis.axisInfo.dataFormat=1;if(change=="info-polling")axis.axisInfo.excludeFromPolling=true;if(change=="info-special")axis.axisInfo.specialAxisType=1;if(change=="info-deadzone")axis.axisInfo.pollingDeadZone=.1f;if(change=="info-reference")axis.axisInfo=new Rewired.Data.Mapping.HardwareAxisInfo();
            if(change=="identifier-name")identifier.name="changed";if(change=="identifier-positive")identifier.positiveName="changed";if(change=="identifier-negative")identifier.negativeName="changed";if(change=="identifier-id")identifier.id=99;if(change=="identifier-type")identifier.elementType=Rewired.ControllerElementType.Button;if(change=="identifier-compound")identifier.compoundElementType=1;
            if(change=="duplicate-assignment")Rewired.ReInput.players.SystemPlayer.controllers.Controllers.Add(device);
            if(change=="id-reuse")data.Definitions[0]=new Rewired.Data.CustomController_Editor {id=2};
            bool preserved=false;try {adapter.Remove();preserved=adapter.NeedsCleanup;}catch(Exception){preserved=adapter.NeedsCleanup;}
            check(preserved && data.Definitions.Count==1 && data.Deletes==0 && Rewired.ReInput.controllers.Destroys==0 && Rewired.ReInput.Subscribers==0,"Changed/additional public content prevents destructive cleanup and detaches the callback: "+change);
        }
        Rewired.ReInput.Reset();adapter=Adapter(()=>0);adapter.Add();data=Rewired.ReInput.Data;var unrelated=new Rewired.Data.CustomController_Editor {id=99};data.Definitions.Add(unrelated);adapter.Remove();
        check(!adapter.NeedsCleanup && data.Definitions.Count==1 && Object.ReferenceEquals(data.Definitions[0],unrelated),"Unrelated new game definition is preserved while the exact owned definition is removed by fresh identity lookup");
        Rewired.ReInput.Reset();adapter=Adapter(()=>0);adapter.Add();data=Rewired.ReInput.Data;Rewired.ReInput.controllers.DestroyAction=()=>data.Maps[0].actionElementMaps.Add(new Rewired.ActionElementMap(99,Rewired.ControllerElementType.Axis,9,Rewired.Pole.Positive,Rewired.AxisRange.Full,false));adapter.Remove();
        check(adapter.NeedsCleanup && data.Deletes==0 && data.Maps[0].actionElementMaps.Count==4 && Rewired.ReInput.controllers.CustomControllers.Count==0,"Fresh proof after public device destruction preserves a game addition that appeared during cleanup");
        Rewired.ReInput.Reset();Rewired.ReInput.Data.AddEntryDuringMapCreate=true;adapter=Adapter(()=>0);try{adapter.Add();}catch(Exception){}adapter.Remove();check(adapter.NeedsCleanup && Rewired.ReInput.Data.Deletes==0 && Rewired.ReInput.Data.Maps[0].actionElementMaps.Count==1 && Rewired.ReInput.Data.Maps[0].actionElementMaps[0].actionId==99 && Rewired.ReInput.controllers.CustomControllers.Count==0,"Content already present when the new map returns is preserved without clearing it or creating a device");
        Rewired.ReInput.Reset();Rewired.ReInput.Data.ThrowAxisBefore=true;adapter=Adapter(()=>0);try{adapter.Add();}catch(Exception){}adapter.Remove();check(!adapter.NeedsCleanup && Rewired.ReInput.Data.Definitions.Count==0,"A known partial definition failure cleans the exact last proved owned definition");
        Rewired.ReInput.Reset();Rewired.ReInput.Data.ThrowDefinitionAfter=true;adapter=Adapter(()=>0);try{adapter.Add();}catch(Exception){}adapter.Remove();check(adapter.NeedsCleanup && Rewired.ReInput.Data.Definitions.Count==1 && Rewired.ReInput.Data.Deletes==0,"A thrown void definition creation cannot prove origin from a single new ID and preserves its unknown outcome");
        Rewired.ReInput.Reset();Rewired.ReInput.controllers.ThrowCreateAfter=true;adapter=Adapter(()=>0);try{adapter.Add();}catch(Exception){}adapter.Remove();check(!adapter.NeedsCleanup && Rewired.ReInput.Data.Definitions.Count==0 && Rewired.ReInput.controllers.CustomControllers.Count==0,"Thrown creation after mutation recovers the unique definition-linked unassigned device and removes only proved owned content");
        foreach(bool throws in new[]{false,true})
        {
            Rewired.ReInput.Reset();Rewired.ReInput.controllers.ForeignCreateNull=!throws;Rewired.ReInput.controllers.ForeignCreateThrow=throws;adapter=Adapter(()=>0);try{adapter.Add();}catch(Exception){}adapter.Remove();
            check(adapter.NeedsCleanup && Rewired.ReInput.controllers.CustomControllers.Count==1 && Rewired.ReInput.controllers.CustomControllers[0].tag=="game-owned" && Rewired.ReInput.controllers.Destroys==0 && Rewired.ReInput.Data.Deletes==0 && Rewired.ReInput.Subscribers==0,"Null/throw creation never adopts a foreign same-definition device without the exact owned tag");
        }
        Rewired.ReInput.Reset();adapter=Adapter(()=>0);adapter.Add();device=Rewired.ReInput.controllers.CustomControllers[0];adapter.SetDesired(.4,0,.8,1);Rewired.ReInput.controllers.JoystickContent.Add(new Rewired.Controller(99));Rewired.ReInput.Pulse();check(adapter.FaultReason!="" && device.Values[0]==0 && device.Values[2]==0,"A new physical-controller conflict stops the public callback and releases owned axes");
        try{adapter.Remove();}catch(Exception){}check(Rewired.ReInput.controllers.JoystickContent.Count==1 && Rewired.ReInput.Subscribers==0,"Cleanup never removes a newly arrived physical controller");Rewired.ReInput.Reset();
        foreach(string change in new[]{"tag","source-id","axis-removed","axis-reordered","identifier-reordered","axis-value","runtime-axis-removed","runtime-axis-reordered","runtime-axis-ID","runtime-identifier-reordered","runtime-polling","runtime-calibration-value","runtime-calibration-reference","runtime-calibration-curve","runtime-calibration-empty"})
        {
            Rewired.ReInput.Reset();now=0;adapter=Adapter(()=>now);adapter.Add();device=Rewired.ReInput.controllers.CustomControllers[0];data=Rewired.ReInput.Data;adapter.SetDesired(.4,.3,.8,1);now=.1;Rewired.ReInput.Pulse();
            if(change=="tag")device.tag="game changed";if(change=="source-id")device.sourceControllerId=99;if(change=="axis-removed")data.Definitions[0].axes.RemoveAt(0);if(change=="axis-reordered")data.Definitions[0].axes.Reverse();if(change=="identifier-reordered")data.Definitions[0].elementIdentifiers.Reverse();if(change=="axis-value")data.Definitions[0].axes[0].invert=true;
            if(change=="runtime-axis-removed")device.Axes.RemoveAt(0);if(change=="runtime-axis-reordered")device.Axes.Reverse();if(change=="runtime-axis-ID")device.Axes[0].id=99;if(change=="runtime-identifier-reordered")device.AxisElementIdentifiers.Reverse();if(change=="runtime-polling")device.Axes[0].pollingDeadZone=.1f;if(change=="runtime-calibration-value")device.calibrationMap.Axes[0].deadZone=.1f;if(change=="runtime-calibration-reference")device.calibrationMap=new Rewired.CalibrationMap();if(change=="runtime-calibration-curve")device.calibrationMap.Axes[0].sensitivityCurve.postWrapMode=1;if(change=="runtime-calibration-empty")device.calibrationMap.EmptyXml=true;
            now=1;Rewired.ReInput.Pulse();try{adapter.Remove();}catch(Exception){}
            check(Math.Abs(device.Values[0]-.4)<1e-6 && Math.Abs(device.Values[1]-.3)<1e-6 && Math.Abs(device.Values[2]-.8)<1e-6 && adapter.NeedsCleanup && adapter.CleanupStatus=="neutral_release_unconfirmed" && Rewired.ReInput.controllers.Destroys==0 && data.Deletes==0,"Expiry/error/removal never writes into changed original channel identity/configuration: "+change);
        }
        foreach(bool runtimeChange in new[]{false,true})
        {
            Rewired.ReInput.Reset();now=0;adapter=Adapter(()=>now);adapter.Add();device=Rewired.ReInput.controllers.CustomControllers[0];data=Rewired.ReInput.Data;adapter.SetDesired(.4,.3,.8,1);now=.1;Rewired.ReInput.Pulse();
            if(runtimeChange)Rewired.ReInput.players.SystemPlayer.controllers.maps.Content[0].Entries.Reverse();else data.Maps[0].actionElementMaps.Reverse();
            now=1;Rewired.ReInput.Pulse();try{adapter.Remove();}catch(Exception){}
            check(device.Values[0]==0 && device.Values[1]==0 && device.Values[2]==0 && adapter.NeedsCleanup && Rewired.ReInput.controllers.Destroys==0 && data.Deletes==0,"Changed mapping preserves map content but releases only intact admitted original channels");
        }
        Rewired.ReInput.Reset();now=0;adapter=Adapter(()=>now);adapter.Add();device=Rewired.ReInput.controllers.CustomControllers[0];adapter.SetDesired(.4,0,.8,1);var otherThread=new System.Threading.Thread(()=>Rewired.ReInput.Pulse());otherThread.Start();otherThread.Join();
        check(adapter.FaultReason!="" && device.Values[0]==0 && device.Values[2]==0 && adapter.AppliedUpdates==0,"A callback on another thread cannot write values into the owner-thread device");adapter.Remove();check(!adapter.NeedsCleanup && Rewired.ReInput.Subscribers==0,"Owner-thread teardown remains available after a wrong-thread callback");
        Rewired.ReInput.Reset();now=0;adapter=Adapter(()=>now);adapter.Add();device=Rewired.ReInput.controllers.CustomControllers[0];adapter.SetDesired(.4,0,.8,1);now=.1;Rewired.ReInput.Pulse();now=.05;Rewired.ReInput.Pulse();check(device.Values[0]==0 && device.Values[2]==0 && adapter.FaultReason=="source_expired_or_clock_invalid","Actual public event releases intact original channels on clock regression");adapter.Remove();Rewired.ReInput.Reset();
        adapter=Adapter(()=>0);adapter.Add();device=Rewired.ReInput.controllers.CustomControllers[0];Rewired.ReInput.players.SystemPlayer.controllers.maps.Content[0].EmptyXml=true;try{adapter.Remove();}catch(Exception){}check(adapter.NeedsCleanup && Rewired.ReInput.controllers.Destroys==0 && Rewired.ReInput.Data.Deletes==0,"An empty returned runtime-map serialization is not an ownership proof");Rewired.ReInput.Reset();
    }
}
