using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace Ugevr
{
    // One instance per eye pair, created only for opt-in diagnostic capture.
    public sealed class OverlayWallTiming
    {
        const int ScanCapacity=16;
        struct Scan
        {
            public string Stage;
            public int Discovered,Inspected;
            public bool Complete;
            public long QueryTicks,InspectionTicks;
        }
        readonly Scan[] scans=new Scan[ScanCapacity];
        readonly long[] gameTicks=new long[2],uiTicks=new long[2];
        readonly bool[] gameReturned=new bool[2],uiReturned=new bool[2];
        readonly bool[] gameAttempted=new bool[2],uiAttempted=new bool[2];
        int count,dropped;
        public static long Timestamp() {return Stopwatch.GetTimestamp();}
        public void RecordScan(string stage,int discovered,int inspected,bool complete,long queryTicks,long inspectionTicks)
        {
            if(count==ScanCapacity) {++dropped;return;}
            scans[count++]=new Scan {Stage=stage==null?"":stage.Length>64?stage.Substring(0,64):stage,Discovered=discovered,Inspected=inspected,Complete=complete,QueryTicks=queryTicks,InspectionTicks=inspectionTicks};
        }
        public void Render(Camera camera,int eye,bool ui)
        {
            if(eye<0 || eye>1) throw new ArgumentOutOfRangeException("eye");
            (ui?uiAttempted:gameAttempted)[eye]=true;
            long start=Timestamp();bool returned=false;
            try {if(ui) OverlayCameraPass.Render(camera);else camera.Render();returned=true;}
            finally
            {
                (ui?uiTicks:gameTicks)[eye]=Timestamp()-start;
                (ui?uiReturned:gameReturned)[eye]=returned;
            }
        }
        public string DiagnosticJson()
        {
            var text=new StringBuilder("{\"clock\":\"Stopwatch\",\"frequency\":").Append(Stopwatch.Frequency.ToString(CultureInfo.InvariantCulture))
                .Append(",\"scope\":\"current eye pair CPU wall time; Camera.Render return is not GPU completion\",\"scanCapacity\":").Append(ScanCapacity)
                .Append(",\"droppedScans\":").Append(dropped).Append(",\"scans\":[");
            for(int i=0;i<count;++i)
            {
                if(i!=0) text.Append(',');Scan scan=scans[i];
                text.Append("{\"stage\":").Append(Quote(scan.Stage)).Append(",\"inspectionObjectKind\":\"GameObject\",\"discovered\":").Append(scan.Discovered)
                    .Append(",\"inspected\":").Append(scan.Inspected).Append(",\"complete\":").Append(scan.Complete?"true":"false")
                    .Append(",\"queryWallTicks\":").Append(scan.QueryTicks.ToString(CultureInfo.InvariantCulture))
                    .Append(",\"inspectionWallTicks\":").Append(scan.InspectionTicks.ToString(CultureInfo.InvariantCulture)).Append('}');
            }
            text.Append("],\"cameraRenders\":[");
            for(int eye=0;eye<2;++eye)
            {
                if(eye!=0) text.Append(',');
                text.Append("{\"eye\":").Append(eye).Append(",\"gameWallTicks\":").Append(gameTicks[eye].ToString(CultureInfo.InvariantCulture))
                    .Append(",\"gameAttempted\":").Append(gameAttempted[eye]?"true":"false")
                    .Append(",\"gameReturned\":").Append(gameReturned[eye]?"true":"false")
                    .Append(",\"uiWallTicks\":").Append(uiTicks[eye].ToString(CultureInfo.InvariantCulture))
                    .Append(",\"uiAttempted\":").Append(uiAttempted[eye]?"true":"false")
                    .Append(",\"uiReturned\":").Append(uiReturned[eye]?"true":"false").Append('}');
            }
            return text.Append("]}").ToString();
        }
        static string Quote(string value)
        {
            var text=new StringBuilder("\"");
            foreach(char character in value)
            {
                if(character=='"' || character=='\\') text.Append('\\').Append(character);
                else if(Char.IsControl(character)) text.Append("\\u").Append(((int)character).ToString("x4",CultureInfo.InvariantCulture));
                else text.Append(character);
            }
            return text.Append('"').ToString();
        }
    }
}
