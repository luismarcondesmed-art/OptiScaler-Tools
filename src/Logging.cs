using System;
using System.IO;
using System.Net.Http;
using System.Collections.Generic;

internal static partial class Program
{
    static void UseEnglishCulture() {
        var english=new System.Globalization.CultureInfo("en-US");
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture=english;
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture=english;
        System.Threading.Thread.CurrentThread.CurrentCulture=english;
        System.Threading.Thread.CurrentThread.CurrentUICulture=english;
    }
    internal static string DescribeException(Exception error) {
        if(error is AppError)return error.Message;
        if(error is UnauthorizedAccessException)return "Access was denied. Check folder permissions and close the game before trying again.";
        if(error is System.ComponentModel.Win32Exception)return "A Windows operation failed (native error "+((System.ComponentModel.Win32Exception)error).NativeErrorCode+").";
        if(error is HttpRequestException){var status=System.Text.RegularExpressions.Regex.Match(error.Message,@"\b[45]\d{2}\b");return "The HTTP request failed"+(status.Success?" (HTTP "+status.Value+")":"")+". Check the connection or server response and retry.";}
        if(error is System.OperationCanceledException)return "The operation timed out or was canceled.";
        if(error is IOException)return "A file operation failed. The file may be locked, missing, or unavailable ("+error.GetType().Name+", 0x"+error.HResult.ToString("X8")+").";
        return error.Message;
    }
    internal sealed class AppError:Exception {internal readonly string Code;internal readonly int Exit;internal AppError(string code,int exit,string message,Exception inner=null):base(message,inner){Code=code;Exit=exit;} }
    static string SessionId;
    static readonly object LogGate=new object();
    static DateTime SessionStarted;
    static string SessionOperation;
    static bool SessionWarning;
    static string SessionOpti,SessionStream;
    static int ExitCode(Exception e) {return e is AppError?((AppError)e).Exit:e is UnauthorizedAccessException?52:e is HttpRequestException?20:1;}
    static string ErrorCode(Exception e) {return e is AppError?((AppError)e).Code:e is UnauthorizedAccessException?"E_PERMISSION":e is HttpRequestException?"E_NETWORK":e is IOException?"E_IO":"E_UNEXPECTED";}
    static string Argument(string[] args,string name,string fallback) {int i=Array.IndexOf(args,name);if(i<0)return fallback;if(i+1>=args.Length)throw new AppError("E_ARGS",10,"Missing value for "+name);return args[i+1];}
    static void BeginSession(string operation) {
        SessionId=DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+"-"+Guid.NewGuid().ToString("N").Substring(0,6);SessionStarted=DateTime.UtcNow;SessionOperation=operation;SessionWarning=false;SessionOpti=null;SessionStream=null;
        WriteEvent("INFO","SESSION_START","Starting "+operation,new{root=Root,operation=operation});
    }
    static void WriteEvent(string level,string code,string message,object data) {
        if(level=="WARN") SessionWarning=true;
        string line=DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz")+" ["+level+"] ["+code+"] "+message;
        lock(LogGate) {
            Directory.CreateDirectory(LogsRoot);File.AppendAllText(LogPath,line+Environment.NewLine);
            File.AppendAllText(System.IO.Path.ChangeExtension(LogPath,".jsonl"),Json.Serialize(new{timestamp=DateTimeOffset.Now.ToString("o"),session=SessionId,level=level,code=code,message=message,data=data})+Environment.NewLine);
        }
        Console.WriteLine(line);var listener=LogReceived;if(listener!=null)listener("["+level+"] ["+code+"] "+message);
    }
    static void EndSession(bool success,Exception error) {
        if(error!=null) WriteEvent("ERROR",ErrorCode(error),DescribeException(error),new {exception=error.GetType().FullName,detail=DescribeException(error),stack=error.StackTrace,inner=error.InnerException==null?null:DescribeException(error.InnerException),hresult="0x"+error.HResult.ToString("X8"),exitCode=ExitCode(error)});
        WriteEvent(success?(SessionWarning?"WARN":"INFO"):"ERROR",success?"SUCCESS":"FAILED",success?"Operation completed"+(SessionWarning?" with warnings.":" successfully."):"Operation failed.",new{optiscaler=SessionOpti,streamline=SessionStream});
        if(SessionId!=null) File.WriteAllText(System.IO.Path.Combine(LogsRoot,"latest-run.json"),Json.Serialize(new{session=SessionId,operation=SessionOperation,started=SessionStarted.ToString("o"),finished=DateTime.UtcNow.ToString("o"),success=success,warning=SessionWarning,code=error==null?"OK":ErrorCode(error),exitCode=error==null?0:ExitCode(error),optiscaler=SessionOpti,streamline=SessionStream,log=LogPath}));
    }
    static string ReadStreamlineTag() {try{return Json.Deserialize<Manifest>(File.ReadAllText(System.IO.Path.Combine(StreamlineRoot,".manifest.json"))).tag;}catch{return "Local version unknown";} }
}
