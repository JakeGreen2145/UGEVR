using System.IO;

namespace Ugevr
{
    // This token belongs to one eye pair. Only successful pairs attempt consumption.
    public struct CaptureRequestAdmission
    {
        readonly bool imagesEnabled,requiresNativeUi;
        public readonly bool CaptureNativeUiState;
        CaptureRequestAdmission(bool imagesEnabled,bool requiresNativeUi,bool admitted)
        {this.imagesEnabled=imagesEnabled;this.requiresNativeUi=requiresNativeUi;CaptureNativeUiState=admitted;}

        public static CaptureRequestAdmission BeforePair(bool nativeUiEnabled,bool imagesEnabled,bool overlayEnabled,string requestPath)
        {
            bool requiresNativeUi=nativeUiEnabled && imagesEnabled && overlayEnabled;
            return new CaptureRequestAdmission(imagesEnabled,requiresNativeUi,requiresNativeUi && File.Exists(requestPath));
        }
        public bool TryConsume(string requestPath)
        {
            if(!imagesEnabled || (requiresNativeUi && !CaptureNativeUiState) || !File.Exists(requestPath)) return false;
            File.Delete(requestPath);
            return true;
        }
    }
}
