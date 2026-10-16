using System;
using System.Collections.Generic;
using Ugevr;
using Valve.VR;

static class RewiredCallbackFailureChecks
{
    const string PrivateText="private-fixture-exception-and-device-value";
    static RewiredRightSample Single(double amount=0)
    {return new RewiredRightSample {Available=true,Identity="private-fixture-source",Index=2,Packet=1,TriggerAxis=1,X=amount,Y=amount,Trigger=amount};}
    static RewiredPairSample Pair(double now,double amount=0)
    {return new RewiredPairSample {Available=true,Profile=PublicOwnedRewiredInput.FiveChannelProfile,StartedAt=now,CompletedAt=now,
        Left=new RewiredHandSample {Index=2,Packet=1,Role=ETrackedControllerRole.LeftHand,Identity="left",Model=OpenVrRewiredPairReader.LeftModel,Signature=OpenVrRewiredPairReader.Signature,X=amount,Y=amount},
        Right=new RewiredHandSample {Index=1,Packet=1,Role=ETrackedControllerRole.RightHand,Identity="right",Model=OpenVrRewiredPairReader.RightModel,Signature=OpenVrRewiredPairReader.Signature,X=amount,Y=amount,Trigger=amount}};}
    public static void Run(Action<bool,string> check)
    {
        foreach(bool five in new[]{false,true}) foreach(string fault in new[]{"healthy","setter","setter-direct-disable","snapshot-query","snapshot-values","snapshot-reference","wrong-thread"})
        {
            Rewired.ReInput.Reset();double now=0;
            var backend=new PublicOwnedRewiredInput(typeof(Rewired.ReInput).Assembly,new Rewired.InputManager_Base(),()=>now,()=>true,false,
                five?RewiredChannelDescriptor.Five:RewiredChannelDescriptor.Legacy);
            var single=five?null:new RewiredInputDiagnostic(()=>backend,15,.2,"refuse");
            var dual=five?new RewiredFiveInputDiagnostic(()=>backend,15,.2,"refuse"):null;
            Action<double,double> tick=(time,amount)=>{now=time;if(five)dual.Tick(true,true,1,Pair(time,amount),time);else single.Tick(true,true,1,Single(amount),time);};
            tick(0,0);if(five){tick(.01,0);tick(.02,0);}tick(.04,.6);
            var device=Rewired.ReInput.controllers.CustomControllers[0];var map=Rewired.ReInput.players.SystemPlayer.controllers.maps.Content[0];
            now=.05;Rewired.ReInput.Pulse();
            check(backend.AppliedUpdates==1 && device.Values[device.Values.Length-1]>.5,"Original public event delivers a first complete desired feed: "+five+"/"+fault);
            if(fault.StartsWith("setter"))Rewired.CustomController.BeforeSetAxis=(index,value)=>{if(index==1 && value>0)throw new InvalidOperationException(PrivateText);};
            int xmlCalls=0;
            if(fault=="snapshot-query")Rewired.ControllerMap.XmlTransform=xml=>{if(++xmlCalls==1)throw new InvalidOperationException(PrivateText);throw new ArgumentException(PrivateText);};
            if(fault=="snapshot-values")map.Entries[0].invert=true;
            if(fault=="snapshot-reference")map.Entries=new List<Rewired.ActionElementMap>(map.Entries);
            now=.06;
            if(fault=="wrong-thread") {var thread=new System.Threading.Thread(Rewired.ReInput.Pulse);thread.Start();thread.Join();}
            else Rewired.ReInput.Pulse();
            var original=backend.LastFailure;
            if(fault=="setter-direct-disable") {if(five)dual.Cancel("session_disabled",.07);else single.Cancel("session_disabled",.07);}
            else tick(.07,.6);
            var failure=five?dual.Failure:single.Failure;var cleanup=five?dual.CleanupFailure:single.CleanupFailure;
            if(fault=="healthy")
            {
                check(backend.AppliedUpdates==2 && failure==null && cleanup==null && backend.LastFailure==null && backend.LastCleanupFailure==null,"Healthy public callbacks fabricate no primary or cleanup diagnostics: "+five);
                if(five)dual.Cancel("normal_end",.08);else single.Cancel("normal_end",.08);
                check(!backend.NeedsCleanup && Rewired.ReInput.Subscribers==0 && Rewired.ReInput.Data.Definitions.Count==0,"Normal profile still removes only its unchanged owned state: "+five);
                continue;
            }
            check(original!=null && Object.ReferenceEquals(original,failure) && backend.AppliedUpdates==1 && (five?dual.Complete:single.Complete),"Callback refusal propagates the exact first retained failure through ordinary policy Tick: "+five+"/"+fault);
            string expected=fault.StartsWith("setter")?"invoke_public_method_SetAxisValue":fault=="snapshot-query"?"callback_snapshot_assignment_maps":fault=="wrong-thread"?"callback_thread":"callback_snapshot";
            check(failure.Stage==expected && !failure.ToLog().Contains(PrivateText) && failure.ToLog().Length<1200,"Controlled callback/member stage is emitted without private exception text: "+five+"/"+fault);
            if(fault=="snapshot-query")
            {
                check(failure.ExceptionType=="System.Reflection.TargetInvocationException" && failure.InnerExceptionType=="System.InvalidOperationException" && cleanup!=null && cleanup.Stage=="cleanup_snapshot_assignment_maps" && cleanup.InnerExceptionType=="System.ArgumentException","Original callback query exception and later distinct cleanup query exception remain separately observable: "+five);
                check(backend.NeedsCleanup && (five?dual.RemoveAttempts:single.RemoveAttempts)==2 && backend.CleanupStatus=="cleanup_unconfirmed" && Array.TrueForAll(device.Values,v=>v==0) && Rewired.ReInput.controllers.Destroys==0,"The V8-style snapshot exception preserves bounded Remove throw behavior and intact-channel neutral release: "+five);
                check(cleanup.ToCleanupLog().Contains("cleanupFailureStage=cleanup_snapshot_assignment_maps") && !cleanup.ToCleanupLog().Contains(PrivateText) && cleanup.ToCleanupLog().Length<700,"Cleanup log uses separate bounded fields without raw messages: "+five);
            }
            else if(fault.StartsWith("snapshot-"))
            {
                check(failure.Category==(fault=="snapshot-values"?"snapshot_values_changed":"snapshot_reference_changed") && cleanup==null && backend.CleanupStatus=="changed_or_added_content_preserved","Cached snapshot mismatch identifies values/reference without turning preserved foreign map state into a cleanup exception: "+five+"/"+fault);
                check(backend.NeedsCleanup && Rewired.ReInput.controllers.Destroys==0 && Rewired.ReInput.Data.Deletes==0 && Array.TrueForAll(device.Values,v=>v==0),"Changed map resources remain preserved while intact channels are neutralized: "+five+"/"+fault);
            }
            else
                check(!backend.NeedsCleanup && cleanup==null && Array.TrueForAll(device.Values,v=>v==0) && Rewired.ReInput.Subscribers==0,"A callback setter/thread failure still permits original safe owner-thread neutral/removal: "+five+"/"+fault);
            if(five){dual.Cancel("later",1);dual.Tick(true,true,2,Pair(1),1);}else {single.Cancel("later",1);single.Tick(true,true,2,Single(),1);}
            check(Object.ReferenceEquals(failure,five?dual.Failure:single.Failure) && Object.ReferenceEquals(cleanup,five?dual.CleanupFailure:single.CleanupFailure) && backend.AppliedUpdates==1,"Later cancel/tick cannot replace either first cause or recreate a completed profile: "+five+"/"+fault);
        }
        Rewired.ReInput.Reset();
    }
}
