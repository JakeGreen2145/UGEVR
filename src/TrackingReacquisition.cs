using System;

namespace Ugevr
{
    // These values correspond to the exported native frame contract. Other
    // bridge values are deliberately not classified as a tracking interruption.
    public enum NativePoseCallbackStatus { Success=0, InvalidPose=-13 }
    public enum PoseCallbackDisposition { AwaitCallback, ReadPose, RetryPose, PauseTracking, Fail }

    public sealed class TrackingReacquisition
    {
        readonly double timeoutSeconds;
        double deadline;
        public bool Recovering { get; private set; }
        public bool Paused { get; private set; }

        public TrackingReacquisition(double timeoutSeconds)
        {
            if (Double.IsNaN(timeoutSeconds) || Double.IsInfinity(timeoutSeconds) || timeoutSeconds<=0)
                throw new ArgumentOutOfRangeException("timeoutSeconds");
            this.timeoutSeconds=timeoutSeconds;
        }

        public PoseCallbackDisposition Observe(bool acknowledged,int waitError,NativePoseCallbackStatus bridgeStatus,double now)
        {
            if (Double.IsNaN(now) || Double.IsInfinity(now) || now<0) throw new ArgumentOutOfRangeException("now");
            // Read no error from an older callback as if it belonged to the
            // pending frame. Runtime and non-pose bridge errors remain fatal.
            if (acknowledged && (waitError!=0 || (bridgeStatus!=NativePoseCallbackStatus.Success && bridgeStatus!=NativePoseCallbackStatus.InvalidPose)))
                return PoseCallbackDisposition.Fail;
            if (Paused || (Recovering && now>=deadline))
            {
                Paused=true;
                return PoseCallbackDisposition.PauseTracking;
            }
            if (!acknowledged) return PoseCallbackDisposition.AwaitCallback;
            if (bridgeStatus==NativePoseCallbackStatus.InvalidPose)
            {
                if (!Recovering) { Recovering=true; deadline=now+timeoutSeconds; }
                return PoseCallbackDisposition.RetryPose;
            }
            // A valid native wait permits reading its pose, not rendering it.
            // Confirm only after the managed HMD flags/matrix are also valid.
            return PoseCallbackDisposition.ReadPose;
        }

        public void ConfirmValidPose()
        {
            if (Paused) throw new InvalidOperationException("Tracking is paused; explicitly retry before accepting a pose.");
            Reset();
        }
        public void Reset() { Recovering=false; Paused=false; deadline=0; }
    }
}
