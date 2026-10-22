using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;

internal static partial class Program
{
    static string BackupObjectRoot {get{return Path.Combine(BuildsRoot,".backups",".objects");}}
    static string BackupObjectPath(string hash){if(!System.Text.RegularExpressions.Regex.IsMatch(hash??"",@"^[a-f0-9]{64}$"))throw new AppError("E_BACKUP_HASH",53,"Invalid backup object identifier.");return Path.Combine(BackupObjectRoot,hash+".gz");}
    static string HashStream(Stream stream){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();}
    static bool ValidBackupObject(string path,string hash){try{using(var file=File.OpenRead(path))using(var gzip=new GZipStream(file,CompressionMode.Decompress))return HashStream(gzip)==hash;}catch{return false;}}
    static string StoreBackupObject(string source){
        string hash=Hash(source),path=BackupObjectPath(hash);Directory.CreateDirectory(BackupObjectRoot);if(File.Exists(path)&&ValidBackupObject(path,hash))return hash;
        string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{using(var file=File.Create(temp))using(var gzip=new GZipStream(file,CompressionLevel.Optimal))using(var input=File.OpenRead(source))input.CopyTo(gzip);
            if(!ValidBackupObject(temp,hash))throw new AppError("E_BACKUP_HASH",53,"The backup could not be verified before replacing a file.");
            if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);return hash;
        }finally{if(File.Exists(temp))File.Delete(temp);}
    }
    static void RestoreBackupObject(string hash,string destination){
        string path=BackupObjectPath(hash),temp=destination+".restore-"+Guid.NewGuid().ToString("N");
        try{Directory.CreateDirectory(Path.GetDirectoryName(destination));using(var input=File.OpenRead(path))using(var gzip=new GZipStream(input,CompressionMode.Decompress))using(var output=File.Create(temp))gzip.CopyTo(output);
            if(Hash(temp)!=hash)throw new AppError("E_BACKUP_HASH",53,"The compressed backup is damaged. The destination was kept.");File.Copy(temp,destination,true);
        }finally{if(File.Exists(temp))File.Delete(temp);}
    }
    static void PruneDownloads(string folder,string keep=null){
        if(!Directory.Exists(folder))return;foreach(string file in Directory.GetFiles(folder).Where(IsArchive))if(!string.Equals(Path.GetFileName(file),keep,StringComparison.OrdinalIgnoreCase)){
            try{long size=new FileInfo(file).Length;File.Delete(file);WriteEvent("INFO","CACHE_PRUNED","Removed a redundant download archive.",new{file=Path.GetFileName(file),bytes=size});}catch(Exception e){WriteEvent("WARN","W_CACHE_CLEANUP","Could not remove a redundant archive: "+DescribeException(e),null);}
        }
    }
}
