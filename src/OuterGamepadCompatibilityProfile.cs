using System;
using System.Reflection;

namespace Ugevr
{
    public enum OwnedGamepadButton {South,West}
    public static class OuterGamepadCompatibilityProfile
    {
        public const string Name="outer-1.4.1-right-a-south-axis0-leftstick-v1";
        public const string WakeName="outer-1.4.1-right-a-west-axis0-leftstick-v1";
        public static bool Supports(string profile) {return profile==Name || profile==WakeName;}
        public static OwnedGamepadButton ButtonFor(string profile)
        {
            if(profile==Name) return OwnedGamepadButton.South;
            if(profile==WakeName) return OwnedGamepadButton.West;
            throw new ArgumentException("Select an explicit supported gamepad profile.");
        }
        public const string LayoutPrefix="XInputControllerWindowsUGEVR_";
        static readonly Guid ExpectedGameModule=new Guid("995fa289-c662-4f44-9176-33f00ebf2052");
        public static IOwnedGamepad Create(string profile)
        {
            if(!Supports(profile)) return PublicOwnedGamepad.Unavailable("game_profile_unavailable");
            foreach(Assembly a in AppDomain.CurrentDomain.GetAssemblies()) if(a.GetName().Name=="Assembly-CSharp")
            {
                if(a.ManifestModule.ModuleVersionId!=ExpectedGameModule) return PublicOwnedGamepad.Unavailable("game_compatibility_identity_mismatch");
                return PublicOwnedGamepad.Create(LayoutPrefix,ButtonFor(profile));
            }
            return PublicOwnedGamepad.Unavailable("game_compatibility_not_loaded");
        }
    }
}
