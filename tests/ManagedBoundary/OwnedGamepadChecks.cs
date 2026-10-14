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
        Hub.Reset();var westPolicy=new OpenVrGamepadDiagnostic(delegate {return Backend(OuterGamepadCompatibilityProfile.ButtonFor(OuterGamepadCompatibilityProfile.WakeName));},5,0.2,"refuse");var aSample=Neutral();
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

        Hub.Reset();int factories=0;var p=new OpenVrGamepadDiagnostic(delegate {++factories;return Backend();},5,0.2,"refuse");var input=Neutral();
        p.Tick(false,true,1,input,0);p.Tick(true,false,1,input,1);input.A=true;p.Tick(true,true,1,input,2);
        check(factories==0 && Hub.Adds==0,"Default-off, focus and neutral-controller activation gates precede all device creation");
        Hub.Reset();var diagonal=new OpenVrGamepadDiagnostic(Backend,5,0.2,"refuse");var diagonalInput=Neutral();diagonalInput.X=diagonalInput.Y=0.19;
        diagonal.Tick(true,true,1,diagonalInput,0);
        check(Hub.Adds==0 && Hub.Registers==0 && diagonal.Reason=="right_controller_not_neutral","A diagonal outside the configured radial deadzone cannot arm even when each component is below 0.2");
        diagonalInput.X=diagonalInput.Y=0.14;diagonal.Tick(true,true,1,diagonalInput,0.1);diagonal.Tick(true,true,1,diagonalInput,0.2);
        check(Hub.Adds==1 && Hub.Queues==0,"An accepted diagonal inside the same radial mapping stays output-neutral after activation");diagonal.Cancel("test_end",0.3);
        Hub.Reset();var narrow=new OpenVrGamepadDiagnostic(Backend,5,0.1,"refuse");var narrowInput=Neutral();narrowInput.X=0.15;
        narrow.Tick(true,true,1,narrowInput,0);
        check(Hub.Adds==0 && Hub.Registers==0,"A nondefault narrower deadzone rejects an activation sample that would produce movement");
        narrowInput.X=0.1;narrow.Tick(true,true,1,narrowInput,0.1);narrow.Tick(true,true,1,narrowInput,0.2);
        check(Hub.Adds==1 && Hub.Queues==0,"The configured radial boundary is accepted only when the output mapping also produces zero");narrow.Cancel("test_end",0.3);
        Hub.Reset();p=new OpenVrGamepadDiagnostic(delegate {++factories;return Backend();},5,0.2,"refuse");
        input=Neutral();p.Tick(true,true,1,input,3);input.A=true;input.X=1;p.Tick(true,true,1,input,3.1);
        check(Hub.Pending.Count==1 && p.Queues==1,"Explicit test profile maps one received A edge and axis sample to one owned state queue");
        Hub.NormalUpdate();p.Tick(true,true,1,input,3.2);
        check(p.AppliedSamples==1 && p.Queues==1,"Unchanged held input is observed without queue duplication or extra input updates");
        p.Tick(false,true,1,input,3.3);
        check(p.ReleaseAttempts==1 && !p.Complete,"Session disable queues neutral state without claiming completion");Hub.NormalUpdate();p.Tick(false,true,1,input,3.4);
        check(p.Complete && p.ReleaseObserved && p.RemoveAttempts==1 && Hub.Devices.Count==1,"Subsequent normal neutral state permits owned cleanup");
        Hub.Reset();p=new OpenVrGamepadDiagnostic(Backend,5,0.2,"refuse");input=Neutral();p.Tick(true,true,1,input,0);input.A=true;p.Tick(true,true,1,input,0.1);Hub.NormalUpdate();
        external=new Pad {added=true,enabled=true,layout="physical-new"};Hub.Devices.Add(external);external.MakeCurrent();p.Tick(true,false,1,input,0.2);Hub.NormalUpdate();
        check(p.Complete && Object.ReferenceEquals(Pad.current,external) && external.added,"Focus-loss release preserves an already observed external current and cannot let its neutral event reclaim current");
        Hub.Reset();p=new OpenVrGamepadDiagnostic(Backend,5,0.2,"refuse");input=Neutral();input.Packet=10;p.Tick(true,true,1,input,0);input.A=true;input.Packet=9;p.Tick(true,true,1,input,0.1);Hub.NormalUpdate();p.Tick(true,true,1,input,0.2);
        check(p.Complete && p.Reason=="controller_packet_regressed" && p.Queues==0,"Observed packet regression releases context without replaying the source state");
        Hub.Reset();p=new OpenVrGamepadDiagnostic(Backend,5,0.2,"refuse");input=Neutral();input.Packet=UInt32.MaxValue;p.Tick(true,true,1,input,0);input.A=true;input.Packet=0;p.Tick(true,true,1,input,0.1);
        check(!p.Complete && p.Queues==1,"Documented unsigned packet wrap does not become a false controller reset");p.Cancel("test_end",0.2);
        foreach(string reason in new[]{"focus","generation","identity","disconnect","deadline","clock"})
        {
            Hub.Reset();p=new OpenVrGamepadDiagnostic(Backend,5,0.2,"refuse");input=Neutral();p.Tick(true,true,1,input,0);input.A=true;p.Tick(true,true,1,input,0.1);Hub.NormalUpdate();
            ulong gen=1;bool focus=true;double now=0.2;
            if(reason=="focus") focus=false;if(reason=="generation") gen=2;if(reason=="identity") input.Identity="different";if(reason=="disconnect") input.Available=false;if(reason=="deadline") now=5;if(reason=="clock") now=-1;
            p.Tick(true,focus,gen,input,now);Hub.NormalUpdate();p.Tick(true,focus,gen,input,6);
            check(p.Complete && p.ReleaseAttempts>=1 && Hub.Devices.Count==1,"Lifecycle change releases and removes only the owned device: "+reason);
        }
        Hub.Reset();p=new OpenVrGamepadDiagnostic(Backend,5,0.2,"refuse");input=Neutral();p.Tick(true,true,1,input,0);input.A=true;Hub.QueueThenThrow=true;p.Tick(true,true,1,input,0.1);
        check(p.Complete && p.ReleaseAttempts==1 && Hub.Queues==2 && Hub.Devices.Count==1,"Uncertain throwing down queue still attempts release and removes the captured device");
        Hub.Reset();p=new OpenVrGamepadDiagnostic(Backend,5,0.2,"refuse");p.Tick(true,true,1,Neutral(),0);Hub.RemoveAlwaysThrow=true;p.Cancel("teardown",1);
        check(p.Status=="cleanup_unconfirmed" && p.RemoveAttempts==2 && Hub.Removes==2,"Teardown failures remain unconfirmed after bounded removal attempts");
        Hub.Reset();p=new OpenVrGamepadDiagnostic(Backend,5,0.2,"refuse");input=Neutral();p.Tick(true,true,1,input,0);input.X=Double.NaN;p.Tick(true,true,1,input,0.1);Hub.NormalUpdate();p.Tick(true,true,1,input,0.2);
        check(p.Complete && p.Queues==0,"Nonfinite controller state never reaches the public queue");
    }
}
