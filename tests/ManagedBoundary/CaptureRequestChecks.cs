using System;
using System.IO;
using UnityEngine;
using Ugevr;

static class CaptureRequestChecks
{
    public static void Run(Action<bool,string> assert)
    {
        string directory=Path.Combine(Path.GetTempPath(),"ugevr-capture-admission-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string request=Path.Combine(directory,"capture-request");
        try
        {
            var late=CaptureRequestAdmission.BeforePair(true,true,true,request);
            File.WriteAllText(request,"");
            assert(!late.CaptureNativeUiState && !late.TryConsume(request) && File.Exists(request),"Combined request arriving during a pair remains on disk instead of capturing images with missing native observations");
            var next=CaptureRequestAdmission.BeforePair(true,true,true,request);
            assert(next.CaptureNativeUiState && next.TryConsume(request) && !File.Exists(request),"Next eligible pair admits and consumes the retained combined capture request");
            assert(!next.TryConsume(request),"Admitted token does not fabricate or repeatedly consume an absent capture request");
            foreach(bool overlay in new[]{false,true})
            {
                var ordinary=CaptureRequestAdmission.BeforePair(false,true,overlay,request);File.WriteAllText(request,"");
                assert(!ordinary.CaptureNativeUiState && ordinary.TryConsume(request) && !File.Exists(request),"Default image-only capture keeps accepting a late request with either overlay configuration");
            }
            var noOverlay=CaptureRequestAdmission.BeforePair(true,true,false,request);File.WriteAllText(request,"");
            assert(!noOverlay.CaptureNativeUiState && noOverlay.TryConsume(request),"Overlay-disabled image capture does not wait indefinitely for native overlay admission");
            File.WriteAllText(request,"");var disabled=CaptureRequestAdmission.BeforePair(true,false,true,request);
            assert(!disabled.CaptureNativeUiState && !disabled.TryConsume(request) && File.Exists(request),"Disabled image capture preserves the request without admitting native sampling");
            var admitted=CaptureRequestAdmission.BeforePair(true,true,true,request);
            var camera=new Camera();camera.RenderAction=delegate {throw new InvalidOperationException("failed pair");};
            bool failed=false;
            try {camera.Render();admitted.TryConsume(request);} catch(InvalidOperationException) {failed=true;}finally {camera.RenderAction=null;}
            assert(failed && File.Exists(request),"Failed pair leaves the admitted capture request pending before completion consumption");
            var afterDisable=CaptureRequestAdmission.BeforePair(false,true,true,request);
            assert(afterDisable.TryConsume(request),"A new image-only pair can consume a request retained before diagnostics were disabled");
            if(Environment.OSVersion.Platform==PlatformID.Win32NT)
            {
                File.WriteAllText(request,"");admitted=CaptureRequestAdmission.BeforePair(true,true,true,request);
                bool deleteFailed=false;
                using(var locked=new FileStream(request,FileMode.Open,FileAccess.Read,FileShare.None))
                    try {admitted.TryConsume(request);} catch(IOException) {deleteFailed=true;}
                assert(deleteFailed && File.Exists(request),"Request deletion failure propagates without claiming that capture was consumed");
                assert(CaptureRequestAdmission.BeforePair(true,true,true,request).TryConsume(request),"Fresh admission can consume a preserved request after its file error clears");
            }
            File.WriteAllText(request,"");admitted=CaptureRequestAdmission.BeforePair(true,true,true,request);
            using(var empty=new OverlayCanvasScope(new Camera {nearClipPlane=.3f,farClipPlane=100},1,2,false,1.8,false,null,admitted.CaptureNativeUiState,4096))
                assert(empty.SelectedCount==0 && empty.DiagnosticJson().Contains("\"admittedRenderers\":0") && admitted.TryConsume(request),"Enabled overlay with zero selected roots still admits an explicit empty native snapshot and consumes images");
        }
        finally {if(File.Exists(request)) File.Delete(request);Directory.Delete(directory);}
    }
}
