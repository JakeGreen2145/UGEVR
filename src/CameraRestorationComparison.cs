using System;
using UnityEngine;

namespace Ugevr
{
    public static class CameraRestorationComparison
    {
        // Preserve the aggregate's position-first getter order and short circuit.
        // The finally records only the actual operands, without querying Unity again.
        public static bool Evaluate(Camera camera,Vector3 anchorPosition,Quaternion anchorRotation,CameraRestorationPoseRecord diagnostic)
        {
            if(diagnostic==null)return camera.transform.position==anchorPosition && camera.transform.rotation==anchorRotation;
            Vector3? actualPosition=null;Quaternion? actualRotation=null;
            bool? positionEqual=null,rotationEqual=null;Exception failure=null;
            try
            {
                actualPosition=camera.transform.position;
                positionEqual=actualPosition.Value==anchorPosition;
                if(!positionEqual.Value)return false;
                actualRotation=camera.transform.rotation;
                rotationEqual=actualRotation.Value==anchorRotation;
                return rotationEqual.Value;
            }
            catch(Exception error) {failure=error;throw;}
            finally {diagnostic.RecordComparison(anchorPosition,anchorRotation,actualPosition,actualRotation,positionEqual,rotationEqual,failure);}
        }
    }
}
