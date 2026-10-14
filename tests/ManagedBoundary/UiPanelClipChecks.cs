using System;
using System.Text.Json;
using Ugevr;
using UnityEngine;
using UnityEngine.UI;

static class UiPanelClipChecks
{
    sealed class Fake : IPanelClipAccess
    {
        public bool Clip,ThrowCapture,ThrowEnable,ThrowDisable,Changed,Optional;
        public int Captures,Enables,Disables,Count=1,InventoryCount=1;
        public float Alpha=1,Width=100;public object Parent=new object();
        readonly object renderer=new object(),root=new object(),material=new object(),shader=new object(),token=new object(),graphic=new object(),rect=new object(),gameObject=new object();
        public bool InheritedAlphaAvailable {get {return Optional;}}
        public bool SoftnessAvailable {get {return Optional;}}
        public PanelClipSnapshot Capture(bool borrowed)
        {
            ++Captures;if(ThrowCapture)throw new InvalidOperationException();
            var inventory=new object[InventoryCount];for(int i=0;i<inventory.Length;++i)inventory[i]=Changed?new object():token;
            var nodes=new PanelClipNode[Count];
            for(int i=0;i<nodes.Length;++i)nodes[i]=new PanelClipNode {Renderer=renderer,Root=root,Parent=Parent,Graphic=graphic,ChildRect=rect,RendererObject=gameObject,RendererId=1,RootId=2,GraphicId=5,ChildRectId=6,ParentId=7,RendererObjectId=8,CanonicalGraphic="Image",Panel=new Rect(0,0,Width,50),ConvertedPanel=new Rect(0,0,Width,50),Child=new Rect(90,45,20,10),Clip=Clip,Color=new Color {a=1},Alpha=Alpha,InheritedAlpha=Optional?(float?)1:null,Softness=Optional?(Vector2?)new Vector2():null,Materials=new[]{material},Shaders=new[]{shader},PopMaterials=new object[0],MaterialIds=new[]{3},ShaderIds=new[]{4},PopMaterialIds=new int[0]};
            return new PanelClipSnapshot {Inventory=inventory,RootPlacement=new[]{borrowed?10f:0f},Nodes=nodes};
        }
        public void Enable(object value,Rect rect){++Enables;Clip=true;if(ThrowEnable)throw new InvalidOperationException();}
        public void Disable(object value){++Disables;if(ThrowDisable)throw new InvalidOperationException();Clip=false;}
    }
    sealed class CustomImage : Image {}
    sealed class Modifier : Component,IMaterialModifier {}
    static void Refuses(Action action,Action<bool,string> check,string message)
    {bool rejected=false;try {action();}catch(InvalidOperationException){rejected=true;}check(rejected,message);}
    static Canvas NativeRoot()
    {
        var rect=new RectTransform {sizeDelta=new Vector2(100,50),localRotation=new Quaternion(0,0,0,1),localScale=new Vector3(1,1,1)};
        var canvas=new Canvas {Rects=new[]{rect}};
        var renderer=new CanvasRenderer {Materials=new[]{Graphic.Default}};
        var image=new Image {canvas=canvas,canvasRenderer=renderer,rectTransform=rect};
        renderer.AttachedComponents=new Component[]{renderer,image,rect};
        canvas.Renderers=new[]{renderer};canvas.ClipComponents=new Component[]{canvas,rect,renderer,image};
        return canvas;
    }
    static CanonicalUguiPanelClipAccess Adapter(Canvas root)
    {return new CanonicalUguiPanelClipAccess(new[]{root},typeof(Image).Assembly,g=>true);}
    public static void Run(Action<bool,string> check)
    {
        check(UiPanelClipScope.Parse("none")==UiPanelClipping.None,"panel crop stays explicitly off");
        bool bad=false;try {UiPanelClipScope.Parse("auto");}catch(ArgumentException){bad=true;}check(bad,"unknown automatic crop refuses");
        check(CanonicalUguiPanelClipAccess.SupportedBuild(new Guid("77113f89-90f5-421e-9612-be5b86cac085"))&&CanonicalUguiPanelClipAccess.SupportedBuild(new Guid("576a1661-0fc3-4a1a-9fd2-ae2d01246769"))&&!CanonicalUguiPanelClipAccess.SupportedBuild(Guid.Empty),"adapter requires inspected UGUI identity");
        var fake=new Fake();var scope=new UiPanelClipScope(fake);scope.RecordPanelPlacement();scope.BeginEye(0);
        check(fake.Clip&&fake.Enables==1,"one clip write before UI render");scope.ObserveAfterEye(0);scope.EndEye(0);
        check(!fake.Clip&&fake.Disables==1&&scope.Conflicts==0,"unclipped public flag restored after UI");
        scope.BeginEye(1);scope.ObserveAfterEye(1);scope.EndEye(1);scope.Dispose();scope.ObserveDesktop();
        check(fake.Enables==2&&fake.Disables==2,"each eye bounded one enable/disable");
        using(var json=JsonDocument.Parse(scope.DiagnosticJson()))
        {
            var value=json.RootElement;check(value.GetProperty("observations").GetArrayLength()==8,"all eight diagnostic phases retained");
            check(!value.GetProperty("foreignRectangleReplacementDetectable").GetBoolean()&&!value.GetProperty("completeNativeStateRestored").GetBoolean(),"diagnostic discloses rectangle and native-state limits");
            check(value.GetProperty("inheritedAlphaUnavailableReason").GetString()=="public_getter_unavailable","unsupported derived getter remains unavailable");
            var row=value.GetProperty("observations")[0].GetProperty("renderers")[0];
            check(row.GetProperty("childRootLocalRectangle")[0].GetSingle()==90&&row.GetProperty("materialIds")[0].GetInt32()==3,"diagnostic records outside child and material identities");
        }
        foreach(var invalid in new[]{new Fake {Clip=true},new Fake {Count=0},new Fake {Count=8193},new Fake {InventoryCount=65537},new Fake {Width=Single.NaN},new Fake {ThrowCapture=true}})
        {Refuses(()=>new UiPanelClipScope(invalid),check,"invalid admission refuses before native writes");check(invalid.Enables==0&&invalid.Disables==0,"refused inventory untouched");}
        fake=new Fake {ThrowEnable=true};scope=new UiPanelClipScope(fake);scope.RecordPanelPlacement();Refuses(()=>scope.BeginEye(0),check,"void setter failure surfaced");scope.EndEye(0);scope.Dispose();
        check(fake.Disables==1&&!fake.Clip,"potentially written failing enable receives owned bounded cleanup");
        fake=new Fake();scope=new UiPanelClipScope(fake);scope.RecordPanelPlacement();scope.BeginEye(0);fake.Parent=new object();scope.EndEye(0);scope.Dispose();
        check(fake.Disables==0&&scope.Conflicts>0&&fake.Clip,"changed parent preserved without blind cleanup");
        fake=new Fake();scope=new UiPanelClipScope(fake);scope.RecordPanelPlacement();scope.BeginEye(0);fake.Changed=true;scope.EndEye(0);scope.Dispose();
        check(fake.Disables==0,"foreign component addition prevents cleanup write");
        fake=new Fake();scope=new UiPanelClipScope(fake);scope.RecordPanelPlacement();scope.BeginEye(0);fake.ThrowCapture=true;scope.EndEye(0);scope.Dispose();
        check(fake.Disables==0&&fake.Clip&&scope.Conflicts>0,"lost required cleanup query preserves uncertain state");
        fake=new Fake();scope=new UiPanelClipScope(fake);scope.RecordPanelPlacement();scope.BeginEye(0);fake.Clip=false;scope.ObserveAfterEye(0);scope.EndEye(0);scope.Dispose();
        check(fake.Disables==0&&scope.Conflicts>0,"callback-disabled flag is reported without redundant setter");
        fake=new Fake {ThrowDisable=true};scope=new UiPanelClipScope(fake);scope.RecordPanelPlacement();scope.BeginEye(0);scope.EndEye(0);scope.Dispose();
        check(fake.Disables==1&&scope.Conflicts>0,"failed cleanup never receives second blind write");
        fake=new Fake();scope=new UiPanelClipScope(fake);scope.RecordPanelPlacement();scope.BeginEye(0);fake.Alpha=.4f;scope.ObserveAfterEye(0);scope.EndEye(0);scope.Dispose();
        check(fake.Alpha==.4f&&!fake.Clip&&scope.Conflicts>0,"foreign alpha change retained and reported, clip flag cleaned");
        fake=new Fake();scope=new UiPanelClipScope(fake);scope.RecordPanelPlacement();fake.Clip=true;Refuses(()=>scope.BeginEye(0),check,"new clip before eye refuses");scope.Dispose();check(fake.Disables==0,"new foreign clip preserved");
        fake=new Fake {Count=65};scope=new UiPanelClipScope(fake);scope.Dispose();
        using(var json=JsonDocument.Parse(scope.DiagnosticJson()))check(json.RootElement.GetProperty("observations")[0].GetProperty("droppedDetailedRenderers").GetInt32()==1,"diagnostic cap reports omitted renderer");
        fake=new Fake {Optional=true};scope=new UiPanelClipScope(fake);scope.Dispose();
        using(var json=JsonDocument.Parse(scope.DiagnosticJson()))check(json.RootElement.GetProperty("softnessAvailable").GetBoolean()&&json.RootElement.GetProperty("inheritedAlphaUnavailableReason").ValueKind==JsonValueKind.Null,"supported optional getters reported as available");
        fake=new Fake();scope=new UiPanelClipScope(fake);scope.RecordPanelPlacement();scope.BeginEye(0);scope.Dispose();
        using(var json=JsonDocument.Parse(scope.DiagnosticJson()))check(!fake.Clip&&json.RootElement.GetProperty("observations")[2].GetProperty("phase").GetString()=="after_left_cleanup","Dispose retains actual admitted-eye cleanup phase");

        var root=NativeRoot();var adapter=Adapter(root);var snapshot=adapter.Capture(false);
        check(snapshot.Nodes.Length==1&&snapshot.Nodes[0].Panel.width==100&&snapshot.Nodes[0].ConvertedPanel.width==100,"public default Image and actual root conversion admitted");
        var r=root.Rects[0];r.WorldCorners=points=>{points[0]=new Vector3(10,20,0);points[1]=new Vector3(-90,20,0);points[2]=new Vector3(-90,220,0);points[3]=new Vector3(10,220,0);};r.InversePoint=p=>new Vector3((p.y-20)/2,(10-p.x)/2,0);
        snapshot=adapter.Capture(false);check(snapshot.Nodes[0].ConvertedPanel.width==100&&snapshot.Nodes[0].ConvertedPanel.height==50,"rotated scaled world corners return canvas-local rectangle");
        foreach(var mutate in new Action<Canvas>[] {
            c=>c.ClipComponents=new Component[]{c,null},
            c=>c.ClipComponents=new Component[]{c,new RectMask2D()},
            c=>c.ClipComponents=new Component[]{c,new Mask()},
            c=>c.ClipComponents=new Component[]{c,new Modifier()},
            c=>c.ClipComponents=new Component[]{c,new Canvas()},
            c=>c.ClipComponents=new Component[]{c,new CustomImage()},
            c=>c.Renderers[0].gameObject.activeSelf=false,
            c=>c.Renderers[0].Materials=new[]{new Material()},
            c=>c.Renderers[0].Materials=new Material[9],
            c=>c.Renderers[0].AttachedComponents=new Component[0],
            c=>c.Rects[0].parent=new Transform {AttachedComponents=new Component[]{new Mask()}},
            c=>c.ClipComponents=new Component[65537]
        })
        {root=NativeRoot();mutate(root);Refuses(()=>Adapter(root).Capture(false),check,"unsupported native adapter path refuses");check(root.Renderers[0].ClipEnables==0,"adapter admission never clips");}
        root=NativeRoot();root.Renderers[0].ClipValue=true;Refuses(()=>new UiPanelClipScope(Adapter(root)),check,"preexisting native clip refused");
        root=NativeRoot();root.Renderers[0].AlphaValue=Single.PositiveInfinity;Refuses(()=>new UiPanelClipScope(Adapter(root)),check,"nonfinite native scalar refused");
        root=NativeRoot();root.Renderers[0].clippingSoftness=new Vector2(1,0);Refuses(()=>new UiPanelClipScope(Adapter(root)),check,"existing softness refused");
        root=NativeRoot();root.Renderers[0].PopMaterials=new[]{Graphic.Default};Refuses(()=>new UiPanelClipScope(Adapter(root)),check,"pop material path refused");
        root=NativeRoot();root.Renderers[0].ThrowCull=true;Refuses(()=>new UiPanelClipScope(Adapter(root)),check,"required public getter error refuses");
        root=NativeRoot();var canonicalText=new Text {canvas=root,canvasRenderer=root.Renderers[0],rectTransform=root.Rects[0]};root.ClipComponents=new Component[]{root,root.Rects[0],root.Renderers[0],canonicalText};root.Renderers[0].AttachedComponents=new Component[]{canonicalText};
        check(Adapter(root).Capture(false).Nodes.Length==1,"exact Text with actual default-material reference admitted");
        canonicalText.material=new Material();Refuses(()=>Adapter(root).Capture(false),check,"Text with separate assigned material refuses entire root");
        root=NativeRoot();root.Renderers[0].AttachedComponents=new Component[]{root.ClipComponents[3],new Modifier()};Refuses(()=>Adapter(root).Capture(false),check,"modifier inserted between hierarchy and attached inventories refuses");
        Canvas.Instances.Clear();root=NativeRoot();Canvas.Instances.Add(root);int defaultQueries=Graphic.DefaultQueries;
        var overlay=new OverlayCanvasScope(new Camera {nearClipPlane=.01f,farClipPlane=100},1,2,true);overlay.Dispose();
        check(Graphic.DefaultQueries==defaultQueries&&root.Renderers[0].NativeQueries==0&&root.Renderers[0].ClipEnables==0,"default overlay path makes no clipping/material observations or writes");
        Resources.Extras.Clear();LayerMask.Name=layer=>"layer";
        fake=new Fake();var nativeUi=new Camera();var game=new Camera {nearClipPlane=.01f,farClipPlane=100};
        var clipped=new OverlayCanvasScope(game,1,2,true,1.8,false,null,false,1024,UiPanelClipping.CanonicalUguiDefaultRootV1,roots=>new UiPanelClipScope(fake));
        clipped.SetCommonHead(new RigidPose(new Position(0,0,0),Orientation.Identity));clipped.UseAfterEffectsCamera(nativeUi,20);
        int flushes=0;Canvas.ForceAction=()=>{++flushes;check(!fake.Clip,"native crop disabled at each desktop/layout flush");};
        game.RenderAction=()=>check(!fake.Clip&&fake.Enables==0,"game eye runs before native crop");
        nativeUi.RenderAction=()=>{check(fake.Clip&&fake.Enables==1,"UI eye runs inside native crop");throw new InvalidOperationException();};
        clipped.ApplyEye(0);clipped.RenderGameEye(0);Refuses(()=>clipped.RenderUiEye(0),check,"UI render failure reaches cleanup finally");
        check(!fake.Clip&&fake.Disables==1,"UI exception disables crop before outer restoration");clipped.Dispose();clipped.RebuildRestoredGeometry();
        check(flushes>=3&&clipped.RestorationConflicts==0,"final desktop rebuild observes unclipped state: "+clipped.RestorationError);
        bool factoryCalled=false;
        var ordinary=new OverlayCanvasScope(game,1,2,true,1.8,false,null,false,1024,UiPanelClipping.None,roots=>{factoryCalled=true;throw new Exception();});ordinary.Dispose();
        check(!factoryCalled,"default-off construction does not call clipping adapter factory");
        Canvas.Instances.Clear();Canvas.ForceAction=null;
        ZeroRootChecks(check);
    }
    static void ZeroRootChecks(Action<bool,string> check)
    {
        Canvas.Instances.Clear();Resources.Extras.Clear();Canvas.ForceAction=null;
        int materialQueries=Graphic.DefaultQueries,layerQueries=Resources.QueryTypes.Count,factoryCalls=0,renders=0,flushes=0;
        var camera=new Camera {nearClipPlane=.01f,farClipPlane=100,cullingMask=123};
        Canvas.ForceAction=()=>++flushes;
        var scope=new OverlayCanvasScope(camera,1,2,true,1.8,false,null,false,1024,UiPanelClipping.CanonicalUguiDefaultRootV1,
            roots=>{++factoryCalls;throw new Exception("zero roots must never reach clip admission");});
        camera.RenderAction=()=>++renders;
        scope.UseAfterEffectsCamera(new Camera(),20);scope.ApplyEye(0);scope.RenderGameEye(0);scope.ApplyEye(1);scope.RenderGameEye(1);
        scope.Dispose();scope.RebuildRestoredGeometry();
        check(renders==2&&camera.cullingMask==123&&scope.SelectedCount==0&&scope.IsolatedMask==0&&scope.RestorationConflicts==0,"zero selected roots preserve ordinary two-eye rendering");
        check(factoryCalls==0&&Graphic.DefaultQueries==materialQueries&&Resources.QueryTypes.Count==layerQueries&&flushes==0,"zero selected roots perform no clip admission, material, layer or layout operations");
        using(var json=JsonDocument.Parse(scope.DiagnosticJson()))
        {
            var clip=json.RootElement.GetProperty("panelClipping");
            check(clip.GetProperty("status").GetString()=="not_applicable_no_selected_roots"&&!clip.GetProperty("admitted").GetBoolean()&&clip.GetProperty("selectedRootCount").GetInt32()==0,"selected zero-root snapshot explicitly not applicable");
            check(!clip.GetProperty("clipSpecificQueriesPerformed").GetBoolean()&&!clip.GetProperty("clipSettersPerformed").GetBoolean()&&!clip.GetProperty("uiCoverageClaimed").GetBoolean(),"not applicable does not claim sampled native UI or coverage");
            JsonElement unused;check(!clip.TryGetProperty("originalClipFlag",out unused)&&!clip.TryGetProperty("completeNativeStateRestored",out unused),"unsampled renderer flag and restoration remain unclaimed");
        }
        var ordinary=new OverlayCanvasScope(camera,1,2,true);
        using(var json=JsonDocument.Parse(ordinary.DiagnosticJson())){JsonElement unused;check(!json.RootElement.TryGetProperty("panelClipping",out unused),"default off retains diagnostic omission for zero roots");}
        ordinary.Dispose();
        bool invalid=false;try {new OverlayCanvasScope(camera,1,2,true,1.8,false,null,false,1024,(UiPanelClipping)999);}catch(ArgumentException){invalid=true;}
        check(invalid,"zero roots do not suppress invalid clipping-mode refusal");
        Refuses(()=>new UiPanelClipScope(new Fake {Count=0}),check,"explicit empty clip inventory still refuses");
        try {new CanonicalUguiPanelClipAccess(new Canvas[65],typeof(Image).Assembly,g=>true);check(false,"excess roots must refuse");}
        catch(InvalidOperationException error){check(error.Message=="panel_clip_root_limit_exceeded","excess-root refusal is bounded and distinct from empty roots");}
        try {new CanonicalUguiPanelClipAccess(new Canvas[0],typeof(Image).Assembly,g=>true);check(false,"explicit empty adapter must refuse");}
        catch(InvalidOperationException error){check(error.Message=="panel_clip_empty_roots","explicit empty adapter remains a distinct refusal");}

        // A zero-root snapshot must not promise coverage of a later-born canvas.
        var later=NativeRoot();
        var emptySnapshot=new OverlayCanvasScope(camera,1,2,true,1.8,false,null,false,1024,UiPanelClipping.CanonicalUguiDefaultRootV1,
            roots=>{++factoryCalls;throw new Exception();});
        Canvas.Instances.Add(later);emptySnapshot.ApplyEye(0);emptySnapshot.RenderGameEye(0);emptySnapshot.Dispose();
        check(emptySnapshot.SelectedCount==0&&factoryCalls==0&&later.Renderers[0].NativeQueries==0&&later.Renderers[0].ClipEnables==0,"later UI is outside the retained zero-root snapshot and untouched");
        var next=new OverlayCanvasScope(camera,1,2,true,1.8,false,null,false,1024,UiPanelClipping.CanonicalUguiDefaultRootV1,
            roots=>{++factoryCalls;check(roots.Length==1&&roots[0]==later,"fresh nonempty scope admits actual selected root");return new UiPanelClipScope(Adapter(roots[0]));});
        check(next.SelectedCount==1&&factoryCalls==1,"zero-root exception does not skip later fresh admission");next.Dispose();
        var bad=NativeRoot();bad.Renderers[0].Materials=new[]{new Material()};Canvas.Instances.Add(bad);
        Refuses(()=>new OverlayCanvasScope(camera,1,2,true,1.8,false,null,false,1024,UiPanelClipping.CanonicalUguiDefaultRootV1,
            roots=>{check(roots.Length==2,"all selected roots passed together to admission");return new UiPanelClipScope(new CanonicalUguiPanelClipAccess(roots,typeof(Image).Assembly,g=>true));}),check,"one unsupported nonempty root refuses the entire admission");
        check(later.Renderers[0].ClipEnables==0&&bad.Renderers[0].ClipEnables==0,"nonempty refusal never crops a partial root subset");
        Canvas.Instances.Clear();Canvas.ForceAction=null;Resources.Extras.Clear();
    }
}
