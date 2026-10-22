using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using FilePath=System.IO.Path;

internal static partial class Program
{
    sealed partial class MainWindow
    {
        readonly ComboBox iniSubject=new ComboBox();readonly TextBlock resetHint=new TextBlock();Button resetIni,iniBrowse;
        readonly RadioButton quickView=new RadioButton(),fullView=new RadioButton();
        void BuildEditorPage(){
            var page=Page("Edit .ini","Choose what you want to configure.");var dock=new DockPanel();
            var toolbar=new Grid{Margin=new Thickness(0,14,0,0)};toolbar.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});toolbar.ColumnDefinitions.Add(new ColumnDefinition());toolbar.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            resetIni=MakeButton("Reset to defaults",StageFactoryDefaults);toolbar.Children.Add(resetIni);changes.VerticalAlignment=VerticalAlignment.Center;changes.FontSize=11;changes.Margin=new Thickness(10,0,10,0);Grid.SetColumn(changes,1);toolbar.Children.Add(changes);saveIni=MakeButton("Save changes",async()=>await Execute("ini"),"Primary");saveIni.Margin=new Thickness(0);Grid.SetColumn(saveIni,2);toolbar.Children.Add(saveIni);DockPanel.SetDock(toolbar,Dock.Bottom);dock.Children.Add(toolbar);
            var contents=Column();var source=Column();source.Children.Add(Text("Configuration",12,"#AFAFAF"));
            var modeRow=new Grid{Margin=new Thickness(0,8,0,0)};modeRow.ColumnDefinitions.Add(new ColumnDefinition());modeRow.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});modeRow.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            iniTarget.Items.Add("OptiScaler package");iniTarget.Items.Add("Installed application");iniTarget.SelectedIndex=0;iniTarget.Margin=new Thickness(0,0,12,0);modeRow.Children.Add(iniTarget);Watch(iniTarget);
            var reload=MakeButton("Reload",()=>{if(CanDiscard()&&File.Exists(editedIni))LoadIni(editedIni);},"Quiet");Grid.SetColumn(reload,1);modeRow.Children.Add(reload);var external=MakeButton("Open file",()=>{if(File.Exists(editedIni))Shell(editedIni);},"Quiet");Grid.SetColumn(external,2);modeRow.Children.Add(external);source.Children.Add(modeRow);
            var subjectRow=new Grid{Margin=new Thickness(0,10,0,0)};subjectRow.ColumnDefinitions.Add(new ColumnDefinition());subjectRow.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});iniSubject.Tag="Choose a prepared nightly";subjectRow.Children.Add(iniSubject);Watch(iniSubject);iniBrowse=MakeButton("Browse .exe",BrowseEditorApplication,"Quiet");Grid.SetColumn(iniBrowse,1);iniBrowse.Margin=new Thickness(10,0,0,0);iniBrowse.Visibility=Visibility.Collapsed;subjectRow.Children.Add(iniBrowse);source.Children.Add(subjectRow);
            fileLabel.FontSize=11;fileLabel.Foreground=Brush("#AFAFAF");fileLabel.TextTrimming=TextTrimming.CharacterEllipsis;fileLabel.TextWrapping=TextWrapping.NoWrap;fileLabel.Margin=new Thickness(0,12,0,0);source.Children.Add(fileLabel);resetHint.FontSize=11;resetHint.Foreground=Brush("#AFAFAF");resetHint.Margin=new Thickness(0,5,0,0);source.Children.Add(resetHint);contents.Children.Add(Card(source,new Thickness(18)));
            iniTarget.SelectionChanged+=(s,e)=>{
                if(refreshing)return;int next=iniTarget.SelectedIndex;
                if(!CanDiscard()){refreshing=true;iniTarget.SelectedIndex=currentIniTarget;refreshing=false;return;}
                currentIniTarget=next;ClearIniSelection();RefreshIniSubjects();LoadSelectedSubject();
            };
            iniSubject.SelectionChanged+=(s,e)=>{if(refreshing)return;if(!CanDiscard()){refreshing=true;SetSubject(editedIni);refreshing=false;return;}LoadSelectedSubject();};
            var views=Row();views.Margin=new Thickness(0,0,0,14);
            quickView.Content="Quick controls";quickView.GroupName="EditorView";quickView.IsChecked=true;quickView.Style=(Style)FindResource("EditorTab");fullView.Content="All settings";fullView.GroupName="EditorView";fullView.Style=(Style)FindResource("EditorTab");quickView.Checked+=(s,e)=>{quickSettings.Visibility=Visibility.Visible;allSettings.Visibility=Visibility.Collapsed;};fullView.Checked+=(s,e)=>{quickSettings.Visibility=Visibility.Collapsed;allSettings.Visibility=Visibility.Visible;RenderIniRows();};views.Children.Add(quickView);views.Children.Add(fullView);contents.Children.Add(views);
            contents.Children.Add(quickSettings);quickSettings.Children.Add(FieldsCard("Upscaling",null,ProfileFields.Take(3).ToArray(),3));quickSettings.Children.Add(FieldsCard("Frame generation","Input is the game's source. Output is the engine OptiScaler uses.",ProfileFields.Skip(3).Take(4).ToArray(),4));
            var spoofBox=Column();spoofBox.Children.Add(Text("GPU spoofing",15));foreach(var option in new[]{new Choice("auto","Automatic"),new Choice("off","Disabled"),new Choice("streamline","Partial � Streamline only"),new Choice("full","Full spoofing"),new Choice("custom","Custom settings")})spoof.Items.Add(option);SetChoice(spoof,"auto");spoof.Margin=new Thickness(0,12,0,0);spoofBox.Children.Add(spoof);spoofHint.FontSize=11;spoofHint.Foreground=Brush("#AFAFAF");spoofHint.Margin=new Thickness(0,8,0,0);spoofBox.Children.Add(spoofHint);spoof.SelectionChanged+=(s,e)=>{if(!loadingIni){dirty.Add("spoofing");SyncRawFromQuick();}UpdateSpoofHint();UpdateDirty();};Watch(spoof);quickSettings.Children.Add(Card(spoofBox));
            var advanced=Column();advanced.Children.Add(FieldsCard("Models","These options depend on your GPU and installed libraries.",ProfileFields.Skip(7).ToArray(),2));var profiles=Row();profiles.Children.Add(MakeButton("Recommended profile",StageDefaults,"Quiet"));profiles.Children.Add(MakeButton("Saved profile",StageSavedProfile,"Quiet"));profiles.Children.Add(MakeButton("Save profile",SaveProfile,"Quiet"));advanced.Children.Add(profiles);quickSettings.Children.Add(new Expander{Header="Models and profiles",Content=advanced,Foreground=Brush("#AFAFAF"),Margin=new Thickness(2,4,2,16)});
            BuildAllSettings();contents.Children.Add(allSettings);dock.Children.Add(Scroll(contents));AddBody(page,dock);UpdateDirty();
        }
        void SetSubject(string path){iniSubject.SelectedItem=iniSubject.Items.OfType<Choice>().FirstOrDefault(x=>string.Equals(x.Value,path,StringComparison.OrdinalIgnoreCase));}
        void RefreshIniSubjects(string preferred=null){
            bool previous=refreshing;refreshing=true;try{
                var old=iniSubject.SelectedItem as Choice;string selected=preferred??(old==null?null:old.Value);iniSubject.Items.Clear();
                if(iniTarget.SelectedIndex==0){if(Directory.Exists(BuildsRoot))foreach(string folder in Directory.GetDirectories(BuildsRoot).Where(x=>File.Exists(FilePath.Combine(x,"instalacao.json"))).OrderByDescending(Directory.GetLastWriteTimeUtc))iniSubject.Items.Add(new Choice(FilePath.Combine(folder,"OptiScaler.ini"),FilePath.GetFileName(folder)));}
                else{
                    try{foreach(var game in ReadLibrary().games.OrderBy(x=>x.Name))iniSubject.Items.Add(new Choice(FilePath.Combine(game.Folder,"OptiScaler.ini"),game.Name+" � "+game.OptiScaler));}catch(Exception e){status.Text=DescribeException(e);}
                    string manual=preferred??editedIni;if(!string.IsNullOrWhiteSpace(manual)&&!iniSubject.Items.OfType<Choice>().Any(x=>x.Value.Equals(manual,StringComparison.OrdinalIgnoreCase)))iniSubject.Items.Add(new Choice(manual,FilePath.GetFileName(FilePath.GetDirectoryName(manual))+" � selected application"));
                }
                SetSubject(selected);if(iniSubject.SelectedIndex<0&&iniSubject.Items.Count>0)iniSubject.SelectedIndex=0;iniSubject.Tag=iniTarget.SelectedIndex==0?"Choose a prepared nightly":"Choose an installed application";iniBrowse.Visibility=iniTarget.SelectedIndex==1?Visibility.Visible:Visibility.Collapsed;
            }finally{refreshing=previous;}
        }
        void ClearIniSelection(){editedIni=null;dirty.Clear();iniEntries.Clear();rawValues.Clear();fileLabel.Text="No configuration selected";fileLabel.ToolTip=null;resetHint.Text="Choose a package or an application's executable to continue.";RenderIniRows();UpdateDirty();}
        void LoadSelectedSubject(){var selected=iniSubject.SelectedItem as Choice;if(selected==null||!LoadIni(selected.Value))ClearIniSelection();}
        bool SelectApplicationIni(string path){
            if(!CanDiscard()||!File.Exists(path))return false;refreshing=true;try{iniTarget.SelectedIndex=1;currentIniTarget=1;RefreshIniSubjects(path);SetSubject(path);}finally{refreshing=false;}return LoadIni(path);
        }
        void BrowseEditorApplication(){var dialog=new Microsoft.Win32.OpenFileDialog{Filter="Application executable (*.exe)|*.exe",CheckFileExists=true,Title="Choose the application whose OptiScaler.ini you want to edit"};if(dialog.ShowDialog(this)!=true)return;string path=FilePath.Combine(FilePath.GetDirectoryName(dialog.FileName),"OptiScaler.ini");if(!File.Exists(path))throw new AppError("E_INI_PATH",40,"This application does not have an OptiScaler.ini beside its executable.");SelectApplicationIni(path);}
        void StageFactoryDefaults(){
            string path=FindIniDefaults(editedIni);if(path==null)throw new AppError("E_INI_DEFAULTS",40,"No original defaults are saved for this version. Update the matching package first, then try again.");
            var defaults=new IniDocument(path).Entries();loadingIni=true;try{
                foreach(var entry in defaults){if(!iniEntries.Any(x=>x.Id.Equals(entry.Id,StringComparison.OrdinalIgnoreCase)))iniEntries.Add(entry);rawValues[entry.Id]=entry.Value;dirty.Add("raw:"+entry.Id);var known=ProfileFields.FirstOrDefault(x=>IniId(x.Section,x.Key).Equals(entry.Id,StringComparison.OrdinalIgnoreCase));if(known!=null){SetChoice(editors[known.ConfigKey],entry.Value);dirty.Add(known.ConfigKey);}}SetChoice(spoof,"custom");dirty.Remove("spoofing");
            }finally{loadingIni=false;}RenderIniRows();UpdateSpoofHint();UpdateDirty();status.Text="Original release defaults staged for the selected file. Save changes to apply; custom keys are kept.";
        }
    }
}
