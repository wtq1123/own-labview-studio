using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
namespace OwnLabView {
// Hold one NI Application reference in an owned, bounded helper process for
// the lifetime of the desktop/MCP client. EOF releases only our COM reference;
// no Quit or process termination is sent to LabVIEW itself.
public static class NiApplicationLifetime {
    static readonly object gate=new object();
    static Process host;
    static HashSet<string> sessionProcesses=new HashSet<string>();
    static HashSet<string> Snapshot() {
        var result=new HashSet<string>();
        string executable=LabView.RegisteredExecutable();
        foreach(var process in Process.GetProcessesByName("LabVIEW"))using(process)try {
            if(String.Equals(Path.GetFullPath(process.MainModule.FileName),executable,StringComparison.OrdinalIgnoreCase))
                result.Add(process.Id+":"+process.StartTime.ToUniversalTime().Ticks);
        }catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}
        return result;
    }
    public static void EnsureForBuild() {
        lock(gate) {
            if(host!=null&&!host.HasExited&&sessionProcesses.SetEquals(Snapshot()))return;
            CloseHost();
            host=new Process {StartInfo=new ProcessStartInfo {
                FileName=Path.Combine(Storage.Root,"bin","OwnLabViewMcp.exe"),Arguments="--ni-keepalive",
                UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,
                RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,
                StandardOutputEncoding=new UTF8Encoding(false),StandardErrorEncoding=new UTF8Encoding(false),
                WorkingDirectory=Storage.Root
            }};
            try {
                host.Start();
                var stderr=host.StandardError.ReadToEndAsync();
                var ready=host.StandardOutput.ReadLineAsync();
                if(!ready.Wait(30000))throw new TimeoutException("LabVIEW session activation did not finish within 30 seconds.");
                if(ready.Result!="NI_SESSION_READY")throw new InvalidOperationException("LabVIEW session activation failed. "+(stderr.IsCompleted?stderr.Result:""));
                sessionProcesses=Snapshot();
                if(sessionProcesses.Count==0)throw new InvalidOperationException("LabVIEW exited during session activation.");
            } catch {CloseHost();throw;}
        }
    }
    public static int RunHost() {
        object app=null;
        try {
            using(var lease=new NiSessionLease()) {
                app=Activator.CreateInstance(Type.GetTypeFromProgID("LabVIEW.Application",true));
                lease.Attached();
            }
            Console.WriteLine("NI_SESSION_READY");Console.Out.Flush();
            while(Console.ReadLine()!=null){}
            return 0;
        } finally {LabView.Release(app);}
    }
    static void CloseHost() {
        Process child=host;host=null;sessionProcesses.Clear();
        if(child==null)return;
        try {
            try {child.StandardInput.Close();}
            catch(InvalidOperationException){}
            catch(IOException ex){CleanupWarning(ex);}
            try {if(child.HasExited||child.WaitForExit(5000))return;}
            catch(InvalidOperationException){return;}
            catch(System.ComponentModel.Win32Exception ex){CleanupWarning(ex);}
            // Only the exact Process instance started by this client is stopped.
            try {child.Kill();child.WaitForExit(2000);}
            catch(InvalidOperationException){}
            catch(System.ComponentModel.Win32Exception ex){CleanupWarning(ex);}
        } finally {child.Dispose();}
    }
    static void CleanupWarning(Exception ex) {
        try {Console.Error.WriteLine("Own LabVIEW session helper cleanup: "+ex.Message);}
        catch(IOException){}catch(ObjectDisposedException){}
    }
    public static void Close() {
        lock(gate) {
            CloseHost();
        }
    }
}
}
