using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using Valve.VR;

namespace Ugevr
{
    // Explicit image-capture diagnostics only. Query engine properties, never game getters.
    public static class CameraDiagnostics
    {
        const int Limit=64;
        static string Number(float value) { return Single.IsNaN(value) || Single.IsInfinity(value)?"null":value.ToString("R",CultureInfo.InvariantCulture); }
        static string Text(string value)
        {
            if (value==null) return "null";
            if (value.Length>256) value=value.Substring(0,256);
            var text=new StringBuilder("\"");
            foreach (char c in value)
            {
                if (c=='"' || c=='\\') text.Append('\\').Append(c);
                else if (c<32) text.Append("\\u").Append(((int)c).ToString("x4"));
                else text.Append(c);
            }
            return text.Append('"').ToString();
        }
        public static string Matrix(Matrix4x4 matrix)
        {
            var text=new StringBuilder("[");
            for (int row=0;row<4;++row)
            {
                if (row!=0) text.Append(','); text.Append('[');
                for (int col=0;col<4;++col) { if (col!=0) text.Append(','); text.Append(Number(matrix[row,col])); }
                text.Append(']');
            }
            return text.Append(']').ToString();
        }
        static string RectValue(Rect value) { return "["+Number(value.x)+","+Number(value.y)+","+Number(value.width)+","+Number(value.height)+"]"; }
        static string Target(RenderTexture target)
        {
            return target==null?"null":"{\"name\":"+Text(target.name)+",\"width\":"+target.width+",\"height\":"+target.height+"}";
        }
        static object Property(object instance,string name)
        {
            try { var property=instance.GetType().GetProperty(name,BindingFlags.Public|BindingFlags.Instance); return property==null?null:property.GetValue(instance,null); }
            catch { return null; }
        }
        static string PropertyText(object instance,string name)
        {
            object value=Property(instance,name); return value==null?"null":Text(Convert.ToString(value,CultureInfo.InvariantCulture));
        }
        public static void Write(string directory,int frame,CVRSystem runtime,Camera selected,Matrix4x4[] beforeRender)
        {
            Camera[] cameras=Camera.allCameras;
            var body=new StringBuilder("{\"diagnosticVersion\":1,\"gameFrame\":").Append(Time.frameCount)
                .Append(",\"selectedCamera\":").Append(selected==null?"null":Text(selected.name))
                .Append(",\"cameraCount\":").Append(cameras.Length).Append(",\"cameraLimit\":").Append(Limit)
                .Append(",\"cameras\":[");
            for (int i=0;i<cameras.Length && i<Limit;++i)
            {
                Camera camera=cameras[i]; if (i!=0) body.Append(',');
                body.Append("{\"name\":").Append(Text(camera.name)).Append(",\"instanceId\":").Append(camera.GetInstanceID())
                    .Append(",\"enabled\":").Append(camera.enabled?"true":"false").Append(",\"orthographic\":").Append(camera.orthographic?"true":"false")
                    .Append(",\"depth\":").Append(Number(camera.depth)).Append(",\"cullingMask\":").Append(camera.cullingMask)
                    .Append(",\"clearFlags\":").Append(Text(camera.clearFlags.ToString())).Append(",\"target\":").Append(Target(camera.targetTexture))
                    .Append(",\"rect\":").Append(RectValue(camera.rect)).Append(",\"pixelRect\":").Append(RectValue(camera.pixelRect))
                    .Append(",\"near\":").Append(Number(camera.nearClipPlane)).Append(",\"far\":").Append(Number(camera.farClipPlane))
                    .Append(",\"stereoEnabled\":").Append(PropertyText(camera,"stereoEnabled")).Append(",\"stereoTargetEye\":").Append(PropertyText(camera,"stereoTargetEye"))
                    .Append(",\"stereoActiveEye\":").Append(PropertyText(camera,"stereoActiveEye")).Append(",\"projectionAfterRestore\":").Append(Matrix(camera.projectionMatrix))
                    .Append(",\"components\":[");
                Component[] components=camera.GetComponents<Component>();
                for (int j=0;j<components.Length && j<Limit;++j) { if (j!=0) body.Append(','); body.Append(components[j]==null?"null":Text(components[j].GetType().FullName)); }
                body.Append("],\"componentCount\":").Append(components.Length).Append('}');
            }
            body.Append("],\"projectionRaw\":[");
            for (int eye=0;eye<2;++eye)
            {
                if (eye!=0) body.Append(','); float left=0,right=0,top=0,bottom=0;
                runtime.GetProjectionRaw((EVREye)eye,ref left,ref right,ref top,ref bottom);
                body.Append("{\"eye\":").Append(eye).Append(",\"left\":").Append(Number(left)).Append(",\"right\":").Append(Number(right))
                    .Append(",\"top\":").Append(Number(top)).Append(",\"bottom\":").Append(Number(bottom))
                    .Append(",\"beforeRenderCallback\":").Append(Matrix(beforeRender[eye])).Append('}');
            }
            body.Append("],\"canvasLimit\":").Append(Limit).Append(",\"canvases\":[");
            int canvasCount=0,rootCount=0;
            Type canvasType=Type.GetType("UnityEngine.Canvas, UnityEngine.UIModule",false) ?? Type.GetType("UnityEngine.Canvas, UnityEngine",false);
            if (canvasType!=null)
            {
                UnityEngine.Object[] canvases=UnityEngine.Object.FindObjectsOfType(canvasType); canvasCount=canvases.Length;
                foreach (UnityEngine.Object canvas in canvases)
                {
                    object root=Property(canvas,"isRootCanvas"); if (root is bool && !(bool)root) continue;
                    ++rootCount; if (rootCount>Limit) continue; if (rootCount>1) body.Append(',');
                    Camera worldCamera=Property(canvas,"worldCamera") as Camera;
                    body.Append("{\"name\":").Append(Text(canvas.name)).Append(",\"instanceId\":").Append(canvas.GetInstanceID())
                        .Append(",\"renderMode\":").Append(PropertyText(canvas,"renderMode")).Append(",\"worldCamera\":").Append(worldCamera==null?"null":Text(worldCamera.name))
                        .Append(",\"sortingOrder\":").Append(PropertyText(canvas,"sortingOrder")).Append(",\"targetDisplay\":").Append(PropertyText(canvas,"targetDisplay"))
                        .Append(",\"planeDistance\":").Append(PropertyText(canvas,"planeDistance")).Append('}');
                }
            }
            body.Append("],\"canvasTypeAvailable\":").Append(canvasType==null?"false":"true").Append(",\"canvasCount\":").Append(canvasCount)
                .Append(",\"rootCanvasCount\":").Append(rootCount).Append(",\"scope\":\"Enabled cameras and active root canvases after eye restoration. Projection matrices were sampled before game render callbacks. No game getters or render state were changed.\"}");
            File.WriteAllText(Path.Combine(directory,"capture-diagnostics-"+frame+".json"),body.ToString());
        }
    }
}
