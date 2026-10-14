using System;
using Ugevr;

static class RewiredMapFactoryChecks
{
    static PublicOwnedRewiredInput Adapter() {return new PublicOwnedRewiredInput(typeof(Rewired.ReInput).Assembly,new Rewired.InputManager_Base(),()=>0,()=>true,false);}
    static RewiredRightSample Neutral() {return new RewiredRightSample {Available=true,Identity="invented-source",Index=2,Packet=1,TriggerAxis=1};}
    static RewiredInputDiagnostic Policy(PublicOwnedRewiredInput backend) {return new RewiredInputDiagnostic(()=>backend,15,.2,"refuse");}
    public static void Run(Action<bool,string> check)
    {
        Rewired.ReInput.Reset();var data=Rewired.ReInput.Data;
        data.AddCustomController();bool returned=data.CreateCustomControllerMap(0,2,0);
        check(returned==false && data.Maps.Count==1 && data.Maps[0].actionElementMaps.Count==0,"Controlled public factory reproduces the shipped contract: false return after creating one fresh empty map");
        Rewired.ReInput.Reset();var adapter=Adapter();var policy=Policy(adapter);policy.Tick(true,true,1,Neutral(),0);data=Rewired.ReInput.Data;
        check(policy.Status=="active" && policy.Failure==null && data.MapCreates==1 && data.Maps.Count==1 && data.Maps[0].actionElementMaps.Count==3 && Rewired.ReInput.Subscribers==1,"Actual adapter admits the normally returned false factory only after its fresh empty map/content proof and adds exactly its three entries");
        var moving=Neutral();moving.X=.6;moving.Trigger=.7;policy.Tick(true,true,1,moving,.1);Rewired.ReInput.Pulse();
        check(policy.AppliedUpdates==1 && Rewired.ReInput.controllers.CustomControllers[0].Values[0]>.4 && Rewired.ReInput.controllers.CustomControllers[0].Values[2]>.6,"Admitted post-state proof permits the existing normal public update event to feed original owned channels");
        policy.Cancel("end",.2);
        check(policy.Complete && !adapter.NeedsCleanup && data.Definitions.Count==0 && data.Maps.Count==0 && Rewired.ReInput.Subscribers==0,"Successfully proved false-return creation cleans only its owned definition, map, device and callback");
        foreach(string fault in new[]{"no-addition","foreign-extra","wrong-layout","preloaded-entry","changed-definition","null-entries","throws-after"})
        {
            Rewired.ReInput.Reset();data=Rewired.ReInput.Data;
            if(fault=="no-addition")data.MapNoAddition=true;if(fault=="foreign-extra")data.MapExtraAddition=true;if(fault=="wrong-layout")data.MapWrongLayout=true;if(fault=="preloaded-entry")data.AddEntryDuringMapCreate=true;if(fault=="changed-definition")data.MapChangeDefinition=true;if(fault=="null-entries")data.MapNullEntries=true;if(fault=="throws-after")data.MapThrowAfter=true;
            adapter=Adapter();policy=Policy(adapter);policy.Tick(true,true,1,Neutral(),0);
            check(policy.Complete && policy.AddAttempts==1 && policy.AppliedUpdates==0 && data.MapCreates==1 && Rewired.ReInput.controllers.CustomControllers.Count==0 && Rewired.ReInput.Subscribers==0,"False/exception result cannot bypass absent, foreign, changed or incomplete map state: "+fault);
            if(fault=="no-addition")check(policy.Failure.Category=="map_post_state_unavailable_or_ambiguous" && !adapter.NeedsCleanup && data.Definitions.Count==0 && data.Maps.Count==0,"A false return with no addition refuses input and removes only the previously proved unchanged definition");
            else check(adapter.NeedsCleanup && data.Definitions.Count==1 && data.Deletes==0 && data.Maps.Count==(fault=="foreign-extra"?2:1),"Unproved/changed map contents preserve definition/map resources rather than erasing them: "+fault);
            if(fault=="throws-after")check(policy.Failure.Stage=="invoke_public_method_CreateCustomControllerMap" && policy.Failure.Category=="public_method_exception","Thrown creation after addition retains unknown origin and its original API failure instead of adopting its plausible map");
            if(fault=="changed-definition")check(policy.Failure.Category=="definition_changed_during_map_creation" && data.Definitions[0].name=="changed during map creation","A changed admitted definition prevents adopting or deleting the new map");
            int attempts=policy.AddAttempts;policy.Tick(true,true,2,Neutral(),1);policy.Cancel("retry",2);
            check(policy.AddAttempts==attempts && data.MapCreates==1,"A rejected map factory window never retries or spends another creation: "+fault);
        }
        Rewired.ReInput.Reset();data=Rewired.ReInput.Data;
        var existing=new Rewired.Data.Mapping.ControllerMap_Editor {id=99,categoryId=0,layoutId=0,customControllerUid=2};data.Maps.Add(existing);
        adapter=Adapter();policy=Policy(adapter);policy.Tick(true,true,1,Neutral(),0);
        check(policy.Complete && data.MapCreates==0 && adapter.NeedsCleanup && data.Deletes==0 && data.Maps.Count==1 && Object.ReferenceEquals(existing,data.Maps[0]),"An already existing map for the fresh definition ID refuses before the factory and remains untouched");
        existing.name="external update after refusal";policy.Tick(true,true,1,Neutral(),1);policy.Cancel("end",2);
        check(data.Maps[0].name=="external update after refusal" && data.MapCreates==0 && data.Deletes==0,"Changed existing map content cannot become owned through a later tick or cancellation");Rewired.ReInput.Reset();
    }
}
