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
        const int SelectorRootLimit=16,FilteredSubtreeLimit=256,MetadataCacheLimit=256,OutputContextLimit=64;
        static readonly string[] Phases={"snapshot","applied-left","after-render-left","applied-right","after-render-right","direct-restoration","camera-restored-before-rebuild","final-rebuild"};
        readonly Dictionary<int,Node> nodes=new Dictionary<int,Node>();
        readonly List<Node> snapshotNodes=new List<Node>();
        readonly List<Entry> entries=new List<Entry>();
        static readonly Dictionary<Type,Schema> metadata=new Dictionary<Type,Schema>();
        static readonly object metadataLock=new object();
        static readonly Unavailable absentProperty=new Unavailable {Reason="property absent"};
        readonly Dictionary<int,List<Node>> children=new Dictionary<int,List<Node>>();
        readonly List<int> requestedRoots=new List<int>();
        readonly HashSet<int> admitted=new HashSet<int>();
        readonly Dictionary<int,int> rootSubtreeCounts=new Dictionary<int,int>();
        int droppedNodes,droppedComponents,queryErrors,inventoryQueries,getterReads,metadataHits,metadataMisses,filteredSubtreeDrops;
        string selectorError;
        bool admissionFinished;
        public bool Filtered {get;private set;}
        sealed class Schema {public string[] Names;public PropertyInfo[] Properties;}
        sealed class Node {public int Id,Parent;public string Name;public Transform ParentTransform;public RectTransform Rect;public Canvas Root;public string[] Types=new string[0];public readonly List<Entry> Entries=new List<Entry>();}
        sealed class Entry {public Component Component;public RectTransform Rect;public Canvas Root;public string Type;public int Id;public PropertyInfo[] Properties;public string[] Names;public readonly Sample[] Samples=new Sample[8];}
        sealed class Sample {public bool Destroyed;public object[] Values;public Vector2 Size;public Vector3 Anchored;public float CanvasScale;public string CanvasMode;}
        sealed class Asset {public int Id;}
        sealed class Unavailable {public string Reason;}
        public int ComponentCount {get {return entries.Count;}}

        public UiLayoutDiagnostics(string selector=null)
        {
            if(String.IsNullOrEmpty(selector)) return;
            if(selector.Length>256) {Filtered=true;selectorError="Root selector exceeds 256 characters.";return;}
            if(selector.Trim().Length==0) return;
            Filtered=true;
            string[] parts=selector.Split(',');
            if(parts.Length>SelectorRootLimit) {selectorError="Too many root IDs; the diagnostic accepts at most sixteen.";return;}
            foreach(string part in parts)
            {
                int id;
                if(!Int32.TryParse(part.Trim(),NumberStyles.Integer,CultureInfo.InvariantCulture,out id) || id==0 || requestedRoots.Contains(id))
                {selectorError="Root IDs must be distinct nonzero signed decimal integers.";requestedRoots.Clear();return;}
                requestedRoots.Add(id);
            }
        }

        public void RegisterSnapshot(RectTransform rect,Canvas root,int id,string name)
        {
            if(rect==null || nodes.ContainsKey(id)) return;
            if(nodes.Count>=NodeLimit) {++droppedNodes;return;}
            Transform parent=rect.parent;
            var node=new Node {Id=id,Parent=parent==null?0:parent.GetInstanceID(),ParentTransform=parent,Name=name,Rect=rect,Root=root};
            nodes.Add(id,node);
            snapshotNodes.Add(node);
            List<Node> siblings;
            if(!children.TryGetValue(node.Parent,out siblings)) {siblings=new List<Node>();children.Add(node.Parent,siblings);}
            siblings.Add(node);
        }
        public void FinishAdmission()
        {
            if(admissionFinished) return;admissionFinished=true;
            if(selectorError!=null) return;
            if(!Filtered) foreach(Node node in snapshotNodes) Admit(node);
            else
            {
                foreach(int rootId in requestedRoots)
                {
                    Node root;if(!nodes.TryGetValue(rootId,out root)) continue;
                    var queue=new Queue<Node>();var visited=new HashSet<int>();queue.Enqueue(root);int count=0;
                    while(queue.Count>0)
                    {
                        Node node=queue.Dequeue();if(!visited.Add(node.Id)) continue;
                        if(count>=FilteredSubtreeLimit) {++filteredSubtreeDrops;continue;}
                        ++count;Admit(node);
                        List<Node> descendants;if(children.TryGetValue(node.Id,out descendants)) foreach(Node child in descendants) queue.Enqueue(child);
                    }
                    rootSubtreeCounts[rootId]=count;
                    int parent=root.Parent;
                    for(int depth=0;depth<AncestorLimit && nodes.TryGetValue(parent,out root);++depth) {Admit(root);parent=root.Parent;}
                }
            }
        }

        public void Add(RectTransform rect,Canvas root)
        {
            if(rect==null) return;
            RegisterSnapshot(rect,root,rect.GetInstanceID(),rect.name);
            if(!Filtered) {Node node;if(nodes.TryGetValue(rect.GetInstanceID(),out node)) Admit(node);}
        }
        void Admit(Node node)
        {
            if(!admitted.Add(node.Id) || node.Rect==null) return;
            try
            {
                ++inventoryQueries;Component[] components=node.Rect.GetComponents<Component>();
                var types=new List<string>();
                for(int i=0;i<components.Length;++i)
                {
                    if(i>=ComponentsPerNode) {droppedComponents+=components.Length-i;break;}
                    Component component=components[i];if(component==null) continue;
                    Type type=component.GetType();types.Add(type.FullName);
                    Schema schema=SchemaFor(type);if(schema==null) continue;
                    if(entries.Count>=ComponentLimit) {++droppedComponents;continue;}
                    var entry=new Entry {Component=component,Rect=node.Rect,Root=node.Root,Type=type.FullName,Id=component.GetInstanceID(),Names=schema.Names,Properties=schema.Properties};
                    entries.Add(entry);node.Entries.Add(entry);
                }
                node.Types=types.ToArray();
            }
            catch {++queryErrors;node.Types=new string[0];}
        }
        Schema SchemaFor(Type type)
        {
            Schema schema;
            lock(metadataLock) if(metadata.TryGetValue(type,out schema)) {++metadataHits;return schema;}
            ++metadataMisses;string[] names=InputNames(type);schema=null;
            if(names!=null)
            {
                var selected=new PropertyInfo[names.Length];
                for(int j=0;j<names.Length;++j)
                {
                    PropertyInfo candidate=type.GetProperty(names[j],BindingFlags.Public|BindingFlags.Instance);
                    if(candidate!=null && candidate.GetIndexParameters().Length==0 && candidate.CanRead && TrustedAssembly(candidate.DeclaringType.Assembly.GetName().Name)) selected[j]=candidate;
                }
                schema=new Schema {Names=names,Properties=selected};
            }
            lock(metadataLock) if(metadata.Count<MetadataCacheLimit && !metadata.ContainsKey(type)) metadata.Add(type,schema);
            return schema;
        }
        static bool TrustedAssembly(string name)
        {
            return name=="UnityEngine" || name=="UnityEngine.CoreModule" || name=="UnityEngine.UI" || name=="Unity.TextMeshPro" || name=="TextMeshPro";
        }
        static string[] InputNames(Type type)
        {
            string name=type.FullName;
            if(name!="UnityEngine.UI.Text" && name!="TMPro.TextMeshProUGUI" && name!="TMPro.TextMeshPro" && name!="UnityEngine.UI.HorizontalLayoutGroup" && name!="UnityEngine.UI.VerticalLayoutGroup" && name!="UnityEngine.UI.GridLayoutGroup" && name!="UnityEngine.UI.ContentSizeFitter" && name!="UnityEngine.UI.AspectRatioFitter") return null;
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
                    try {if(entry.Properties[i]==null) sample.Values[i]=absentProperty;else {++getterReads;sample.Values[i]=Freeze(entry.Properties[i].GetValue(entry.Component,null));}}
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
            var padding=value as RectOffset;
            if(padding!=null && value.GetType()==typeof(RectOffset)) return new[]{padding.left,padding.right,padding.top,padding.bottom};
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
                if(count++>0) text.Append(',');text.Append("{\"rectId\":").Append(node.Id).Append(",\"admitted\":").Append(admitted.Contains(node.Id)?"true":"false").Append(",\"name\":").Append(Quote(node.Name)).Append(",\"parentRectId\":").Append(node.Parent).Append(",\"componentTypes\":[");
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
            int cacheCount;lock(metadataLock) cacheCount=metadata.Count;
            return "{\"nodeLimit\":"+NodeLimit+",\"componentLimit\":"+ComponentLimit+",\"componentsPerNodeLimit\":"+ComponentsPerNode+",\"nodeCount\":"+nodes.Count+",\"admittedNodeCount\":"+admitted.Count+",\"knownInputComponentCount\":"+entries.Count+",\"droppedNodes\":"+droppedNodes+",\"droppedComponents\":"+droppedComponents+",\"queryErrors\":"+queryErrors+",\"componentInventoryQueries\":"+inventoryQueries+",\"getterReads\":"+getterReads+",\"metadataCacheLimit\":"+MetadataCacheLimit+",\"metadataCacheCount\":"+cacheCount+",\"metadataCacheHits\":"+metadataHits+",\"metadataCacheMisses\":"+metadataMisses+"}";
        }
        public string Selection()
        {
            int matchedCount=0,destroyedCount=0;
            foreach(int id in requestedRoots) {Node node;if(nodes.TryGetValue(id,out node)) {++matchedCount;if(node.Rect==null) ++destroyedCount;}}
            string status=selectorError!=null?"invalid-selector":!Filtered?"full-coverage":matchedCount==0?"no-matched-roots":matchedCount<requestedRoots.Count || destroyedCount>0?"partial-coverage":"matched";
            var text=new StringBuilder("{\"mode\":").Append(Quote(selectorError!=null?"invalid-selector":Filtered?"explicit-runtime-rect-subtrees":"full-snapshot"))
                .Append(",\"status\":").Append(Quote(status)).Append(",\"requestedRootCount\":").Append(requestedRoots.Count)
                .Append(",\"matchedRootCount\":").Append(matchedCount).Append(",\"missingRootCount\":").Append(requestedRoots.Count-matchedCount).Append(",\"destroyedRootCount\":").Append(destroyedCount)
                .Append(",\"error\":").Append(Quote(selectorError)).Append(",\"rootLimit\":").Append(SelectorRootLimit)
                .Append(",\"subtreeNodeLimitPerRoot\":").Append(FilteredSubtreeLimit).Append(",\"ancestorLimit\":").Append(AncestorLimit)
                .Append(",\"droppedSubtreeFrontierNodes\":").Append(filteredSubtreeDrops).Append(",\"requestedRoots\":[");
            for(int i=0;i<requestedRoots.Count;++i)
            {
                if(i>0) text.Append(',');int id=requestedRoots[i];Node node;bool matched=nodes.TryGetValue(id,out node);
                text.Append("{\"requestedRectId\":").Append(id).Append(",\"matched\":").Append(matched?"true":"false");
                if(matched)
                {
                    Node parent;string parentName=nodes.TryGetValue(node.Parent,out parent)?parent.Name:node.ParentTransform==null?null:node.ParentTransform.name;
                    int count;rootSubtreeCounts.TryGetValue(id,out count);
                    text.Append(",\"observedName\":").Append(Quote(node.Name)).Append(",\"parentInstanceId\":").Append(node.Parent)
                        .Append(",\"parentName\":").Append(Quote(parentName)).Append(",\"destroyed\":").Append(node.Rect==null?"true":"false")
                        .Append(",\"subtreeNodesAdmitted\":").Append(count);
                }
                text.Append('}');
            }
            return text.Append("]}").ToString();
        }
        public string SelectedContexts()
        {
            if(!Filtered || selectorError!=null) return "null";
            var text=new StringBuilder("{\"contextLimit\":").Append(OutputContextLimit).Append(",\"contexts\":[");int count=0,dropped=0;
            foreach(Node node in snapshotNodes)
            {
                if(node.Entries.Count==0) continue;
                if(count>=OutputContextLimit) {++dropped;continue;}
                if(count++>0) text.Append(',');text.Append(Context(node.Id));
            }
            return text.Append("],\"droppedContexts\":").Append(dropped).Append('}').ToString();
        }
    }
}
