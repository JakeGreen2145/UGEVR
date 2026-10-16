using System;
using System.Reflection;
using System.Text.Json;
using Ugevr;
using UnityEngine;
using UnityEngine.UI;

// Controlled public API fixture, not a native draw/shader/texture-binding proof.
static class NativeUiGraphicChecks
{
    sealed class CustomImage : Image {}
    static NativeUiGraphicDiagnostics Details(bool trusted=true)
    {return new NativeUiGraphicDiagnostics(typeof(Image).Assembly,guid=>trusted);}
    static CanvasRenderer Renderer(RectTransform root,Graphic graphic=null)
    {
        var rect=new RectTransform {sizeDelta=new Vector2(80,20),anchoredPosition3D=new Vector3(10,30,0),parent=root};
        var renderer=new CanvasRenderer();renderer.gameObject.ObservedTransform=rect;
        renderer.AttachedComponents=graphic==null?new Component[]{rect}:new Component[]{rect,graphic};return renderer;
    }
    public static void Run(Action<bool,string> assert)
    {
        var root=new RectTransform {sizeDelta=new Vector2(1400,800),position=new Vector3(1,2,0)};
        var image=new Image {color=new Color {r=0,g=0,b=0,a=1},sprite=new Sprite {name="sprite",texture=new Texture {name="sprite texture"}}};
        var renderer=Renderer(root,image);
        var material=new Material {name="actual slot",shader=new Shader {name="actual shader"},mainTexture=new Texture {name="material texture"}};
        renderer.Materials=new[]{material};renderer.PopMaterials=new[]{material};
        int factories=Graphic.DefaultQueries;
        var details=Details();details.RegisterRoot(51,root);details.Admit(renderer,71,51);details.Capture(0);
        ((RectTransform)renderer.gameObject.ObservedTransform).anchoredPosition3D=new Vector3(11,32,0);
        image.color=new Color {r=1,a=.25f};details.Capture(1);
        int calls=renderer.NativeQueries,colors=image.ColorQueries,sprites=image.SpriteQueries;
        string first=details.DiagnosticJson();details.Capture(1);string second=details.DiagnosticJson();
        assert(first==second && calls==renderer.NativeQueries && colors==image.ColorQueries && sprites==image.SpriteQueries,"Repeated phase and serialization have no extra engine/Graphic reads");
        using(var doc=JsonDocument.Parse(first))
        {
            var value=doc.RootElement;var entry=value.GetProperty("renderers")[0];var phase=entry.GetProperty("phases")[0];
            assert(entry.GetProperty("rendererId").GetInt32()==71 && entry.GetProperty("rootCanvasId").GetInt32()==51 && entry.GetProperty("graphicId").GetInt32()==image.GetInstanceID(),"Detail IDs link the actual same-object Graphic inventory to cached renderer and selected root");
            assert(phase.GetProperty("rect").GetProperty("parentId").GetInt32()==root.GetInstanceID() && phase.GetProperty("rect").GetProperty("worldCorners").GetArrayLength()==4 && phase.GetProperty("rect").GetProperty("rootLocalCorners").GetArrayLength()==4,"Child/root/parent and both corner spaces are retained");
            assert(phase.GetProperty("rect").GetProperty("anchoredPosition3D")[0].GetSingle()==10 && entry.GetProperty("phases")[1].GetProperty("rect").GetProperty("anchoredPosition3D")[0].GetSingle()==11,"Phase geometry is frozen before later rect changes");
            assert(phase.GetProperty("graphicColor")[3].GetSingle()==1 && entry.GetProperty("phases")[1].GetProperty("graphicColor")[3].GetSingle()==.25f,"Canonical public color field-read samples are frozen per phase");
            assert(phase.GetProperty("materialCount").GetProperty("slots")[0].GetProperty("material").GetProperty("id").GetInt32()==material.GetInstanceID() && phase.GetProperty("imageSprite").GetProperty("texture").GetProperty("id").GetInt32()==image.sprite.texture.GetInstanceID(),"Actual material/shader and separate sprite texture IDs are retained without material creation");
            assert(value.GetProperty("canvasRendererTextureBinding").GetString().StartsWith("unavailable") && entry.GetProperty("phases")[2].ValueKind==JsonValueKind.Null,"Hidden SetTexture binding and unused phases remain explicitly unavailable");
        }
        assert(renderer.Materials[0]==material && renderer.CullValue==false && renderer.ClipEnables==0 && renderer.ClipDisables==0 && renderer.AlphaValue==1,"Detail reads never change material, cull, clip or alpha");
        assert(image.LazyOrMaterialQueries==0 && Graphic.DefaultQueries==factories,"Detail reads never invoke lazy Graphic canvas/renderer/rect/material getters or default material factories");

        foreach(bool trust in new[]{false,true})
        {
            var custom=new CustomImage();renderer=Renderer(root,custom);details=Details(trust);details.RegisterRoot(1,root);details.Admit(renderer,2,1);details.Capture(0);
            using(var doc=JsonDocument.Parse(details.DiagnosticJson()))
                assert(doc.RootElement.GetProperty("renderers")[0].GetProperty("graphicGetterUnavailable").GetString()!=null && custom.ColorQueries==0 && custom.SpriteQueries==0,"Unknown uGUI builds and custom subclasses never invoke Graphic getters");
        }
        var bounded=Details();CanvasRenderer beyondRoot=null,beyondTotal=null;
        for(int r=0;r<5;++r)
        {
            bounded.RegisterRoot(100+r,root);
            for(int i=0;i<65;++i){renderer=Renderer(root);bounded.Admit(renderer,1000+r*100+i,100+r);if(r==0&&i==64)beyondRoot=renderer;if(r==4&&i==0)beyondTotal=renderer;}
        }
        bounded.Capture(0);
        using(var doc=JsonDocument.Parse(bounded.DiagnosticJson()))
        {
            var value=doc.RootElement;
            assert(value.GetProperty("admittedRenderers").GetInt32()==256 && value.GetProperty("droppedRenderers").GetInt32()==69,"Per-root64/global256 bound reports every omitted renderer");
            assert(value.GetProperty("roots")[0].GetProperty("rootCanvasId").GetInt32()==100 && value.GetProperty("roots")[4].GetProperty("order").GetInt32()==4 && value.GetProperty("roots")[4].GetProperty("dropped").GetInt32()==65,"Selected root order, resolved IDs and per-root drops are explicit");
            assert(beyondRoot.ComponentInventoryQueries==0 && beyondRoot.NativeQueries==0 && beyondTotal.ComponentInventoryQueries==0 && beyondTotal.NativeQueries==0,"Dropped detail renderers receive no component/material/geometry queries");
        }
        renderer=Renderer(root,new Image());renderer.Materials=new[]{material,material,material,material,material,material};renderer.PopMaterials=renderer.Materials;renderer.ThrowMaterialSlot=1;
        renderer.ColorValue=new Color {r=Single.NaN,a=1};details=Details();details.RegisterRoot(1,root);details.Admit(renderer,2,1);details.Capture(0);
        using(var doc=JsonDocument.Parse(details.DiagnosticJson()))
        {
            var value=doc.RootElement;var phase=value.GetProperty("renderers")[0].GetProperty("phases")[0];
            assert(phase.GetProperty("materialCount").GetProperty("count").GetInt32()==6 && phase.GetProperty("materialCount").GetProperty("slots").GetArrayLength()==4 && value.GetProperty("omittedMaterialSlots").GetInt32()==4,"Actual slot counts are retained while only four material and four pop slots are read");
            assert(phase.GetProperty("materialCount").GetProperty("slots")[1].ValueKind==JsonValueKind.Null && value.GetProperty("queryErrors").GetInt32()==1 && value.GetProperty("nonfiniteValues").GetInt32()==1,"Throwing material getter and nonfinite color remain null with explicit counters");
        }
        renderer.Destroyed=true;int prior=renderer.NativeQueries;details.Capture(1);
        using(var doc=JsonDocument.Parse(details.DiagnosticJson()))assert(doc.RootElement.GetProperty("renderers")[0].GetProperty("phases")[1].GetProperty("destroyed").GetBoolean() && renderer.NativeQueries==prior,"Destroyed admitted renderer is marked without further native reads");

        details=Details();details.RegisterRoot(1,root);renderer=Renderer(root,new Image());renderer.AttachedComponents=new Component[17];for(int i=0;i<17;++i)renderer.AttachedComponents[i]=new Component();details.Admit(renderer,1,1);details.Capture(0);
        using(var doc=JsonDocument.Parse(details.DiagnosticJson()))assert(doc.RootElement.GetProperty("omittedComponents").GetInt32()==1 && doc.RootElement.GetProperty("renderers")[0].GetProperty("graphicGetterUnavailable").GetString()=="component_inventory_exceeds16","Component overflow stays unavailable rather than guessing a partial Graphic link");

        // Pathological names exercise the actual output cap and JSON escaping.
        details=Details();var named=new Material {name=new string('\u0001',300),shader=new Shader {name=new string('\u0002',300)},mainTexture=new Texture {name=new string('\u0003',300)}};
        for(int r=0;r<4;++r){details.RegisterRoot(r+1,root);for(int i=0;i<64;++i){renderer=Renderer(root);renderer.Materials=new[]{named,named,named,named};renderer.PopMaterials=renderer.Materials;details.Admit(renderer,r*64+i,r+1);}}
        details.Capture(0);first=details.DiagnosticJson();second=details.DiagnosticJson();
        using(var doc=JsonDocument.Parse(first))
            assert(doc.RootElement.GetProperty("phases")[0].GetProperty("omittedRows").GetInt32()>0 && first.Length<NativeUiGraphicDiagnostics.PhaseCharacterLimit+100000 && first==second,"Pathological strings preserve parseable bounded output, explicit omissions and serialization stability");

        // Existing native UI alone does not enable this additional cost scope.
        Canvas.Instances.Clear();
        try
        {
            renderer=Renderer(root,image);var canvas=new Canvas {gameObject=root.gameObject,Rects=new[]{root},Renderers=new[]{renderer},renderMode=RenderMode.ScreenSpaceOverlay};Canvas.Instances.Add(canvas);
            foreach(bool nativeEnabled in new[]{false,true})foreach(bool detailEnabled in new[]{false,true})
            {
                int inventories=renderer.ComponentInventoryQueries;var scope=new OverlayCanvasScope(new Camera {nearClipPlane=.1f,farClipPlane=100},1,2,false,1.8,false,null,nativeEnabled,1024,UiPanelClipping.None,detailEnabled);
                scope.Dispose();scope.CaptureNativeUiAfterRestoration();string json=scope.DiagnosticJson();
                assert(json.Contains("\"graphicDetails\":")== (nativeEnabled&&detailEnabled),"Graphic detail opt-in is jointly gated by native request admission");
                if(!(nativeEnabled&&detailEnabled))assert(renderer.ComponentInventoryQueries==inventories,"Default-off detail path adds zero local component inventory queries");
            }
        }
        finally {Canvas.Instances.Clear();}
    }
}
