using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

internal static partial class Program
{
    static byte[] PeFixture() {
        byte[] data=new byte[256];data[0]=0x4d;data[1]=0x5a;data[0x3c]=0x80;data[0x80]=0x50;data[0x81]=0x45;data[0x84]=0x64;data[0x85]=0x86;return data;
    }
    sealed class PatcherFixture:HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct) {
            var response=new HttpResponseMessage(HttpStatusCode.OK);
            if(request.RequestUri.AbsoluteUri.Contains("dllmain.cpp"))response.Content=new StringContent("// CHECK_UE(Fake)\nCHECK_UE(Supported)\nif (exeName == L\"Other.exe\") {}\n");
            else response.Content=new ByteArrayContent(PeFixture());return Task.FromResult(response);
        }
    }
    static void FeatureTests(string dir) {
        string originalRoot=Root;var originalHttp=Http;Directory.CreateDirectory(dir);
        try {
            Root=dir;
            Check(DescribeException(new UnauthorizedAccessException("Acesso negado")).StartsWith("Access was denied")&&DescribeException(new IOException("Arquivo bloqueado")).StartsWith("A file operation failed"),"Localized operating-system errors are presented in English with diagnostic codes");
            string toml=Path.Combine(dir,"test.toml");
            File.WriteAllText(toml,"# comment\n[app]\ngui = false\n[game]\nexecutable = 'C:\\Game #1\\Game.exe' # trailing\n[ini]\nfg_input = \"nvngxfg\"\n");
            var config=AppSettings.Load(toml);Check(!config.Bool("app.gui") && config.Get("game.executable")=="C:\\Game #1\\Game.exe","TOML: CLI mode and paths containing # are preserved");
            config.Values["game.proxy"]="asi";config.Save();Check(AppSettings.Load(toml).Get("game.proxy")=="asi" && File.ReadAllText(toml).Contains("# comment"),"TOML: saving and reloading preserves comments");
            File.WriteAllText(toml,"[app]\ngui = maybe\n");bool rejected=false;try{AppSettings.Load(toml);}catch(AppError){rejected=true;}Check(rejected,"Invalid TOML returns E_CONFIG");
            string ini=Path.Combine(dir,"section.ini");File.WriteAllText(ini,"[FrameGen]\nEnabled=auto\n[DLSS]\nEnabled=auto\n[Custom]\nUnknown=keep\n");
            var document=new IniDocument(ini);document.Set("FrameGen","Enabled","true");document.Set("FSR","Fsr4Model","2");ApplySpoofing(document,"streamline");document.Save(true);
            var read=new IniDocument(ini);Check(read.Get("FrameGen","Enabled")=="true" && read.Get("DLSS","Enabled")=="auto" && read.Get("Custom","Unknown")=="keep" && File.Exists(ini+".bak"),"INI editing changes only the correct section and creates a backup");
            Check(read.Get("Spoofing","Dxgi")=="false" && read.Get("Spoofing","StreamlineSpoofing")=="true","Partial spoofing enables Streamline without DXGI spoofing");
            ApplySpoofing(read,"full");read.Save(false);Check(new IniDocument(ini).Get("Spoofing","Registry")=="true","Full spoofing includes Registry and User32");
            string complete=Path.Combine(dir,"complete.ini");string original="Root=one\r\n[Custom]\r\n; Use a custom path\r\n; Paths can contain # and =\r\nPath = C:\\Game#1\\a=b.dll ; keep this note\r\n\r\n; Rectangle in pixels\r\nLeft=auto\r\nTop=auto\r\n\r\n; Literal text\r\nQuoted=\"a ; b # c\"\r\n[Other]\r\nPath=other\r\n";File.WriteAllText(complete,original);
            var full=new IniDocument(complete);var entries=full.Entries();Check(entries.Count==6&&entries.Single(x=>x.Key=="Top").Description=="Rectangle in pixels"&&entries.Single(x=>x.Section=="Custom"&&x.Key=="Path").Description.Contains("keep this note")&&full.Get("Custom","Quoted")=="\"a ; b # c\"","Every INI key is enumerated with grouped comments, inline help and quoted values");
            full.Save(false);Check(File.ReadAllText(complete)==original,"An untouched INI round-trips without changing comments, spacing or line endings");
            foreach(var entry in entries)full.Set(entry.Section,entry.Key,entry.Value=="auto"?"12":entry.Value);full.Set("Custom","Path",@"D:\Game#2\x=y.dll");full.Save(true);var allRead=new IniDocument(complete);
            Check(allRead.Get("","Root")=="one"&&allRead.Get("Custom","Top")=="12"&&allRead.Get("Custom","Path")==@"D:\Game#2\x=y.dll"&&allRead.Get("Other","Path")=="other"&&File.ReadAllText(complete).Contains(" ; keep this note")&&File.ReadAllText(complete+".bak")==original,"All-value editing preserves global keys, section identity, paths, inline comments and the backup");
            File.WriteAllText(complete,"[Custom]\nEmpty= ; original comment\n");full=new IniDocument(complete);full.Set("Custom","Empty","filled");full.Save(false);Check(new IniDocument(complete).Get("Custom","Empty")=="filled"&&File.ReadAllText(complete).Contains(" ; original comment"),"Editing an empty value keeps its inline comment separate from the new value");
            Organize();string loader=Path.Combine(SourceRoot,"asi_loader","asiloader.dll");File.WriteAllBytes(loader,PeFixture());
            string game=Path.Combine(dir,"games","test");Directory.CreateDirectory(game);string exe=Path.Combine(game,"Game.exe");File.WriteAllBytes(exe,PeFixture());Check(IsAmd64(exe),"Validates a PE x64 executable without launching it");
            Check(SelectProxy(game,"auto",null)=="dxgi","First priority: dxgi");File.WriteAllText(Path.Combine(game,"dxgi.dll"),"foreign-dxgi");
            Check(SelectProxy(game,"auto",null)=="asi","Second priority: ASI when dxgi is occupied");File.WriteAllText(Path.Combine(game,"version.dll"),"foreign-version");
            Check(SelectProxy(game,"auto",null)=="winmm","Third priority: winmm when the ASI loader conflicts");File.Delete(Path.Combine(game,"version.dll"));
            string package=Path.Combine(BuildsRoot,"nightly-game-test");Directory.CreateDirectory(Path.Combine(package,"OptiScaler"));File.WriteAllBytes(Path.Combine(package,"OptiScaler.dll"),PeFixture());File.WriteAllText(Path.Combine(package,"OptiScaler.ini"),"[FrameGen]\nEnabled=auto\n[Plugins]\nLoadAsiPlugins=auto\n");File.WriteAllText(Path.Combine(package,"instalacao.json"),"{\"optiscaler\":\"nightly-test\",\"streamline\":\"stream-test\"}");
            InstallGame(exe,package,"auto","off").GetAwaiter().GetResult();
            Check(File.Exists(Path.Combine(game,"OptiScaler.asi")) && Hash(Path.Combine(game,"version.dll"))==Hash(loader) && File.ReadAllText(Path.Combine(game,"dxgi.dll"))=="foreign-dxgi","ASI installation copies the loader as version.dll and preserves other mods");
            File.WriteAllText(Path.Combine(game,"version.dll"),"changed-by-user");InstallGame(exe,package,"auto","off").GetAwaiter().GetResult();
            Check(File.Exists(Path.Combine(game,"winmm.dll")) && !File.Exists(Path.Combine(game,"OptiScaler.asi")) && File.ReadAllText(Path.Combine(game,"version.dll"))=="changed-by-user","Switching proxies removes only unchanged files from the previous managed install");
            string badGame=Path.Combine(dir,"games","failure");Directory.CreateDirectory(Path.Combine(badGame,"OptiScaler.ini"));string badExe=Path.Combine(badGame,"Failure.exe");File.WriteAllBytes(badExe,PeFixture());
            rejected=false;try{InstallGame(badExe,package,"dxgi","off").GetAwaiter().GetResult();}catch{rejected=true;}
            Check(rejected && !File.Exists(Path.Combine(badGame,"dxgi.dll")) && File.Exists(badExe),"Copy failure rolls back new files and preserves the game");
            string txDir=Path.Combine(dir,"rollback");Directory.CreateDirectory(txDir);File.WriteAllText(Path.Combine(txDir,"existing"),"original");string changed=Path.Combine(dir,"replacement");File.WriteAllText(changed,"new");
            using(var tx=new FileTransaction(txDir,Path.Combine(dir,"backup"))) {tx.Put(changed,"existing");tx.Put(changed,"created");}
            Check(File.ReadAllText(Path.Combine(txDir,"existing"))=="original" && !File.Exists(Path.Combine(txDir,"created")),"Rollback restores original files and removes new files");
            Check(PatcherSupports("CHECK_UE(Supported)","Supported-Win64-Shipping.exe") && !PatcherSupports("// CHECK_UE(Fake)","Fake-Win64-Shipping.exe"),"OptiPatcher requires a positive support match and ignores comments");
            Http=new HttpClient(new PatcherFixture());
            Check(AddPatcher("Supported-Win64-Shipping.exe",package,"auto").GetAwaiter().GetResult()=="installed-rolling" && new IniDocument(Path.Combine(package,"OptiScaler.ini")).Get("Plugins","LoadAsiPlugins")=="true","Supported OptiPatcher installs and enables ASI plugins");
            Check(AddPatcher("Unsupported.exe",package,"auto").GetAwaiter().GetResult()=="unsupported","OptiPatcher is skipped for unsupported executables");
            Http.Dispose();Http=originalHttp;
            string helperGame=Path.Combine(dir,"games","helper with spaces");Directory.CreateDirectory(helperGame);string helperExe=Path.Combine(helperGame,"Helper.exe");File.WriteAllBytes(helperExe,PeFixture());
            RunSetupProcess(helperExe,package,"dxgi","off");Check(File.Exists(Path.Combine(helperGame,"dxgi.dll")) && ReadGameManifest(helperGame).proxy=="dxgi","The same EXE runs its internal installer automatically and returns zero");
            Check(QuoteArgument("C:\\path with spaces\\").EndsWith("\\\\\""),"Setup arguments preserve spaces and trailing backslashes");
        } finally {if(!object.ReferenceEquals(Http,originalHttp))Http.Dispose();Http=originalHttp;Root=originalRoot;}
    }
}
