using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

internal static partial class Program
{
    sealed class StorageJournal {public string[] files {get;set;}public Dictionary<string,string> compressedOriginals {get;set;}}
    static void StorageTests(string dir){
        string originalRoot=Root;Root=dir;
        try{
            Directory.CreateDirectory(dir);string input=Path.Combine(dir,"original.dll"),replacement=Path.Combine(dir,"replacement.dll");byte[] bytes=Enumerable.Range(0,262144).Select(x=>(byte)(x%127)).ToArray();File.WriteAllBytes(input,bytes);File.WriteAllBytes(replacement,bytes.Select(x=>(byte)(x^64)).ToArray());
            string game=Path.Combine(dir,"game"),backup=Path.Combine(dir,"backup-identical");Directory.CreateDirectory(game);string target=Path.Combine(game,"same.dll");File.Copy(input,target);DateTime before=new DateTime(2001,1,2,3,4,5,DateTimeKind.Utc);File.SetLastWriteTimeUtc(target,before);
            using(var transaction=new FileTransaction(game,backup)){transaction.Put(input,"same.dll");transaction.Commit();}
            Check(File.GetLastWriteTimeUtc(target)==before&&Json.Deserialize<StorageJournal>(File.ReadAllText(Path.Combine(backup,"transaction.json"))).files.Length==0,"Unchanged files are neither overwritten nor duplicated in installation backups");
            for(int i=0;i<2;i++){string folder=Path.Combine(dir,"changed-"+i);Directory.CreateDirectory(folder);File.Copy(input,Path.Combine(folder,"large.dll"));using(var transaction=new FileTransaction(folder,Path.Combine(dir,"backup-"+i))){transaction.Put(replacement,"large.dll");transaction.Commit();}}
            string hash=Hash(input),objectPath=BackupObjectPath(hash);Check(Directory.GetFiles(BackupObjectRoot,"*.gz").Length==1&&new FileInfo(objectPath).Length<bytes.Length&&Json.Deserialize<StorageJournal>(File.ReadAllText(Path.Combine(dir,"backup-1","transaction.json"))).compressedOriginals["large.dll"]==hash,"Identical large originals from different games share one verified compressed backup object");
            File.Copy(input,target,true);using(var transaction=new FileTransaction(game,Path.Combine(dir,"backup-rollback")))transaction.Put(replacement,"same.dll");Check(Hash(target)==hash,"A failed transaction restores large files from the compressed backup store");
            File.WriteAllText(objectPath,"damaged");File.WriteAllText(target,"keep destination");bool rejected=false;try{RestoreBackupObject(hash,target);}catch{rejected=true;}Check(rejected&&File.ReadAllText(target)=="keep destination"&&!Directory.GetFiles(game,"*.restore-*").Any(),"A damaged backup is rejected without replacing the destination or leaving decompression copies");
            StoreBackupObject(input);Check(ValidBackupObject(objectPath,hash),"A damaged shared object is repaired and verified before it is reused");
            string downloads=Path.Combine(dir,"downloads");Directory.CreateDirectory(downloads);File.WriteAllText(Path.Combine(downloads,"old.zip"),"old");File.WriteAllText(Path.Combine(downloads,"new.7z"),"new");File.WriteAllText(Path.Combine(downloads,"notes.txt"),"keep");PruneDownloads(downloads,"new.7z");Check(!File.Exists(Path.Combine(downloads,"old.zip"))&&File.Exists(Path.Combine(downloads,"new.7z"))&&File.Exists(Path.Combine(downloads,"notes.txt")),"Archive cleanup retains the selected nightly and does not remove other files");
            using(var icon=System.Drawing.Icon.ExtractAssociatedIcon(typeof(Program).Assembly.Location))Check(icon!=null&&icon.Width>=16,"The single executable contains the application icon");
        }finally{Root=originalRoot;}
    }
}
