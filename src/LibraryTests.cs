using System;
using System.IO;
using System.Linq;
using System.Net.Http;

internal static partial class Program
{
    static void LibraryTests(string dir){
        string previous=Root;var previousHttp=Http;var previousSettings=Settings;bool previousOpen=NoOpen;var fixture=new FixtureHandler();
        try{
            Root=dir;Settings=new AppSettings();NoOpen=true;Organize();Http=new HttpClient(fixture);
            File.WriteAllText(Path.Combine(SourceRoot,"dlssenabler","version.dll"),"fixture-enabler");
            string package=Path.Combine(BuildsRoot,"nightly-library-old");Directory.CreateDirectory(package);File.WriteAllBytes(Path.Combine(package,"OptiScaler.dll"),PeFixture());File.WriteAllText(Path.Combine(package,"OptiScaler.ini"),"[FrameGen]\nEnabled=auto\nFGInput=auto\nFGOutput=auto\nFGNvngxReplacement=auto\n");File.WriteAllText(Path.Combine(package,"instalacao.json"),"{\"optiscaler\":\"old\",\"streamline\":\"old-stream\"}");
            string[] exes=new string[4];for(int i=0;i<exes.Length;i++){string folder=Path.Combine(dir,"games","Game "+i);Directory.CreateDirectory(folder);exes[i]=Path.Combine(folder,"Game.exe");File.WriteAllBytes(exes[i],PeFixture());InstallGame(exes[i],package,"dxgi","off").GetAwaiter().GetResult();}
            Check(ReadLibrary().games.Count==4&&ReadLibrary().games.All(x=>x.PatcherMode=="off"&&x.Proxy=="dxgi"),"Successful installations are persisted with per-game loader and patcher preferences");
            InstallGame(exes[1],package,"dxgi","off").GetAwaiter().GetResult();Check(ReadLibrary().games.Count==4,"Reinstalling the same folder does not duplicate its library entry");
            string ini=Path.Combine(Path.GetDirectoryName(exes[1]),"OptiScaler.ini");File.WriteAllText(ini,"[FrameGen]\n; Keep my game-specific settings\nEnabled=false\nFGInput=fsrfg\nFGOutput=xefg\nFGNvngxReplacement=None\n[Custom]\nValue=keep-me\n");
            fixture.Opti=FixtureZip(new System.Collections.Generic.Dictionary<string,string>{{"OptiScaler.dll","fixture-opti"},{"OptiScaler.ini","[FrameGen]\nFGInput=auto\nFGOutput=auto\nFGNvngxReplacement=auto\n; A setting introduced in this nightly\nNewOption=auto\n"}});
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(exes[0]),"dxgi.dll"),"another-mod");File.Delete(exes[2]);File.Delete(Path.Combine(Path.GetDirectoryName(exes[3]),"OptiScaler-Tools.install.json"));
            bool partial=false;try{UpdateAllGames(true,false).GetAwaiter().GetResult();}catch(AppError e){partial=e.Code=="E_BATCH_PARTIAL"&&e.Exit==55;}
            var library=ReadLibrary();Check(partial&&library.games.Count(x=>x.LastResult=="Updated")==1&&library.games.Count(x=>x.LastResult=="Failed")==1&&library.games.Count(x=>x.LastResult=="Missing")==2,"Batch updates continue after a failure, retain missing games and report partial completion");
            Check(fixture.StreamlineDownloads==1&&ReadGameManifest(Path.GetDirectoryName(exes[1])).optiscaler=="test-nightly"&&File.ReadAllText(Path.Combine(Path.GetDirectoryName(exes[0]),"dxgi.dll"))=="another-mod","The latest package is downloaded once for the batch and conflicting files stay untouched");
            var retained=new IniDocument(ini);Check(retained.Get("FrameGen","Enabled")=="false"&&retained.Get("FrameGen","FGOutput")=="xefg"&&retained.Get("Custom","Value")=="keep-me"&&File.ReadAllText(ini).Contains("; Keep my game-specific settings")&&File.Exists(Path.Combine(ReadGameManifest(Path.GetDirectoryName(exes[1])).backup,"OptiScaler.ini")),"Game-specific INI values, comments and a pre-update backup survive batch updates");
            Check(retained.Get("FrameGen","NewOption")=="auto"&&retained.Entries().First(x=>x.Key=="NewOption").Description.Contains("introduced in this nightly"),"New nightly settings are added together with their original hover descriptions");
            RunSetupProcess(exes[1],LatestBuild(),"dxgi","off",true);Check(new IniDocument(ini).Get("Custom","Value")=="keep-me"&&ReadLibrary().games.Count==4,"The real setup helper preserves INI settings and records its installation across processes");
            ForgetGame(exes[1]);Settings.Values["game.executable"]=exes[1];DiscoverGames();Check(ReadLibrary().games.Count==3,"Removing a game keeps its files and prevents automatic history import from adding it again");ImportGame(exes[1]);Check(ReadLibrary().games.Count==4&&File.Exists(ini),"A forgotten installation can be imported explicitly without reinstalling it");
            File.Delete(LibraryPath);DiscoverGames();Check(ReadLibrary().games.Any(x=>x.Executable==exes[1])&&ReadLibrary().games.Any(x=>x.Executable==exes[0]),"Previous managed installations are discovered from successful logs and the configured game");
            string good=File.ReadAllText(LibraryPath);File.WriteAllText(LibraryPath,"{broken");bool corrupt=false;try{RegisterGame(ReadGameManifest(Path.GetDirectoryName(exes[1])));}catch(AppError e){corrupt=e.Code=="E_GAME_LIBRARY";}Check(corrupt&&File.ReadAllText(LibraryPath)=="{broken","A corrupt game library is reported without overwriting its contents");File.WriteAllText(LibraryPath,good);
        }finally{Http.Dispose();Http=previousHttp;Settings=previousSettings;Root=previous;NoOpen=previousOpen;}
    }
}
