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
        public const string DualName="outer-1.4.1-dual-hand-a-west-sticks-v1";
        public const string DualCancelName="outer-1.4.1-dual-hand-a-west-b-east-sticks-v1";
        public static bool SupportsDualCancel(string profile) {return profile==DualCancelName;}
        public static LegacyCancelPairSample ReadDualCancelSafely(CVRSystem system,string profile)
        {return SupportsDualCancel(profile)?OuterDualCancelSource.ReadSafely(system):new LegacyCancelPairSample {Pair=new LegacyPairSample {UnavailableReason="game_profile_unavailable"}};}
        public static IOwnedDualCancelGamepad CreateDualCancel(string profile)
        {
            if(!SupportsDualCancel(profile))return (IOwnedDualCancelGamepad)PublicOwnedGamepad.Unavailable("game_profile_unavailable");
            foreach(Assembly a in AppDomain.CurrentDomain.GetAssemblies())if(a.GetName().Name=="Assembly-CSharp")
            {
                if(a.ManifestModule.ModuleVersionId!=ExpectedGameModule)return (IOwnedDualCancelGamepad)PublicOwnedGamepad.Unavailable("game_compatibility_identity_mismatch");
                return PublicOwnedGamepad.CreateDualCancel(LayoutPrefix);
            }
            return (IOwnedDualCancelGamepad)PublicOwnedGamepad.Unavailable("game_compatibility_not_loaded");
        }
        public const string ButtonOnlySerial="frame_testbench_right";
        public const string ButtonOnlyModel="Deckard MP";
        public static LegacyRightSample ReadSafely(CVRSystem system,string profile)
        {
            try {return Read(system,profile);}
            catch(Exception error) {return new LegacyRightSample {UnavailableReason="reader_exception:"+error.GetType().FullName};}
        }
        public static LegacyRightSample Read(CVRSystem system,string profile)
        {
            if(profile==ObservedAxis0Name) return OpenVrLegacyRightReader.ReadObservedTransportAxis0(system,ButtonOnlySerial,ButtonOnlyModel);
            if(profile==ButtonOnlyWakeName) return OpenVrLegacyRightReader.ReadButtonOnly(system,ButtonOnlySerial,ButtonOnlyModel);
            if(profile==Name || profile==WakeName) return OpenVrLegacyRightReader.Read(system);
            return new LegacyRightSample {UnavailableReason="game_profile_unavailable"};
        }
        public static bool Supports(string profile) {return profile==Name || profile==WakeName || profile==ButtonOnlyWakeName || profile==ObservedAxis0Name;}
        public static bool SupportsDual(string profile) {return profile==DualName;}
        public static LegacyPairSample ReadDualSafely(CVRSystem system,string profile)
        {return SupportsDual(profile)?OpenVrLegacyPairReader.ReadSafely(system):new LegacyPairSample {UnavailableReason="game_profile_unavailable"};}
        public static IOwnedDualGamepad CreateDual(string profile)
        {
            if(!SupportsDual(profile)) return (IOwnedDualGamepad)PublicOwnedGamepad.Unavailable("game_profile_unavailable");
            foreach(Assembly a in AppDomain.CurrentDomain.GetAssemblies()) if(a.GetName().Name=="Assembly-CSharp")
            {
                if(a.ManifestModule.ModuleVersionId!=ExpectedGameModule) return (IOwnedDualGamepad)PublicOwnedGamepad.Unavailable("game_compatibility_identity_mismatch");
                return PublicOwnedGamepad.CreateDual(LayoutPrefix,OwnedGamepadButton.West);
            }
            return (IOwnedDualGamepad)PublicOwnedGamepad.Unavailable("game_compatibility_not_loaded");
        }
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
