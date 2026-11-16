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
        folder=Installer.IoPath(folder);
        Directory.CreateDirectory(folder);
        foreach(string file in new[]{"CatQuest.exe","CatQuestUpdater.exe","CatQuestUpdater.exe.config","UnityPlayer.dll",old?"obsolete.dll":"new.dll"}) File.WriteAllText(Path.Combine(folder,file),version+file);
        Installer.Write(Path.Combine(folder,UpdateRules.InventoryName),new PackageInventory {version=version,files=Directory.GetFiles(folder).Select(p=>new PackageFile {path=Path.GetFileName(p),size=new FileInfo(p).Length,sha256=UpdateRules.Hash(p)}).ToArray()});
    }
    static void NativeFixture(string path,ushort machine)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        using(var stream=File.Create(path))using(var writer=new BinaryWriter(stream)) {
            stream.SetLength(128);writer.Write((ushort)0x5a4d);stream.Position=0x3c;writer.Write(64);stream.Position=64;
            writer.Write((uint)0x00004550);writer.Write(machine);
        }
    }
    static void WriteNativeInventory(string stage)
    {
        Installer.Write(Path.Combine(stage,UpdateRules.InventoryName),new PackageInventory {version="1.3",sqlite=true,
            files=Directory.GetFiles(stage,"*",SearchOption.AllDirectories).Where(p=>Path.GetFileName(p)!=UpdateRules.InventoryName)
                .Select(p=>new PackageFile{path=p.Substring(stage.Length+1).Replace('\\','/'),size=new FileInfo(p).Length,sha256=UpdateRules.Hash(p)}).ToArray()});
    }
    static InstallRequest Archive(string stage,string zip)
    {
        using(var archive=ZipFile.Open(zip,ZipArchiveMode.Create))
            foreach(string file in Directory.GetFiles(stage,"*",SearchOption.AllDirectories))archive.CreateEntryFromFile(file,file.Substring(stage.Length+1).Replace('\\','/'));
        return new InstallRequest{package=zip,size=new FileInfo(zip).Length,sha256=UpdateRules.Hash(zip),version="1.3"};
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
            foreach(string protectedPath in new[]{"catquest-data.json","custom.sqlite3","custom.sqlite3-wal","custom.sqlite3-shm","custom.sqlite3.migration.json","custom.sqlite3.pre-sqlite.json.bak","custom.sqlite3.before-import-1.sqlite3","portable.catquestbackup"}) {
                var inventory=Installer.LoadInventory(stage);
                inventory.files=inventory.files.Concat(new[]{new PackageFile{path=protectedPath,size=0,sha256=new string('a',64)}}).ToArray();
                Reject(()=>UpdateRules.ValidateInventory(inventory),"Player storage accepted in package: "+protectedPath);
            }
            var sqliteInventory=Installer.LoadInventory(stage);sqliteInventory.sqlite=true;
            Reject(()=>UpdateRules.ValidateInventory(sqliteInventory),"SQLite package without native dependency accepted");
            sqliteInventory.files=sqliteInventory.files.Concat(new[]{new PackageFile{path=UpdateRules.SqliteNativePath,size=1,sha256=new string('a',64)}}).ToArray();
            UpdateRules.ValidateInventory(sqliteInventory);Check(true,"SQLite native dependency included in inventory");
            var missingConfig=sqliteInventory.files;sqliteInventory.files=missingConfig.Where(f=>f.path!="CatQuestUpdater.exe.config").ToArray();
            Reject(()=>UpdateRules.ValidateInventory(sqliteInventory),"SQLite package missing updater runtime config accepted");
            var nativeStage=Path.Combine(root,"native-stage");Package(nativeStage,"1.3",false);
            NativeFixture(Path.Combine(nativeStage,UpdateRules.SqliteNativePath),0x8664);
            NativeFixture(Path.Combine(nativeStage,"CatQuest_Data/Plugins/ARM64/gilzoide-sqlite-net.dll"),0xaa64);
            WriteNativeInventory(nativeStage);Installer.Extract(Archive(nativeStage,Path.Combine(root,"native.zip")),Path.Combine(root,"native-valid"));Check(true,"x64 native with ARM64 sibling accepted");
            NativeFixture(Path.Combine(nativeStage,UpdateRules.SqliteNativePath),0xaa64);WriteNativeInventory(nativeStage);
            Reject(()=>Installer.Extract(Archive(nativeStage,Path.Combine(root,"wrong-arm.zip")),Path.Combine(root,"wrong-arm")),"Hash-correct ARM64 at x64 path accepted");
            File.WriteAllText(Path.Combine(nativeStage,UpdateRules.SqliteNativePath),"invalid PE");WriteNativeInventory(nativeStage);
            Reject(()=>Installer.Extract(Archive(nativeStage,Path.Combine(root,"malformed.zip")),Path.Combine(root,"malformed")),"Hash-correct malformed native PE accepted");
            var longRoot=Path.Combine(root,new string('a',90),"中文 安装目录",new string('b',90));
            var longStage=Path.Combine(longRoot,"release");var longTarget=Path.Combine(longRoot,"installed");var longTransaction=Path.Combine(longRoot,"transaction");
            Check(Path.Combine(longTransaction,"backup/CatQuestUpdater.exe.config").Length>260,"Long path regression exceeds MAX_PATH");
            Package(longStage,"1.3",false);Package(longTarget,"1.2",true);
            Reject(()=>Installer.Apply(longTarget,longStage,longTransaction,n=>{if(n==5)throw new IOException("Injected long path rollback");}),"Long path injected failure ignored");
            Check(Installer.LoadInventory(longTarget).version=="1.2"&&File.ReadAllText(Installer.IoPath(Path.Combine(longTarget,"CatQuest.exe")))=="1.2CatQuest.exe","Unicode long path rollback preserves old installation");
            Installer.Apply(longTarget,longStage,Path.Combine(longRoot,"retry"));Check(Installer.LoadInventory(longTarget).version=="1.3","Unicode long path update succeeds");
            var longExtract=Path.Combine(longRoot,"extracted");
            Installer.Extract(Archive(stage,Path.Combine(root,"long-extract.zip")),longExtract);
            Check(File.ReadAllText(Installer.IoPath(Path.Combine(longExtract,"CatQuest.exe")))=="1.3CatQuest.exe","Archive extraction supports extended long staging path");
            Reject(()=>UpdateRules.SafePath(Installer.IoPath(longExtract),"../escape"),"Extended root must retain traversal protection");
            var target=Path.Combine(root,"installed");Package(target,"1.2",true);File.WriteAllText(Path.Combine(target,"personal.txt"),"keep");
            foreach(string storage in new[]{"custom.sqlite3","custom.sqlite3-wal","custom.sqlite3-shm","portable.catquestbackup","custom.sqlite3.migration.json"})File.WriteAllText(Path.Combine(target,storage),"private data");
            Installer.Apply(target,stage,Path.Combine(root,"transaction-success"));
            Check(new[]{"custom.sqlite3","custom.sqlite3-wal","custom.sqlite3-shm","portable.catquestbackup","custom.sqlite3.migration.json"}.All(p=>File.ReadAllText(Path.Combine(target,p))=="private data"),"SQLite and sidecars survive installation cleanup");
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
            string lockedJournal=Path.Combine(root,"locked-journal.json");
            Installer.Write(lockedJournal,new Journal {state="old"});
            string oldJournal=File.ReadAllText(lockedJournal);
            using(var held=new FileStream(lockedJournal,FileMode.Open,FileAccess.Read,FileShare.Read))
                Reject(()=>Installer.Write(lockedJournal,new Journal {state="new"}),"Persistent journal lock must fail");
            Check(File.ReadAllText(lockedJournal)==oldJournal,"Failed journal replacement changed committed state");
            Installer.Write(lockedJournal,new Journal {state="retry"});
            Check(Installer.Read<Journal>(lockedJournal).state=="retry","Journal retry after persistent lock failed");
            var brief=new FileStream(lockedJournal,FileMode.Open,FileAccess.Read,FileShare.Read);
            var release=new System.Threading.Thread(()=>{System.Threading.Thread.Sleep(30);brief.Dispose();});
            release.Start();
            try { Installer.Write(lockedJournal,new Journal {state="transient"}); }
            finally { release.Join(); brief.Dispose(); }
            Check(Installer.Read<Journal>(lockedJournal).state=="transient","Brief journal lock was not retried safely");
            File.WriteAllText(Path.Combine(root,"result.txt"),DateTime.UtcNow.ToString("O")+" PASS "+checks+" checks: versions, source restriction, paths, extraction/checksums, replacement, obsolete files, unrelated files, duplicate, rollback/retry, crash recovery, SQLite dependencies and PE validation, Unicode long-path extraction/install/rollback/retry.");
            Console.WriteLine(File.ReadAllText(Path.Combine(root,"result.txt")));return 0;
        }
        catch(Exception e){File.WriteAllText(Path.Combine(root,"result.txt"),"FAIL "+e);Console.WriteLine(e);return 1;}
    }
}
