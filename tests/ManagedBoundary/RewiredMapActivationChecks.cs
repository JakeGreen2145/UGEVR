using System;
using Ugevr;

static class RewiredMapActivationChecks
{
    static RewiredRightSample Sample(double value=0) {return new RewiredRightSample {Available=true,Identity="fixture-owned-source",Index=2,Packet=1,TriggerAxis=1,X=value,Trigger=value};}
    static PublicOwnedRewiredInput Backend(Func<double> clock) {return new PublicOwnedRewiredInput(typeof(Rewired.ReInput).Assembly,new Rewired.InputManager_Base(),clock,()=>true,false);}
    public static void Run(Action<bool,string> check)
    {
        Rewired.ReInput.Reset();double now=0;var backend=Backend(()=>now);backend.Add();var map=Rewired.ReInput.players.SystemPlayer.controllers.maps.Content[0];
        check(map is Rewired.CustomControllerMap && map.enabled && Rewired.ControllerMap.EnableCalls==1 && Rewired.ReInput.Subscribers==1,"Actual public backend enables the freshly assigned default-disabled custom map before subscribing");
        backend.SetDesired(.4,-.6,.7,1);now=.1;Rewired.ReInput.Pulse();
        check(Rewired.ReInput.players.SystemPlayer.GetAxisRaw(31)==.4f && Rewired.ReInput.players.SystemPlayer.GetAxisRaw(32)==-.6f && Rewired.ReInput.players.SystemPlayer.GetAxisRaw(36)==.7f,"Enabled owned fixture map aggregates the three normal system-player actions through public readback");
        backend.Remove();check(!backend.NeedsCleanup && Rewired.ReInput.Data.Definitions.Count==0 && Rewired.ReInput.Subscribers==0,"Verified owned enabled-map snapshot supports ordinary neutral/removal cleanup");
        foreach(string malformed in new[]{"missing","duplicate","numeric-zero","uppercase","whitespace","nested-only","nested-content","root-attribute","field-attribute","wrong-namespace","wrong-root","broken","doctype"})
        {
            Rewired.ReInput.Reset();now=0;backend=Backend(()=>now);
            Rewired.ControllerMap.XmlTransform=xml=> {
                if(malformed=="missing")return xml.Replace("<enabled>false</enabled>","");
                if(malformed=="duplicate")return xml.Replace("<enabled>false</enabled>","<enabled>false</enabled><enabled>false</enabled>");
                if(malformed=="numeric-zero")return xml.Replace("<enabled>false</enabled>","<enabled>0</enabled>");
                if(malformed=="uppercase")return xml.Replace("<enabled>false</enabled>","<enabled>False</enabled>");
                if(malformed=="whitespace")return xml.Replace("<enabled>false</enabled>","<enabled> false </enabled>");
                if(malformed=="nested-only")return xml.Replace("<enabled>false</enabled>","<other><enabled>false</enabled></other>");
                if(malformed=="nested-content")return xml.Replace("<enabled>false</enabled>","<enabled><value>false</value></enabled>");
                if(malformed=="root-attribute")return xml.Replace("<enabled>false</enabled>","").Replace(" dataVersion=", " enabled=\"false\" dataVersion=");
                if(malformed=="field-attribute")return xml.Replace("<enabled>false</enabled>","<enabled unexpected=\"1\">false</enabled>");
                if(malformed=="wrong-namespace")return xml.Replace("<enabled>false</enabled>","<enabled xmlns=\"\">false</enabled>");
                if(malformed=="wrong-root")return xml.Replace("CustomControllerMap","ForeignMap");
                if(malformed=="broken")return xml+"<";
                return "<!DOCTYPE CustomControllerMap [<!ENTITY value 'false'>]>"+xml.Replace("<enabled>false</enabled>","<enabled>&value;</enabled>");
            };
            var refused=new RewiredInputDiagnostic(()=>backend,15,.2,"refuse");refused.Tick(true,true,1,Sample(),0);
            check(refused.Complete && refused.Reason=="public_creation_failed" && refused.AppliedUpdates==0 && Rewired.ControllerMap.EnableCalls==0 && Rewired.ReInput.Subscribers==0,"Strict shipped enabled-element shape refuses unavailable or altered serialization before setter/feed: "+malformed);
            check(backend.NeedsCleanup && Rewired.ReInput.Data.Deletes==0 && Rewired.ReInput.controllers.Destroys==0,"Unknown enabled serialization does not adopt or destroy the assigned map: "+malformed);
        }
        foreach(string altered in new[]{"source-map","entry-mapping","added-map"})
        {
            Rewired.ReInput.Reset();now=0;backend=Backend(()=>now);
            Rewired.Player.ControllerHelper.MapHelper.AfterLoad=m=> {if(altered=="source-map")m.sourceMapId=991;else if(altered=="entry-mapping")m.Entries[1].elementIdentifierId=0;else Rewired.ReInput.players.SystemPlayer.controllers.maps.Content.Add(new Rewired.CustomControllerMap {controllerId=m.controllerId});};
            var refused=new RewiredInputDiagnostic(()=>backend,15,.2,"refuse");refused.Tick(true,true,1,Sample(),0);
            check(refused.Complete && refused.AppliedUpdates==0 && Rewired.ControllerMap.EnableCalls==0 && Rewired.ReInput.Subscribers==0,"Assignment returning changed or extra runtime map content is refused before any enable setter: "+altered);
            check(backend.NeedsCleanup && Rewired.ReInput.Data.Deletes==0 && Rewired.ReInput.controllers.Destroys==0,"Unproved initial runtime map content is preserved rather than adopted as an owned baseline: "+altered);
        }
        foreach(string after in new[]{"numeric-one","missing","duplicate","nested-value-changed"})
        {
            Rewired.ReInput.Reset();now=0;backend=Backend(()=>now);
            Rewired.ControllerMap.XmlTransform=xml=> {
                bool enabled=xml.Contains("<enabled>true</enabled>");
                if(after=="nested-value-changed")return xml.Replace("</CustomControllerMap>","<other><enabled>"+(enabled?"true":"false")+"</enabled></other></CustomControllerMap>");
                if(!enabled)return xml;
                return xml.Replace("<enabled>true</enabled>",after=="numeric-one"?"<enabled>1</enabled>":after=="missing"?"":"<enabled>true</enabled><enabled>true</enabled>");
            };
            var refused=new RewiredInputDiagnostic(()=>backend,15,.2,"refuse");refused.Tick(true,true,1,Sample(),0);
            check(refused.Complete && refused.AppliedUpdates==0 && Rewired.ControllerMap.EnableCalls==1 && Rewired.ReInput.Subscribers==0,"Unexpected post-setter XML cannot become the activated expected snapshot: "+after);
            check(backend.NeedsCleanup && Rewired.ReInput.Data.Deletes==0 && Rewired.ReInput.controllers.Destroys==0,"Post-setter XML disagreement preserves the previous proof and uncertain map resources: "+after);
        }
        foreach(string fault in new[]{"ignored","throw-before","throw-after","changed-entry","changed-definition","foreign-map","foreign-device","disabled-controller"})
        {
            Rewired.ReInput.Reset();now=0;backend=Backend(()=>now);var policy=new RewiredInputDiagnostic(()=>backend,15,.2,"refuse");
            if(fault=="ignored")Rewired.ControllerMap.IgnoreEnable=true;
            if(fault=="throw-before")Rewired.ControllerMap.ThrowEnableBefore=true;
            if(fault=="throw-after")Rewired.ControllerMap.ThrowEnableAfter=true;
            if(fault=="disabled-controller")Rewired.ReInput.controllers.CreateDisabled=true;
            if(fault=="changed-entry")Rewired.ControllerMap.EnableAction=m=>m.Entries[0].actionId=99;
            if(fault=="changed-definition")Rewired.ControllerMap.EnableAction=m=>Rewired.ReInput.Data.Definitions[0].name="externally changed";
            if(fault=="foreign-map")Rewired.ControllerMap.EnableAction=m=>Rewired.ReInput.players.SystemPlayer.controllers.maps.Content.Add(new Rewired.CustomControllerMap {controllerId=m.controllerId});
            if(fault=="foreign-device")Rewired.ControllerMap.EnableAction=m=>Rewired.ReInput.controllers.CustomControllers.Add(new Rewired.CustomController(91,2));
            policy.Tick(true,true,1,Sample(),0);
            check(policy.Complete && policy.Reason=="public_creation_failed" && policy.AddAttempts==1 && policy.AppliedUpdates==0 && policy.Failure!=null && Rewired.ReInput.Subscribers==0,"Map activation refuses an unconfirmed outcome without subscribing or feeding: "+fault);
            if(fault=="ignored" || fault=="throw-before")
                check(!backend.NeedsCleanup && Rewired.ReInput.Data.Definitions.Count==0,"Unchanged owned false-map state remains eligible for bounded cleanup: "+fault);
            else
                check(backend.NeedsCleanup && Rewired.ReInput.Data.Definitions.Count==1 && Rewired.ReInput.controllers.Destroys==0 && Rewired.ReInput.Data.Deletes==0,"Unconfirmed or foreign map/device changes remain preserved: "+fault);
            if(fault=="ignored")check(policy.Failure.Category=="owned_map_enable_readback_false","A normal setter return with false readback is a precise controlled refusal");
            if(fault=="disabled-controller")check(policy.Failure.Category=="owned_controller_disabled" && Rewired.ControllerMap.EnableCalls==0 && Rewired.ReInput.controllers.CustomControllers[0].enabled==false,"A fresh disabled controller is refused and is never implicitly enabled");
            int calls=Rewired.ControllerMap.EnableCalls;policy.Tick(true,true,2,Sample(),1);check(policy.AddAttempts==1 && Rewired.ControllerMap.EnableCalls==calls,"Failed activation cannot create or enable a second window: "+fault);
        }
        Rewired.ReInput.Reset();now=0;backend=Backend(()=>now);backend.Add();map=Rewired.ReInput.players.SystemPlayer.controllers.maps.Content[0];backend.SetDesired(.5,0,.7,1);now=.1;Rewired.ReInput.Pulse();map.enabled=false;now=.2;
        bool stopped=false;try {backend.CheckRuntime();}catch {stopped=true;}backend.Remove();
        check(stopped && backend.NeedsCleanup && Rewired.ReInput.controllers.CustomControllers[0].Values[2]==0 && map.enabled==false && Rewired.ReInput.Data.Deletes==0,"A later external map disable is preserved while only intact owned axes are neutralized");
        Rewired.ReInput.Reset();now=0;backend=Backend(()=>now);var disabled=new RewiredInputDiagnostic(()=>backend,15,.2,"refuse");disabled.Tick(true,true,1,Sample(),0);now=.1;Rewired.ReInput.Pulse();disabled.Tick(true,true,1,Sample(.7),now);
        check(disabled.Readback==null && disabled.ReadbackSamples==0 && Rewired.Player.ActionReads==0,"Default-off action diagnostics perform no additional public action reads");disabled.Cancel("done",now);
        Rewired.ReInput.Reset();now=0;backend=Backend(()=>now);var active=new RewiredInputDiagnostic(()=>backend,5,.2,"refuse",true);active.Tick(true,true,1,Sample(),0);var owned=Rewired.ReInput.controllers.CustomControllers[0];Rewired.ReInput.controllers.LastActive=owned;
        for(int i=1;i<=49;++i) {now=i*.1;active.Tick(true,true,1,Sample(.7),now);Rewired.ReInput.Pulse();}
        check(active.ReadbackSamples==5 && Rewired.Player.ActionReads==15 && active.Readback.MapEnabled==true && active.Readback.ControllerEnabled==true && active.Readback.LastActiveMatchesOwned==true,"Optional fixed public readbacks are bounded to one sample per second during the five-second window");
        check(active.Readback.PlayerAction31.HasValue && active.Readback.PlayerAction31.Value>.5 && active.Readback.OwnedAxis2==.7f && active.Readback.PlayerAction36==.7f && active.Readback.FailedFields==0,"Readback distinguishes populated owned axes and system action aggregation from mere setter returns");
        var terminal=active.Readback;int reads=Rewired.Player.ActionReads;now=5;active.Tick(true,true,1,Sample(),now);active.Tick(true,true,1,Sample(),6);
        check(active.Complete && Object.ReferenceEquals(terminal,active.Readback) && Rewired.Player.ActionReads==reads,"Completion retains the last fixed readback and performs no further reads");
        Rewired.ReInput.Reset();now=0;backend=Backend(()=>now);backend.Add();Rewired.Player.ThrowActionRead=true;var readback=backend.CaptureReadback();
        check(readback.FailedFields==224 && readback.PlayerAction31==null && readback.PlayerAction32==null && readback.PlayerAction36==null && readback.MapEnabled==true,"Failed fixed public action queries remain unavailable rather than becoming zero");
        Rewired.Player.ThrowActionRead=false;Rewired.Player.NonfiniteActionRead=true;readback=backend.CaptureReadback();
        check(readback.FailedFields==224 && readback.NonfiniteFields==224 && readback.PlayerAction31==null,"Nonfinite action readbacks retain distinct failure bits and never become valid zero values");
        Rewired.Player.NonfiniteActionRead=false;Rewired.ReInput.controllers.LastActive=new Rewired.Controller(72){tag="private-foreign-identity"};readback=backend.CaptureReadback();
        check(readback.LastActiveMatchesOwned==false && !readback.ToLog().Contains("private-foreign") && !readback.ToLog().Contains("fixture-owned-source"),"Public activity readback compares references and never logs controller tags or source identity");backend.Remove();Rewired.ReInput.Reset();
    }
}
