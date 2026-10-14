using System;
using System.Collections.Generic;
using System.Reflection;

namespace Ugevr
{
    public sealed class PublicEngineEKeyboard : IEngineEKeyboard
    {
        readonly string unavailable;
        readonly object original,eCode,imeCode;
        readonly Type keyType;
        readonly PropertyInfo current,eKey,allKeys,enabled,added,imeSelected,code,down,press,release,count,item;
        readonly ConstructorInfo stateConstructor;
        readonly MethodInfo queue;
        public string UnavailableReason {get {return unavailable;}}
        PublicEngineEKeyboard(string reason) {unavailable=reason;}
        PublicEngineEKeyboard(Assembly assembly)
        {
            Type keyboard=assembly.GetType("UnityEngine.InputSystem.Keyboard",true);
            Type device=assembly.GetType("UnityEngine.InputSystem.InputDevice",true);
            Type key=assembly.GetType("UnityEngine.InputSystem.Controls.KeyControl",true);
            Type button=assembly.GetType("UnityEngine.InputSystem.Controls.ButtonControl",true);
            Type state=assembly.GetType("UnityEngine.InputSystem.LowLevel.KeyboardState",true);
            Type hub=assembly.GetType("UnityEngine.InputSystem.InputSystem",true);
            keyType=assembly.GetType("UnityEngine.InputSystem.Key",true);
            if(!keyType.IsEnum || Enum.GetUnderlyingType(keyType)!=typeof(int) || !state.IsValueType) throw new InvalidOperationException("Unexpected keyboard state shape.");
            eCode=Enum.Parse(keyType,"E",false);imeCode=Enum.Parse(keyType,"IMESelected",false);
            ValidateCode(eCode);ValidateCode(imeCode);
            Type collection=assembly.GetType("UnityEngine.InputSystem.Utilities.ReadOnlyArray`1",true).MakeGenericType(key);
            current=Getter(keyboard,"current",keyboard,true,0x06000544);
            eKey=Getter(keyboard,"eKey",key,false,0x060004d8);
            allKeys=Getter(keyboard,"allKeys",collection,false,0x06000543);
            enabled=Getter(device,"enabled",typeof(bool),false,0x06000476);
            added=Getter(device,"added",typeof(bool),false,0x06000478);
            imeSelected=Getter(keyboard,"imeSelected",button,false,0x06000540);
            code=Getter(key,"keyCode",keyType,false,0x06000f5c);
            down=Getter(button,"isPressed",typeof(bool),false,0x06000f37);
            press=Getter(button,"wasPressedThisFrame",typeof(bool),false,0x06000f38);
            release=Getter(button,"wasReleasedThisFrame",typeof(bool),false,0x06000f39);
            count=Getter(collection,"Count",typeof(int),false,0x06001117);
            item=collection.GetProperty("Item",BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if(item==null || item.PropertyType!=key || item.GetIndexParameters().Length!=1 || item.GetIndexParameters()[0].ParameterType!=typeof(int) || item.GetGetMethod(false)==null || item.GetGetMethod(false).MetadataToken!=0x06001118)
                throw new InvalidOperationException("Unexpected public key collection shape.");
            stateConstructor=state.GetConstructor(new[]{keyType.MakeArrayType()});
            if(stateConstructor==null || stateConstructor.MetadataToken!=0x06000c86) throw new InvalidOperationException("Unexpected public keyboard state constructor.");
            MethodInfo generic=assembly.ManifestModule.ResolveMethod(0x0600035d) as MethodInfo;
            if(generic==null || generic.DeclaringType!=hub || generic.Name!="QueueStateEvent" || !generic.IsPublic || !generic.IsStatic || !generic.IsGenericMethodDefinition || generic.GetGenericArguments().Length!=1 || generic.ReturnType!=typeof(void))
                throw new InvalidOperationException("Unexpected public state queue.");
            queue=generic.MakeGenericMethod(state);ParameterInfo[] parameters=queue.GetParameters();
            if(parameters.Length!=3 || parameters[0].ParameterType!=device || parameters[1].ParameterType!=state || parameters[2].ParameterType!=typeof(double)) throw new InvalidOperationException("Unexpected public state queue parameters.");
            original=current.GetValue(null,null);
            unavailable=original==null?"keyboard_not_present":"";
        }
        static PropertyInfo Getter(Type type,string name,Type result,bool isStatic,int token)
        {
            PropertyInfo property=type.GetProperty(name,BindingFlags.Public | BindingFlags.DeclaredOnly | (isStatic?BindingFlags.Static:BindingFlags.Instance));
            if(property==null || property.PropertyType!=result || property.GetIndexParameters().Length!=0) throw new InvalidOperationException("Unexpected public engine input property.");
            MethodInfo getter=property.GetGetMethod(false);
            if(getter==null || getter.IsStatic!=isStatic || (getter.IsVirtual && !getter.IsFinal) || (token!=0 && getter.MetadataToken!=token)) throw new InvalidOperationException("Unexpected public engine input getter.");
            return property;
        }
        public static IEngineEKeyboard Create()
        {
            try
            {
                foreach(Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if(assembly.GetName().Name!="Unity.InputSystem") continue;
                    if(!PublicKeyboardEReader.MatchesIdentity(assembly.GetName(),assembly.ManifestModule.ModuleVersionId)) return new PublicEngineEKeyboard("assembly_identity_mismatch");
                    return new PublicEngineEKeyboard(assembly);
                }
                return new PublicEngineEKeyboard("assembly_not_loaded");
            }
            catch {return new PublicEngineEKeyboard("public_api_shape_unavailable");}
        }
        void ValidateCode(object value)
        {
            int numeric=Convert.ToInt32(value,System.Globalization.CultureInfo.InvariantCulture);
            int maximum=Convert.ToInt32(imeCode,System.Globalization.CultureInfo.InvariantCulture);
            if(maximum<=0 || maximum>=256 || numeric<=0 || numeric>maximum || !Enum.IsDefined(keyType,value)) throw new InvalidOperationException("Unexpected key state bit.");
        }
        List<object> HeldNonE(out int controls,out int heldCount)
        {
            object collection=allKeys.GetValue(original,null);int keys=(int)count.GetValue(collection,null);
            if(keys<=0 || keys>=256) throw new InvalidOperationException("Key control bound exceeded.");
            controls=keys+1;heldCount=0;
            var held=new List<object>();var codes=new HashSet<int>();
            for(int i=0;i<keys;++i)
            {
                object control=item.GetValue(collection,new object[]{i});
                if(control==null) throw new InvalidOperationException("Key control is unavailable.");
                object value=code.GetValue(control,null);ValidateCode(value);
                if(value.Equals(imeCode)) throw new InvalidOperationException("IME state is outside the key collection.");
                if(!codes.Add(Convert.ToInt32(value,System.Globalization.CultureInfo.InvariantCulture))) throw new InvalidOperationException("Duplicate key state bit.");
                if(!(bool)down.GetValue(control,null)) continue;
                ++heldCount;if(!value.Equals(eCode)) held.Add(value);
            }
            object ime=imeSelected.GetValue(original,null);
            if(ime==null) throw new InvalidOperationException("IME state control is unavailable.");
            if((bool)down.GetValue(ime,null)) {++heldCount;held.Add(imeCode);}
            return held;
        }
        public EngineESnapshot Read()
        {
            var sample=new EngineESnapshot();if(unavailable.Length!=0 || original==null) return sample;
            sample.Added=(bool)added.GetValue(original,null);if(!sample.Added) return sample;
            sample.Enabled=(bool)enabled.GetValue(original,null);sample.OriginalIsCurrent=Object.ReferenceEquals(original,current.GetValue(null,null));
            int controls,heldCount;List<object> other=HeldNonE(out controls,out heldCount);sample.ControlCount=controls;sample.OtherHeldCount=other.Count;
            object e=eKey.GetValue(original,null);if(e==null) throw new InvalidOperationException("E control unavailable.");
            if(!eCode.Equals(code.GetValue(e,null))) throw new InvalidOperationException("Unexpected E state bit.");
            sample.Down=(bool)down.GetValue(e,null);sample.Press=(bool)press.GetValue(e,null);sample.Release=(bool)release.GetValue(e,null);
            // Reject altered layouts that conceal a held control behind an E alias.
            if(heldCount>other.Count && !sample.Down) throw new InvalidOperationException("Unexpected E state alias.");
            return sample;
        }
        public int Queue(bool eDown)
        {
            if(original==null || !(bool)added.GetValue(original,null)) throw new InvalidOperationException("Original keyboard removed.");
            int controls,heldCount;List<object> other=HeldNonE(out controls,out heldCount);
            if(eDown)
            {
                if(!(bool)enabled.GetValue(original,null) || !Object.ReferenceEquals(original,current.GetValue(null,null)) || heldCount!=0) throw new InvalidOperationException("Keyboard baseline changed before queue.");
                other.Add(eCode);
            }
            Array keys=Array.CreateInstance(keyType,other.Count);for(int i=0;i<other.Count;++i) keys.SetValue(other[i],i);
            object state=stateConstructor.Invoke(new object[]{keys});queue.Invoke(null,new object[]{original,state,-1.0});
            return eDown?0:other.Count;
        }
    }
}
