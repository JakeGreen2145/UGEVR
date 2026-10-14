using System;
using System.Reflection;
using System.Threading;
using Ugevr;
using Valve.VR;
using Hub=UnityEngine.InputSystem.InputSystem;

static class GamepadBindingChecks
{
    sealed class Fixture
    {
        public int SingleFactories,DualFactories,SingleReads,DualReads;
        public bool ThrowPair;
        public LegacyRightSample Single=new LegacyRightSample {Available=true,Identity="invented-right",Index=2,Packet=17};
        public LegacyPairSample Pair=new LegacyPairSample {Available=true,
            Left=new LegacyHandSample {Available=true,Index=1,Role=ETrackedControllerRole.LeftHand,Identity="invented-left",Axis0Type=2,Packet=17},
            Right=new LegacyHandSample {Available=true,Index=2,Role=ETrackedControllerRole.RightHand,Identity="invented-right",Axis0Type=2,Packet=17}};
        public GamepadDiagnosticBinding Binding(string profile=OuterGamepadCompatibilityProfile.DualName,int neutral=3,bool activation=true,bool session=true,bool rewired=false,bool engine=false)
        {
            return new GamepadDiagnosticBinding(activation,session,rewired,engine,profile,5,.2,"refuse",neutral,
                delegate {++SingleFactories;return new PublicOwnedGamepad(Assembly.GetExecutingAssembly(),false,OuterGamepadCompatibilityProfile.LayoutPrefix,OwnedGamepadButton.West,false);},
                delegate {++DualFactories;return new PublicOwnedGamepad(Assembly.GetExecutingAssembly(),false,OuterGamepadCompatibilityProfile.LayoutPrefix,OwnedGamepadButton.West,true);},
                delegate {++SingleReads;return Single;},delegate {++DualReads;if(ThrowPair)throw new InvalidOperationException("No private identity in reason.");return Pair;});
        }
    }
    public static void Run(Action<bool,string> check)
    {
        Hub.Reset();var f=new Fixture();var binding=f.Binding("invalid",0,false,false,true,true);binding.Tick(true,true,1,0);binding.Pause("pause",.1);binding.Cancel("cancel",.2);
        check(binding.Single==null && binding.Dual==null && f.SingleFactories==0 && f.DualFactories==0 && f.SingleReads==0 && f.DualReads==0 && Hub.Registers==0,"Explicit activation off selects no policy, reader or backend even with invalid/conflicting configuration");
        foreach(string fault in new[]{"missing-session","rewired","engine","unknown-profile","dual-neutral1","dual-neutral2","dual-neutral4","dual-neutral10"})
        {
            f=new Fixture();bool refused=false;int count=fault=="dual-neutral1"?1:fault=="dual-neutral2"?2:fault=="dual-neutral4"?4:fault=="dual-neutral10"?10:3;
            try {f.Binding(fault=="unknown-profile"?"none":OuterGamepadCompatibilityProfile.DualName,count,true,fault!="missing-session",fault=="rewired",fault=="engine");}catch(ArgumentException){refused=true;}
            check(refused && f.SingleFactories==0 && f.DualFactories==0 && f.SingleReads==0 && f.DualReads==0,"Activation preflight refuses its selected conflict/profile/neutral value before any reader/backend: "+fault);
        }
        foreach(string profile in new[]{OuterGamepadCompatibilityProfile.Name,OuterGamepadCompatibilityProfile.WakeName,OuterGamepadCompatibilityProfile.ButtonOnlyWakeName,OuterGamepadCompatibilityProfile.ObservedAxis0Name})
        {
            Hub.Reset();f=new Fixture();binding=f.Binding(profile,1);binding.Tick(true,true,1,0);
            check(binding.Single!=null && binding.Dual==null && binding.SupportsIdentityCapture && binding.Single.Status=="active" && f.SingleReads==1 && f.SingleFactories==1 && f.DualFactories==0 && f.DualReads==0,"Existing explicit single-hand profile retains only its reader/backend and private identity support: "+profile);
            binding.Cancel("end",.1);check(binding.Complete && Hub.Devices.Count==1,"Single-hand lifecycle cancellation removes only its selected owned device: "+profile);
        }
        foreach(int count in new[]{1,10})
        {Hub.Reset();f=new Fixture();binding=f.Binding(OuterGamepadCompatibilityProfile.WakeName,count);for(int n=0;n<count;++n)binding.Tick(true,true,1,n*.1);check(binding.Single.RequiredNeutralReads==count && f.SingleFactories==1,"Single-hand route retains configured neutral-read endpoint: "+count);binding.Cancel("end",2);}
        Hub.Reset();f=new Fixture();binding=f.Binding();
        check(binding.Dual!=null && binding.Single==null && !binding.SupportsIdentityCapture && f.DualFactories==0 && f.SingleFactories==0,"Dual selection owns only its pair policy and explicitly declines single-hand private identity capture");
        binding.Tick(false,true,1,0);binding.Tick(true,false,1,.1);
        check(f.SingleReads==0 && f.DualReads==0 && Hub.Registers==0,"Disabled/focus guards suppress both readers and backend creation in the actual binding");
        binding.Tick(true,true,1,.2);binding.Pause("session_disabled",.3);binding.Tick(false,true,1,.4);
        check(binding.Dual.NeutralReads==0 && !binding.Complete && f.DualFactories==0,"Reversible pause before dual admission retains the first authorized window while resetting pair stability");
        binding.Tick(true,true,1,.5);binding.Tick(true,true,1,.6);check(f.DualFactories==0,"Dual binding cannot create its backend after only two post-pause reads");binding.Tick(true,true,1,.7);
        check(binding.Dual.Status=="active" && f.DualFactories==1 && f.SingleFactories==0 && f.SingleReads==0 && Hub.Adds==1,"Exactly three pair reads enter the dual backend exclusively through Injector's binding route");
        f.Pair.Left.X=.6;f.Pair.Right.Y=-.6;f.Pair.Right.Pressed=128;binding.Tick(true,true,1,.8);Hub.NormalUpdate();binding.Tick(true,true,1,.9);
        check(binding.Dual.Queues==1 && binding.Dual.AppliedSamples==1 && Hub.Pending.Count==0,"Selected dual route queues both hands and observes normal public engine processing without single-hand reads");
        int reads=f.DualReads;binding.Tick(false,true,1,1);Hub.NormalUpdate();binding.Tick(false,true,1,1.1);
        check(binding.Complete && binding.Dual.ReleaseObserved && binding.Dual.RemoveAttempts==1 && f.DualReads==reads && Hub.Devices.Count==1,"Disabled dual route drains and observes both-stick neutral release without any source read");
        binding.Tick(true,true,2,1.2);binding.Pause("later",1.3);binding.Cancel("lifecycle",1.4);
        check(f.DualFactories==1 && f.SingleFactories==0 && f.DualReads==reads && Hub.Adds==1,"Ended selected dual window cannot restart or fall through to a single-hand backend on retry");
        foreach(string operation in new[]{"tick","pause","cancel"})
        {
            Hub.Reset();f=new Fixture();binding=f.Binding();binding.Tick(true,true,1,0);binding.Tick(true,true,1,.1);binding.Tick(true,true,1,.2);int prior=f.DualReads;
            var worker=new Thread(new ThreadStart(delegate {if(operation=="tick")binding.Tick(true,true,1,.3);if(operation=="pause")binding.Pause("foreign",.3);if(operation=="cancel")binding.Cancel("foreign",.3);}));worker.Start();worker.Join();
            check(binding.LastError=="owner_thread_required" && f.DualReads==prior && !binding.Complete && Hub.Removes==0,"Foreign-thread binding operation cannot read, pause or remove the owner-thread backend: "+operation);
            binding.Cancel("owner_teardown",.4);check(binding.Complete && Hub.Devices.Count==1,"Owner-thread teardown remains available after a rejected foreign-thread operation: "+operation);
        }
        Hub.Reset();f=new Fixture {ThrowPair=true};binding=f.Binding();binding.Tick(true,true,1,0);
        check(binding.LastPair.UnavailableReason=="reader_exception:System.InvalidOperationException" && binding.Dual.AddAttempts==0 && f.SingleReads==0,"Pair read exception is categorized without its message or falling through to the old reader");binding.Cancel("end",.1);
        Hub.Reset();f=new Fixture();binding=f.Binding();binding.Tick(true,true,1,0);binding.Cancel("component_disabled",.1);binding.Tick(true,true,1,.2);
        check(binding.Complete && f.DualFactories==0 && f.DualReads==1 && Hub.Registers==0,"Terminal lifecycle cancellation before admission prevents subsequent SDK reads or first device creation");
        Hub.Reset();f=new Fixture();binding=f.Binding();binding.Tick(true,true,1,0);binding.Tick(true,true,1,.1);binding.Tick(true,true,1,.2);f.Pair.Right.X=.6;binding.Tick(true,true,1,.3);Hub.NormalUpdate();
        binding.Pause("vr_failure",.4);int previousReads=f.DualReads;binding.Tick(false,true,1,.5);binding.Tick(true,true,2,.6);
        check(binding.Complete && binding.Dual.Reason=="vr_failure" && f.DualFactories==1 && f.SingleFactories==0 && f.DualReads==previousReads && Hub.Devices.Count==1,"VR failure pause after admission releases/removes exclusively through the selected dual policy and cannot start another window");
        int sdkGets=0;binding=GamepadDiagnosticBinding.Create(false,false,true,true,"none",0,0,"invalid",0,delegate {++sdkGets;return null;});binding.Tick(true,true,1,0);
        check(sdkGets==0 && binding.Single==null && binding.Dual==null,"Production factory's default-off route never gets the SDK or creates either public backend");
        Hub.Reset();
    }
}
