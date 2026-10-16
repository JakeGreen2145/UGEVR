using System;
using System.Collections;
using System.Collections.Generic;
using Ugevr;
using Valve.VR;

static class RewiredJoystickInventoryChecks
{
    const string PrivateText="private-roster-error-and-device-name";
    sealed class Roster : ICollection
    {
        public readonly string Mode;
        public int Gets,Moves,Currents,Disposes,Counts;
        public Roster(string mode) {Mode=mode;}
        public int Count {get {++Counts;throw new InvalidOperationException(PrivateText);}}
        public bool IsSynchronized {get {throw new NotSupportedException();}}
        public object SyncRoot {get {throw new NotSupportedException();}}
        public void CopyTo(Array array,int index) {throw new NotSupportedException();}
        public IEnumerator GetEnumerator()
        {
            ++Gets;if(Mode=="get")throw new InvalidOperationException(PrivateText);
            if(Mode=="null-enumerator")return null;
            return new Cursor(this);
        }
        sealed class Cursor : IEnumerator,IDisposable
        {
            readonly Roster owner;int at=-1;
            public Cursor(Roster owner) {this.owner=owner;}
            public bool MoveNext()
            {
                ++owner.Moves;++at;
                if((owner.Mode=="move" || owner.Mode=="move-dispose") && at==1)throw new InvalidOperationException(PrivateText);
                return at<(owner.Mode=="empty"?0:owner.Mode=="bound" || owner.Mode=="bound-dispose"?65:owner.Mode=="two"?2:1);
            }
            public object Current
            {
                get {++owner.Currents;if(owner.Mode=="current")throw new InvalidOperationException(PrivateText);
                    if(owner.Mode=="null-entry" || owner.Mode=="null-dispose")return null;
                    return new Rewired.Controller(99);}
            }
            public void Reset() {throw new NotSupportedException();}
            public void Dispose() {++owner.Disposes;if(owner.Mode=="dispose" || owner.Mode.EndsWith("-dispose"))throw new ArgumentException(PrivateText);}
        }
    }
    static RewiredRightSample Single(double amount=0)
    {return new RewiredRightSample {Available=true,Identity="private-source",Index=2,Packet=1,TriggerAxis=1,X=amount,Y=amount,Trigger=amount};}
    static RewiredPairSample Pair(double now,double amount=0)
    {return new RewiredPairSample {Available=true,Profile=PublicOwnedRewiredInput.FiveChannelProfile,StartedAt=now,CompletedAt=now,
        Left=new RewiredHandSample {Index=2,Packet=1,Role=ETrackedControllerRole.LeftHand,Identity="left",Model=OpenVrRewiredPairReader.LeftModel,Signature=OpenVrRewiredPairReader.Signature,X=amount,Y=amount},
        Right=new RewiredHandSample {Index=1,Packet=1,Role=ETrackedControllerRole.RightHand,Identity="right",Model=OpenVrRewiredPairReader.RightModel,Signature=OpenVrRewiredPairReader.Signature,X=amount,Y=amount,Trigger=amount}};}
    public static void Run(Action<bool,string> check)
    {
        foreach(bool five in new[]{false,true}) foreach(string mode in new[]{"empty","one","null","non-enumerable","null-entry","bound","get","move","current","dispose","null-enumerator","move-dispose","null-dispose","bound-dispose"})
        {
            Rewired.ReInput.Reset();double now=0;
            var backend=new PublicOwnedRewiredInput(typeof(Rewired.ReInput).Assembly,new Rewired.InputManager_Base(),()=>now,()=>true,false,
                five?RewiredChannelDescriptor.Five:RewiredChannelDescriptor.Legacy);
            var single=five?null:new RewiredInputDiagnostic(()=>backend,15,.2,"refuse");
            var dual=five?new RewiredFiveInputDiagnostic(()=>backend,15,.2,"refuse"):null;
            Action<double,double> tick=(time,amount)=>{now=time;if(five)dual.Tick(true,true,1,Pair(time,amount),time);else single.Tick(true,true,1,Single(amount),time);};
            tick(0,0);if(five){tick(.01,0);tick(.02,0);}tick(.04,.6);now=.05;Rewired.ReInput.Pulse();
            var helper=Rewired.ReInput.controllers;var device=helper.CustomControllers[0];var roster=new Roster(mode);
            helper.JoystickResult=()=>mode=="null"?null:mode=="non-enumerable"?new object():(object)roster;
            int before=helper.JoystickQueries;now=.06;Rewired.ReInput.Pulse();
            check(helper.JoystickQueries==before+1 && roster.Counts==0,"Public callback queries the joystick property once and never asks ICollection.Count: "+five+"/"+mode);
            if(mode=="empty")
            {
                check(backend.LastFailure==null && backend.AppliedUpdates==2 && roster.Gets==1 && roster.Moves==1 && roster.Currents==0 && roster.Disposes==1,"Healthy empty inventory completes ordinary event feeding after disposal: "+five);
                tick(.07,.6);if(five)dual.Cancel("normal",.08);else single.Cancel("normal",.08);
                check(!backend.NeedsCleanup && (five?dual.Failure:single.Failure)==null && (five?dual.CleanupFailure:single.CleanupFailure)==null,"Healthy empty inventory does not fabricate diagnostics or change owned removal: "+five);
                continue;
            }
            var first=backend.LastFailure;var info=first.JoystickInventory;
            string verdict=mode=="one"?"completed_nonempty":mode=="null"?"collection_null":mode=="non-enumerable"?"collection_not_enumerable":mode=="null-entry"?"null_entry":mode=="bound"?"bound_exceeded":"enumerator_exception";
            string operation=mode=="one"?"completed":mode=="null" || mode=="non-enumerable"?"collection":mode=="get"?"get_enumerator":mode=="move" || mode=="null-enumerator"?"move_next":mode=="current"?"current":mode=="null-entry" || mode=="bound"?"entry":"dispose";
            int observed=mode=="one" || mode=="move" || mode=="move-dispose" || mode=="dispose"?1:mode=="bound" || mode=="bound-dispose"?65:0;
            check(info!=null && info.Verdict==verdict && info.Operation==operation && info.ObservedEntries==observed && info.CompletedCount==(mode=="one"?(int?)1:null),"Native-style first failure distinguishes completed counts from partial entries and every enumeration operation: "+five+"/"+mode);
            check(first.Category=="joystick_"+verdict && first.Stage=="callback_snapshot_physical_joysticks" && first.ExceptionType==(operation=="dispose"?"System.ArgumentException":mode=="null-enumerator"?"System.NullReferenceException":"System.InvalidOperationException") && first.InnerExceptionType=="none","Original exception type and foreach Dispose-over-body precedence remain exact: "+five+"/"+mode);
            check(roster.Gets==(mode=="null" || mode=="non-enumerable"?0:1) && roster.Disposes==(mode=="null" || mode=="non-enumerable" || mode=="get" || mode=="null-enumerator"?0:1) && backend.AppliedUpdates==1,"No repeated roster enumeration or successful feed follows a refused inventory: "+five+"/"+mode);
            var cleanupRoster=new Roster("two");helper.JoystickResult=()=>cleanupRoster;
            tick(.07,.6);
            var failure=five?dual.Failure:single.Failure;var cleanup=five?dual.CleanupFailure:single.CleanupFailure;
            check(Object.ReferenceEquals(first,failure) && cleanup!=null && cleanup.JoystickInventory.CompletedCount==2 && cleanup.JoystickInventory.ObservedEntries==2 && cleanup.JoystickInventory.Verdict=="completed_nonempty" && cleanup.Stage=="cleanup_snapshot_physical_joysticks","Callback inventory is immutable while first cleanup independently observes a different complete roster: "+five+"/"+mode);
            check(helper.JoystickQueries==before+3 && cleanupRoster.Gets==2 && cleanupRoster.Disposes==2 && cleanupRoster.Counts==0 && (five?dual.RemoveAttempts:single.RemoveAttempts)==2 && backend.NeedsCleanup && helper.Destroys==0 && Rewired.ReInput.Data.Deletes==0 && Array.TrueForAll(device.Values,v=>v==0),"Two unchanged conditional removal attempts preserve roster/resources and neutralize only intact owned channels: "+five+"/"+mode);
            string text=failure.ToLog()+" "+cleanup.ToCleanupLog();
            check(text.Contains("joystickCompletedCount="+(mode=="one"?"1":"not_completed")) && text.Contains("cleanupJoystickCompletedCount=2") && !text.Contains(PrivateText) && text.Length<1800,"Only bounded scalar count/category fields are logged, never roster IDs, names or exception messages: "+five+"/"+mode);
            if(five)dual.Cancel("later",1);else single.Cancel("later",1);
            check(Object.ReferenceEquals(failure,five?dual.Failure:single.Failure) && Object.ReferenceEquals(cleanup,five?dual.CleanupFailure:single.CleanupFailure),"Both first observations remain sticky after later cancellation: "+five+"/"+mode);
        }
        Rewired.ReInput.Reset();
    }
}
