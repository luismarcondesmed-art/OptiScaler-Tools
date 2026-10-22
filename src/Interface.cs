using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Threading;
using WPath=System.Windows.Shapes.Path;
using FilePath=System.IO.Path;

internal static partial class Program
{
    sealed class UiLogRow {public string Time {get;set;}public string Level {get;set;}public string Code {get;set;}public string Message {get;set;}}
    sealed class MainWindow : Window
    {
        readonly Grid canvas=new Grid(),host=new Grid();
        readonly List<FrameworkElement> pages=new List<FrameworkElement>();
        readonly List<RadioButton> navigation=new List<RadioButton>();
        readonly List<Control> actions=new List<Control>();
        readonly TextBlock status=new TextBlock(),packageVersion=new TextBlock(),streamVersion=new TextBlock(),fileLabel=new TextBlock(),changes=new TextBlock(),spoofHint=new TextBlock(),materialInfo=new TextBlock(),emptyLog=new TextBlock();
        readonly TextBox gamePath=new TextBox(),activity=new TextBox(),search=new TextBox();
        readonly ComboBox packages=new ComboBox(),proxies=new ComboBox(),spoof=new ComboBox(),iniTarget=new ComboBox(),logFiles=new ComboBox(),levels=new ComboBox();
        readonly CheckBox force=new CheckBox(),openAfter=new CheckBox(),useGui=new CheckBox(),patcher=new CheckBox();
        readonly DataGrid logs=new DataGrid();readonly ProgressBar progress=new ProgressBar();readonly Expander activityPanel=new Expander();
        readonly DataGrid gameList=new DataGrid();readonly TextBlock gameCount=new TextBlock(),gameDetails=new TextBlock();Button updateAll;
        readonly Dictionary<string,ComboBox> editors=new Dictionary<string,ComboBox>();
        readonly Dictionary<string,string> rawValues=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string,TextBox> rawEditors=new Dictionary<string,TextBox>(StringComparer.OrdinalIgnoreCase);
        readonly TextBox iniSearch=new TextBox();readonly ComboBox iniSection=new ComboBox();readonly TextBlock iniCount=new TextBlock();
        readonly StackPanel allRows=new StackPanel(),quickSettings=new StackPanel(),allSettings=new StackPanel();
        List<IniDocument.Entry> iniEntries=new List<IniDocument.Entry>();
        readonly HashSet<string> dirty=new HashSet<string>();readonly List<UiLogRow> logRows=new List<UiLogRow>();
        readonly ObservableCollection<UiLogRow> visibleRows=new ObservableCollection<UiLogRow>();
        Button update,openLatest,install,saveIni;bool busy,loadingIni,refreshing;string editedIni;int pageIndex,currentIniTarget;
        internal MainWindow() {
            Title="OptiScaler Tools";Width=1160;Height=820;MinWidth=1030;MinHeight=740;
            WindowStartupLocation=WindowStartupLocation.CenterScreen;Background=Brush("#20242C");
            FontFamily=new FontFamily("Segoe UI Variable Text, Segoe UI");FontSize=13;Foreground=Brush("#D5DAE3");
            using(var theme=typeof(Program).Assembly.GetManifestResourceStream("Theme.xaml"))Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Load(theme));
            canvas.Background=Brush("#20242C");canvas.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(226)});canvas.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});Content=canvas;
            BuildSidebar();
            var right=new Grid();right.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});right.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});Grid.SetColumn(right,1);canvas.Children.Add(right);
            right.Children.Add(host);
            var footer=new Grid{Margin=new Thickness(34,0,34,18)};footer.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});footer.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            progress.Height=3;progress.IsIndeterminate=true;progress.Foreground=Brush("#8092B8");progress.Background=Brush("#394352");progress.Visibility=Visibility.Collapsed;progress.Margin=new Thickness(0,0,0,10);footer.Children.Add(progress);
            status.Text="Ready";status.Foreground=Brush("#A5B1C4");status.FontSize=12;Grid.SetRow(status,1);footer.Children.Add(status);Grid.SetRow(footer,1);right.Children.Add(footer);
            BuildUpdatePage();BuildInstallPage();BuildEditorPage();BuildLogsPage();BuildLibraryPage();SelectPage(0);
            LogReceived=Receive;
            SourceInitialized+=(s,e)=>ApplyBackdrop();
            Loaded+=(s,e)=>{try{using(var gate=AcquireLock()){Organize();DiscoverGames();}RefreshState();LoadPackageIni(false);Append("Ready to update, install, or edit your configuration.");}catch(Exception ex){ShowError(ex);}};
            Closing+=(s,e)=>{if(busy){e.Cancel=true;status.Text="Please wait for the current operation to finish.";}else if(!CanDiscard())e.Cancel=true;};
            Closed+=(s,e)=>LogReceived=null;
            RefreshState();
        }
        static SolidColorBrush Brush(string color){return (SolidColorBrush)new BrushConverter().ConvertFromString(color);}
        TextBlock Text(string value,double size=13,string color=null){return new TextBlock{Text=value,FontSize=size,Foreground=Brush(color??"#D5DAE3"),TextWrapping=TextWrapping.Wrap};}
        StackPanel Column(){return new StackPanel{Orientation=Orientation.Vertical};}
        StackPanel Row(){return new StackPanel{Orientation=Orientation.Horizontal};}
        Border Card(UIElement child,Thickness? padding=null){return new Border{Background=Brush("#C02B303A"),BorderBrush=Brush("#60495364"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12),Padding=padding??new Thickness(20),Margin=new Thickness(0,0,0,16),Child=child};}
        Button MakeButton(string label,Action click,string style=null){var b=new Button{Content=label,Margin=new Thickness(0,0,10,0)};if(style!=null)b.Style=(Style)FindResource(style);b.Click+=(s,e)=>{try{click();}catch(Exception ex){ShowError(ex);}};actions.Add(b);return b;}
        void Watch(Control control){actions.Add(control);}
        void BuildSidebar(){
            var sidebar=new DockPanel{Margin=new Thickness(0)};
            var pane=new Border{Background=Brush("#B8242831"),BorderBrush=Brush("#60505A6C"),BorderThickness=new Thickness(0,0,1,0),Padding=new Thickness(14,27,14,18),Child=sidebar};canvas.Children.Add(pane);
            var brand=Column();brand.Margin=new Thickness(12,0,0,30);brand.Children.Add(Text("OptiScaler",23));brand.Children.Add(new TextBlock{Text="TOOLS",FontSize=10,Foreground=Brush("#8491A4"),Margin=new Thickness(1,5,0,0)});DockPanel.SetDock(brand,Dock.Top);sidebar.Children.Add(brand);
            var bottom=Column();bottom.Margin=new Thickness(13,18,0,0);bottom.Children.Add(Text("Portable workspace",11,"#8794A6"));materialInfo.Text="Soft tint";materialInfo.FontSize=11;materialInfo.Foreground=Brush("#8794A6");materialInfo.Margin=new Thickness(0,5,0,0);bottom.Children.Add(materialInfo);DockPanel.SetDock(bottom,Dock.Bottom);sidebar.Children.Add(bottom);
            var nav=Column();sidebar.Children.Add(nav);
            string[] names={"1. Update OptiScaler","2. Install","Edit .ini","Logs / setup","Installed games"};
            string[] shapes={"M12,3 L12,15 M7,10 L12,15 17,10 M4,17 L4,21 20,21 20,17","M4,7 L12,3 20,7 20,17 12,21 4,17 Z M4,7 L12,11 20,7 M12,11 L12,21","M4,6 L20,6 M4,12 L20,12 M4,18 L20,18 M8,3 L8,9 M16,9 L16,15 M10,15 L10,21","M5,3 L16,3 20,7 20,21 5,21 Z M16,3 L16,7 20,7 M8,11 L17,11 M8,15 L17,15","M7,7 L17,7 21,17 18,20 14,16 10,16 6,20 3,17 Z M6,11 L10,11 M8,9 L8,13 M16,11 L16,12 M18,13 L18,14"};
            for(int i=0;i<names.Length;i++){
                int index=i;var content=Row();content.VerticalAlignment=VerticalAlignment.Center;
                content.Children.Add(new WPath{Data=Geometry.Parse(shapes[i]),Stroke=Brush("#A2B3D2"),StrokeThickness=1.55,Width=21,Height=21,Stretch=Stretch.Uniform,Margin=new Thickness(0,0,12,0)});
                content.Children.Add(new TextBlock{Text=names[i],FontSize=13,VerticalAlignment=VerticalAlignment.Center});
                var item=new RadioButton{Content=content,GroupName="Navigation",Style=(Style)FindResource("Navigation")};item.Checked+=(s,e)=>SelectPage(index);nav.Children.Add(item);navigation.Add(item);
            }
        }
        Grid Page(string title,string subtitle){
            var page=new Grid{Margin=new Thickness(34,28,34,16),Visibility=Visibility.Collapsed};page.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});page.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});
            var heading=Column();heading.Margin=new Thickness(0,0,0,24);var name=Text(title,29);name.FontWeight=FontWeights.SemiBold;heading.Children.Add(name);heading.Children.Add(new TextBlock{Text=subtitle,Foreground=Brush("#A5B3C8"),Margin=new Thickness(0,7,0,0),TextWrapping=TextWrapping.Wrap});page.Children.Add(heading);pages.Add(page);host.Children.Add(page);return page;
        }
        void AddBody(Grid page,UIElement body){Grid.SetRow(body,1);page.Children.Add(body);}
        ScrollViewer Scroll(UIElement body){return new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Padding=new Thickness(0,0,8,0)};}
        void SelectPage(int index){if(index>=pages.Count)return;pageIndex=index;for(int i=0;i<pages.Count;i++)pages[i].Visibility=i==index?Visibility.Visible:Visibility.Collapsed;if(navigation[index].IsChecked!=true)navigation[index].IsChecked=true;if(index==3)RenderLogs();}
        void BuildUpdatePage(){
            var page=Page("Update OptiScaler","The latest nightly, prepared with your settings.");var body=Column();
            var hero=Column();hero.HorizontalAlignment=HorizontalAlignment.Stretch;hero.Margin=new Thickness(0,20,0,20);
            var small=Text("LOCAL PACKAGE",11,"#8190A5");small.HorizontalAlignment=HorizontalAlignment.Center;hero.Children.Add(small);
            packageVersion.FontSize=25;packageVersion.FontWeight=FontWeights.SemiBold;packageVersion.HorizontalAlignment=HorizontalAlignment.Center;packageVersion.Margin=new Thickness(0,10,0,0);hero.Children.Add(packageVersion);
            streamVersion.Foreground=Brush("#A5B3C8");streamVersion.HorizontalAlignment=HorizontalAlignment.Center;streamVersion.Margin=new Thickness(0,9,0,30);hero.Children.Add(streamVersion);
            update=MakeButton("Update",async()=>await Execute("prepare"),"Primary");update.Width=205;update.HorizontalAlignment=HorizontalAlignment.Center;hero.Children.Add(update);
            var sub=Row();sub.HorizontalAlignment=HorizontalAlignment.Center;sub.Margin=new Thickness(0,14,0,0);openLatest=MakeButton("Open latest",OpenReady,"Quiet");sub.Children.Add(openLatest);sub.Children.Add(MakeButton("Update Streamline",async()=>await Execute("streamline"),"Quiet"));hero.Children.Add(sub);body.Children.Add(Card(hero,new Thickness(26)));
            activityPanel.Header="Activity";activityPanel.Foreground=Brush("#A5B3C8");activityPanel.Margin=new Thickness(3,8,3,0);
            activity.IsReadOnly=true;activity.TextWrapping=TextWrapping.Wrap;activity.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;activity.FontFamily=new FontFamily("Cascadia Mono, Consolas");activity.FontSize=11;activity.Height=180;activity.Margin=new Thickness(0,12,0,0);activity.Background=Brush("#60272D37");activityPanel.Content=activity;body.Children.Add(activityPanel);AddBody(page,Scroll(body));
        }
        void BuildInstallPage(){
            var page=Page("Install","Choose a game. We will take care of the files.");var body=Column();var form=Column();
            form.Children.Add(Text("Game executable",14));
            var gameRow=new Grid{Margin=new Thickness(0,9,0,20)};gameRow.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});gameRow.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            gamePath.IsReadOnly=true;gamePath.Text=Settings.Get("game.executable");gamePath.Tag="Select the game executable.";gamePath.ToolTip="Choose the real Windows x64 game executable.";gamePath.Margin=new Thickness(0,0,10,0);gameRow.Children.Add(gamePath);
            var browse=MakeButton("Browse.",SelectGame);Grid.SetColumn(browse,1);browse.Margin=new Thickness(0);gameRow.Children.Add(browse);form.Children.Add(gameRow);
            var pair=new Grid();pair.ColumnDefinitions.Add(new ColumnDefinition());pair.ColumnDefinitions.Add(new ColumnDefinition());
            var pack=Column();pack.Margin=new Thickness(0,0,10,0);pack.Children.Add(Text("Nightly package",12,"#A5B3C8"));packages.Margin=new Thickness(0,8,0,0);packages.Tag="Prepare a package first";pack.Children.Add(packages);Watch(packages);pair.Children.Add(pack);
            var loader=Column();loader.Margin=new Thickness(10,0,0,0);loader.Children.Add(Text("Load method",12,"#A5B3C8"));proxies.Margin=new Thickness(0,8,0,0);proxies.Items.Add(new Choice("auto","Automatic � dxgi  .asi  winmm"));proxies.Items.Add(new Choice("dxgi","dxgi.dll"));proxies.Items.Add(new Choice("asi","OptiScaler.asi + ASI loader"));proxies.Items.Add(new Choice("winmm","winmm.dll"));SetChoice(proxies,Settings.Get("game.proxy"));loader.Children.Add(proxies);Watch(proxies);Grid.SetColumn(loader,1);pair.Children.Add(loader);form.Children.Add(pair);
            patcher.Content="Add OptiPatcher when this game is supported";patcher.IsChecked=Settings.Get("game.optipatcher")=="auto";patcher.Margin=new Thickness(0,19,0,0);form.Children.Add(patcher);Watch(patcher);body.Children.Add(Card(form));
            var primary=Column();primary.Margin=new Thickness(0,8,0,0);install=MakeButton("Install",async()=>await Execute("game"),"Primary");install.Width=190;install.HorizontalAlignment=HorizontalAlignment.Left;primary.Children.Add(install);
            var sub=Row();sub.Margin=new Thickness(0,9,0,0);sub.Children.Add(MakeButton("Open folder",()=>{if(File.Exists(gamePath.Text))Shell(FilePath.GetDirectoryName(gamePath.Text));},"Quiet"));sub.Children.Add(MakeButton("Edit OptiScaler.ini",OpenGameIni,"Quiet"));primary.Children.Add(sub);body.Children.Add(primary);
            var note=Text("Close the game first. Existing files are backed up before installation.",12,"#8491A3");note.Margin=new Thickness(0,25,0,0);body.Children.Add(note);body.Children.Add(MakeButton("Manage installed games",()=>{SelectPage(4);RefreshGames();},"Quiet"));AddBody(page,Scroll(body));
        }
        void BuildLibraryPage(){
            var page=Page("Installed games","Your managed installations, in one place.");var body=new DockPanel();
            var top=Column();var buttons=Row();updateAll=MakeButton("Update all",async()=>await Execute("all"),"Primary");buttons.Children.Add(updateAll);buttons.Children.Add(MakeButton("Import installed",ImportInstalled,"Quiet"));buttons.Children.Add(MakeButton("Refresh",()=>{using(var gate=AcquireLock())DiscoverGames();RefreshGames();},"Quiet"));top.Children.Add(buttons);
            var hint=Text("Downloads the latest nightly once, then updates each game. Keeps each game's INI values and load method. Close your games first.",12,"#A5B3C8");hint.Margin=new Thickness(0,16,0,14);top.Children.Add(hint);gameCount.Margin=new Thickness(0,0,0,12);gameCount.FontSize=12;top.Children.Add(gameCount);DockPanel.SetDock(top,Dock.Top);body.Children.Add(top);
            var bottom=Column();bottom.Margin=new Thickness(0,16,0,0);gameDetails.FontSize=11;gameDetails.Foreground=Brush("#A5B3C8");gameDetails.Margin=new Thickness(0,0,0,12);bottom.Children.Add(gameDetails);var tools=Row();tools.Children.Add(MakeButton("Open folder",()=>{var selected=SelectedGame();if(Directory.Exists(selected.Folder))Shell(selected.Folder);},"Quiet"));tools.Children.Add(MakeButton("Edit INI",()=>{var selected=SelectedGame();if(CanDiscard()&&LoadIni(FilePath.Combine(selected.Folder,"OptiScaler.ini"))){refreshing=true;iniTarget.SelectedIndex=2;currentIniTarget=2;refreshing=false;SelectPage(2);}},"Quiet"));var remove=MakeButton("Remove from list",()=>{var selected=SelectedGame();ForgetGame(selected.Executable);RefreshGames();status.Text="Removed from the list. The game's files were kept.";},"Quiet");remove.ToolTip="Stops managing this game. Does not uninstall or delete game files. Use Import installed to add it again.";tools.Children.Add(remove);bottom.Children.Add(tools);DockPanel.SetDock(bottom,Dock.Bottom);body.Children.Add(bottom);
            gameList.IsReadOnly=true;gameList.CanUserAddRows=false;gameList.CanUserDeleteRows=false;gameList.AutoGenerateColumns=false;gameList.SelectionMode=DataGridSelectionMode.Single;gameList.MinHeight=150;Watch(gameList);
            foreach(var column in new[]{new[]{"Game","Name"},new[]{"OptiScaler","OptiScaler"},new[]{"Streamline","Streamline"},new[]{"Loader","Proxy"},new[]{"Status","Status"}})gameList.Columns.Add(new DataGridTextColumn{Header=column[0],Binding=new System.Windows.Data.Binding(column[1]),Width=new DataGridLength(column[1]=="Name"?1.5:column[1]=="Proxy"?.7:1,DataGridLengthUnitType.Star)});
            gameList.SelectionChanged+=(s,e)=>{var game=gameList.SelectedItem as InstalledGame;gameDetails.Text=game==null?"Select a game to open its folder or edit its INI.":game.Executable+"\nLast update: "+(game.UpdatedUtc??"Not recorded")+"  |  Result: "+(game.ErrorCode??"OK");};body.Children.Add(gameList);AddBody(page,body);
        }
        InstalledGame SelectedGame(){var game=gameList.SelectedItem as InstalledGame;if(game==null)throw new AppError("E_GAME_SELECTION",54,"Select a registered game first.");return game;}
        void ImportInstalled(){var dialog=new Microsoft.Win32.OpenFileDialog{Filter="Game executable (*.exe)|*.exe",CheckFileExists=true,Title="Import a game previously installed with OptiScaler Tools"};if(dialog.ShowDialog(this)!=true)return;ImportGame(dialog.FileName);RefreshGames();status.Text="Installed game imported.";}
        void RefreshGames(){
            if(updateAll==null)return;string selected=(gameList.SelectedItem as InstalledGame)==null?null:((InstalledGame)gameList.SelectedItem).Executable;
            try{var games=ReadLibrary().games.OrderBy(x=>x.Name,StringComparer.OrdinalIgnoreCase).ToList();gameList.ItemsSource=games;gameCount.Text=games.Count==0?"No games registered yet. New installations are added automatically; use Import installed for earlier installations.":games.Count+" managed installation"+(games.Count==1?"":"s");gameList.SelectedItem=games.FirstOrDefault(x=>x.Executable==selected);updateAll.IsEnabled=!busy&&games.Count>0;}
            catch(Exception e){gameCount.Text=ErrorCode(e)+": "+DescribeException(e);updateAll.IsEnabled=false;}
        }
        ComboBox Field(ProfileField field){
            var combo=new ComboBox{Margin=new Thickness(0,7,0,0),ToolTip="["+field.Section+"] "+field.Key};System.Windows.Automation.AutomationProperties.SetName(combo,field.Label);foreach(var opt in field.Options)combo.Items.Add(opt);SetChoice(combo,Settings.Get(field.ConfigKey));editors.Add(field.ConfigKey,combo);Watch(combo);
            combo.SelectionChanged+=(s,e)=>{if(!loadingIni){dirty.Add(field.ConfigKey);SyncRawFromQuick();UpdateDirty();}};return combo;
        }
        UIElement FieldsCard(string title,string subtitle,ProfileField[] fields,int columns){
            var box=Column();var heading=Text(title,15);heading.FontWeight=FontWeights.Medium;box.Children.Add(heading);if(subtitle!=null)box.Children.Add(new TextBlock{Text=subtitle,Foreground=Brush("#8491A3"),FontSize=11,Margin=new Thickness(0,5,0,6),TextWrapping=TextWrapping.Wrap});
            var grid=new Grid{Margin=new Thickness(0,13,0,0)};for(int c=0;c<columns;c++)grid.ColumnDefinitions.Add(new ColumnDefinition());
            for(int i=0;i<fields.Length;i++){
                int r=i/columns;if(grid.RowDefinitions.Count<=r)grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
                var cell=Column();cell.Margin=new Thickness(i%columns==0?0:9,0,i%columns==columns-1?0:9,13);cell.Children.Add(Text(fields[i].Label,12,"#A5B3C8"));cell.Children.Add(Field(fields[i]));Grid.SetColumn(cell,i%columns);Grid.SetRow(cell,r);grid.Children.Add(cell);
            }box.Children.Add(grid);return Card(box,new Thickness(19,17,19,5));
        }
        void BuildEditorPage(){
            var page=Page("Edit .ini","Choose a file, adjust your settings, then save.");var dock=new DockPanel();
            var toolbar=new Grid{Margin=new Thickness(0,2,0,0)};toolbar.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});toolbar.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});toolbar.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            var reset=MakeButton("Predefined values",StageDefaults);toolbar.Children.Add(reset);changes.Foreground=Brush("#A5B3C8");changes.VerticalAlignment=VerticalAlignment.Center;changes.FontSize=11;changes.Margin=new Thickness(4,0,10,0);Grid.SetColumn(changes,1);toolbar.Children.Add(changes);saveIni=MakeButton("Save changes",async()=>await Execute("ini"),"Primary");saveIni.Margin=new Thickness(0);Grid.SetColumn(saveIni,2);toolbar.Children.Add(saveIni);DockPanel.SetDock(toolbar,Dock.Bottom);dock.Children.Add(toolbar);
            var contents=Column();var source=Column();var sourceRow=new Grid();sourceRow.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});sourceRow.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});sourceRow.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            iniTarget.Items.Add("Latest / selected package");iniTarget.Items.Add("Installed game");iniTarget.Items.Add("Choose another file.");iniTarget.SelectedIndex=0;iniTarget.Margin=new Thickness(0,0,12,0);sourceRow.Children.Add(iniTarget);Watch(iniTarget);
            var reload=MakeButton("Reload",()=>{if(CanDiscard()&&File.Exists(editedIni))LoadIni(editedIni);},"Quiet");Grid.SetColumn(reload,1);sourceRow.Children.Add(reload);var external=MakeButton("Open file",()=>{if(File.Exists(editedIni))Shell(editedIni);},"Quiet");Grid.SetColumn(external,2);sourceRow.Children.Add(external);source.Children.Add(sourceRow);
            fileLabel.FontSize=11;fileLabel.Foreground=Brush("#8390A4");fileLabel.TextTrimming=TextTrimming.CharacterEllipsis;fileLabel.TextWrapping=TextWrapping.NoWrap;fileLabel.Margin=new Thickness(0,10,0,0);source.Children.Add(fileLabel);contents.Children.Add(Card(source,new Thickness(16)));
            iniTarget.SelectionChanged+=(s,e)=>{
                if(refreshing)return;int next=iniTarget.SelectedIndex;bool loaded=false;
                try{if(CanDiscard())loaded=next==0?LoadPackageIni(false):next==1?LoadGameIni():SelectIni();}catch(Exception error){ShowError(error);}
                if(loaded)currentIniTarget=next;else{refreshing=true;iniTarget.SelectedIndex=currentIniTarget;refreshing=false;}
            };
            var views=Row();views.Margin=new Thickness(0,0,0,14);views.Children.Add(MakeButton("Quick controls",()=>{quickSettings.Visibility=Visibility.Visible;allSettings.Visibility=Visibility.Collapsed;},"Quiet"));views.Children.Add(MakeButton("All settings",()=>{quickSettings.Visibility=Visibility.Collapsed;allSettings.Visibility=Visibility.Visible;RenderIniRows();},"Quiet"));contents.Children.Add(views);
            contents.Children.Add(quickSettings);
            quickSettings.Children.Add(FieldsCard("Upscaling",null,ProfileFields.Take(3).ToArray(),3));
            quickSettings.Children.Add(FieldsCard("Frame generation","Input is the game's source. Output is the engine OptiScaler uses.",ProfileFields.Skip(3).Take(4).ToArray(),4));
            var spoofBox=Column();spoofBox.Children.Add(Text("GPU spoofing",15));spoof.Items.Add(new Choice("auto","Automatic"));spoof.Items.Add(new Choice("off","Disabled"));spoof.Items.Add(new Choice("streamline","Partial � Streamline only"));spoof.Items.Add(new Choice("full","Full spoofing"));spoof.Items.Add(new Choice("custom","Custom � keep existing settings"));SetChoice(spoof,"auto");spoof.Margin=new Thickness(0,12,0,0);spoofBox.Children.Add(spoof);spoofHint.FontSize=11;spoofHint.Foreground=Brush("#8190A4");spoofHint.Margin=new Thickness(0,8,0,0);spoofHint.TextWrapping=TextWrapping.Wrap;spoofBox.Children.Add(spoofHint);spoof.SelectionChanged+=(s,e)=>{if(!loadingIni){dirty.Add("spoofing");SyncRawFromQuick();}UpdateSpoofHint();UpdateDirty();};Watch(spoof);quickSettings.Children.Add(Card(spoofBox,new Thickness(19,16,19,16)));
            var advanced=Column();advanced.Children.Add(FieldsCard("Model options","These options depend on your GPU and the installed DLLs.",ProfileFields.Skip(7).ToArray(),2));
            var profile=Row();profile.Children.Add(MakeButton("Use saved profile",StageSavedProfile,"Quiet"));profile.Children.Add(MakeButton("Save as default profile",SaveProfile,"Quiet"));advanced.Children.Add(profile);
            var expander=new Expander{Header="Models and default profile",Content=advanced,Foreground=Brush("#A5B3C8"),Margin=new Thickness(2,2,2,15)};quickSettings.Children.Add(expander);BuildAllSettings();contents.Children.Add(allSettings);dock.Children.Add(Scroll(contents));AddBody(page,dock);UpdateDirty();
        }
        static string IniId(string section,string key){return section+"\0"+key;}
        string IniHelp(IniDocument.Entry entry){return "["+entry.Section+"] "+entry.Key+"\n\n"+(string.IsNullOrWhiteSpace(entry.Description)?"No description is provided in this INI file.":entry.Description);}
        void BuildAllSettings(){
            allSettings.Visibility=Visibility.Collapsed;
            var filters=new Grid{Margin=new Thickness(0,0,0,12)};filters.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(215)});filters.ColumnDefinitions.Add(new ColumnDefinition());
            iniSection.Items.Add("All sections");iniSection.SelectedIndex=0;iniSection.Margin=new Thickness(0,0,12,0);filters.Children.Add(iniSection);Watch(iniSection);
            iniSearch.Tag="Search keys, values or descriptions";iniSearch.Height=38;Grid.SetColumn(iniSearch,1);filters.Children.Add(iniSearch);Watch(iniSearch);allSettings.Children.Add(filters);
            iniCount.FontSize=11;iniCount.Margin=new Thickness(0,0,0,12);allSettings.Children.Add(iniCount);allSettings.Children.Add(allRows);
            iniSearch.TextChanged+=(s,e)=>{if(!loadingIni)RenderIniRows();};iniSection.SelectionChanged+=(s,e)=>{if(!loadingIni)RenderIniRows();};
        }
        void RenderIniRows(){
            foreach(var control in rawEditors.Values)actions.Remove(control);rawEditors.Clear();allRows.Children.Clear();
            string query=iniSearch.Text.Trim(),section=iniSection.SelectedIndex>0?iniSection.SelectedItem as string:null;int count=0;string lastSection=null;
            foreach(var entry in iniEntries.GroupBy(x=>x.Id,StringComparer.OrdinalIgnoreCase).Select(x=>x.First())){
                if(section!=null&&!entry.Section.Equals(section,StringComparison.OrdinalIgnoreCase))continue;
                if(query.Length>0&&(entry.Section+" "+entry.Key+" "+rawValues[entry.Id]+" "+entry.Description).IndexOf(query,StringComparison.OrdinalIgnoreCase)<0)continue;
                if(lastSection!=entry.Section){var heading=Text(entry.Section.Length==0?"General":entry.Section,15);heading.Margin=new Thickness(0,14,0,12);allRows.Children.Add(heading);lastSection=entry.Section;}
                var row=new Grid{Margin=new Thickness(0,0,0,9)};row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
                string tip=IniHelp(entry);var label=Text(entry.Key,12);label.Margin=new Thickness(2,8,16,0);label.ToolTip=tip;row.Children.Add(label);
                var box=new TextBox{Text=rawValues[entry.Id],ToolTip=tip,IsEnabled=!busy,Height=38};System.Windows.Automation.AutomationProperties.SetName(box,"["+entry.Section+"] "+entry.Key);Grid.SetColumn(box,1);row.Children.Add(box);rawEditors[entry.Id]=box;Watch(box);
                box.TextChanged+=(s,e)=>{if(loadingIni)return;rawValues[entry.Id]=box.Text;dirty.Add("raw:"+entry.Id);loadingIni=true;try{
                    var known=ProfileFields.FirstOrDefault(x=>IniId(x.Section,x.Key).Equals(entry.Id,StringComparison.OrdinalIgnoreCase));if(known!=null){SetChoice(editors[known.ConfigKey],box.Text);dirty.Add(known.ConfigKey);}
                    if(entry.Section.Equals("Spoofing",StringComparison.OrdinalIgnoreCase)||entry.Section.Equals("fakenvapi",StringComparison.OrdinalIgnoreCase)){SetChoice(spoof,"custom");dirty.Remove("spoofing");}
                }finally{loadingIni=false;}UpdateSpoofHint();UpdateDirty();};
                allRows.Children.Add(row);count++;
            }
            iniCount.Text=count+" of "+iniEntries.Select(x=>x.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count()+" settings � Hover over a key or value to read the INI comments.";
            if(count==0)allRows.Children.Add(Text(iniEntries.Count==0?"Choose an INI file to view its settings.":"No settings match this search.",13));
        }
        void SyncRawFromQuick(){
            if(loadingIni)return;
            loadingIni=true;try{
                foreach(var field in ProfileFields)if(dirty.Contains(field.ConfigKey)){string id=IniId(field.Section,field.Key);rawValues[id]=Selected(editors[field.ConfigKey]);TextBox box;if(rawEditors.TryGetValue(id,out box))box.Text=rawValues[id];}
                if(dirty.Contains("ini.dlss_preset")){
                    string id=IniId("DLSS","RenderPresetOverride");if(rawValues.ContainsKey(id)){rawValues[id]=Selected(editors["ini.dlss_preset"])=="auto"?"auto":"true";dirty.Add("raw:"+id);TextBox box;if(rawEditors.TryGetValue(id,out box))box.Text=rawValues[id];}
                }
                if(dirty.Contains("spoofing")&&Selected(spoof)!="custom"){
                    string mode=Selected(spoof);foreach(var entry in iniEntries.Where(x=>x.Section.Equals("Spoofing",StringComparison.OrdinalIgnoreCase)||x.Section.Equals("fakenvapi",StringComparison.OrdinalIgnoreCase)&&x.Key.Equals("UseFakenvapi",StringComparison.OrdinalIgnoreCase))){
                        if(entry.Section.Equals("Spoofing",StringComparison.OrdinalIgnoreCase)&&!new[]{"StreamlineSpoofing","Dxgi","Vulkan","VulkanExtensionSpoofing","Registry","User32","SpoofHAGS"}.Contains(entry.Key,StringComparer.OrdinalIgnoreCase))continue;
                        string value=entry.Section.Equals("fakenvapi",StringComparison.OrdinalIgnoreCase)?mode=="full"?"true":mode=="off"?"false":"auto":mode=="auto"?"auto":mode=="full"?"true":mode=="streamline"&&entry.Key=="StreamlineSpoofing"?"true":mode=="streamline"&&entry.Key=="SpoofHAGS"?"auto":"false";
                        rawValues[entry.Id]=value;dirty.Add("raw:"+entry.Id);TextBox box;if(rawEditors.TryGetValue(entry.Id,out box))box.Text=value;
                    }
                }
            }finally{loadingIni=false;}
        }
        void BuildLogsPage(){
            var page=Page("Logs / setup","Follow each operation, or change how the app starts.");var dock=new DockPanel();
            var preferences=Column();var checkRow=Row();useGui.Content="Open the graphical interface";useGui.IsChecked=Settings.Bool("app.gui");useGui.Margin=new Thickness(0,0,25,12);openAfter.Content="Open folder on completion";openAfter.IsChecked=Settings.Bool("app.open_output");checkRow.Children.Add(useGui);checkRow.Children.Add(openAfter);preferences.Children.Add(checkRow);Watch(useGui);Watch(openAfter);
            force.Content="Download again instead of using cached files";force.Margin=new Thickness(0,0,0,14);preferences.Children.Add(force);Watch(force);
            var prefsButtons=Row();prefsButtons.Children.Add(MakeButton("Save setup",SavePreferences));prefsButtons.Children.Add(MakeButton("Open TOML",()=>Shell(FilePath.Combine(AppRoot,"OptiScaler-Tools.toml")),"Quiet"));prefsButtons.Children.Add(MakeButton("Clear Streamline cache",async()=>await Execute("clear"),"Quiet"));preferences.Children.Add(prefsButtons);var prefsCard=Card(preferences,new Thickness(18));DockPanel.SetDock(prefsCard,Dock.Top);dock.Children.Add(prefsCard);
            var body=new Grid();body.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});body.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});body.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});body.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            var sessions=new Grid{Margin=new Thickness(0,0,0,12)};sessions.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});sessions.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});logFiles.Tag="No saved sessions yet";sessions.Children.Add(logFiles);var load=MakeButton("Load session",LoadLog,"Quiet");load.Margin=new Thickness(10,0,0,0);Grid.SetColumn(load,1);sessions.Children.Add(load);body.Children.Add(sessions);
            var filters=new Grid{Margin=new Thickness(0,0,0,13)};filters.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(178)});filters.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});levels.Items.Add("All events");levels.Items.Add("Warnings and errors");levels.Items.Add("Errors only");levels.SelectedIndex=0;levels.Margin=new Thickness(0,0,12,0);filters.Children.Add(levels);search.Tag="Search events or codes";search.ToolTip="Search messages or error codes";Grid.SetColumn(search,1);filters.Children.Add(search);Grid.SetRow(filters,1);body.Children.Add(filters);levels.SelectionChanged+=(s,e)=>RenderLogs();search.TextChanged+=(s,e)=>RenderLogs();
            logs.IsReadOnly=true;logs.CanUserAddRows=false;logs.CanUserDeleteRows=false;logs.AutoGenerateColumns=false;logs.SelectionMode=DataGridSelectionMode.Single;logs.ItemsSource=visibleRows;
            logs.Columns.Add(new DataGridTextColumn{Header="Time",Binding=new System.Windows.Data.Binding("Time"),Width=84});logs.Columns.Add(new DataGridTextColumn{Header="Level",Binding=new System.Windows.Data.Binding("Level"),Width=67});logs.Columns.Add(new DataGridTextColumn{Header="Code",Binding=new System.Windows.Data.Binding("Code"),Width=139});logs.Columns.Add(new DataGridTextColumn{Header="Event",Binding=new System.Windows.Data.Binding("Message"),Width=new DataGridLength(1,DataGridLengthUnitType.Star)});
            var rowStyle=new Style(typeof(DataGridRow));var warning=new DataTrigger{Binding=new System.Windows.Data.Binding("Level"),Value="WARN"};warning.Setters.Add(new Setter(Control.ForegroundProperty,Brush("#D0B58B")));rowStyle.Triggers.Add(warning);var error=new DataTrigger{Binding=new System.Windows.Data.Binding("Level"),Value="ERROR"};error.Setters.Add(new Setter(Control.ForegroundProperty,Brush("#D79BA2")));rowStyle.Triggers.Add(error);logs.RowStyle=rowStyle;
            var logArea=new Grid();logArea.Children.Add(logs);emptyLog.Text="No events yet. Run an operation or load a session.";emptyLog.FontSize=12;emptyLog.Foreground=Brush("#8A97A9");emptyLog.HorizontalAlignment=HorizontalAlignment.Center;emptyLog.VerticalAlignment=VerticalAlignment.Center;emptyLog.IsHitTestVisible=false;logArea.Children.Add(emptyLog);Grid.SetRow(logArea,2);body.Children.Add(logArea);
            var links=Row();links.Margin=new Thickness(0,12,0,0);links.Children.Add(MakeButton("Open logs",()=>{Directory.CreateDirectory(LogsRoot);Shell(LogsRoot);},"Quiet"));links.Children.Add(MakeButton("Last result",()=>{string p=FilePath.Combine(LogsRoot,"latest-run.json");if(File.Exists(p))Shell(p);},"Quiet"));Grid.SetRow(links,3);body.Children.Add(links);dock.Children.Add(body);AddBody(page,dock);
        }
        static string Selected(ComboBox c){return c.SelectedItem is Choice?((Choice)c.SelectedItem).Value:"auto";}
        static void SetChoice(ComboBox c,string value){for(int i=0;i<c.Items.Count;i++)if(c.Items[i] is Choice&&((Choice)c.Items[i]).Value.Equals(value,StringComparison.OrdinalIgnoreCase)){c.SelectedIndex=i;return;}c.Items.Add(new Choice(value,"Current: "+value));c.SelectedIndex=c.Items.Count-1;}
        string SelectedPackage(){return packages.SelectedItem==null?LatestBuild():FilePath.Combine(BuildsRoot,(string)packages.SelectedItem);}
        void SelectGame(){var dialog=new Microsoft.Win32.OpenFileDialog{Filter="Game executable (*.exe)|*.exe",CheckFileExists=true,Title="Choose the actual game executable"};if(dialog.ShowDialog(this)!=true)return;if(!IsAmd64(dialog.FileName)){ShowError(new AppError("E_GAME_EXE",50,"Choose a Windows x64 executable."));return;}var values=new Dictionary<string,string>{{"game.executable",dialog.FileName}};SaveSettings(values);gamePath.Text=dialog.FileName;RefreshState();}
        void OpenGameIni(){if(!File.Exists(gamePath.Text))throw new AppError("E_GAME_EXE",50,"Choose a game first.");string path=FilePath.Combine(FilePath.GetDirectoryName(gamePath.Text),"OptiScaler.ini");if(!File.Exists(path))throw new AppError("E_INI_PATH",40,"This game does not have an OptiScaler.ini yet. Install a package first.");Shell(path);}
        bool CanDiscard(){return dirty.Count==0||MessageBox.Show(this,"Discard the editor's unsaved changes?","Unsaved changes",MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes;}
        bool SelectIni(){var dialog=new Microsoft.Win32.OpenFileDialog{Filter="OptiScaler INI (*.ini)|*.ini",CheckFileExists=true,Title="Choose an OptiScaler INI"};return dialog.ShowDialog(this)==true&&LoadIni(dialog.FileName);}
        bool LoadPackageIni(bool confirm=true){if(confirm&&!CanDiscard())return false;string package=SelectedPackage();return package!=null&&LoadIni(FilePath.Combine(package,"OptiScaler.ini"));}
        bool LoadGameIni(){if(!File.Exists(gamePath.Text)){status.Text="Choose a game on the Install page first.";return false;}return LoadIni(FilePath.Combine(FilePath.GetDirectoryName(gamePath.Text),"OptiScaler.ini"));}
        bool LoadIni(string path){
            if(!File.Exists(path)){status.Text="INI not found: "+path;return false;}
            loadingIni=true;
            try{
                var doc=new IniDocument(path);iniEntries=doc.Entries();rawValues.Clear();foreach(var entry in iniEntries)if(!rawValues.ContainsKey(entry.Id))rawValues[entry.Id]=entry.Value;
                foreach(var field in ProfileFields){SetChoice(editors[field.ConfigKey],doc.Get(field.Section,field.Key));var entry=iniEntries.FirstOrDefault(x=>x.Id.Equals(IniId(field.Section,field.Key),StringComparison.OrdinalIgnoreCase));editors[field.ConfigKey].ToolTip=entry==null?"["+field.Section+"] "+field.Key:IniHelp(entry);}
                iniSection.Items.Clear();iniSection.Items.Add("All sections");foreach(string section in iniEntries.Select(x=>x.Section).Distinct(StringComparer.OrdinalIgnoreCase))iniSection.Items.Add(section);iniSection.SelectedIndex=0;iniSearch.Text="";
                spoof.ToolTip="A preset for the spoofing switches. Open All settings to read each switch's original INI comments and edit it individually.";
                string[] keys={"StreamlineSpoofing","Dxgi","Vulkan","VulkanExtensionSpoofing","Registry","User32","SpoofHAGS"};
                string mode=keys.All(x=>doc.Get("Spoofing",x)=="auto")?"auto":keys.All(x=>doc.Get("Spoofing",x)=="true")?"full":keys.All(x=>doc.Get("Spoofing",x)=="false")?"off":doc.Get("Spoofing","StreamlineSpoofing")=="true"&&keys.Where(x=>x!="StreamlineSpoofing"&&x!="SpoofHAGS").All(x=>doc.Get("Spoofing",x)=="false")?"streamline":"custom";
                SetChoice(spoof,mode);editedIni=FilePath.GetFullPath(path);fileLabel.Text=editedIni;fileLabel.ToolTip=editedIni;dirty.Clear();UpdateSpoofHint();UpdateDirty();
            }finally{loadingIni=false;}
            RenderIniRows();
            return true;
        }
        void UpdateDirty(){
            bool valid=!editors.ContainsKey("ini.fg_output")||Selected(editors["ini.fg_input"])!="nvngxfg"||new[]{"auto","nofg","fsrfg"}.Contains(Selected(editors["ini.fg_output"]));
            int count=dirty.Count(x=>!x.StartsWith("raw:")||!ProfileFields.Any(f=>IniId(f.Section,f.Key).Equals(x.Substring(4),StringComparison.OrdinalIgnoreCase)));
            changes.Text=!valid?"NvngxFG input requires FSR FG output.":dirty.Count==0?"No unsaved changes":count+" setting"+(count==1?"":"s")+" changed � not saved yet";changes.Foreground=Brush(valid?"#A5B3C8":"#D0B58B");
            if(saveIni!=null)saveIni.IsEnabled=!busy&&dirty.Count>0&&File.Exists(editedIni)&&valid;
        }
        void UpdateSpoofHint(){string value=Selected(spoof);spoofHint.Text=value=="auto"?"Let OptiScaler choose the appropriate behavior.":value=="streamline"?"DXGI off. Streamline on. Other identity hooks are disabled.":value=="full"?"Enable DXGI, Vulkan, Registry, User32, HAGS, and Fakenvapi spoofing.":value=="off"?"Disable the spoofing switches controlled by this preset.":"Keep the custom switches already present in this file.";}
        void StageProfile(AppSettings profile,string source){
            loadingIni=true;try{foreach(var field in ProfileFields){SetChoice(editors[field.ConfigKey],profile.Get(field.ConfigKey));dirty.Add(field.ConfigKey);}SetChoice(spoof,profile.Get("ini.spoofing"));dirty.Add("spoofing");}finally{loadingIni=false;}
            SyncRawFromQuick();UpdateSpoofHint();UpdateDirty();status.Text=source+" loaded into the editor. Save changes to apply.";
        }
        void StageDefaults(){StageProfile(new AppSettings(),"Predefined values");}
        void StageSavedProfile(){StageProfile(Settings,"Saved profile");}
        void SaveEditor(){
            if(!File.Exists(editedIni))throw new AppError("E_INI_PATH",40,"Choose an existing INI file.");
            if(dirty.Count==0)return;var doc=new IniDocument(editedIni);foreach(var field in ProfileFields)if(dirty.Contains(field.ConfigKey))doc.Set(field.Section,field.Key,Selected(editors[field.ConfigKey]));
            if(dirty.Contains("ini.dlss_preset"))doc.Set("DLSS","RenderPresetOverride",Selected(editors["ini.dlss_preset"])=="auto"?"auto":"true");if(dirty.Contains("spoofing")&&Selected(spoof)!="custom")ApplySpoofing(doc,Selected(spoof));
            foreach(var entry in iniEntries)if(dirty.Contains("raw:"+entry.Id))doc.Set(entry.Section,entry.Key,rawValues[entry.Id]);
            if(doc.Get("FrameGen","FGInput")=="nvngxfg"&&!new[]{"auto","nofg","fsrfg"}.Contains(doc.Get("FrameGen","FGOutput")))throw new AppError("E_INI_COMBINATION",40,"NvngxFG input requires FSR FG output. Use a different input for XeFG or DLSSG output.");
            doc.Save(true);WriteEvent("INFO","INI_SAVED","INI saved. The previous file is available as .bak.",new{path=editedIni,changed=dirty.ToArray()});
            string query=iniSearch.Text,section=iniSection.SelectedItem as string;LoadIni(editedIni);loadingIni=true;try{iniSearch.Text=query;if(section!=null&&iniSection.Items.Contains(section))iniSection.SelectedItem=section;}finally{loadingIni=false;}RenderIniRows();UpdateDirty();
        }
        void SaveProfile(){if(Selected(spoof)=="custom")throw new AppError("E_PROFILE_SPOOFING",40,"Choose a spoofing preset before saving a default profile. Custom INI switches can still be saved to the file.");var values=ProfileFields.ToDictionary(x=>x.ConfigKey,x=>Selected(editors[x.ConfigKey]));values["ini.spoofing"]=Selected(spoof);SaveSettings(values);status.Text="Default profile saved to the TOML file.";}
        void SavePreferences(){SaveSettings(new Dictionary<string,string>{{"app.gui",useGui.IsChecked==true?"true":"false"},{"app.open_output",openAfter.IsChecked==true?"true":"false"},{"game.proxy",Selected(proxies)},{"game.optipatcher",patcher.IsChecked==true?"auto":"off"}});status.Text="Setup saved.";}
        void SaveSettings(Dictionary<string,string> values){var candidate=new AppSettings{FilePath=FilePath.Combine(AppRoot,"OptiScaler-Tools.toml")};foreach(var kv in Settings.Values)candidate.Values[kv.Key]=kv.Value;foreach(var kv in values)candidate.Values[kv.Key]=kv.Value;candidate.Save();Settings=candidate;}
        void RefreshState(){
            string latest=LatestBuild();packageVersion.Text=latest==null?"No package prepared":FilePath.GetFileName(latest);streamVersion.Text="Streamline  "+ReadStreamlineTag();openLatest.IsEnabled=!busy&&latest!=null;install.IsEnabled=!busy&&latest!=null&&File.Exists(gamePath.Text);
            string selected=packages.SelectedItem as string;packages.Items.Clear();if(Directory.Exists(BuildsRoot))foreach(string dir in Directory.GetDirectories(BuildsRoot).Where(x=>!FilePath.GetFileName(x).StartsWith(".")&&File.Exists(FilePath.Combine(x,"instalacao.json"))).OrderByDescending(Directory.GetLastWriteTimeUtc))packages.Items.Add(FilePath.GetFileName(dir));if(selected!=null&&packages.Items.Contains(selected))packages.SelectedItem=selected;else if(packages.Items.Count>0)packages.SelectedIndex=0;
            RefreshLogs();RefreshGames();UpdateDirty();
        }
        void RefreshLogs(){string previous=logFiles.SelectedItem as string;logFiles.Items.Clear();if(Directory.Exists(LogsRoot))foreach(string file in Directory.GetFiles(LogsRoot,"*.jsonl").Where(x=>FilePath.GetFileName(x)!="tests.jsonl").OrderByDescending(File.GetLastWriteTimeUtc))logFiles.Items.Add(FilePath.GetFileName(file));if(previous!=null&&logFiles.Items.Contains(previous))logFiles.SelectedItem=previous;else if(logFiles.Items.Count>0)logFiles.SelectedIndex=0;logFiles.IsEnabled=logFiles.Items.Count>0;logFiles.ToolTip=logFiles.Items.Count==0?"No saved sessions yet":"Choose a previous operation";}
        void LoadLog(){if(logFiles.SelectedItem==null)return;logRows.Clear();foreach(string line in File.ReadLines(FilePath.Combine(LogsRoot,(string)logFiles.SelectedItem))){try{var item=Json.Deserialize<Dictionary<string,object>>(line);logRows.Add(new UiLogRow{Time=Convert.ToString(item["timestamp"]),Level=Convert.ToString(item["level"]),Code=Convert.ToString(item["code"]),Message=Convert.ToString(item["message"])});}catch{}}RenderLogs();}
        void RenderLogs(){visibleRows.Clear();foreach(var row in logRows){if(levels.SelectedIndex==1&&row.Level=="INFO"||levels.SelectedIndex==2&&row.Level!="ERROR")continue;if(!string.IsNullOrWhiteSpace(search.Text)&&!(row.Time+row.Level+row.Code+row.Message).ToLowerInvariant().Contains(search.Text.ToLowerInvariant()))continue;visibleRows.Add(row);}emptyLog.Text=logRows.Count==0?"No events yet. Run an operation or load a session.":"No events match this filter.";emptyLog.Visibility=visibleRows.Count==0?Visibility.Visible:Visibility.Collapsed;}
        void Receive(string line){if(Dispatcher.HasShutdownStarted)return;Dispatcher.BeginInvoke(new Action(()=>{Append(line);var match=System.Text.RegularExpressions.Regex.Match(line,@"^\[([^\]]+)\] \[([^\]]+)\] (.*)$");if(match.Success){logRows.Add(new UiLogRow{Time=DateTime.Now.ToString("HH:mm:ss"),Level=match.Groups[1].Value,Code=match.Groups[2].Value,Message=match.Groups[3].Value});if(pageIndex==3)RenderLogs();}if(busy)status.Text=ShortStatus(line);}));}
        string ShortStatus(string line){if(line.Contains("Downloading"))return "Downloading files.";if(line.Contains("Extracting"))return "Preparing the package.";if(line.Contains("Checking GitHub"))return "Checking the latest release.";if(line.Contains("GAME_START"))return "Installing the selected package.";if(line.Contains("[WARN]"))return "Continuing with a warning. See Logs / setup for details.";return "Working.";}
        void Append(string line){activity.AppendText(DateTime.Now.ToString("HH:mm:ss")+"  "+line+Environment.NewLine);activity.ScrollToEnd();}
        void ShowError(Exception e){if(!busy)try{BeginSession("interface-error");EndSession(false,e);RefreshLogs();}catch{}status.Text=ErrorCode(e)+" � "+DescribeException(e);status.Foreground=Brush("#D79BA2");Append(status.Text);activityPanel.IsExpanded=true;MessageBox.Show(this,DescribeException(e)+"\n\nCode: "+ErrorCode(e)+"\nLogs: "+LogsRoot,"OptiScaler Tools",MessageBoxButton.OK,MessageBoxImage.Error);}
        void Shell(string path){Process.Start(new ProcessStartInfo(path){UseShellExecute=true});}
        void OpenReady(){string latest=LatestBuild();if(latest!=null)Shell(latest);}
        async Task Execute(string action){
            if(busy)return;string game=gamePath.Text,package=SelectedPackage(),proxy=Selected(proxies),patch=patcher.IsChecked==true?"auto":"off";bool shouldOpen=openAfter.IsChecked==true;
            if((action=="game"||action=="all")&&dirty.Count>0){if(!CanDiscard())return;LoadIni(editedIni);}busy=true;foreach(var control in actions)control.IsEnabled=false;progress.Visibility=Visibility.Visible;status.Foreground=Brush("#A5B1C4");status.Text="Starting.";Force=force.IsChecked==true;NoOpen=true;NoPause=true;
            try{
                logRows.Clear();RenderLogs();BeginSession(action);
                if(action=="ini")SaveEditor();else await Task.Run(()=>{if(action=="all"){UpdateAllGames().GetAwaiter().GetResult();}else if(action=="game"){RunSetupProcess(game,package,proxy,patch);var installed=ReadGameManifest(FilePath.GetDirectoryName(game));SessionOpti=installed.optiscaler;SessionStream=installed.streamline;}else using(var gate=AcquireLock()){Organize();if(action=="prepare")Install().GetAwaiter().GetResult();else if(action=="streamline")Streamline().GetAwaiter().GetResult();else ClearStreamline();}});
                EndSession(true,null);status.Text=SessionWarning?"Completed with warnings. See Logs / setup.":action=="ini"?"Changes saved. A backup of the previous INI was created.":"Done. Everything is ready.";status.Foreground=Brush(SessionWarning?"#D0B58B":"#9ABDB1");
                if(action=="prepare"&&dirty.Count==0){RefreshState();string latest=LatestBuild();if(latest!=null&&packages.Items.Contains(FilePath.GetFileName(latest)))packages.SelectedItem=FilePath.GetFileName(latest);LoadPackageIni(false);}if(shouldOpen&&action=="prepare")OpenReady();else if(shouldOpen&&action=="game")Shell(FilePath.GetDirectoryName(game));
            }catch(Exception e){try{EndSession(false,e);}catch{}ShowError(e);}finally{busy=false;progress.Visibility=Visibility.Collapsed;foreach(var control in actions)control.IsEnabled=true;RefreshState();if(SessionId!=null&&logFiles.Items.Contains(SessionId+".jsonl"))logFiles.SelectedItem=SessionId+".jsonl";}
        }
        [StructLayout(LayoutKind.Sequential)]struct Margins{internal int Left,Right,Top,Bottom;}
        [DllImport("dwmapi.dll")]static extern int DwmSetWindowAttribute(IntPtr handle,int attribute,ref int value,int size);
        [DllImport("dwmapi.dll")]static extern int DwmExtendFrameIntoClientArea(IntPtr handle,ref Margins margins);
        void ApplyBackdrop(){
            try{IntPtr handle=new WindowInteropHelper(this).Handle;int light=1;DwmSetWindowAttribute(handle,20,ref light,4);int round=2;DwmSetWindowAttribute(handle,33,ref round,4);int mica=2;
                if(DwmSetWindowAttribute(handle,38,ref mica,4)==0){var margin=new Margins{Left=-1,Right=-1,Top=-1,Bottom=-1};if(DwmExtendFrameIntoClientArea(handle,ref margin)==0){HwndSource.FromHwnd(handle).CompositionTarget.BackgroundColor=Colors.Transparent;Background=Brush("#00FFFFFF");canvas.Background=Brush("#C020242C");materialInfo.Text="Mica � Windows backdrop";}}
            }catch{materialInfo.Text="Soft tint";}
        }
        internal Task TestUpdate(){openAfter.IsChecked=false;return Execute("prepare");}
        internal async Task TestGameLibrary(){
            string folder=FilePath.Combine(Root,"games","Library fixture");Directory.CreateDirectory(folder);string executable=FilePath.Combine(folder,"LibraryGame.exe");File.WriteAllBytes(executable,PeFixture());
            await InstallGame(executable,LatestBuild(),"dxgi","off");RefreshGames();SelectPage(4);Check(gameList.Items.Count==1&&updateAll.IsEnabled,"The installed-games page lists successful installations and enables Update all");
            string path=FilePath.Combine(folder,"OptiScaler.ini");var ini=new IniDocument(path);ini.Set("FrameGen","Enabled","false");ini.Save(false);
            await Execute("all");Check(!busy&&updateAll.IsEnabled&&gameList.Items.Count==1&&((InstalledGame)gameList.Items[0]).LastResult=="Updated"&&new IniDocument(path).Get("FrameGen","Enabled")=="false","Update all works through the interface, refreshes per-game results and keeps game settings");
            gameList.SelectedIndex=0;Check(gameDetails.Text.Contains(executable)&&gameDetails.Text.Contains("OK"),"Selecting a managed game shows its executable, update timestamp and result code");
            Capture(FilePath.Combine(AppRoot,"src","interface-installed-games.png"));
        }
        internal bool ReadyAfterTest{get{return !busy&&update.IsEnabled&&openLatest.IsEnabled&&status.Text.StartsWith("Done.");}}
        internal void Capture(string file){UpdateLayout();var root=(FrameworkElement)Content;var image=new RenderTargetBitmap((int)root.ActualWidth,(int)root.ActualHeight,96,96,PixelFormats.Pbgra32);image.Render(root);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using(var stream=File.Create(file))encoder.Save(stream);}
        internal void CapturePages(string folder){for(int i=0;i<pages.Count;i++){SelectPage(i);UpdateLayout();PumpDispatcher();Capture(FilePath.Combine(folder,i==0?"interface-preview.png":"interface-aba-"+(i+1)+".png"));}SelectPage(2);quickSettings.Visibility=Visibility.Collapsed;allSettings.Visibility=Visibility.Visible;RenderIniRows();UpdateLayout();PumpDispatcher();Capture(FilePath.Combine(folder,"interface-all-settings.png"));quickSettings.Visibility=Visibility.Visible;allSettings.Visibility=Visibility.Collapsed;SelectPage(0);}
        internal void TestIniEditor(){
            LoadPackageIni(false);string file=editedIni;SetChoice(editors["ini.fg_enabled"],"true");SetChoice(editors["ini.dx12_upscaler"],"ffx");SaveEditor();var doc=new IniDocument(file);Check(doc.Get("FrameGen","Enabled")=="true"&&doc.Get("Upscalers","Dx12Upscaler")=="ffx"&&doc.Get("DLSS","Enabled")=="auto"&&File.Exists(file+".bak"),"The graphical editor saves the correct sections and keeps a backup");
            SetChoice(editors["ini.fg_output"],"xefg");Check(!saveIni.IsEnabled&&changes.Text.Contains("requires FSR FG"),"Invalid frame-generation combinations are explained before saving");
            string before=File.ReadAllText(file);StageDefaults();Check(File.ReadAllText(file)==before&&Selected(editors["ini.fg_input"])=="nvngxfg"&&Selected(editors["ini.fg_output"])=="fsrfg"&&dirty.Count>=ProfileFields.Length+1,"Predefined values are staged without changing the INI");SaveEditor();doc=new IniDocument(file);Check(doc.Get("FrameGen","FGNvngxReplacement")=="Combo"&&doc.Get("FrameGen","Enabled")=="auto"&&dirty.Count==0,"Saving predefined values restores the built-in profile");
            File.AppendAllText(file,"\n[Custom]\n; Set any custom value\n; This comment is the hover description\nExample=original ; keep inline\n[DLSS]\nRenderPresetOverride=auto\n");LoadIni(file);
            string id=IniId("Custom","Example");Check(rawEditors.ContainsKey(id)&&((string)rawEditors[id].ToolTip).Contains("hover description")&&((string)rawEditors[id].ToolTip).Contains("keep inline"),"All settings exposes custom keys with original multi-line and inline hover help");
            rawEditors[id].Text="custom=value#1";iniSearch.Text="hover description";Check(rawEditors.Count==1&&rawEditors[id].Text=="custom=value#1","INI search includes descriptions and retains unsaved values across filtering");SaveEditor();Check(new IniDocument(file).Get("Custom","Example")=="custom=value#1"&&File.ReadAllText(file).Contains(" ; keep inline")&&iniSearch.Text=="hover description","Full editor saves custom values, keeps inline comments and retains the filter");
            iniSearch.Text="";rawEditors[IniId("FrameGen","Enabled")].Text="false";Check(Selected(editors["ini.fg_enabled"])=="false","Raw INI changes synchronize with quick controls");SetChoice(editors["ini.fg_enabled"],"true");Check(rawEditors[IniId("FrameGen","Enabled")].Text=="true","Quick controls synchronize with the full editor");
            SetChoice(editors["ini.dlss_preset"],"10");rawEditors[IniId("DLSS","RenderPresetOverride")].Text="false";SaveEditor();Check(new IniDocument(file).Get("DLSS","RenderPresetOverride")=="false"&&dirty.Count==0,"Explicit full-editor values take precedence over preset side effects");
        }
    }
    static void PumpDispatcher(){var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);}
}
