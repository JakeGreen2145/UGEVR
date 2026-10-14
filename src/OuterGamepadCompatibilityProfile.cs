using System;
using System.Reflection;

namespace Ugevr
{
    public static class OuterGamepadCompatibilityProfile
    {
        public const string Name="outer-1.4.1-right-a-south-axis0-leftstick-v1";
        public const string LayoutPrefix="XInputControllerWindowsUGEVR_";
        static readonly Guid ExpectedGameModule=new Guid("995fa289-c662-4f44-9176-33f00ebf2052");
        public static IOwnedGamepad Create()
        {
            foreach(Assembly a in AppDomain.CurrentDomain.GetAssemblies()) if(a.GetName().Name=="Assembly-CSharp")
            {
                if(a.ManifestModule.ModuleVersionId!=ExpectedGameModule) return PublicOwnedGamepad.Unavailable("game_compatibility_identity_mismatch");
                return PublicOwnedGamepad.Create(LayoutPrefix);
            }
            return PublicOwnedGamepad.Unavailable("game_compatibility_not_loaded");
        }
    }
}
