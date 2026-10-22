using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

// Windows .NET Framework 4.8: no PowerShell, SDK or runtime download required.
internal static partial class Program
{
    static string Root = AppDomain.CurrentDomain.BaseDirectory;
    static readonly string AppRoot = AppDomain.CurrentDomain.BaseDirectory;
    static string SourceRoot { get { return Path.Combine(Root,"source"); } }
    static string DataRoot { get { return Path.Combine(SourceRoot,".cache"); } }
    static string BuildsRoot { get { return Path.Combine(Root,"output"); } }
    static string DownloadsRoot { get { return Path.Combine(DataRoot,"downloads"); } }
    static string StreamlineRoot { get { return Path.Combine(SourceRoot,"streamline"); } }
    static string LogsRoot { get { return Path.Combine(BuildsRoot,".logs"); } }
    static string LogPath { get { return Path.Combine(LogsRoot,TestMode?"tests.log":(SessionId??"application")+".log"); } }
    static AppSettings Settings = new AppSettings();
    static Action<string> LogReceived;
    static bool NoPause, NoOpen, Force, TestMode;
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
    static HttpClient Http = CreateClient();
    static HttpClient CreateClient() {
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        ServicePointManager.DefaultConnectionLimit = 16;
        var h = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.None };
        var c = new HttpClient(h) { Timeout = TimeSpan.FromMinutes(15) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("OptiScaler-Tools/1.0");
        return c;
    }
    [STAThread]
    static int Main(string[] args) {
        UseEnglishCulture();
        if(args.Contains("--setup-worker")){AttachCli();return SetupMain(args);}
        NoPause = args.Contains("--no-pause") || Console.IsInputRedirected;
        NoOpen = args.Contains("--no-open"); Force = args.Contains("--force");
        TestMode = args.Contains("--self-test");
        bool gui=args.Length==0 || args.Contains("--gui");
        try {
            if(!TestMode && !args.Contains("--ui-test")) Settings=AppSettings.Load(Path.Combine(AppRoot,"OptiScaler-Tools.toml"));
            NoOpen=args.Contains("--no-open") || !Settings.Bool("app.open_output");
            gui=args.Contains("--gui") || (args.Length==0 && Settings.Bool("app.gui"));
            if(!gui) AttachCli();
            if(!gui && args.Length==0) NoPause=false;
            for(int i=0;i<args.Length;i++) if(args[i]=="--dest") {
                if(++i==args.Length) throw new Exception("Missing path after --dest.");
                Root=Path.GetFullPath(args[i]);
            }
            if(args.Contains("--help")) { Console.WriteLine("OptiScaler-Tools [--gui|--install|--streamline|--clear-streamline|--update-all|--games|--menu|--self-test|--organize] [--game GAME.exe] [--proxy auto|dxgi|asi|winmm] [--dest PATH] [--force] [--no-open] [--no-pause]\nWithout arguments, follows app.gui in OptiScaler-Tools.toml. --install prepares the latest nightly in CLI mode.\n--game installs the local package beside the selected executable. --games lists registered installations. --update-all prepares the latest nightly and updates every registered game, preserving game INI settings. Logs: output/.logs."); return 0; }
            if(args.Contains("--self-test")) { SelfTest(); return 0; }
            if(args.Contains("--ui-test")) { UiTest(); return 0; }
            if(gui) { RunWindow(); return 0; }
            Directory.CreateDirectory(Root);
            if(args.Contains("--update-all")){
                BeginSession("update-all");UpdateAllGames().GetAwaiter().GetResult();EndSession(true,null);Pause();return 0;
            }
            // A file lock is scoped to the destination, including across processes.
            using(var gate = AcquireLock()) {
                Organize();
                BeginSession("cli");
                Log("=== OptiScaler Tools ===");
                if(args.Contains("--organize")) Log("Workspace organized.");
                else if(args.Contains("--games")){DiscoverGames();foreach(var game in ReadLibrary().games)Console.WriteLine(game.Name+" | "+game.OptiScaler+" | "+game.Streamline+" | "+game.Status+" | "+game.Executable);}
                else if(args.Contains("--game")) {
                    string exe=Argument(args,"--game",Settings.Get("game.executable"));
                    InstallGame(exe,LatestBuild(),Argument(args,"--proxy",Settings.Get("game.proxy")),Settings.Get("game.optipatcher")).GetAwaiter().GetResult();
                }
                else if(args.Contains("--menu")) Menu();
                else if(args.Contains("--streamline")) { Open(Streamline().GetAwaiter().GetResult()); }
                else if(args.Contains("--clear-streamline")) ClearStreamline();
                else Install().GetAwaiter().GetResult();
                EndSession(true,null);
            }
            Pause(); return 0;
        } catch(Exception e) {
            Console.ForegroundColor=ConsoleColor.Red; Console.WriteLine("ERROR: "+DescribeException(e)); Console.ResetColor();
            try { EndSession(false,e); } catch { }
            if(gui) System.Windows.MessageBox.Show(DescribeException(e),"OptiScaler Tools",System.Windows.MessageBoxButton.OK,System.Windows.MessageBoxImage.Error);
            Pause(); return ExitCode(e);
        }
    }
    static void Pause() { if(!NoPause) { Console.WriteLine("Press ENTER to close."); Console.ReadLine(); } }
    static void Log(string s) { WriteEvent(s.StartsWith("ERROR")?"ERROR":s.StartsWith("WARNING")?"WARN":"INFO","EVENT",s,null); }
    static FileStream AcquireLock() { Directory.CreateDirectory(DataRoot); return new FileStream(Path.Combine(DataRoot,".optiscaler.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None); }
    static void Open(string path) { if(!NoOpen) Process.Start(new ProcessStartInfo(path) { UseShellExecute=true }); }
    static void Menu() {
        while(true) {
            Console.WriteLine("\n[1] Update OptiScaler nightly\n[2] Update Streamline\n[3] Clear Streamline cache\n[4] Exit");
            string s=Console.ReadLine(); if(s==null || s=="4") return;
            try { if(s=="1") Install().GetAwaiter().GetResult(); else if(s=="2") Open(Streamline().GetAwaiter().GetResult()); else if(s=="3") ClearStreamline(); }
            catch(Exception e) { Log("ERROR: "+DescribeException(e)); }
        }
    }
    static string Child(string parent,string name) {
        if(string.IsNullOrWhiteSpace(name) || name=="." || name==".." || name.IndexOfAny(Path.GetInvalidFileNameChars())>=0 || name.EndsWith(".") || name.EndsWith(" ")) throw new Exception("Invalid remote file name: "+name);
        return Path.Combine(parent,name);
    }
    static async Task<T> Retry<T>(Func<Task<T>> action) {
        for(int n=1;;n++) {
            try { return await action(); }
            catch(Exception e) { if(n==4) throw; Log("Attempt "+n+" failed: "+DescribeException(e)); }
            await Task.Delay(n*1000);
        }
    }
    internal sealed class Asset { public long id {get;set;} public string name {get;set;} public long size {get;set;} public string digest {get;set;} public string browser_download_url {get;set;} }
    internal sealed class Release { public string tag_name {get;set;} public bool draft {get;set;} public Asset[] assets {get;set;} }
    static async Task<Release> ReleaseFor(string repo) {
        Log("Checking GitHub: "+repo);
        string body=await Retry(async delegate {
            using(var cts=new CancellationTokenSource(TimeSpan.FromSeconds(30)))
            using(var r=await Http.GetAsync("https://api.github.com/repos/"+repo+"/releases?per_page=10",cts.Token)) {
                if((int)r.StatusCode==403 || (int)r.StatusCode==429) throw new Exception("GitHub rate-limited these requests. Try again later.");
                r.EnsureSuccessStatusCode(); return await r.Content.ReadAsStringAsync();
            }
        });
        var releases=Json.Deserialize<Release[]>(body);
        var result=releases.FirstOrDefault(x=>!x.draft && x.assets!=null && x.assets.Any(a=>IsArchive(a.name)));
        if(result==null) throw new Exception("No release with a downloadable package was found: "+repo);
        Log("Version: "+result.tag_name); return result;
    }
    static bool IsArchive(string s) { return Regex.IsMatch(s,@"\.(zip|7z)$",RegexOptions.IgnoreCase); }
    static Asset Select(Release r,bool zipOnly) {
        var a=r.assets.Where(x=>IsArchive(x.name) && (!zipOnly || x.name.EndsWith(".zip",StringComparison.OrdinalIgnoreCase)))
            .Where(x=>!Regex.IsMatch(x.name,@"aarch64|arm64|armv\d|(^|[-_.])arm([-_.]|$)|(^|[-_.])(x86(?![_-]64)|win32|i[3-6]86)([-_.]|$)|linux|macos|darwin",RegexOptions.IgnoreCase))
            .Where(x=>!Regex.IsMatch(x.name,@"symbol|pdb|source|debug",RegexOptions.IgnoreCase))
            .OrderBy(x=>Regex.IsMatch(x.name,@"x64|x86[_-]64|amd64|win64",RegexOptions.IgnoreCase)?0:1)
            .ThenBy(x=>x.name.EndsWith(".zip",StringComparison.OrdinalIgnoreCase)?0:1).FirstOrDefault();
        if(a==null) throw new Exception("No compatible Windows x64 package is available. Assets: "+string.Join(", ",r.assets.Select(x=>x.name))); return a;
    }
    static string Hash(string path) { using(var sha=SHA256.Create()) using(var f=File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(f)).Replace("-", "").ToLowerInvariant(); }
    static bool Valid(string path,Asset a) {
        if(!File.Exists(path) || new FileInfo(path).Length!=a.size || a.size<=0) return false;
        string digest=a.digest;
        return string.IsNullOrEmpty(digest) || !digest.StartsWith("sha256:",StringComparison.OrdinalIgnoreCase) || Hash(path).Equals(digest.Substring(7),StringComparison.OrdinalIgnoreCase);
    }
    static async Task<string> Download(Asset a,string dir) {
        Directory.CreateDirectory(dir); string path=Child(dir,a.name);
        if(!Force && Valid(path,a)) { Log("Verified download already available: "+a.name); return path; }
        await Retry(async delegate {
            string tmp=path+".partial";
            try {
                Log("Downloading "+a.name+" ("+(a.size/1048576.0).ToString("F1")+" MB)...");
                bool segmented=false;
                if(a.size>=8*1024*1024) {
                    try { await Segments(a,tmp); segmented=true; }
                    catch(Exception e) { Log("Parallel download unavailable; falling back to one connection: "+DescribeException(e)); }
                }
                if(!segmented) {
                    using(var r=await Http.GetAsync(a.browser_download_url,HttpCompletionOption.ResponseHeadersRead)) {
                        r.EnsureSuccessStatusCode(); using(var f=File.Create(tmp)) await r.Content.CopyToAsync(f);
                    }
                }
                if(!Valid(tmp,a)) throw new AppError("E_DOWNLOAD_HASH",21,"The downloaded file has an incorrect size or SHA-256.");
                if(File.Exists(path)) File.Delete(path); File.Move(tmp,path); return true;
            } finally { if(File.Exists(tmp)) File.Delete(tmp); }
        });
        return path;
    }
    static async Task Segments(Asset a,string tmp) {
        const int count=8; long chunk=(a.size+count-1)/count;
        string dir=tmp+"."+Guid.NewGuid().ToString("N"); Directory.CreateDirectory(dir);
        try {
            var jobs=Enumerable.Range(0,count).Select(async i=> {
                long start=i*chunk,end=Math.Min(a.size-1,start+chunk-1);
                using(var q=new HttpRequestMessage(HttpMethod.Get,a.browser_download_url)) {
                    q.Headers.Range=new RangeHeaderValue(start,end);
                    using(var r=await Http.SendAsync(q,HttpCompletionOption.ResponseHeadersRead)) {
                        var cr=r.Content.Headers.ContentRange;
                        if(r.StatusCode!=HttpStatusCode.PartialContent || cr==null || cr.From!=start || cr.To!=end || cr.Length!=a.size) throw new Exception("The server did not confirm the requested HTTP range.");
                        string part=Path.Combine(dir,i.ToString());
                        using(var f=File.Create(part)) await r.Content.CopyToAsync(f);
                        if(new FileInfo(part).Length!=end-start+1) throw new Exception("Incomplete download segment.");
                    }
                }
            });
            await Task.WhenAll(jobs);
            using(var output=File.Create(tmp)) for(int i=0;i<count;i++) using(var input=File.OpenRead(Path.Combine(dir,i.ToString()))) input.CopyTo(output);
        } finally { Directory.Delete(dir,true); }
    }
    static void Extract(string archive,string target) {
        Directory.CreateDirectory(target); Log("Extracting "+Path.GetFileName(archive));
        if(archive.EndsWith(".zip",StringComparison.OrdinalIgnoreCase)) {
            using(var zip=ZipFile.OpenRead(archive)) foreach(var e in zip.Entries) {
                string dst=Path.GetFullPath(Path.Combine(target,e.FullName));
                if(!dst.StartsWith(Path.GetFullPath(target)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new Exception("The ZIP contains an unsafe path.");
                if(string.IsNullOrEmpty(e.Name)) { Directory.CreateDirectory(dst); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(dst)); e.ExtractToFile(dst,true);
            }
            return;
        }
        // Windows 11 tar/libarchive supports 7z; also retain NanaZip/7-Zip support.
        var candidates=new List<string>();
        foreach(string folder in (Environment.GetEnvironmentVariable("PATH")??"").Split(';')) if(!string.IsNullOrWhiteSpace(folder)) candidates.Add(Path.Combine(folder.Trim('"'),"7z.exe"));
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"7-Zip","7z.exe"));
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"tar.exe"));
        foreach(string exe in candidates.Distinct(StringComparer.OrdinalIgnoreCase)) {
            if(!File.Exists(exe)) continue;
            string arguments=Path.GetFileName(exe).Equals("tar.exe",StringComparison.OrdinalIgnoreCase)? "-xf \""+archive+"\" -C \""+target+"\"" : "x \""+archive+"\" -o\""+target+"\" -y";
            try {
                using(var p=Process.Start(new ProcessStartInfo(exe,arguments) { UseShellExecute=false,CreateNoWindow=true })) { p.WaitForExit(); if(p.ExitCode==0) return; }
            } catch(Exception e) { Log("Extractor unavailable: "+DescribeException(e)); }
        }
        throw new AppError("E_EXTRACT",30,"Could not extract the 7z archive. Install NanaZip or 7-Zip. ZIP files need no external extractor.");
    }
    static void CopyTree(string from,string to) {
        Directory.CreateDirectory(to);
        foreach(string f in Directory.GetFiles(from)) File.Copy(f,Path.Combine(to,Path.GetFileName(f)),true);
        foreach(string d in Directory.GetDirectories(from)) CopyTree(d,Path.Combine(to,Path.GetFileName(d)));
    }
    static bool Dlls(string dir) { return Directory.Exists(dir) && Directory.GetFiles(dir,"*.dll").Length>0; }
    static void ClearStreamline() {
        foreach(string path in new[]{StreamlineRoot,Path.Combine(DownloadsRoot,"streamline")}) {
            string p=Path.GetFullPath(path);
            if(!p.StartsWith(Path.GetFullPath(Root).TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase)) throw new Exception("Invalid destination.");
            if(Directory.Exists(p)) Directory.Delete(p,true);
        }
        Log("Streamline cache cleared. Existing packages were kept.");
    }
    static async Task<string> Streamline() {
        var r=await ReleaseFor("NVIDIA-RTX/Streamline"); var a=Select(r,true);
        string cache=StreamlineRoot, stamp=Path.Combine(cache,".manifest.json");
        if(!Force && VerifyManifest(cache,stamp,a.id)) {PruneDownloads(Path.Combine(DownloadsRoot,"streamline"));SessionStream=r.tag_name;Log("Streamline is up to date and verified: "+r.tag_name); return cache; }
        string zip=await Download(a,Path.Combine(DownloadsRoot,"streamline"));
        string work=Path.Combine(DataRoot,".streamline-"+Guid.NewGuid().ToString("N"));
        try {
            string ready=Path.Combine(work,"ready"); Directory.CreateDirectory(ready);
            // Extract only production x64 DLLs, not source, PDBs or debug builds.
            using(var package=ZipFile.OpenRead(zip)) {
                var entries=package.Entries.Where(e=>Regex.IsMatch(e.FullName.Replace('\\','/'),@"(^|/)bin/x64/[^/]+\.dll$",RegexOptions.IgnoreCase)).ToArray();
                var dirs=entries.Select(e=>e.FullName.Substring(0,e.FullName.Length-e.Name.Length)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                if(dirs.Length!=1) throw new Exception("Expected exactly one production bin/x64 folder; found: "+dirs.Length);
                foreach(var entry in entries) entry.ExtractToFile(Child(ready,entry.Name),true);
            }
            if(!Dlls(ready)) throw new Exception("The Streamline package contains no bin/x64 DLLs.");
            WriteManifest(ready,Path.Combine(ready,".manifest.json"),a.id,r.tag_name);
            Publish(ready,cache);PruneDownloads(Path.Combine(DownloadsRoot,"streamline"));SessionStream=r.tag_name;Log("Streamline updated: "+r.tag_name); return cache;
        } finally { if(Directory.Exists(work)) Directory.Delete(work,true); }
    }
    internal sealed class Manifest { public long asset {get;set;} public string tag {get;set;} public Dictionary<string,string> files {get;set;} }
    static void WriteManifest(string dir,string file,long id,string tag) {
        var m=new Manifest { asset=id,tag=tag,files=Directory.GetFiles(dir,"*.dll").ToDictionary(Path.GetFileName,Hash) };
        File.WriteAllText(file,Json.Serialize(m));
    }
    static bool VerifyManifest(string dir,string stamp,long id) {
        try {
            var m=Json.Deserialize<Manifest>(File.ReadAllText(stamp));
            return m.asset==id && m.files.Count>0 && Directory.GetFiles(dir,"*.dll").Length==m.files.Count && m.files.All(x=>File.Exists(Child(dir,x.Key)) && Hash(Child(dir,x.Key))==x.Value);
        } catch { return false; }
    }
    static void Publish(string staging,string destination) {
        string old=destination+".backup-"+Guid.NewGuid().ToString("N"); bool existed=Directory.Exists(destination);
        if(existed) Directory.Move(destination,old);
        try { Directory.Move(staging,destination); }
        catch { if(existed) Directory.Move(old,destination); throw; }
        if(existed) { try { Directory.Delete(old,true); } catch(Exception e) { Log("The previous backup was kept: "+old+" ("+DescribeException(e)+")"); } }
    }
    internal static void UpdateIni(string path) {
        string text=File.ReadAllText(path); string nl=text.Contains("\r\n")?"\r\n":"\n";
        foreach(var kv in new Dictionary<string,string>{{"FGInput","nvngxfg"},{"FGOutput","fsrfg"},{"FGNvngxReplacement","Combo"}}) {
            string pattern=@"(?im)^[ \t]*[;#]?[ \t]*"+kv.Key+@"[ \t]*=[^\r\n]*";
            if(Regex.IsMatch(text,pattern)) text=Regex.Replace(text,pattern,kv.Key+"="+kv.Value);
            else throw new AppError("E_INI_LAYOUT",40,"Key "+kv.Key+" is missing from the INI. The upstream format changed; the previous package was kept.");
        }
        File.WriteAllText(path,text,new UTF8Encoding(false));
    }
    static async Task Install() {
        var r=await ReleaseFor("optiscaler/OptiScaler-nightly"); var a=Select(r,false);
        SessionOpti=r.tag_name;
        string archive=await Download(a,Path.Combine(DownloadsRoot,"optiscaler")), destination=Child(BuildsRoot,r.tag_name);
        Directory.CreateDirectory(BuildsRoot);
        string work=Path.Combine(DataRoot,".build-"+Guid.NewGuid().ToString("N"));
        bool fallback=false;
        try {
            Extract(archive,work);
            string ini=Directory.GetFiles(work,"OptiScaler.ini",SearchOption.AllDirectories).OrderBy(x=>x.Split(Path.DirectorySeparatorChar).Length).FirstOrDefault();
            if(ini==null) throw new AppError("E_INI_MISSING",40,"OptiScaler.ini was not found in the package.");
            string build=Path.GetDirectoryName(ini), plugins=Path.Combine(build,"optiscaler"); Directory.CreateDirectory(plugins);
            string dll=Path.Combine(SourceRoot,"dlssenabler","version.dll"), dst=Path.Combine(plugins,"dlss-enabler-headless.dll");
            if(File.Exists(dll)) File.Copy(dll,dst,true);
            else throw new AppError("E_SOURCE_ENABLER",53,"DLSS Enabler was not found at source/dlssenabler/version.dll. Keep the source folder beside the application.");
            if(new FileInfo(dst).Length==0) throw new Exception("version.dll is empty.");
            string streamline;
            try { streamline=await Streamline(); }
            catch(Exception e) {
                Log("WARNING: Could not update Streamline: "+DescribeException(e));
                streamline=StreamlineRoot;
                if(!Dlls(streamline)) throw new Exception("No updated Streamline or local fallback is available. The previous package was kept.");
                string stamp=Path.Combine(streamline,".manifest.json");
                if(File.Exists(stamp)) {
                    Manifest existing;try{existing=Json.Deserialize<Manifest>(File.ReadAllText(stamp));}catch(Exception sourceError){throw new AppError("E_SOURCE_MANIFEST",53,"The local Streamline manifest is invalid.",sourceError);}
                    if(!VerifyManifest(streamline,stamp,existing.asset))throw new AppError("E_SOURCE_HASH",53,"The fallback Streamline files are corrupted. The previous package was kept.");
                }
                fallback=true; Log("WARNING: Using a local fallback; the latest version could not be confirmed.");
            }
            CopyTree(streamline,Path.Combine(plugins,"streamline"));
            string amd=Path.Combine(SourceRoot,"amd_files");
            if(Directory.Exists(amd)) foreach(string file in Directory.GetFiles(amd,"*.dll")) File.Copy(file,Path.Combine(plugins,Path.GetFileName(file)),true);
            File.Copy(ini,Path.Combine(plugins,"OptiScaler-Tools.defaults.ini"),true);
            UpdateIni(ini);
            ApplyProfile(ini,Settings);
            string streamVersion=ReadStreamlineTag();
            SessionStream=streamVersion;
            File.WriteAllText(Path.Combine(work,"instalacao.json"),Json.Serialize(new { optiscaler=r.tag_name,asset=a.id,streamline=streamVersion,streamlineFallback=fallback,versionSha256=Hash(dst),completed=DateTime.UtcNow.ToString("o") }));
            WriteEvent(fallback?"WARN":"INFO","PACKAGE_READY","OptiScaler "+r.tag_name+"; Streamline "+streamVersion,new {optiscaler=r.tag_name,streamline=streamVersion,fallback=fallback});
            Publish(work,destination);
            PruneDownloads(Path.Combine(DownloadsRoot,"optiscaler"),a.name);
            File.WriteAllText(Path.Combine(DataRoot,"ultima_versao_baixada.txt"),r.tag_name+Environment.NewLine);
            Log(fallback?"PACKAGE READY WITH WARNING: Using local Streamline; an update is pending.":"PACKAGE READY: OptiScaler, INI, Streamline, and DLSS Enabler verified.");
            Log(destination); Open(destination);
        } finally { if(Directory.Exists(work)) Directory.Delete(work,true); }
    }
    static void SelfTest() {
        string dir=Path.Combine(Root,".self-test-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try {
            var architectureRelease=new Release { assets=new[]{new Asset { name="streamline-sdk-v2.14.16-aarch64.zip" },new Asset { name="streamline-sdk-v2.14.16.zip" }} };
            Check(Select(architectureRelease,true).name=="streamline-sdk-v2.14.16.zip","Selects x64 when aarch64 appears first in the release");
            foreach(string arch in new[]{"aarch64","ARM64","armv8","x86","win32","linux-x64","macos"}) {
                bool refused=false; try { Select(new Release { assets=new[]{new Asset { name="streamline-sdk-"+arch+".zip" }} },true); } catch { refused=true; }
                Check(refused,"Never selects an incompatible package: "+arch);
            }
            Check(Select(new Release { assets=new[]{new Asset {name="streamline-sdk-x86_64.zip"}} },true).name.Contains("x86_64"),"Accepts the x86_64 architecture alias");
            string ini=Path.Combine(dir,"OptiScaler.ini");
            File.WriteAllText(ini,"[FrameGen]\r\n;FGInput=auto\r\nFGOutput=auto\r\nFGNvngxReplacement=auto\r\nOther=keep\r\n");
            UpdateIni(ini); string once=File.ReadAllText(ini); UpdateIni(ini);
            Check(once==File.ReadAllText(ini) && once.Contains("FGInput=nvngxfg") && once.Contains("FGOutput=fsrfg") && once.Contains("FGNvngxReplacement=Combo") && once.Contains("Other=keep"),"INI changes are idempotent");
            File.WriteAllText(ini,"[Unknown]\n"); bool rejected=false; try { UpdateIni(ini); } catch { rejected=true; } Check(rejected,"Rejects an INI with missing known keys");
            string dll=Path.Combine(dir,"sl.test.dll"); File.WriteAllText(dll,"fixture"); string stamp=Path.Combine(dir,"manifest.json"); WriteManifest(dir,stamp,1,"v1");
            Check(VerifyManifest(dir,stamp,1) && !VerifyManifest(dir,stamp,2),"Caches by asset identity"); File.WriteAllText(dll,"corrupt"); Check(!VerifyManifest(dir,stamp,1),"Detects a corrupted DLL");
            var asset=new Asset {size=new FileInfo(dll).Length,digest="sha256:"+Hash(dll)}; Check(Valid(dll,asset),"Accepts a valid SHA-256"); asset.digest="sha256:bad"; Check(!Valid(dll,asset),"Rejects an incorrect SHA-256");
            rejected=false; try { Child(dir,"../escape"); } catch { rejected=true; } Check(rejected,"Rejects an unsafe remote file name");
            string staging=Path.Combine(dir,"staging"),dest=Path.Combine(dir,"dest"); Directory.CreateDirectory(staging); Directory.CreateDirectory(dest); File.WriteAllText(Path.Combine(dest,"old"),"old"); File.WriteAllText(Path.Combine(staging,"new"),"new"); Publish(staging,dest); Check(File.Exists(Path.Combine(dest,"new")) && !File.Exists(Path.Combine(dest,"old")),"Publishes a package without stale files");
            Check(typeof(Program).Assembly.GetManifestResourceStream("version.dll")==null,"The executable does not duplicate the external DLSS Enabler DLL");
            string zip=Path.Combine(dir,"test.zip"), extracted=Path.Combine(dir,"unpacked");
            using(var z=ZipFile.Open(zip,ZipArchiveMode.Create)) { using(var w=new StreamWriter(z.CreateEntry("nested/file.txt").Open())) w.Write("ok"); }
            Extract(zip,extracted); Check(File.ReadAllText(Path.Combine(extracted,"nested","file.txt"))=="ok","ZIP extraction");
            string bad=Path.Combine(dir,"bad.zip"); using(var z=ZipFile.Open(bad,ZipArchiveMode.Create)) z.CreateEntry("../escape.txt");
            rejected=false; try { Extract(bad,Path.Combine(dir,"bad")); } catch { rejected=true; } Check(rejected,"Rejects ZIP path traversal");
            string tar=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"tar.exe");
            if(File.Exists(tar)) {
                string seven=Path.Combine(dir,"test.7z");
                using(var proc=Process.Start(new ProcessStartInfo(tar,"--format=7zip -cf \""+seven+"\" -C \""+extracted+"\" nested/file.txt") {UseShellExecute=false,CreateNoWindow=true})) {
                    proc.WaitForExit(); Check(proc.ExitCode==0,"Creates a 7z fixture with Windows libarchive");
                }
                string sevenDir=Path.Combine(dir,"seven"); Extract(seven,sevenDir);
                Check(File.ReadAllText(Path.Combine(sevenDir,"nested","file.txt"))=="ok","7z extraction with system extractors");
            }
            PipelineTest(Path.Combine(dir,"pipeline"));
            MigrationTest(Path.Combine(dir,"migration"));
            FeatureTests(Path.Combine(dir,"features"));
            LibraryTests(Path.Combine(dir,"library"));
            StorageTests(Path.Combine(dir,"storage"));
            Console.WriteLine("All tests passed.");
        } finally { Directory.Delete(dir,true); }
    }
    static byte[] FixtureZip(Dictionary<string,string> files) {
        using(var mem=new MemoryStream()) {
            using(var zip=new ZipArchive(mem,ZipArchiveMode.Create,true)) foreach(var kv in files) using(var w=new StreamWriter(zip.CreateEntry(kv.Key).Open())) w.Write(kv.Value);
            return mem.ToArray();
        }
    }
    sealed class FixtureHandler : HttpMessageHandler {
        public int StreamlineDownloads;
        public byte[] Opti=FixtureZip(new Dictionary<string,string>{{"OptiScaler.ini","[FrameGen]\nFGInput=auto\nFGOutput=auto\nFGNvngxReplacement=auto\nOther=keep\n"},{"OptiScaler.dll","fixture-opti"}});
        public byte[] Stream=FixtureZip(new Dictionary<string,string>{{"sdk/bin/x64/sl.test.dll","fixture-stream"},{"sdk/bin/x64/development/skip.dll","skip"},{"sdk/bin/x64/skip.pdb","skip"}});
        public byte[] Large=new byte[8*1024*1024+19];
        public bool IgnoreRange, StreamUnavailable;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage q,CancellationToken ct) {
            string url=q.RequestUri.AbsoluteUri; var r=new HttpResponseMessage(HttpStatusCode.OK);
            if(url.Contains("api.github.com")) {
                bool opti=url.Contains("OptiScaler-nightly"); byte[] data=opti?Opti:Stream;
                var correct=new Asset {id=opti?1:2,name=opti?"opti.zip":"stream.zip",size=data.Length,browser_download_url=opti?"https://fixture/opti":"https://fixture/stream"};
                var assets=opti?new[]{correct}:new[]{new Asset {id=99,name="streamline-sdk-v2.14.16-aarch64.zip",size=1,browser_download_url="https://fixture/wrong-architecture"},correct};
                r.Content=new StringContent(!opti && StreamUnavailable?"[]":Json.Serialize(new[]{new Release {tag_name=opti?"test-nightly":"test-streamline", assets=assets}}));
            } else {
                if(url.EndsWith("wrong-architecture")) throw new Exception("Test: attempted to download a forbidden ARM package.");
                byte[] data=url.EndsWith("opti")?Opti:url.EndsWith("stream")?Stream:Large;
                if(url.EndsWith("stream")) Interlocked.Increment(ref StreamlineDownloads);
                var range=q.Headers.Range;
                if(range!=null && !IgnoreRange) {
                    var item=range.Ranges.First(); long start=item.From.Value,end=item.To.Value;
                    byte[] part=new byte[(int)(end-start+1)]; Buffer.BlockCopy(data,(int)start,part,0,part.Length);
                    r.StatusCode=HttpStatusCode.PartialContent; r.Content=new ByteArrayContent(part); r.Content.Headers.ContentRange=new ContentRangeHeaderValue(start,end,data.Length);
                } else r.Content=new ByteArrayContent(data);
            }
            return Task.FromResult(r);
        }
    }
    static void PipelineTest(string dir) {
        string savedRoot=Root; bool savedOpen=NoOpen; HttpClient savedHttp=Http;
        var fixture=new FixtureHandler();
        try {
            Root=dir; NoOpen=true; Directory.CreateDirectory(Root); Http=new HttpClient(fixture); Http.DefaultRequestHeaders.UserAgent.ParseAdd("OptiScaler-Test/1.0");
            Directory.CreateDirectory(Path.Combine(SourceRoot,"dlssenabler"));File.WriteAllText(Path.Combine(SourceRoot,"dlssenabler","version.dll"),"fixture-enabler");
            Install().GetAwaiter().GetResult();
            string build=Path.Combine(BuildsRoot,"test-nightly"),p=Path.Combine(build,"optiscaler");
            Check(File.Exists(Path.Combine(p,"dlss-enabler-headless.dll")) && File.Exists(Path.Combine(p,"streamline","sl.test.dll")) && File.ReadAllText(Path.Combine(build,"OptiScaler.ini")).Contains("FGOutput=fsrfg"),"Complete installation with a simulated API");
            Check(!File.Exists(Path.Combine(p,"streamline","skip.dll")) && !File.Exists(Path.Combine(p,"streamline","skip.pdb")),"Copies only production bin/x64 DLLs");
            File.Delete(Path.Combine(p,"dlss-enabler-headless.dll")); Install().GetAwaiter().GetResult();
            Check(fixture.StreamlineDownloads==1 && File.Exists(Path.Combine(p,"dlss-enabler-headless.dll")),"Repairs the same nightly without downloading Streamline again");
            File.WriteAllText(Path.Combine(StreamlineRoot,"sl.test.dll"),"corrupt"); Install().GetAwaiter().GetResult();
            Check(File.ReadAllText(Path.Combine(StreamlineRoot,"sl.test.dll"))=="fixture-stream" && fixture.StreamlineDownloads==2,"Repairs corrupted DLLs by downloading the SDK again after archive cleanup");
            Directory.CreateDirectory(Path.Combine(SourceRoot,"dlssenabler")); File.WriteAllText(Path.Combine(SourceRoot,"dlssenabler","version.dll"),"local-override"); Install().GetAwaiter().GetResult();
            Check(File.ReadAllText(Path.Combine(p,"dlss-enabler-headless.dll"))=="local-override","The external version.dll takes priority");
            fixture.StreamUnavailable=true; Install().GetAwaiter().GetResult();
            Check(File.ReadAllText(Path.Combine(build,"instalacao.json")).Contains("\"streamlineFallback\":true"),"Reports the local fallback when the upstream request fails");
            byte[] savedOpti=fixture.Opti; fixture.Opti=FixtureZip(new Dictionary<string,string>{{"OptiScaler.ini","[Changed]\nUnknown=value\n"}});
            bool rejected=false; try { Install().GetAwaiter().GetResult(); } catch { rejected=true; }
            Check(rejected && File.ReadAllText(Path.Combine(build,"OptiScaler.ini")).Contains("FGOutput=fsrfg"),"Installation failure preserves the previous package"); fixture.Opti=savedOpti;
            var a=new Asset {name="large.zip",size=fixture.Large.Length,browser_download_url="https://fixture/large"};
            string large=Download(a,Root).GetAwaiter().GetResult(); Check(new FileInfo(large).Length==a.size,"Downloads in eight HTTP ranges");
            File.Delete(large); fixture.IgnoreRange=true; large=Download(a,Root).GetAwaiter().GetResult(); Check(new FileInfo(large).Length==a.size,"Falls back to one connection when the server ignores Range");
            ClearStreamline(); Check(!Directory.Exists(StreamlineRoot) && Directory.Exists(build),"Cache cleanup preserves prepared packages");
        } finally { Http.Dispose(); Http=savedHttp; Root=savedRoot; NoOpen=savedOpen; }
    }
    static void Check(bool value,string name) { if(!value) throw new Exception("Test failed: "+name); Console.WriteLine("OK: "+name); }
}
