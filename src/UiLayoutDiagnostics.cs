using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Ugevr
{
    // Explicit diagnostic only. Read cached standard-engine inputs; never ask
    // layout or text generators to compute preferred sizes or regenerate meshes.
    public sealed class UiLayoutDiagnostics
    {
        const int NodeLimit=8192,ComponentLimit=1024,ComponentsPerNode=32,AncestorLimit=8;
        static readonly string[] Phases={"snapshot","applied-left","after-render-left","applied-right","after-render-right","direct-restoration","camera-restored-before-rebuild","final-rebuild"};
        readonly Dictionary<int,Node> nodes=new Dictionary<int,Node>();
        readonly List<Entry> entries=new List<Entry>();
        readonly Dictionary<Type,PropertyInfo[]> properties=new Dictionary<Type,PropertyInfo[]>();
        int droppedNodes,droppedComponents,queryErrors;
        sealed class Node {public int Id,Parent;public string Name;public string[] Types;public readonly List<Entry> Entries=new List<Entry>();}
        sealed class Entry {public Component Component;public RectTransform Rect;public Canvas Root;public string Type;public int Id;public PropertyInfo[] Properties;public string[] Names;public readonly Sample[] Samples=new Sample[8];}
        sealed class Sample {public bool Destroyed;public object[] Values;public Vector2 Size;public Vector3 Anchored;public float CanvasScale;public string CanvasMode;}
        sealed class Asset {public int Id;}
        sealed class Unavailable {public string Reason;}
        public int ComponentCount {get {return entries.Count;}}

        public void Add(RectTransform rect,Canvas root)
        {
            if(rect==null || nodes.ContainsKey(rect.GetInstanceID())) return;
            if(nodes.Count>=NodeLimit) {++droppedNodes;return;}
            var node=new Node {Id=rect.GetInstanceID(),Parent=rect.parent==null?0:rect.parent.GetInstanceID(),Name=rect.name};
            nodes.Add(node.Id,node);
            try
            {
                Component[] components=rect.GetComponents<Component>();
                var types=new List<string>();
                for(int i=0;i<components.Length;++i)
                {
                    if(i>=ComponentsPerNode) {droppedComponents+=components.Length-i;break;}
                    Component component=components[i];if(component==null) continue;
                    Type type=component.GetType();types.Add(type.FullName);
                    string[] names=InputNames(type);if(names==null) continue;
                    if(entries.Count>=ComponentLimit) {++droppedComponents;continue;}
                    PropertyInfo[] selected;
                    if(!properties.TryGetValue(type,out selected))
                    {
                        selected=new PropertyInfo[names.Length];
                        for(int j=0;j<names.Length;++j)
                        {
                            PropertyInfo candidate=type.GetProperty(names[j],BindingFlags.Public|BindingFlags.Instance);
                            if(candidate!=null && candidate.GetIndexParameters().Length==0 && candidate.CanRead && TrustedAssembly(candidate.DeclaringType.Assembly.GetName().Name)) selected[j]=candidate;
                        }
                        properties.Add(type,selected);
                    }
                    var entry=new Entry {Component=component,Rect=rect,Root=root,Type=type.FullName,Id=component.GetInstanceID(),Names=names,Properties=selected};
                    entries.Add(entry);node.Entries.Add(entry);
                }
                node.Types=types.ToArray();
            }
            catch {++queryErrors;node.Types=new string[0];}
        }
        static bool TrustedAssembly(string name)
        {
            return name=="UnityEngine" || name=="UnityEngine.CoreModule" || name=="UnityEngine.UI" || name=="Unity.TextMeshPro" || name=="TextMeshPro";
        }
        static string[] InputNames(Type type)
        {
            string assembly=type.Assembly.GetName().Name;
            if(type.FullName=="UnityEngine.UI.Text" && assembly=="UnityEngine.UI")
                return new[]{"enabled","text","font","fontSize","fontStyle","alignment","supportRichText","resizeTextForBestFit","resizeTextMinSize","resizeTextMaxSize","horizontalOverflow","verticalOverflow","lineSpacing"};
            if((type.FullName=="TMPro.TextMeshProUGUI" || type.FullName=="TMPro.TextMeshPro") && (assembly=="Unity.TextMeshPro" || assembly=="TextMeshPro"))
                return new[]{"enabled","text","font","fontSize","fontStyle","alignment","enableAutoSizing","fontSizeMin","fontSizeMax","enableWordWrapping","overflowMode","characterSpacing","wordSpacing","lineSpacing"};
            if(assembly!="UnityEngine.UI") return null;
            switch(type.FullName)
            {
                case "UnityEngine.UI.HorizontalLayoutGroup":case "UnityEngine.UI.VerticalLayoutGroup":
                    return new[]{"enabled","padding","childAlignment","spacing","childControlWidth","childControlHeight","childForceExpandWidth","childForceExpandHeight","childScaleWidth","childScaleHeight"};
                case "UnityEngine.UI.GridLayoutGroup":return new[]{"enabled","padding","childAlignment","cellSize","spacing","startCorner","startAxis","constraint","constraintCount"};
                case "UnityEngine.UI.ContentSizeFitter":return new[]{"enabled","horizontalFit","verticalFit"};
                case "UnityEngine.UI.AspectRatioFitter":return new[]{"enabled","aspectMode","aspectRatio"};
            }
            return null;
        }
        public void Capture(int phase)
        {
            if(phase<0 || phase>=Phases.Length) throw new ArgumentOutOfRangeException("phase");
            foreach(Entry entry in entries)
            {
                var sample=new Sample {Destroyed=entry.Component==null || entry.Rect==null};entry.Samples[phase]=sample;
                if(sample.Destroyed) continue;
                sample.Values=new object[entry.Names.Length];
                for(int i=0;i<sample.Values.Length;++i)
                {
                    try {sample.Values[i]=entry.Properties[i]==null?new Unavailable {Reason="property absent"}:Freeze(entry.Properties[i].GetValue(entry.Component,null));}
                    catch {++queryErrors;sample.Values[i]=new Unavailable {Reason="cached input query failed"};}
                }
                try
                {
                    sample.Size=entry.Rect.sizeDelta;sample.Anchored=entry.Rect.anchoredPosition3D;
                    sample.CanvasMode=entry.Root==null?null:entry.Root.renderMode.ToString();sample.CanvasScale=entry.Root==null?0:entry.Root.scaleFactor;
                }
                catch {++queryErrors;}
            }
        }
        static object Freeze(object value)
        {
            var asset=value as UnityEngine.Object;if(value is UnityEngine.Object) return asset==null?null:(object)new Asset {Id=asset.GetInstanceID()};
            if(value==null || value is string || value is bool || value is int || value is float || value is double || value.GetType().IsEnum || value is Vector2) return value;
            if(value.GetType().FullName=="UnityEngine.RectOffset" && TrustedAssembly(value.GetType().Assembly.GetName().Name))
            {
                var parts=new int[4];string[] names={"left","right","top","bottom"};
                for(int i=0;i<parts.Length;++i) parts[i]=(int)value.GetType().GetProperty(names[i]).GetValue(value,null);
                return parts;
            }
            return new Unavailable {Reason="unsupported cached value type"};
        }
        static string Quote(string value)
        {
            if(value==null) return "null";if(value.Length>128) value=value.Substring(0,128);
            var text=new StringBuilder("\"");
            foreach(char c in value) {if(c=='\"' || c=='\\') text.Append('\\').Append(c);else if(c<32) text.Append("\\u").Append(((int)c).ToString("x4"));else text.Append(c);}
            return text.Append('"').ToString();
        }
        static string Number(float value) {return Single.IsNaN(value) || Single.IsInfinity(value)?"null":value.ToString("R",CultureInfo.InvariantCulture);}
        static string Value(object value,string property)
        {
            if(value==null) return "null";
            var unavailable=value as Unavailable;if(unavailable!=null) return "{\"unavailable\":"+Quote(unavailable.Reason)+"}";
            var asset=value as Asset;if(asset!=null) return "{\"assetInstanceId\":"+asset.Id+"}";
            if(property=="text" && value is string)
            {
                string content=(string)value;int length=Math.Min(content.Length,65536);
                byte[] hash;using(var algorithm=SHA256.Create()) hash=algorithm.ComputeHash(Encoding.UTF8.GetBytes(content.Substring(0,length)));
                var hex=new StringBuilder();foreach(byte b in hash) hex.Append(b.ToString("x2",CultureInfo.InvariantCulture));
                return "{\"length\":"+content.Length+",\"sha256\":"+Quote(hex.ToString())+",\"hashedUtf16Length\":"+length+",\"truncated\":"+(length<content.Length?"true":"false")+"}";
            }
            if(value is bool) return (bool)value?"true":"false";
            if(value is float) return Number((float)value);
            if(value is double) {double number=(double)value;return Double.IsNaN(number) || Double.IsInfinity(number)?"null":number.ToString("R",CultureInfo.InvariantCulture);}
            if(value is int) return ((int)value).ToString(CultureInfo.InvariantCulture);
            if(value is Vector2) {var vector=(Vector2)value;return "["+Number(vector.x)+","+Number(vector.y)+"]";}
            if(value is int[]) return "["+String.Join(",",Array.ConvertAll((int[])value,v=>v.ToString(CultureInfo.InvariantCulture)))+"]";
            return Quote(Convert.ToString(value,CultureInfo.InvariantCulture));
        }
        public string Context(int rectId)
        {
            var text=new StringBuilder("{\"requestedRectId\":").Append(rectId).Append(",\"ancestorLimit\":").Append(AncestorLimit).Append(",\"nodes\":[");
            var seen=new HashSet<int>();Node node;int count=0;
            while(count<AncestorLimit && nodes.TryGetValue(rectId,out node) && seen.Add(rectId))
            {
                if(count++>0) text.Append(',');text.Append("{\"rectId\":").Append(node.Id).Append(",\"name\":").Append(Quote(node.Name)).Append(",\"parentRectId\":").Append(node.Parent).Append(",\"componentTypes\":[");
                for(int i=0;i<node.Types.Length;++i) {if(i>0) text.Append(',');text.Append(Quote(node.Types[i]));}
                text.Append("],\"knownEngineInputs\":[");
                for(int i=0;i<node.Entries.Count;++i)
                {
                    if(i>0) text.Append(',');Entry entry=node.Entries[i];text.Append("{\"componentId\":").Append(entry.Id).Append(",\"type\":").Append(Quote(entry.Type)).Append(",\"phases\":{");
                    for(int p=0;p<Phases.Length;++p)
                    {
                        if(p>0) text.Append(',');text.Append(Quote(Phases[p])).Append(':');Sample sample=entry.Samples[p];
                        if(sample==null) {text.Append("null");continue;}
                        text.Append("{\"destroyed\":").Append(sample.Destroyed?"true":"false");
                        if(!sample.Destroyed)
                        {
                            text.Append(",\"canvasMode\":").Append(Quote(sample.CanvasMode)).Append(",\"canvasScaleFactor\":").Append(Number(sample.CanvasScale))
                                .Append(",\"sizeDelta\":[").Append(Number(sample.Size.x)).Append(',').Append(Number(sample.Size.y)).Append(']')
                                .Append(",\"anchoredPosition3D\":[").Append(Number(sample.Anchored.x)).Append(',').Append(Number(sample.Anchored.y)).Append(',').Append(Number(sample.Anchored.z)).Append(']')
                                .Append(",\"inputs\":{");
                            for(int j=0;j<entry.Names.Length;++j) {if(j>0) text.Append(',');text.Append(Quote(entry.Names[j])).Append(':').Append(Value(sample.Values[j],entry.Names[j]));}
                            text.Append('}');
                        }
                        text.Append('}');
                    }
                    text.Append("}}");
                }
                text.Append("]}");rectId=node.Parent;
            }
            return text.Append("],\"remainingAncestorId\":").Append(rectId).Append('}').ToString();
        }
        public string Summary()
        {
            return "{\"nodeLimit\":"+NodeLimit+",\"componentLimit\":"+ComponentLimit+",\"componentsPerNodeLimit\":"+ComponentsPerNode+",\"nodeCount\":"+nodes.Count+",\"knownInputComponentCount\":"+entries.Count+",\"droppedNodes\":"+droppedNodes+",\"droppedComponents\":"+droppedComponents+",\"queryErrors\":"+queryErrors+"}";
        }
    }
}
