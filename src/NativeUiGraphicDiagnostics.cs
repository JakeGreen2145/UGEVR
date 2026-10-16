using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace Ugevr
{
    // Additional explicit diagnostic only. Never ask Graphic for a material,
    // canvas or cached transform; never call mesh/layout/update callbacks.
    public sealed class NativeUiGraphicDiagnostics
    {
        public const int PerRootLimit=64,RendererLimit=256,MaterialSlotLimit=4,ComponentLimit=16,StringLimit=256,PhaseCharacterLimit=524288;
        sealed class Root {public RectTransform Rect;public int Id,Order,Admitted,Dropped;}
        sealed class Entry
        {
            public CanvasRenderer Renderer;public Root Root;public RectTransform Rect;
            public Component Graphic;public int RendererId,RectId,GraphicId,ComponentCount;
            public string Type,Unavailable;public readonly string[] Samples=new string[NativeUiStateDiagnostics.PhaseCount];
        }
        readonly List<Root> roots=new List<Root>();
        readonly List<Entry> entries=new List<Entry>();
        readonly Dictionary<int,Root> byRoot=new Dictionary<int,Root>();
        readonly bool[] captured=new bool[NativeUiStateDiagnostics.PhaseCount];
        readonly int[] omittedPhaseRows=new int[NativeUiStateDiagnostics.PhaseCount];
        readonly GraphicAccess access;
        int drops,errors,queries,nonfinite,stringTruncations,componentDrops;
        long slotDrops;
        public NativeUiGraphicDiagnostics() : this(FindUgui(),CanonicalUguiPanelClipAccess.SupportedBuild) {}
        internal NativeUiGraphicDiagnostics(Assembly assembly,Func<Guid,bool> supported)
        {access=new GraphicAccess(assembly,supported);}
        static Assembly FindUgui()
        {
            Assembly found=null;
            foreach(var assembly in AppDomain.CurrentDomain.GetAssemblies())if(assembly.GetName().Name=="UnityEngine.UI")
            {if(found!=null)return null;found=assembly;}
            return found;
        }
        sealed class GraphicAccess
        {
            public Type Graphic,Image,Text;public MethodInfo Color,Sprite;public string Unavailable;
            public GraphicAccess(Assembly assembly,Func<Guid,bool> supported)
            {
                try
                {
                    if(assembly==null || !supported(assembly.ManifestModule.ModuleVersionId)) {Unavailable="ugui_build_not_inspected_or_ambiguous";return;}
                    Graphic=assembly.GetType("UnityEngine.UI.Graphic",false);Image=assembly.GetType("UnityEngine.UI.Image",false);Text=assembly.GetType("UnityEngine.UI.Text",false);
                    if(Graphic==null || Image==null || Text==null || !Graphic.IsPublic || !Image.IsPublic || !Text.IsPublic
                       || !typeof(Component).IsAssignableFrom(Graphic) || !Graphic.IsAssignableFrom(Image) || !Graphic.IsAssignableFrom(Text))
                        throw new InvalidOperationException();
                    Color=Getter(Graphic,"color",typeof(Color));Sprite=Getter(Image,"sprite",typeof(Sprite));
                    if(Image.GetProperty("color").GetGetMethod().DeclaringType!=Graphic || Text.GetProperty("color").GetGetMethod().DeclaringType!=Graphic)
                        throw new InvalidOperationException();
                }
                catch {Graphic=null;Image=null;Text=null;Unavailable="inspected_public_getter_shape_unavailable";}
            }
            static MethodInfo Getter(Type type,string name,Type result)
            {
                var property=type.GetProperty(name,BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly);
                var getter=property==null?null:property.GetGetMethod();
                // color is virtual in inspected uGUI; callers require the exact
                // canonical Image/Text type with no override of that getter.
                if(getter==null || getter.ReturnType!=result || getter.IsStatic || getter.ContainsGenericParameters || getter.GetParameters().Length!=0)
                    throw new InvalidOperationException();
                return getter;
            }
        }
        T Read<T>(Func<T> getter) {++queries;return getter();}
        string Query(Func<string> getter) {try {return getter();}catch {++errors;return "null";}}
        public void RegisterRoot(int rootId,RectTransform root)
        {
            if(byRoot.ContainsKey(rootId))return;
            // The overlay already refuses more than64 roots. Keep this class
            // independently bounded if called outside that scope.
            if(roots.Count>=64) {++drops;return;}
            var entry=new Root {Id=rootId,Rect=root,Order=roots.Count};roots.Add(entry);byRoot.Add(rootId,entry);
        }
        public void Admit(CanvasRenderer renderer,int rendererId,int rootId)
        {
            Root root;
            if(!byRoot.TryGetValue(rootId,out root)) {++drops;return;}
            if(root.Admitted>=PerRootLimit || entries.Count>=RendererLimit) {++root.Dropped;++drops;return;}
            var entry=new Entry {Renderer=renderer,RendererId=rendererId,Root=root};entries.Add(entry);++root.Admitted;
            try
            {
                entry.Rect=Read(()=>renderer.gameObject.transform as RectTransform);
                if(entry.Rect!=null)entry.RectId=Read(()=>entry.Rect.GetInstanceID());
                if(access.Graphic==null) {entry.Unavailable=access.Unavailable;return;}
                Component[] components=Read(()=>renderer.GetComponents<Component>());entry.ComponentCount=components.Length;
                if(components.Length>ComponentLimit) {componentDrops+=components.Length-ComponentLimit;entry.Unavailable="component_inventory_exceeds16";return;}
                foreach(var component in components)if(component!=null && access.Graphic.IsAssignableFrom(component.GetType()))
                {
                    if(entry.Graphic!=null) {entry.Graphic=null;entry.Unavailable="multiple_graphics_on_renderer_object";return;}
                    entry.Graphic=component;
                }
                if(entry.Graphic==null) {entry.Unavailable="no_graphic_on_renderer_object";return;}
                entry.GraphicId=Read(()=>entry.Graphic.GetInstanceID());entry.Type=entry.Graphic.GetType().FullName;
                if(entry.Type.Length>StringLimit){entry.Type=entry.Type.Substring(0,StringLimit);++stringTruncations;}
                if(entry.Graphic.GetType()!=access.Image && entry.Graphic.GetType()!=access.Text)entry.Unavailable="custom_graphic_getters_not_invoked";
            }
            catch {++errors;entry.Unavailable="admission_query_failed";}
        }
        public void Capture(int phase)
        {
            if(phase<0 || phase>=captured.Length)throw new ArgumentOutOfRangeException("phase");
            if(captured[phase])return;captured[phase]=true;
            int characters=0;
            for(int index=0;index<entries.Count;++index)
            {
                var entry=entries[index];
                int startErrors=errors,startNonfinite=nonfinite,startQueries=queries;
                long startDrops=slotDrops;
                var b=new StringBuilder("{\"destroyed\":").Append(entry.Renderer==null?"true":"false");
                if(entry.Renderer!=null)
                {
                    b.Append(",\"rendererObjectId\":").Append(Query(()=>Id(Read(()=>entry.Renderer.gameObject))))
                     .Append(",\"activeSelf\":").Append(Query(()=>Flag(Read(()=>entry.Renderer.gameObject.activeSelf))))
                     .Append(",\"activeInHierarchy\":").Append(Query(()=>Flag(Read(()=>entry.Renderer.gameObject.activeInHierarchy))))
                     .Append(",\"rect\":").Append(Query(()=>Geometry(entry)))
                     .Append(",\"rendererColor\":").Append(Query(()=>ColorValue(Read(()=>entry.Renderer.GetColor()))))
                     .Append(",\"materialCount\":").Append(Materials(entry.Renderer,false))
                     .Append(",\"popMaterialCount\":").Append(Materials(entry.Renderer,true))
                     .Append(",\"hasPopInstruction\":").Append(Query(()=>Flag(Read(()=>entry.Renderer.hasPopInstruction))));
                    if(entry.Unavailable==null && entry.Graphic!=null)
                    {
                        b.Append(",\"graphicActive\":").Append(Query(()=>Flag(Read(()=>((Behaviour)entry.Graphic).isActiveAndEnabled))))
                         .Append(",\"graphicColor\":").Append(Query(()=>ColorValue(Read(()=>(Color)access.Color.Invoke(entry.Graphic,null)))));
                        if(entry.Graphic.GetType()==access.Image)
                            b.Append(",\"imageSprite\":").Append(Query(()=>SpriteValue(Read(()=>(Sprite)access.Sprite.Invoke(entry.Graphic,null)))));
                    }
                }
                b.Append(",\"queryErrors\":").Append(errors-startErrors).Append(",\"readOperationAttempts\":").Append(queries-startQueries)
                 .Append(",\"nonfiniteValues\":").Append(nonfinite-startNonfinite).Append(",\"omittedMaterialSlots\":").Append(slotDrops-startDrops).Append('}');
                if(b.Length>PhaseCharacterLimit-characters) {omittedPhaseRows[phase]=entries.Count-index;break;}
                entry.Samples[phase]=b.ToString();characters+=b.Length;
            }
        }
        string Geometry(Entry entry)
        {
            if(entry.Rect==null || entry.Root.Rect==null)return "null";
            Transform parent=Read(()=>entry.Rect.parent);
            var corners=new Vector3[4];Read(()=>{entry.Rect.GetWorldCorners(corners);return 0;});
            var local=new Vector3[4];for(int i=0;i<4;++i){int j=i;local[i]=Read(()=>entry.Root.Rect.InverseTransformPoint(corners[j]));}
            return "{\"parentId\":"+Id(parent)+",\"rootRectId\":"+Id(entry.Root.Rect)+",\"childRect\":"+RectValue(Read(()=>entry.Rect.rect))
                +",\"rootRect\":"+RectValue(Read(()=>entry.Root.Rect.rect))+",\"anchoredPosition3D\":"+Vector(Read(()=>entry.Rect.anchoredPosition3D))
                +",\"anchorMin\":"+Vector(Read(()=>entry.Rect.anchorMin))+",\"anchorMax\":"+Vector(Read(()=>entry.Rect.anchorMax))
                +",\"pivot\":"+Vector(Read(()=>entry.Rect.pivot))+",\"sizeDelta\":"+Vector(Read(()=>entry.Rect.sizeDelta))
                +",\"worldCorners\":"+Corners(corners)+",\"rootLocalCorners\":"+Corners(local)+"}";
        }
        string Materials(CanvasRenderer renderer,bool pop)
        {
            return Query(()=>
            {
                int count=pop?Read(()=>renderer.popMaterialCount):Read(()=>renderer.materialCount);
                if(count<0)throw new InvalidOperationException();
                slotDrops+=Math.Max(0,count-MaterialSlotLimit);
                var b=new StringBuilder("{\"count\":").Append(count).Append(",\"slots\":[");
                for(int i=0;i<count && i<MaterialSlotLimit;++i)
                {
                    int slot=i;if(i>0)b.Append(',');
                    b.Append(Query(()=>MaterialValue(pop?Read(()=>renderer.GetPopMaterial(slot)):Read(()=>renderer.GetMaterial(slot)))));
                }
                return b.Append("]}").ToString();
            });
        }
        string MaterialValue(Material material)
        {
            if(material==null)return "null";
            return "{\"material\":"+Asset(material)+",\"shader\":"+Query(()=>Asset(Read(()=>material.shader)))
                   +",\"materialMainTexture\":"+Query(()=>Asset(Read(()=>material.mainTexture)))+"}";
        }
        string SpriteValue(Sprite sprite)
        {return sprite==null?"null":"{\"sprite\":"+Asset(sprite)+",\"texture\":"+Query(()=>Asset(Read(()=>sprite.texture)))+"}";}
        string Asset(UnityEngine.Object value)
        {return value==null?"null":"{\"id\":"+Id(value)+",\"name\":"+Text(Read(()=>value.name))+"}";}
        string Id(UnityEngine.Object value) {return value==null?"null":Read(()=>value.GetInstanceID()).ToString(CultureInfo.InvariantCulture);}
        static string Flag(bool value) {return value?"true":"false";}
        string Number(float value) {if(Single.IsNaN(value)||Single.IsInfinity(value)){++nonfinite;return "null";}return value.ToString("R",CultureInfo.InvariantCulture);}
        string Vector(Vector2 value) {return "["+Number(value.x)+","+Number(value.y)+"]";}
        string Vector(Vector3 value) {return "["+Number(value.x)+","+Number(value.y)+","+Number(value.z)+"]";}
        string RectValue(Rect value) {return "["+Number(value.x)+","+Number(value.y)+","+Number(value.width)+","+Number(value.height)+"]";}
        string ColorValue(Color value) {return "["+Number(value.r)+","+Number(value.g)+","+Number(value.b)+","+Number(value.a)+"]";}
        string Corners(Vector3[] values) {var b=new StringBuilder("[");for(int i=0;i<values.Length;++i){if(i>0)b.Append(',');b.Append(Vector(values[i]));}return b.Append(']').ToString();}
        string Text(string value,bool countTruncation=true)
        {
            if(value==null)return "null";
            if(value.Length>StringLimit){value=value.Substring(0,StringLimit);if(countTruncation)++stringTruncations;}
            var b=new StringBuilder("\"");
            for(int i=0;i<value.Length;++i)
            {
                char c=value[i];
                if(c=='"'||c=='\\')b.Append('\\').Append(c);
                else if(Char.IsControl(c))b.Append("\\u").Append(((int)c).ToString("x4",CultureInfo.InvariantCulture));
                else if(Char.IsHighSurrogate(c)) {if(i+1<value.Length&&Char.IsLowSurrogate(value[i+1]))b.Append(c).Append(value[++i]);else b.Append("\\ufffd");}
                else if(Char.IsLowSurrogate(c))b.Append("\\ufffd");else b.Append(c);
            }
            return b.Append('"').ToString();
        }
        public string DiagnosticJson()
        {
            // All engine observations are already frozen; JSON performs no queries.
            var b=new StringBuilder("{\"scope\":\"borrowed renderer geometry and asset identities; no draw attribution\",\"rendererLimit\":256,\"perRootLimit\":64,\"materialSlotLimit\":4,\"componentLimit\":16,\"stringLimit\":256,\"admittedRenderers\":").Append(entries.Count)
                .Append(",\"droppedRenderers\":").Append(drops).Append(",\"queryErrors\":").Append(errors).Append(",\"readOperationAttempts\":").Append(queries)
                .Append(",\"nonfiniteValues\":").Append(nonfinite).Append(",\"omittedMaterialSlots\":").Append(slotDrops).Append(",\"omittedComponents\":").Append(componentDrops)
                .Append(",\"stringTruncations\":").Append(stringTruncations).Append(",\"canonicalGetterUnavailable\":").Append(Text(access.Unavailable,false))
                .Append(",\"canvasRendererTextureBinding\":\"unavailable; materialMainTexture does not observe SetTexture overrides\",\"phaseCharacterLimit\":").Append(PhaseCharacterLimit)
                .Append(",\"phases\":[");
            for(int phase=0;phase<captured.Length;++phase){if(phase>0)b.Append(',');b.Append("{\"phaseIndex\":").Append(phase).Append(",\"captured\":").Append(Flag(captured[phase])).Append(",\"omittedRows\":").Append(omittedPhaseRows[phase]).Append('}');}
            b.Append("],\"roots\":[");
            for(int i=0;i<roots.Count;++i){if(i>0)b.Append(',');var root=roots[i];b.Append("{\"order\":").Append(root.Order).Append(",\"rootCanvasId\":").Append(root.Id).Append(",\"admitted\":").Append(root.Admitted).Append(",\"dropped\":").Append(root.Dropped).Append('}');}
            b.Append("],\"renderers\":[");
            for(int i=0;i<entries.Count;++i)
            {
                if(i>0)b.Append(',');var entry=entries[i];
                b.Append("{\"rendererId\":").Append(entry.RendererId).Append(",\"rootCanvasId\":").Append(entry.Root.Id).Append(",\"childRectId\":").Append(entry.RectId)
                 .Append(",\"graphicId\":").Append(entry.GraphicId).Append(",\"graphicType\":").Append(Text(entry.Type,false)).Append(",\"graphicGetterUnavailable\":").Append(Text(entry.Unavailable,false))
                 .Append(",\"componentCount\":").Append(entry.ComponentCount).Append(",\"phases\":[");
                for(int phase=0;phase<captured.Length;++phase){if(phase>0)b.Append(',');b.Append(entry.Samples[phase]??"null");}b.Append("]}");
            }
            return b.Append("]}").ToString();
        }
    }
}
