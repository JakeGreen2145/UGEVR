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
        public CameraState(Camera camera) : this(camera,null) {}
        public CameraState(Camera camera,CameraRestorationPoseRecord diagnostic) : this(camera,diagnostic,CameraTransformRestoration.World) {}
        public CameraState(Camera camera,CameraRestorationPoseRecord diagnostic,CameraTransformRestoration mode)
        {
            this.camera=camera;
            this.diagnostic=diagnostic;
            if(mode==CameraTransformRestoration.LocalWithWorldFallback && diagnostic!=null)diagnostic.SelectLocalMode();
            position=camera.transform.position; rotation=camera.transform.rotation;
            if(mode==CameraTransformRestoration.LocalWithWorldFallback)
            {
                localRestoration=new CameraLocalRestoration(camera,position,rotation);
                if(diagnostic!=null)diagnostic.SelectLocalRestoration(localRestoration);
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
            try
            {
                camera.ResetProjectionMatrix();
                automaticProjection=Equal(projection,camera.projectionMatrix);
                camera.ResetWorldToCameraMatrix();
                automaticView=Equal(view,camera.worldToCameraMatrix);
            }
            finally
            {
                if (automaticProjection) camera.ResetProjectionMatrix(); else camera.projectionMatrix=projection;
                if (automaticView) camera.ResetWorldToCameraMatrix(); else camera.worldToCameraMatrix=view;
            }
        }
        public void RecordOwnedEyePose() {if(localRestoration!=null)localRestoration.RecordOwnedEyePose();}
        public void Dispose()
        {
            if (restored || camera == null) return;
            restored=true;
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
        static bool Equal(Matrix4x4 a,Matrix4x4 b)
        {
            for (int i=0;i<16;++i) if (a[i]!=b[i]) return false;
            return true;
        }
    }
}
