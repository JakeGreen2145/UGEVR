using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Ugevr
{
    // Experimental profile for one observed build. All target members invoked here are public.
    public sealed class PublicOwnedRewiredInput : IOwnedRewiredInput
    {
        public const string Profile="firewatch-2017-rewired-right-stick-trigger-v1";
        const string CoreHash="15508ee97bbf97731f20be14e40b36d208507ad475b97c896866d39c13aef87b";
        const string GameHash="145b56aebe921c0fdc660a4e64008272e09a283332146958ce2f356c7490522a";
        readonly Func<double> clock;
        readonly Func<bool> focused;
        readonly string tag="UGEVR-Rewired-"+Guid.NewGuid().ToString("N");
        readonly Dictionary<string,MethodInfo> methods=new Dictionary<string,MethodInfo>();
        readonly Type hub,dataType,definitionType,axisType,identifierType,runtimeAxesType,calibrationType,editorMapType,controllerType,controllerBase,controllerEnum,elementEnum,poleEnum,rangeEnum,entryType;
        readonly object data,global,players,player,playerControllers,playerMaps;
        readonly EventInfo inputUpdate;
        readonly bool pinned;
        object definition,editorMap,owned,runtimeMap;
        object[] editorEntries;
        int definitionId,ownedId,ownerThread;
        int[] axes;
        Action handler;
        RewiredOwnershipSnapshot definitionExpected,channelExpected,runtimeChannelExpected,expected;
        bool mutationAttempted,subscribed,deviceRemoved,definitionRemoved,ambiguous;
        double x,y,trigger,expires,lastFeedAt;
        public string UnavailableReason {get;private set;}
        public string FaultReason {get;private set;}
        public string CleanupStatus {get;private set;}
        public int AppliedUpdates {get;private set;}
        public bool NeedsCleanup {get {return mutationAttempted && (!definitionRemoved || !deviceRemoved || subscribed || ambiguous);}}
        PublicOwnedRewiredInput(string reason) {UnavailableReason=reason;FaultReason="";CleanupStatus="not_created";}
        internal PublicOwnedRewiredInput(Assembly assembly,object manager,Func<double> clock,Func<bool> focused,bool pinned=true)
        {
            this.clock=clock;this.focused=focused;this.pinned=pinned;UnavailableReason="";FaultReason="";CleanupStatus="not_created";
            hub=assembly.GetType("Rewired.ReInput",true);dataType=assembly.GetType("Rewired.Data.UserData",true);
            definitionType=assembly.GetType("Rewired.Data.CustomController_Editor",true);editorMapType=assembly.GetType("Rewired.Data.Mapping.ControllerMap_Editor",true);
            axisType=assembly.GetType("Rewired.Data.CustomController_Editor+Axis",true);identifierType=assembly.GetType("Rewired.ControllerElementIdentifier",true);
            runtimeAxesType=assembly.GetType("Rewired.ControllerWithAxes",true);calibrationType=assembly.GetType("Rewired.CalibrationMap",true);
            controllerType=assembly.GetType("Rewired.CustomController",true);controllerBase=assembly.GetType("Rewired.Controller",true);
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
            M(dataType,"CreateCustomControllerMap",0x0600153c,typeof(bool),typeof(int),typeof(int),typeof(int));
            M(dataType,"GetCustomControllerMap",0x06001535,editorMapType,typeof(int),typeof(int),typeof(int));
            M(dataType,"GetCustomControllerMapCount",0x0600154e,typeof(int),typeof(int));
            M(global.GetType(),"CreateCustomController",0x06002840,controllerType,typeof(int),typeof(string));
            M(global.GetType(),"DestroyCustomController",0x06002841,typeof(bool),controllerType);
            M(playerControllers.GetType(),"AddController",0x0600225b,typeof(void),controllerBase,typeof(bool));
            M(playerMaps.GetType(),"LoadMap",0x06002386,typeof(void),controllerEnum,typeof(int),typeof(int),typeof(int));
            M(controllerType,"SetAxisValue",0x0600088e,typeof(void),typeof(int),typeof(float));
            M(controllerBase,"get_tag",0x06000728,typeof(string));
            M(runtimeAxesType,"GetAxisIndexById",0x06000836,typeof(int),typeof(int));M(calibrationType,"ToXmlString",0x06001cb5,typeof(string));
            string[] axisFields={"elementIdentifierId","name","range","invert","deadZone","zero","min","max","doNotCalibrateRange","sensitivityType","sensitivity","sensitivityCurve","axisInfo"};
            int[] axisTokens={0x040007aa,0x040007ab,0x040007ac,0x040007ad,0x040007ae,0x040007af,0x040007b0,0x040007b1,0x040007b2,0x040007b3,0x040007b4,0x040007b5,0x040007b6};
            for(int i=0;i<axisFields.Length;++i) {FieldInfo field=axisType.GetField(axisFields[i],BindingFlags.Public|BindingFlags.Instance);if(field==null || (pinned && field.MetadataToken!=axisTokens[i])) throw new InvalidOperationException("Public axis field profile changed.");}
            MethodInfo all=playerMaps.GetType().GetMethod("GetAllMaps",BindingFlags.Public|BindingFlags.Instance,null,Type.EmptyTypes,null);
            if(all==null || (pinned && all.MetadataToken!=0x06002390) || all.IsGenericMethod || !typeof(IEnumerable).IsAssignableFrom(all.ReturnType)) throw new InvalidOperationException("Public map enumeration shape changed.");
            ConstructorInfo entry=entryType.GetConstructor(new[]{typeof(int),elementEnum,typeof(int),poleEnum,rangeEnum,typeof(bool)});
            if(entry==null || (pinned && entry.MetadataToken!=0x06001c8c)) throw new InvalidOperationException("Public map entry shape changed.");
        }
        public static IOwnedRewiredInput Create()
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
                return new PublicOwnedRewiredInput(core,manager,delegate {return Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency;},delegate {return Application.isFocused;});
            }
            catch {return new PublicOwnedRewiredInput("public_api_profile_unavailable");}
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
            method=owner.GetMethod(name,BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly,null,arguments,null);
            if(!PublicMethodShape(method,owner,name,result,arguments) || (pinned && method.MetadataToken!=token)) throw new InvalidOperationException("Pinned public method shape changed: "+name);
            methods.Add(key,method);return method;
        }
        object Call(object target,string name,int token,Type result,Type[] arguments,params object[] values)
        {return M(target.GetType(),name,token,result,arguments).Invoke(target,values);}
        static object StaticValue(Type owner,string name)
        {
            PropertyInfo property=owner.GetProperty(name,BindingFlags.Public|BindingFlags.Static|BindingFlags.DeclaredOnly);
            if(property==null || property.GetGetMethod(false)==null || property.GetIndexParameters().Length!=0) throw new InvalidOperationException("Public static property unavailable.");
            return property.GetValue(null,null);
        }
        static object PublicValue(object target,string name)
        {
            if(target==null) throw new InvalidOperationException("Public owner unavailable.");
            Type type=target.GetType();PropertyInfo property=type.GetProperty(name,BindingFlags.Public|BindingFlags.Instance);
            if(property!=null && property.GetGetMethod(false)!=null && property.GetIndexParameters().Length==0) return property.GetValue(target,null);
            FieldInfo field=type.GetField(name,BindingFlags.Public|BindingFlags.Instance);if(field!=null) return field.GetValue(target);
            throw new InvalidOperationException("Public member unavailable: "+name);
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
            if(mutationAttempted) throw new InvalidOperationException("Only one Rewired window per launch.");
            ownerThread=System.Threading.Thread.CurrentThread.ManagedThreadId;
            if(!(bool)StaticValue(hub,"isReady") || DefinitionIds().Length!=0 || CustomDevices().Length!=0 || Bounded(PublicValue(global,"Joysticks"),64).Length!=0) throw new InvalidOperationException("Existing Rewired definition/device conflict or unready manager.");
            int[] actionIds={31,32,36};string[] actionNames={"LookXAxis_Stick","LookYAxis_Stick","DialogDownButton"};
            for(int i=0;i<3;++i) if((string)Call(data,"GetActionNameById",0x0600148f,typeof(string),new[]{typeof(int)},actionIds[i])!=actionNames[i]) throw new InvalidOperationException("Action profile changed.");
            if(!ContainsId((int[])Call(data,"GetMapCategoryIds",0x060014c4,typeof(int[]),Type.EmptyTypes),0) || !ContainsId((int[])Call(data,"GetCustomControllerLayoutIds",0x06001506,typeof(int[]),Type.EmptyTypes),0)) throw new InvalidOperationException("Existing category/layout0 unavailable.");
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
            definitionExpected=DefinitionSnapshot();
            for(int i=0;i<3;++i) {Call(definition,"AddAxis",0x06000d06,typeof(void),Type.EmptyTypes);definitionExpected=DefinitionSnapshot();}
            axes=(int[])Call(definition,"GetAxisElementIdentifierIds",0x06000d00,typeof(int[]),Type.EmptyTypes);
            if(axes.Length!=3 || axes[0]==axes[1] || axes[0]==axes[2] || axes[1]==axes[2]) throw new InvalidOperationException("Owned axis shape changed.");
            channelExpected=DefinitionSnapshot(false);
            try {if(!(bool)Call(data,"CreateCustomControllerMap",0x0600153c,typeof(bool),new[]{typeof(int),typeof(int),typeof(int)},0,definitionId,0)) throw new InvalidOperationException("Definition map refused.");}
            finally {if(MapCount()==1) editorMap=EditorMap();else if(MapCount()!=0) ambiguous=true;}
            if(editorMap==null || ambiguous) throw new InvalidOperationException("Definition map creation unknown.");
            IList list=PublicValue(editorMap,"actionElementMaps") as IList;if(list==null || list.Count!=0) throw new InvalidOperationException("Unexpected new definition map content preserved.");
            definitionExpected=DefinitionSnapshot();editorEntries=new object[3];
            ConstructorInfo constructor=entryType.GetConstructor(new[]{typeof(int),elementEnum,typeof(int),poleEnum,rangeEnum,typeof(bool)});
            for(int i=0;i<3;++i)
            {editorEntries[i]=constructor.Invoke(new object[]{actionIds[i],Enum.Parse(elementEnum,"Axis"),axes[i],Enum.Parse(poleEnum,"Positive"),Enum.Parse(rangeEnum,"Full"),false});list.Add(editorEntries[i]);definitionExpected=DefinitionSnapshot();}
            definitionExpected=DefinitionSnapshot();
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
            Call(playerControllers,"AddController",0x0600225b,typeof(void),new[]{controllerBase,typeof(bool)},owned,false);
            object[] maps=OwnedMaps(player);
            if(maps.Length==0) {Call(playerMaps,"LoadMap",0x06002386,typeof(void),new[]{controllerEnum,typeof(int),typeof(int),typeof(int)},Enum.Parse(controllerEnum,"Custom"),ownedId,0,0);maps=OwnedMaps(player);}
            if(maps.Length!=1) throw new InvalidOperationException("Owned runtime map admission unknown.");runtimeMap=maps[0];
            expected=Snapshot();expires=clock()+.5;handler=OnInputUpdate;subscribed=true;inputUpdate.AddEventHandler(null,handler);CleanupStatus="owned_active";
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
            if(Convert.ToInt32(PublicValue(owned,"axisCount"))!=3) throw new InvalidOperationException("Runtime axis count changed.");
            object collection=PublicValue(owned,"Axes");object[] runtimeAxes=Bounded(collection,3);if(runtimeAxes.Length!=3) throw new InvalidOperationException("Runtime axis inventory changed.");nodes.Add(collection);nodes.AddRange(runtimeAxes);
            object identifierCollection=PublicValue(owned,"AxisElementIdentifiers");object[] identifiers=Bounded(identifierCollection,3);if(identifiers.Length!=3) throw new InvalidOperationException("Runtime identifier inventory changed.");nodes.Add(identifierCollection);nodes.AddRange(identifiers);
            for(int i=0;i<3;++i)
            {
                if((int)M(runtimeAxesType,"GetAxisIndexById",0x06000836,typeof(int),typeof(int)).Invoke(owned,new object[]{axes[i]})!=i || Convert.ToInt32(PublicValue(runtimeAxes[i],"id"))!=axes[i] || !System.Object.ReferenceEquals(PublicValue(runtimeAxes[i],"elementIdentifier"),identifiers[i])) throw new InvalidOperationException("Runtime channel index/identifier changed.");
                foreach(string member in new[]{"id","name","type","pollingDeadZone"}) Value(text,PublicValue(runtimeAxes[i],member));
                foreach(string member in new[]{"id","name","positiveName","negativeName","elementType","compoundElementType"}) Value(text,PublicValue(identifiers[i],member));
            }
            object calibration=PublicValue(owned,"calibrationMap");if(calibration==null || calibration.GetType()!=calibrationType || Convert.ToInt32(PublicValue(calibration,"axisCount"))!=3) throw new InvalidOperationException("Runtime calibration changed.");nodes.Add(calibration);
            object calibrationCollection=PublicValue(calibration,"Axes");object[] calibrated=Bounded(calibrationCollection,3);if(calibrated.Length!=3) throw new InvalidOperationException("Calibration axis inventory changed.");nodes.Add(calibrationCollection);nodes.AddRange(calibrated);
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
        RewiredOwnershipSnapshot Snapshot()
        {
            if(Bounded(PublicValue(global,"Joysticks"),64).Length!=0) throw new InvalidOperationException("Physical joystick conflict appeared.");
            RewiredOwnershipSnapshot original=DefinitionSnapshot();var nodes=new List<object>(original.Nodes);var text=new StringBuilder(original.Values);
            if(!RewiredOwnershipSnapshot.Same(runtimeChannelExpected,RuntimeChannelSnapshot())) throw new InvalidOperationException("Admitted runtime channel/calibration changed.");
            object[] devices=CustomDevices();if(devices.Length!=1 || !System.Object.ReferenceEquals(devices[0],owned) || !DeviceTagged(owned)) throw new InvalidOperationException("Custom-device conflict/reference/tag changed.");nodes.Add(owned);
            foreach(object candidate in AllPlayers())
            {
                int assigned=References(Bounded(PublicValue(PublicValue(candidate,"controllers"),"Controllers"),64),owned);object[] maps=OwnedMaps(candidate);
                if(!System.Object.ReferenceEquals(candidate,player)) {if(assigned!=0 || maps.Length!=0) throw new InvalidOperationException("Game added an assignment/map to owned device.");continue;}
                if(assigned!=1 || maps.Length!=1 || (runtimeMap!=null && !System.Object.ReferenceEquals(maps[0],runtimeMap))) throw new InvalidOperationException("Owned assignment/map changed.");
                nodes.Add(candidate);nodes.Add(maps[0]);MethodInfo serialize=maps[0].GetType().GetMethod("ToXmlString",Type.EmptyTypes);
                if(serialize==null || !serialize.IsPublic || serialize.ReturnType!=typeof(string) || (pinned && serialize.MetadataToken!=0x06001ec0)) throw new InvalidOperationException("Public map serialization unavailable.");string xml=(string)serialize.Invoke(maps[0],null);if(String.IsNullOrEmpty(xml)) throw new InvalidOperationException("Runtime map serialization failed or unavailable.");Value(text,xml);
                object runtimeEntriesCollection=PublicValue(maps[0],"AllMaps");object[] runtimeEntries=Bounded(runtimeEntriesCollection,16);if(runtimeEntries.Length!=3) throw new InvalidOperationException("Runtime map entries changed.");nodes.Add(runtimeEntriesCollection);nodes.AddRange(runtimeEntries);
            }
            return new RewiredOwnershipSnapshot {Nodes=nodes.ToArray(),Values=text.ToString()};
        }
        public void SetDesired(double x,double y,double trigger,double expires)
        {CheckThread();this.x=x;this.y=y;this.trigger=trigger;this.expires=expires;}
        void Apply(double x,double y,double trigger)
        {
            if(owned==null || deviceRemoved) return;
            if(!RewiredOwnershipSnapshot.Same(channelExpected,DefinitionSnapshot(false)) || !RewiredOwnershipSnapshot.Same(runtimeChannelExpected,RuntimeChannelSnapshot()))
                throw new InvalidOperationException("Original owned channel identity/configuration changed; no values written.");
            Call(owned,"SetAxisValue",0x0600088e,typeof(void),new[]{typeof(int),typeof(float)},0,(float)x);
            Call(owned,"SetAxisValue",0x0600088e,typeof(void),new[]{typeof(int),typeof(float)},1,(float)y);
            Call(owned,"SetAxisValue",0x0600088e,typeof(void),new[]{typeof(int),typeof(float)},2,(float)trigger);
        }
        void OnInputUpdate()
        {
            try
            {
                CheckThread();double now=clock();
                bool focus=focused();
                if(!FeedAllowed(focus,!String.IsNullOrEmpty(FaultReason),now,lastFeedAt,expires)) {Apply(0,0,0);if(String.IsNullOrEmpty(FaultReason)) FaultReason=!focus?"focus_lost":"source_expired_or_clock_invalid";return;}lastFeedAt=now;
                if(!RewiredOwnershipSnapshot.Same(expected,Snapshot())) throw new InvalidOperationException("Owned content changed.");
                Apply(x,y,trigger);++AppliedUpdates;
            }
            catch {FaultReason="owned_content_or_public_feed_changed";if(ownerThread==System.Threading.Thread.CurrentThread.ManagedThreadId) try {Apply(0,0,0);} catch { }}
        }
        public void Remove()
        {
            CheckThread();
            CleanupStatus="cleanup_unconfirmed";
            if(subscribed) {inputUpdate.RemoveEventHandler(null,handler);subscribed=false;}
            try {Apply(0,0,0);} catch {CleanupStatus="neutral_release_unconfirmed";return;}
            if(ambiguous) {CleanupStatus="creation_outcome_unknown_preserved";return;}
            if(!deviceRemoved)
            {
                bool intact=expected==null?UnassignedCreationIntact():RewiredOwnershipSnapshot.Same(expected,Snapshot());
                if(!intact) {CleanupStatus="changed_or_added_content_preserved";return;}
                try {Call(global,"DestroyCustomController",0x06002841,typeof(bool),new[]{controllerType},owned);}
                finally {deviceRemoved=!Contains(CustomDevices(),owned);}
                if(!deviceRemoved) {CleanupStatus="device_destroy_unconfirmed";return;}
            }
            if(!definitionRemoved && definition!=null)
            {
                if(definitionExpected==null || !RewiredOwnershipSnapshot.Same(definitionExpected,DefinitionSnapshot())) {CleanupStatus="changed_definition_content_preserved";return;}
                int index=(int)Call(data,"GetCustomControllerIndex",0x0600154f,typeof(int),new[]{typeof(int)},definitionId);
                if(index<0 || !System.Object.ReferenceEquals(DefinitionById(definitionId),definition)) {CleanupStatus="definition_identity_changed_preserved";return;}
                try {Call(data,"DeleteCustomController",0x0600154b,typeof(void),new[]{typeof(int)},index);}
                finally {definitionRemoved=!ContainsId(DefinitionIds(),definitionId) && MapCount()==0;}
            }
            CleanupStatus=NeedsCleanup?"cleanup_unconfirmed":"owned_resources_removed";
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
