using System;
using System.Threading;
using Valve.VR;

namespace Ugevr
{
    // One selected policy and backend route for the Injector component lifetime.
    public sealed class GamepadDiagnosticBinding
    {
        readonly int ownerThread;
        readonly Func<LegacyRightSample> readSingle;
        readonly Func<LegacyPairSample> readDual;
        readonly Func<LegacyCancelPairSample> readCancel;
        public OpenVrDualCancelGamepadDiagnostic CancelDual {get;private set;}
        public LegacyCancelPairSample LastCancelPair {get;private set;}
        public OpenVrGamepadDiagnostic Single {get;private set;}
        public OpenVrDualGamepadDiagnostic Dual {get;private set;}
        public LegacyRightSample LastSingle {get;private set;}
        public LegacyPairSample LastPair {get;private set;}
        public string LastError {get;private set;}
        public bool Complete {get {return CancelDual!=null?CancelDual.Complete:Single!=null?Single.Complete:Dual==null || Dual.Complete;}}
        public bool SupportsIdentityCapture {get {return Single!=null;}}

        public static GamepadDiagnosticBinding Create(bool activation,bool sessionAvailable,bool rewiredActivation,bool engineExperiment,
            string profile,double seconds,double deadzone,string conflictPolicy,int neutralReads,Func<CVRSystem> system)
        {
            if(system==null) throw new ArgumentNullException("system");
            return new GamepadDiagnosticBinding(activation,sessionAvailable,rewiredActivation,engineExperiment,profile,seconds,deadzone,conflictPolicy,neutralReads,
                delegate {return OuterGamepadCompatibilityProfile.Create(profile);},delegate {return OuterGamepadCompatibilityProfile.CreateDual(profile);},
                delegate {return OuterGamepadCompatibilityProfile.ReadSafely(system(),profile);},delegate {return OuterGamepadCompatibilityProfile.ReadDualSafely(system(),profile);},
                delegate {return OuterGamepadCompatibilityProfile.CreateDualCancel(profile);},delegate {return OuterGamepadCompatibilityProfile.ReadDualCancelSafely(system(),profile);});
        }
        internal GamepadDiagnosticBinding(bool activation,bool sessionAvailable,bool rewiredActivation,bool engineExperiment,
            string profile,double seconds,double deadzone,string conflictPolicy,int neutralReads,
            Func<IOwnedGamepad> singleFactory,Func<IOwnedDualGamepad> dualFactory,Func<LegacyRightSample> readSingle,Func<LegacyPairSample> readDual,
            Func<IOwnedDualCancelGamepad> cancelFactory=null,Func<LegacyCancelPairSample> readCancel=null)
        {
            ownerThread=Thread.CurrentThread.ManagedThreadId;LastError="";
            if(!activation) return;
            if(!sessionAvailable || rewiredActivation || engineExperiment) throw new ArgumentException("Select a valid session and disable simultaneous Rewired and E experiments.");
            // Dual profiles must never enter the legacy single-hand backend or reader.
            if(OuterGamepadCompatibilityProfile.SupportsDualCancel(profile))
            {
                if(neutralReads!=OpenVrDualCancelGamepadDiagnostic.RequiredNeutralReads || readCancel==null)throw new ArgumentException("The cancel profile requires exactly three neutral pair reads and its exclusive reader.");
                CancelDual=new OpenVrDualCancelGamepadDiagnostic(cancelFactory,seconds,deadzone,conflictPolicy);this.readCancel=readCancel;
            }
            else if(OuterGamepadCompatibilityProfile.SupportsDual(profile))
            {
                if(neutralReads!=OpenVrDualGamepadDiagnostic.RequiredNeutralReads) throw new ArgumentException("The dual-hand profile requires DiagnosticNeutralReads exactly 3.");
                if(readDual==null) throw new ArgumentNullException("readDual");
                Dual=new OpenVrDualGamepadDiagnostic(dualFactory,seconds,deadzone,conflictPolicy);this.readDual=readDual;
            }
            else if(OuterGamepadCompatibilityProfile.Supports(profile))
            {
                if(readSingle==null) throw new ArgumentNullException("readSingle");
                Single=new OpenVrGamepadDiagnostic(singleFactory,seconds,deadzone,conflictPolicy,neutralReads);this.readSingle=readSingle;
            }
            else throw new ArgumentException("Select an explicit supported gamepad diagnostic profile.");
        }
        bool OwnerThread()
        {
            if(Thread.CurrentThread.ManagedThreadId==ownerThread) return true;
            LastError="owner_thread_required";return false;
        }
        public void Tick(bool enabled,bool focused,ulong generation,double now)
        {
            if(!OwnerThread()) return;
            if(CancelDual!=null)
            {
                var input=new LegacyCancelPairSample();
                if(enabled && focused && !CancelDual.Complete && CancelDual.Status!="releasing")try {input=readCancel();}catch {input.Pair.UnavailableReason="cancel_pair_reader_exception";}
                LastCancelPair=input;CancelDual.Tick(enabled,focused,generation,input,now);
            }
            else if(Single!=null)
            {
                var input=new LegacyRightSample();
                if(enabled && focused && !Single.Complete)
                    try {input=readSingle();}catch(Exception error){input.UnavailableReason="reader_exception:"+error.GetType().FullName;}
                LastSingle=input;Single.Tick(enabled,focused,generation,input,now);
            }
            else if(Dual!=null)
            {
                var input=new LegacyPairSample();
                if(enabled && focused && !Dual.Complete)
                    try {input=readDual();}catch(Exception error){input.UnavailableReason="reader_exception:"+error.GetType().FullName;}
                LastPair=input;Dual.Tick(enabled,focused,generation,input,now);
            }
        }
        public void Pause(string reason,double now)
        {if(!OwnerThread()) return;if(CancelDual!=null)CancelDual.Pause(reason,now);else if(Single!=null) Single.Pause(reason,now);else if(Dual!=null) Dual.Pause(reason,now);}
        public void Cancel(string reason,double now)
        {if(!OwnerThread()) return;if(CancelDual!=null)CancelDual.Cancel(reason,now);else if(Single!=null) Single.Cancel(reason,now);else if(Dual!=null) Dual.Cancel(reason,now);}
    }
}
