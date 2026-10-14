using System;
using UnityEngine;

namespace Ugevr
{
    public static class OverlayCameraPass
    {
        public static void Prepare(Camera scene,Camera ui,int isolatedMask)
        {
            if(scene==null || ui==null || scene==ui || scene.targetTexture==null) throw new InvalidOperationException("UI pass requires separate scene/UI cameras and a live eye target.");
            ui.CopyFrom(scene);
            ui.enabled=false;
            ui.transform.position=scene.transform.position;ui.transform.rotation=scene.transform.rotation;
            ui.worldToCameraMatrix=scene.worldToCameraMatrix;ui.projectionMatrix=scene.projectionMatrix;
            ui.targetTexture=scene.targetTexture;ui.rect=scene.rect;
            ui.cullingMask=isolatedMask;
            ui.clearFlags=CameraClearFlags.Depth;
            ui.backgroundColor=Color.clear;
            ui.renderingPath=RenderingPath.Forward;
            ui.useOcclusionCulling=false;
        }
        public static void Render(Camera ui)
        {
            try {ui.Render();}
            finally {if(ui!=null) ui.targetTexture=null;}
        }
    }
}
