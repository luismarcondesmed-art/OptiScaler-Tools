using System;
using System.IO;

internal static partial class Program
{
    static string FindIniDefaults(string ini){
        if(string.IsNullOrWhiteSpace(ini))return null;
        string folder=Path.GetDirectoryName(Path.GetFullPath(ini));
        string own=Path.Combine(folder,"OptiScaler","OptiScaler-Tools.defaults.ini");if(File.Exists(own))return own;
        var manifest=ReadGameManifest(folder);if(manifest!=null&&!string.IsNullOrWhiteSpace(manifest.optiscaler)){
            try{string prepared=Path.Combine(Child(BuildsRoot,manifest.optiscaler),"OptiScaler","OptiScaler-Tools.defaults.ini");if(File.Exists(prepared))return prepared;}catch{}
        }
        return null;
    }
}
