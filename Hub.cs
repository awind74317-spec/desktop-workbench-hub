using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ellipse = System.Windows.Shapes.Ellipse;
using System.Windows.Threading;
using Microsoft.Win32;

public class Hub : Window {
    static string Root=AppDomain.CurrentDomain.BaseDirectory;
    static JavaScriptSerializer Json=new JavaScriptSerializer(){MaxJsonLength=4000000};
    static string Python=Path.Combine(Root,@".venv\Scripts\python.exe");
    static Brush Ink=Brush("#DFE2EC"), Muted=Brush("#A7B2BD"), Green=Brush("#4EDEA3"), Red=Brush("#FFB4AB"), Blue=Brush("#8ED5FF");
    StackPanel Services=new StackPanel(),Tokens=new StackPanel();TextBlock Summary,Footer;Grid TokenTiles;TextBlock[] TileValues=new TextBlock[3];
    Button Pin;bool Polling,ActionBusy,GlassEnabled;DispatcherTimer Timer;
    Dictionary<string,ServiceRow> Rows=new Dictionary<string,ServiceRow>();Dictionary<string,AiCard> Cards=new Dictionary<string,AiCard>();
    Dictionary<string,object> Prefs=new Dictionary<string,object>();
    [DllImport("dwmapi.dll")]static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd,ref Margins margins);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr FindWindow(string cls,string title);
    [DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")]static extern bool ShowWindow(IntPtr hwnd,int command);
    [DllImport("user32.dll")]static extern int SetWindowCompositionAttribute(IntPtr hwnd,ref CompositionData data);
    [DllImport("gdi32.dll")]static extern IntPtr CreateRoundRectRgn(int left,int top,int right,int bottom,int width,int height);
    [DllImport("user32.dll")]static extern int SetWindowRgn(IntPtr hwnd,IntPtr region,bool redraw);
    [DllImport("gdi32.dll")]static extern bool DeleteObject(IntPtr value);
    [StructLayout(LayoutKind.Sequential)]struct Margins {public int Left,Right,Top,Bottom;}
    [StructLayout(LayoutKind.Sequential)]struct AccentPolicy {public int State,Flags;public uint Tint;public int Animation;}
    [StructLayout(LayoutKind.Sequential)]struct CompositionData {public int Attribute;public IntPtr Data;public int Size;}
    public static Brush Brush(string c){return (Brush)new BrushConverter().ConvertFromString(c);}
    static Dictionary<string,object> Obj(object o){return o as Dictionary<string,object>??new Dictionary<string,object>();}
    static object Val(Dictionary<string,object> d,string k,object fallback=null){object v;return d.TryGetValue(k,out v)?v:fallback;}
    static string Str(Dictionary<string,object>d,string k,string fallback=""){return Convert.ToString(Val(d,k,fallback));}
    static bool Bool(Dictionary<string,object>d,string k){return Val(d,k) is bool && (bool)d[k];}
    static double Num(Dictionary<string,object>d,string k,double fallback=0){try{return Convert.ToDouble(Val(d,k,fallback));}catch{return fallback;}}
    static TextBlock Text(string text,double size=13,Brush color=null,bool bold=false){return new TextBlock{Text=text,FontSize=size,Foreground=color??Ink,FontWeight=bold?FontWeights.SemiBold:FontWeights.Normal,VerticalAlignment=VerticalAlignment.Center};}
    static Button Flat(object content,string label=null){var b=new Button{Content=content,Background=Brushes.Transparent,BorderThickness=new Thickness(0),Padding=new Thickness(4),Cursor=Cursors.Hand,HorizontalContentAlignment=HorizontalAlignment.Left,Foreground=Ink};
        b.Template=(ControlTemplate)XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'><Border x:Name='Surface' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' Background='{TemplateBinding Background}' CornerRadius='6' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Surface' Property='Background' Value='#31353D'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Surface' Property='BorderBrush' Value='#8ED5FF'/><Setter TargetName='Surface' Property='BorderThickness' Value='1'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='.5'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
        if(label!=null)AutomationProperties.SetName(b,label);return b;}
    static Border Card(UIElement child,string color="#181C23",int radius=8){return new Border{Background=Brush(color),BorderBrush=Brush("#3E484F"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(radius),Child=child};}
    static Border Pane(string title,UIElement content){var layout=new DockPanel{Margin=new Thickness(10)};var heading=Text(title,13,Ink,true);heading.Margin=new Thickness(0,0,0,10);DockPanel.SetDock(heading,Dock.Top);layout.Children.Add(heading);
        var scroll=new ScrollViewer{Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        scroll.Resources.Add(typeof(ScrollBar),XamlReader.Parse(@"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ScrollBar'>
            <Setter Property='Width' Value='8'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollBar'>
            <Grid Background='#0A0E15'><Track x:Name='PART_Track' Orientation='Vertical' IsDirectionReversed='True' Minimum='{TemplateBinding Minimum}' Maximum='{TemplateBinding Maximum}' ViewportSize='{TemplateBinding ViewportSize}' Value='{Binding Value, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}'>
            <Track.DecreaseRepeatButton><RepeatButton Command='ScrollBar.PageUpCommand' Focusable='False'><RepeatButton.Template><ControlTemplate><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton>
            <Track.Thumb><Thumb><Thumb.Template><ControlTemplate><Border Background='#3E484F' CornerRadius='3' Margin='2,0'/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
            <Track.IncreaseRepeatButton><RepeatButton Command='ScrollBar.PageDownCommand' Focusable='False'><RepeatButton.Template><ControlTemplate><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton>
            </Track></Grid></ControlTemplate></Setter.Value></Setter></Style>"));layout.Children.Add(scroll);return Card(layout,"#0A0E15");}
    static double? Remaining(Dictionary<string,object> data,bool stale){double value=Num(data,"remaining",Double.NaN),reset=Num(data,"reset");return stale||Double.IsNaN(value)||Double.IsInfinity(value)||value<0||value>100||(reset>0&&reset<=DateTimeOffset.UtcNow.ToUnixTimeSeconds())?(double?)null:value;}
    static Ellipse Lamp(double size){return new Ellipse{Width=size,Height=size,Fill=Red,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center};}
    static Border AppIcon(string id){var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.UriSource=new Uri(System.IO.Path.Combine(Root,"assets",id+".png"));bitmap.EndInit();bitmap.Freeze();return new Border{Child=new Image{Source=bitmap,Stretch=Stretch.Uniform},Width=28,Height=28,Margin=new Thickness(0,0,7,0),Background=id=="codex"?Brush("#DFE2EC"):Brushes.Transparent,CornerRadius=new CornerRadius(4),Padding=new Thickness(id=="codex"?2:0)};}
    static ToggleButton Switch(){var t=new ToggleButton{Width=43,Height=25,Cursor=Cursors.Hand};t.Template=(ControlTemplate)XamlReader.Parse(@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ToggleButton'><Grid Width='43' Height='25'><Border x:Name='Track' CornerRadius='13' Background='#3E484F'/><Ellipse x:Name='Thumb' Width='21' Height='21' Fill='White' HorizontalAlignment='Left' Margin='2'/></Grid><ControlTemplate.Triggers><Trigger Property='IsChecked' Value='True'><Setter TargetName='Track' Property='Background' Value='#38BDF8'/><Setter TargetName='Thumb' Property='HorizontalAlignment' Value='Right'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='.55'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");return t;}
    public Hub(){
        Title="工作檯總管";MinWidth=900;MinHeight=520;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.CanResizeWithGrip;Background=Brushes.Transparent;
        FontFamily=new FontFamily("Segoe UI, Microsoft JhengHei UI");Topmost=true;
        try{Prefs=Obj(Json.DeserializeObject(File.ReadAllText(Path.Combine(Root,"settings.json"),Encoding.UTF8)));}catch{}
        Height=Math.Max(MinHeight,Math.Min(Num(Prefs,"height",600),SystemParameters.WorkArea.Height-24));Width=Math.Max(MinWidth,Math.Min(Num(Prefs,"width",980)<MinWidth?980:Num(Prefs,"width",980),SystemParameters.WorkArea.Width-24));
        Topmost=!(Val(Prefs,"topmost") is bool)||Bool(Prefs,"topmost");
        Left=Num(Prefs,"left",SystemParameters.WorkArea.Right-Width-28);Top=Num(Prefs,"top",28);
        Left=Math.Max(SystemParameters.WorkArea.Left,Math.Min(Left,SystemParameters.WorkArea.Right-Width));Top=Math.Max(0,Math.Min(Top,SystemParameters.WorkArea.Bottom-Height));
        var shell=new Border{CornerRadius=new CornerRadius(12),Background=Brush("#F00F141A"),BorderBrush=Brush("#3E484F"),BorderThickness=new Thickness(1),Padding=new Thickness(12,10,12,8)};
        // Stitch 的三區設計直接接回原本桌面元件與後端，各區獨立捲動。
        Content=shell;var layout=new DockPanel();shell.Child=layout;
        var bottom=new DockPanel{Margin=new Thickness(2,8,2,0)};DockPanel.SetDock(bottom,Dock.Bottom);layout.Children.Add(bottom);
        var grip=Text("◢",14,Muted);grip.Margin=new Thickness(8,0,0,0);grip.ToolTip="拖曳右下角調整視窗大小";DockPanel.SetDock(grip,Dock.Right);bottom.Children.Add(grip);
        var refresh=Flat(Text("↻  重新整理",11,Muted),"重新整理狀態與額度");refresh.Click+=async(s,e)=>await Refresh(true);bottom.Children.Add(refresh);
        Footer=Text("連接本機工作檯…",10,Muted);Footer.TextTrimming=TextTrimming.CharacterEllipsis;Footer.Margin=new Thickness(9,0,0,0);bottom.Children.Add(Footer);
        var body=new StackPanel();DockPanel.SetDock(body,Dock.Top);layout.Children.Add(body);
        var header=new DockPanel{Margin=new Thickness(0,0,0,4),Background=Brushes.Transparent,Cursor=Cursors.SizeAll};body.Children.Add(header);
        header.MouseLeftButtonDown+=(s,e)=>{DependencyObject target=e.OriginalSource as DependencyObject;while(target!=null){if(target is Button)return;target=VisualTreeHelper.GetParent(target);}if(e.ButtonState==MouseButtonState.Pressed){
            bool restoreGlass=GlassEnabled;
            try{if(restoreGlass)SetGlass(false);DragMove();}
            finally{if(restoreGlass)SetGlass(true);}
        }};
        var controls=new StackPanel{Orientation=Orientation.Horizontal};DockPanel.SetDock(controls,Dock.Right);header.Children.Add(controls);
        Pin=Flat(Text("置頂",12,Blue),"切換置頂");Pin.Padding=new Thickness(10,6,10,6);Pin.Click+= (s,e)=>{Topmost=!Topmost;PaintPin();SavePosition();};controls.Children.Add(Pin);
        var min=Flat(Text("−",20,Muted),"最小化");min.Width=32;min.HorizontalContentAlignment=HorizontalAlignment.Center;min.Click+=(s,e)=>WindowState=WindowState.Minimized;controls.Children.Add(min);
        var close=Flat(Text("×",21,Muted),"關閉浮窗");close.Width=32;close.HorizontalContentAlignment=HorizontalAlignment.Center;close.Click+=(s,e)=>Close();controls.Children.Add(close);
        var mark=Card(Text("▦",18,Blue,true),"#203443",6);mark.Width=28;mark.Height=28;mark.Padding=new Thickness(5,0,0,0);mark.Margin=new Thickness(0,0,9,0);header.Children.Add(mark);header.Children.Add(Text("工作檯總管",16,Ink,true));
        Summary=Text("正在確認工作檯…",12,Muted);Summary.Margin=new Thickness(3,4,0,10);Summary.TextTrimming=TextTrimming.CharacterEllipsis;Summary.ToolTip="額度依序是 5 小時剩餘 / 本週剩餘";body.Children.Add(Summary);
        var serviceBody=new StackPanel();serviceBody.Children.Add(Services);
        var addRow=Flat(Text("＋  加入工作檯",12,Blue),"選擇 CMD 或 BAT 加入工作檯");addRow.Margin=new Thickness(0,8,0,0);addRow.Height=33;addRow.Click+=(s,e)=>AddDialog();serviceBody.Children.Add(addRow);
        var aiBody=new StackPanel();var claude=new AiCard(this,"claude","Claude Code","#FFBF9E");var codex=new AiCard(this,"codex","Codex","#4EDEA3");
        claude.Panel.Margin=new Thickness(0,0,0,10);aiBody.Children.Add(claude.Panel);aiBody.Children.Add(codex.Panel);Cards["claude"]=claude;Cards["codex"]=codex;
        TokenTiles=new Grid{Margin=new Thickness(0,0,0,8)};var tileLabels=new[]{"今日主要花費","今日對話","該換對話"};
        for(int i=0;i<3;i++){TokenTiles.ColumnDefinitions.Add(new ColumnDefinition());var tile=new StackPanel{Margin=new Thickness(6,7,6,7)};TileValues[i]=Text("—",18,i==0?Blue:i==2?Red:Ink,true);tile.Children.Add(Text(tileLabels[i],10,Muted));tile.Children.Add(TileValues[i]);
            var tileCard=Card(tile,i==2?"#32171D":"#181C23",5);tileCard.Margin=new Thickness(i==0?0:3,0,i==2?0:3,0);Grid.SetColumn(tileCard,i);TokenTiles.Children.Add(tileCard);}
        var tokenBody=new StackPanel();tokenBody.Children.Add(TokenTiles);tokenBody.Children.Add(Tokens);
        var columns=new Grid();columns.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(24,GridUnitType.Star)});columns.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(29,GridUnitType.Star)});columns.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(35,GridUnitType.Star)});
        var panes=new[]{Pane("工作檯",serviceBody),Pane("AI 額度監控 · 剩餘量",aiBody),Pane("Token 使用分析",tokenBody)};
        for(int i=0;i<panes.Length;i++){panes[i].Margin=new Thickness(i==0?0:4,0,i==2?0:4,0);Grid.SetColumn(panes[i],i);columns.Children.Add(panes[i]);}layout.Children.Add(columns);
        SourceInitialized+=(s,e)=>ApplyGlass();
        SizeChanged+=(s,e)=>RoundWindow();
        Loaded+=async(s,e)=>{PaintPin();await Refresh(false);};
        Timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(5)};Timer.Tick+=async(s,e)=>await Refresh(false);Timer.Start();
        Closing+=(s,e)=>{Timer.Stop();SavePosition();};
    }
    void ApplyGlass(){
        var handle=new WindowInteropHelper(this).Handle;HwndSource.FromHwnd(handle).CompositionTarget.BackgroundColor=Colors.Transparent;
        var margins=new Margins{Left=-1,Right=-1,Top=-1,Bottom=-1};DwmExtendFrameIntoClientArea(handle,ref margins);
        // This machine runs Windows 10: its acrylic accent is used instead of the Windows 11-only backdrop attribute.
        SetGlass(true);
        RoundWindow();
    }
    void SetGlass(bool enabled){
        var handle=new WindowInteropHelper(this).Handle;
        var accent=new AccentPolicy{State=enabled?4:0,Flags=2,Tint=0xE61A140F,Animation=0};int size=Marshal.SizeOf(typeof(AccentPolicy));IntPtr pointer=Marshal.AllocHGlobal(size);
        try{Marshal.StructureToPtr(accent,pointer,false);var data=new CompositionData{Attribute=19,Data=pointer,Size=size};bool applied=SetWindowCompositionAttribute(handle,ref data)!=0;GlassEnabled=enabled&&applied;}finally{Marshal.FreeHGlobal(pointer);}
    }
    void RoundWindow(){var handle=new WindowInteropHelper(this).Handle;if(handle==IntPtr.Zero||ActualWidth<=0||ActualHeight<=0)return;var source=HwndSource.FromHwnd(handle);if(source==null||source.CompositionTarget==null)return;var scale=source.CompositionTarget.TransformToDevice;var region=CreateRoundRectRgn(0,0,(int)Math.Ceiling(ActualWidth*scale.M11)+1,(int)Math.Ceiling(ActualHeight*scale.M22)+1,(int)(24*scale.M11),(int)(24*scale.M22));if(SetWindowRgn(handle,region,true)==0)DeleteObject(region);}
    void PaintPin(){Pin.Background=Topmost?Brush("#203443"):Brushes.Transparent;Pin.ToolTip=Topmost?"已置頂；點擊取消":"點擊保持最上層";}
    void SavePosition(){try{var p=System.IO.Path.Combine(Root,"settings.json");var d=Obj(Json.DeserializeObject(File.ReadAllText(p,Encoding.UTF8)));d["left"]=Left;d["top"]=Top;d["width"]=Width;d["height"]=Height;d["topmost"]=Topmost;File.WriteAllText(p,Json.Serialize(d),new UTF8Encoding(false));}catch{}}
    static async Task<Dictionary<string,object>> Request(Dictionary<string,object> message){return await Task.Run(()=>{
        var info=new ProcessStartInfo(Python,"-s -X utf8 \""+Path.Combine(Root,"backend.py")+"\""){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=Root,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
        // 用 UTF-8 送：預設是系統的 Big5，中文工作流名稱到後端會解碼失敗
        using(var process=Process.Start(info)){var body=new UTF8Encoding(false).GetBytes(Json.Serialize(message));process.StandardInput.BaseStream.Write(body,0,body.Length);process.StandardInput.Close();string output=process.StandardOutput.ReadToEnd();string error=process.StandardError.ReadToEnd();process.WaitForExit();
            if(String.IsNullOrWhiteSpace(output))throw new Exception("本機監控程式未回應："+error.Substring(0,Math.Min(160,error.Length)));
            var d=Obj(Json.DeserializeObject(output));if(d.ContainsKey("error"))throw new Exception(Str(d,"error"));return d;}
    });}
    async Task Refresh(bool force){if(Polling||ActionBusy)return;Polling=true;try{var state=await Request(new Dictionary<string,object>{{"action",force?"refresh":"status"}});var list=(object[])state["services"];
        if(!Rows.Keys.OrderBy(x=>x).SequenceEqual(list.Select(x=>Str(Obj(x),"id")).OrderBy(x=>x))||Services.Children.Count==0){Services.Children.Clear();Rows.Clear();foreach(var raw in list){var item=Obj(raw);var row=new ServiceRow(this,item);Rows[Str(item,"id")]=row;Services.Children.Add(row.Panel);}}
        if(list.Length==0&&Services.Children.Count==0){var empty=Text("尚未加入工作檯\n點上方「＋」加入第一個工作檯",12,Muted);empty.TextWrapping=TextWrapping.Wrap;empty.Margin=new Thickness(6,14,6,0);Services.Children.Add(empty);}
        int running=0;foreach(var raw in list){var item=Obj(raw);Rows[Str(item,"id")].Update(item);if(Bool(item,"running"))running++;}
        var quotas=Obj(state["quotas"]);
        Func<string,string,string> pct=(name,key)=>{var q=Obj(Val(quotas,name));var value=Remaining(Obj(Val(q,key)),Val(q,"error")!=null);return value.HasValue?Math.Round(value.Value)+"%":"—";};
        Summary.Text="●  "+running+" / "+list.Length+" 運行中 · Claude "+pct("claude","five")+" / "+pct("claude","week")+" · Codex "+pct("codex","five")+" / "+pct("codex","week");Summary.Foreground=running>0?Green:Muted;foreach(var name in new[]{"claude","codex"})Cards[name].Update(Obj(state[name]),Obj(Val(quotas,name)));
        PaintTokens(Obj(Val(state,"tokens")));
        Footer.Text="剛剛更新 · "+DateTime.Now.ToString("HH:mm:ss");Footer.Foreground=Muted;Footer.ToolTip=(GlassEnabled?"背景模糊已套用。":"系統未接受背景模糊設定。")+"工作檯每 5 秒檢查；額度約每 2 分鐘更新。關閉浮窗不會關閉工作檯。";
    }catch(Exception ex){Footer.Text=ex.Message;Footer.Foreground=Red;Footer.ToolTip=ex.Message;}finally{Polling=false;}}
    static string K(double n){return n>=1e6?(n/1e6).ToString("0.0")+"M":Math.Round(n/1e3)+"k";}
    // 主要花費＝新輸入＋快取寫＋輸出；快取讀便宜很多，只放在提示。
    static double Cost(Dictionary<string,object> d){return Num(d,"fresh")+Num(d,"cache_write")+Num(d,"output");}
    static string Detail(Dictionary<string,object> d){return "新輸入 "+K(Num(d,"fresh"))+" · 快取寫 "+K(Num(d,"cache_write"))+" · 輸出 "+K(Num(d,"output"))+" · 快取讀 "+K(Num(d,"cache_read"));}
    static object[] Signs(Dictionary<string,object> d){return Val(d,"signs") as object[]??new object[0];}
    void PaintTokens(Dictionary<string,object> t){Tokens.Children.Clear();
        if(t.ContainsKey("error")){foreach(var value in TileValues)value.Text="—";Tokens.Children.Add(Text(Str(t,"error"),11,Red));return;}
        var total=Obj(Val(t,"total"));var rows=(Val(t,"rows") as object[]??new object[0]).Select(Obj).ToList();var flagged=rows.Where(x=>Signs(x).Length>0).ToList();
        TileValues[0].Text=K(Cost(total));TileValues[1].Text=rows.Count.ToString();TileValues[2].Text=flagged.Count.ToString();TileValues[2].Foreground=flagged.Count>0?Red:Ink;
        TokenTiles.ToolTip="主要花費＝"+Detail(total).Replace(" · 快取讀","\n快取讀")+"（便宜很多，不計入）\n各對話從開始累計，約每 30 秒更新；列出要換的對話與最近 4 個";
        foreach(var r in flagged.Concat(rows.Where(x=>Signs(x).Length==0).Take(4))){var signs=Signs(r);double context=Num(r,"context"),ratio=Math.Min(1,context/400000);
            var g=new Grid{Margin=new Thickness(7,6,7,6)};g.RowDefinitions.Add(new RowDefinition());g.RowDefinitions.Add(new RowDefinition());g.RowDefinitions.Add(new RowDefinition{Height=new GridLength(7)});
            g.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(23)});g.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});g.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(52)});
            var icon=AppIcon(Str(r,"tool").ToLower());icon.Width=16;icon.Height=16;icon.Margin=new Thickness(0);icon.HorizontalAlignment=HorizontalAlignment.Left;g.Children.Add(icon);
            var title=Text(Str(r,"title")==""?"（無標題）":Str(r,"title"),12);title.TextTrimming=TextTrimming.CharacterEllipsis;Grid.SetColumn(title,1);g.Children.Add(title);
            var tags=new WrapPanel{Margin=new Thickness(0,3,0,0)};Grid.SetColumn(tags,1);Grid.SetRow(tags,1);Grid.SetColumnSpan(tags,2);g.Children.Add(tags);tags.Children.Add(Text(Str(r,"tool"),10,Muted));
            foreach(var raw in signs){var s=Obj(raw);tags.Children.Add(new Border{Background=Brush("#46252B"),CornerRadius=new CornerRadius(3),Padding=new Thickness(5,1,5,1),Margin=new Thickness(6,0,0,0),Child=Text(Str(s,"tag"),10,Red,true),ToolTip=Str(s,"why")});}
            var cost=Text(K(Cost(r)),12,signs.Length>0?Brush("#FFBF9E"):Green,true);cost.HorizontalAlignment=HorizontalAlignment.Right;Grid.SetColumn(cost,2);g.Children.Add(cost);
            // 上下文長度條：400k 滿格，60% 起轉黃
            var bar=new Grid();bar.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(Math.Max(ratio,.001),GridUnitType.Star)});bar.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(Math.Max(1-ratio,.001),GridUnitType.Star)});
            bar.Children.Add(new Border{Background=ratio>=1?Red:ratio>=.6?Brush("#E9AC44"):Green,CornerRadius=new CornerRadius(2)});
            var track=new Border{Background=Brush("#31353D"),CornerRadius=new CornerRadius(2),Height=3,VerticalAlignment=VerticalAlignment.Bottom,Child=bar};Grid.SetRow(track,2);Grid.SetColumn(track,1);Grid.SetColumnSpan(track,2);g.Children.Add(track);
            g.ToolTip=Str(r,"tool")+"："+Str(r,"title")+"\n上下文 "+K(context)+"（400k 滿格）· 截圖 "+Num(r,"shots")+" 張\n"+Detail(r)+String.Concat(signs.Select(s=>"\n⚠ "+Str(Obj(s),"why")));
            var card=Card(g,signs.Length>0?"#2B1920":"#181C23",5);card.Margin=new Thickness(0,0,0,5);Tokens.Children.Add(card);
        }
        if(rows.Count==0)Tokens.Children.Add(Text("今天尚無對話用量",12,Muted));
    }
    async Task Action(string action,string id,string path=null){if(ActionBusy)return;ActionBusy=true;foreach(var row in Rows.Values)row.Toggle.IsEnabled=false;
        ServiceRow target;if(Rows.TryGetValue(id,out target))target.Busy(action=="stop"?"停止中":"啟動中");
        Footer.Text="正在處理…";
        string failure=null;
        try{var message=new Dictionary<string,object>{{"action",action},{"id",id}};if(path!=null)message["path"]=path;await Request(message);Footer.Text="已完成";}catch(Exception ex){failure=ex.Message;}
        finally{ActionBusy=false;foreach(var row in Rows.Values)row.Toggle.IsEnabled=true;}
        await Refresh(false);if(failure!=null){Footer.Text=failure;Footer.ToolTip=failure;Footer.Foreground=Red;}
    }
    class ServiceRow {
        public Border Panel;public ToggleButton Toggle;Ellipse Dot;TextBlock Detail;
        public ServiceRow(Hub owner,Dictionary<string,object> item){var row=new Grid{Margin=new Thickness(6,7,6,7),MinHeight=36};
            foreach(var width in new[]{34.0,Double.NaN,-1,14.0,44.0})row.ColumnDefinitions.Add(new ColumnDefinition{Width=Double.IsNaN(width)?new GridLength(1,GridUnitType.Star):width<0?GridLength.Auto:new GridLength(width)});
            var icon=Card(Text(Str(item,"icon"),17,Brushes.White,true),Str(item,"color"),6);icon.Width=27;icon.Height=27;icon.Padding=new Thickness(4,0,0,0);row.Children.Add(icon);
            var labels=new StackPanel();var title=Text(Str(item,"name"),12,Ink,true);title.TextTrimming=TextTrimming.CharacterEllipsis;labels.Children.Add(title);Detail=Text("讀取中",10,Muted);Detail.TextTrimming=TextTrimming.CharacterEllipsis;labels.Children.Add(Detail);
            var name=Flat(labels,"開啟 "+Str(item,"name"));name.ToolTip=Str(item,"name")+"\n"+Str(item,"url");name.Click+=async(s,e)=>await owner.Action("open",Str(item,"id"));Grid.SetColumn(name,1);row.Children.Add(name);
            Dot=Lamp(6);Grid.SetColumn(Dot,3);row.Children.Add(Dot);
            Toggle=Switch();Toggle.LayoutTransform=new ScaleTransform(.9,.9);AutomationProperties.SetName(Toggle,Str(item,"name")+" 啟停");Toggle.Click+=async(s,e)=>await owner.Action(Toggle.IsChecked==true?"start":"stop",Str(item,"id"));Grid.SetColumn(Toggle,4);row.Children.Add(Toggle);Panel=Card(row,"#181C23",5);Panel.Margin=new Thickness(0,0,0,6);
        }
        public void Busy(string message){Dot.Fill=Brush("#FFBF9E");Dot.ToolTip=message;Detail.Text=message;}
        public void Update(Dictionary<string,object> data){bool on=Bool(data,"running");Toggle.IsChecked=on;Dot.Fill=on?Green:Red;Detail.Text=Str(data,"detail");Detail.ToolTip=Str(data,"detail")+"\n"+Str(data,"url");Dot.ToolTip=Str(data,"detail");AutomationProperties.SetName(Dot,Str(data,"detail"));}
    }
    class QuotaBar : StackPanel {
        ProgressBar Bar;TextBlock Value,Reset;
        public QuotaBar(string title,string color){Margin=new Thickness(0,8,0,0);var head=new DockPanel();Value=Text("—",16,Brush(color),true);DockPanel.SetDock(Value,Dock.Right);head.Children.Add(Value);head.Children.Add(Text(title,11,Muted));Children.Add(head);
            Bar=new ProgressBar{Minimum=0,Maximum=100,Height=4,Background=Brush("#31353D"),Foreground=Brush(color),BorderThickness=new Thickness(0),Margin=new Thickness(0,4,0,4)};
            Bar.Template=(ControlTemplate)XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ProgressBar'><Grid><Border x:Name='PART_Track' Background='{TemplateBinding Background}' CornerRadius='2'/><Border x:Name='PART_Indicator' Background='{TemplateBinding Foreground}' CornerRadius='2' HorizontalAlignment='Left'/></Grid></ControlTemplate>");Children.Add(Bar);Reset=Text("讀取中",10,Muted);Children.Add(Reset);}
        static string ResetText(Dictionary<string,object> window){if(window.Count==0)return "目前未提供";double reset=Num(window,"reset");if(reset<=0)return "重置時間未提供";var left=DateTimeOffset.FromUnixTimeSeconds((long)reset)-DateTimeOffset.UtcNow;if(left.TotalSeconds<=0)return "等待更新";if(left.TotalDays>=1)return Math.Floor(left.TotalDays)+" 天 "+left.Hours+" 時後重置";if(left.TotalHours>=1)return Math.Floor(left.TotalHours)+" 時 "+left.Minutes+" 分後重置";return Math.Ceiling(left.TotalMinutes)+" 分後重置";}
        public void Update(Dictionary<string,object> data,bool stale){var remaining=Remaining(data,stale);Value.Text=remaining.HasValue?Math.Round(remaining.Value)+"%":"—";Bar.Value=remaining??0;Bar.Visibility=remaining.HasValue?Visibility.Visible:Visibility.Hidden;Reset.Text=stale?"暫時讀取不到":!remaining.HasValue&&Num(data,"reset")<=0?"目前未提供":ResetText(data);AutomationProperties.SetName(this,Value.Text+" · "+Reset.Text);}
    }
    class AiCard {
        public Border Panel;Ellipse Live;TextBlock TaskLabel;QuotaBar Five,Week;TextBlock Stamp;
        public AiCard(Hub owner,string id,string title,string color){var stack=new StackPanel{Margin=new Thickness(10,9,10,9)};Panel=Card(stack);Panel.BorderBrush=Brush(color);Panel.BorderThickness=new Thickness(2,0,0,0);
            var head=new StackPanel{Orientation=Orientation.Horizontal};var icon=AppIcon(id);icon.Width=20;icon.Height=20;head.Children.Add(icon);head.Children.Add(Text(title,13,Ink,true));Live=Lamp(6);Live.Margin=new Thickness(8,0,0,0);head.Children.Add(Live);var button=Flat(head,"開啟 "+title);button.Padding=new Thickness(0);button.Click+=async(s,e)=>await owner.Action("open_ai",id);stack.Children.Add(button);
            TaskLabel=Text("讀取任務狀態…",11,Muted);TaskLabel.Margin=new Thickness(0,6,0,1);stack.Children.Add(TaskLabel);
            Five=new QuotaBar("5 小時剩餘",color);Week=new QuotaBar("本週剩餘",color);stack.Children.Add(Five);stack.Children.Add(Week);
            Stamp=Text("",10,Muted);Stamp.TextTrimming=TextTrimming.CharacterEllipsis;Stamp.Margin=new Thickness(0,8,0,0);stack.Children.Add(Stamp);
        }
        public void Update(Dictionary<string,object> state,Dictionary<string,object> quota){bool on=Bool(state,"running");Live.Fill=on?Green:Red;Live.ToolTip=on?"程式已開啟":"程式已關閉";AutomationProperties.SetName(Live,on?"程式已開啟":"程式已關閉");
            string task=Str(state,"task","狀態未知");TaskLabel.Text=task=="未啟用"?"AI 監控尚未啟用":!on?"未執行":task+(Val(state,"active")==null||task=="狀態未知"?"":" · "+Num(state,"active")+" 個任務");TaskLabel.ToolTip=Str(state,"source");TaskLabel.Foreground=task=="執行中"?Green:Muted;
            bool stale=Val(quota,"error")!=null;Five.Update(Obj(Val(quota,"five")),stale);Week.Update(Obj(Val(quota,"week")),stale);
            Stamp.Text=stale?Str(quota,"error"):Num(quota,"fetched")>0?"額度更新 "+DateTimeOffset.FromUnixTimeSeconds((long)Num(quota,"fetched")).ToLocalTime().ToString("HH:mm"):"額度更新時間未提供";Panel.ToolTip=Str(quota,"source")+(stale?"；"+Str(quota,"error"):"");
        }
    }
    void AddDialog(){
        var win=new Window{Title="加入工作檯",Width=420,Height=355,Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.NoResize,Background=Brush("#0F141A"),FontFamily=FontFamily,Topmost=Topmost};
        var box=new StackPanel{Margin=new Thickness(20)};win.Content=box;
        Func<string,TextBox> field=label=>{box.Children.Add(Text(label,12,Muted));var t=new TextBox{Height=29,Margin=new Thickness(0,4,0,10),Padding=new Thickness(5),Background=Brush("#1C2027"),Foreground=Ink,CaretBrush=Ink,BorderBrush=Brush("#3E484F")};box.Children.Add(t);return t;};
        var name=field("顯示名稱");var path=field("啟動檔（點這裡選 CMD / BAT）");path.IsReadOnly=true;path.PreviewMouseLeftButtonDown+=(s,e)=>{var dlg=new OpenFileDialog{Filter="Windows 啟動檔|*.cmd;*.bat"};if(dlg.ShowDialog(win)==true){path.Text=dlg.FileName;if(String.IsNullOrWhiteSpace(name.Text))name.Text=Path.GetFileNameWithoutExtension(dlg.FileName);}};
        var url=field("工作檯網址，例如 http://127.0.0.1:9000/");var stop=field("停止檔（選填；沒有時停止該工作檯的服務）");stop.IsReadOnly=true;stop.PreviewMouseLeftButtonDown+=(s,e)=>{var dlg=new OpenFileDialog{Filter="Windows 停止檔|*.cmd;*.bat"};if(dlg.ShowDialog(win)==true)stop.Text=dlg.FileName;};
        var save=Flat(Text("加入工作檯",13,Blue,true),"儲存新增工作檯");save.HorizontalAlignment=HorizontalAlignment.Right;save.Click+=async(s,e)=>{save.IsEnabled=false;try{await Request(new Dictionary<string,object>{{"action","add"},{"name",name.Text},{"launcher",path.Text},{"url",url.Text},{"stop_launcher",stop.Text}});win.Close();await Refresh(false);}catch(Exception ex){MessageBox.Show(win,ex.Message,"無法加入");}finally{save.IsEnabled=true;}};box.Children.Add(save);win.ShowDialog();
    }
    [STAThread]public static void Main(){foreach(var p in Process.GetProcessesByName("WorkbenchHub")){try{if(p.Id!=Process.GetCurrentProcess().Id&&String.Equals(p.MainModule.FileName,Process.GetCurrentProcess().MainModule.FileName,StringComparison.OrdinalIgnoreCase)&&p.MainWindowHandle!=IntPtr.Zero){ShowWindow(p.MainWindowHandle,9);SetForegroundWindow(p.MainWindowHandle);return;}}catch{}}var app=new Application();app.DispatcherUnhandledException+=(s,e)=>{MessageBox.Show(e.Exception.Message,"工作檯總管");e.Handled=true;};app.Run(new Hub());}
}
