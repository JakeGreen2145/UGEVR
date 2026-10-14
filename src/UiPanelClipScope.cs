using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace Ugevr
{
    public enum UiPanelClipping { None, CanonicalUguiDefaultRootV1 }
    internal sealed class PanelClipNode
    {
        public object Renderer,Root,Parent,Graphic,ChildRect,RendererObject;
        public int RendererId,RootId,GraphicId,ChildRectId;
        public int ParentId,RendererObjectId;
        public string CanonicalGraphic;
        public Rect Panel,ConvertedPanel,Child;
        public bool Clip,Cull,Pop;
        public Color Color;
        public float Alpha;
        public float? InheritedAlpha;
        public Vector2? Softness;
        public object[] Materials,Shaders,PopMaterials;
        public int[] MaterialIds,ShaderIds,PopMaterialIds;
    }
    internal sealed class PanelClipSnapshot
    {
        public object[] Inventory;
        public float[] RootPlacement;
        public PanelClipNode[] Nodes;
    }
    internal interface IPanelClipAccess
    {
        PanelClipSnapshot Capture(bool borrowed);
        void Enable(object renderer,Rect rectangle);
        void Disable(object renderer);
        bool InheritedAlphaAvailable {get;}
        bool SoftnessAvailable {get;}
    }
    // A public-field experiment, not complete rectangle/native-state ownership.
    public sealed class UiPanelClipScope : IDisposable
    {
        public const int MaximumRenderers=8192,MaximumComponents=65536,MaximumMaterialSlots=8;
        const int DiagnosticRenderers=64,MaximumDiagnosticCharacters=262144;
        int droppedObservations;
        readonly IPanelClipAccess access;
        readonly PanelClipSnapshot initial;
        PanelClipSnapshot placed;
        readonly List<string> observations=new List<string>();
        readonly List<string> errors=new List<string>();
        bool[] attempted,cleanupTried;
        bool disposed,eyeActive;
        int activeEye=-1;
        public int Conflicts {get;private set;}
        public string LastError {get;private set;}
        public static UiPanelClipping Parse(string value)
        {
            if(value=="none")return UiPanelClipping.None;
            if(value=="canonical-ugui-default-root-v1")return UiPanelClipping.CanonicalUguiDefaultRootV1;
            throw new ArgumentException("UI.PanelClipping must be none or canonical-ugui-default-root-v1.");
        }
        public UiPanelClipScope(Canvas[] roots) : this(new CanonicalUguiPanelClipAccess(roots)) {}
        internal UiPanelClipScope(IPanelClipAccess access)
        {
            this.access=access;initial=access.Capture(false);ValidateShape(initial);
            foreach(var node in initial.Nodes)if(node.Clip)throw new InvalidOperationException("panel_clip_existing_native_clip");
            attempted=new bool[initial.Nodes.Length];cleanupTried=new bool[attempted.Length];LastError="";
            Observe("admission",initial);
        }
        static bool Finite(float value) {return !Single.IsNaN(value)&&!Single.IsInfinity(value);}
        static bool Finite(Rect value) {return Finite(value.x)&&Finite(value.y)&&Finite(value.width)&&Finite(value.height)&&value.width>0&&value.height>0;}
        static void ValidateShape(PanelClipSnapshot snapshot)
        {
            if(snapshot==null||snapshot.Inventory==null||snapshot.Inventory.Length==0||snapshot.Inventory.Length>MaximumComponents||snapshot.Nodes==null||snapshot.Nodes.Length==0||snapshot.Nodes.Length>MaximumRenderers||snapshot.RootPlacement==null||snapshot.RootPlacement.Length==0||snapshot.RootPlacement.Length>64*26)throw new InvalidOperationException("panel_clip_incomplete_or_empty_inventory");
            foreach(float v in snapshot.RootPlacement)if(!Finite(v))throw new InvalidOperationException("panel_clip_nonfinite_placement");
            foreach(var node in snapshot.Nodes)
            {
                if(node==null||node.Renderer==null||node.Root==null||!Finite(node.Panel)||!Finite(node.ConvertedPanel)||!Finite(node.Child)||!Finite(node.Alpha)||!Finite(node.Color.r)||!Finite(node.Color.g)||!Finite(node.Color.b)||!Finite(node.Color.a)||node.Materials==null||node.Shaders==null||node.Materials.Length!=1||node.Shaders.Length!=1||node.Materials[0]==null||node.Shaders[0]==null||node.PopMaterials==null||node.PopMaterials.Length!=0||node.Pop)throw new InvalidOperationException("panel_clip_required_fields_unavailable_or_noncanonical");
                if(node.InheritedAlpha.HasValue&&!Finite(node.InheritedAlpha.Value))throw new InvalidOperationException("panel_clip_nonfinite_inherited_alpha");
                if(node.Softness.HasValue&&(!Finite(node.Softness.Value.x)||!Finite(node.Softness.Value.y)||node.Softness.Value.x!=0||node.Softness.Value.y!=0))throw new InvalidOperationException("panel_clip_softness_not_zero_or_nonfinite");
                if(node.MaterialIds==null||node.MaterialIds.Length!=node.Materials.Length||node.ShaderIds==null||node.ShaderIds.Length!=node.Shaders.Length||node.PopMaterialIds==null||node.PopMaterialIds.Length!=node.PopMaterials.Length)throw new InvalidOperationException("panel_clip_material_identity_inventory_unavailable");
                if(node.Graphic==null||node.ChildRect==null||node.RendererObject==null||node.RendererId==0||node.RootId==0||node.GraphicId==0||node.ChildRectId==0||node.MaterialIds[0]==0||node.ShaderIds[0]==0)throw new InvalidOperationException("panel_clip_required_identity_unavailable");
                if(node.RendererObjectId==0||(node.CanonicalGraphic!="Image"&&node.CanonicalGraphic!="Text"))throw new InvalidOperationException("panel_clip_canonical_identity_unavailable");
            }
        }
        static bool Same(object[] a,object[] b)
        {if(a.Length!=b.Length)return false;for(int i=0;i<a.Length;++i)if(!System.Object.ReferenceEquals(a[i],b[i]))return false;return true;}
        static bool Same(float[] a,float[] b)
        {if(a.Length!=b.Length)return false;for(int i=0;i<a.Length;++i)if(a[i]!=b[i])return false;return true;}
        static bool Same(Rect a,Rect b) {return a.x==b.x&&a.y==b.y&&a.width==b.width&&a.height==b.height;}
        void Guard(PanelClipSnapshot current,bool placement)
        {
            ValidateShape(current);
            if(!Same(initial.Inventory,current.Inventory)||current.Nodes.Length!=initial.Nodes.Length)throw new InvalidOperationException("panel_clip_component_inventory_changed");
            if(placement && !Same(placed.RootPlacement,current.RootPlacement))throw new InvalidOperationException("panel_clip_owned_panel_placement_changed");
            for(int i=0;i<initial.Nodes.Length;++i)
            {
                var a=initial.Nodes[i];var b=current.Nodes[i];
                if(a.ParentId!=b.ParentId||a.RendererObjectId!=b.RendererObjectId||a.CanonicalGraphic!=b.CanonicalGraphic)throw new InvalidOperationException("panel_clip_public_identity_changed");
                if(!System.Object.ReferenceEquals(a.Renderer,b.Renderer)||!System.Object.ReferenceEquals(a.Root,b.Root)||!System.Object.ReferenceEquals(a.Parent,b.Parent)||!System.Object.ReferenceEquals(a.Graphic,b.Graphic)||!System.Object.ReferenceEquals(a.ChildRect,b.ChildRect)||!System.Object.ReferenceEquals(a.RendererObject,b.RendererObject)||a.RendererId!=b.RendererId||a.RootId!=b.RootId||a.GraphicId!=b.GraphicId||a.ChildRectId!=b.ChildRectId||!Same(a.Panel,b.Panel)||!Same(a.Materials,b.Materials)||!Same(a.Shaders,b.Shaders)||!Same(a.PopMaterials,b.PopMaterials))throw new InvalidOperationException("panel_clip_identity_hierarchy_rectangle_or_material_changed");
            }
        }
        public void RecordPanelPlacement()
        {
            if(disposed||placed!=null)return;
            var current=access.Capture(true);Guard(current,false);
            foreach(var node in current.Nodes)if(node.Clip)throw new InvalidOperationException("panel_clip_new_native_clip_before_placement");
            placed=current;
        }
        public void BeginEye(int eye)
        {
            if(disposed||eyeActive||placed==null||eye<0||eye>1)throw new InvalidOperationException("panel_clip_pass_not_admitted");
            var current=access.Capture(true);Guard(current,true);
            foreach(var node in current.Nodes)if(node.Clip)throw new InvalidOperationException("panel_clip_new_native_clip_before_eye");
            eyeActive=true;activeEye=eye;Array.Clear(attempted,0,attempted.Length);Array.Clear(cleanupTried,0,cleanupTried.Length);
            Observe(eye==0?"before_left_ui":"before_right_ui",current);
            for(int i=0;i<current.Nodes.Length;++i)
            {
                // Mark before the void native setter; throwing does not prove no write.
                attempted[i]=true;access.Enable(current.Nodes[i].Renderer,current.Nodes[i].Panel);
            }
        }
        public void ObserveAfterEye(int eye)
        {
            if(!eyeActive)return;
            eye=activeEye;
            try {var current=access.Capture(true);Guard(current,true);Observe(eye==0?"after_left_ui":"after_right_ui",current);foreach(var n in current.Nodes)if(!n.Clip)Conflict("panel_clip_flag_lost_during_ui");}
            catch {Conflict("panel_clip_post_ui_query_or_context_failure");}
        }
        void Conflict(string error) {++Conflicts;LastError=error;if(errors.Count<16&&!errors.Contains(error))errors.Add(error);}
        public void EndEye(int eye)
        {
            if(!eyeActive)return;
            eye=activeEye;
            try
            {
                PanelClipSnapshot current;
                try {current=access.Capture(true);Guard(current,true);}
                catch {Conflict("panel_clip_cleanup_context_unconfirmed_foreign_state_preserved");return;}
                for(int i=0;i<attempted.Length;++i)
                {
                    if(!attempted[i]||cleanupTried[i])continue;cleanupTried[i]=true;
                    if(!current.Nodes[i].Clip){attempted[i]=false;continue;}
                    try {access.Disable(current.Nodes[i].Renderer);}
                    catch {Conflict("panel_clip_disable_exception_no_second_write");}
                }
                try
                {
                    current=access.Capture(true);Guard(current,true);Observe(eye==0?"after_left_cleanup":"after_right_cleanup",current);
                    for(int i=0;i<attempted.Length;++i)if(attempted[i]){if(current.Nodes[i].Clip)Conflict("panel_clip_cleanup_flag_still_enabled");else attempted[i]=false;}
                }
                catch {Conflict("panel_clip_cleanup_readback_unconfirmed");}
            }
            finally {eyeActive=false;activeEye=-1;}
        }
        public void ObserveDesktop()
        {
            if(!disposed)return;
            try {var current=access.Capture(false);Guard(current,false);Observe("after_desktop_rebuild",current);foreach(var n in current.Nodes)if(n.Clip)Conflict("panel_clip_desktop_flag_enabled");}
            catch {Conflict("panel_clip_desktop_readback_unconfirmed");}
        }
        public void Dispose()
        {if(disposed)return;if(eyeActive)EndEye(-1);disposed=true;}
        static string F(float v) {return Finite(v)?v.ToString("R",CultureInfo.InvariantCulture):"null";}
        static string R(Rect r) {return "["+F(r.x)+","+F(r.y)+","+F(r.width)+","+F(r.height)+"]";}
        static string Ids(int[] values) {return values==null?"null":"["+String.Join(",",Array.ConvertAll(values,v=>v.ToString(CultureInfo.InvariantCulture)))+"]";}
        void Observe(string phase,PanelClipSnapshot value)
        {
            if(phase!="admission")for(int i=0;i<value.Nodes.Length;++i)
            {
                var a=initial.Nodes[i];var b=value.Nodes[i];
                if(a.Cull!=b.Cull||a.Alpha!=b.Alpha||a.Color.r!=b.Color.r||a.Color.g!=b.Color.g||a.Color.b!=b.Color.b||a.Color.a!=b.Color.a||a.InheritedAlpha!=b.InheritedAlpha)Conflict("panel_clip_unrelated_public_fields_changed_preserved");
            }
            if(observations.Count==8){++droppedObservations;return;}
            var text=new StringBuilder("{\"phase\":\"").Append(phase).Append("\",\"rendererCount\":").Append(value.Nodes.Length).Append(",\"droppedDetailedRenderers\":").Append(Math.Max(0,value.Nodes.Length-DiagnosticRenderers)).Append(",\"renderers\":[");
            for(int i=0;i<Math.Min(DiagnosticRenderers,value.Nodes.Length);++i)
            {
                if(i!=0)text.Append(',');var n=value.Nodes[i];
                text.Append("{\"rendererId\":").Append(n.RendererId).Append(",\"rootId\":").Append(n.RootId).Append(",\"graphicId\":").Append(n.GraphicId).Append(",\"childRectId\":").Append(n.ChildRectId).Append(",\"parentId\":").Append(n.ParentId).Append(",\"rendererObjectId\":").Append(n.RendererObjectId).Append(",\"canonicalGraphic\":\"").Append(n.CanonicalGraphic=="Text"?"Text":"Image").Append("\",\"activityChecked\":true").Append(",\"panelRootLocalRectangle\":").Append(R(n.Panel)).Append(",\"convertedPanelRootLocalRectangle\":").Append(R(n.ConvertedPanel)).Append(",\"childRootLocalRectangle\":").Append(R(n.Child)).Append(",\"clip\":").Append(n.Clip?"true":"false").Append(",\"cull\":").Append(n.Cull?"true":"false").Append(",\"color\":[").Append(F(n.Color.r)).Append(',').Append(F(n.Color.g)).Append(',').Append(F(n.Color.b)).Append(',').Append(F(n.Color.a)).Append("],\"alpha\":").Append(F(n.Alpha)).Append(",\"inheritedAlpha\":").Append(n.InheritedAlpha.HasValue?F(n.InheritedAlpha.Value):"null").Append(",\"softness\":").Append(n.Softness.HasValue?"["+F(n.Softness.Value.x)+","+F(n.Softness.Value.y)+"]":"null").Append(",\"materialCount\":").Append(n.Materials.Length).Append(",\"materialIds\":").Append(Ids(n.MaterialIds)).Append(",\"shaderIds\":").Append(Ids(n.ShaderIds)).Append(",\"popMaterialIds\":").Append(Ids(n.PopMaterialIds)).Append(",\"popMaterialCount\":").Append(n.PopMaterials.Length).Append(",\"hasPopInstruction\":").Append(n.Pop?"true":"false").Append('}');
            }
            string record=text.Append("]}").ToString();int length=record.Length;foreach(var existing in observations)length+=existing.Length;
            if(length>MaximumDiagnosticCharacters){++droppedObservations;return;}
            observations.Add(record);
        }
        public string DiagnosticJson()
        {
            return "{\"mode\":\"canonical-ugui-default-root-v1\",\"completeNativeStateRestored\":false,\"foreignRectangleReplacementDetectable\":false,\"originalClipFlag\":false,\"inheritedAlphaAvailable\":"+(access.InheritedAlphaAvailable?"true":"false")+",\"softnessAvailable\":"+(access.SoftnessAvailable?"true":"false")+",\"inheritedAlphaUnavailableReason\":"+(access.InheritedAlphaAvailable?"null":"\"public_getter_unavailable\"")+",\"softnessUnavailableReason\":"+(access.SoftnessAvailable?"null":"\"public_getter_unavailable\"")+",\"droppedObservations\":"+droppedObservations+",\"conflicts\":"+Conflicts+",\"error\":\""+LastError+"\",\"errors\":["+(errors.Count==0?"":"\""+String.Join("\",\"",errors.ToArray())+"\"")+"],\"observations\":["+String.Join(",",observations.ToArray())+"]}";
        }
    }
}
