using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static partial class Program
{
    sealed partial class MainWindow
    {
        static bool VisibleValue(TextBox box){
            box.UpdateLayout();if(box.ActualWidth<1||box.ActualHeight<1)return false;
            int width=(int)Math.Ceiling(box.ActualWidth),height=(int)Math.Ceiling(box.ActualHeight);var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);var visual=new DrawingVisual();using(var drawing=visual.RenderOpen())drawing.DrawRectangle(new VisualBrush(box),null,new Rect(0,0,width,height));bitmap.Render(visual);var pixels=new byte[width*height*4];bitmap.CopyPixels(pixels,width*4,0);int ink=0;
            for(int y=3;y<height-3;y++)for(int x=8;x<width-8;x++){int i=(y*width+x)*4;if(pixels[i]>145&&pixels[i+1]>145&&pixels[i+2]>145)ink++;}return ink>10;
        }
        internal async Task TestEditorWorkflow(){
            SelectPage(2);iniTarget.SelectedIndex=0;LoadPackageIni(false);fullView.IsChecked=true;iniSearch.Text="FGInput";UpdateLayout();PumpDispatcher();
            var value=rawEditors[IniId("FrameGen","FGInput")];Check(VisibleValue(value),"All settings paints visible value glyphs inside a 38-pixel text field");
            value.Text="fsrfg";UpdateLayout();Check(VisibleValue(value),"Edited full-editor values remain visible while typing");SaveEditor();
            string packageIni=editedIni;var package=new IniDocument(packageIni);package.Set("Custom","Keep","package-specific");package.Save(false);LoadIni(packageIni);string before=File.ReadAllText(packageIni);
            Check(resetIni.IsEnabled&&FindIniDefaults(packageIni)!=null,"The selected package resolves its own original release defaults");StageFactoryDefaults();Check(File.ReadAllText(packageIni)==before&&Selected(editors["ini.fg_input"])=="auto"&&rawValues[IniId("FrameGen","FGOutput")]=="auto","Reset stages original values for every documented key without writing the selected file");SaveEditor();Check(new IniDocument(packageIni).Get("FrameGen","FGInput")=="auto"&&new IniDocument(packageIni).Get("Custom","Keep")=="package-specific"&&File.Exists(packageIni+".bak"),"Saving a package reset keeps custom keys and backs up the previous configuration");
            string first=ReadLibrary().games.Single().Executable;string secondFolder=Path.Combine(Root,"games","Other application");Directory.CreateDirectory(secondFolder);string second=Path.Combine(secondFolder,"OtherGame.exe");File.WriteAllBytes(second,PeFixture());await InstallGame(second,LatestBuild(),"dxgi","off");string firstIni=Path.Combine(Path.GetDirectoryName(first),"OptiScaler.ini"),secondIni=Path.Combine(secondFolder,"OptiScaler.ini");var other=new IniDocument(secondIni);other.Set("FrameGen","Enabled","true");other.Save(false);
            iniTarget.SelectedIndex=1;SetSubject(firstIni);Check(iniSubject.Items.Count==2&&editedIni==firstIni&&Selected(editors["ini.fg_enabled"])=="false","Installed application mode lists registered games independently of the Install page");SetSubject(secondIni);Check(editedIni==secondIni&&Selected(editors["ini.fg_enabled"])=="true","Selecting another application loads that application's actual INI values");
            string firstBefore=File.ReadAllText(firstIni),packageBefore=File.ReadAllText(packageIni);StageFactoryDefaults();SaveEditor();Check(new IniDocument(secondIni).Get("FrameGen","FGInput")=="auto"&&File.ReadAllText(firstIni)==firstBefore&&File.ReadAllText(packageIni)==packageBefore,"Application reset changes only the selected application's INI");
            Capture(Path.Combine(AppRoot,"src","interface-application-editor.png"));iniTarget.SelectedIndex=0;Check(editedIni==packageIni,"Switching back to package mode reloads the package instead of retaining a game's INI");
            string manual=Path.Combine(Root,"manual");Directory.CreateDirectory(manual);string custom=Path.Combine(manual,"OptiScaler.ini");File.WriteAllText(custom,"[Custom]\nValue=keep\n");SelectApplicationIni(custom);Check(!resetIni.IsEnabled&&resetHint.Text.Contains("unavailable"),"Reset is unavailable when the matching original defaults cannot be identified");
            ClearIniSelection();Check(!saveIni.IsEnabled&&!resetIni.IsEnabled&&editors.Values.All(x=>!x.IsEnabled),"An empty target disables editing and reset until a configuration is selected");
        }
    }
}
