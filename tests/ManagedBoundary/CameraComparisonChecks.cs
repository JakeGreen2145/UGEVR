using System;
using System.IO;
using System.Text;
using Ugevr;
using UnityEngine;

static class CameraComparisonChecks
{
    sealed class ObservedTransform : Transform
    {
        public int PositionQueries,RotationQueries,ScaleQueries,MatrixQueries;
        public bool ThrowPosition,ThrowRotation,ThrowMatrix;
        public Vector3 NextPosition;public Quaternion NextRotation;
        public override Vector3 position {get {++PositionQueries;if(ThrowPosition)throw new InvalidOperationException("private-position");return NextPosition;} set {NextPosition=value;}}
        public override Quaternion rotation {get {++RotationQueries;if(ThrowRotation)throw new InvalidOperationException("private-rotation");return NextRotation;} set {NextRotation=value;}}
        public override Vector3 localScale {get {++ScaleQueries;return base.localScale;} set {base.localScale=value;}}
        public override Matrix4x4 localToWorldMatrix {get {++MatrixQueries;if(ThrowMatrix)throw new InvalidOperationException("private-matrix");return base.localToWorldMatrix;} set {base.localToWorldMatrix=value;}}
        public void ResetQueries() {PositionQueries=RotationQueries=ScaleQueries=MatrixQueries=0;}
    }
    public static void Run(Action<bool,string> check)
    {
        string root=Path.Combine(Path.GetTempPath(),"ugevr-comparison-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            string request=Path.Combine(root,"capture-request");File.WriteAllText(request,"");
            var anchor=new Vector3(1,2,3);var identity=new Quaternion(0,0,0,1);
            var transform=new ObservedTransform {NextPosition=anchor,NextRotation=identity};var camera=new Camera {transform=transform};
            var owner=new CameraRestorationPoseDiagnostics(root);
            check(CameraRestorationComparison.Evaluate(camera,anchor,identity,null) && transform.PositionQueries==1 && transform.RotationQueries==1 && transform.ScaleQueries==0 && transform.MatrixQueries==0,"Disabled comparison preserves one position/rotation read and no context diagnostics");
            transform.NextPosition=new Vector3(1,2,4);transform.ResetQueries();
            check(!CameraRestorationComparison.Evaluate(camera,anchor,identity,null) && transform.PositionQueries==1 && transform.RotationQueries==0 && transform.ScaleQueries==0 && transform.MatrixQueries==0,"Disabled comparison preserves the original position-first short circuit");
            transform.NextPosition=anchor;var falsePosition=owner.Begin(true,request,camera,1);transform.ResetQueries();
            transform.NextPosition=new Vector3(1,2,3.0000152587890625f);transform.ThrowRotation=true;
            bool result=CameraRestorationComparison.Evaluate(camera,anchor,identity,falsePosition);falsePosition.ExistingGameCameraRestored=result;
            string failedPosition=falsePosition.ToPrivateYaml();falsePosition.Publish();
            check(!result && transform.PositionQueries==1 && transform.RotationQueries==0 && transform.ScaleQueries==0 && transform.MatrixQueries==0,"Admitted comparison reads actual position once and never queries skipped rotation or parent state");
            check(failedPosition.Contains("position_equal: false") && failedPosition.Contains("actual_world_position: [1, 2, 3.0000152587890625]") && failedPosition.Contains("position_squared_distance: 2.3283064365386963E-10") && failedPosition.Contains("rotation_term_evaluated: false") && failedPosition.Contains("actual_world_rotation_xyzw: null") && failedPosition.Contains("rotation_equal: null"),"Actual aggregate operands and Unity float squared distance identify failed position while skipped rotation remains unavailable");
            transform.ThrowRotation=false;transform.NextPosition=anchor;transform.NextRotation=identity;
            var falseRotation=owner.Begin(true,request,camera,2);transform.NextRotation=new Quaternion(0,0,0,-1);transform.ResetQueries();
            check(!CameraRestorationComparison.Evaluate(camera,anchor,identity,falseRotation) && transform.PositionQueries==1 && transform.RotationQueries==1,"Rotation predicate evaluates only after successful position with no second read");
            string negative=falseRotation.ToPrivateYaml();falseRotation.Publish();
            check(negative.Contains("rotation_term_evaluated: true") && negative.Contains("position_equal: true") && negative.Contains("rotation_equal: false") && negative.Contains("rotation_signed_dot: -1") && negative.Contains("actual_world_rotation_xyzw: [0, 0, 0, -1]"),"Signed raw dot and actual quaternion remain unnormalized even for equivalent orientation signs");
            transform.NextRotation=identity;var gettersFail=owner.Begin(true,request,camera,3);transform.ThrowRotation=true;transform.ResetQueries();
            bool threw=false;try {CameraRestorationComparison.Evaluate(camera,anchor,identity,gettersFail);}catch(InvalidOperationException){threw=true;}
            string error=gettersFail.ToPrivateYaml();gettersFail.Publish();
            check(threw && transform.PositionQueries==1 && transform.RotationQueries==1 && error.Contains("position_equal: true") && error.Contains("rotation_equal: null") && error.Contains("query_error: diagnostic_query_or_serialization_failure") && !error.Contains("private-rotation"),"Original getter exception propagates while admitted partial operands and sanitized error are retained");
            transform.ThrowRotation=false;var positionFail=owner.Begin(true,request,camera,4);transform.ThrowPosition=true;transform.ResetQueries();
            threw=false;try {CameraRestorationComparison.Evaluate(camera,anchor,identity,positionFail);}catch(InvalidOperationException){threw=true;}
            error=positionFail.ToPrivateYaml();positionFail.Publish();
            check(threw && transform.PositionQueries==1 && transform.RotationQueries==0 && error.Contains("position_equal: null") && error.Contains("actual_world_position: null") && !error.Contains("private-position"),"Failed initial comparison getter preserves short circuit and unavailable operands");
            transform.ThrowPosition=false;var parent=new ObservedTransform {NextPosition=new Vector3(4,5,6),NextRotation=identity,localPosition=new Vector3(7,8,9),localScale=new Vector3(2,3,4),lossyScale=new Vector3(5,6,7),localToWorldMatrix=new Matrix4x4 {Marker=11},worldToLocalMatrix=new Matrix4x4 {Marker=12}};
            transform.parent=parent;transform.localScale=new Vector3(8,9,10);transform.lossyScale=new Vector3(11,12,13);transform.localToWorldMatrix=new Matrix4x4 {Marker=21};transform.worldToLocalMatrix=new Matrix4x4 {Marker=22};
            var context=owner.Begin(true,request,camera,5);parent.NextPosition=new Vector3(14,15,16);parent.localPosition=new Vector3(17,18,19);parent.localToWorldMatrix=new Matrix4x4 {Marker=31};context.Observe("after_camera_state_dispose");
            string contextBody=context.ToPrivateYaml();context.Publish();
            check(contextBody.Contains("parent_world_position: [4, 5, 6]") && contextBody.Contains("parent_world_position: [14, 15, 16]") && contextBody.Contains("parent_local_position: [17, 18, 19]") && contextBody.Contains("parent_local_scale: [2, 3, 4]") && contextBody.Contains("parent_lossy_scale: [5, 6, 7]"),"Fresh parent world/local pose and scales distinguish parent motion despite stable identity");
            check(contextBody.Contains("local_to_world_matrix: [21, 0") && contextBody.Contains("world_to_local_matrix: [22, 0") && contextBody.Contains("parent_local_to_world_matrix: [11, 0") && contextBody.Contains("parent_local_to_world_matrix: [31, 0") && contextBody.Contains("parent_world_to_local_matrix: [12, 0"),"All sixteen Unity-index matrix elements are copied for camera and parent without setters");
            parent.ThrowPosition=true;var parentFailure=owner.Begin(true,request,camera,6);string partial=parentFailure.ToPrivateYaml();parentFailure.Publish();
            check(partial.Contains("query_error: null\n    parent_query_error: diagnostic_query_or_serialization_failure") && partial.Contains("world_position: [1, 2, 3]") && partial.Contains("parent_world_position: null") && !partial.Contains("private-position"),"Parent getter failure leaves completed self context intact and unavailable parent fields explicit");
            parent.ThrowPosition=false;transform.ThrowMatrix=true;var matrixFailure=owner.Begin(true,request,camera,7);partial=matrixFailure.ToPrivateYaml();matrixFailure.Publish();
            check(partial.Contains("query_error: diagnostic_query_or_serialization_failure") && partial.Contains("local_to_world_matrix: null") && partial.Contains("world_position: [1, 2, 3]") && !partial.Contains("private-matrix"),"Matrix getter failure adds no invented matrix or restoration writes");
            transform.ThrowMatrix=false;var bounded=owner.Begin(true,request,camera,8);for(int i=0;i<7;i++)bounded.Observe("after_left_render");
            transform.ResetQueries();parent.ResetQueries();bounded.Observe("after_right_render");
            check(transform.PositionQueries==0 && transform.MatrixQueries==0 && parent.PositionQueries==0 && parent.MatrixQueries==0,"Eight-stage capacity refuses extra parent/self reads");
            check(CameraRestorationComparison.Evaluate(camera,anchor,identity,bounded),"Complete admitted comparison preserves true aggregate");string whole=bounded.ToPrivateYaml();bounded.Publish();
            check(Encoding.UTF8.GetByteCount(whole)<CameraRestorationPoseDiagnostics.MaximumBytes && File.ReadAllText(Path.Combine(root,"camera-restoration-pose-8.yaml"))==whole,"Eight full parent/self stages and actual operands publish under the unchanged32KiB bound");
            transform.ResetQueries();parent.ResetQueries();check(owner.Begin(true,request,camera,9)==null && transform.PositionQueries==0 && parent.PositionQueries==0,"Eight-record capacity performs no context reads");
            string extremes=Path.Combine(root,"extremes");Directory.CreateDirectory(extremes);string extremeRequest=Path.Combine(extremes,"capture-request");File.WriteAllText(extremeRequest,"");
            var elements=new float[16];for(int i=0;i<16;i++)elements[i]=(i%2==0?1:-1)*float.MaxValue;
            var extremeVector=new Vector3(float.MaxValue,-float.MaxValue,float.MaxValue);var extremeRotation=new Quaternion(float.MaxValue,-float.MaxValue,float.MaxValue,-float.MaxValue);
            transform.NextPosition=parent.NextPosition=transform.localPosition=parent.localPosition=transform.localScale=parent.localScale=transform.lossyScale=parent.lossyScale=extremeVector;
            transform.NextRotation=parent.NextRotation=transform.localRotation=parent.localRotation=extremeRotation;
            transform.localToWorldMatrix=parent.localToWorldMatrix=transform.worldToLocalMatrix=parent.worldToLocalMatrix=new Matrix4x4 {Elements=elements};
            var extremeOwner=new CameraRestorationPoseDiagnostics(extremes);var extremeRecord=extremeOwner.Begin(true,extremeRequest,camera,99);for(int i=0;i<7;i++)extremeRecord.Observe("after_left_render");
            CameraRestorationComparison.Evaluate(camera,extremeVector,extremeRotation,extremeRecord);string extremeBody=extremeRecord.ToPrivateYaml();extremeRecord.Publish();
            int extremeBytes=Encoding.UTF8.GetByteCount(extremeBody);Console.WriteLine("Maximum finite-value camera/parent fixture bytes: "+extremeBytes);
            check(extremeBytes<=CameraRestorationPoseDiagnostics.MaximumBytes && File.ReadAllText(Path.Combine(extremes,"camera-restoration-pose-1.yaml"))==extremeBody,"Eight full stages with maximum-magnitude finite components and actual operands fit the unchanged byte cap");
        }
        finally
        {
            string resolved=Path.GetFullPath(root),temp=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            string name=Path.GetFileName(resolved),prefix="ugevr-comparison-";Guid id;
            if(!resolved.StartsWith(temp,StringComparison.OrdinalIgnoreCase)||!name.StartsWith(prefix,StringComparison.Ordinal)||!Guid.TryParseExact(name.Substring(prefix.Length),"N",out id))throw new Exception("Unsafe fixture cleanup.");
            Directory.Delete(resolved,true);
        }
    }
}
