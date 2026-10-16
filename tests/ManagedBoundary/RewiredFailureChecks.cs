using System;
using System.Reflection;
using Ugevr;

static class RewiredFailureChecks
{
    const string PrivateText="invented-private-device-and-error-text";
    sealed class Fake : IOwnedRewiredInput,IRewiredFailureDiagnostics
    {
        public bool Created,ThrowAdd,ThrowDesired,ThrowRemove;
        public int Removes;
        public string UnavailableReason {get;set;}
        public string FaultReason {get {return "";}}
        public string CleanupStatus {get {return Created?"owned":"not_created";}}
        public bool NeedsCleanup {get {return Created;}}
        public int AppliedUpdates {get {return 0;}}
        public RewiredFailureInfo LastFailure {get;set;}
        public void Add() {Created=true;if(ThrowAdd)throw new TargetInvocationException(new InvalidOperationException(PrivateText));}
        public void SetDesired(double x,double y,double trigger,double expires) {if(ThrowDesired)throw new InvalidOperationException(PrivateText);}
        public void Remove() {++Removes;LastFailure=RewiredFailureInfo.Capture(new Exception(PrivateText),"cleanup","cleanup_failed");if(ThrowRemove)throw new Exception(PrivateText);Created=false;}
    }
    sealed class PrivateException : Exception {public PrivateException():base(PrivateText){}}
    static RewiredRightSample Neutral() {return new RewiredRightSample {Available=true,Identity="invented-source",Index=2,Packet=1,TriggerAxis=1};}
    static PublicOwnedRewiredInput Adapter() {return new PublicOwnedRewiredInput(typeof(Rewired.ReInput).Assembly,new Rewired.InputManager_Base(),()=>0,()=>true,false);}
    static RewiredInputDiagnostic Policy(Func<IOwnedRewiredInput> create) {return new RewiredInputDiagnostic(create,15,.2,"refuse");}
    public static void Run(Action<bool,string> check)
    {
        var policy=Policy(delegate {throw new InvalidOperationException(PrivateText);});policy.Tick(true,true,1,Neutral(),0);
        check(policy.Complete && policy.AddAttempts==0 && policy.RemoveAttempts==0 && policy.Failure.Stage=="factory" && policy.Failure.ExceptionType=="System.InvalidOperationException","A throwing factory records its phase and exception type before any backend ownership");
        check(!policy.Failure.ToLog().Contains(PrivateText),"Sanitized factory diagnostics never contain exception messages or controller values");
        policy=Policy(()=>null);policy.Tick(true,true,1,Neutral(),0);
        check(policy.Failure.Stage=="factory" && policy.Failure.Category=="backend_unavailable" && policy.Failure.ExceptionType=="none","A null factory result remains distinct from a thrown public API call");
        var fake=new Fake {UnavailableReason="public_api_profile_unavailable",LastFailure=RewiredFailureInfo.Capture(new InvalidOperationException(PrivateText),"factory_public_profile","public_api_exception")};policy=Policy(()=>fake);policy.Tick(true,true,1,Neutral(),0);
        check(policy.Failure.Stage=="factory_public_profile" && policy.AddAttempts==0 && policy.RemoveAttempts==0,"An unavailable public factory result carries its original bounded diagnostics through policy cancellation");
        foreach(string failure in new[]{"add","desired"})
        {
            fake=new Fake {ThrowAdd=failure=="add",ThrowDesired=failure=="desired",ThrowRemove=true};policy=Policy(()=>fake);policy.Tick(true,true,1,Neutral(),0);var original=policy.Failure;
            check(policy.Complete && policy.AddAttempts==1 && policy.RemoveAttempts==2 && policy.Status=="cleanup_unconfirmed" && policy.Failure.Stage==(failure=="add"?"add":"initial_desired_state"),"A partial creation/initial feed failure retains its phase while bounded cleanup remains honestly unconfirmed: "+failure);
            check(policy.Failure.ExceptionType==(failure=="add"?"System.Reflection.TargetInvocationException":"System.InvalidOperationException") && policy.Failure.InnerExceptionType==(failure=="add"?"System.InvalidOperationException":"none"),"Public reflection wrapper and inner exception types remain distinct: "+failure);
            policy.Tick(true,true,2,Neutral(),2);policy.Cancel("later",3);
            check(Object.ReferenceEquals(original,policy.Failure) && policy.AddAttempts==1 && policy.RemoveAttempts==2 && !policy.Failure.ToLog().Contains(PrivateText),"Later cleanup errors, retry ticks and cancellation cannot erase the first failure or start another window: "+failure);
        }
        var safe=RewiredFailureInfo.Capture(new PrivateException(),new string('x',200),new string('y',200));
        check(safe.Stage.Length==96 && safe.Category.Length==64 && safe.ExceptionType=="non_system_exception" && !safe.ToLog().Contains(PrivateText),"Diagnostic labels and exception-type categories are bounded and do not publish application exception names/messages");
        foreach(string fault in new[]{"not-ready","definition","custom","joystick","action","category","layout"})
        {
            Rewired.ReInput.Reset();var data=Rewired.ReInput.Data;
            if(fault=="not-ready")Rewired.ReInput.isReady=false;if(fault=="definition")data.Definitions.Add(new Rewired.Data.CustomController_Editor {id=99});if(fault=="custom")Rewired.ReInput.controllers.CustomControllers.Add(new Rewired.CustomController(99,99));if(fault=="joystick")Rewired.ReInput.controllers.JoystickContent.Add(new Rewired.Controller(99));if(fault=="action")data.BadAction=true;if(fault=="category")data.MissingCategory=true;if(fault=="layout")data.MissingLayout=true;
            var adapter=Adapter();policy=Policy(()=>adapter);policy.Tick(true,true,1,Neutral(),0);
            string category=fault=="not-ready"?"manager_not_ready":fault=="definition"?"existing_definition_conflict":fault=="custom"?"existing_custom_device_conflict":fault=="joystick"?"existing_joystick_conflict":fault=="action"?"action_profile_mismatch":fault=="category"?"category_zero_unavailable":"layout_zero_unavailable";
            check(policy.Complete && policy.AddAttempts==1 && policy.RemoveAttempts==0 && adapter.CleanupStatus=="not_created" && !adapter.NeedsCleanup && policy.Failure.Category==category,"Actual public backend diagnoses its precise pre-mutation refusal without claiming or removing resources: "+fault);
            check(policy.Failure.Ready==(fault!="not-ready") && (fault!="joystick" || policy.Failure.Joysticks==1) && (fault!="definition" || policy.Failure.Definitions==1) && (fault!="custom" || policy.Failure.CustomDevices==1),"Preflight records only public readiness/count results already queried by the selected guard: "+fault);
            if(fault=="not-ready")check(policy.Failure.Definitions==null && policy.Failure.CustomDevices==null && policy.Failure.Joysticks==null,"An unready manager does not cause extra diagnostic enumeration");
        }
        foreach(string fault in new[]{"axis-before","definition-after","controller-after"})
        {
            Rewired.ReInput.Reset();var data=Rewired.ReInput.Data;if(fault=="axis-before")data.ThrowAxisBefore=true;if(fault=="definition-after")data.ThrowDefinitionAfter=true;if(fault=="controller-after")Rewired.ReInput.controllers.ThrowCreateAfter=true;
            var adapter=Adapter();policy=Policy(()=>adapter);policy.Tick(true,true,1,Neutral(),0);
            string method=fault=="axis-before"?"AddAxis":fault=="definition-after"?"AddCustomController":"CreateCustomController";
            check(policy.Failure.Stage=="invoke_public_method_"+method && policy.Failure.Category=="public_method_exception" && policy.Failure.ExceptionType=="System.Reflection.TargetInvocationException" && policy.Failure.InnerExceptionType=="System.Exception","Actual public reflection failure identifies its called member and wrapper/inner types without any message: "+fault);
            check(policy.RemoveAttempts>=1 && (fault=="definition-after"?adapter.NeedsCleanup && data.Definitions.Count==1:!adapter.NeedsCleanup && data.Definitions.Count==0) && !policy.Failure.ToLog().Contains("mutation"),"Partial creation diagnostics preserve existing ownership-dependent cleanup outcomes: "+fault);
        }
        Rewired.ReInput.Reset();var healthy=Adapter();policy=Policy(()=>healthy);policy.Tick(true,true,1,Neutral(),0);check(policy.Failure==null && healthy.LastFailure==null && policy.Status=="active","A successful public backend path produces no fabricated failure diagnostic");policy.Cancel("end",1);Rewired.ReInput.Reset();
    }
}
