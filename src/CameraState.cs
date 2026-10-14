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
        public CameraState(Camera camera) : this(camera,null) {}
        public CameraState(Camera camera,CameraRestorationPoseRecord diagnostic)
        {
            this.camera=camera;
            this.diagnostic=diagnostic;
            position=camera.transform.position; rotation=camera.transform.rotation;
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
        public void Dispose()
        {
            if (restored || camera == null) return;
            restored=true;
            camera.targetTexture=target;
            camera.transform.position=position; camera.transform.rotation=rotation;
            camera.rect=rect; camera.useOcclusionCulling=occlusion;
            camera.cullingMask=cullingMask;
            camera.backgroundColor=background; camera.clearFlags=clearFlags;
            if (automaticProjection) camera.ResetProjectionMatrix(); else camera.projectionMatrix=projection;
            if (automaticView) camera.ResetWorldToCameraMatrix(); else camera.worldToCameraMatrix=view;
            if(diagnostic!=null)diagnostic.Observe("after_camera_state_dispose");
        }
        static bool Equal(Matrix4x4 a,Matrix4x4 b)
        {
            for (int i=0;i<16;++i) if (a[i]!=b[i]) return false;
            return true;
        }
    }
}
