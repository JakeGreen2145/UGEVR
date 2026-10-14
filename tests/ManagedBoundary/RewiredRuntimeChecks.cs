using System;
using Ugevr;

static class RewiredRuntimeChecks
{
    static RewiredRightSample Sample(double trigger=0) {return new RewiredRightSample {Available=true,Identity="invented-runtime-source",Index=2,Packet=17,TriggerAxis=1,Trigger=trigger,X=trigger};}
    public static void Run(Action<bool,string> check)
    {
        foreach(string fault in new[]{"callback-cleared","no-first-callback","ready-lost","manager-inactive","manager-data-replaced","complete-runtime-replaced","owned-device-removed","clock-regressed","clock-nonfinite","changed-map"})
        {
            Rewired.ReInput.Reset();double now=0;var manager=new Rewired.InputManager_Base();var originalData=Rewired.ReInput.Data;
            var adapter=new PublicOwnedRewiredInput(typeof(Rewired.ReInput).Assembly,manager,()=>now,()=>true,false);var policy=new RewiredInputDiagnostic(()=>adapter,15,.2,"refuse");policy.Tick(true,true,1,Sample(),0);
            var owned=Rewired.ReInput.controllers.CustomControllers[0];
            if(fault!="no-first-callback") {now=.05;Rewired.ReInput.Pulse();now=.1;policy.Tick(true,true,1,Sample(.7),now);Rewired.ReInput.Pulse();}
            int feeds=policy.AppliedUpdates;
            if(fault=="callback-cleared")Rewired.ReInput.ClearInputSourceCallbacks();
            if(fault=="ready-lost")Rewired.ReInput.isReady=false;
            if(fault=="manager-inactive")manager.isActiveAndEnabled=false;
            if(fault=="manager-data-replaced")Rewired.ReInput.Data=new Rewired.Data.UserData();
            if(fault=="complete-runtime-replaced")Rewired.ReInput.Reset();
            if(fault=="owned-device-removed")Rewired.ReInput.controllers.CustomControllers.Remove(owned);
            if(fault=="changed-map")Rewired.ReInput.players.SystemPlayer.controllers.maps.Content[0].Entries.Reverse();
            now=fault=="clock-regressed"?.09:fault=="clock-nonfinite"?Double.NaN:fault=="callback-cleared"?.61:fault=="no-first-callback"?.5:.21;
            policy.Tick(true,true,1,Sample(),fault.StartsWith("clock-")?.21:now);
            string category=fault=="callback-cleared"?"input_callback_stale":fault=="no-first-callback"?"input_callback_never_observed":fault=="ready-lost"?"runtime_not_ready":fault=="manager-inactive"?"active_manager_unavailable":fault=="manager-data-replaced" || fault=="complete-runtime-replaced"?"manager_data_changed":fault=="owned-device-removed"?"owned_device_missing_from_current_context":fault.StartsWith("clock-")?"backend_clock_invalid":"owned_content_changed";
            check(policy.Complete && policy.Failure!=null && policy.Failure.Category==category && policy.AddAttempts==1 && policy.AppliedUpdates==feeds,"Actual public runtime monitor terminates a fresh-source policy on lost delivery/context without waiting for a missing callback: "+fault);
            if(fault=="callback-cleared" || fault=="no-first-callback" || fault.StartsWith("clock-"))
                check(!adapter.NeedsCleanup && originalData.Definitions.Count==0 && originalData.Maps.Count==0 && owned.Values[0]==0 && owned.Values[2]==0 && policy.RemoveAttempts==1,"Intact current channels still receive bounded neutral/removal cleanup after a heartbeat/clock fault: "+fault);
            else if(fault=="changed-map")
                check(adapter.NeedsCleanup && owned.Values[0]==0 && owned.Values[2]==0 && originalData.Definitions.Count==1 && originalData.Deletes==0,"Changed map content remains preserved while only intact original channels are neutralized");
            else
                check(adapter.NeedsCleanup && policy.RemoveAttempts==2 && adapter.CleanupStatus=="neutral_release_unconfirmed" && originalData.Definitions.Count==1 && originalData.Deletes==0 && owned.Values[2]>.6,"Lost runtime identity never writes stale owned channels or destroys retired definition content: "+fault);
            var failure=policy.Failure;now=1;policy.Tick(true,true,2,Sample(),1);policy.Cancel("later",2);
            check(policy.AddAttempts==1 && policy.AppliedUpdates==feeds && Object.ReferenceEquals(failure,policy.Failure),"Runtime failure remains sticky and cannot recreate or re-subscribe a second window: "+fault);
        }
        Rewired.ReInput.Reset();double clock=0;var healthy=new PublicOwnedRewiredInput(typeof(Rewired.ReInput).Assembly,new Rewired.InputManager_Base(),()=>clock,()=>true,false);var active=new RewiredInputDiagnostic(()=>healthy,15,.2,"refuse");active.Tick(true,true,1,Sample(),0);
        clock=.49;active.Tick(true,true,1,Sample(),.49);
        check(!active.Complete && active.Failure==null && Rewired.ReInput.Subscribers==1,"A normally subscribed backend receives a bounded grace period for its first ordinary public callback");
        Rewired.ReInput.Pulse();
        for(int i=1;i<=12;++i) {clock=.49+i*.1;active.Tick(true,true,1,Sample(.6),clock);Rewired.ReInput.Pulse();}
        check(!active.Complete && active.AppliedUpdates==13 && active.Failure==null,"Regular normal engine callbacks refresh the independent heartbeat across repeated policy ticks");
        active.Cancel("normal_disable",clock+.1);check(!healthy.NeedsCleanup && Rewired.ReInput.Data.Definitions.Count==0 && Rewired.ReInput.Subscribers==0,"Healthy monitored backend retains ordinary bounded disable cleanup");
        Rewired.ReInput.Reset();clock=0;healthy=new PublicOwnedRewiredInput(typeof(Rewired.ReInput).Assembly,new Rewired.InputManager_Base(),()=>clock,()=>true,false);active=new RewiredInputDiagnostic(()=>healthy,15,.2,"refuse");active.Tick(true,true,1,Sample(),0);
        clock=.1;active.Tick(true,true,1,Sample(.7),clock);Rewired.ReInput.Pulse();var delayed=Rewired.ReInput.controllers.CustomControllers[0];int priorFeeds=active.AppliedUpdates;
        clock=.61;Rewired.ReInput.Pulse();
        check(healthy.FaultReason=="source_expired_or_clock_invalid" && delayed.Values[0]==0 && delayed.Values[2]==0 && active.AppliedUpdates==priorFeeds,"An ordinary callback after an elapsed 500ms source-refresh gap neutralizes intact channels without counting another input feed");
        active.Tick(true,true,1,Sample(),clock);
        check(active.Complete && active.Reason=="source_expired_or_clock_invalid" && !healthy.NeedsCleanup && active.AddAttempts==1 && active.RemoveAttempts==1,"A resumed policy preserves expiry and removes only its owned resources without extending the deadline or creating another window");
        Rewired.ReInput.Reset();clock=0;var owner=new Rewired.InputManager_Base();healthy=new PublicOwnedRewiredInput(typeof(Rewired.ReInput).Assembly,owner,()=>clock,()=>true,false);healthy.Add();healthy.SetDesired(.6,0,.7,1);clock=.1;Rewired.ReInput.Pulse();var retained=Rewired.ReInput.controllers.CustomControllers[0];Rewired.ReInput.Reset();clock=.2;healthy.Remove();
        check(healthy.NeedsCleanup && healthy.CleanupStatus=="neutral_release_unconfirmed" && retained.Values[2]>.6 && Rewired.ReInput.Data.Definitions.Count==0,"Teardown checks current public context even when no later policy/monitor tick occurred after reset");
        Rewired.ReInput.Reset();
    }
}
