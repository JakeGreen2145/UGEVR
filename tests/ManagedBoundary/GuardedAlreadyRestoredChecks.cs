using System;
using System.IO;
using Ugevr;
using UnityEngine;

static class GuardedAlreadyRestoredChecks
{
    // Raw float operands from the hashed v5 cached record. This controlled
    // boundary replays its final state, not Unity callbacks or writer identity.
    sealed class Probe : Transform
    {
        public Vector3 World,Local,Scale=new Vector3(1,1,1),Lossy=new Vector3(1,1,1);
        public Quaternion WorldRotation=new Quaternion(0,0,0,1),LocalRotation=new Quaternion(0,0,0,1);
        public Matrix4x4 Forward,Inverse;
        public int WorldWrites,WorldRotationWrites,LocalWrites,LocalRotationWrites,ScaleWrites;
        public bool ThrowQuery;
        void Read(){if(ThrowQuery)throw new Exception("private query");}
        public override Vector3 position {get {Read();return World;}set {++WorldWrites;World=value;}}
        public override Quaternion rotation {get {Read();return WorldRotation;}set {++WorldRotationWrites;WorldRotation=value;}}
        public override Vector3 localPosition {get {Read();return Local;}set {++LocalWrites;Local=value;if(parent!=null){var p=parent.position;var scale=parent.localScale;World=new Vector3(p.x+value.x*scale.x,p.y+value.y*scale.y,p.z+value.z*scale.z);}else World=value;}}
        public override Quaternion localRotation {get {Read();return LocalRotation;}set {++LocalRotationWrites;LocalRotation=WorldRotation=value;}}
        public override Vector3 localScale {get {Read();return Scale;}set {++ScaleWrites;Scale=value;}}
        public override Vector3 lossyScale {get {Read();return Lossy;}set {Lossy=value;}}
        public override Matrix4x4 localToWorldMatrix {get {Read();return Forward;}set {Forward=value;}}
        public override Matrix4x4 worldToLocalMatrix {get {Read();return Inverse;}set {Inverse=value;}}
        public int Writes {get {return WorldWrites+WorldRotationWrites+LocalWrites+LocalRotationWrites+ScaleWrites;}}
    }
    static readonly Vector3 SavedWorld=new Vector3(1920.0003662109375f,1080,-1870.6148681640625f);
    static readonly Vector3 SavedLocal=new Vector3(.00018310546875f,.000030517578125f,-935.30743408203125f);
    static readonly Vector3 OwnedWorld=new Vector3(1920.0291748046875f,1080,-1870.6148681640625f);
    static readonly Vector3 OwnedLocal=new Vector3(15.364584922790527f,0,-1066.666748046875f);
    static Matrix4x4 Matrix(float scale,Vector3 translation)
    {var a=new float[16];a[0]=a[5]=a[10]=scale;a[12]=translation.x;a[13]=translation.y;a[14]=translation.z;a[15]=1;return new Matrix4x4 {Elements=a};}
    static Probe Parent()
    {return new Probe {World=new Vector3(1920,1080,0),Local=new Vector3(1919.5f,1079.5f,0),Scale=new Vector3(2,2,2),Lossy=new Vector3(2,2,2),Forward=Matrix(2,new Vector3(1920,1080,0)),Inverse=Matrix(.5f,new Vector3(-960,-540,0))};}
    sealed class Case
    {
        public readonly Probe Child,Parent;
        public readonly Camera Camera;
        public readonly CameraState Scope;
        public readonly CameraRestorationPoseRecord Record;
        public Case(string root,CameraTransformRestoration mode=CameraTransformRestoration.GuardedLocal,bool owned=true)
        {
            Parent=GuardedAlreadyRestoredChecks.Parent();Child=new Probe {parent=Parent,World=SavedWorld,Local=SavedLocal,Lossy=new Vector3(2,2,2),Forward=Matrix(2,SavedWorld),Inverse=Matrix(.5f,new Vector3(-960.00018310546875f,-540,935.30743408203125f))};
            Camera=new Camera {transform=Child};Record=new CameraRestorationPoseRecord(new CameraRestorationPoseDiagnostics(root),Camera,116,1,116,Path.Combine(root,"not-published.yaml"));Scope=new CameraState(Camera,Record,mode);
            Child.World=OwnedWorld;Child.Local=OwnedLocal;if(owned)Scope.RecordOwnedEyePose();ReturnSaved();
        }
        public void ReturnSaved(){Child.World=SavedWorld;Child.Local=SavedLocal;}
    }
    static float Next(float value){int bits=BitConverter.ToInt32(BitConverter.GetBytes(value),0);return BitConverter.ToSingle(BitConverter.GetBytes(bits+1),0);}
    static void Refuses(Action<bool,string> check,Case c,string label)
    {
        int writes=c.Child.Writes;bool refused=false;try {c.Scope.Dispose();}catch(InvalidOperationException){refused=true;}
        check(refused && c.Child.Writes==writes && !c.Scope.CachedLocalRestoration.AlreadyRestoredNoWrite,"Exact no-write gate refuses without transform setters: "+label);
    }
    public static void Run(Action<bool,string> check)
    {
        string root=Path.Combine(Path.GetTempPath(),"ugevr-exact-no-write-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var native=new Case(root);native.Camera.targetTexture=new RenderTexture();native.Camera.rect=new Rect(.7f,.2f,.3f,.4f);int writes=native.Child.Writes;native.Scope.Dispose();var outcome=native.Scope.CachedLocalRestoration;
            check(outcome.AlreadyRestoredNoWrite && outcome.AppliedSpace=="already_restored_no_write" && outcome.RestoreReturned && outcome.PostContext=="unchanged","Actual v5 saved/owned/final operands admit explicit exact no-write completion");
            check(native.Child.Writes==writes && native.Parent.Writes==0 && native.Camera.targetTexture==null && native.Camera.rect.x==0,"No-write completion performs zero transform setters while restoring other borrowed camera fields");
            string body=native.Record.ToPrivateYaml();check(body.Contains("owned_position_equal: false")&&body.Contains("owned_rotation_equal: null")&&body.Contains("refusal_or_fallback_reason: none"),"New admission retains the actual unequal owned-eye operand and original short circuit rather than fabricating ownership equality");
            native.Child.World=new Vector3(99,98,97);native.Scope.Dispose();check(native.Child.Writes==writes && native.Child.World.x==99,"Completed no-write disposal remains idempotent and preserves subsequent changes");
            var ordinary=new Case(root);ordinary.Child.World=OwnedWorld;ordinary.Child.Local=OwnedLocal;ordinary.Scope.Dispose();check(!ordinary.Scope.CachedLocalRestoration.AlreadyRestoredNoWrite && ordinary.Scope.CachedLocalRestoration.AppliedSpace=="local" && ordinary.Child.LocalWrites==1 && ordinary.Child.LocalRotationWrites==1 && ordinary.Child.WorldWrites==0,"Ordinary guarded owned-eye restoration retains its local position/rotation setter path");
            var missing=new Case(root,CameraTransformRestoration.GuardedLocal,false);Refuses(check,missing,"missing owned eye");
            var invalid=new Case(root);invalid.Child.World=new Vector3(float.NaN,0,0);invalid.Scope.RecordOwnedEyePose();invalid.ReturnSaved();Refuses(check,invalid,"invalid owned eye");
            Action<Case>[] misses={
                c=>c.Child.World.x=Next(c.Child.World.x),c=>c.Child.Local.x=Next(c.Child.Local.x),c=>c.Child.WorldRotation.w=-1,c=>c.Child.LocalRotation.w=-1,
                c=>c.Child.Scale.x=Next(c.Child.Scale.x),c=>c.Child.Lossy.x=Next(c.Child.Lossy.x),c=>c.Child.Forward.Elements[12]=Next(c.Child.Forward.Elements[12]),c=>c.Child.Inverse.Elements[12]=Next(c.Child.Inverse.Elements[12]),
                c=>c.Parent.World.x=Next(c.Parent.World.x),c=>c.Parent.WorldRotation.w=-1,c=>c.Parent.Local.x=Next(c.Parent.Local.x),c=>c.Parent.LocalRotation.w=-1,c=>c.Parent.Scale.x=Next(c.Parent.Scale.x),c=>c.Parent.Lossy.x=Next(c.Parent.Lossy.x),c=>c.Parent.Forward.Elements[12]=Next(c.Parent.Forward.Elements[12]),c=>c.Parent.Inverse.Elements[12]=Next(c.Parent.Inverse.Elements[12]),
                c=>c.Child.parent=Parent(),c=>c.Camera.transform=new Probe {parent=c.Parent,World=SavedWorld,Local=SavedLocal},c=>c.Child.InstanceIdOverride=int.MinValue,c=>c.Parent.Destroyed=true,
                c=>c.Child.World.x=float.NaN,c=>c.Child.Local.x=float.PositiveInfinity,c=>c.Child.Forward.Elements[0]=float.NaN,c=>c.Parent.Inverse.Elements[0]=float.NaN,c=>c.Child.ThrowQuery=true
            };
            for(int i=0;i<misses.Length;++i){var c=new Case(root);misses[i](c);Refuses(check,c,"native-operand near miss "+i);}
            var after=new Case(root);after.Camera.TargetSetter=t=>after.Child.World.x=Next(SavedWorld.x);bool failed=false;try{after.Scope.Dispose();}catch(InvalidOperationException){failed=true;}
            check(failed && after.Child.Writes==0 && after.Child.World.x==Next(SavedWorld.x) && after.Scope.CachedLocalRestoration.Failure=="already_restored_state_changed" && !after.Scope.CachedLocalRestoration.RestoreReturned,"Exact postcheck rejects one-ULP setter-time world mutation without a corrective transform write");
            Action<Case>[] postMisses={c=>c.Child.Local.x=Next(c.Child.Local.x),c=>c.Child.Forward.Elements[12]=Next(c.Child.Forward.Elements[12]),c=>c.Child.Inverse.Elements[12]=Next(c.Child.Inverse.Elements[12])};
            for(int i=0;i<postMisses.Length;++i)
            {
                var c=new Case(root);int index=i;c.Camera.TargetSetter=t=>postMisses[index](c);failed=false;try{c.Scope.Dispose();}catch(InvalidOperationException){failed=true;}
                check(failed && c.Child.Writes==0 && c.Scope.CachedLocalRestoration.Failure=="already_restored_state_changed","Exact postcheck rejects local or camera-matrix mutation without transform correction: "+i);
            }
            var parentAfter=new Case(root);parentAfter.Camera.TargetSetter=t=>parentAfter.Parent.World.x+=1;failed=false;try{parentAfter.Scope.Dispose();}catch(InvalidOperationException){failed=true;}
            check(failed && parentAfter.Child.Writes==0 && parentAfter.Scope.CachedLocalRestoration.PostContext=="parent_world_position_changed","Parent context changed by a borrowed-field setter remains a post-context refusal");
            var fault=new Case(root);fault.Camera.rect=new Rect(.8f,0,0,0);fault.Camera.TargetSetter=t=>{throw new Exception("private borrowed-field fault");};failed=false;try{fault.Scope.Dispose();}catch(InvalidOperationException){failed=true;}
            check(failed && fault.Child.Writes==0 && fault.Camera.rect.x==0 && !fault.Scope.CachedLocalRestoration.RestoreReturned,"A borrowed-field setter exception still rejects and restores remaining fields best effort");
            var queryAfter=new Case(root);queryAfter.Camera.TargetSetter=t=>queryAfter.Child.ThrowQuery=true;failed=false;try{queryAfter.Scope.Dispose();}catch(InvalidOperationException){failed=true;}
            check(failed && queryAfter.Child.Writes==0 && queryAfter.Scope.CachedLocalRestoration.PostContext=="native_query_failed","Unavailable post-setter context cannot become successful no-write restoration");
            foreach(var mode in new[]{CameraTransformRestoration.World,CameraTransformRestoration.LocalWithWorldFallback})
            {var c=new Case(root,mode);c.Scope.Dispose();check(c.Child.WorldWrites==1 && c.Child.WorldRotationWrites==1 && c.Child.LocalWrites==0,"Legacy mode retains its existing world restoration despite exact saved final pose: "+mode);}
        }
        finally {if(!Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(root).StartsWith("ugevr-exact-no-write-",StringComparison.Ordinal))throw new Exception("Unsafe fixture cleanup.");Directory.Delete(root,true);}
    }
}