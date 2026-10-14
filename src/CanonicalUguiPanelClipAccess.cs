using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Ugevr
{
    // Narrow public adapter for the two inspected UGUI builds. No guessed shader names.
    internal sealed class CanonicalUguiPanelClipAccess : IPanelClipAccess
    {
        readonly Canvas[] roots;
        readonly Material defaultMaterial;
        readonly Shader defaultShader;
        readonly MethodInfo inherited,softness;
        readonly Type image,text,graphicType,mask,rectMask,clippable,materialModifier,meshModifier;
        readonly MethodInfo graphicCanvas,graphicRenderer,graphicRect,graphicActive,imageMaterial,textMaterial;
        public bool InheritedAlphaAvailable {get {return inherited!=null;}}
        public bool SoftnessAvailable {get {return softness!=null;}}
        internal static bool SupportedBuild(Guid value)
        {return value==new Guid("77113f89-90f5-421e-9612-be5b86cac085")||value==new Guid("576a1661-0fc3-4a1a-9fd2-ae2d01246769");}
        public CanonicalUguiPanelClipAccess(Canvas[] roots) : this(roots,FindUgui(),SupportedBuild) {}
        static Assembly FindUgui()
        {
            Assembly result=null;
            foreach(var assembly in AppDomain.CurrentDomain.GetAssemblies())if(assembly.GetName().Name=="UnityEngine.UI")
            {if(result!=null)throw new InvalidOperationException("panel_clip_ambiguous_ugui_assembly");result=assembly;}
            if(result==null)throw new InvalidOperationException("panel_clip_ugui_not_loaded");return result;
        }
        static Type PublicType(Assembly assembly,string name)
        {
            var type=assembly.GetType("UnityEngine.UI."+name,false);
            if(type==null||!type.IsPublic)throw new InvalidOperationException("panel_clip_public_type_unavailable");return type;
        }
        static MethodInfo Getter(Type type,string name,Type result,bool isStatic)
        {
            var property=type.GetProperty(name,BindingFlags.Public|(isStatic?BindingFlags.Static:BindingFlags.Instance));
            var getter=property==null?null:property.GetGetMethod();
            if(getter==null||getter.IsStatic!=isStatic||getter.ReturnType!=result||getter.GetParameters().Length!=0)throw new InvalidOperationException("panel_clip_public_getter_shape_unknown");return getter;
        }
        internal CanonicalUguiPanelClipAccess(Canvas[] roots,Assembly assembly,Func<Guid,bool> inspectedBuild)
        {
            if(!inspectedBuild(assembly.ManifestModule.ModuleVersionId))throw new InvalidOperationException("panel_clip_uninspected_ugui_build");
            if(roots==null||roots.Length==0)throw new InvalidOperationException("panel_clip_empty_roots");
            if(roots.Length>64)throw new InvalidOperationException("panel_clip_root_limit_exceeded");
            image=PublicType(assembly,"Image");text=PublicType(assembly,"Text");graphicType=PublicType(assembly,"Graphic");
            mask=PublicType(assembly,"Mask");rectMask=PublicType(assembly,"RectMask2D");clippable=PublicType(assembly,"IClippable");materialModifier=PublicType(assembly,"IMaterialModifier");meshModifier=PublicType(assembly,"IMeshModifier");
            if(!graphicType.IsAssignableFrom(image)||!graphicType.IsAssignableFrom(text)||!typeof(Component).IsAssignableFrom(graphicType)||!clippable.IsInterface||!materialModifier.IsInterface||!meshModifier.IsInterface)throw new InvalidOperationException("panel_clip_public_type_shape_unknown");
            graphicCanvas=Getter(graphicType,"canvas",typeof(Canvas),false);graphicRenderer=Getter(graphicType,"canvasRenderer",typeof(CanvasRenderer),false);graphicRect=Getter(graphicType,"rectTransform",typeof(RectTransform),false);graphicActive=Getter(graphicType,"isActiveAndEnabled",typeof(bool),false);
            imageMaterial=Getter(image,"material",typeof(Material),false);textMaterial=Getter(text,"material",typeof(Material),false);
            this.roots=(Canvas[])roots.Clone();
            defaultMaterial=(Material)Getter(graphicType,"defaultGraphicMaterial",typeof(Material),true).Invoke(null,null);
            if(defaultMaterial==null||defaultMaterial.shader==null)throw new InvalidOperationException("panel_clip_default_material_unavailable");
            defaultShader=defaultMaterial.shader;
            inherited=NativeUiStateDiagnostics.FindCachedFloatMethod(typeof(CanvasRenderer),"GetInheritedAlpha");
            var property=typeof(CanvasRenderer).GetProperty("clippingSoftness",BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly);
            if(property!=null)
            {
                softness=property.GetGetMethod();
                if(softness==null||softness.IsVirtual||softness.ReturnType!=typeof(Vector2)||softness.GetParameters().Length!=0)throw new InvalidOperationException("panel_clip_softness_api_shape_unknown");
            }
        }
        static void Add(List<object> inventory,object value)
        {if(inventory.Count==UiPanelClipScope.MaximumComponents)throw new InvalidOperationException("panel_clip_component_limit");inventory.Add(value);}
        static Rect Bounds(RectTransform rect,RectTransform root)
        {
            var corners=new Vector3[4];rect.GetWorldCorners(corners);
            float minX=Single.PositiveInfinity,minY=Single.PositiveInfinity,maxX=Single.NegativeInfinity,maxY=Single.NegativeInfinity;
            foreach(var corner in corners)
            {
                var p=root.InverseTransformPoint(corner);
                if(Single.IsNaN(p.x)||Single.IsInfinity(p.x)||Single.IsNaN(p.y)||Single.IsInfinity(p.y)||Single.IsNaN(p.z)||Single.IsInfinity(p.z))throw new InvalidOperationException("panel_clip_nonfinite_canvas_corner");
                minX=Math.Min(minX,p.x);maxX=Math.Max(maxX,p.x);minY=Math.Min(minY,p.y);maxY=Math.Max(maxY,p.y);
            }
            return new Rect(minX,minY,maxX-minX,maxY-minY);
        }
        static void Pose(List<float> placement,Transform root)
        {
            var p=root.position;var q=root.rotation;var s=root.localScale;var m=root.localToWorldMatrix;
            placement.AddRange(new[]{p.x,p.y,p.z,q.x,q.y,q.z,q.w,s.x,s.y,s.z});
            for(int i=0;i<16;++i)placement.Add(m[i]);
        }
        public PanelClipSnapshot Capture(bool borrowed)
        {
            var inventory=new List<object>();var nodes=new List<PanelClipNode>();var placement=new List<float>();var seen=new HashSet<int>();
            foreach(var canvas in roots)
            {
                if(canvas==null||!canvas.isActiveAndEnabled||!canvas.isRootCanvas||canvas.renderMode!=(borrowed?RenderMode.WorldSpace:RenderMode.ScreenSpaceOverlay))throw new InvalidOperationException("panel_clip_root_activity_or_mode_changed");
                var root=canvas.transform as RectTransform;if(root==null)throw new InvalidOperationException("panel_clip_root_rectangle_unavailable");
                Add(inventory,canvas);Add(inventory,root);Add(inventory,root.parent);Pose(placement,root);
                Transform ancestor=root.parent;int depth=0;
                while(ancestor!=null)
                {
                    if(++depth>128)throw new InvalidOperationException("panel_clip_ancestor_limit");
                    Add(inventory,ancestor);
                    var components=ancestor.GetComponents<Component>();
                    if(components.Length>UiPanelClipScope.MaximumComponents-inventory.Count)throw new InvalidOperationException("panel_clip_ancestor_component_limit");
                    foreach(var c in components)
                    {if(c==null||mask.IsInstanceOfType(c)||rectMask.IsInstanceOfType(c))throw new InvalidOperationException("panel_clip_unknown_or_masked_ancestor");Add(inventory,c);}
                    ancestor=ancestor.parent;
                }
                // Unity allocates the complete array first; this is an inspection bound,
                // not a native allocation limit. Never inspect a truncated prefix.
                var all=canvas.GetComponentsInChildren<Component>(true);
                if(all.Length>UiPanelClipScope.MaximumComponents-inventory.Count)throw new InvalidOperationException("panel_clip_descendant_component_limit");
                var renderers=new List<CanvasRenderer>();
                foreach(var c in all)
                {
                    if(c==null)throw new InvalidOperationException("panel_clip_unknown_component_slot");
                    Add(inventory,c);
                    if(!c.gameObject.activeInHierarchy||!c.gameObject.activeSelf)throw new InvalidOperationException("panel_clip_inactive_descendant");
                    if(mask.IsInstanceOfType(c)||rectMask.IsInstanceOfType(c)||(c is Canvas && c!=canvas))throw new InvalidOperationException("panel_clip_mask_or_nested_canvas");
                    bool graphic=graphicType.IsInstanceOfType(c);
                    if(graphic&&(c.GetType()!=image&&c.GetType()!=text))throw new InvalidOperationException("panel_clip_custom_graphic");
                    if((clippable.IsInstanceOfType(c)||materialModifier.IsInstanceOfType(c)||meshModifier.IsInstanceOfType(c))&&!graphic)throw new InvalidOperationException("panel_clip_custom_clipping_material_or_mesh_path");
                    var renderer=c as CanvasRenderer;if(renderer!=null)renderers.Add(renderer);
                }
                Rect panel=root.rect;Rect converted=Bounds(root,root);
                if(renderers.Count==0)throw new InvalidOperationException("panel_clip_empty_renderer_inventory");
                foreach(var renderer in renderers)
                {
                    if(nodes.Count==UiPanelClipScope.MaximumRenderers)throw new InvalidOperationException("panel_clip_renderer_limit");
                    if(renderer.GetType()!=typeof(CanvasRenderer)||!seen.Add(renderer.GetInstanceID()))throw new InvalidOperationException("panel_clip_duplicate_or_custom_renderer");
                    Component graphic=null;var attached=renderer.GetComponents<Component>();
                    if(attached.Length>64)throw new InvalidOperationException("panel_clip_renderer_component_limit");
                    foreach(var c in attached){if(c==null)throw new InvalidOperationException("panel_clip_unknown_attached_component");Add(inventory,c);if(mask.IsInstanceOfType(c)||rectMask.IsInstanceOfType(c)||(meshModifier.IsInstanceOfType(c)||materialModifier.IsInstanceOfType(c)||clippable.IsInstanceOfType(c))&&!graphicType.IsInstanceOfType(c))throw new InvalidOperationException("panel_clip_attached_mask_or_custom_path");if(graphicType.IsInstanceOfType(c)){if(graphic!=null)throw new InvalidOperationException("panel_clip_multiple_graphics");graphic=c;}}
                    if(graphic==null||(graphic.GetType()!=image&&graphic.GetType()!=text)||!(bool)graphicActive.Invoke(graphic,null)||(Canvas)graphicCanvas.Invoke(graphic,null)!=canvas||(CanvasRenderer)graphicRenderer.Invoke(graphic,null)!=renderer||(Material)(graphic.GetType()==image?imageMaterial:textMaterial).Invoke(graphic,null)!=defaultMaterial)throw new InvalidOperationException("panel_clip_noncanonical_graphic_or_material");
                    var rect=(RectTransform)graphicRect.Invoke(graphic,null);if(rect==null)throw new InvalidOperationException("panel_clip_child_rectangle_missing");
                    int count=renderer.materialCount,pops=renderer.popMaterialCount;
                    if(count<0||count>UiPanelClipScope.MaximumMaterialSlots||pops<0||pops>UiPanelClipScope.MaximumMaterialSlots)throw new InvalidOperationException("panel_clip_material_slot_limit");
                    var materials=new object[count];var shaders=new object[count];var popMaterials=new object[pops];var materialIds=new int[count];var shaderIds=new int[count];var popIds=new int[pops];
                    for(int i=0;i<count;++i){var material=renderer.GetMaterial(i);materials[i]=material;shaders[i]=material==null?null:material.shader;if(material!=defaultMaterial||material.shader!=defaultShader)throw new InvalidOperationException("panel_clip_custom_render_material_or_shader");materialIds[i]=material.GetInstanceID();shaderIds[i]=defaultShader.GetInstanceID();}
                    for(int i=0;i<pops;++i){var material=renderer.GetPopMaterial(i);popMaterials[i]=material;popIds[i]=material==null?0:material.GetInstanceID();}
                    var node=new PanelClipNode {Renderer=renderer,Root=canvas,Parent=rect.parent,Graphic=graphic,ChildRect=rect,RendererObject=renderer.gameObject,RendererId=renderer.GetInstanceID(),RootId=canvas.GetInstanceID(),GraphicId=graphic.GetInstanceID(),ChildRectId=rect.GetInstanceID(),Panel=panel,ConvertedPanel=converted,Child=Bounds(rect,root),Clip=renderer.hasRectClipping,Cull=renderer.cull,Color=renderer.GetColor(),Alpha=renderer.GetAlpha(),Materials=materials,Shaders=shaders,PopMaterials=popMaterials,MaterialIds=materialIds,ShaderIds=shaderIds,PopMaterialIds=popIds,Pop=renderer.hasPopInstruction};
                    if(inherited!=null)node.InheritedAlpha=(float)inherited.Invoke(renderer,null);
                    if(softness!=null)node.Softness=(Vector2)softness.Invoke(renderer,null);
                    node.ParentId=(Transform)node.Parent==null?0:((Transform)node.Parent).GetInstanceID();node.RendererObjectId=((GameObject)node.RendererObject).GetInstanceID();node.CanonicalGraphic=graphic.GetType()==image?"Image":"Text";
                    nodes.Add(node);
                }
            }
            return new PanelClipSnapshot {Inventory=inventory.ToArray(),Nodes=nodes.ToArray(),RootPlacement=placement.ToArray()};
        }
        public void Enable(object renderer,Rect rectangle) {((CanvasRenderer)renderer).EnableRectClipping(rectangle);}
        public void Disable(object renderer) {((CanvasRenderer)renderer).DisableRectClipping();}
    }
}
