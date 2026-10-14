using System;
using System.Collections.Generic;
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
        Canvas.Instances.Clear();Canvas.ForceAction=null;
    }
}
