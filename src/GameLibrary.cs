using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;

internal static partial class Program
{
    internal sealed class InstalledGame {
        public string Executable {get;set;} public string Name {get;set;}
        public string OptiScaler {get;set;} public string Streamline {get;set;}
        public string Proxy {get;set;} public string PatcherMode {get;set;}
        public string UpdatedUtc {get;set;} public string LastResult {get;set;} public string ErrorCode {get;set;}
        public string Folder {get{return Path.GetDirectoryName(Executable);}}
        public string Status {get{return !File.Exists(Executable)?"Executable missing":ReadGameManifest(Folder)==null?"Installation missing":LastResult??"Installed";}}
    }
    internal sealed class GameLibrary {
        public int schema {get;set;}
        public List<InstalledGame> games {get;set;}
        public List<string> ignoredFolders {get;set;}
        public GameLibrary(){schema=1;games=new List<InstalledGame>();ignoredFolders=new List<string>();}
    }
    static string LibraryPath {get{return Path.Combine(DataRoot,"installed-games.json");}}
    static FileStream LibraryLock(){Directory.CreateDirectory(DataRoot);return new FileStream(Path.Combine(DataRoot,".installed-games.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
    static GameLibrary ReadLibrary(){
        if(!File.Exists(LibraryPath))return new GameLibrary();
        try{var library=Json.Deserialize<GameLibrary>(File.ReadAllText(LibraryPath));if(library==null||library.schema!=1||library.games==null||library.ignoredFolders==null)throw new Exception("Unsupported library format.");
            if(library.games.Any(x=>x==null||string.IsNullOrWhiteSpace(x.Executable)||!Path.IsPathRooted(x.Executable)))throw new Exception("Invalid executable path.");return library;
        }catch(Exception e){throw new AppError("E_GAME_LIBRARY",54,"The installed-games list could not be read. The file was kept at "+LibraryPath+". Restore its .bak copy if needed.",e);}
    }
    static void SaveLibrary(GameLibrary library){
        string temp=LibraryPath+".tmp";File.WriteAllText(temp,Json.Serialize(library));
        if(File.Exists(LibraryPath))File.Replace(temp,LibraryPath,LibraryPath+".bak");else File.Move(temp,LibraryPath);
    }
    static InstalledGame LibraryEntry(GameManifest manifest){return new InstalledGame{Executable=Path.GetFullPath(manifest.executable),Name=Path.GetFileNameWithoutExtension(manifest.executable),OptiScaler=manifest.optiscaler,Streamline=manifest.streamline,Proxy=manifest.proxy,PatcherMode=manifest.patcherMode??(manifest.patcher=="disabled"?"off":"auto"),UpdatedUtc=manifest.updatedUtc,LastResult="Installed",ErrorCode="OK"};}
    static void RegisterGame(GameManifest manifest){
        using(var gate=LibraryLock()){
            var library=ReadLibrary();var entry=LibraryEntry(manifest);library.games.RemoveAll(x=>x.Folder.Equals(entry.Folder,StringComparison.OrdinalIgnoreCase));library.ignoredFolders.RemoveAll(x=>x.Equals(entry.Folder,StringComparison.OrdinalIgnoreCase));library.games.Add(entry);SaveLibrary(library);
        }
        WriteEvent("INFO","GAME_REGISTERED","Game added to the installed-games list.",new{executable=manifest.executable});
    }
    static void ForgetGame(string executable){using(var gate=LibraryLock()){
        var library=ReadLibrary();string folder=Path.GetDirectoryName(Path.GetFullPath(executable));library.games.RemoveAll(x=>x.Folder.Equals(folder,StringComparison.OrdinalIgnoreCase));if(!library.ignoredFolders.Contains(folder,StringComparer.OrdinalIgnoreCase))library.ignoredFolders.Add(folder);SaveLibrary(library);
    }}
    static void DiscoverGames(){
        using(var gate=LibraryLock()){
            var library=ReadLibrary();var candidates=new HashSet<string>(StringComparer.OrdinalIgnoreCase);string configured=Settings.Get("game.executable");if(!string.IsNullOrWhiteSpace(configured))candidates.Add(configured);
            if(Directory.Exists(LogsRoot))foreach(string log in Directory.GetFiles(LogsRoot,"*.jsonl"))foreach(string line in File.ReadLines(log)){
                try{var row=Json.Deserialize<Dictionary<string,object>>(line);if(Convert.ToString(row["code"])!="GAME_SUCCESS")continue;var data=(Dictionary<string,object>)row["data"];candidates.Add(Convert.ToString(data["game"]));}catch{}
            }
            bool changed=false;
            foreach(string executable in candidates){
                if(string.IsNullOrWhiteSpace(executable)||!Path.IsPathRooted(executable))continue;string folder=Path.GetDirectoryName(Path.GetFullPath(executable));
                if(library.ignoredFolders.Contains(folder,StringComparer.OrdinalIgnoreCase)||library.games.Any(x=>x.Folder.Equals(folder,StringComparison.OrdinalIgnoreCase)))continue;
                var manifest=ReadGameManifest(folder);if(manifest==null||string.IsNullOrWhiteSpace(manifest.executable)||!Path.GetFullPath(manifest.executable).Equals(Path.GetFullPath(executable),StringComparison.OrdinalIgnoreCase))continue;
                library.games.Add(LibraryEntry(manifest));changed=true;
            }
            if(changed)SaveLibrary(library);
        }
    }
    static void ImportGame(string executable){
        string full=Path.GetFullPath(executable);var manifest=ReadGameManifest(Path.GetDirectoryName(full));
        if(!File.Exists(full)||manifest==null||string.IsNullOrWhiteSpace(manifest.executable)||!Path.GetFullPath(manifest.executable).Equals(full,StringComparison.OrdinalIgnoreCase))throw new AppError("E_GAME_IMPORT",54,"Choose an executable previously installed by OptiScaler Tools. Its installation manifest must be beside it.");
        RegisterGame(manifest);
    }
    internal sealed class BatchResult {internal int Updated,Failed,Missing;public override string ToString(){return Updated+" updated, "+Failed+" failed, "+Missing+" missing.";}}
    static void RecordGameResult(string executable,string result,string code){using(var gate=LibraryLock()){
        var library=ReadLibrary();var game=library.games.FirstOrDefault(x=>x.Executable.Equals(executable,StringComparison.OrdinalIgnoreCase));if(game!=null){game.LastResult=result;game.ErrorCode=code;SaveLibrary(library);}
    }}
    static async Task<BatchResult> UpdateAllGames(bool prepare=true,bool helper=true){
        List<InstalledGame> games;using(var gate=AcquireLock()){Organize();DiscoverGames();games=ReadLibrary().games.ToList();}
        var result=new BatchResult();if(games.Count==0){WriteEvent("INFO","BATCH_EMPTY","No managed games are registered.",null);return result;}
        WriteEvent("INFO","BATCH_START","Updating "+games.Count+" registered games. Game INI values and load methods will be retained.",null);
        if(prepare)using(var gate=AcquireLock())await Install();
        string package=LatestBuild();if(package==null)throw new AppError("E_PACKAGE",30,"No prepared package is available.");
        foreach(var game in games){
            try{
                if(!File.Exists(game.Executable)||ReadGameManifest(game.Folder)==null){result.Missing++;RecordGameResult(game.Executable,"Missing","E_GAME_MISSING");WriteEvent("WARN","E_GAME_MISSING","Skipping a missing game or installation: "+game.Executable,null);continue;}
                var manifest=ReadGameManifest(game.Folder);if(string.IsNullOrWhiteSpace(manifest.executable)||!Path.GetFullPath(manifest.executable).Equals(game.Executable,StringComparison.OrdinalIgnoreCase))throw new AppError("E_GAME_IDENTITY",54,"The game's installation manifest belongs to a different executable.");
                WriteEvent("INFO","BATCH_GAME","Updating "+game.Name,new{executable=game.Executable});
                if(helper)RunSetupProcess(game.Executable,package,game.Proxy,game.PatcherMode,true);
                else using(var gate=AcquireLock())await InstallGame(game.Executable,package,game.Proxy,game.PatcherMode,true);
                RecordGameResult(game.Executable,"Updated","OK");result.Updated++;
            }catch(Exception e){result.Failed++;RecordGameResult(game.Executable,"Failed",ErrorCode(e));WriteEvent("WARN","BATCH_GAME_FAILED","Could not update "+game.Name+": "+DescribeException(e),new{executable=game.Executable,code=ErrorCode(e),exitCode=ExitCode(e)});}
        }
        WriteEvent("INFO","BATCH_COMPLETE",result.ToString(),new{updated=result.Updated,failed=result.Failed,missing=result.Missing});
        if(result.Failed>0||result.Missing>0)throw new AppError("E_BATCH_PARTIAL",55,"Update all completed: "+result+" See per-game results and logs.");return result;
    }
    static void PreserveGameSettings(string game,string staging){
        string current=Path.Combine(game,"OptiScaler.ini"),next=Path.Combine(staging,"OptiScaler.ini");if(!File.Exists(current))throw new AppError("E_INI_PATH",40,"The installed INI is missing; restore it before updating this game.");
        var defaults=new IniDocument(next).Entries();File.Copy(current,next,true);var retained=new IniDocument(next);var keys=new HashSet<string>(retained.Entries().Select(x=>x.Id),StringComparer.OrdinalIgnoreCase);bool added=false;
        foreach(var field in defaults)if(keys.Add(field.Id)){retained.Set(field.Section,field.Key,field.Value,field.Description);added=true;}if(added)retained.Save(false);
        WriteEvent("INFO","INI_RETAINED","Retained this game's INI values and comments; added newly introduced settings.",null);
    }
}
