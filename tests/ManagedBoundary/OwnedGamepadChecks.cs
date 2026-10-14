using System;
using System.Reflection;
using Ugevr;
using Pad=UnityEngine.InputSystem.Gamepad;
using Hub=UnityEngine.InputSystem.InputSystem;

static class OwnedGamepadChecks
{
    static PublicOwnedGamepad Backend() {return new PublicOwnedGamepad(Assembly.GetExecutingAssembly(),false,OuterGamepadCompatibilityProfile.LayoutPrefix,OwnedGamepadButton.South);}
    static PublicOwnedGamepad Backend(OwnedGamepadButton target) {return new PublicOwnedGamepad(Assembly.GetExecutingAssembly(),false,OuterGamepadCompatibilityProfile.LayoutPrefix,target);}
    static LegacyRightSample Neutral() {return new LegacyRightSample {Available=true,Identity="verified-controller",Index=3};}
    public static void Run(Action<bool,string> check)
    {
        check(OuterGamepadCompatibilityProfile.Create(OuterGamepadCompatibilityProfile.Name).UnavailableReason=="game_compatibility_not_loaded","Explicit Outer compatibility never loads or guesses a missing game assembly");
        check(OuterGamepadCompatibilityProfile.Supports(OuterGamepadCompatibilityProfile.WakeName) && OuterGamepadCompatibilityProfile.ButtonFor(OuterGamepadCompatibilityProfile.WakeName)==OwnedGamepadButton.West && OuterGamepadCompatibilityProfile.ButtonFor(OuterGamepadCompatibilityProfile.Name)==OwnedGamepadButton.South,"Explicit profiles select distinct West wake/interact and South menu/jump outputs");
        check(OuterGamepadCompatibilityProfile.Supports(OuterGamepadCompatibilityProfile.ButtonOnlyWakeName) && OuterGamepadCompatibilityProfile.ButtonFor(OuterGamepadCompatibilityProfile.ButtonOnlyWakeName)==OwnedGamepadButton.West,"Restricted button-only profile explicitly selects West separately from joystick and South profiles");
        check(OuterGamepadCompatibilityProfile.Supports(OuterGamepadCompatibilityProfile.ObservedAxis0Name) && OuterGamepadCompatibilityProfile.ButtonFor(OuterGamepadCompatibilityProfile.ObservedAxis0Name)==OwnedGamepadButton.West,"Observed transport axes require their own explicit West profile, separate from button-only and declared joystick profiles");
        check(!OuterGamepadCompatibilityProfile.Supports("none") && OuterGamepadCompatibilityProfile.Create("none").UnavailableReason=="game_profile_unavailable","Unknown profiles refuse before looking for game assemblies or mutating InputSystem");
        Hub.Reset();bool rejected=false;try {Backend((OwnedGamepadButton)42);} catch(ArgumentException) {rejected=true;}
        check(rejected && Hub.Registers==0 && Hub.Adds==0,"Unsupported output buttons refuse before layout or device creation");
        Hub.Reset();var west=Backend(OuterGamepadCompatibilityProfile.ButtonFor(OuterGamepadCompatibilityProfile.WakeName));west.Add(false);var westOwned=Pad.current;
        west.Queue(true,0.25,-0.5);
        check(Hub.Pending[0].Item2.buttons==(1u<<11) && !west.Read().ButtonDown,"West backend queues the public enum bit from the actual fixture API, not South or a hardcoded SDK bit");
        Hub.NormalUpdate();
        check(west.Read().ButtonDown && westOwned.buttonWest.isPressed && !westOwned.buttonSouth.isPressed && west.Read().X==0.25,"West readback observes the selected public control after normal update without mislabeling South");
        west.Queue(false,0,0);Hub.NormalUpdate();
        check(west.Read().Neutral && !westOwned.buttonWest.isPressed && !westOwned.buttonSouth.isPressed,"Neutral release clears the selected West state through normal update");west.Remove();
        Hub.Reset();var westPolicy=new OpenVrGamepadDiagnostic(delegate {return Backend(OuterGamepadCompatibilityProfile.ButtonFor(OuterGamepadCompatibilityProfile.WakeName));},5,0.2,"refuse",1);var aSample=Neutral();
        westPolicy.Tick(true,true,1,aSample,0);aSample.A=true;aSample.X=1;westPolicy.Tick(true,true,1,aSample,0.1);Hub.NormalUpdate();westPolicy.Tick(true,true,1,aSample,0.2);
        check(Pad.current.buttonWest.isPressed && !Pad.current.buttonSouth.isPressed && westPolicy.AppliedSamples==1 && westPolicy.Queues==1,"The explicit wake profile forwards right A and movement once and acknowledges West through the real backend");
        westPolicy.Tick(true,false,1,aSample,0.3);Hub.NormalUpdate();westPolicy.Tick(true,false,1,aSample,0.4);
        check(westPolicy.Complete && westPolicy.ReleaseObserved && Hub.Removes==1 && Hub.LayoutRemoves==1 && Hub.Keyboard.added && Hub.Keyboard.enabled,"West focus-loss releases and removes only the owned device and unchanged layout");
        Hub.Reset();var b=Backend();var keyboard=Hub.Keyboard;b.Add(false);var owned=Pad.current;
        check(owned!=null && owned.layout.StartsWith("XInputControllerWindowsUGEVR_") && owned.description.manufacturer=="UGEVR" && owned.description.capabilities=="{}" && owned.description.serial.StartsWith("ugevr-synthetic-"),"Compatibility device declares its own synthetic identity without physical vendor IDs");
        b.Queue(true,0.25,-0.5);
        check(!b.Read().ButtonDown && Hub.Pending.Count==1,"Public backend queues GPAD without synchronous engine updates");
        Hub.NormalUpdate();var read=b.Read();
        check(read.ButtonDown && read.X==0.25 && read.Y==-0.5 && Object.ReferenceEquals(Hub.Keyboard,keyboard) && keyboard.added && keyboard.enabled,"Normal input update applies only the owned pad and preserves the physical keyboard");
        b.Queue(false,0,0);Hub.NormalUpdate();b.Remove();
        check(!b.NeedsCleanup && !owned.added && Hub.Removes==1 && Hub.LayoutRemoves==1 && Hub.DeviceObservers==0 && Hub.LayoutObservers==0 && keyboard.added,"Removal deregisters only the owned layout/device and releases observers");

        Hub.Reset();var prior=new Pad {added=true,enabled=true,layout="physical"};Hub.Devices.Add(prior);prior.MakeCurrent();b=Backend();
        try {b.Add(false);} catch { }
        check(Hub.Registers==0 && Hub.Adds==0 && Object.ReferenceEquals(Pad.current,prior),"Default current-device conflict policy refuses before registry or device mutation");
        b=Backend();b.Add(true);b.Remove();
        check(Object.ReferenceEquals(Pad.current,prior) && prior.added,"Explicit prefer-owned restores the still-added prior current through public MakeCurrent");

        Hub.Reset();prior=new Pad {added=true,enabled=true,layout="prior"};Hub.Devices.Add(prior);prior.MakeCurrent();b=Backend();b.Add(true);
        var external=new Pad {added=true,enabled=true,layout="external"};Hub.Devices.Add(external);external.MakeCurrent();b.Read();b.Remove();
        check(Object.ReferenceEquals(Pad.current,external) && prior.added && external.added,"Observed external current takeover prevents restoring an older current device");
        Hub.Reset();prior=new Pad {added=true,enabled=true,layout="prior"};Hub.Devices.Add(prior);prior.MakeCurrent();b=Backend();b.Add(true);Hub.CurrentOnRemoval=external;b.Remove();
        check(Object.ReferenceEquals(Pad.current,external),"Reentrant external current selection during removal is preserved");

        Hub.Reset();Hub.Collision=true;b=Backend();try {b.Add(false);} catch { }
        check(Hub.Registers==0 && Hub.Adds==0 && !b.NeedsCleanup,"Unowned layout collision is never replaced");
        Hub.Reset();Hub.AddThenThrow=true;b=Backend();try {b.Add(false);} catch { }
        check(b.HasOwnedDevice && b.AddFailures==1,"Synchronous public device callback captures exact identity before add-then-throw");b.Remove();
        check(Hub.Removes==1 && Hub.LayoutRemoves==1,"Add-then-throw cleanup removes only the captured reference");
        Hub.Reset();Hub.RegisterThenThrow=true;b=Backend();try {b.Add(false);} catch { }b.Remove();
        check(Hub.Adds==0 && Hub.LayoutRemoves==1 && !b.NeedsCleanup,"Registration-then-throw cleanup owns the exact unique unchanged layout without creating a device");
        Hub.Reset();Hub.DisableAdded=true;b=Backend();try {b.Add(false);} catch { }
        check(b.HasOwnedDevice && !b.Read().Enabled && Hub.Adds==1,"Game-disabled owned device is reported without re-enabling it");b.Remove();
        Hub.Reset();b=Backend();b.Add(false);owned=Pad.current;Hub.Replace(owned.layout);try {b.Queue(true,0,0);} catch { }
        check(Hub.Queues==0,"Externally changed layout refuses further queued input");b.Remove();
        check(Hub.LayoutRemoves==0 && Hub.Layouts.Count==1,"Changed layout definition is preserved during owned device removal");
        Hub.Reset();b=Backend();b.Add(false);owned=Pad.current;Hub.Devices.Add(new Pad {added=true,enabled=true,layout=owned.layout});b.Remove();
        check(Hub.LayoutRemoves==0 && Hub.Devices.Count==2,"Another device using the registered layout prevents deregistration");
        Hub.Reset();Hub.RemoveThenThrow=true;b=Backend();b.Add(false);try {b.Remove();} catch { }Hub.RemoveThenThrow=false;b.Remove();
        check(Hub.Removes==1 && !b.NeedsCleanup && Hub.LayoutRemoves==1,"Removal retry confirms original identity already absent without repeated device mutation");

        foreach(string pauseReason in new[]{"session_disabled","vr_failure"})
        {
            Hub.Reset();int waitingFactories=0;
            var waiting=new OpenVrGamepadDiagnostic(delegate {++waitingFactories;return Backend();},5,0.2,"refuse",1);
            waiting.Tick(false,true,1,Neutral(),0);waiting.Pause(pauseReason,0.1);waiting.Tick(false,true,1,Neutral(),0.2);
            check(!waiting.Complete && waiting.Status=="waiting_neutral" && waiting.Reason==pauseReason && waitingFactories==0 && Hub.Adds==0 && Hub.Registers==0,"A reversible pause before activation preserves the first window without any owned resources: "+pauseReason);
            var pendingInput=Neutral();pendingInput.A=true;waiting.Tick(true,true,2,pendingInput,0.3);
            check(waitingFactories==0 && waiting.Reason=="right_controller_not_neutral","Resume still waits for deliberate neutral input before spending its authorized first window: "+pauseReason);
            pendingInput.A=false;waiting.Tick(true,true,2,pendingInput,0.4);
            check(waiting.Status=="active" && waitingFactories==1 && Hub.Adds==1,"A resumed never-started diagnostic starts exactly its first window: "+pauseReason);
            pendingInput.A=true;waiting.Tick(true,true,2,pendingInput,0.5);Hub.NormalUpdate();waiting.Pause(pauseReason,0.6);Hub.NormalUpdate();waiting.Tick(true,true,3,Neutral(),0.7);
            check(waiting.Complete && waiting.ReleaseAttempts==1 && Hub.Removes==1 && waitingFactories==1 && Hub.Adds==1,"Pause after a started window releases once and cannot create a second window: "+pauseReason);
        }
        foreach(string lifecycle in new[]{"component_disabled","game_lifecycle_end"})
        {
            Hub.Reset();var terminal=new OpenVrGamepadDiagnostic(Backend,5,0.2,"refuse",1);terminal.Cancel(lifecycle,0);terminal.Tick(true,true,1,Neutral(),1);
            check(terminal.Complete && Hub.Adds==0 && Hub.Registers==0,"Terminal lifecycle cancellation remains final even when the first window never started: "+lifecycle);
        }
        Hub.Reset();var stable=new OpenVrGamepadDiagnostic(Backend,5,0.2,"refuse");var stableInput=Neutral();
        stable.Tick(true,true,1,stableInput,0);stable.Tick(true,true,1,stableInput,0.1);
        check(stable.RequiredNeutralReads==3 && stable.NeutralReads==2 && Hub.Registers==0 && Hub.Adds==0,"Production default admission waits for three successive valid neutral samples before any registry/device mutation");
        stable.Tick(true,true,1,stableInput,0.2);
        check(stable.Status=="active" && stable.NeutralReads==3 && Hub.Adds==1 && Hub.Registers==1,"Third stable valid neutral read starts exactly the first authorized window");stable.Cancel("test_end",0.3);
        foreach(string reset in new[]{"fault","focus","disabled","pause","held","axis","index","identity","generation","metadata","axis_type","axis_source","role_fault"})
        {
            Hub.Reset();stable=new OpenVrGamepadDiagnostic(Backend,5,0.2,"refuse");stableInput=Neutral();stable.Tick(true,true,1,stableInput,0);stable.Tick(true,true,1,stableInput,0.1);
            bool available=true,focus=true;ulong gen=1;
            if(reset=="fault" || reset=="role_fault") {stableInput.Available=false;stableInput.UnavailableReason=reset=="fault"?"controller_state_read_failed":"right_role_changed";}
            if(reset=="focus") focus=false;if(reset=="disabled") available=false;if(reset=="pause") {stable.Pause("session_disabled",0.15);available=false;}
            if(reset=="held") stableInput.A=true;if(reset=="axis") stableInput.X=0.3;
            if(reset=="index") ++stableInput.Index;if(reset=="identity") stableInput.Identity="replacement";if(reset=="generation") ++gen;
            if(reset=="metadata") stableInput.Axis0Metadata="TrackedProp_UnknownProperty";if(reset=="axis_type") stableInput.Axis0Type=2;if(reset=="axis_source") {stableInput.AxesAvailable=true;stableInput.AxesSource="observed_transport_axis0";}
            stable.Tick(available,focus,gen,stableInput,0.2);
            check(Hub.Adds==0 && Hub.Registers==0 && !stable.Complete && stable.NeutralReads<=1,"Admission reset or new context cannot reuse earlier successful samples: "+reset);
            stableInput.Available=true;stableInput.UnavailableReason=null;stableInput.A=false;stableInput.X=0;
            stable.Tick(true,true,gen,stableInput,0.3);
            check(Hub.Adds==0 && stable.NeutralReads<=2,"Resume still requires all remaining consecutive neutral samples after reset: "+reset);
            stable.Tick(true,true,gen,stableInput,0.4);stable.Tick(true,true,gen,stableInput,0.5);
            check(Hub.Adds==1 && stable.Status=="active","Stable replacement context may start only the first window after fresh admission: "+reset);stable.Cancel("test_end",0.6);
        }
        foreach(int required in new[]{1,2,10})
        {
            Hub.Reset();stable=new OpenVrGamepadDiagnostic(Backend,5,0.2,"refuse",required);
            for(int i=0;i<required-1;++i) stable.Tick(true,true,1,Neutral(),i*0.01);
            check(Hub.Adds==0,"Configured admission performs no early device creation: "+required);
            stable.Tick(true,true,1,Neutral(),required*0.01);check(Hub.Adds==1 && stable.RequiredNeutralReads==required,"Configured bounded admission counts the selected number of reads: "+required);stable.Cancel("test_end",1);
        }
        foreach(int required in new[]{0,11})
        {
            Hub.Reset();bool bad=false;try {new OpenVrGamepadDiagnostic(Backend,5,0.2,"refuse",required);} catch(ArgumentOutOfRangeException) {bad=true;}
            check(bad && Hub.Adds==0 && Hub.Registers==0,"Out-of-range admission cannot mutate global input state: "+required);
        }
        Hub.Reset();stable=new OpenVrGamepadDiagnostic(Backend,5,0.2,"refuse");stableInput=Neutral();stableInput.Available=false;stableInput.UnavailableReason="reader_exception:System.NullReferenceException";stable.Tick(true,true,1,stableInput,0);
        stableInput=Neutral();stable.Tick(true,true,1,stableInput,0.1);
        check(stable.NativeUnavailableReason=="reader_exception:System.NullReferenceException" && Hub.Adds==0 && stable.NeutralReads==1,"Reader exception category survives subsequent valid bootstrap samples without weakening admission");
        stable.Tick(true,true,1,stableInput,0.2);stable.Tick(true,true,1,stableInput,0.3);stableInput.Available=false;stableInput.UnavailableReason="controller_pose_invalid";stable.Tick(true,true,1,stableInput,0.4);Hub.NormalUpdate();stable.Tick(true,true,1,Neutral(),0.5);
        check(stable.Complete && stable.Reason=="controller_unavailable" && stable.NativeUnavailableReason=="controller_pose_invalid" && stable.Queues==0 && Hub.Adds==1 && Hub.Removes==1,"The terminating source category stays visible after guarded release/cleanup and cannot start another window");
        Hub.Reset();int factories=0;var p=new OpenVrGamepadDiagnostic(delegate {++factories;return Backend();},5,0.2,"refuse",1);var input=Neutral();
        p.Tick(false,true,1,input,0);p.Tick(true,false,1,input,1);input.A=true;p.Tick(true,true,1,input,2);
        check(factories==0 && Hub.Adds==0,"Default-off, focus and neutral-controller activation gates precede all device creation");
        Hub.Reset();var diagonal=new OpenVrGamepadDiagnostic(Backend,5,0.2,"refuse",1);var diagonalInput=Neutral();diagonalInput.X=diagonalInput.Y=0.19;
        diagonal.Tick(true,true,1,diagonalInput,0);
        check(Hub.Adds==0 && Hub.Registers==0 && diagonal.Reason=="right_controller_not_neutral","A diagonal outside the configured radial deadzone cannot arm even when each component is below 0.2");
        diagonalInput.X=diagonalInput.Y=0.14;diagonal.Tick(true,true,1,diagonalInput,0.1);diagonal.Tick(true,true,1,diagonalInput,0.2);
        check(Hub.Adds==1 && Hub.Queues==0,"An accepted diagonal inside the same radial mapping stays output-neutral after activation");diagonal.Cancel("test_end",0.3);
        Hub.Reset();var narrow=new OpenVrGamepadDiagnostic(Backend,5,0.1,"refuse",1);var narrowInput=Neutral();narrowInput.X=0.15;
        narrow.Tick(true,true,1,narrowInput,0);
        check(Hub.Adds==0 && Hub.Registers==0,"A nondefault narrower deadzone rejects an activation sample that would produce movement");
        narrowInput.X=0.1;narrow.Tick(true,true,1,narrowInput,0.1);narrow.Tick(true,true,1,narrowInput,0.2);
        check(Hub.Adds==1 && Hub.Queues==0,"The configured radial boundary is accepted only when the output mapping also produces zero");narrow.Cancel("test_end",0.3);
        Hub.Reset();p=new OpenVrGamepadDiagnostic(delegate {++factories;return Backend();},5,0.2,"refuse",1);
        input=Neutral();p.Tick(true,true,1,input,3);input.A=true;input.X=1;p.Tick(true,true,1,input,3.1);
        check(Hub.Pending.Count==1 && p.Queues==1,"Explicit test profile maps one received A edge and axis sample to one owned state queue");
        Hub.NormalUpdate();p.Tick(true,true,1,input,3.2);
        check(p.AppliedSamples==1 && p.Queues==1,"Unchanged held input is observed without queue duplication or extra input updates");
        p.Tick(false,true,1,input,3.3);
        check(p.ReleaseAttempts==1 && !p.Complete,"Session disable queues neutral state without claiming completion");Hub.NormalUpdate();p.Tick(false,true,1,input,3.4);
        check(p.Complete && p.ReleaseObserved && p.RemoveAttempts==1 && Hub.Devices.Count==1,"Subsequent normal neutral state permits owned cleanup");
        Hub.Reset();p=new OpenVrGamepadDiagnostic(Backend,5,0.2,"refuse",1);input=Neutral();p.Tick(true,true,1,input,0);input.A=true;p.Tick(true,true,1,input,0.1);Hub.NormalUpdate();
        external=new Pad {added=true,enabled=true,layout="physical-new"};Hub.Devices.Add(external);external.MakeCurrent();p.Tick(true,false,1,input,0.2);Hub.NormalUpdate();
        check(p.Complete && Object.ReferenceEquals(Pad.current,external) && external.added,"Focus-loss release preserves an already observed external current and cannot let its neutral event reclaim current");
        Hub.Reset();p=new OpenVrGamepadDiagnostic(Backend,5,0.2,"refuse",1);input=Neutral();input.Packet=10;p.Tick(true,true,1,input,0);input.A=true;input.Packet=9;p.Tick(true,true,1,input,0.1);Hub.NormalUpdate();p.Tick(true,true,1,input,0.2);
        check(p.Complete && p.Reason=="controller_packet_regressed" && p.Queues==0,"Observed packet regression releases context without replaying the source state");
        Hub.Reset();p=new OpenVrGamepadDiagnostic(Backend,5,0.2,"refuse",1);input=Neutral();input.Packet=UInt32.MaxValue;p.Tick(true,true,1,input,0);input.A=true;input.Packet=0;p.Tick(true,true,1,input,0.1);
        check(!p.Complete && p.Queues==1,"Documented unsigned packet wrap does not become a false controller reset");p.Cancel("test_end",0.2);
        foreach(string reason in new[]{"focus","generation","identity","disconnect","deadline","clock"})
        {
            Hub.Reset();p=new OpenVrGamepadDiagnostic(Backend,5,0.2,"refuse",1);input=Neutral();p.Tick(true,true,1,input,0);input.A=true;p.Tick(true,true,1,input,0.1);Hub.NormalUpdate();
            ulong gen=1;bool focus=true;double now=0.2;
            if(reason=="focus") focus=false;if(reason=="generation") gen=2;if(reason=="identity") input.Identity="different";if(reason=="disconnect") input.Available=false;if(reason=="deadline") now=5;if(reason=="clock") now=-1;
            p.Tick(true,focus,gen,input,now);Hub.NormalUpdate();p.Tick(true,focus,gen,input,6);
            check(p.Complete && p.ReleaseAttempts>=1 && Hub.Devices.Count==1,"Lifecycle change releases and removes only the owned device: "+reason);
        }
        Hub.Reset();p=new OpenVrGamepadDiagnostic(Backend,5,0.2,"refuse",1);input=Neutral();p.Tick(true,true,1,input,0);input.A=true;Hub.QueueThenThrow=true;p.Tick(true,true,1,input,0.1);
        check(p.Complete && p.ReleaseAttempts==1 && Hub.Queues==2 && Hub.Devices.Count==1,"Uncertain throwing down queue still attempts release and removes the captured device");
        Hub.Reset();p=new OpenVrGamepadDiagnostic(Backend,5,0.2,"refuse",1);p.Tick(true,true,1,Neutral(),0);Hub.RemoveAlwaysThrow=true;p.Cancel("teardown",1);
        check(p.Status=="cleanup_unconfirmed" && p.RemoveAttempts==2 && Hub.Removes==2,"Teardown failures remain unconfirmed after bounded removal attempts");
        Hub.Reset();p=new OpenVrGamepadDiagnostic(Backend,5,0.2,"refuse",1);input=Neutral();p.Tick(true,true,1,input,0);input.X=Double.NaN;p.Tick(true,true,1,input,0.1);Hub.NormalUpdate();p.Tick(true,true,1,input,0.2);
        check(p.Complete && p.Queues==0,"Nonfinite controller state never reaches the public queue");
    }
}
