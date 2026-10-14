using System;
using Ugevr;
using UnityEngine;

static class CameraCurrentChecks
{
    static void Reset(Camera current=null)
    {Camera.current=current;Camera.CurrentQueries=0;Camera.SetupCurrentCalls=0;Camera.CurrentGetterAction=null;Camera.SetupCurrentAction=null;}
    public static void Run(Action<bool,string> check)
    {
        try
        {
            var selected=new Camera();var ui=new Camera();var foreign=new Camera();
            Reset(selected);
            using(var off=new CameraCurrentScope(selected,false)) {off.AllowUiCamera(ui);off.MarkRender(foreign);Camera.current=null;}
            check(Camera.CurrentQueries==0 && Camera.SetupCurrentCalls==0,"Current-camera opt-out performs no getter or setter, including render marking");

            Reset();var nullPrior=new CameraCurrentScope(selected,true);Camera.current=ui;nullPrior.Dispose();
            check(Camera.SetupCurrentCalls==0 && nullPrior.Outcome=="prior_null" && Camera.current==ui,"Null prior does not call SetupCurrent or overwrite later context");
            Reset(foreign);var foreignPrior=new CameraCurrentScope(selected,true);Camera.current=null;foreignPrior.Dispose();
            check(Camera.SetupCurrentCalls==0 && foreignPrior.Outcome=="prior_foreign","Foreign prior never grants selected-camera ownership");

            Reset(selected);var restore=new CameraCurrentScope(selected,true);restore.MarkRender(selected);Camera.current=null;restore.Dispose();
            check(restore.Verified && !restore.Conflict && restore.Outcome=="restored" && Camera.current==selected,"Null after owned manual render restores exact selected prior");
            check(restore.GetterAttempts==3 && Camera.SetupCurrentCalls==1,"Successful restoration uses exactly capture/after/verification getters and one setter");
            restore.Dispose();check(Camera.SetupCurrentCalls==1,"Current-camera disposal is idempotent");

            Reset(selected);var noChange=new CameraCurrentScope(selected,true);noChange.MarkRender(selected);noChange.Dispose();
            check(noChange.Verified && noChange.Outcome=="already_current" && Camera.SetupCurrentCalls==0,"Already matching current requires no redundant setter");

            Reset(selected);var uiRestore=new CameraCurrentScope(selected,true);uiRestore.AllowUiCamera(ui);uiRestore.MarkRender(ui);Camera.current=ui;uiRestore.Dispose();
            check(uiRestore.Verified && Camera.current==selected,"Exact UI camera actually rendered by this borrow is a permitted post context");
            Reset(selected);var unrenderedUi=new CameraCurrentScope(selected,true);unrenderedUi.AllowUiCamera(ui);Camera.current=ui;unrenderedUi.Dispose();
            check(unrenderedUi.Conflict && unrenderedUi.Outcome=="foreign_preserved" && Camera.SetupCurrentCalls==0 && Camera.current==ui,"Admitted but unrendered UI camera grants no post-context ownership");
            Reset(selected);var foreignAfter=new CameraCurrentScope(selected,true);foreignAfter.MarkRender(selected);Camera.current=foreign;foreignAfter.Dispose();
            check(foreignAfter.Conflict && foreignAfter.Outcome=="foreign_preserved" && Camera.SetupCurrentCalls==0 && Camera.current==foreign,"Foreign current is preserved and explicitly conflicts");
            bool rejected=false;Reset(selected);using(var invalid=new CameraCurrentScope(selected,true))
            {try {invalid.MarkRender(foreign);} catch(InvalidOperationException) {rejected=true;}}
            check(rejected,"Unknown render camera cannot silently enter the owned set");

            Reset(selected);var destroyed=new CameraCurrentScope(selected,true);destroyed.MarkRender(selected);UnityEngine.Object.Destroy(selected);Camera.current=null;destroyed.Dispose();
            check(destroyed.Conflict && destroyed.Outcome=="prior_destroyed" && Camera.SetupCurrentCalls==0 && destroyed.GetterAttempts==1,"Destroyed prior is never passed to SetupCurrent or re-queried");
            selected=new Camera();

            Reset(selected);Camera.CurrentGetterAction=delegate {throw new InvalidOperationException("capture fault");};
            var captureFault=new CameraCurrentScope(selected,true);captureFault.Dispose();
            check(captureFault.Conflict && captureFault.Outcome=="capture_getter_failed" && Camera.SetupCurrentCalls==0,"Failed capture getter never arms restoration");
            Reset(selected);var afterFault=new CameraCurrentScope(selected,true);afterFault.MarkRender(selected);Camera.current=null;
            Camera.CurrentGetterAction=delegate {throw new InvalidOperationException("after fault");};afterFault.Dispose();
            check(afterFault.Conflict && afterFault.Outcome=="restore_getter_failed" && Camera.SetupCurrentCalls==0,"Failed after getter never guesses current ownership");
            Reset(selected);var setterFault=new CameraCurrentScope(selected,true);setterFault.MarkRender(selected);Camera.current=null;
            Camera.SetupCurrentAction=delegate {throw new InvalidOperationException("setter fault");};setterFault.Dispose();
            check(setterFault.Conflict && setterFault.Outcome=="setup_failed" && !setterFault.Verified && Camera.SetupCurrentCalls==1,"Setter exception remains unverified with no retry");
            Reset(selected);var verifyFault=new CameraCurrentScope(selected,true);verifyFault.MarkRender(selected);Camera.current=null;
            Camera.CurrentGetterAction=delegate {if(Camera.CurrentQueries==3) throw new InvalidOperationException("verify fault");};verifyFault.Dispose();
            check(verifyFault.Conflict && verifyFault.Outcome=="verify_getter_failed" && Camera.SetupCurrentCalls==1,"Verification getter failure is separate from issued setter");
            Reset(selected);var wrongSetter=new CameraCurrentScope(selected,true);wrongSetter.MarkRender(selected);Camera.current=null;
            Camera.SetupCurrentAction=delegate {Camera.current=foreign;};wrongSetter.Dispose();
            check(wrongSetter.Conflict && wrongSetter.Outcome=="verification_failed" && Camera.current==foreign && Camera.SetupCurrentCalls==1,"Setter returning a foreign context fails verification and never overwrites again");

            Reset(selected);var target=new RenderTexture();selected.targetTexture=target;
            var faultScope=new CameraCurrentScope(selected,true);bool renderFault=false,propertiesRestored=false;
            Camera.SetupCurrentAction=delegate(Camera value) {propertiesRestored=selected.targetTexture==target;Camera.current=value;};
            selected.RenderAction=delegate {Camera.current=null;throw new InvalidOperationException("render fault");};
            try {using(var state=new CameraState(selected)) {selected.targetTexture=new RenderTexture();faultScope.MarkRender(selected);selected.Render();}}
            catch(InvalidOperationException) {renderFault=true;}
            finally {faultScope.Dispose();}
            check(renderFault && propertiesRestored && faultScope.Verified,"Render exception survives while camera properties restore before current-context setter");
            check(faultScope.DiagnosticJson().Contains("\"desktopFidelity\":\"unknown\""),"Verified current restoration makes no desktop fidelity claim");
            selected.RenderAction=null;

            Reset(selected);var outer=new CameraCurrentScope(selected,true);outer.AllowUiCamera(ui);outer.MarkRender(ui);Camera.current=ui;
            var inner=new CameraCurrentScope(ui,true);inner.MarkRender(ui);Camera.current=null;inner.Dispose();outer.Dispose();
            check(inner.Verified && outer.Verified && Camera.current==selected && Camera.SetupCurrentCalls==2,"Nested owned UI scopes restore their own exact priors in order");
            Reset(selected);outer=new CameraCurrentScope(selected,true);outer.MarkRender(selected);Camera.current=foreign;
            inner=new CameraCurrentScope(foreign,true);inner.MarkRender(foreign);Camera.current=null;inner.Dispose();outer.Dispose();
            check(inner.Verified && outer.Conflict && Camera.current==foreign && Camera.SetupCurrentCalls==1,"Nested foreign scope restoration is preserved by outer ownership policy");

            Reset(selected);var scope=new CameraCurrentScope(selected,true);scope.MarkRender(selected);Camera.current=null;scope.Dispose();
            var native=new NativeUiStateDiagnostics(selected);native.Capture(5);
            check(native.DiagnosticJson().Contains("\"currentCameraId\":"+selected.GetInstanceID()),"After-restoration native context snapshot observes restored selected current");
        }
        finally {Reset();}
    }
}
