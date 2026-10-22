using System;
using System.IO;
using System.Linq;

using System.Threading.Tasks;

using System.Runtime.InteropServices;

internal static partial class Program
{
    [DllImport("kernel32.dll")] static extern bool AttachConsole(uint processId);
    [DllImport("kernel32.dll")] static extern bool AllocConsole();
    [DllImport("kernel32.dll")] static extern IntPtr GetStdHandle(int kind);
    static void AttachCli() {
        IntPtr output=GetStdHandle(-11);
        if(output!=IntPtr.Zero && output!=new IntPtr(-1))return;
        if(!AttachConsole(0xffffffff))AllocConsole();
        Console.SetOut(new StreamWriter(Console.OpenStandardOutput()){AutoFlush=true});
        Console.SetIn(new StreamReader(Console.OpenStandardInput()));
    }

    // Only move known legacy paths within the selected app folder; never overwrite.
    static void MoveLegacy(string source,string destination) {
        string basePath=Path.GetFullPath(Root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        source=Path.GetFullPath(source); destination=Path.GetFullPath(destination);
        if(!source.StartsWith(basePath,StringComparison.OrdinalIgnoreCase) || !destination.StartsWith(basePath,StringComparison.OrdinalIgnoreCase)) throw new Exception("Migration path is outside the selected workspace.");
        if(!File.Exists(source) && !Directory.Exists(source)) return;
        if((File.GetAttributes(source)&FileAttributes.ReparsePoint)!=0) throw new Exception("Migration does not support links: "+source);
        if(File.Exists(destination) || Directory.Exists(destination)) { Log("WARNING: A conflicting legacy file was kept: "+source); return; }
        Directory.CreateDirectory(Path.GetDirectoryName(destination));
        if(Directory.Exists(source)) Directory.Move(source,destination); else File.Move(source,destination);
    }
    static void Organize() {
        Directory.CreateDirectory(DataRoot); Directory.CreateDirectory(BuildsRoot);
        foreach(string name in new[]{"dlssenabler","amd_files","asi_loader"})Directory.CreateDirectory(Path.Combine(SourceRoot,name));
        MergeLegacy(Path.Combine(Root,"pacotes"),BuildsRoot);
        MergeLegacy(Path.Combine(Root,"dados","downloads"),DownloadsRoot);
        MergeLegacy(Path.Combine(Root,"dados","streamline"),StreamlineRoot);
        MergeLegacy(Path.Combine(Root,"dados","logs"),Path.Combine(LogsRoot,"legacy"));
        MoveLegacy(Path.Combine(Root,"dados","ultima_versao_baixada.txt"),Path.Combine(DataRoot,"ultima_versao_baixada.txt"));
        MoveLegacy(Path.Combine(Root,"plugins","version.dll"),Path.Combine(SourceRoot,"dlssenabler","version.dll"));
        foreach(string file in new[]{"amdxcffx64.dll","amdvkffx64.dll"})MoveLegacy(Path.Combine(Root,"plugins",file),Path.Combine(SourceRoot,"amd_files",file));
        MoveLegacy(Path.Combine(Root,"asiloader.dll"),Path.Combine(SourceRoot,"asi_loader","asiloader.dll"));
        MoveLegacy(Path.Combine(Root,"plugins","streamline"),StreamlineRoot);
        MoveLegacy(Path.Combine(Root,"streamline"),StreamlineRoot);
        MoveLegacy(Path.Combine(Root,"streamline-downloads"),Path.Combine(DownloadsRoot,"streamline"));
        MoveLegacy(Path.Combine(Root,"ultima_versao_baixada.txt"),Path.Combine(DataRoot,"ultima_versao_baixada.txt"));
        MoveLegacy(Path.Combine(Root,"log.txt"),Path.Combine(LogsRoot,"historico.log"));
        MoveLegacy(Path.Combine(Root,"tests.log"),Path.Combine(LogsRoot,"testes-anteriores.log"));
        foreach(string file in Directory.GetFiles(Root,"OptiScaler*")) if(IsArchive(file)) MoveLegacy(file,Path.Combine(DownloadsRoot,"optiscaler",Path.GetFileName(file)));
        foreach(string dir in Directory.GetDirectories(Root)) if(File.Exists(Path.Combine(dir,"instalacao.json"))) MoveLegacy(dir,Path.Combine(BuildsRoot,Path.GetFileName(dir)));
        foreach(string name in new[]{"dados","plugins","pacotes"}) {
            string old=Path.Combine(Root,name);
            if(Directory.Exists(old)) {
                string oldLock=Path.Combine(old,".optiscaler.lock");
                if(File.Exists(oldLock) && new FileInfo(oldLock).Length==0) {try{File.Delete(oldLock);}catch{}}
                if(!Directory.EnumerateFileSystemEntries(old).Any())Directory.Delete(old);
            }
        }
        if(!TestMode)ArchiveLegacyLogs();
    }
    static void ArchiveLegacyLogs() {
        string marker=Path.Combine(DataRoot,"english-logs.migrated");if(File.Exists(marker))return;
        string archive=Path.Combine(LogsRoot,"legacy","pre-english");
        if(Directory.Exists(LogsRoot))foreach(string file in Directory.GetFiles(LogsRoot,"*.jsonl")) {
            bool legacy=File.ReadLines(file).Take(24).Any(line=>System.Text.RegularExpressions.Regex.IsMatch(line,@"(?i)\b(Iniciando|Operacao|Instalacao|Baixando|Consultando|Concluido|Extraindo|AVISO|ERRO|PACOTE PRONTO)\b"));
            if(!legacy)continue;MoveLegacy(file,Path.Combine(archive,Path.GetFileName(file)));string text=Path.ChangeExtension(file,".log");if(File.Exists(text))MoveLegacy(text,Path.Combine(archive,Path.GetFileName(text)));
        }
        foreach(string name in new[]{"aplicativo.log","aplicativo.jsonl","ultima-execucao.json"})MoveLegacy(Path.Combine(LogsRoot,name),Path.Combine(archive,name));
        File.WriteAllText(marker,DateTime.UtcNow.ToString("o"));
    }
    static void MergeLegacy(string from,string to) {
        if(!Directory.Exists(from))return;
        if((File.GetAttributes(from)&FileAttributes.ReparsePoint)!=0)throw new AppError("E_MIGRATE_LINK",50,"Migration does not support links: "+from);
        Directory.CreateDirectory(to);
        foreach(string file in Directory.GetFiles(from))MoveLegacy(file,Path.Combine(to,Path.GetFileName(file)));
        foreach(string sub in Directory.GetDirectories(from))MergeLegacy(sub,Path.Combine(to,Path.GetFileName(sub)));
        if(!Directory.EnumerateFileSystemEntries(from).Any())Directory.Delete(from);
    }
    static string LatestBuild() {
        string marker=Path.Combine(DataRoot,"ultima_versao_baixada.txt");
        try { string path=Child(BuildsRoot,File.ReadAllText(marker).Trim()); if(Directory.Exists(path)) return path; } catch { }
        return Directory.Exists(BuildsRoot)?Directory.GetDirectories(BuildsRoot).Where(x=>File.Exists(Path.Combine(x,"instalacao.json"))).OrderByDescending(Directory.GetLastWriteTimeUtc).FirstOrDefault():null;
    }
    static void MigrationTest(string dir) {
        string previous=Root;
        try {
            Root=dir;Directory.CreateDirectory(dir);
            Directory.CreateDirectory(Path.Combine(dir,"streamline"));File.WriteAllText(Path.Combine(dir,"streamline","fixture.dll"),"keep");
            Directory.CreateDirectory(Path.Combine(dir,"nightly-fixture"));File.WriteAllText(Path.Combine(dir,"nightly-fixture","instalacao.json"),"{}");
            File.WriteAllText(Path.Combine(dir,"ultima_versao_baixada.txt"),"nightly-fixture");File.WriteAllText(Path.Combine(dir,"OptiScaler-fixture.7z"),"archive");
            Organize();Organize();
            Check(File.ReadAllText(Path.Combine(StreamlineRoot,"fixture.dll"))=="keep" && LatestBuild()==Path.Combine(BuildsRoot,"nightly-fixture") && File.ReadAllText(Path.Combine(DownloadsRoot,"optiscaler","OptiScaler-fixture.7z"))=="archive","Migration preserves files and is repeatable");
            Directory.CreateDirectory(Path.Combine(dir,"streamline"));File.WriteAllText(Path.Combine(dir,"streamline","local.dll"),"legacy");Organize();
            Check(File.Exists(Path.Combine(dir,"streamline","local.dll")) && File.ReadAllText(Path.Combine(StreamlineRoot,"fixture.dll"))=="keep","Migration preserves both copies on conflict");
        } finally {Root=previous;}
    }
    static void RunWindow() {
        var app=new System.Windows.Application {ShutdownMode=System.Windows.ShutdownMode.OnMainWindowClose};
        app.Run(new MainWindow());
    }
    static void UiTest() {
        var app=new System.Windows.Application {ShutdownMode=System.Windows.ShutdownMode.OnExplicitShutdown};
        using(var scope=new UiSnapshotScope()) {
            var window=new MainWindow();window.WindowStartupLocation=System.Windows.WindowStartupLocation.Manual;window.Left=-20000;window.Top=-20000;
            window.Show();PumpDispatcher();System.IO.Directory.CreateDirectory(System.IO.Path.Combine(Root,"src"));window.CapturePages(System.IO.Path.Combine(Root,"src"));
            Check(window.IsLoaded,"All four WPF pages render without downloading files");window.Close();
            HttpClientStateTest();
        }
        app.Shutdown();
    }
    sealed class UiSnapshotScope:IDisposable {public void Dispose(){LogReceived=null;}}
    static void HttpClientStateTest() {
        string original=Root;var originalHttp=Http;bool originalTest=TestMode;
        string testDir=Path.Combine(Root,".ui-test-"+Guid.NewGuid().ToString("N"));Exception failure=null;
        try {
            Root=testDir;TestMode=true;Directory.CreateDirectory(Root);Http=new System.Net.Http.HttpClient(new FixtureHandler());
            var window=new MainWindow();window.WindowStartupLocation=System.Windows.WindowStartupLocation.Manual;window.Left=-20000;window.Top=-20000;
            var frame=new System.Windows.Threading.DispatcherFrame();
            window.ContentRendered+=async(s,e)=> {
                try {await window.TestUpdate();Check(File.Exists(Path.Combine(LatestBuild(),"optiscaler","streamline","sl.test.dll"))&&window.ReadyAfterTest,"Update completes through the WPF interface without blocking the window");window.TestIniEditor();}
                catch(Exception ex){failure=ex;}
                finally{window.Close();frame.Continue=false;}
            };
            window.Show();System.Windows.Threading.Dispatcher.PushFrame(frame);
            if(failure!=null)throw failure;
        }finally{Http.Dispose();Http=originalHttp;Root=original;TestMode=originalTest;if(Directory.Exists(testDir))Directory.Delete(testDir,true);}
    }
}
