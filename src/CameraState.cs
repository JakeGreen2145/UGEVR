using System;
using UnityEngine;

namespace Ugevr
{
    // Each sequential eye pair borrows one camera. Restore even if Render throws.
    public sealed class CameraState : IDisposable
    {
        readonly Camera camera;
        readonly CameraRestorationPoseRecord diagnostic;
        readonly Vector3 position;
        readonly Quaternion rotation;
        readonly RenderTexture target;
        readonly Matrix4x4 projection, view;
        readonly bool automaticProjection, automaticView;
        readonly Rect rect;
        readonly bool occlusion;
        readonly int cullingMask;
        readonly Color background;
        readonly CameraClearFlags clearFlags;
        bool restored;
        readonly CameraLocalRestoration localRestoration;
        readonly bool guardedLocal;
        public CameraState(Camera camera) : this(camera,null) {}
        public CameraState(Camera camera,CameraRestorationPoseRecord diagnostic) : this(camera,diagnostic,CameraTransformRestoration.World) {}
        public CameraState(Camera camera,CameraRestorationPoseRecord diagnostic,CameraTransformRestoration mode)
        {
            this.camera=camera;
            this.diagnostic=diagnostic;
            guardedLocal=mode==CameraTransformRestoration.GuardedLocal;
            if(mode!=CameraTransformRestoration.World && diagnostic!=null)diagnostic.SelectLocalMode(guardedLocal?"guarded-local":"local-with-world-fallback");
            position=camera.transform.position; rotation=camera.transform.rotation;
            if(mode!=CameraTransformRestoration.World)
            {
                localRestoration=new CameraLocalRestoration(camera,position,rotation,guardedLocal);
                if(diagnostic!=null)diagnostic.SelectLocalRestoration(localRestoration);
                if(guardedLocal && localRestoration.AdmissionRefusal!=null)
                    throw new InvalidOperationException("Guarded camera restoration admission refused: "+localRestoration.AdmissionRefusal);
            }
            if(diagnostic!=null)diagnostic.ObserveSavedWorld(position,rotation);
            target=camera.targetTexture; projection=camera.projectionMatrix;
            view=camera.worldToCameraMatrix; rect=camera.rect;
            occlusion=camera.useOcclusionCulling; background=camera.backgroundColor;
            cullingMask=camera.cullingMask;
            clearFlags=camera.clearFlags;
            // Unity exposes matrix values, not whether a setter override is active.
            // Compare against the engine's own defaults rather than approximating
            // aspect/physical-camera/parent conventions. No rendering occurs here.
            Exception snapshotFailure=null;
            try
            {
                camera.ResetProjectionMatrix();
                automaticProjection=Equal(projection,camera.projectionMatrix);
                camera.ResetWorldToCameraMatrix();
                automaticView=Equal(view,camera.worldToCameraMatrix);
            }
            catch(Exception error) {if(!guardedLocal)throw;snapshotFailure=error;}
            finally
            {
                if(guardedLocal)
                {
                    Attempt(delegate {if(automaticProjection)camera.ResetProjectionMatrix();else camera.projectionMatrix=projection;},ref snapshotFailure);
                    Attempt(delegate {if(automaticView)camera.ResetWorldToCameraMatrix();else camera.worldToCameraMatrix=view;},ref snapshotFailure);
                }
                else
                {
                    if (automaticProjection) camera.ResetProjectionMatrix(); else camera.projectionMatrix=projection;
                    if (automaticView) camera.ResetWorldToCameraMatrix(); else camera.worldToCameraMatrix=view;
                }
            }
            if(snapshotFailure!=null)throw new InvalidOperationException("Guarded camera restoration admission failed: matrix_snapshot_or_restore_exception",snapshotFailure);
        }
        public void RecordOwnedEyePose() {if(localRestoration!=null)localRestoration.RecordOwnedEyePose();}
        public void Dispose()
        {
            if (restored) return;
            if(camera==null)
            {
                if(guardedLocal) {restored=true;throw new InvalidOperationException("Guarded camera restoration failed: camera_unavailable");}
                return;
            }
            restored=true;
            if(guardedLocal) {DisposeGuarded();return;}
            bool useLocal=false;
            if(localRestoration!=null)
            {
                useLocal=localRestoration.ChooseLocal();
                if(diagnostic!=null)diagnostic.Observe("before_camera_state_dispose");
            }
            try
            {
                camera.targetTexture=target;
                if(useLocal)localRestoration.RestoreLocal();
                else {camera.transform.position=position; camera.transform.rotation=rotation;}
                camera.rect=rect; camera.useOcclusionCulling=occlusion;
                camera.cullingMask=cullingMask;
                camera.backgroundColor=background; camera.clearFlags=clearFlags;
                if (automaticProjection) camera.ResetProjectionMatrix(); else camera.projectionMatrix=projection;
                if (automaticView) camera.ResetWorldToCameraMatrix(); else camera.worldToCameraMatrix=view;
                if(localRestoration!=null) {localRestoration.ObserveAfterRestoration();localRestoration.Completed();}
                if(diagnostic!=null)diagnostic.Observe("after_camera_state_dispose");
            }
            catch {if(localRestoration!=null)localRestoration.Failed();throw;}
        }
        static void Attempt(Action action,ref Exception first)
        {try {action();}catch(Exception error){if(first==null)first=error;}}
        void DisposeGuarded()
        {
            Exception first=null;string conflict=null;bool local=false;
            try {local=localRestoration.ChooseLocal();if(!local)conflict=localRestoration.Refusal;}
            catch(Exception error){first=error;conflict="eligibility_query_failed";}
            if(diagnostic!=null)diagnostic.Observe("before_camera_state_dispose");
            // Eligibility refusal never writes either local or world pose. Other
            // borrowed fields each get one independent restoration attempt.
            Attempt(delegate {camera.targetTexture=target;},ref first);
            if(local)Attempt(delegate {localRestoration.RestoreLocal();},ref first);
            Attempt(delegate {camera.rect=rect;},ref first);
            Attempt(delegate {camera.useOcclusionCulling=occlusion;},ref first);
            Attempt(delegate {camera.cullingMask=cullingMask;},ref first);
            Attempt(delegate {camera.backgroundColor=background;},ref first);
            Attempt(delegate {camera.clearFlags=clearFlags;},ref first);
            Attempt(delegate {if(automaticProjection)camera.ResetProjectionMatrix();else camera.projectionMatrix=projection;},ref first);
            Attempt(delegate {if(automaticView)camera.ResetWorldToCameraMatrix();else camera.worldToCameraMatrix=view;},ref first);
            Attempt(delegate {localRestoration.ObserveAfterRestoration();},ref first);
            if(local && first==null)conflict=localRestoration.GuardedResultRefusal();
            if(first==null && conflict==null)localRestoration.Completed();else localRestoration.GuardedFailed(conflict);
            if(diagnostic!=null)diagnostic.Observe("after_camera_state_dispose");
            // Dispose runs before SetEyeTextures/Submit in the injector. Refusal
            // or partial cleanup therefore prevents this pair from submission.
            if(first!=null || conflict!=null)
                throw new InvalidOperationException("Guarded camera restoration failed: "+(conflict??"restoration_setter_or_query_exception"),first);
        }
        static bool Equal(Matrix4x4 a,Matrix4x4 b)
        {
            for (int i=0;i<16;++i) if (a[i]!=b[i]) return false;
            return true;
        }
    }
}
