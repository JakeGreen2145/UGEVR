using System;
using System.Collections;
using System.Reflection;

namespace Ugevr
{
    // Outer 1.4.1 compatibility is deliberately separate from the generic state policy.
    // The prefix is the inspected game's public-device-name compatibility rule, not hardware identity.
    public sealed class PublicOwnedGamepad : IOwnedGamepad
    {
        readonly string unavailable;
        readonly Type padType,stateType,descriptionType,vectorType;
        readonly PropertyInfo current,added,enabled,layout,description,serial,devices,south,stick,pressed;
        readonly MethodInfo register,load,removeLayout,add,remove,makeCurrent,queue,readVector,toJson;
        readonly FieldInfo buttons,leftStick,vx,vy;
        readonly EventInfo deviceChanges,layoutChanges;
        readonly string identity,layoutName;
        readonly uint southMask;
        Delegate deviceHandler,layoutHandler;
        object owned,prior;
        string definition;
        bool adding,registered,layoutConflict,currentConflict,subscribed,removed;
        public int AddFailures {get;private set;}
        public int RemoveFailures {get;private set;}
        public int LayoutRegistrationAttempts {get;private set;}
        public int LayoutRemovalAttempts {get;private set;}
        public string CurrentRestoration {get;private set;}
        public string UnavailableReason {get {return unavailable;}}
        public bool HasOwnedDevice {get {return owned!=null && !removed;}}
        public bool NeedsCleanup {get {return HasOwnedDevice || subscribed;}}
        public string CleanupStatus {get;private set;}
        PublicOwnedGamepad(string reason) {unavailable=reason;CleanupStatus="not_created";}
        internal PublicOwnedGamepad(Assembly assembly,bool pinnedTokens,string prefix)
        {
            unavailable="";CleanupStatus="not_created";
            identity="ugevr-synthetic-"+Guid.NewGuid().ToString("N");
            if(String.IsNullOrEmpty(prefix) || prefix.Length>64) throw new ArgumentException("Invalid owned layout prefix.");
            foreach(char c in prefix) if(!((c>='A' && c<='Z') || (c>='a' && c<='z') || (c>='0' && c<='9') || c=='_')) throw new ArgumentException("Invalid owned layout prefix.");
            layoutName=prefix+identity.Substring(16);
            padType=assembly.GetType("UnityEngine.InputSystem.Gamepad",true);
            stateType=assembly.GetType("UnityEngine.InputSystem.LowLevel.GamepadState",true);
            descriptionType=assembly.GetType("UnityEngine.InputSystem.Layouts.InputDeviceDescription",true);
            Type device=assembly.GetType("UnityEngine.InputSystem.InputDevice",true),hub=assembly.GetType("UnityEngine.InputSystem.InputSystem",true);
            Type button=assembly.GetType("UnityEngine.InputSystem.Controls.ButtonControl",true),stickType=assembly.GetType("UnityEngine.InputSystem.Controls.StickControl",true);
            current=Property(padType,"current",true,padType);added=Property(device,"added",false,typeof(bool));enabled=Property(device,"enabled",false,typeof(bool));
            layout=Property(device,"layout",false,typeof(string));description=Property(device,"description",false,descriptionType);serial=Property(descriptionType,"serial",false,typeof(string));
            devices=hub.GetProperty("devices",BindingFlags.Public|BindingFlags.Static);
            if(devices==null || !typeof(IEnumerable).IsAssignableFrom(devices.PropertyType)) throw new InvalidOperationException("Public device enumeration unavailable.");
            south=Property(padType,"buttonSouth",false,button);stick=Property(padType,"leftStick",false,stickType);pressed=Property(button,"isPressed",false,typeof(bool));
            buttons=Field(stateType,"buttons",typeof(uint),pinnedTokens?0x040004c8:0);
            leftStick=stateType.GetField("leftStick",BindingFlags.Public|BindingFlags.Instance);
            if(leftStick==null || (pinnedTokens && leftStick.MetadataToken!=0x040004c9)) throw new InvalidOperationException("Public GamepadState shape changed.");
            vectorType=leftStick.FieldType;if(vectorType.FullName!="UnityEngine.Vector2" || !vectorType.IsValueType) throw new InvalidOperationException("Public stick state type changed.");
            vx=Field(vectorType,"x",typeof(float),0);vy=Field(vectorType,"y",typeof(float),0);
            readVector=stickType.GetMethod("ReadUnprocessedValue",Type.EmptyTypes);
            if(readVector==null || !readVector.IsPublic || readVector.ReturnType!=vectorType || (pinnedTokens && readVector.MetadataToken!=0x060003c4)) throw new InvalidOperationException("Public stick read unavailable.");
            Type enumType=assembly.GetType("UnityEngine.InputSystem.LowLevel.GamepadButton",true);
            if(!enumType.IsEnum) throw new InvalidOperationException("Public button enum unavailable.");
            int bit=Convert.ToInt32(Enum.Parse(enumType,"South",false));if(bit<0 || bit>=32) throw new InvalidOperationException("Invalid South bit.");southMask=1u<<bit;
            Type matcher=assembly.GetType("UnityEngine.InputSystem.Layouts.InputDeviceMatcher",true);
            register=Method(hub,"RegisterLayout",new[]{typeof(string),typeof(string),typeof(Nullable<>).MakeGenericType(matcher)},typeof(void),true,pinnedTokens?0x0600031b:0);
            load=Method(hub,"LoadLayout",new[]{typeof(string)},assembly.GetType("UnityEngine.InputSystem.Layouts.InputControlLayout",true),true,pinnedTokens?0x06000325:0);
            toJson=Method(load.ReturnType,"ToJson",Type.EmptyTypes,typeof(string),false,pinnedTokens?0x06000ebc:0);
            removeLayout=Method(hub,"RemoveLayout",new[]{typeof(string)},typeof(void),true,pinnedTokens?0x06000321:0);
            add=Method(hub,"AddDevice",new[]{descriptionType},device,true,pinnedTokens?0x06000339:0);
            remove=Method(hub,"RemoveDevice",new[]{device},typeof(void),true,pinnedTokens?0x0600033b:0);
            makeCurrent=Method(padType,"MakeCurrent",Type.EmptyTypes,typeof(void),false,pinnedTokens?0x0600046d:0);
            MethodInfo generic=null;
            foreach(MethodInfo m in hub.GetMethods(BindingFlags.Public|BindingFlags.Static))
                if(m.Name=="QueueStateEvent" && m.IsGenericMethodDefinition && m.GetGenericArguments().Length==1 && (!pinnedTokens || m.MetadataToken==0x0600035d)) {generic=m;break;}
            if(generic==null || !stateType.IsValueType || !stateType.IsPublic) throw new InvalidOperationException("Public typed state queue unavailable.");
            queue=generic.MakeGenericMethod(stateType);var qp=queue.GetParameters();
            if(queue.ReturnType!=typeof(void) || qp.Length!=3 || qp[0].ParameterType!=device || qp[1].ParameterType!=stateType || qp[2].ParameterType!=typeof(double)) throw new InvalidOperationException("Public typed queue shape changed.");
            deviceChanges=hub.GetEvent("onDeviceChange",BindingFlags.Public|BindingFlags.Static);layoutChanges=hub.GetEvent("onLayoutChange",BindingFlags.Public|BindingFlags.Static);
            if(deviceChanges==null || layoutChanges==null) throw new InvalidOperationException("Public ownership observers unavailable.");
        }
        public static IOwnedGamepad Unavailable(string reason) {return new PublicOwnedGamepad(reason);}
        public static IOwnedGamepad Create(string prefix)
        {
            try
            {
                foreach(Assembly a in AppDomain.CurrentDomain.GetAssemblies()) if(a.GetName().Name=="Unity.InputSystem")
                {
                    if(!PublicKeyboardEReader.MatchesIdentity(a.GetName(),a.ManifestModule.ModuleVersionId)) return new PublicOwnedGamepad("assembly_identity_mismatch");
                    return new PublicOwnedGamepad(a,true,prefix);
                }
                return new PublicOwnedGamepad("assembly_not_loaded");
            }
            catch {return new PublicOwnedGamepad("public_api_shape_unavailable");}
        }
        static PropertyInfo Property(Type type,string name,bool isStatic,Type result)
        {
            var p=type.GetProperty(name,BindingFlags.Public|(isStatic?BindingFlags.Static:BindingFlags.Instance));
            if(p==null || p.PropertyType!=result || p.GetIndexParameters().Length!=0 || p.GetGetMethod(false)==null || p.GetGetMethod(false).IsStatic!=isStatic) throw new InvalidOperationException("Public property shape changed.");return p;
        }
        static FieldInfo Field(Type type,string name,Type result,int token)
        {
            var f=type.GetField(name,BindingFlags.Public|BindingFlags.Instance);if(f==null || f.FieldType!=result || (token!=0 && f.MetadataToken!=token)) throw new InvalidOperationException("Public state field changed.");return f;
        }
        static MethodInfo Method(Type type,string name,Type[] parameters,Type result,bool isStatic,int token)
        {
            var m=type.GetMethod(name,BindingFlags.Public|(isStatic?BindingFlags.Static:BindingFlags.Instance),null,parameters,null);
            if(m==null || m.ReturnType!=result || m.IsStatic!=isStatic || (token!=0 && m.MetadataToken!=token)) throw new InvalidOperationException("Public method shape changed.");return m;
        }
        object Current() {return current.GetValue(null,null);}
        bool Added(object d) {return d!=null && (bool)added.GetValue(d,null);}
        string LayoutDefinition()
        {
            var value=load.Invoke(null,new object[]{layoutName});return value==null?null:(string)toJson.Invoke(value,null);
        }
        void Subscribe()
        {
            var args=deviceChanges.EventHandlerType.GetGenericArguments();
            var callback=GetType().GetMethod("DeviceChanged",BindingFlags.NonPublic|BindingFlags.Instance).MakeGenericMethod(args);
            deviceHandler=Delegate.CreateDelegate(deviceChanges.EventHandlerType,this,callback);
            var change=layoutChanges.EventHandlerType.GetGenericArguments()[1];
            callback=GetType().GetMethod("LayoutChanged",BindingFlags.NonPublic|BindingFlags.Instance).MakeGenericMethod(change);
            layoutHandler=Delegate.CreateDelegate(layoutChanges.EventHandlerType,this,callback);
            deviceChanges.AddEventHandler(null,deviceHandler);subscribed=true;layoutChanges.AddEventHandler(null,layoutHandler);
        }
        void DeviceChanged<TDevice,TChange>(TDevice d,TChange change)
        {
            try
            {
                object value=d;
                if(adding && change.ToString()=="Added" && value!=null && padType.IsInstanceOfType(value) &&
                    (string)layout.GetValue(value,null)==layoutName && (string)serial.GetValue(description.GetValue(value,null),null)==identity)
                {
                    if(owned!=null && !Object.ReferenceEquals(owned,value)) {layoutConflict=true;return;}
                    // Captured before AddDevice returns, including add-then-throw paths.
                    owned=value;
                }
            }
            catch {layoutConflict=true;}
        }
        void LayoutChanged<TChange>(string name,TChange change)
        {
            if(name==layoutName && change.ToString()!="Added") layoutConflict=true;
        }
        void SetDescription(object value,string name,string text)
        {
            var p=descriptionType.GetProperty(name,BindingFlags.Public|BindingFlags.Instance);
            if(p==null || p.PropertyType!=typeof(string) || p.GetSetMethod(false)==null) throw new InvalidOperationException("Public description setter unavailable.");p.SetValue(value,text,null);
        }
        public void Add(bool preferOwned)
        {
            if(unavailable.Length!=0 || registered || owned!=null) throw new InvalidOperationException("Device creation unavailable or already attempted.");
            prior=Current();if(prior!=null && !preferOwned) {CleanupStatus="refused_existing_current_gamepad";throw new InvalidOperationException("Existing current gamepad requires explicit prefer-owned policy.");}
            if(LayoutDefinition()!=null) {CleanupStatus="refused_unowned_layout_collision";throw new InvalidOperationException("Unowned layout collision.");}
            Subscribe();
            try
            {
                // Public matcher JSON has no serial field. Use a unique synthetic interface per creation.
                string json="{\"name\":\""+layoutName+"\",\"extend\":\"Gamepad\",\"displayName\":\"UGEVR synthetic diagnostic gamepad\",\"device\":{\"interface\":\"UGEVRVirtualGamepad_"+identity+"\",\"manufacturer\":\"UGEVR\"}}";
                ++LayoutRegistrationAttempts;registered=true;register.Invoke(null,new object[]{json,layoutName,null});
                definition=LayoutDefinition();if(definition==null || layoutConflict) throw new InvalidOperationException("Layout registration changed externally.");
                object desc=Activator.CreateInstance(descriptionType);
                SetDescription(desc,"interfaceName","UGEVRVirtualGamepad_"+identity);SetDescription(desc,"manufacturer","UGEVR");SetDescription(desc,"product","UGEVR synthetic diagnostic gamepad");SetDescription(desc,"serial",identity);SetDescription(desc,"capabilities","{}");
                adding=true;object result=add.Invoke(null,new[]{desc});adding=false;
                if(result==null || !padType.IsInstanceOfType(result) || (owned!=null && !Object.ReferenceEquals(result,owned))) throw new InvalidOperationException("Created device identity mismatch.");
                owned=result;
                if(!Added(owned) || !(bool)enabled.GetValue(owned,null) || (string)layout.GetValue(owned,null)!=layoutName) throw new InvalidOperationException("Game rejected the owned device.");
                object actual=Current();
                if(actual!=null && !Object.ReferenceEquals(actual,owned) && !Object.ReferenceEquals(actual,prior)) {currentConflict=true;throw new InvalidOperationException("External current gamepad changed during add.");}
                makeCurrent.Invoke(owned,null);CleanupStatus="owned_device_added";
            }
            catch
            {
                adding=false;++AddFailures;CleanupStatus=owned==null?"add_outcome_unknown":"owned_add_failed";
                // The known unique registration can have completed before a public callback throws.
                // Preserve any observed replacement; never adopt its definition for deletion.
                if(registered && definition==null && !layoutConflict)
                    try {definition=LayoutDefinition();if(definition==null) registered=false;} catch { }
                throw;
            }
        }
        public OwnedPadState Read()
        {
            var s=new OwnedPadState();if(!HasOwnedDevice) return s;
            s.Added=Added(owned);if(!s.Added) return s;
            s.Enabled=(bool)enabled.GetValue(owned,null);s.Current=Object.ReferenceEquals(Current(),owned);
            if(!s.Current) currentConflict=true;
            object button=south.GetValue(owned,null),axis=stick.GetValue(owned,null),v=readVector.Invoke(axis,null);
            s.South=(bool)pressed.GetValue(button,null);s.X=(float)vx.GetValue(v);s.Y=(float)vy.GetValue(v);return s;
        }
        public void Queue(bool down,double x,double y)
        {
            if(!HasOwnedDevice || !Added(owned) || layoutConflict || LayoutDefinition()!=definition) throw new InvalidOperationException("Owned device or layout changed.");
            if(Double.IsNaN(x) || Double.IsInfinity(x) || Double.IsNaN(y) || Double.IsInfinity(y) || Math.Abs(x)>1 || Math.Abs(y)>1) throw new ArgumentException("Invalid stick state.");
            bool neutral=!down && x==0 && y==0;
            if(!Object.ReferenceEquals(Current(),owned)) currentConflict=true;
            if(!neutral && (!(bool)enabled.GetValue(owned,null) || !Object.ReferenceEquals(Current(),owned))) {currentConflict=true;throw new InvalidOperationException("Current gamepad ownership changed.");}
            object state=Activator.CreateInstance(stateType),v=Activator.CreateInstance(vectorType);buttons.SetValue(state,down?southMask:0u);vx.SetValue(v,(float)x);vy.SetValue(v,(float)y);leftStick.SetValue(state,v);
            queue.Invoke(null,new[]{owned,state,(object)(-1.0)});
        }
        public void Remove()
        {
            try
            {
                bool wasCurrent=owned!=null && Object.ReferenceEquals(Current(),owned);
                if(owned!=null && Added(owned)) remove.Invoke(null,new[]{owned});
                if(owned!=null && Added(owned)) throw new InvalidOperationException("Owned removal not confirmed.");
                removed=true;
                // Gamepad.OnRemoved clears current only if it was this device. Never overwrite another current.
                CurrentRestoration="not_required";
                if(wasCurrent && !currentConflict && Current()==null && prior!=null && Added(prior) && (bool)enabled.GetValue(prior,null))
                {makeCurrent.Invoke(prior,null);CurrentRestoration=Object.ReferenceEquals(Current(),prior)?"prior_restored":"external_current_preserved";}
                else if(currentConflict || Current()!=null) CurrentRestoration="external_current_preserved";
                else if(prior!=null) CurrentRestoration="prior_unavailable";
                bool other=false;int count=0;
                foreach(object d in (IEnumerable)devices.GetValue(null,null))
                {
                    if(++count>256) {other=true;break;}
                    if((string)layout.GetValue(d,null)==layoutName) {other=true;break;}
                }
                if(registered && !layoutConflict && !other && definition!=null && LayoutDefinition()==definition) {++LayoutRemovalAttempts;removeLayout.Invoke(null,new object[]{layoutName});registered=false;}
                CleanupStatus=(registered?"device_removed_layout_preserved":"owned_device_and_layout_removed")+";current="+CurrentRestoration+
                    ";addFailures="+AddFailures+";removeFailures="+RemoveFailures+";layoutRegisters="+LayoutRegistrationAttempts+";layoutRemoves="+LayoutRemovalAttempts;
                Unsubscribe();
            }
            catch {++RemoveFailures;CleanupStatus="cleanup_unconfirmed";throw;}
        }
        void Unsubscribe()
        {
            if(!subscribed) return;
            deviceChanges.RemoveEventHandler(null,deviceHandler);layoutChanges.RemoveEventHandler(null,layoutHandler);subscribed=false;
        }
    }
}
