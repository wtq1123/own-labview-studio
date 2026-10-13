using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace OwnLabView {
// NI helper front panels are shared mutable state in the LabVIEW process.
// A single lease covers an entire build/project operation across desktop/MCP workers.
public sealed class NiSessionLease : IDisposable {
    Mutex mutex;
    bool acquired;
    bool ownsMarker;
    readonly string marker=Path.Combine(Storage.Root,"ni-operation-in-progress.json");
    public NiSessionLease() {
        mutex=new Mutex(false,@"Local\OwnLabViewStudio_NI_x86");
        try {
            acquired=mutex.WaitOne(0);
            if(!acquired)throw new InvalidOperationException("Another Own LabVIEW operation is active. Wait for it to finish before starting another build or project.");
            Storage.NoReparse(marker);
            if(File.Exists(marker)) {
                var previous=Data.Map(Data.Json.DeserializeObject(File.ReadAllText(marker,Encoding.UTF8)));
                string[] now=LabViewProcesses();
                var old=Data.Array(previous["labview_processes"]).Select(Convert.ToString).ToArray();
                if(now.Length>0&&(old.Length==0||old.Intersect(now).Any()))throw new InvalidOperationException("A previous NI operation did not finish cleanly. Preserve your work and restart LabVIEW before another build. Recovery record: "+marker);
                // No previous NI process remains: its outstanding operations and
                // native references cannot survive into the next session.
                File.Delete(marker);
            }
            Storage.WriteNew(marker,new{worker_pid=Process.GetCurrentProcess().Id,labview_processes=LabViewProcesses(),started_utc=DateTime.UtcNow.ToString("o")});ownsMarker=true;
        } catch(AbandonedMutexException) {
            acquired=true;Dispose();
            throw new InvalidOperationException("A previous NI operation ended unexpectedly. Check LabVIEW for unfinished operations before retrying.");
        } catch {Dispose();throw;}
    }
    static string[] LabViewProcesses(){return Process.GetProcessesByName("LabVIEW").Select(p=>{using(p){try{return p.Id+":"+p.StartTime.ToUniversalTime().Ticks;}catch{return p.Id+":unknown";}}}).ToArray();}
    public void Attached(){if(ownsMarker)File.WriteAllText(marker,Data.Json.Serialize(new{worker_pid=Process.GetCurrentProcess().Id,labview_processes=LabViewProcesses(),attached_utc=DateTime.UtcNow.ToString("o")}),new UTF8Encoding(false));}
    public void Dispose(){try{if(ownsMarker){File.Delete(marker);ownsMarker=false;}}finally{if(mutex!=null){if(acquired){mutex.ReleaseMutex();acquired=false;}mutex.Dispose();mutex=null;}}}
}
}
