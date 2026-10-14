using System;
using System.Globalization;

namespace Ugevr
{
    public interface IRewiredActionReadback {RewiredActionReadback CaptureReadback();}
    // Fixed public API observations only. Null is unavailable, not a zero input value.
    public sealed class RewiredActionReadback
    {
        public bool? MapEnabled,ControllerEnabled,LastActiveMatchesOwned;
        public double? OwnedAxis0,OwnedAxis1,OwnedAxis2,PlayerAction31,PlayerAction32,PlayerAction36;
        public uint FailedFields,NonfiniteFields;
        static string Scalar(bool? value) {return value.HasValue?value.Value.ToString().ToLowerInvariant():"unavailable";}
        static string Scalar(double? value) {return value.HasValue?value.Value.ToString("R",CultureInfo.InvariantCulture):"unavailable";}
        public string ToLog()
        {return "readbackStatus="+(FailedFields==0?"sampled":"partial_or_unavailable")+" readbackFailedFields="+FailedFields+" readbackNonfiniteFields="+NonfiniteFields+" ownedMapEnabled="+Scalar(MapEnabled)+" ownedControllerEnabled="+Scalar(ControllerEnabled)+" ownedRawAxis0="+Scalar(OwnedAxis0)+" ownedRawAxis1="+Scalar(OwnedAxis1)+" ownedRawAxis2="+Scalar(OwnedAxis2)+" systemRawLookX="+Scalar(PlayerAction31)+" systemRawLookY="+Scalar(PlayerAction32)+" systemRawContextUse="+Scalar(PlayerAction36)+" lastActiveMatchesOwned="+Scalar(LastActiveMatchesOwned);}
    }
}
