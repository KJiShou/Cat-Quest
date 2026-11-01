using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using CatQuest.Updates;
using CatQuestUpdater;

// Disposable process fixture, compiled only into Library by Verify-Handoff.ps1.
public static class HandoffFixture
{
    public static int Main(string[] args)
    {
        string target=AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
        if(args.Length==0){File.WriteAllText(Path.Combine(target,"restarted.txt"),"restarted "+DateTime.UtcNow.ToString("O"));return 0;}
        string work=Path.GetFullPath(args[0]);
        var manifest=Installer.Read<ReleaseManifest>(Path.Combine(work,"release.json"));
        using(var parent=Process.GetCurrentProcess())
            Installer.Write(Path.Combine(work,"request.json"),new InstallRequest{directory=target,package=Path.Combine(work,"package.zip"),version=manifest.version,sha256=manifest.sha256,size=manifest.size,parentPid=parent.Id,parentStartedUtcTicks=parent.StartTime.ToUniversalTime().Ticks});
        using(var updater=Process.Start(new ProcessStartInfo(Path.Combine(work,"CatQuestUpdater.exe"),"\""+Path.Combine(work,"request.json")+"\""){UseShellExecute=false,CreateNoWindow=true}))
        {
            File.WriteAllText(Path.Combine(work,"helper.pid"),updater.Id.ToString());
            for(int i=0;i<100 && !File.Exists(Path.Combine(work,"ready"));i++){if(updater.HasExited)return 1;Thread.Sleep(100);}
            return File.Exists(Path.Combine(work,"ready"))?0:1;
        }
    }
}
