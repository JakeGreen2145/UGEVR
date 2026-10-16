using System;
using System.IO;
using System.Text;
using System.Threading;
using Ugevr;

static class CameraEvidenceFailureStageChecks
{
    sealed class PrivateException : Exception {public PrivateException():base("private message and path") {}}
    sealed class FaultStream : Stream
    {
        readonly Exception write,close;public int Writes,Closes;
        public FaultStream(Exception write,Exception close){this.write=write;this.close=close;}
        public override bool CanRead {get {return false;}}public override bool CanSeek {get {return false;}}public override bool CanWrite {get {return true;}}
        public override long Length {get {return 0;}}public override long Position {get {return 0;}set {throw new NotSupportedException();}}
        public override void Flush(){}public override int Read(byte[] b,int o,int n){throw new NotSupportedException();}public override long Seek(long o,SeekOrigin origin){throw new NotSupportedException();}public override void SetLength(long n){throw new NotSupportedException();}
        public override void Write(byte[] b,int o,int n){++Writes;if(write!=null)throw write;}
        protected override void Dispose(bool disposing){++Closes;if(close!=null)throw close;base.Dispose(disposing);}
    }
    static void WriteClosePrecedence(Action<bool,string> check)
    {
        for(int mask=0;mask<4;++mask)
        {
            Exception write=(mask&1)!=0?new ArgumentException("private write"):null,close=(mask&2)!=0?new IOException("private close"):null,actual=null;
            var stream=new FaultStream(write,close);var stage=CameraEvidenceFailureStage.TemporaryFileCreate;
            try {CameraRestorationPoseDiagnostics.WriteAndClose(stream,new byte[]{1,2},ref stage);}catch(Exception error){actual=error;}
            check(Object.ReferenceEquals(actual,close??write) && stream.Writes==1 && stream.Closes==1,"Actual production write/close helper preserves using exception precedence and single attempts");
            check(stage==(close==null?CameraEvidenceFailureStage.TemporaryFileWrite:CameraEvidenceFailureStage.TemporaryFileClose),"Write/close stage identifies the exception actually propagated, including dual failure");
        }
    }
    public static void Run(Action<bool,string> check)
    {
        WriteClosePrecedence(check);
        string root=Path.Combine(Path.GetTempPath(),"ugevr-stage-check-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var publisher=new CameraRestorationPoseDiagnostics(root);string destination=Path.Combine(root,"evidence.yaml");
            check(publisher.LastError=="" && publisher.LastFailureStage=="" && publisher.LastExceptionKind=="","New passive publisher has no invented failure stage or exception kind");
            publisher.Publish(destination,null);
            check(publisher.LastError=="diagnostic_query_or_serialization_failure" && publisher.LastFailureStage=="utf8_encoding" && publisher.LastExceptionKind=="argument_null" && !File.Exists(destination),"Real null input fails UTF8 conversion with closed stage and kind");
            foreach(string malformed in new[]{"\ud800","\udfff","\ud800A","\ud83d\ude00\udfff"})
            {
                publisher.Publish(destination,malformed);
                check(publisher.LastError=="diagnostic_query_or_serialization_failure" && publisher.LastFailureStage=="utf8_encoding" && publisher.LastExceptionKind=="encoder_fallback" && !File.Exists(destination),"Explicit UTF16 validation rejects unpaired surrogate without publishing malformed UTF8");
            }
            string valid=Path.Combine(root,"valid-unicode.yaml");publisher.Publish(valid,"a\u00e9\u4e2d\ud83d\ude00");
            byte[] expected={0x61,0xc3,0xa9,0xe4,0xb8,0xad,0xf0,0x9f,0x98,0x80},actual=File.ReadAllBytes(valid);bool equal=actual.Length==expected.Length;for(int i=0;equal&&i<actual.Length;++i)equal=actual[i]==expected[i];
            check(publisher.LastError=="" && equal,"Valid BMP and surrogate-pair text retains independently expected UTF8 bytes without BOM");File.Delete(valid);
            publisher.Publish(destination,new string('x',32769));
            check(publisher.LastError=="diagnostic_query_or_serialization_failure" && publisher.LastFailureStage=="byte_bound" && publisher.LastExceptionKind=="invalid_operation" && Directory.GetFiles(root).Length==0,"Real byte overflow is distinguished from serialization and produces no partial file");
            string run=Guid.NewGuid().ToString("D");var owner=new GuardedCameraFailureDiagnostics(root,run);var bad=owner.Begin(1,1,1);bad.Failed(null,new string('x',129));bad.Publish();
            check(owner.LastError=="diagnostic_query_or_serialization_failure" && owner.LastFailureStage=="cached_serialization" && owner.LastExceptionKind=="invalid_operation" && owner.Attempts==1,"Actual cached vocabulary serialization failure preserves old category and bounded attempt");
            string blocked=Path.Combine(root,"blocked");File.WriteAllText(blocked,"original");var io=new CameraRestorationPoseDiagnostics(blocked);io.Publish(Path.Combine(blocked,"out.yaml"),"safe");
            check(io.LastError=="io_failure" && io.LastFailureStage=="directory_create" && io.LastExceptionKind=="io_exception" && File.ReadAllText(blocked)=="original","Real parent-file collision reports directory-create stage without overwriting original");
            File.WriteAllText(destination,"original");publisher.Publish(destination,"new");
            check(publisher.LastError=="io_failure" && publisher.LastFailureStage=="move" && publisher.LastExceptionKind=="io_exception" && File.ReadAllText(destination)=="original" && Directory.GetFiles(root,"*.tmp").Length==0,"Destination collision retains old bytes and removes only owned temporary output");
            const string fixedName="0123456789abcdef01234567.tmp";string collision=Path.Combine(root,fixedName);File.WriteAllText(collision,"unowned");var deterministic=new CameraRestorationPoseDiagnostics(root,()=>fixedName);deterministic.Publish(Path.Combine(root,"never.yaml"),"new");
            check(deterministic.LastError=="io_failure" && deterministic.LastFailureStage=="temporary_file_create" && deterministic.LastExceptionKind=="io_exception" && File.ReadAllText(collision)=="unowned" && !File.Exists(Path.Combine(root,"never.yaml")),"Actual CreateNew temporary collision preserves unowned existing file");File.Delete(collision);
            var thread=new Thread(()=>publisher.Publish(Path.Combine(root,"thread.yaml"),"new"));thread.Start();thread.Join();
            check(publisher.LastFailureStage=="owner_thread" && publisher.LastExceptionKind=="invalid_operation" && !File.Exists(Path.Combine(root,"thread.yaml")),"Wrong-thread failure is closed and performs no publication");
            publisher.Failed(new System.IO.IsolatedStorage.IsolatedStorageException("private long-path details"),CameraEvidenceFailureStage.TemporaryFileCreate);
            check(publisher.LastError=="diagnostic_query_or_serialization_failure" && publisher.LastFailureStage=="temporary_file_create" && publisher.LastExceptionKind=="isolated_storage","Legacy Mono isolated-storage exception has explicit closed kind and original generic category");
            publisher.Failed(new PrivateException(),(CameraEvidenceFailureStage)int.MaxValue);
            check(publisher.LastFailureStage=="diagnostic_processing" && publisher.LastExceptionKind=="other_exception","Unknown stage and arbitrary exception class cannot become unbounded labels");
            publisher.Publish(Path.Combine(root,"success.yaml"),"safe");
            check(publisher.LastError=="" && publisher.LastFailureStage=="" && publisher.LastExceptionKind=="","Successful publication clears all old diagnostic failure labels");
            string name="guarded-camera-failure-"+run+"-1.yaml";int padding=254-root.Length-name.Length-2;
            check(padding>0 && padding<256,"Controlled long-directory fixture stays inside one bounded temporary component");
            string deep=Path.Combine(root,new string('d',padding));Directory.CreateDirectory(deep);var deepOwner=new GuardedCameraFailureDiagnostics(deep,run);var record=deepOwner.Begin(1,1,1);record.Failed(null,"owned_eye_position_changed");record.PublishAfterPairUnwind();string target=Path.Combine(deep,name);
            check(target.Length==254 && deepOwner.LastError=="" && File.Exists(target) && Directory.GetFiles(deep,"*.tmp").Length==0,"Actual 254-character destination publishes and removes short same-directory temporary path");
            string temp=Path.Combine(deep,fixedName);check(temp.Length<target.Length && Path.GetDirectoryName(temp)==Path.GetDirectoryName(target),"New temporary basename stays shorter than guarded destination in its same directory");
            check(GuardedCameraFailureDiagnostics.Create(false,true,true,"invalid","invalid")==null && GuardedCameraFailureDiagnostics.Create(true,false,true,"invalid","invalid")==null,"Default-off diagnostics still ignore invalid paths and ownership data");
        }
        finally
        {
            string resolved=Path.GetFullPath(root);if(!resolved.StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(root).StartsWith("ugevr-stage-check-",StringComparison.Ordinal))throw new Exception("Unsafe fixture cleanup.");
            Directory.Delete(root,true);
        }
    }
}