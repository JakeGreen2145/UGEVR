using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using UnityEngine;

namespace Ugevr
{
    // Experimental profile for one observed build. All target members invoked here are public.
    public sealed class PublicOwnedRewiredInput : IOwnedRewiredInput,IRewiredFailureDiagnostics,IRewiredCleanupFailureDiagnostics,IRewiredRuntimeMonitor,IRewiredActionReadback,IOwnedFiveRewiredInput
    {
        public const string Profile="firewatch-2017-rewired-right-stick-trigger-v1";
        public const string ObservedTriggerProfile="firewatch-2017-rewired-right-stick-observed-trigger-axis1-v1";
        public const string FiveChannelProfile="firewatch-2017-rewired-dual-sticks-observed-trigger-axis1-v1";
        public static bool SupportsProfile(string profile) {return profile==Profile || profile==ObservedTriggerProfile || profile==FiveChannelProfile;}
        const string CoreHash="15508ee97bbf97731f20be14e40b36d208507ad475b97c896866d39c13aef87b";
        const string GameHash="145b56aebe921c0fdc660a4e64008272e09a283332146958ce2f356c7490522a";
        readonly RewiredChannelDescriptor channels;
        double[] desired;
        readonly Func<double> clock;
        readonly Func<bool> focused;
        readonly string tag="UGEVR-Rewired-"+Guid.NewGuid().ToString("N");
        readonly Dictionary<string,MethodInfo> methods=new Dictionary<string,MethodInfo>();
        readonly Type hub,dataType,definitionType,axisType,identifierType,runtimeAxesType,calibrationType,editorMapType,controllerType,controllerBase,runtimeMapType,controllerEnum,elementEnum,poleEnum,rangeEnum,entryType;
        readonly object manager,data,global,players,player,playerControllers,playerMaps;
        readonly EventInfo inputUpdate;
        readonly bool pinned;
        object definition,editorMap,owned,runtimeMap;
        object[] editorEntries;
        int definitionId,ownedId,ownerThread;
        int[] axes;
        Action handler;
        RewiredOwnershipSnapshot definitionExpected,channelExpected,runtimeChannelExpected,expected;
        bool mutationAttempted,subscribed,deviceRemoved,definitionRemoved,ambiguous;
        double expires,lastFeedAt;
        double heartbeatStart,lastCallbackAt,lastMonitorAt,nextFullMonitor;
        bool callbackObserved,contextLost;
        public const double CallbackGraceSeconds=.5;
        public string UnavailableReason {get;private set;}
        public string FaultReason {get;private set;}
        public string CleanupStatus {get;private set;}
        public int AppliedUpdates {get;private set;}
        public RewiredFailureInfo LastFailure {get;private set;}
        public RewiredFailureInfo LastCleanupFailure {get;private set;}
        string snapshotDomain="not_started";
        string addStage;
        bool? preflightReady;
        int? preflightDefinitions,preflightCustomDevices,preflightJoysticks;
        public bool NeedsCleanup {get {return mutationAttempted && (!definitionRemoved || !deviceRemoved || subscribed || ambiguous);}}
        PublicOwnedRewiredInput(string reason) {channels=RewiredChannelDescriptor.Legacy;desired=new double[3];UnavailableReason=reason;FaultReason="";CleanupStatus="not_created";}
        internal PublicOwnedRewiredInput(Assembly assembly,object manager,Func<double> clock,Func<bool> focused,bool pinned=true)
            :this(assembly,manager,clock,focused,pinned,RewiredChannelDescriptor.Legacy) {}
        internal PublicOwnedRewiredInput(Assembly assembly,object manager,Func<double> clock,Func<bool> focused,bool pinned,RewiredChannelDescriptor channels)
        {
            if(channels!=RewiredChannelDescriptor.Legacy && channels!=RewiredChannelDescriptor.Five) throw new ArgumentException("Compiled channel descriptor required.");
            this.channels=channels;desired=new double[channels.Count];
            this.manager=manager;this.clock=clock;this.focused=focused;this.pinned=pinned;UnavailableReason="";FaultReason="";CleanupStatus="not_created";
            hub=assembly.GetType("Rewired.ReInput",true);dataType=assembly.GetType("Rewired.Data.UserData",true);
            definitionType=assembly.GetType("Rewired.Data.CustomController_Editor",true);editorMapType=assembly.GetType("Rewired.Data.Mapping.ControllerMap_Editor",true);
            axisType=assembly.GetType("Rewired.Data.CustomController_Editor+Axis",true);identifierType=assembly.GetType("Rewired.ControllerElementIdentifier",true);
            runtimeAxesType=assembly.GetType("Rewired.ControllerWithAxes",true);calibrationType=assembly.GetType("Rewired.CalibrationMap",true);
            controllerType=assembly.GetType("Rewired.CustomController",true);controllerBase=assembly.GetType("Rewired.Controller",true);
            runtimeMapType=assembly.GetType("Rewired.ControllerMap",true);
            controllerEnum=assembly.GetType("Rewired.ControllerType",true);elementEnum=assembly.GetType("Rewired.ControllerElementType",true);
            poleEnum=assembly.GetType("Rewired.Pole",true);rangeEnum=assembly.GetType("Rewired.AxisRange",true);entryType=assembly.GetType("Rewired.ActionElementMap",true);
            if(Convert.ToInt32(Enum.Parse(controllerEnum,"Custom"))!=20 || Convert.ToInt32(Enum.Parse(elementEnum,"Axis"))!=0) throw new InvalidOperationException("Enum profile changed.");
            data=PublicValue(manager,"userData");if(data==null || data.GetType()!=dataType) throw new InvalidOperationException("UserData unavailable.");
            global=StaticValue(hub,"controllers");players=StaticValue(hub,"players");player=PublicValue(players,"SystemPlayer");
            if(player==null || player.GetType()!=assembly.GetType("Rewired.Player",true)) throw new InvalidOperationException("System player unavailable.");
            playerControllers=PublicValue(player,"controllers");playerMaps=PublicValue(playerControllers,"maps");
            inputUpdate=hub.GetEvent("InputSourceUpdateEvent",BindingFlags.Public|BindingFlags.Static|BindingFlags.DeclaredOnly);
            if(inputUpdate==null || inputUpdate.EventHandlerType!=typeof(Action) || inputUpdate.GetAddMethod(false)==null || inputUpdate.GetRemoveMethod(false)==null || (pinned && (inputUpdate.GetAddMethod(false).MetadataToken!=0x06002748 || inputUpdate.GetRemoveMethod(false).MetadataToken!=0x06002749))) throw new InvalidOperationException("Public update event shape changed.");
            // Resolve every mutator before the first mutation.
            M(dataType,"AddCustomController",0x06001549,typeof(void));M(dataType,"GetCustomControllerIds",0x06001551,typeof(int[]));
            if(M(dataType,"AddCustomController",0x06001549,typeof(void)).IsVirtual) throw new InvalidOperationException("Definition creation must use the inspected nonvirtual factory.");
            M(dataType,"GetCustomControllerById",0x06001555,definitionType,typeof(int));M(dataType,"GetCustomControllerIndex",0x0600154f,typeof(int),typeof(int));
            M(dataType,"DeleteCustomController",0x0600154b,typeof(void),typeof(int));M(definitionType,"AddAxis",0x06000d06,typeof(void));
            M(definitionType,"GetAxisElementIdentifierIds",0x06000d00,typeof(int[]));
            if(M(dataType,"CreateCustomControllerMap",0x0600153c,typeof(bool),typeof(int),typeof(int),typeof(int)).IsVirtual)
                throw new RewiredOperationException("resolve_public_method_CreateCustomControllerMap","virtual_map_factory_unsupported");
            M(dataType,"GetCustomControllerMap",0x06001535,editorMapType,typeof(int),typeof(int),typeof(int));
            M(dataType,"GetCustomControllerMapCount",0x0600154e,typeof(int),typeof(int));
            M(global.GetType(),"CreateCustomController",0x06002840,controllerType,typeof(int),typeof(string));
            M(global.GetType(),"DestroyCustomController",0x06002841,typeof(bool),controllerType);
            M(playerControllers.GetType(),"AddController",0x0600225b,typeof(void),controllerBase,typeof(bool));
            M(playerMaps.GetType(),"LoadMap",0x06002386,typeof(void),controllerEnum,typeof(int),typeof(int),typeof(int));
            M(controllerType,"SetAxisValue",0x0600088e,typeof(void),typeof(int),typeof(float));
            M(controllerBase,"get_tag",0x06000728,typeof(string));
            M(controllerBase,"get_enabled",0x06000724,typeof(bool));M(runtimeMapType,"get_enabled",0x06001e2b,typeof(bool));
            if(M(runtimeMapType,"set_enabled",0x06001e2c,typeof(void),typeof(bool)).IsVirtual) throw new RewiredOperationException("resolve_runtime_map_enabled","virtual_map_enabled_setter_unsupported");
            M(runtimeAxesType,"GetAxisIndexById",0x06000836,typeof(int),typeof(int));M(calibrationType,"ToXmlString",0x06001cb5,typeof(string));
            string[] axisFields={"elementIdentifierId","name","range","invert","deadZone","zero","min","max","doNotCalibrateRange","sensitivityType","sensitivity","sensitivityCurve","axisInfo"};
            int[] axisTokens={0x040007aa,0x040007ab,0x040007ac,0x040007ad,0x040007ae,0x040007af,0x040007b0,0x040007b1,0x040007b2,0x040007b3,0x040007b4,0x040007b5,0x040007b6};
            for(int i=0;i<axisFields.Length;++i) {FieldInfo field=axisType.GetField(axisFields[i],BindingFlags.Public|BindingFlags.Instance);if(field==null || (pinned && field.MetadataToken!=axisTokens[i])) throw new InvalidOperationException("Public axis field profile changed.");}
            MethodInfo all=playerMaps.GetType().GetMethod("GetAllMaps",BindingFlags.Public|BindingFlags.Instance,null,Type.EmptyTypes,null);
            if(all==null || (pinned && all.MetadataToken!=0x06002390) || all.IsGenericMethod || !typeof(IEnumerable).IsAssignableFrom(all.ReturnType)) throw new InvalidOperationException("Public map enumeration shape changed.");
            ConstructorInfo entry=entryType.GetConstructor(new[]{typeof(int),elementEnum,typeof(int),poleEnum,rangeEnum,typeof(bool)});
            if(entry==null || (pinned && entry.MetadataToken!=0x06001c8c)) throw new InvalidOperationException("Public map entry shape changed.");
        }
        public static IOwnedRewiredInput Create() {return CreateBackend(RewiredChannelDescriptor.Legacy);}
        public static IOwnedFiveRewiredInput CreateFive() {return (IOwnedFiveRewiredInput)CreateBackend(RewiredChannelDescriptor.Five);}
        static IOwnedRewiredInput CreateBackend(RewiredChannelDescriptor channels)
        {
            try
            {
                Assembly core=null,game=null;
                foreach(Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {if(assembly.GetName().Name=="Rewired_Core") core=assembly;if(assembly.GetName().Name=="Assembly-CSharp") game=assembly;}
                if(!MatchesAssembly(core,"Rewired_Core",new Guid("159fb15d-62c3-4977-b0f0-ed64736f91be"),CoreHash) || !MatchesAssembly(game,"Assembly-CSharp",new Guid("4e377d15-4996-4da3-9447-797d50034429"),GameHash)) return new PublicOwnedRewiredInput("assembly_profile_mismatch_or_not_loaded");
                Type managerType=core.GetType("Rewired.InputManager_Base",true);UnityEngine.Object[] candidates=UnityEngine.Object.FindObjectsOfType(managerType);
                if(candidates.Length>16) return new PublicOwnedRewiredInput("manager_query_bound_exceeded");
                object manager=null;
                foreach(UnityEngine.Object candidate in candidates)
                {
                    var behaviour=candidate as Behaviour;if(behaviour==null || !behaviour.isActiveAndEnabled) continue;
                    if(manager!=null) return new PublicOwnedRewiredInput("manager_ambiguous");manager=candidate;
                }
                if(manager==null) return new PublicOwnedRewiredInput("manager_not_active");
                return new PublicOwnedRewiredInput(core,manager,delegate {return Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency;},delegate {return Application.isFocused;},true,channels);
            }
            catch(Exception error) {return new PublicOwnedRewiredInput("public_api_profile_unavailable") {LastFailure=RewiredFailureInfo.Capture(error,"factory_public_profile","public_api_exception")};}
        }
        public static bool MatchesIdentity(string name,Guid mvid,string hash,string expectedName,Guid expectedMvid,string expectedHash)
        {return name==expectedName && mvid==expectedMvid && hash==expectedHash;}
        public static bool FeedAllowed(bool focused,bool faulted,double now,double last,double expires)
        {return focused && !faulted && !Double.IsNaN(now) && !Double.IsInfinity(now) && !Double.IsNaN(expires) && !Double.IsInfinity(expires) && now>=last && now<expires;}
        static bool MatchesAssembly(Assembly assembly,string name,Guid mvid,string expected)
        {
            if(assembly==null || assembly.GetName().Name!=name || assembly.ManifestModule.ModuleVersionId!=mvid || String.IsNullOrEmpty(assembly.Location)) return false;
            using(var stream=File.OpenRead(assembly.Location)) using(var sha=SHA256.Create())
            {var text=new StringBuilder();foreach(byte b in sha.ComputeHash(stream)) text.Append(b.ToString("x2",CultureInfo.InvariantCulture));return MatchesIdentity(assembly.GetName().Name,assembly.ManifestModule.ModuleVersionId,text.ToString(),name,mvid,expected);}
        }
        public static bool PublicMethodShape(MethodInfo method,Type owner,string name,Type result,Type[] arguments)
        {
            if(method==null || !method.IsPublic || method.IsStatic || method.IsGenericMethod || method.DeclaringType!=owner || method.Name!=name || method.ReturnType!=result) return false;
            ParameterInfo[] parameters=method.GetParameters();if(parameters.Length!=arguments.Length) return false;
            for(int i=0;i<parameters.Length;++i) if(parameters[i].ParameterType!=arguments[i]) return false;return true;
        }
        MethodInfo M(Type owner,string name,int token,Type result,params Type[] arguments)
        {
            string key=owner.FullName+"/"+token.ToString("x8",CultureInfo.InvariantCulture);MethodInfo method;
            if(methods.TryGetValue(key,out method)) return method;
            if(name=="GetLastActiveController")
            {
                // The shipped helper also exposes a generic zero-argument overload.
                // Select only the fixed nongeneric shape instead of ambiguous GetMethod.
                method=null;var candidates=owner.GetMethods(BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly);
                if(candidates.Length>128) throw new RewiredOperationException("resolve_public_method_"+name,"public_method_query_bound_exceeded");
                foreach(var candidate in candidates) if(PublicMethodShape(candidate,owner,name,result,arguments) && (!pinned || candidate.MetadataToken==token))
                {if(method!=null) throw new RewiredOperationException("resolve_public_method_"+name,"public_method_shape_ambiguous");method=candidate;}
            }
            else method=owner.GetMethod(name,BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly,null,arguments,null);
            if(!PublicMethodShape(method,owner,name,result,arguments) || (pinned && method.MetadataToken!=token)) throw new RewiredOperationException("resolve_public_method_"+name,"public_method_shape_or_token_mismatch");
            methods.Add(key,method);return method;
        }
        object Call(object target,string name,int token,Type result,Type[] arguments,params object[] values)
        {
            MethodInfo method=M(target.GetType(),name,token,result,arguments);
            try {return method.Invoke(target,values);}
            catch(Exception error) {throw new RewiredOperationException("invoke_public_method_"+name,"public_method_exception",error);}
        }
        static object StaticValue(Type owner,string name)
        {
            PropertyInfo property=owner.GetProperty(name,BindingFlags.Public|BindingFlags.Static|BindingFlags.DeclaredOnly);
            if(property==null || property.GetGetMethod(false)==null || property.GetIndexParameters().Length!=0) throw new RewiredOperationException("read_public_static_"+name,"public_property_shape_unavailable");
            try {return property.GetValue(null,null);}catch(Exception error){throw new RewiredOperationException("read_public_static_"+name,"public_property_exception",error);}
        }
        static object PublicValue(object target,string name)
        {
            if(target==null) throw new RewiredOperationException("read_public_member_"+name,"public_owner_unavailable");
            Type type=target.GetType();PropertyInfo property=type.GetProperty(name,BindingFlags.Public|BindingFlags.Instance);
            if(property!=null && property.GetGetMethod(false)!=null && property.GetIndexParameters().Length==0)
                try {return property.GetValue(target,null);}catch(Exception error){throw new RewiredOperationException("read_public_member_"+name,"public_property_exception",error);}
            FieldInfo field=type.GetField(name,BindingFlags.Public|BindingFlags.Instance);if(field!=null)
                try {return field.GetValue(target);}catch(Exception error){throw new RewiredOperationException("read_public_member_"+name,"public_field_exception",error);}
            throw new RewiredOperationException("read_public_member_"+name,"public_member_unavailable");
        }
        static object[] Bounded(object values,int bound)
        {
            IEnumerable source=values as IEnumerable;if(source==null) throw new InvalidOperationException("Public collection unavailable.");
            var result=new List<object>();foreach(object value in source) {if(value==null || result.Count==bound) throw new InvalidOperationException("Public collection bound/null exceeded.");result.Add(value);}return result.ToArray();
        }
        static bool Contains(object[] values,object value) {foreach(object item in values) if(System.Object.ReferenceEquals(item,value)) return true;return false;}
        static int References(object[] values,object value) {int count=0;foreach(object item in values) if(System.Object.ReferenceEquals(item,value)) ++count;return count;}
        int[] DefinitionIds() {return (int[])Call(data,"GetCustomControllerIds",0x06001551,typeof(int[]),Type.EmptyTypes);}
        object DefinitionById(int id) {return Call(data,"GetCustomControllerById",0x06001555,definitionType,new[]{typeof(int)},id);}
        object EditorMap() {return Call(data,"GetCustomControllerMap",0x06001535,editorMapType,new[]{typeof(int),typeof(int),typeof(int)},0,definitionId,0);}
        int MapCount() {return (int)Call(data,"GetCustomControllerMapCount",0x0600154e,typeof(int),new[]{typeof(int)},definitionId);}
        object[] CustomDevices() {return Bounded(PublicValue(global,"CustomControllers"),64);}
        void CheckThread() {if(ownerThread!=System.Threading.Thread.CurrentThread.ManagedThreadId) throw new InvalidOperationException("Unexpected Rewired thread.");}
        public void Add()
        {
            try {AddCore();}
            catch(Exception error)
            {
                if(LastFailure==null) LastFailure=RewiredFailureInfo.Capture(error,addStage??"add_entry","public_api_exception",preflightReady,preflightDefinitions,preflightCustomDevices,preflightJoysticks);
                throw;
            }
        }
        void AddCore()
        {
            addStage="add_entry";
            if(mutationAttempted) throw new InvalidOperationException("Only one Rewired window per launch.");
            ownerThread=System.Threading.Thread.CurrentThread.ManagedThreadId;
            addStage="preflight_ready";preflightReady=(bool)StaticValue(hub,"isReady");if(preflightReady!=true) throw new RewiredOperationException(addStage,"manager_not_ready");
            addStage="preflight_definitions";preflightDefinitions=DefinitionIds().Length;if(preflightDefinitions!=0) throw new RewiredOperationException(addStage,"existing_definition_conflict");
            addStage="preflight_custom_devices";preflightCustomDevices=CustomDevices().Length;if(preflightCustomDevices!=0) throw new RewiredOperationException(addStage,"existing_custom_device_conflict");
            addStage="preflight_joysticks";preflightJoysticks=Bounded(PublicValue(global,"Joysticks"),64).Length;if(preflightJoysticks!=0) throw new RewiredOperationException(addStage,"existing_joystick_conflict");
            addStage="preflight_action_names";for(int i=0;i<channels.Count;++i) if((string)Call(data,"GetActionNameById",0x0600148f,typeof(string),new[]{typeof(int)},channels.ActionId(i))!=channels.ActionName(i)) throw new RewiredOperationException(addStage,"action_profile_mismatch");
            addStage="preflight_categories";if(!ContainsId((int[])Call(data,"GetMapCategoryIds",0x060014c4,typeof(int[]),Type.EmptyTypes),0)) throw new RewiredOperationException(addStage,"category_zero_unavailable");
            addStage="preflight_layouts";if(!ContainsId((int[])Call(data,"GetCustomControllerLayoutIds",0x06001506,typeof(int[]),Type.EmptyTypes),0)) throw new RewiredOperationException(addStage,"layout_zero_unavailable");
            addStage="create_definition";
            mutationAttempted=true;deviceRemoved=true;
            // The pinned nonvirtual CIL creates a fresh concrete definition, then List.Add;
            // its constructors/name helpers contain no application callbacks. A thrown call
            // does not establish that this path returned our definition.
            bool definitionReturned=false;
            try {Call(data,"AddCustomController",0x06001549,typeof(void),Type.EmptyTypes);definitionReturned=true;}
            finally
            {
                int[] ids=DefinitionIds();if(definitionReturned && ids.Length==1) {definitionId=ids[0];definition=DefinitionById(definitionId);}else if(ids.Length!=0) ambiguous=true;
                if(definition==null && ids.Length==0) definitionRemoved=true;
            }
            if(definition==null || ambiguous) throw new InvalidOperationException("Definition creation outcome unknown.");
            addStage="snapshot_definition";
            definitionExpected=DefinitionSnapshot();
            addStage="create_definition_axes";
            for(int i=0;i<channels.Count;++i) {Call(definition,"AddAxis",0x06000d06,typeof(void),Type.EmptyTypes);definitionExpected=DefinitionSnapshot();}
            axes=(int[])Call(definition,"GetAxisElementIdentifierIds",0x06000d00,typeof(int[]),Type.EmptyTypes);
            if(axes.Length!=channels.Count) throw new InvalidOperationException("Owned axis shape changed.");
            for(int i=0;i<axes.Length;++i) for(int j=0;j<i;++j) if(axes[i]==axes[j]) throw new InvalidOperationException("Owned axis IDs duplicated.");
            channelExpected=DefinitionSnapshot(false);
            addStage="create_definition_map";
            if(MapCount()!=0) throw new RewiredOperationException(addStage,"existing_definition_map_preserved");
            // Pinned 0600153c creates a fresh empty map, adds it, and returns false
            // unconditionally. Its return is not a success flag. Only a normal return
            // plus the following object/content proof permits adoption.
            try {Call(data,"CreateCustomControllerMap",0x0600153c,typeof(bool),new[]{typeof(int),typeof(int),typeof(int)},0,definitionId,0);}
            finally {if(MapCount()==1) editorMap=EditorMap();else if(MapCount()!=0) ambiguous=true;}
            if(editorMap==null || ambiguous) throw new RewiredOperationException(addStage,"map_post_state_unavailable_or_ambiguous");
            if(!RewiredOwnershipSnapshot.Same(channelExpected,DefinitionSnapshot(false))) throw new RewiredOperationException(addStage,"definition_changed_during_map_creation");
            if(editorMap.GetType()!=editorMapType || Convert.ToInt32(PublicValue(editorMap,"categoryId"))!=0 || Convert.ToInt32(PublicValue(editorMap,"layoutId"))!=0 || Convert.ToInt32(PublicValue(editorMap,"customControllerUid"))!=definitionId)
                throw new RewiredOperationException(addStage,"map_identity_unexpected_preserved");
            IList list=PublicValue(editorMap,"actionElementMaps") as IList;if(list==null || list.Count!=0) throw new RewiredOperationException(addStage,"map_initial_content_unexpected_preserved");
            definitionExpected=DefinitionSnapshot();editorEntries=new object[channels.Count];
            addStage="create_map_entries";
            ConstructorInfo constructor=entryType.GetConstructor(new[]{typeof(int),elementEnum,typeof(int),poleEnum,rangeEnum,typeof(bool)});
            for(int i=0;i<channels.Count;++i)
            {editorEntries[i]=constructor.Invoke(new object[]{channels.ActionId(i),Enum.Parse(elementEnum,"Axis"),axes[i],Enum.Parse(poleEnum,"Positive"),Enum.Parse(rangeEnum,"Full"),false});list.Add(editorEntries[i]);definitionExpected=DefinitionSnapshot();}
            definitionExpected=DefinitionSnapshot();
            addStage="create_runtime_controller";
            try {owned=Call(global,"CreateCustomController",0x06002840,controllerType,new[]{typeof(int),typeof(string)},definitionId,tag);}
            finally
            {
                object[] devices=CustomDevices();
                if(owned==null && devices.Length==1 && DeviceTagged(devices[0])) owned=devices[0];
                if(owned!=null && devices.Length==1 && System.Object.ReferenceEquals(devices[0],owned) && DeviceTagged(owned)) {deviceRemoved=false;ownedId=Convert.ToInt32(PublicValue(owned,"id"));runtimeChannelExpected=RuntimeChannelSnapshot();}
                else {owned=null;if(devices.Length!=0) ambiguous=true;}
            }
            if(owned==null) throw new InvalidOperationException("Controller creation unavailable.");
            ownedId=Convert.ToInt32(PublicValue(owned,"id"));
            addStage="require_owned_controller_enabled";
            if(!OwnedEnabled()) throw new RewiredOperationException(addStage,"owned_controller_disabled");
            addStage="assign_system_player";
            Call(playerControllers,"AddController",0x0600225b,typeof(void),new[]{controllerBase,typeof(bool)},owned,false);
            object[] maps=OwnedMaps(player);
            if(maps.Length==0) {Call(playerMaps,"LoadMap",0x06002386,typeof(void),new[]{controllerEnum,typeof(int),typeof(int),typeof(int)},Enum.Parse(controllerEnum,"Custom"),ownedId,0,0);maps=OwnedMaps(player);}
            if(maps.Length!=1) throw new InvalidOperationException("Owned runtime map admission unknown.");runtimeMap=maps[0];
            addStage="activate_owned_runtime_map";
            CheckCurrentContext();
            if(!RewiredOwnershipSnapshot.Same(definitionExpected,DefinitionSnapshot())) throw new RewiredOperationException(addStage,"definition_changed_before_map_enable");
            RequireRuntimeMapAdmission();var before=Snapshot(true);var beforeFull=Snapshot();expected=beforeFull;
            if(!MapEnabled())
            {
                // Pinned nonvirtual setter is only _enabled=value; ret. Normal return
                // and a full proof excluding this single intended field are required.
                M(runtimeMapType,"set_enabled",0x06001e2c,typeof(void),typeof(bool)).Invoke(runtimeMap,new object[]{true});
                if(!MapEnabled()) throw new RewiredOperationException(addStage,"owned_map_enable_readback_false");
                if(!RewiredOwnershipSnapshot.Same(before,Snapshot(true))) throw new RewiredOperationException(addStage,"owned_content_changed_during_map_enable");
            }
            else if(!RewiredOwnershipSnapshot.Same(beforeFull,Snapshot())) throw new RewiredOperationException(addStage,"owned_content_changed_before_map_enable");
            var activated=Snapshot();
            if(!OwnedEnabled() || !MapEnabled() || !RewiredOwnershipSnapshot.Same(before,Snapshot(true))) throw new RewiredOperationException(addStage,"owned_map_final_activation_unconfirmed");
            addStage="snapshot_runtime_and_subscribe";
            expected=activated;heartbeatStart=lastMonitorAt=clock();expires=heartbeatStart+.5;handler=OnInputUpdate;subscribed=true;inputUpdate.AddEventHandler(null,handler);CleanupStatus="owned_active";
        }
        static bool ContainsId(int[] ids,int id) {if(ids==null || ids.Length>64) throw new InvalidOperationException("Identifier bound exceeded.");foreach(int value in ids) if(value==id) return true;return false;}
        bool DeviceTagged(object device)
        {return device!=null && device.GetType()==controllerType && Convert.ToInt32(PublicValue(device,"sourceControllerId"))==definitionId && (string)M(controllerBase,"get_tag",0x06000728,typeof(string)).Invoke(device,null)==tag;}
        static void Value(StringBuilder text,object value)
        {
            string item=value is float?((float)value).ToString("R",CultureInfo.InvariantCulture):value is double?((double)value).ToString("R",CultureInfo.InvariantCulture):Convert.ToString(value,CultureInfo.InvariantCulture) ?? "";if(item.Length>65536) throw new InvalidOperationException("Owned content serialization bound exceeded.");text.Append(item.Length).Append(':').Append(item).Append(';');
        }
        RewiredOwnershipSnapshot DefinitionSnapshot(bool includeMap=true)
        {
            if(definition==null || !System.Object.ReferenceEquals(DefinitionById(definitionId),definition)) throw new InvalidOperationException("Definition reference changed.");
            var nodes=new List<object>();var text=new StringBuilder();nodes.Add(definition);
            foreach(string name in new[]{"id","name","descriptiveName","typeGuid","axisCount","buttonCount"}) Value(text,PublicValue(definition,name));
            foreach(string name in new[]{"axes","buttons","elementIdentifiers"})
            {
                object collection=PublicValue(definition,name);object[] children=Bounded(collection,16);nodes.Add(collection);Value(text,children.Length);nodes.AddRange(children);
                if(name=="buttons" && children.Length!=0) throw new InvalidOperationException("Unowned buttons appeared.");
                foreach(object child in children)
                {
                    if(name=="axes") AxisSnapshot(child,nodes,text);
                    if(name=="elementIdentifiers")
                    {
                        if(child.GetType()!=identifierType) throw new InvalidOperationException("Identifier type changed.");
                        foreach(string member in new[]{"id","name","positiveName","negativeName","elementType","compoundElementType"}) Value(text,PublicValue(child,member));
                    }
                }
            }
            foreach(int axis in (int[])Call(definition,"GetAxisElementIdentifierIds",0x06000d00,typeof(int[]),Type.EmptyTypes)) Value(text,axis);
            foreach(string name in (string[])Call(definition,"GetAxisNames",0x06000cff,typeof(string[]),Type.EmptyTypes)) Value(text,name);
            if(includeMap && editorMap!=null)
            {
                if(MapCount()!=1 || !System.Object.ReferenceEquals(EditorMap(),editorMap)) throw new InvalidOperationException("Definition map reference/additions changed.");nodes.Add(editorMap);
                foreach(string name in new[]{"id","categoryId","layoutId","name","hardwareGuidString","customControllerUid"}) Value(text,PublicValue(editorMap,name));
                object entryCollection=PublicValue(editorMap,"actionElementMaps");object[] entries=Bounded(entryCollection,16);nodes.Add(entryCollection);Value(text,entries.Length);nodes.AddRange(entries);
                foreach(object entry in entries) foreach(string name in new[]{"actionId","elementIdentifierId","elementType","axisRange","axisContribution","invert","keyboardKeyCode","modifierKey1","modifierKey2","modifierKey3","enabled"}) Value(text,PublicValue(entry,name));
            }
            else if(includeMap && MapCount()!=0) throw new InvalidOperationException("Unowned definition map appeared.");
            return new RewiredOwnershipSnapshot {Nodes=nodes.ToArray(),Values=text.ToString()};
        }
        void AxisSnapshot(object axis,List<object> nodes,StringBuilder text)
        {
            if(axis.GetType()!=axisType) throw new InvalidOperationException("Axis type changed.");
            foreach(string member in new[]{"elementIdentifierId","name","range","invert","deadZone","zero","min","max","doNotCalibrateRange","sensitivityType","sensitivity"}) Value(text,PublicValue(axis,member));
            object info=PublicValue(axis,"axisInfo");nodes.Add(info);Value(text,info!=null);
            if(info!=null) foreach(string member in new[]{"dataFormat","excludeFromPolling","specialAxisType","pollingDeadZone"}) Value(text,PublicValue(info,member));
            CurveSnapshot(PublicValue(axis,"sensitivityCurve"),nodes,text);
        }
        static void CurveSnapshot(object curve,List<object> nodes,StringBuilder text)
        {
            nodes.Add(curve);Value(text,curve!=null);
            if(curve==null) return;
            Value(text,PublicValue(curve,"preWrapMode"));Value(text,PublicValue(curve,"postWrapMode"));
            int count=Convert.ToInt32(PublicValue(curve,"length"));if(count<0 || count>64) throw new InvalidOperationException("Curve key inspection bound exceeded.");
            object[] keys=Bounded(PublicValue(curve,"keys"),64);if(keys.Length!=count) throw new InvalidOperationException("Curve changed during inspection.");Value(text,count);
            foreach(object key in keys)
            {
                foreach(string member in new[]{"time","value","inTangent","outTangent","tangentMode"}) Value(text,PublicValue(key,member));
                foreach(string member in new[]{"inWeight","outWeight","weightedMode"})
                {PropertyInfo property=key.GetType().GetProperty(member,BindingFlags.Public|BindingFlags.Instance);if(property!=null && property.GetGetMethod(false)!=null) Value(text,property.GetValue(key,null));}
            }
        }
        RewiredOwnershipSnapshot RuntimeChannelSnapshot()
        {
            if(!Contains(CustomDevices(),owned) || !DeviceTagged(owned) || Convert.ToInt32(PublicValue(owned,"id"))!=ownedId) throw new InvalidOperationException("Owned runtime device changed.");
            var nodes=new List<object>();var text=new StringBuilder();nodes.Add(owned);Value(text,PublicValue(owned,"enabled"));
            if(Convert.ToInt32(PublicValue(owned,"axisCount"))!=channels.Count) throw new InvalidOperationException("Runtime axis count changed.");
            object collection=PublicValue(owned,"Axes");object[] runtimeAxes=Bounded(collection,channels.Count);if(runtimeAxes.Length!=channels.Count) throw new InvalidOperationException("Runtime axis inventory changed.");nodes.Add(collection);nodes.AddRange(runtimeAxes);
            object identifierCollection=PublicValue(owned,"AxisElementIdentifiers");object[] identifiers=Bounded(identifierCollection,channels.Count);if(identifiers.Length!=channels.Count) throw new InvalidOperationException("Runtime identifier inventory changed.");nodes.Add(identifierCollection);nodes.AddRange(identifiers);
            for(int i=0;i<channels.Count;++i)
            {
                if((int)M(runtimeAxesType,"GetAxisIndexById",0x06000836,typeof(int),typeof(int)).Invoke(owned,new object[]{axes[i]})!=i || Convert.ToInt32(PublicValue(runtimeAxes[i],"id"))!=axes[i] || !System.Object.ReferenceEquals(PublicValue(runtimeAxes[i],"elementIdentifier"),identifiers[i])) throw new InvalidOperationException("Runtime channel index/identifier changed.");
                foreach(string member in new[]{"id","name","type","pollingDeadZone"}) Value(text,PublicValue(runtimeAxes[i],member));
                foreach(string member in new[]{"id","name","positiveName","negativeName","elementType","compoundElementType"}) Value(text,PublicValue(identifiers[i],member));
            }
            object calibration=PublicValue(owned,"calibrationMap");if(calibration==null || calibration.GetType()!=calibrationType || Convert.ToInt32(PublicValue(calibration,"axisCount"))!=channels.Count) throw new InvalidOperationException("Runtime calibration changed.");nodes.Add(calibration);
            object calibrationCollection=PublicValue(calibration,"Axes");object[] calibrated=Bounded(calibrationCollection,channels.Count);if(calibrated.Length!=channels.Count) throw new InvalidOperationException("Calibration axis inventory changed.");nodes.Add(calibrationCollection);nodes.AddRange(calibrated);
            foreach(object axis in calibrated)
            {
                foreach(string member in new[]{"enabled","deadZone","calibratedZero","calibratedMin","calibratedMax","invert","sensitivityType","sensitivity","applyRangeCalibration"}) Value(text,PublicValue(axis,member));
                CurveSnapshot(PublicValue(axis,"sensitivityCurve"),nodes,text);
            }
            string calibrationXml=(string)M(calibrationType,"ToXmlString",0x06001cb5,typeof(string)).Invoke(calibration,null);if(String.IsNullOrEmpty(calibrationXml)) throw new InvalidOperationException("Calibration serialization failed or unavailable.");Value(text,calibrationXml);
            return new RewiredOwnershipSnapshot {Nodes=nodes.ToArray(),Values=text.ToString()};
        }
        object[] AllPlayers()
        {
            var all=new List<object>();all.Add(player);foreach(object candidate in Bounded(PublicValue(players,"AllPlayers"),64)) if(!Contains(all.ToArray(),candidate)) all.Add(candidate);return all.ToArray();
        }
        object[] OwnedMaps(object targetPlayer)
        {
            object maps=PublicValue(PublicValue(targetPlayer,"controllers"),"maps");MethodInfo getter=maps.GetType().GetMethod("GetAllMaps",BindingFlags.Public|BindingFlags.Instance,null,Type.EmptyTypes,null);
            if(getter==null || (pinned && getter.MetadataToken!=0x06002390)) throw new InvalidOperationException("Map getter changed.");var result=new List<object>();
            foreach(object map in Bounded(getter.Invoke(maps,null),1024)) if(Convert.ToInt32(PublicValue(map,"controllerType"))==20 && Convert.ToInt32(PublicValue(map,"controllerId"))==ownedId) result.Add(map);return result.ToArray();
        }
        bool OwnedEnabled() {return (bool)M(controllerBase,"get_enabled",0x06000724,typeof(bool)).Invoke(owned,null);}
        bool MapEnabled() {return (bool)M(runtimeMapType,"get_enabled",0x06001e2b,typeof(bool)).Invoke(runtimeMap,null);}
        void RequireRuntimeMapAdmission()
        {
            if(runtimeMap.GetType()!=hub.Assembly.GetType("Rewired.CustomControllerMap",true) || Convert.ToInt32(PublicValue(runtimeMap,"controllerType"))!=20 || Convert.ToInt32(PublicValue(runtimeMap,"controllerId"))!=ownedId || Convert.ToInt32(PublicValue(runtimeMap,"categoryId"))!=0 || Convert.ToInt32(PublicValue(runtimeMap,"layoutId"))!=0 || Convert.ToInt32(PublicValue(runtimeMap,"sourceMapId"))!=Convert.ToInt32(PublicValue(editorMap,"id")))
                throw new RewiredOperationException("activate_owned_runtime_map","runtime_map_identity_unexpected_preserved");
            var entries=Bounded(PublicValue(runtimeMap,"AllMaps"),16);if(entries.Length!=channels.Count) throw new RewiredOperationException("activate_owned_runtime_map","runtime_map_entries_unexpected_preserved");

            for(int i=0;i<channels.Count;++i)
                if(Convert.ToInt32(PublicValue(entries[i],"actionId"))!=channels.ActionId(i) || Convert.ToInt32(PublicValue(entries[i],"elementIdentifierId"))!=axes[i] || Convert.ToInt32(PublicValue(entries[i],"elementType"))!=0 || Convert.ToInt32(PublicValue(entries[i],"axisRange"))!=0 || Convert.ToInt32(PublicValue(entries[i],"axisContribution"))!=0 || (bool)PublicValue(entries[i],"invert"))
                    throw new RewiredOperationException("activate_owned_runtime_map","runtime_map_entry_profile_unexpected_preserved");
        }
        static string MapXmlWithoutEnabled(string xml,bool enabled)
        {
            if(String.IsNullOrEmpty(xml) || xml.Length>65536) throw new RewiredOperationException("activate_owned_runtime_map","runtime_map_xml_bound_or_empty");
            var document=new XmlDocument();document.XmlResolver=null;
            var settings=new XmlReaderSettings {XmlResolver=null};
#if NET8_0_OR_GREATER
            settings.DtdProcessing=DtdProcessing.Prohibit;
#else
            settings.ProhibitDtd=true; // net35 has no DtdProcessing property.
#endif
            using(var reader=XmlReader.Create(new StringReader(xml),settings)) document.Load(reader);
            var root=document.DocumentElement;
            const string mapNamespace="http://guavaman.com/rewired";
            if(root==null || root.LocalName!="CustomControllerMap" || root.NamespaceURI!=mapNamespace || root.Prefix.Length!=0)
                throw new RewiredOperationException("activate_owned_runtime_map","runtime_map_xml_root_unexpected");
            foreach(XmlAttribute attribute in root.Attributes) if(attribute.LocalName=="enabled")
                throw new RewiredOperationException("activate_owned_runtime_map","runtime_map_xml_enabled_shape_unexpected");
            XmlElement field=null;
            foreach(XmlNode node in root.ChildNodes)
            {
                var element=node as XmlElement;if(element==null || element.LocalName!="enabled") continue;
                if(field!=null || element.NamespaceURI!=mapNamespace || element.Prefix.Length!=0 || element.Attributes.Count!=0 || element.ChildNodes.Count!=1 || element.FirstChild.NodeType!=XmlNodeType.Text)
                    throw new RewiredOperationException("activate_owned_runtime_map","runtime_map_xml_enabled_shape_unexpected");
                field=element;
            }
            // Shipped SerializedObject.Add<bool> writes a direct field element.
            // System.Xml's Boolean writer emits lowercase true/false, never numeric forms.
            if(field==null || field.InnerText!=(enabled?"true":"false"))
                throw new RewiredOperationException("activate_owned_runtime_map","runtime_map_xml_enabled_disagreement");
            field.InnerText="owned_enabled_transition";return document.OuterXml;
        }
        RewiredOwnershipSnapshot Snapshot(bool excludeMapEnabled=false)
        {
            snapshotDomain="physical_joysticks";
            if(Bounded(PublicValue(global,"Joysticks"),64).Length!=0) throw new InvalidOperationException("Physical joystick conflict appeared.");
            snapshotDomain="definition";
            RewiredOwnershipSnapshot original=DefinitionSnapshot();var nodes=new List<object>(original.Nodes);var text=new StringBuilder(original.Values);
            snapshotDomain="runtime_channels";
            if(!RewiredOwnershipSnapshot.Same(runtimeChannelExpected,RuntimeChannelSnapshot())) throw new InvalidOperationException("Admitted runtime channel/calibration changed.");
            snapshotDomain="custom_devices";
            object[] devices=CustomDevices();if(devices.Length!=1 || !System.Object.ReferenceEquals(devices[0],owned) || !DeviceTagged(owned)) throw new InvalidOperationException("Custom-device conflict/reference/tag changed.");nodes.Add(owned);
            snapshotDomain="assignment_maps";
            foreach(object candidate in AllPlayers())
            {
                int assigned=References(Bounded(PublicValue(PublicValue(candidate,"controllers"),"Controllers"),64),owned);object[] maps=OwnedMaps(candidate);
                if(!System.Object.ReferenceEquals(candidate,player)) {if(assigned!=0 || maps.Length!=0) throw new InvalidOperationException("Game added an assignment/map to owned device.");continue;}
                if(assigned!=1 || maps.Length!=1 || (runtimeMap!=null && !System.Object.ReferenceEquals(maps[0],runtimeMap))) throw new InvalidOperationException("Owned assignment/map changed.");
                nodes.Add(candidate);nodes.Add(maps[0]);MethodInfo serialize=maps[0].GetType().GetMethod("ToXmlString",Type.EmptyTypes);
                if(serialize==null || !serialize.IsPublic || serialize.ReturnType!=typeof(string) || (pinned && serialize.MetadataToken!=0x06001ec0)) throw new InvalidOperationException("Public map serialization unavailable.");string xml=(string)serialize.Invoke(maps[0],null);if(String.IsNullOrEmpty(xml)) throw new InvalidOperationException("Runtime map serialization failed or unavailable.");Value(text,excludeMapEnabled?MapXmlWithoutEnabled(xml,MapEnabled()):xml);
                object runtimeEntriesCollection=PublicValue(maps[0],"AllMaps");object[] runtimeEntries=Bounded(runtimeEntriesCollection,16);if(runtimeEntries.Length!=channels.Count) throw new InvalidOperationException("Runtime map entries changed.");nodes.Add(runtimeEntriesCollection);nodes.AddRange(runtimeEntries);
            }
            snapshotDomain="complete";return new RewiredOwnershipSnapshot {Nodes=nodes.ToArray(),Values=text.ToString()};
        }
        public void SetDesired(double x,double y,double trigger,double expires)
        {
            if(channels!=RewiredChannelDescriptor.Legacy) throw new InvalidOperationException("Use the typed five-channel contract.");
            SetDesiredValues(new[]{x,y,trigger},expires);
        }
        public void SetDesired(RewiredFiveValues values,double expires)
        {
            if(channels!=RewiredChannelDescriptor.Five || !values.Valid) throw new ArgumentException("Valid typed five-channel state required.");
            SetDesiredValues(new[]{values.MoveX,values.MoveY,values.LookX,values.LookY,values.Trigger},expires);
        }
        void SetDesiredValues(double[] values,double expires)
        {
            CheckThread();if(Double.IsNaN(expires) || Double.IsInfinity(expires) || values.Length!=channels.Count) throw new ArgumentException("Invalid complete desired state.");
            for(int i=0;i<values.Length;++i) if(Double.IsNaN(values[i]) || Double.IsInfinity(values[i]) || Math.Abs(values[i])>1 || i==values.Length-1 && values[i]<0) throw new ArgumentException("Invalid raw desired channel.");
            desired=(double[])values.Clone();this.expires=expires;
        }
        bool? ReadbackBool(RewiredActionReadback result,int bit,Func<bool> read)
        {try {return read();}catch {result.FailedFields|=1u<<bit;return null;}}
        double? ReadbackAxis(RewiredActionReadback result,int bit,Func<float> read)
        {try {float value=read();if(Single.IsNaN(value) || Single.IsInfinity(value)) {result.FailedFields|=1u<<bit;result.NonfiniteFields|=1u<<bit;return null;}return value;}catch {result.FailedFields|=1u<<bit;return null;}}
        public RewiredActionReadback CaptureReadback()
        {
            var result=new RewiredActionReadback {ChannelCount=channels.Count};
            try {CheckThread();CheckCurrentContext();if(owned==null || deviceRemoved || runtimeMap==null) throw new InvalidOperationException();}
            catch {result.FailedFields=channels.Count==5?8191u:511u;return result;}
            result.MapEnabled=ReadbackBool(result,0,MapEnabled);result.ControllerEnabled=ReadbackBool(result,1,OwnedEnabled);
            result.OwnedAxis0=ReadbackAxis(result,2,delegate {return (float)M(runtimeAxesType,"GetAxisRaw",0x06000839,typeof(float),typeof(int)).Invoke(owned,new object[]{0});});
            result.OwnedAxis1=ReadbackAxis(result,3,delegate {return (float)M(runtimeAxesType,"GetAxisRaw",0x06000839,typeof(float),typeof(int)).Invoke(owned,new object[]{1});});
            result.OwnedAxis2=ReadbackAxis(result,4,delegate {return (float)M(runtimeAxesType,"GetAxisRaw",0x06000839,typeof(float),typeof(int)).Invoke(owned,new object[]{2});});
            result.PlayerAction31=ReadbackAxis(result,5,delegate {return (float)M(player.GetType(),"GetAxisRaw",0x060021f9,typeof(float),typeof(int)).Invoke(player,new object[]{31});});
            result.PlayerAction32=ReadbackAxis(result,6,delegate {return (float)M(player.GetType(),"GetAxisRaw",0x060021f9,typeof(float),typeof(int)).Invoke(player,new object[]{32});});
            result.PlayerAction36=ReadbackAxis(result,7,delegate {return (float)M(player.GetType(),"GetAxisRaw",0x060021f9,typeof(float),typeof(int)).Invoke(player,new object[]{36});});
            if(channels.Count==5)
            {
                result.OwnedAxis3=ReadbackAxis(result,9,delegate {return (float)M(runtimeAxesType,"GetAxisRaw",0x06000839,typeof(float),typeof(int)).Invoke(owned,new object[]{3});});
                result.OwnedAxis4=ReadbackAxis(result,10,delegate {return (float)M(runtimeAxesType,"GetAxisRaw",0x06000839,typeof(float),typeof(int)).Invoke(owned,new object[]{4});});
                result.PlayerAction29=ReadbackAxis(result,11,delegate {return (float)M(player.GetType(),"GetAxisRaw",0x060021f9,typeof(float),typeof(int)).Invoke(player,new object[]{29});});
                result.PlayerAction30=ReadbackAxis(result,12,delegate {return (float)M(player.GetType(),"GetAxisRaw",0x060021f9,typeof(float),typeof(int)).Invoke(player,new object[]{30});});
            }
            result.LastActiveMatchesOwned=ReadbackBool(result,8,delegate {return System.Object.ReferenceEquals(owned,M(global.GetType(),"GetLastActiveController",0x06002847,controllerBase).Invoke(global,null));});
            return result;
        }
        void ContextFailure(string category)
        {contextLost=true;throw new RewiredOperationException("runtime_context",category);}
        void CheckCurrentContext()
        {
            if(contextLost) throw new RewiredOperationException("runtime_context","runtime_context_already_lost");
            try
            {
                if(!(bool)StaticValue(hub,"isReady")) ContextFailure("runtime_not_ready");
                var behaviour=manager as Behaviour;
                if(behaviour==null || !behaviour.isActiveAndEnabled) ContextFailure("active_manager_unavailable");
                if(!System.Object.ReferenceEquals(PublicValue(manager,"userData"),data)) ContextFailure("manager_data_changed");
                if(!System.Object.ReferenceEquals(StaticValue(hub,"controllers"),global)) ContextFailure("global_controller_helper_changed");
                if(!System.Object.ReferenceEquals(StaticValue(hub,"players"),players)) ContextFailure("global_player_helper_changed");
                if(!System.Object.ReferenceEquals(PublicValue(players,"SystemPlayer"),player)) ContextFailure("system_player_changed");
                if(!System.Object.ReferenceEquals(PublicValue(player,"controllers"),playerControllers) || !System.Object.ReferenceEquals(PublicValue(playerControllers,"maps"),playerMaps)) ContextFailure("system_player_helpers_changed");
                if(owned!=null && !deviceRemoved && !Contains(CustomDevices(),owned)) ContextFailure("owned_device_missing_from_current_context");
            }
            catch(Exception error)
            {
                contextLost=true;var failure=RewiredFailureInfo.Capture(error,"runtime_context","public_context_read_failed");
                if(LastFailure==null) LastFailure=failure;if(String.IsNullOrEmpty(FaultReason)) FaultReason=failure.Category;
                throw;
            }
        }
        public void CheckRuntime()
        {
            CheckThread();
            try
            {
                CheckCurrentContext();double now=clock();
                if(Double.IsNaN(now) || Double.IsInfinity(now) || now<lastMonitorAt || callbackObserved && now<lastCallbackAt)
                    throw new RewiredOperationException("runtime_monitor","backend_clock_invalid");
                lastMonitorAt=now;
                if(now-(callbackObserved?lastCallbackAt:heartbeatStart)>=CallbackGraceSeconds)
                    throw new RewiredOperationException("runtime_monitor",callbackObserved?"input_callback_stale":"input_callback_never_observed");
                // Callback checks already inspect the full snapshot. Missing callbacks must
                // not remove that check entirely; bound this extra inspection to 10Hz.
                if(now>=nextFullMonitor)
                {
                    nextFullMonitor=now+.1;
                    if(!RewiredOwnershipSnapshot.Same(expected,Snapshot())) throw new RewiredOperationException("runtime_monitor","owned_content_changed");
                }
            }
            catch(Exception error)
            {
                var failure=RewiredFailureInfo.Capture(error,"runtime_monitor","public_runtime_monitor_failed");
                if(LastFailure==null) LastFailure=failure;
                if(String.IsNullOrEmpty(FaultReason)) FaultReason=failure.Category;
                throw;
            }
        }
        void Apply(double[] values)
        {
            if(owned==null || deviceRemoved) return;
            CheckCurrentContext();
            if(!OwnedEnabled()) throw new RewiredOperationException("write_owned_axes","owned_controller_disabled");
            if(!RewiredOwnershipSnapshot.Same(channelExpected,DefinitionSnapshot(false)) || !RewiredOwnershipSnapshot.Same(runtimeChannelExpected,RuntimeChannelSnapshot()))
                throw new InvalidOperationException("Original owned channel identity/configuration changed; no values written.");
            for(int i=0;i<channels.Count;++i) Call(owned,"SetAxisValue",0x0600088e,typeof(void),new[]{typeof(int),typeof(float)},i,(float)values[i]);
        }
        void OnInputUpdate()
        {
            string stage="callback_thread";
            try
            {
                CheckThread();stage="callback_context";CheckCurrentContext();stage="callback_clock";double now=clock();
                if(Double.IsNaN(now) || Double.IsInfinity(now) || callbackObserved && now<lastCallbackAt || now<heartbeatStart)
                {
                    var error=new RewiredOperationException("runtime_callback","backend_clock_invalid");if(LastFailure==null) LastFailure=RewiredFailureInfo.Capture(error,"runtime_callback","backend_clock_invalid");
                    if(String.IsNullOrEmpty(FaultReason)) FaultReason="source_expired_or_clock_invalid";throw error;
                }
                callbackObserved=true;lastCallbackAt=now;
                stage="callback_focus";bool focus=focused();
                if(!FeedAllowed(focus,!String.IsNullOrEmpty(FaultReason),now,lastFeedAt,expires)) {stage="callback_neutral_apply";Apply(new double[channels.Count]);if(String.IsNullOrEmpty(FaultReason)) FaultReason=!focus?"focus_lost":"source_expired_or_clock_invalid";return;}lastFeedAt=now;
                stage="callback_snapshot";var current=Snapshot();
                if(!RewiredOwnershipSnapshot.Same(expected,current)) throw new RewiredOperationException("callback_snapshot",RewiredOwnershipSnapshot.Mismatch(expected,current));
                stage="callback_desired_apply";Apply(desired);++AppliedUpdates;
            }
            catch(Exception error)
            {
                RememberFailure(error,stage=="callback_snapshot"?stage+"_"+snapshotDomain:stage);
                if(String.IsNullOrEmpty(FaultReason)) FaultReason="owned_content_or_public_feed_changed";
                if(ownerThread==System.Threading.Thread.CurrentThread.ManagedThreadId) try {Apply(new double[channels.Count]);}catch(Exception neutralError) {RememberCleanupFailure(neutralError,"callback_fault_neutral_apply");}
            }
        }
        void RememberFailure(Exception error,string stage)
        {try {if(LastFailure==null)LastFailure=RewiredFailureInfo.Capture(error,stage,"public_callback_failed");}catch { }}
        void RememberCleanupFailure(Exception error,string stage)
        {try {if(LastCleanupFailure==null)LastCleanupFailure=RewiredFailureInfo.Capture(error,stage,"public_cleanup_failed");}catch { }}
        public void Remove()
        {
            string stage="cleanup_thread";
            try
            {
            CheckThread();
            CleanupStatus="cleanup_unconfirmed";
            stage="cleanup_unsubscribe";
            if(subscribed) {inputUpdate.RemoveEventHandler(null,handler);subscribed=false;}
            stage="cleanup_neutral_apply";
            try {Apply(new double[channels.Count]);}catch(Exception error) {RememberCleanupFailure(error,stage);CleanupStatus="neutral_release_unconfirmed";return;}
            if(ambiguous) {CleanupStatus="creation_outcome_unknown_preserved";return;}
            if(!deviceRemoved)
            {
                stage=expected==null?"cleanup_unassigned_creation":"cleanup_snapshot";
                bool intact=expected==null?UnassignedCreationIntact():RewiredOwnershipSnapshot.Same(expected,Snapshot());
                if(!intact) {CleanupStatus="changed_or_added_content_preserved";return;}
                stage="cleanup_destroy_device";
                try {Call(global,"DestroyCustomController",0x06002841,typeof(bool),new[]{controllerType},owned);}
                finally {deviceRemoved=!Contains(CustomDevices(),owned);}
                if(!deviceRemoved) {CleanupStatus="device_destroy_unconfirmed";return;}
            }
            if(!definitionRemoved && definition!=null)
            {
                stage="cleanup_definition_snapshot";
                if(definitionExpected==null || !RewiredOwnershipSnapshot.Same(definitionExpected,DefinitionSnapshot())) {CleanupStatus="changed_definition_content_preserved";return;}
                stage="cleanup_definition_index";
                int index=(int)Call(data,"GetCustomControllerIndex",0x0600154f,typeof(int),new[]{typeof(int)},definitionId);
                if(index<0 || !System.Object.ReferenceEquals(DefinitionById(definitionId),definition)) {CleanupStatus="definition_identity_changed_preserved";return;}
                stage="cleanup_delete_definition";try {Call(data,"DeleteCustomController",0x0600154b,typeof(void),new[]{typeof(int)},index);}
                finally {definitionRemoved=!ContainsId(DefinitionIds(),definitionId) && MapCount()==0;}
            }
            CleanupStatus=NeedsCleanup?"cleanup_unconfirmed":"owned_resources_removed";
            }
            catch(Exception error) {RememberCleanupFailure(error,stage=="cleanup_snapshot"?stage+"_"+snapshotDomain:stage);throw;}
        }
        bool UnassignedCreationIntact()
        {
            if(!RewiredOwnershipSnapshot.Same(definitionExpected,DefinitionSnapshot())) return false;
            object[] devices=CustomDevices();if(devices.Length!=1 || !System.Object.ReferenceEquals(devices[0],owned) || !DeviceTagged(owned)) return false;
            foreach(object candidate in AllPlayers()) if(Contains(Bounded(PublicValue(PublicValue(candidate,"controllers"),"Controllers"),64),owned) || OwnedMaps(candidate).Length!=0) return false;
            return true;
        }
    }
}
