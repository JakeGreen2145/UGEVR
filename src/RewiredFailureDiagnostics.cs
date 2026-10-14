using System;
using System.Globalization;
using System.Reflection;

namespace Ugevr
{
    public interface IRewiredFailureDiagnostics {RewiredFailureInfo LastFailure {get;}}
    // Only controlled operation/category labels, public counts and exception types. No messages or values.
    public sealed class RewiredFailureInfo
    {
        public readonly string Stage,Category,ExceptionType,InnerExceptionType;
        public readonly bool? Ready;
        public readonly int? Definitions,CustomDevices,Joysticks;
        RewiredFailureInfo(string stage,string category,Exception error,bool? ready,int? definitions,int? devices,int? joysticks)
        {
            Stage=Label(stage,96);Category=Label(category,64);ExceptionType=TypeName(error);
            InnerExceptionType=TypeName(error==null?null:error.InnerException);Ready=ready;Definitions=definitions;CustomDevices=devices;Joysticks=joysticks;
        }
        static string Label(string value,int bound)
        {
            if(String.IsNullOrEmpty(value)) return "none";
            var b=new System.Text.StringBuilder();foreach(char c in value) {if(b.Length==bound) break;b.Append(Char.IsLetterOrDigit(c) || c=='_' || c=='.'?c:'_');}return b.ToString();
        }
        static string TypeName(Exception error)
        {
            if(error==null) return "none";string name=error.GetType().FullName;
            return name!=null && name.StartsWith("System.",StringComparison.Ordinal)?Label(name,128):"non_system_exception";
        }
        public static RewiredFailureInfo Capture(Exception error,string stage,string category,bool? ready=null,int? definitions=null,int? devices=null,int? joysticks=null)
        {
            var wrapped=error as RewiredOperationException;
            if(wrapped!=null) return new RewiredFailureInfo(wrapped.Stage,wrapped.Category,wrapped.InnerException,ready,definitions,devices,joysticks);
            return new RewiredFailureInfo(stage,category,error,ready,definitions,devices,joysticks);
        }
        static string Count(int? value) {return value.HasValue?value.Value.ToString(CultureInfo.InvariantCulture):"not_read";}
        public string ToLog()
        {return "failureStage="+Stage+" failureCategory="+Category+" exceptionType="+ExceptionType+" innerExceptionType="+InnerExceptionType+" preflightReady="+(Ready.HasValue?Ready.Value.ToString().ToLowerInvariant():"not_read")+" preflightDefinitions="+Count(Definitions)+" preflightCustomDevices="+Count(CustomDevices)+" preflightJoysticks="+Count(Joysticks);}
    }
    internal sealed class RewiredOperationException : Exception
    {
        internal readonly string Stage,Category;
        internal RewiredOperationException(string stage,string category,Exception error=null) : base("Rewired public operation failed.",error) {Stage=stage;Category=category;}
    }
}
