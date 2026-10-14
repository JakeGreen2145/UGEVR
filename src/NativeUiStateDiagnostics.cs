using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace Ugevr
{
    // Observations only: no cull/clip/alpha setter and no additional canvas or scene query.
    public sealed class NativeUiStateDiagnostics
    {
        public const int RendererLimit=1024,AdmissionOccurrenceLimit=8192,PhaseCount=6;
        static readonly string[] PhaseNames={"before-borrow","after-game-left","after-ui-left","after-game-right","after-ui-right","after-restoration"};
        sealed class Entry {public CanvasRenderer Renderer;public int Id,RootId;public string Name;public readonly Sample[] Samples=new Sample[PhaseCount];}
        struct Sample {public bool Captured,Destroyed;public bool? Cull,RectClipping;public float? LocalAlpha,InheritedAlpha;public int Errors,Nonfinite;}
        struct Context {public bool Captured;public int? ActiveTexture,CurrentCamera,CurrentTarget,SelectedTarget;public int Errors;}
        readonly List<Entry> entries=new List<Entry>();
        readonly HashSet<int> seen=new HashSet<int>();
        readonly Context[] contexts=new Context[PhaseCount];
        readonly Camera camera;
        readonly MethodInfo inheritedAlpha;
        readonly bool alphaLookupFailed;
        int encountered,dropped,duplicates,admissionErrors,rendererQueries,contextQueries;
        public static bool ShouldCapture(bool configured,bool imagesEnabled,bool requestPresentBeforePair) {return configured && imagesEnabled && requestPresentBeforePair;}
        public NativeUiStateDiagnostics(Camera camera)
        {
            this.camera=camera;
            try {inheritedAlpha=FindCachedFloatMethod(typeof(CanvasRenderer),"GetInheritedAlpha");}
            catch {alphaLookupFailed=true;}
        }
        public static MethodInfo FindCachedFloatMethod(Type type,string name)
        {
            MethodInfo method=type.GetMethod(name,BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,null,Type.EmptyTypes,null);
            return method!=null && !method.IsVirtual && !method.ContainsGenericParameters && method.ReturnType==typeof(float)?method:null;
        }
        public void Admit(CanvasRenderer renderer,int rootId)
        {
            ++encountered;
            // Stop admission getters at the bound. Later occurrences include possible duplicates.
            if(encountered>AdmissionOccurrenceLimit || entries.Count==RendererLimit) {++dropped;return;}
            try
            {
                if(renderer==null) {++admissionErrors;return;}
                int id=renderer.GetInstanceID();if(seen.Contains(id)) {++duplicates;return;}
                string name=renderer.name ?? "";
                entries.Add(new Entry {Renderer=renderer,Id=id,RootId=rootId,Name=name.Length>64?name.Substring(0,64):name});
                seen.Add(id);
            }
            catch {++admissionErrors;}
        }
        public void Capture(int phase)
        {
            if(phase<0 || phase>=PhaseCount) throw new ArgumentOutOfRangeException("phase");
            if(contexts[phase].Captured) return;
            Context context=new Context {Captured=true};
            try {++contextQueries;context.ActiveTexture=Id(RenderTexture.active);} catch {++context.Errors;}
            Camera current=null;
            try {++contextQueries;current=Camera.current;context.CurrentCamera=Id(current);} catch {++context.Errors;}
            if(current!=null) try {++contextQueries;context.CurrentTarget=Id(current.targetTexture);} catch {++context.Errors;}
            if(camera!=null) try {++contextQueries;context.SelectedTarget=Id(camera.targetTexture);} catch {++context.Errors;}
            contexts[phase]=context;
            foreach(Entry entry in entries)
            {
                Sample sample=new Sample {Captured=true};
                if(entry.Renderer==null) {sample.Destroyed=true;entry.Samples[phase]=sample;continue;}
                try {++rendererQueries;sample.Cull=entry.Renderer.cull;} catch {++sample.Errors;}
                try {++rendererQueries;sample.RectClipping=entry.Renderer.hasRectClipping;} catch {++sample.Errors;}
                try {++rendererQueries;sample.LocalAlpha=entry.Renderer.GetAlpha();if(!Finite(sample.LocalAlpha.Value)) ++sample.Nonfinite;} catch {++sample.Errors;}
                if(inheritedAlpha!=null)
                    try {++rendererQueries;sample.InheritedAlpha=(float)inheritedAlpha.Invoke(entry.Renderer,null);if(!Finite(sample.InheritedAlpha.Value)) ++sample.Nonfinite;} catch {++sample.Errors;}
                entry.Samples[phase]=sample;
            }
        }
        static int? Id(UnityEngine.Object value) {return value==null?(int?)null:value.GetInstanceID();}
        static bool Finite(float value) {return !Single.IsNaN(value) && !Single.IsInfinity(value);}
        static string Number(int? value) {return value.HasValue?value.Value.ToString(CultureInfo.InvariantCulture):"null";}
        static string Number(float? value) {return value.HasValue && Finite(value.Value)?value.Value.ToString("R",CultureInfo.InvariantCulture):"null";}
        static string Flag(bool? value) {return value.HasValue?(value.Value?"true":"false"):"null";}
        static string Quote(string value)
        {
            var text=new StringBuilder("\"");
            for(int index=0;index<value.Length;++index)
            {
                char character=value[index];
                if(character=='"' || character=='\\') text.Append('\\').Append(character);
                else if(Char.IsControl(character)) text.Append("\\u").Append(((int)character).ToString("x4",CultureInfo.InvariantCulture));
                else if(Char.IsHighSurrogate(character))
                {if(index+1<value.Length && Char.IsLowSurrogate(value[index+1])) text.Append(character).Append(value[++index]);else text.Append("\\ufffd");}
                else if(Char.IsLowSurrogate(character)) text.Append("\\ufffd");
                else text.Append(character);
            }
            return text.Append('"').ToString();
        }
        public string DiagnosticJson()
        {
            int errors=admissionErrors,nonfinite=0,destroyedSamples=0,capturedPhases=0;
            foreach(Context context in contexts) {errors+=context.Errors;if(context.Captured) ++capturedPhases;}
            foreach(Entry entry in entries) foreach(Sample sample in entry.Samples) {errors+=sample.Errors;nonfinite+=sample.Nonfinite;if(sample.Destroyed) ++destroyedSamples;}
            var text=new StringBuilder("{\"scope\":\"cached native UI fields on initially admitted selected-root renderers; not complete batch state\",\"rendererLimit\":").Append(RendererLimit)
                .Append(",\"admissionOccurrenceLimit\":").Append(AdmissionOccurrenceLimit)
                .Append(",\"encounteredOccurrences\":").Append(encountered).Append(",\"admittedRenderers\":").Append(entries.Count)
                .Append(",\"droppedOccurrences\":").Append(dropped).Append(",\"duplicateAdmittedOccurrences\":").Append(duplicates).Append(",\"admissionErrors\":").Append(admissionErrors)
                .Append(",\"queryErrors\":").Append(errors-admissionErrors).Append(",\"nonfiniteValues\":").Append(nonfinite).Append(",\"destroyedSamples\":").Append(destroyedSamples)
                .Append(",\"rendererGetterAttempts\":").Append(rendererQueries).Append(",\"contextGetterAttempts\":").Append(contextQueries).Append(",\"capturedPhases\":").Append(capturedPhases)
                .Append(",\"inheritedAlphaAvailable\":").Append(inheritedAlpha!=null?"true":"false").Append(",\"inheritedAlphaUnavailableReason\":").Append(inheritedAlpha==null?"\"public_method_not_present_or_shape_unsupported\"":"null")
                .Append(",\"inheritedAlphaLookupFailed\":").Append(alphaLookupFailed?"true":"false")
                .Append(",\"nativeStateRestored\":false,\"newRendererAdmissionAfterBorrow\":false,\"contexts\":[");
            for(int phase=0;phase<PhaseCount;++phase)
            {
                if(phase!=0) text.Append(',');Context context=contexts[phase];
                text.Append("{\"phase\":").Append(Quote(PhaseNames[phase])).Append(",\"captured\":").Append(context.Captured?"true":"false")
                    .Append(",\"activeRenderTextureId\":").Append(Number(context.ActiveTexture)).Append(",\"currentCameraId\":").Append(Number(context.CurrentCamera))
                    .Append(",\"currentCameraTargetId\":").Append(Number(context.CurrentTarget)).Append(",\"selectedCameraTargetId\":").Append(Number(context.SelectedTarget)).Append(",\"queryErrors\":").Append(context.Errors).Append('}');
            }
            text.Append("],\"renderers\":[");
            for(int i=0;i<entries.Count;++i)
            {
                if(i!=0) text.Append(',');Entry entry=entries[i];
                text.Append("{\"rendererId\":").Append(entry.Id).Append(",\"rootCanvasId\":").Append(entry.RootId).Append(",\"name\":").Append(Quote(entry.Name)).Append(",\"phases\":[");
                for(int phase=0;phase<PhaseCount;++phase)
                {
                    if(phase!=0) text.Append(',');Sample sample=entry.Samples[phase];
                    text.Append("{\"phase\":").Append(Quote(PhaseNames[phase])).Append(",\"captured\":").Append(sample.Captured?"true":"false").Append(",\"destroyed\":").Append(sample.Destroyed?"true":"false")
                        .Append(",\"cull\":").Append(Flag(sample.Cull)).Append(",\"hasRectClipping\":").Append(Flag(sample.RectClipping))
                        .Append(",\"localAlpha\":").Append(Number(sample.LocalAlpha)).Append(",\"inheritedAlpha\":").Append(Number(sample.InheritedAlpha))
                        .Append(",\"nonfiniteValues\":").Append(sample.Nonfinite).Append(",\"queryErrors\":").Append(sample.Errors).Append('}');
                }
                text.Append("]}");
            }
            return text.Append("]}").ToString();
        }
    }
}
