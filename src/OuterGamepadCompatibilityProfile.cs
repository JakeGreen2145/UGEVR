using System;
using System.Reflection;
using Valve.VR;

namespace Ugevr
{
    public enum OwnedGamepadButton {South,West}
    public static class OuterGamepadCompatibilityProfile
    {
        public const string Name="outer-1.4.1-right-a-south-axis0-leftstick-v1";
        public const string WakeName="outer-1.4.1-right-a-west-axis0-leftstick-v1";
        public const string ButtonOnlyWakeName="outer-1.4.1-deckardmp-right-a-west-button-only-v1";
        public const string ObservedAxis0Name="outer-1.4.1-deckardmp-right-a-west-observed-axis0-leftstick-v1";
        public const string ButtonOnlySerial="frame_testbench_right";
        public const string ButtonOnlyModel="Deckard MP";
        public static LegacyRightSample Read(CVRSystem system,string profile)
        {
            if(profile==ObservedAxis0Name) return OpenVrLegacyRightReader.ReadObservedTransportAxis0(system,ButtonOnlySerial,ButtonOnlyModel);
            if(profile==ButtonOnlyWakeName) return OpenVrLegacyRightReader.ReadButtonOnly(system,ButtonOnlySerial,ButtonOnlyModel);
            if(profile==Name || profile==WakeName) return OpenVrLegacyRightReader.Read(system);
            return new LegacyRightSample {UnavailableReason="game_profile_unavailable"};
        }
        public static bool Supports(string profile) {return profile==Name || profile==WakeName || profile==ButtonOnlyWakeName || profile==ObservedAxis0Name;}
        public static OwnedGamepadButton ButtonFor(string profile)
        {
            if(profile==Name) return OwnedGamepadButton.South;
            if(profile==WakeName || profile==ButtonOnlyWakeName || profile==ObservedAxis0Name) return OwnedGamepadButton.West;
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
