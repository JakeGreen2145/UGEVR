using System;
using UnityEngine;

namespace Ugevr
{
    // Explicit experiment for the current-camera context lost after manual
    // renders. This does not establish a native UI or desktop fidelity fix.
    public sealed class CameraCurrentScope : IDisposable
    {
        readonly bool enabled;
        readonly Camera selected,prior;
        Camera ui;
        bool armed,selectedRendered,uiRendered,disposed;
        public string Outcome {get;private set;}
        public bool Conflict {get;private set;}
        public bool SetterAttempted {get;private set;}
        public bool Verified {get;private set;}
        public int GetterAttempts {get;private set;}

        public CameraCurrentScope(Camera selected,bool enabled)
        {
            this.selected=selected;this.enabled=enabled;Outcome="disabled";
            if(!enabled) return;
            Outcome="selected_unavailable";
            if(selected==null) return;
            try {++GetterAttempts;prior=Camera.current;}
            catch {Outcome="capture_getter_failed";Conflict=true;return;}
            if(prior==null) {Outcome="prior_null";return;}
            if(prior!=selected) {Outcome="prior_foreign";return;}
            armed=true;Outcome="prior_selected";
        }
        public void AllowUiCamera(Camera ownedUi)
        {
            if(!enabled || !armed) return;
            if(disposed || ui!=null || ownedUi==null || ownedUi==selected)
                throw new InvalidOperationException("Invalid current-camera UI ownership admission.");
            ui=ownedUi;
        }
        public void MarkRender(Camera renderer)
        {
            if(!enabled || !armed) return;
            if(disposed || renderer==null) throw new InvalidOperationException("Current-camera render scope unavailable.");
            if(renderer==selected) selectedRendered=true;
            else if(ui!=null && renderer==ui) uiRendered=true;
            else throw new InvalidOperationException("Current-camera renderer was not owned by this borrow.");
        }
        public void Dispose()
        {
            if(disposed) return;
            disposed=true;
            if(!enabled || !armed) return;
            if(prior==null || selected==null) {Outcome="prior_destroyed";Conflict=true;return;}
            Camera after;
            try {++GetterAttempts;after=Camera.current;}
            catch {Outcome="restore_getter_failed";Conflict=true;return;}
            if(after==prior) {Outcome="already_current";Verified=true;return;}
            bool owned=after==null || (selectedRendered && after==selected) || (uiRendered && ui!=null && after==ui);
            if(!owned) {Outcome="foreign_preserved";Conflict=true;return;}
            // Never set a null prior. The live selected camera is the sole
            // restoration destination, after its borrowed properties restore.
            SetterAttempted=true;
            try {Camera.SetupCurrent(prior);}
            catch {Outcome="setup_failed";Conflict=true;return;}
            try {++GetterAttempts;Verified=Camera.current==prior;}
            catch {Outcome="verify_getter_failed";Conflict=true;return;}
            Outcome=Verified?"restored":"verification_failed";Conflict=!Verified;
        }
        public string DiagnosticJson()
        {
            return "{\"experiment\":\"current-camera-ownership\",\"enabled\":"+Bool(enabled)
                +",\"outcome\":\""+Outcome+"\",\"priorWasSelected\":"+Bool(armed)
                +",\"selectedRenderAttempted\":"+Bool(selectedRendered)+",\"uiRenderAttempted\":"+Bool(uiRendered)
                +",\"getterAttempts\":"+GetterAttempts+",\"setterAttempted\":"+Bool(SetterAttempted)
                +",\"verified\":"+Bool(Verified)+",\"ownershipConflict\":"+Bool(Conflict)
                +",\"desktopFidelity\":\"unknown\"}";
        }
        static string Bool(bool value) {return value?"true":"false";}
    }
}
