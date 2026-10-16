using System;
using System.Collections.Generic;
using System.IO;
using System.IO.IsolatedStorage;
using Ugevr;

static class PairReadinessPathChecks
{
    public static void Run(Action<bool,string> check)
    {
        string leaf=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"ugevr-readiness-path/claim"));
        string parent=Path.GetDirectoryName(leaf);var ancestors=new List<string>();
        for(string p=leaf;p!=null;p=Path.GetDirectoryName(p))ancestors.Add(p);
        var visited=new List<string>();int nativeCalls=0;
        Func<string,RewiredPairSourceReadiness.NativeAttributeRead> noNative=delegate(string p){++nativeCalls;throw new Exception("Unexpected native fallback");};
        RewiredPairSourceReadiness.Safe(leaf,delegate(string p){visited.Add(p);return FileAttributes.Directory;},noNative);
        check(String.Join("|",visited.ToArray())==String.Join("|",ancestors.ToArray()) && nativeCalls==0,"Ordinary attributes inspect every ancestor without extra native calls");
        foreach(Exception missing in new Exception[]{new FileNotFoundException(),new DirectoryNotFoundException()})
        {
            visited.Clear();nativeCalls=0;
            RewiredPairSourceReadiness.Safe(leaf,delegate(string p){visited.Add(p);if(p==leaf)throw missing;return FileAttributes.Directory;},noNative);
            check(visited.Count==ancestors.Count && nativeCalls==0,"Ordinary missing-target exceptions preserve the ancestor walk without native fallback");
        }
        foreach(int code in new[]{2,3})
        {
            visited.Clear();nativeCalls=0;
            RewiredPairSourceReadiness.Safe(leaf,delegate(string p){visited.Add(p);if(p==leaf)throw new IsolatedStorageException();return FileAttributes.Directory;},delegate(string p){++nativeCalls;return new RewiredPairSourceReadiness.NativeAttributeRead(UInt32.MaxValue,code);});
            check(nativeCalls==1 && visited.Count==ancestors.Count,"Legacy missing-file/path classification accepts only native absence and continues all ancestors");
        }
        foreach(int code in new[]{0,5,32,87,123,206})
        {
            var original=new IsolatedStorageException();Exception seen=null;nativeCalls=0;visited.Clear();
            try {RewiredPairSourceReadiness.Safe(leaf,delegate(string p){visited.Add(p);throw original;},delegate(string p){++nativeCalls;return new RewiredPairSourceReadiness.NativeAttributeRead(UInt32.MaxValue,code);});}catch(Exception error){seen=error;}
            check(Object.ReferenceEquals(seen,original) && nativeCalls==1 && visited.Count==1,"Unknown/access/sharing/invalid/long-path native failures retain the original refusal");
        }
        foreach(Exception failure in new Exception[]{new UnauthorizedAccessException(),new IOException()})
        {
            Exception seen=null;nativeCalls=0;
            try {RewiredPairSourceReadiness.Safe(leaf,delegate(string p){throw failure;},noNative);}catch(Exception error){seen=error;}
            check(Object.ReferenceEquals(seen,failure) && nativeCalls==0,"Ordinary non-missing failures never invoke or relax into fallback");
        }
        nativeCalls=0;visited.Clear();
        RewiredPairSourceReadiness.Safe(leaf,delegate(string p){visited.Add(p);if(p==leaf)throw new IsolatedStorageException();return FileAttributes.Directory;},delegate(string p){++nativeCalls;return new RewiredPairSourceReadiness.NativeAttributeRead((uint)FileAttributes.Normal,5);});
        check(nativeCalls==1 && visited.Count==ancestors.Count,"A racing existing ordinary target is inspected using returned attributes, irrespective of stale last-error");
        foreach(bool fallback in new[]{false,true})foreach(string target in new[]{leaf,parent})
        {
            bool refused=false;nativeCalls=0;
            try {RewiredPairSourceReadiness.Safe(leaf,delegate(string p){if(p==target){if(fallback)throw new IsolatedStorageException();return FileAttributes.Directory|FileAttributes.ReparsePoint;}return FileAttributes.Directory;},delegate(string p){++nativeCalls;return new RewiredPairSourceReadiness.NativeAttributeRead((uint)(FileAttributes.Directory|FileAttributes.ReparsePoint),0);});}catch(IOException){refused=true;}
            check(refused && nativeCalls==(fallback?1:0),"Existing or racing reparse targets and ancestors always refuse");
        }
        Exception nativeFailure=new UnauthorizedAccessException(),observed=null;
        try {RewiredPairSourceReadiness.Safe(leaf,delegate(string p){throw new IsolatedStorageException();},delegate(string p){throw nativeFailure;});}catch(Exception error){observed=error;}
        check(Object.ReferenceEquals(observed,nativeFailure),"Native classification exceptions fail closed without an absence inference");
    }
}
