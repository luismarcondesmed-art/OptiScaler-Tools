using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;

internal sealed class AppSettings
{
    internal readonly Dictionary<string,string> Values=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase) {
        {"app.gui","true"},{"app.open_output","true"},
        {"game.executable",""},{"game.proxy","auto"},{"game.optipatcher","auto"},
        {"ini.dx11_upscaler","auto"},{"ini.dx12_upscaler","auto"},{"ini.vulkan_upscaler","auto"},
        {"ini.fg_enabled","auto"},{"ini.fg_input","nvngxfg"},{"ini.fg_output","fsrfg"},{"ini.fg_replacement","Combo"},
        {"ini.fsr_upscaler_index","auto"},{"ini.fsr_fg_index","auto"},{"ini.fsr4_model","auto"},
        {"ini.dlss_preset","auto"},{"ini.spoofing","auto"}
    };
    internal string FilePath;
    internal string Get(string key) {return Values[key];}
    internal bool Bool(string key) {return Get(key)=="true";}
    internal static AppSettings Load(string path) {
        var result=new AppSettings {FilePath=path};if(!File.Exists(path)) return result;
        string section="";int lineNo=0;var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(string raw in File.ReadAllLines(path)) {
            lineNo++;string line=StripComment(raw).Trim();if(line.Length==0) continue;
            if(line.StartsWith("[") && line.EndsWith("]")) {section=line.Substring(1,line.Length-2).Trim();if(!new[]{"app","game","ini"}.Contains(section)) throw Error(lineNo,"Unknown section: "+section);continue;}
            int eq=line.IndexOf('=');if(eq<1) throw Error(lineNo,"Expected key = value.");
            string key=section+"."+line.Substring(0,eq).Trim(),value=line.Substring(eq+1).Trim();
            if(!result.Values.ContainsKey(key) || !seen.Add(key)) throw Error(lineNo,"Unknown or duplicate key: "+key);
            try {
                if(value.StartsWith("\"") && value.EndsWith("\"")) value=new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<string>(value);
                else if(value.StartsWith("'") && value.EndsWith("'")) value=value.Substring(1,value.Length-2);
                else if(value!="true" && value!="false") throw new Exception("Use a quoted string or a boolean.");
                result.Values[key]=value;
            } catch(Exception e) {throw Error(lineNo,Program.DescribeException(e));}
        }
        result.Validate();return result;
    }
    static Exception Error(int line,string message) {return new Program.AppError("E_CONFIG",10,"TOML, line "+line+": "+message);}
    static string StripComment(string raw) {
        char quote='\0';bool escaped=false;
        for(int i=0;i<raw.Length;i++) {
            char c=raw[i];if(quote=='\"' && c=='\\' && !escaped) {escaped=true;continue;}
            if((c=='\"'||c=='\'') && !escaped) {if(quote=='\0') quote=c;else if(quote==c) quote='\0';}
            if(c=='#' && quote=='\0') return raw.Substring(0,i);escaped=false;
        }
        return raw;
    }
    internal void Validate() {
        foreach(string key in new[]{"app.gui","app.open_output"}) if(!new[]{"true","false"}.Contains(Get(key))) throw Error(0,key+" requires true/false.");
        if(!new[]{"auto","dxgi","asi","winmm"}.Contains(Get("game.proxy"))) throw Error(0,"game.proxy: use auto, dxgi, asi, or winmm.");
        if(!new[]{"auto","off"}.Contains(Get("game.optipatcher"))) throw Error(0,"game.optipatcher: use auto or off.");
        foreach(var field in Program.ProfileFields) if(!field.Options.Any(x=>x.Value.Equals(Get(field.ConfigKey),StringComparison.OrdinalIgnoreCase))) throw Error(0,"Invalid value for "+field.ConfigKey+": "+Get(field.ConfigKey));
        if(!new[]{"auto","off","streamline","full"}.Contains(Get("ini.spoofing"))) throw Error(0,"ini.spoofing: use auto, off, streamline, or full.");
        if(Get("ini.fg_input")=="nvngxfg" && !new[]{"auto","nofg","fsrfg"}.Contains(Get("ini.fg_output")))throw Error(0,"NvngxFG input requires FSR FG output. Use a different input for XeFG or DLSSG output.");
    }
    internal void Save() {
        Validate();if(string.IsNullOrEmpty(FilePath)) throw new Exception("No TOML path has been configured.");
        var lines=File.Exists(FilePath)?File.ReadAllLines(FilePath).ToList():new List<string>();
        foreach(string section in new[]{"app","game","ini"}) {
            if(!lines.Any(x=>x.Trim()=="["+section+"]")) {lines.Add("");lines.Add("["+section+"]");}
            foreach(var kv in Values.Where(x=>x.Key.StartsWith(section+"."))) {
                string key=kv.Key.Substring(section.Length+1),value=kv.Key.StartsWith("app.")?kv.Value:new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(kv.Value);
                int start=lines.FindIndex(x=>x.Trim()=="["+section+"]"),end=start+1;
                while(end<lines.Count && !lines[end].TrimStart().StartsWith("[")) end++;
                int index=lines.FindIndex(start+1,end-start-1,x=>Regex.IsMatch(x,@"^\s*"+Regex.Escape(key)+@"\s*="));
                if(index>=0) lines[index]=key+" = "+value;else lines.Insert(end,key+" = "+value);
            }
        }
        string tmp=FilePath+".tmp";File.WriteAllLines(tmp,lines,new UTF8Encoding(false));
        if(File.Exists(FilePath)) File.Replace(tmp,FilePath,FilePath+".bak");else File.Move(tmp,FilePath);
    }
}

internal static partial class Program
{
    internal sealed class Choice {internal string Value,Label;internal Choice(string v,string l) {Value=v;Label=l;} public override string ToString(){return Label;} }
    internal sealed class ProfileField {
        internal string ConfigKey,Section,Key,Label;internal Choice[] Options;
        internal ProfileField(string config,string section,string key,string label,params string[] options) {ConfigKey="ini."+config;Section=section;Key=key;Label=label;Options=options.Select(x=>{var p=x.Split(new[]{'|'},2);return new Choice(p[0],p.Length==2?p[1]:p[0]);}).ToArray();}
    }
    internal static readonly ProfileField[] ProfileFields={
        new ProfileField("dx11_upscaler","Upscalers","Dx11Upscaler","DirectX 11","auto|Automatic","fsr22|FSR 2.2","fsr31|FSR 3.1","xess|XeSS (Arc)","xess_12|XeSS via DX12","fsr21_12|FSR 2.1 via DX12","fsr22_12|FSR 2.2 via DX12","ffx_12|FFX / FSR 4 via DX12","dlss|DLSS"),
        new ProfileField("dx12_upscaler","Upscalers","Dx12Upscaler","DirectX 12","auto|Automatic","xess|XeSS","fsr21|FSR 2.1","fsr22|FSR 2.2","ffx|FFX / FSR 3-4","dlss|DLSS"),
        new ProfileField("vulkan_upscaler","Upscalers","VulkanUpscaler","Vulkan","auto|Automatic","fsr21|FSR 2.1","fsr22|FSR 2.2","ffx|FFX / FSR 3","xess|XeSS","fsr21_12|FSR 2.1 via DX12","ffx_12|FFX / FSR 4 via DX12","dlss|DLSS"),
        new ProfileField("fg_enabled","FrameGen","Enabled","Enabled","auto|Automatic","true|Enabled","false|Disabled"),
        new ProfileField("fg_input","FrameGen","FGInput","Source / input","auto|Automatic","nofg|Disabled","nvngxfg|NvngxFG","dlssg|DLSSG / Streamline","fsrfg|FSR FG","upscaler|Upscaler (OptiFG)","fsrfg30|FSR FG 3.0","xefg|XeFG"),
        new ProfileField("fg_output","FrameGen","FGOutput","Output engine","auto|Automatic","nofg|Disabled","fsrfg|FSR FG","xefg|XeFG","dlssg|DLSSG"),
        new ProfileField("fg_replacement","FrameGen","FGNvngxReplacement","FG replacement","auto|Automatic","None|No replacement","Nukems|Nukems","Arturs|DLSS Enabler","FFX|FFX","Combo|Combo"),
        new ProfileField("fsr_upscaler_index","FSR","UpscalerIndex","FSR upscaling backend","auto|Automatic","0|FSR 4 (index 0)","1|FSR 3.1 (index 1)","2|FSR 2.3 (index 2)"),
        new ProfileField("fsr_fg_index","FSR","FGIndex","FSR frame-generation backend","auto|Automatic","0|FSR 4 FG (index 0)","1|FSR 3.1 FG (index 1)"),
        new ProfileField("fsr4_model","FSR","Fsr4Model","FSR 4 model","auto|Automatic","0|FP8","1|INT8","2|INT8 RDNA2","3|INT8 Lite"),
        new ProfileField("dlss_preset","DLSS","RenderPresetForAll","DLSS preset","auto|Automatic","0|Default","1|A","2|B","3|C","4|D","5|E","6|F","7|G","8|H","9|I","10|J","11|K","12|L","13|M","14|N","15|O")
    };
    internal sealed class IniDocument {
        internal sealed class Entry {
            internal string Section,Key,Value,Description;
            internal string Id {get{return Section+"\0"+Key;}}
        }
        readonly List<string> lines;readonly string newline;internal readonly string Path;
        internal IniDocument(string path) {Path=path;string text=File.ReadAllText(path);newline=text.Contains("\r\n")?"\r\n":"\n";lines=Regex.Split(text,@"\r?\n").ToList();}
        static int CommentStart(string value) {
            char quote='\0';
            for(int i=0;i<value.Length;i++) {
                char c=value[i];if(c=='\"'||c=='\''){if(quote=='\0')quote=c;else if(quote==c)quote='\0';}
                if(quote=='\0'&&(c==';'||c=='#')&&(i==0||char.IsWhiteSpace(value[i-1])))return i;
            }return value.Length;
        }
        internal List<Entry> Entries() {
            var result=new List<Entry>();var comments=new List<string>();string section="",previousDescription="";
            foreach(string line in lines) {
                string trimmed=line.Trim();var header=Regex.Match(line,@"^\s*\[([^\]]+)\]");
                if(header.Success){section=header.Groups[1].Value.Trim();comments.Clear();previousDescription="";continue;}
                if(trimmed.StartsWith(";")||trimmed.StartsWith("#")){string comment=trimmed.Substring(1).Trim();if(comment.Length>0&&!Regex.IsMatch(comment,@"^[-=_ ]+$"))comments.Add(comment);continue;}
                var match=Regex.Match(line,@"^\s*([^;#=\[\]]+?)\s*=(.*)$");
                if(match.Success){string tail=match.Groups[2].Value;int split=CommentStart(tail);string inline=tail.Substring(split).TrimStart(';','#',' ','\t');string description=comments.Count>0?string.Join("\n",comments):previousDescription;if(inline.Length>0)description+=(description.Length>0?"\n":"")+inline;
                    result.Add(new Entry{Section=section,Key=match.Groups[1].Value.Trim(),Value=tail.Substring(0,split).Trim(),Description=description});previousDescription=description;comments.Clear();
                }else{comments.Clear();previousDescription="";}
            }return result;
        }
        internal string Get(string section,string key) {
            var entry=Entries().FirstOrDefault(x=>x.Section.Equals(section,StringComparison.OrdinalIgnoreCase)&&x.Key.Equals(key,StringComparison.OrdinalIgnoreCase));return entry==null?"auto":entry.Value;
        }
        internal void Set(string section,string key,string value,string description=null) {
            if(value.IndexOfAny(new[]{'\r','\n'})>=0) throw new AppError("E_INI",40,"Invalid INI value.");
            string current="";int start=-1,end=lines.Count;
            for(int i=0;i<lines.Count;i++) {
                var h=Regex.Match(lines[i],@"^\s*\[([^\]]+)\]");
                if(h.Success) {if(start>=0){end=i;break;}current=h.Groups[1].Value;if(current.Equals(section,StringComparison.OrdinalIgnoreCase)) start=i;}
            }
            if(start<0 && section.Length==0){start=-1;end=lines.FindIndex(x=>Regex.IsMatch(x,@"^\s*\["));if(end<0)end=lines.Count;}
            else if(start<0) {lines.Add("");lines.Add("["+section+"]");if(!string.IsNullOrWhiteSpace(description))lines.AddRange(description.Split('\n').Select(x=>"; "+x.TrimEnd('\r')));lines.Add(key+"="+value);return;}
            bool found=false;
            for(int i=start+1;i<end;i++) {
                var match=Regex.Match(lines[i],@"^(\s*"+Regex.Escape(key)+@"\s*=\s*)(.*)$",RegexOptions.IgnoreCase);
                if(match.Success){string tail=match.Groups[2].Value;int split=CommentStart(tail);string comment=tail.Substring(split);string spacing=split<tail.Length?tail.Substring(0,split).Substring(tail.Substring(0,split).TrimEnd().Length):"";if(comment.Length>0&&spacing.Length==0)spacing=" ";lines[i]=match.Groups[1].Value+value+spacing+comment;found=true;}
            }
            if(!found){var addition=new List<string>();if(!string.IsNullOrWhiteSpace(description))addition.AddRange(description.Split('\n').Select(x=>"; "+x.TrimEnd('\r')));addition.Add(key+"="+value);lines.InsertRange(end,addition);}
        }
        internal void Save(bool backup) {
            if(backup) File.Copy(Path,Path+".bak",true);
            string tmp=Path+".tmp";File.WriteAllText(tmp,string.Join(newline,lines),new UTF8Encoding(false));
            File.Replace(tmp,Path,null);
        }
    }
    static void ApplySpoofing(IniDocument ini,string mode) {
        foreach(string key in new[]{"StreamlineSpoofing","Dxgi","Vulkan","VulkanExtensionSpoofing","Registry","User32","SpoofHAGS"})
            ini.Set("Spoofing",key,mode=="auto"?"auto":mode=="full"?"true":mode=="streamline" && key=="StreamlineSpoofing"?"true":mode=="streamline" && key=="SpoofHAGS"?"auto":"false");
        ini.Set("fakenvapi","UseFakenvapi",mode=="full"?"true":mode=="off"?"false":"auto");
    }
    static void ApplyProfile(string path,AppSettings settings) {
        var ini=new IniDocument(path);
        foreach(var f in ProfileFields) ini.Set(f.Section,f.Key,settings.Get(f.ConfigKey));
        ini.Set("DLSS","RenderPresetOverride",settings.Get("ini.dlss_preset")=="auto"?"auto":"true");
        ApplySpoofing(ini,settings.Get("ini.spoofing"));ini.Save(false);
    }
}
