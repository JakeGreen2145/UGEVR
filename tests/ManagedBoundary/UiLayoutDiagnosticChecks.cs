using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using UnityEngine;
using Ugevr;

public static class UiDiagnosticCounter
{
    public static int Cached,Generators,Unsafe;
    public static bool FailFontSize;
    public static void Read(string property)
    {
        if(property=="preferredWidth") {++Generators;throw new Exception("Text generator must not be called");}
        if(property=="fontMaterial") {++Unsafe;throw new Exception("Material generator must not be called");}
        ++Cached;
        if(property=="fontSize" && FailFontSize) throw new Exception("Controlled cached getter failure");
    }
}

static class UiLayoutDiagnosticChecks
{
    static Component Fixture(string name,string assembly,Dictionary<string,object> values)
    {
        var module=AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(assembly),AssemblyBuilderAccess.Run).DefineDynamicModule("fixture");
        var builder=module.DefineType(name,TypeAttributes.Public,typeof(Component));
        builder.DefineDefaultConstructor(MethodAttributes.Public);
        foreach(var value in values)
        {
            Type type=value.Value==null?typeof(string):value.Value.GetType();
            var field=builder.DefineField("input_"+value.Key,type,FieldAttributes.Public);
            var property=builder.DefineProperty(value.Key,PropertyAttributes.None,type,Type.EmptyTypes);
            var getter=builder.DefineMethod("get_"+value.Key,MethodAttributes.Public|MethodAttributes.SpecialName|MethodAttributes.HideBySig,type,Type.EmptyTypes);
            var il=getter.GetILGenerator();il.Emit(OpCodes.Ldstr,value.Key);il.Emit(OpCodes.Call,typeof(UiDiagnosticCounter).GetMethod("Read"));il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldfld,field);il.Emit(OpCodes.Ret);property.SetGetMethod(getter);
        }
        var component=(Component)Activator.CreateInstance(builder.CreateType());
        foreach(var value in values) component.GetType().GetField("input_"+value.Key).SetValue(component,value.Value);
        return component;
    }
    static void Set(Component component,string name,object value) {component.GetType().GetField("input_"+name).SetValue(component,value);}
    static Dictionary<string,object> TextInputs()
    {
        return new Dictionary<string,object> { {"text","private menu text"},{"fontSize",32},{"fontStyle",1},{"font",new UnityEngine.Object()},{"preferredWidth",266.16f},{"fontMaterial",new UnityEngine.Object()} };
    }
    public static void Run(Action<bool,string> assert)
    {
        Canvas.Instances.Clear();Canvas.ForceAction=null;UiDiagnosticCounter.Cached=UiDiagnosticCounter.Generators=UiDiagnosticCounter.Unsafe=0;
        var text=Fixture("UnityEngine.UI.Text","UnityEngine.UI",TextInputs());
        var group=Fixture("UnityEngine.UI.HorizontalLayoutGroup","UnityEngine.UI",new Dictionary<string,object> {{"spacing",60f},{"enabled",true},{"childControlWidth",true}});
        var rootRect=new RectTransform {sizeDelta=new Vector2(1920,1080),localScale=new Vector3(1,1,1),localRotation=new Quaternion(0,0,0,1)};
        var groupRect=new RectTransform {name="LayoutGroup",parent=rootRect,sizeDelta=new Vector2(386.16f,44.25f),localScale=new Vector3(1,1,1),localRotation=new Quaternion(0,0,0,1),AttachedComponents=new[]{group}};
        var textRect=new RectTransform {name="Text",parent=groupRect,sizeDelta=new Vector2(266.16f,44.25f),localScale=new Vector3(1,1,1),localRotation=new Quaternion(0,0,0,1),AttachedComponents=new[]{text}};
        var canvas=new Canvas {name="menu",renderMode=RenderMode.ScreenSpaceOverlay,scaleFactor=2,Rects=new[]{rootRect,groupRect,textRect}};rootRect.gameObject=canvas.gameObject;
        Canvas.Instances.Add(canvas);var camera=new Camera();
        using(var disabled=new OverlayCanvasScope(camera,1,2,false)) {disabled.SetCommonHead(new RigidPose(new Position(0,0,0),Orientation.Identity));disabled.ApplyEye();}
        assert(UiDiagnosticCounter.Cached==0,"Layout input diagnostics remain inactive by default");
        var scope=new OverlayCanvasScope(camera,1,2,false,1.8,true);scope.SetCommonHead(new RigidPose(new Position(0,0,0),Orientation.Identity));
        using(scope)
        {
            scope.ApplyEye(0);textRect.sizeDelta=new Vector2(266.91f,44.25f);groupRect.sizeDelta=new Vector2(386.91f,44.25f);scope.RecordAfterRender(0);
            scope.ApplyEye(1);scope.RecordAfterRender(1);
        }
        Canvas.ForceAction=delegate {textRect.sizeDelta=new Vector2(266.91f,44.25f);groupRect.sizeDelta=new Vector2(386.91f,44.25f);};scope.RebuildRestoredGeometry();Canvas.ForceAction=null;
        string json=scope.DiagnosticJson();
        using(var document=JsonDocument.Parse(json))
        {
            var diagnostic=document.RootElement.GetProperty("layoutInputDiagnostic");
            assert(diagnostic.GetProperty("summary").GetProperty("knownInputComponentCount").GetInt32()==2,"Exact standard text and layout input components are registered");
            var textContext=diagnostic.GetProperty("mismatchContexts")[1].GetProperty("nodes")[0].GetProperty("knownEngineInputs")[0];
            var phases=textContext.GetProperty("phases");
            string original=phases.GetProperty("snapshot").GetProperty("inputs").GetProperty("text").GetProperty("sha256").GetString();
            assert(original==phases.GetProperty("final-rebuild").GetProperty("inputs").GetProperty("text").GetProperty("sha256").GetString(),"Derived reflow preserves a comparable semantic text hash across phases");
            assert(phases.GetProperty("direct-restoration").GetProperty("sizeDelta")[0].GetSingle()==266.16f && phases.GetProperty("final-rebuild").GetProperty("sizeDelta")[0].GetSingle()==266.91f,"Diagnostic identifies the final rebuild's coherent size change after exact direct restoration");
            assert(phases.GetProperty("snapshot").GetProperty("canvasMode").GetString()=="ScreenSpaceOverlay" && phases.GetProperty("applied-left").GetProperty("canvasMode").GetString()=="WorldSpace","Inputs retain the canvas mode context of each observation");
            assert(diagnostic.GetProperty("mismatchContexts")[1].GetProperty("nodes")[1].GetProperty("knownEngineInputs")[0].GetProperty("type").GetString()=="UnityEngine.UI.HorizontalLayoutGroup","A mismatched child includes its ancestor's known layout inputs");
        }
        assert(!json.Contains("private menu text") && !json.Contains("preferredWidth") && !json.Contains("fontMaterial"),"Evidence contains hashes instead of plaintext and excludes generator/material getters");
        assert(scope.RestorationConflicts==2,"Input diagnostics leave strict restoration failure unchanged");
        assert(UiDiagnosticCounter.Generators==0 && UiDiagnosticCounter.Unsafe==0,"No preferred-width or material getter executes in any phase or during serialization");
        var direct=new UiLayoutDiagnostics();direct.Add(textRect,canvas);direct.Capture(0);
        Set(text,"text","a changed label");Set(text,"fontSize",36);Set(text,"font",new UnityEngine.Object());direct.Capture(2);
        using(var document=JsonDocument.Parse(direct.Context(textRect.GetInstanceID())))
        {
            var phases=document.RootElement.GetProperty("nodes")[0].GetProperty("knownEngineInputs")[0].GetProperty("phases");
            var before=phases.GetProperty("snapshot").GetProperty("inputs");var after=phases.GetProperty("after-render-left").GetProperty("inputs");
            assert(before.GetProperty("text").GetProperty("sha256").GetString()!=after.GetProperty("text").GetProperty("sha256").GetString() && after.GetProperty("fontSize").GetInt32()==36,"Changed cached text/font inputs remain distinguishable from derived-only reflow");
            assert(before.GetProperty("font").GetProperty("assetInstanceId").GetInt32()!=after.GetProperty("font").GetProperty("assetInstanceId").GetInt32(),"Font asset identities are frozen at each phase");
        }
        int reads=UiDiagnosticCounter.Cached;var untrusted=Fixture("UnityEngine.UI.Text","Untrusted.Game",TextInputs());var unknownRect=new RectTransform {AttachedComponents=new[]{untrusted}};
        var ignored=new UiLayoutDiagnostics();ignored.Add(unknownRect,canvas);ignored.Capture(0);ignored.Context(unknownRect.GetInstanceID());
        assert(ignored.ComponentCount==0 && UiDiagnosticCounter.Cached==reads,"A same-name game component from another assembly cannot execute cached getters");
        var custom=Fixture("Game.CustomText","UnityEngine.UI",TextInputs());unknownRect.AttachedComponents=new[]{custom};ignored=new UiLayoutDiagnostics();ignored.Add(unknownRect,canvas);ignored.Capture(0);
        assert(ignored.ComponentCount==0 && UiDiagnosticCounter.Cached==reads,"Unknown component types from a known assembly are also excluded");
        var tmp=Fixture("TMPro.TextMeshProUGUI","Unity.TextMeshPro",TextInputs());var tmpRect=new RectTransform {AttachedComponents=new[]{tmp}};
        var tmpDiagnostic=new UiLayoutDiagnostics();tmpDiagnostic.Add(tmpRect,canvas);tmpDiagnostic.Capture(0);
        assert(tmpDiagnostic.ComponentCount==1 && tmpDiagnostic.Context(tmpRect.GetInstanceID()).Contains("\"fontSize\":32") && UiDiagnosticCounter.Generators==0 && UiDiagnosticCounter.Unsafe==0,"Exact trusted TMP components expose cached inputs without preferred-size or material generation");
        UiDiagnosticCounter.FailFontSize=true;direct.Capture(3);UiDiagnosticCounter.FailFontSize=false;
        assert(direct.Context(textRect.GetInstanceID()).Contains("cached input query failed") && direct.Summary().Contains("\"queryErrors\":1"),"A cached getter failure is bounded and reported without changing restoration policy");
        text.Destroyed=true;direct.Capture(4);
        assert(direct.Context(textRect.GetInstanceID()).Contains("\"destroyed\":true"),"Destroyed engine components are recorded without property access");text.Destroyed=false;
        Set(text,"text",new string('q',65537));direct.Capture(7);
        assert(direct.Context(textRect.GetInstanceID()).Contains("\"hashedUtf16Length\":65536,\"truncated\":true"),"Oversized text hashes expose their exact bounded scope");
        var crowded=new UiLayoutDiagnostics();var extra=new Component[33];for(int i=0;i<extra.Length;++i) extra[i]=new Component();crowded.Add(new RectTransform {AttachedComponents=extra},canvas);
        assert(crowded.Summary().Contains("\"droppedComponents\":1"),"Per-node component inventory truncation is explicit");
        var deep=new UiLayoutDiagnostics();RectTransform parent=null,leaf=null;for(int i=0;i<9;++i) {leaf=new RectTransform {name="ancestor "+i,parent=parent};deep.Add(leaf,canvas);parent=leaf;}
        using(var document=JsonDocument.Parse(deep.Context(leaf.GetInstanceID()))) assert(document.RootElement.GetProperty("nodes").GetArrayLength()==8 && document.RootElement.GetProperty("remainingAncestorId").GetInt32()!=0,"Ancestor traversal is bounded and exposes omitted ancestry");
        var componentBound=new UiLayoutDiagnostics();
        for(int i=0;i<1025;++i) componentBound.Add(new RectTransform {AttachedComponents=new[]{(Component)Activator.CreateInstance(text.GetType())}},canvas);
        assert(componentBound.ComponentCount==1024 && componentBound.Summary().Contains("\"droppedComponents\":1"),"Cached input component admission stops at its global bound without hiding truncation");
        var nodeBound=new UiLayoutDiagnostics();for(int i=0;i<8193;++i) nodeBound.Add(new RectTransform(),canvas);
        assert(nodeBound.Summary().Contains("\"nodeCount\":8192") && nodeBound.Summary().Contains("\"droppedNodes\":1"),"Node inventory also stops at its explicit global bound");
        TargetedChecks(assert,text,group,canvas);
        Canvas.Instances.Clear();Canvas.ForceAction=null;
    }

    static void Register(UiLayoutDiagnostics diagnostic,RectTransform rect,Canvas canvas)
    {
        diagnostic.RegisterSnapshot(rect,canvas,rect.GetInstanceID(),rect.name);
    }
    static int Count(UiLayoutDiagnostics diagnostic,string field)
    {
        using(var document=JsonDocument.Parse(diagnostic.Summary())) return document.RootElement.GetProperty(field).GetInt32();
    }
    static void TargetedChecks(Action<bool,string> assert,Component text,Component group,Canvas canvas)
    {
        Set(text,"text","private menu text");
        var ancestor=new RectTransform {name="Selector parent",InstanceIdOverride=-200};
        var selected=new RectTransform {name="LayoutGroup",InstanceIdOverride=-123,parent=ancestor,AttachedComponents=new[]{group}};
        var child=new RectTransform {name="Text",parent=selected,AttachedComponents=new[]{text}};
        var unrelated=new RectTransform[500];
        for(int i=0;i<unrelated.Length;++i) unrelated[i]=new RectTransform {AttachedComponents=new[]{text}};
        var filtered=new UiLayoutDiagnostics(" -123 ");Register(filtered,ancestor,canvas);Register(filtered,selected,canvas);Register(filtered,child,canvas);
        foreach(var rect in unrelated) Register(filtered,rect,canvas);
        int reads=UiDiagnosticCounter.Cached;
        assert(selected.ComponentInventoryQueries==0 && Count(filtered,"componentInventoryQueries")==0 && UiDiagnosticCounter.Cached==reads,"Snapshot registration admits no components or getters before selector matching");
        filtered.FinishAdmission();for(int phase=0;phase<8;++phase) filtered.Capture(phase);
        assert(Count(filtered,"nodeCount")==503 && Count(filtered,"admittedNodeCount")==3 && Count(filtered,"componentInventoryQueries")==3 && filtered.ComponentCount==2,"Signed runtime root selection admits only its subtree and existing ancestors");
        bool untouched=true;foreach(var rect in unrelated) untouched&=rect.ComponentInventoryQueries==0;
        assert(untouched,"Five hundred unrelated snapshot nodes never execute a component inventory");
        using(var document=JsonDocument.Parse(filtered.Selection()))
        {
            var root=document.RootElement.GetProperty("requestedRoots")[0];
            assert(root.GetProperty("requestedRectId").GetInt32()==-123 && root.GetProperty("matched").GetBoolean() && root.GetProperty("observedName").GetString()=="LayoutGroup" && root.GetProperty("parentInstanceId").GetInt32()==-200 && root.GetProperty("parentName").GetString()=="Selector parent" && root.GetProperty("subtreeNodesAdmitted").GetInt32()==2,"Filtered evidence preserves actual signed identity, name, parent identity and subtree count");
        }
        using(var document=JsonDocument.Parse(filtered.SelectedContexts()))
        {
            var contexts=document.RootElement.GetProperty("contexts");
            var phases=contexts[1].GetProperty("nodes")[0].GetProperty("knownEngineInputs")[0].GetProperty("phases");
            assert(contexts.GetArrayLength()==2 && phases.GetProperty("snapshot").ValueKind==JsonValueKind.Object && phases.GetProperty("final-rebuild").ValueKind==JsonValueKind.Object && phases.EnumerateObject().Count()==8,"Healthy filtered captures expose the selected text/layout inputs in all eight phases without requiring a mismatch");
        }
        var full=new UiLayoutDiagnostics("  ");Register(full,ancestor,canvas);Register(full,selected,canvas);Register(full,child,canvas);foreach(var rect in unrelated) Register(full,rect,canvas);full.FinishAdmission();for(int phase=0;phase<8;++phase) full.Capture(phase);
        assert(Count(full,"admittedNodeCount")==503 && full.ComponentCount==502 && Count(full,"getterReads")>Count(filtered,"getterReads")*200 && full.Selection().Contains("full-snapshot") && full.SelectedContexts()=="null","Empty selector retains full coverage while explicit selection avoids over two hundred times the controlled fixture getter reads");
        var cached=new UiLayoutDiagnostics("-123");Register(cached,selected,canvas);Register(cached,child,canvas);cached.FinishAdmission();cached.Capture(0);
        assert(Count(cached,"metadataCacheMisses")==0 && Count(cached,"metadataCacheHits")==2,"Immutable admitted type metadata is reused by later collector instances");
        var excluded=Fixture("Game.Excluded","Untrusted.Game",TextInputs());var excludedRect=new RectTransform {AttachedComponents=new[]{excluded}};
        var negativeFirst=new UiLayoutDiagnostics();negativeFirst.Add(excludedRect,canvas);var negativeAgain=new UiLayoutDiagnostics();negativeAgain.Add(excludedRect,canvas);reads=UiDiagnosticCounter.Cached;negativeAgain.Capture(0);
        assert(Count(negativeAgain,"metadataCacheHits")==1 && Count(negativeAgain,"metadataCacheMisses")==0 && UiDiagnosticCounter.Cached==reads,"Persistent negative schemas skip repeated reflection without permitting excluded getters");
        var missing=new UiLayoutDiagnostics("2147483647");Register(missing,selected,canvas);missing.FinishAdmission();missing.Capture(0);
        assert(Count(missing,"componentInventoryQueries")==0 && missing.Selection().Contains("\"matched\":false") && !missing.Selection().Contains("invalid-selector"),"Missing valid runtime roots are visible and do not silently expand coverage");
        assert(missing.Selection().Contains("\"status\":\"no-matched-roots\"") && missing.Selection().Contains("\"missingRootCount\":1"),"Missing root coverage has an explicit diagnostic status");
        string[] invalid={"0","2147483648","-2147483649","-123,-123","abc","1,",new string('1',257),new string(' ',257),"1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17"};
        foreach(string selector in invalid)
        {
            var bad=new UiLayoutDiagnostics(selector);bad.Add(selected,canvas);bad.FinishAdmission();bad.Capture(0);
            assert(Count(bad,"componentInventoryQueries")==0 && bad.Selection().Contains("invalid-selector") && bad.SelectedContexts()=="null","Invalid runtime selector is diagnostic-only and performs no component queries: "+selector.Substring(0,Math.Min(selector.Length,40)));
        }
        var extremes=new UiLayoutDiagnostics("-2147483648,2147483647");extremes.FinishAdmission();
        assert(!extremes.Selection().Contains("invalid-selector") && extremes.Selection().Contains("-2147483648"),"Both nonzero signed Int32 endpoints remain valid runtime selectors");
        var sixteen=new UiLayoutDiagnostics("1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16");sixteen.FinishAdmission();
        assert(!sixteen.Selection().Contains("invalid-selector"),"Sixteen explicit roots remain within the selector bound");
        var broadRoot=new RectTransform {name="Broad root"};var capped=new UiLayoutDiagnostics(broadRoot.GetInstanceID().ToString(CultureInfo.InvariantCulture));Register(capped,broadRoot,canvas);
        for(int i=0;i<300;++i) Register(capped,new RectTransform {parent=broadRoot,AttachedComponents=new[]{text}},canvas);
        capped.FinishAdmission();capped.Capture(0);
        assert(Count(capped,"admittedNodeCount")==256 && capped.Selection().Contains("\"droppedSubtreeFrontierNodes\":45"),"Filtered subtree admission is bounded before component inventory and reports its unvisited frontier precisely");
        using(var document=JsonDocument.Parse(capped.SelectedContexts())) assert(document.RootElement.GetProperty("contexts").GetArrayLength()==64 && document.RootElement.GetProperty("droppedContexts").GetInt32()==191,"Healthy selected input context serialization has its own explicit cap");
        RectTransform top=null,deep=null;var ancestors=new List<RectTransform>();for(int i=0;i<12;++i) {deep=new RectTransform {parent=top};ancestors.Add(deep);top=deep;}
        var deepFilter=new UiLayoutDiagnostics(deep.GetInstanceID().ToString(CultureInfo.InvariantCulture));foreach(var rect in ancestors) Register(deepFilter,rect,canvas);deepFilter.FinishAdmission();
        assert(Count(deepFilter,"admittedNodeCount")==9,"Filtered ancestry adds at most eight ancestors beyond the requested root");
        var padding=new RectOffset {left=1,right=2,top=3,bottom=4};var padded=Fixture("UnityEngine.UI.VerticalLayoutGroup","UnityEngine.UI",new Dictionary<string,object>{{"padding",padding}});
        var paddedRect=new RectTransform {AttachedComponents=new[]{padded}};var paddingDiagnostic=new UiLayoutDiagnostics();paddingDiagnostic.Add(paddedRect,canvas);paddingDiagnostic.Capture(0);padding.left=7;paddingDiagnostic.Capture(7);
        string paddingJson=paddingDiagnostic.Context(paddedRect.GetInstanceID());
        assert(paddingJson.Contains("\"padding\":[1,2,3,4]") && paddingJson.Contains("\"padding\":[7,2,3,4]"),"Exact engine RectOffset values are frozen per phase without retaining mutable padding references");
        Canvas.Instances.Clear();var guardRect=new RectTransform {name="guard",sizeDelta=new Vector2(100,100),localScale=new Vector3(1,1,1),localRotation=new Quaternion(0,0,0,1)};
        var guardCanvas=new Canvas {name="guard canvas",renderMode=RenderMode.ScreenSpaceOverlay,Rects=new[]{guardRect}};guardRect.gameObject=guardCanvas.gameObject;Canvas.Instances.Add(guardCanvas);
        var guarded=new OverlayCanvasScope(new Camera(),1,2,false,1.8,true,"0");guarded.SetCommonHead(new RigidPose(new Position(0,0,0),Orientation.Identity));using(guarded) {guarded.ApplyEye(0);guarded.RecordAfterRender(0);}
        Canvas.ForceAction=delegate {guardRect.sizeDelta=new Vector2(100.75f,100);};guarded.RebuildRestoredGeometry();Canvas.ForceAction=null;
        assert(guarded.RestorationConflicts==1 && guarded.DiagnosticJson().Contains("invalid-selector"),"Invalid diagnostic selection leaves the strict production restoration guard active");
        var module=AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("NegativeMetadataFixtures"),AssemblyBuilderAccess.Run).DefineDynamicModule("fixture");
        var negatives=new UiLayoutDiagnostics();
        for(int i=0;i<270;++i)
        {
            var type=module.DefineType("UnknownComponent"+i,TypeAttributes.Public,typeof(Component));type.DefineDefaultConstructor(MethodAttributes.Public);
            negatives.Add(new RectTransform {AttachedComponents=new[]{(Component)Activator.CreateInstance(type.CreateType())}},canvas);
        }
        assert(Count(negatives,"metadataCacheCount")==256 && negatives.ComponentCount==0,"Persistent negative type schemas stop at 256 entries and never admit unknown getters");
    }
}
