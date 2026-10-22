using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

internal static class SetupEntry
{
    static int Main(string[] args) {return Program.SetupMain(args);}
}

internal static partial class Program
{
    internal sealed class GameManifest {
        public string executable {get;set;} public string optiscaler {get;set;} public string streamline {get;set;}
        public string proxy {get;set;} public string patcher {get;set;} public string backup {get;set;}
        public Dictionary<string,string> files {get;set;}
    }
    internal static int SetupMain(string[] args) {
        UseEnglishCulture();
        NoPause=true;NoOpen=true;
        try {
            Root=Path.GetFullPath(Argument(args,"--root",AppRoot));
            Settings=AppSettings.Load(Path.Combine(AppRoot,"OptiScaler-Tools.toml"));
            using(var gate=AcquireLock()) {
                Organize();BeginSession("setup-game");
                InstallGame(Argument(args,"--game",Settings.Get("game.executable")),Argument(args,"--package",LatestBuild()),Argument(args,"--proxy",Settings.Get("game.proxy")),Argument(args,"--patcher",Settings.Get("game.optipatcher"))).GetAwaiter().GetResult();
                EndSession(true,null);
            }return 0;
        } catch(Exception e) {try{EndSession(false,e);}catch{}return ExitCode(e);}
    }
    static bool IsAmd64(string path) {
        try {using(var f=File.OpenRead(path)) using(var r=new BinaryReader(f)) {
            if(f.Length<64 || r.ReadUInt16()!=0x5a4d)return false;f.Position=0x3c;int pe=r.ReadInt32();
            if(pe<64 || pe>f.Length-24)return false;f.Position=pe;return r.ReadUInt32()==0x4550 && r.ReadUInt16()==0x8664;
        }}catch{return false;}
    }
    static GameManifest ReadGameManifest(string folder) {try{return Json.Deserialize<GameManifest>(File.ReadAllText(Path.Combine(folder,"OptiScaler-Tools.install.json")));}catch{return null;} }
    static bool Owned(string dir,string name,GameManifest previous) {
        if(!File.Exists(Path.Combine(dir,name)))return true;
        if(previous!=null && previous.files!=null && previous.files.ContainsKey(name) && Hash(Path.Combine(dir,name))==previous.files[name])return true;
        // Older official OptiScaler installs can be recognized from their PE version resource.
        try{return FileVersionInfo.GetVersionInfo(Path.Combine(dir,name)).OriginalFilename=="OptiScaler.dll";}catch{return false;}
    }
    static string SelectProxy(string folder,string requested,GameManifest previous) {
        string loader=Path.Combine(SourceRoot,"asi_loader","asiloader.dll");
        foreach(string choice in requested=="auto"?new[]{"dxgi","asi","winmm"}:new[]{requested}) {
            string file=choice=="dxgi"?"dxgi.dll":choice=="asi"?"OptiScaler.asi":choice=="winmm"?"winmm.dll":null;
            if(file==null)throw new AppError("E_PROXY",51,"Invalid proxy: "+choice);
            if(!Owned(folder,file,previous)) {WriteEvent("WARN","PROXY_CONFLICT",file+" belongs to the game or another mod; keeping it.",null);continue;}
            if(choice=="asi") {
                if(!File.Exists(loader) || !IsAmd64(loader)) {WriteEvent("WARN","ASI_LOADER_MISSING","The x64 ASI loader is missing or invalid at source/asi_loader/asiloader.dll.",null);continue;}
                if(File.Exists(Path.Combine(folder,"version.dll")) && !Owned(folder,"version.dll",previous) && Hash(Path.Combine(folder,"version.dll"))!=Hash(loader)) {WriteEvent("WARN","ASI_LOADER_CONFLICT","Keeping the existing version.dll and trying the next option.",null);continue;}
            }
            return choice;
        }
        throw new AppError("E_PROXY_CONFLICT",51,"No requested proxy can be installed without replacing another mod. Choose another mode or review dxgi.dll, OptiScaler.asi, and version.dll.");
    }
    static IEnumerable<string> SafeFiles(string dir) {
        foreach(string file in Directory.GetFiles(dir)) {
            if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)throw new AppError("E_PATH",50,"The package contains a link: "+file);yield return file;
        }
        foreach(string child in Directory.GetDirectories(dir)) {
            if((File.GetAttributes(child)&FileAttributes.ReparsePoint)!=0)throw new AppError("E_PATH",50,"The package contains a link: "+child);
            foreach(string file in SafeFiles(child))yield return file;
        }
    }
    static string Contained(string dir,string relative) {
        string root=Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        string path=Path.GetFullPath(Path.Combine(root,relative));
        if(!path.StartsWith(root,StringComparison.OrdinalIgnoreCase))throw new AppError("E_PATH",50,"Path is outside the destination: "+relative);
        string parent=Path.GetDirectoryName(path);
        while(parent.Length>=root.TrimEnd(Path.DirectorySeparatorChar).Length) {
            if(Directory.Exists(parent) && (File.GetAttributes(parent)&FileAttributes.ReparsePoint)!=0)throw new AppError("E_PATH_LINK",50,"Linked directories and junctions are not supported: "+parent);
            parent=Path.GetDirectoryName(parent);if(parent==null)break;
        }
        if(File.Exists(path) && (File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new AppError("E_PATH_LINK",50,"Linked files are not supported: "+path);
        return path;
    }
    static string VersionFromPackage(string package,string key) {
        try{var m=Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(package,"instalacao.json")));if(m.ContainsKey(key))return Convert.ToString(m[key]);}catch{}
        if(key=="streamline")try{return Json.Deserialize<Manifest>(File.ReadAllText(Path.Combine(package,"OptiScaler","streamline",".manifest.json"))).tag;}catch{}
        return "unknown";
    }
    static void StageLocalSources(string staging) {
        string opti=Path.Combine(staging,"OptiScaler");Directory.CreateDirectory(opti);
        string enabler=Path.Combine(SourceRoot,"dlssenabler","version.dll");
        if(File.Exists(enabler))File.Copy(enabler,Path.Combine(opti,"dlss-enabler-headless.dll"),true);
        string amd=Path.Combine(SourceRoot,"amd_files");if(Directory.Exists(amd))foreach(string file in Directory.GetFiles(amd,"*.dll"))File.Copy(file,Path.Combine(opti,Path.GetFileName(file)),true);
        if(Dlls(StreamlineRoot)) {
            string manifest=Path.Combine(StreamlineRoot,".manifest.json");
            if(File.Exists(manifest)) {
                Manifest m;try{m=Json.Deserialize<Manifest>(File.ReadAllText(manifest));}catch(Exception e){throw new AppError("E_SOURCE_MANIFEST",53,"Invalid Streamline manifest.",e);}
                if(!VerifyManifest(StreamlineRoot,manifest,m.asset))throw new AppError("E_SOURCE_HASH",53,"The DLLs in source/streamline have changed. Update Streamline before installing.");
            } else WriteEvent("WARN","W_STREAMLINE_LOCAL","Using local Streamline files without a version or integrity manifest.",null);
            string dest=Path.Combine(opti,"streamline");if(Directory.Exists(dest))Directory.Delete(dest,true);CopyTree(StreamlineRoot,dest);SessionStream=ReadStreamlineTag();
        }
        WriteEvent("INFO","LOCAL_SOURCES","Local source files were applied to the installation package.",new{streamline=SessionStream});
    }
    static bool PatcherSupports(string code,string executable) {
        string name=Path.GetFileName(executable).ToLowerInvariant();
        // Same positive name matching as setup_windows.bat; ignore comments and macro definitions.
        string clean=Regex.Replace(code,@"/\*[\s\S]*?\*/|//[^\r\n]*","");
        clean=Regex.Replace(clean,@"(?m)^\s*#define[^\r\n]*","");
        foreach(System.Text.RegularExpressions.Match m in Regex.Matches(clean,@"CHECK_UE\s*\(\s*([a-zA-Z0-9_]+)\s*\)"))
            if(name==(m.Groups[1].Value+"-Win64-Shipping.exe").ToLowerInvariant() || name==(m.Groups[1].Value+"-WinGDK-Shipping.exe").ToLowerInvariant())return true;
        foreach(System.Text.RegularExpressions.Match m in Regex.Matches(clean,"exeName\\s*==\\s*(?:L)?[\"']([^\"']+)[\"']"))if(name==m.Groups[1].Value.ToLowerInvariant())return true;
        return false;
    }
    static async Task<string> AddPatcher(string executable,string staging,string mode) {
        string old=Path.Combine(staging,"OptiScaler","plugins","OptiPatcher.asi");if(File.Exists(old))File.Delete(old);
        if(mode=="off") {WriteEvent("INFO","PATCHER_DISABLED","OptiPatcher is disabled in the configuration.",null);return "disabled";}
        if(mode!="auto")throw new AppError("E_CONFIG",10,"OptiPatcher: use auto or off.");
        try {
            string code=await Retry(async delegate {
                using(var cts=new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(25)))
                using(var r=await Http.GetAsync("https://raw.githubusercontent.com/optiscaler/OptiPatcher/main/OptiPatcher/dllmain.cpp",cts.Token)) {r.EnsureSuccessStatusCode();return await r.Content.ReadAsStringAsync();}
            });
            if(!PatcherSupports(code,executable)) {WriteEvent("INFO","PATCHER_UNSUPPORTED","The selected executable has no positive official support match. OptiPatcher was skipped.",new{executable=Path.GetFileName(executable)});return "unsupported";}
            string target=Path.Combine(staging,"OptiScaler","plugins","OptiPatcher.asi");Directory.CreateDirectory(Path.GetDirectoryName(target));
            await Retry(async delegate {
                using(var r=await Http.GetAsync("https://github.com/optiscaler/OptiPatcher/releases/download/rolling/OptiPatcher.asi")) {
                    r.EnsureSuccessStatusCode();using(var f=File.Create(target))await r.Content.CopyToAsync(f);
                }
                if(!IsAmd64(target))throw new AppError("E_PATCHER_BINARY",61,"The downloaded OptiPatcher is not a valid x64 PE file.");return true;
            });
            var ini=new IniDocument(Path.Combine(staging,"OptiScaler.ini"));ini.Set("Plugins","LoadAsiPlugins","true");ini.Save(false);
            WriteEvent("INFO","PATCHER_READY","OptiPatcher rolling installed; LoadAsiPlugins=true.",new{sha256=Hash(target)});return "installed-rolling";
        } catch(Exception e) {
            string target=Path.Combine(staging,"OptiScaler","plugins","OptiPatcher.asi");if(File.Exists(target))File.Delete(target);
            WriteEvent("WARN","W_PATCHER_UNAVAILABLE","Could not verify or download OptiPatcher. The main installation will continue.",new{error=DescribeException(e),code=ErrorCode(e)});return "unavailable";
        }
    }
    sealed class FileTransaction : IDisposable {
        readonly string destination,backup;readonly List<string> touched=new List<string>();readonly HashSet<string> existed=new HashSet<string>(StringComparer.OrdinalIgnoreCase);bool committed;
        internal FileTransaction(string dir,string backupFolder) {destination=dir;backup=backupFolder;Directory.CreateDirectory(backup);}
        internal void Put(string source,string relative) {
            string target=Contained(destination,relative);Backup(relative,target);Directory.CreateDirectory(Path.GetDirectoryName(target));File.Copy(source,target,true);
            if(Hash(source)!=Hash(target))throw new AppError("E_COPY_VERIFY",53,"Hash mismatch after copying: "+relative);
        }
        internal void Remove(string relative) {string target=Contained(destination,relative);if(File.Exists(target)){Backup(relative,target);File.Delete(target);}}
        void Backup(string relative,string target) {
            if(touched.Contains(relative,StringComparer.OrdinalIgnoreCase))return;
            if(File.Exists(target)) {string path=Contained(backup,relative);Directory.CreateDirectory(Path.GetDirectoryName(path));File.Copy(target,path,true);existed.Add(relative);}
            touched.Add(relative);
        }
        internal void Commit() {File.WriteAllText(Path.Combine(backup,"transaction.json"),Json.Serialize(new{destination=destination,files=touched,originalFiles=existed,committed=true}));committed=true;}
        public void Dispose() {
            if(committed)return;
            var errors=new List<string>();
            foreach(string relative in touched.AsEnumerable().Reverse()) {
                try {string target=Contained(destination,relative);if(existed.Contains(relative))File.Copy(Contained(backup,relative),target,true);else if(File.Exists(target))File.Delete(target);}
                catch(Exception e){errors.Add(relative+": "+DescribeException(e));}
            }
            WriteEvent(errors.Count==0?"WARN":"ERROR",errors.Count==0?"ROLLBACK_OK":"E_ROLLBACK","Restoring files after an installation failure.",new{backup=backup,errors=errors});
            if(errors.Count>0) File.WriteAllLines(Path.Combine(backup,"ROLLBACK_ERRORS.txt"),errors);
        }
    }
    static async Task InstallGame(string executable,string package,string requested,string patcherMode) {
        if(string.IsNullOrWhiteSpace(executable) || !File.Exists(executable) || !executable.EndsWith(".exe",StringComparison.OrdinalIgnoreCase) || !IsAmd64(executable))throw new AppError("E_GAME_EXE",50,"Choose the actual Windows x64 game executable. Unreal games usually keep it in Binaries/Win64.");
        if(string.IsNullOrEmpty(package) || !Directory.Exists(package) || !File.Exists(Path.Combine(package,"OptiScaler.dll")) || !File.Exists(Path.Combine(package,"OptiScaler.ini")))throw new AppError("E_PACKAGE",30,"Prepare a complete package before installing it into a game.");
        executable=Path.GetFullPath(executable);package=Path.GetFullPath(package);string game=Path.GetDirectoryName(executable);
        string packagePrefix=package.TrimEnd('\\')+"\\";
        if(game.Equals(Path.GetFullPath(Root).TrimEnd('\\'),StringComparison.OrdinalIgnoreCase) || game.StartsWith(packagePrefix,StringComparison.OrdinalIgnoreCase) || game.Equals(package,StringComparison.OrdinalIgnoreCase))throw new AppError("E_GAME_PATH",50,"The game folder must be different from the application and package folders.");
        var previous=ReadGameManifest(game);string proxy=SelectProxy(game,requested,previous);
        string name=proxy=="dxgi"?"dxgi.dll":proxy=="asi"?"OptiScaler.asi":"winmm.dll";
        string id=Guid.NewGuid().ToString("N"),work=Path.Combine(DataRoot,"game-"+id),staging=Path.Combine(work,"staging"),backup=Path.Combine(BuildsRoot,".backups",DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+id.Substring(0,8));
        SessionOpti=VersionFromPackage(package,"optiscaler");SessionStream=VersionFromPackage(package,"streamline");
        WriteEvent("INFO","GAME_START","Installing OptiScaler "+SessionOpti+" / Streamline "+SessionStream+" as "+name,new{executable=executable,package=package,proxy=proxy});
        try {
            Directory.CreateDirectory(staging);
            foreach(string file in SafeFiles(package)) {
                string relative=file.Substring(package.TrimEnd('\\').Length+1);
                if(relative.Equals("instalacao.json",StringComparison.OrdinalIgnoreCase) || relative.EndsWith(".bak",StringComparison.OrdinalIgnoreCase) || relative.StartsWith("setup_",StringComparison.OrdinalIgnoreCase))continue;
                string dest=Contained(staging,relative);Directory.CreateDirectory(Path.GetDirectoryName(dest));File.Copy(file,dest,true);
            }
            File.Move(Path.Combine(staging,"OptiScaler.dll"),Path.Combine(staging,name));
            StageLocalSources(staging);
            if(proxy=="asi")File.Copy(Path.Combine(SourceRoot,"asi_loader","asiloader.dll"),Path.Combine(staging,"version.dll"));
            string patcher=await AddPatcher(executable,staging,patcherMode);
            var manifest=new GameManifest {executable=executable,optiscaler=SessionOpti,streamline=SessionStream,proxy=proxy,patcher=patcher,backup=backup,files=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)};
            foreach(string file in SafeFiles(staging))manifest.files[file.Substring(staging.Length+1)]=Hash(file);
            // Prevent overwriting executable files or unrelated existing proxy names before any write.
            foreach(string relative in manifest.files.Keys) {
                string target=Contained(game,relative);
                if(target.Equals(executable,StringComparison.OrdinalIgnoreCase))throw new AppError("E_GAME_OVERWRITE",50,"The package would replace the selected game executable.");
            }
            string manifestFile=Path.Combine(work,"OptiScaler-Tools.install.json");File.WriteAllText(manifestFile,Json.Serialize(manifest));
            using(var gameLock=new FileStream(Path.Combine(game,".optiscaler-tools.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
            using(var transaction=new FileTransaction(game,backup)) {
                // Only remove old proxy/loader files whose hashes still match our previous installation.
                if(previous!=null && previous.files!=null)foreach(string old in new[]{"dxgi.dll","OptiScaler.asi","winmm.dll","version.dll"})
                    if(!manifest.files.ContainsKey(old) && previous.files.ContainsKey(old) && File.Exists(Path.Combine(game,old)) && Hash(Path.Combine(game,old))==previous.files[old])transaction.Remove(old);
                foreach(string relative in manifest.files.Keys)transaction.Put(Contained(staging,relative),relative);
                transaction.Put(manifestFile,"OptiScaler-Tools.install.json");transaction.Commit();
            }
            WriteEvent("INFO","GAME_SUCCESS","Installation completed and files verified. Proxy: "+name+"; OptiPatcher: "+patcher,new{game=executable,optiscaler=SessionOpti,streamline=SessionStream,proxy=proxy,patcher=patcher,backup=backup});
            Log("The game was not launched; runtime compatibility has not been verified.");
        } finally {if(Directory.Exists(work))Directory.Delete(work,true);}
    }
    static string QuoteArgument(string value) {
        if(value==null)value="";var text=new StringBuilder("\"");int slash=0;
        foreach(char c in value) {if(c=='\\'){slash++;continue;}if(c=='\"'){text.Append('\\',slash*2+1);text.Append(c);}else{text.Append('\\',slash);text.Append(c);}slash=0;}
        text.Append('\\',slash*2);text.Append('"');return text.ToString();
    }
    static void RunSetupProcess(string executable,string package,string proxy,string patcher) {
        string setup=Path.Combine(AppRoot,"setup_optiscaler.exe");if(!File.Exists(setup))throw new AppError("E_SETUP_MISSING",60,"setup_optiscaler.exe was not found beside the application.");
        var args=new[]{"--root",Root,"--game",executable,"--package",package,"--proxy",proxy,"--patcher",patcher};
        var start=new ProcessStartInfo(setup,string.Join(" ",args.Select(QuoteArgument))){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=AppRoot};
        WriteEvent("INFO","SETUP_START","Running setup_optiscaler.exe automatically.",new{executable=executable,proxy=proxy});
        using(var process=Process.Start(start)) {
            process.OutputDataReceived+=(s,e)=>{if(e.Data!=null)WriteEvent(e.Data.Contains("[ERROR]")?"ERROR":e.Data.Contains("[WARN]")?"WARN":"INFO","SETUP_EVENT",e.Data,null);};process.ErrorDataReceived+=(s,e)=>{if(e.Data!=null)WriteEvent("ERROR","SETUP_STDERR",e.Data,null);};
            process.BeginOutputReadLine();process.BeginErrorReadLine();process.WaitForExit();
            WriteEvent(process.ExitCode==0?"INFO":"ERROR","SETUP_EXIT","setup_optiscaler.exe exited with code "+process.ExitCode,new{exitCode=process.ExitCode});
            if(process.ExitCode!=0)throw new AppError("E_SETUP",process.ExitCode,"Setup returned code "+process.ExitCode+". Check the setup log for the cause and rollback result.");
        }
    }
}
