using System;
using System.IO;
using Ugevr;

static class EngineInputExperimentChecks
{
    sealed class Keyboard : IEngineEKeyboard
    {
        public string UnavailableReason {get;set;}
        public EngineESnapshot State=new EngineESnapshot {Added=true,Enabled=true,OriginalIsCurrent=true,ControlCount=115};
        public int Reads,DownQueues,ReleaseQueues,LastPreserved;
        public bool QueueFailure,ReleaseFailure,DownThrowAfterQueue;
        public EngineESnapshot Read() {++Reads;return State;}
        public int Queue(bool down)
        {
            if(QueueFailure || (!down && ReleaseFailure)) throw new InvalidOperationException("controlled queue failure");
            if(down) {++DownQueues;if(DownThrowAfterQueue) throw new InvalidOperationException("controlled uncertain down queue");}
            else {++ReleaseQueues;LastPreserved=State.OtherHeldCount;}
            return down?0:LastPreserved;
        }
    }
    public static void Run(Action<bool,string> assert)
    {
        string root=Path.Combine(Path.GetTempPath(),"ugevr-engine-experiment-"+Guid.NewGuid().ToString("N"));
        DateTime utc=new DateTime(2026,1,1,0,0,0,DateTimeKind.Utc);string run=Guid.NewGuid().ToString("D");int cases=0;
        string Folder() {string value=Path.Combine(root,"case-"+(++cases));Directory.CreateDirectory(Path.Combine(value,"UnityVR-evidence"));return value;}
        string Request(string folder) {return Path.Combine(folder,"UnityVR-evidence","engine-input-experiment-request");}
        string Result(string folder,int number=1) {return File.ReadAllText(Path.Combine(folder,"UnityVR-evidence","engine-input-experiment-result-"+number+".yaml"));}
        void Ask(string folder) {File.WriteAllText(Request(folder),"key=E\nduration_ms=500\n");}
        try
        {
            string folder=Folder();var keyboard=new Keyboard();int factories=0;
            var experiment=new EngineEInputExperiment(folder,run,17,1000,delegate {++factories;return keyboard;});
            Ask(folder);experiment.Tick(false,true,utc,0);
            assert(factories==0 && File.Exists(Request(folder)) && keyboard.DownQueues==0,"Engine input experiment defaults off and leaves its sentinel untouched");
            experiment.Tick(true,true,utc,0);
            assert(keyboard.DownQueues==1 && keyboard.State.Down==false && experiment.Active,"Experiment queues down without forcing an input update or claiming immediate application");
            keyboard.State.Down=true;keyboard.State.Press=true;experiment.Tick(true,true,utc.AddMilliseconds(100),100);
            keyboard.State.Press=false;keyboard.State.OtherHeldCount=2;experiment.Tick(true,true,utc.AddMilliseconds(499),499);
            assert(keyboard.ReleaseQueues==0,"Release is not queued before the requested monotonic deadline");
            experiment.Tick(true,true,utc.AddMilliseconds(500),500);
            assert(keyboard.ReleaseQueues==1 && keyboard.LastPreserved==2,"Release preserves the current non-E state snapshot instead of the baseline");
            keyboard.State.Down=false;keyboard.State.Release=true;experiment.Tick(true,true,utc.AddMilliseconds(600),600);
            string receipt=Result(folder);
            assert(!experiment.Active && receipt.Contains("down_queued: true") && receipt.Contains("press_observed: true") && receipt.Contains("release_edge_observed: true") && receipt.Contains("cleanup_observed: true"),"Receipt separates queue completion from observed E edge and release state");
            assert(receipt.Contains("engine_synthetic: true") && receipt.Contains("physical_forwarding: false") && receipt.Contains("gate_eligible: false") && receipt.Contains("game_wake: 'unknown'"),"Synthetic engine receipt cannot receive physical forwarding or gameplay gate credit");
            keyboard.State.Down=false;keyboard.State.Release=false;keyboard.State.OtherHeldCount=0;
            Ask(folder);experiment.Tick(true,true,utc.AddSeconds(1),1000);experiment.Cancel(utc.AddMilliseconds(1100),1100,"game_lifecycle_end");
            assert(keyboard.ReleaseQueues==2 && Result(folder,2).Contains("status: 'cancelled'") && Result(folder,2).Contains("cleanup_observed: false"),"Teardown queues best-effort release without claiming an unobserved input update");
            Ask(folder);int before=keyboard.DownQueues;experiment.Tick(true,true,utc.AddSeconds(2),2000);
            var restarted=new EngineEInputExperiment(folder,run,17,1000,delegate {return keyboard;});restarted.Tick(true,true,utc.AddSeconds(3),3000);
            assert(File.Exists(Request(folder)) && before==keyboard.DownQueues && restarted.RequestsConsumed==2,"Two-claim limit survives component restart and does not queue a third experiment");
            foreach(string scenario in new[]{"e-held","other-held","ime-active","disabled","removed","not-current","too-many-controls","unfocused","unsupported","bad-schema"})
            {
                folder=Folder();keyboard=new Keyboard();
                if(scenario=="e-held") keyboard.State.Down=true;
                if(scenario=="other-held" || scenario=="ime-active") keyboard.State.OtherHeldCount=1;
                if(scenario=="disabled") keyboard.State.Enabled=false;
                if(scenario=="removed") keyboard.State.Added=false;
                if(scenario=="not-current") keyboard.State.OriginalIsCurrent=false;
                if(scenario=="too-many-controls") keyboard.State.ControlCount=257;
                if(scenario=="unsupported") keyboard.UnavailableReason="assembly_identity_mismatch";
                Ask(folder);if(scenario=="bad-schema") File.WriteAllText(Request(folder),"key=Space\nduration_ms=10000");
                experiment=new EngineEInputExperiment(folder,run,17,1000,delegate {return keyboard;});experiment.Tick(true,scenario!="unfocused",utc,0);
                assert(keyboard.DownQueues==0 && keyboard.ReleaseQueues==0 && !experiment.Active,"Unsupported or nonneutral baseline refuses mutation: "+scenario);
            }
            folder=Folder();keyboard=new Keyboard();Ask(folder);experiment=new EngineEInputExperiment(folder,run,17,1000,delegate {return keyboard;});experiment.Tick(true,true,utc,0);
            keyboard.State.Down=true;keyboard.State.OtherHeldCount=1;experiment.Tick(true,false,utc.AddMilliseconds(100),100);
            assert(keyboard.ReleaseQueues==1 && keyboard.LastPreserved==1,"Focus loss still attempts release on the original device and preserves current other keys");
            keyboard.State.Down=false;keyboard.State.Release=true;experiment.Tick(true,false,utc.AddMilliseconds(200),200);
            assert(Result(folder).Contains("reason: 'focus_lost'"),"Early cancellation reason is distinct from the planned hold deadline");
            folder=Folder();keyboard=new Keyboard();Ask(folder);experiment=new EngineEInputExperiment(folder,run,17,1000,delegate {return keyboard;});experiment.Tick(true,true,utc,0);
            keyboard.State.Down=true;keyboard.State.OriginalIsCurrent=false;keyboard.State.OtherHeldCount=1;experiment.Tick(true,true,utc.AddMilliseconds(100),100);experiment.Cancel(utc.AddMilliseconds(200),200,"game_lifecycle_end");
            assert(keyboard.ReleaseQueues==1 && keyboard.LastPreserved==1 && Result(folder).Contains("cleanup_observed: false"),"A changed current keyboard never redirects release away from the captured original device");
            folder=Folder();keyboard=new Keyboard();Ask(folder);experiment=new EngineEInputExperiment(folder,run,17,1000,delegate {return keyboard;});experiment.Tick(true,true,utc,0);
            keyboard.State.Down=true;experiment.Tick(true,true,utc.AddSeconds(1),1000);keyboard.State.Down=false;keyboard.State.Release=true;experiment.Tick(true,true,utc.AddMilliseconds(1100),1100);
            assert(Result(folder).Contains("hold_overshoot_ms: 500") && Result(folder).Contains("release_queued_ticks: 1000"),"Stalled Update reports actual hold overshoot instead of promising real-time 500ms");
            folder=Folder();keyboard=new Keyboard();Ask(folder);experiment=new EngineEInputExperiment(folder,run,17,1000,delegate {return keyboard;});experiment.Tick(true,true,utc,0);
            experiment.Tick(true,true,utc.AddMilliseconds(500),500);experiment.Tick(true,true,utc.AddMilliseconds(2500),2500);
            assert(Result(folder).Contains("status: 'release_unobserved'") && Result(folder).Contains("cleanup_observed: false"),"Unprocessed normal input queues cannot fabricate a release acknowledgment from a neutral baseline");
            folder=Folder();keyboard=new Keyboard {ReleaseFailure=true};Ask(folder);experiment=new EngineEInputExperiment(folder,run,17,1000,delegate {return keyboard;});experiment.Tick(true,true,utc,0);
            keyboard.State.Down=true;experiment.Tick(true,true,utc.AddMilliseconds(500),500);experiment.Tick(true,true,utc.AddMilliseconds(2500),2500);
            assert(Result(folder).Contains("release_attempts: 2") && Result(folder).Contains("release_queued: false") && keyboard.ReleaseQueues==0,"Unavailable release has bounded attempts and remains explicitly unconfirmed");
            folder=Folder();keyboard=new Keyboard {QueueFailure=true};Ask(folder);experiment=new EngineEInputExperiment(folder,run,17,1000,delegate {return keyboard;});experiment.Tick(true,true,utc,0);
            assert(Result(folder).Contains("status: 'error'") && Result(folder).Contains("down_attempted: true") && Result(folder).Contains("down_queue_outcome: 'unknown'") && Result(folder).Contains("down_queued: false") && Result(folder).Contains("cleanup_observed: false") && Result(folder).Contains("release_attempts: 1"),"An uncertain throwing down attempt still triggers bounded release and cannot claim applied input or successful cleanup");
            folder=Folder();keyboard=new Keyboard {DownThrowAfterQueue=true};Ask(folder);experiment=new EngineEInputExperiment(folder,run,17,1000,delegate {return keyboard;});experiment.Tick(true,true,utc,0);
            assert(keyboard.DownQueues==1 && keyboard.ReleaseQueues==1 && Result(folder).Contains("down_queue_outcome: 'unknown'") && Result(folder).Contains("release_queued: true") && Result(folder).Contains("cleanup_observed: false"),"A down call that enqueues then throws still queues release on the original backend without claiming observed cleanup");
            folder=Folder();keyboard=new Keyboard();Ask(folder);long clock=30;
            experiment=new EngineEInputExperiment(folder,run,17,1000,delegate {return keyboard;},delegate {return clock;});experiment.Tick(true,true,utc,0);
            keyboard.State.Down=true;experiment.Tick(true,true,utc.AddMilliseconds(500),500);
            assert(keyboard.ReleaseQueues==0,"Hold duration starts after queue returns, rather than at the earlier Update timestamp");
            clock=570;experiment.Tick(true,true,utc.AddMilliseconds(530),530);keyboard.State.Down=false;keyboard.State.Release=true;experiment.Tick(true,true,utc.AddMilliseconds(600),600);
            assert(Result(folder).Contains("down_queued_ticks: 30") && Result(folder).Contains("release_queued_ticks: 570") && Result(folder).Contains("hold_elapsed_ms: 540") && Result(folder).Contains("hold_overshoot_ms: 40"),"Receipt records the clock at successful queue return and includes queue work in hold overshoot");
            assert(PublicEngineEKeyboard.Create().UnavailableReason=="assembly_not_loaded","Optional public engine adapter does not load an unavailable InputSystem assembly");
        }
        finally {Directory.Delete(Path.GetFullPath(root),true);}
    }
}
