using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using CatQuest.Updates;
using CatQuestUpdater;

public static class Verification
{
    static int checks;
    static void Check(bool value,string message) { if(!value)throw new Exception(message); checks++; }
    static void Reject(Action action,string message) { bool failed=false;try {action();} catch {failed=true;} Check(failed,message); }
    static void Package(string folder,string version,bool old)
    {
        Directory.CreateDirectory(folder);
        foreach(string file in new[]{"CatQuest.exe","CatQuestUpdater.exe","UnityPlayer.dll",old?"obsolete.dll":"new.dll"}) File.WriteAllText(Path.Combine(folder,file),version+file);
        Installer.Write(Path.Combine(folder,UpdateRules.InventoryName),new PackageInventory {version=version,files=Directory.GetFiles(folder).Select(p=>new PackageFile {path=Path.GetFileName(p),size=new FileInfo(p).Length,sha256=UpdateRules.Hash(p)}).ToArray()});
    }
    public static int Main(string[] args)
    {
        string root=Path.GetFullPath(args[0]);Directory.CreateDirectory(root);
        try
        {
            Check(!UpdateRules.IsNewer("1.2.0","1.2") && UpdateRules.IsNewer("1.10","1.9"),"Numeric version ordering");
            foreach(string bad in new[]{"../escape","/absolute","a/../../escape","a\\b","a:b","CON.txt","a.","a ","a//b"}) Reject(()=>UpdateRules.SafePath(root,bad),"Unsafe path accepted: "+bad);
            Reject(()=>UpdateRules.Validate(new ReleaseManifest{version="2.0",url="https://evil.example/a.zip",size=1,sha256=new string('a',64)}),"Untrusted release accepted");
            var stage=Path.Combine(root,"stage");Package(stage,"1.3",false);
            var target=Path.Combine(root,"installed");Package(target,"1.2",true);File.WriteAllText(Path.Combine(target,"personal.txt"),"keep");
            Installer.Apply(target,stage,Path.Combine(root,"transaction-success"));
            Check(Installer.LoadInventory(target).version=="1.3" && !File.Exists(Path.Combine(target,"obsolete.dll")) && File.ReadAllText(Path.Combine(target,"personal.txt"))=="keep","Upgrade or preservation failed");
            Reject(()=>Installer.Apply(target,stage,Path.Combine(root,"duplicate")),"Duplicate installation accepted");
            var rollback=Path.Combine(root,"rollback");Package(rollback,"1.2",true);
            Reject(()=>Installer.Apply(rollback,stage,Path.Combine(root,"transaction-failure"),n=>{if(n==5)throw new IOException("Injected disk failure");}),"Injected failure ignored");
            Check(Installer.LoadInventory(rollback).version=="1.2" && File.Exists(Path.Combine(rollback,"obsolete.dll")) && !File.Exists(Path.Combine(rollback,"new.dll")) && File.ReadAllText(Path.Combine(rollback,"CatQuest.exe"))=="1.2CatQuest.exe","Rollback failed");
            Installer.Recover(rollback,Path.Combine(root,"transaction-failure"));
            Installer.Apply(rollback,stage,Path.Combine(root,"retry"));Check(Installer.LoadInventory(rollback).version=="1.3","Retry failed");
            var conflict=Path.Combine(root,"conflict");Package(conflict,"1.2",true);File.WriteAllText(Path.Combine(conflict,"new.dll"),"user file");
            Reject(()=>Installer.Apply(conflict,stage,Path.Combine(root,"conflict-transaction")),"User file conflict overwritten");
            Check(File.ReadAllText(Path.Combine(conflict,"new.dll"))=="user file","Conflict altered file");
            var crash=Path.Combine(root,"crash");Package(crash,"1.2",true);var journal=Path.Combine(root,"crash-transaction");Directory.CreateDirectory(Path.Combine(journal,"backup"));
            File.Copy(Path.Combine(crash,"CatQuest.exe"),Path.Combine(journal,"backup/CatQuest.exe"));File.WriteAllText(Path.Combine(crash,"CatQuest.exe"),"partial");
            var log=new Journal{state="installing"};log.entries.Add(new JournalEntry{path="CatQuest.exe",existed=true});Installer.Write(Path.Combine(journal,"journal.json"),log);
            Installer.Recover(crash,journal);Installer.Recover(crash,journal);Check(File.ReadAllText(Path.Combine(crash,"CatQuest.exe"))=="1.2CatQuest.exe","Interrupted installation recovery failed");
            string zip=Path.Combine(root,"package.zip");ZipFile.CreateFromDirectory(stage,zip);
            var request=new InstallRequest{package=zip,size=new FileInfo(zip).Length,sha256=UpdateRules.Hash(zip),version="1.3"};
            Installer.Extract(request,Path.Combine(root,"extracted"));Check(File.Exists(Path.Combine(root,"extracted/CatQuest.exe")),"Valid archive extraction failed");
            request.sha256=new string('0',64);Reject(()=>Installer.Extract(request,Path.Combine(root,"corrupt")),"Corrupt package accepted");
            string evil=Path.Combine(root,"evil.zip");using(var archive=ZipFile.Open(evil,ZipArchiveMode.Create)) {using(var stream=new StreamWriter(archive.CreateEntry("../escape.txt").Open()))stream.Write("bad");}
            Reject(()=>Installer.Extract(new InstallRequest{package=evil,size=new FileInfo(evil).Length,sha256=UpdateRules.Hash(evil),version="1.3"},Path.Combine(root,"evil-stage")),"ZIP traversal accepted");
            Check(!File.Exists(Path.Combine(root,"escape.txt")),"ZIP escaped staging");
            File.WriteAllText(Path.Combine(root,"result.txt"),DateTime.UtcNow.ToString("O")+" PASS "+checks+" checks: versions, source restriction, paths, extraction/checksums, replacement, obsolete files, unrelated files, duplicate, rollback/retry, crash recovery.");
            Console.WriteLine(File.ReadAllText(Path.Combine(root,"result.txt")));return 0;
        }
        catch(Exception e){File.WriteAllText(Path.Combine(root,"result.txt"),"FAIL "+e);Console.WriteLine(e);return 1;}
    }
}
