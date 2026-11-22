using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using LumaWall.Contracts;
using LumaWall.DesktopIntegration;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;

namespace LumaWall.ControlCenter;

public static class NumericRegressionProgram
{
    internal static bool DirectedInput;
    internal static string Output="";
    private static nint previousForeground;
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    internal static void RestoreForeground(){if(previousForeground!=0)SetForegroundWindow(previousForeground);}
    [STAThread] public static void Main(string[] args)
    {
        DirectedInput=args.Contains("--directed-input");
        previousForeground=GetForegroundWindow();Output=Path.GetFullPath(args[0]);Directory.CreateDirectory(Output);
        _=Task.Run(async()=>{await Task.Delay(120000);Fail(new TimeoutException("Numeric UI timed out"));});
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(parameters=>{SynchronizationContext.SetSynchronizationContext(new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()));_=new App();});
    }
    internal static void Fail(Exception e){RestoreForeground();File.WriteAllText(Path.Combine(Output,"failure.txt"),e.ToString());Environment.Exit(1);}
}
public partial class App : Application
{
    public static ChildProcessGroup ChildProcesses {get;}=new();
    public App(){UnhandledException+=(_,e)=>NumericRegressionProgram.Fail(e.Exception);InitializeComponent();}
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        string output=NumericRegressionProgram.Output;AppLog.Initialize("NumericRegression",output);
        var store=new LibraryStore(Path.Combine(output,"data"));store.SetSetting("appearance.v1",new AppearanceOptions {Motion=false,Pattern=false}.ToJson());
        var window=new MainWindow(store);window.LoadedReady+=()=>window.CheckNumeric(output);
        window.AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(-20000,-20000,1440,1000));window.Activate();
    }
}
public sealed partial class MainWindow
{
    [DllImport("user32.dll")] private static extern nint SendMessageW(nint hwnd,uint message,nuint wParam,nint lParam);
    [DllImport("user32.dll")] private static extern nint GetFocus();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window,out uint process);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint from,uint to,bool attach);
    private void NumericForeground()
    {
        nint handle=WinRT.Interop.WindowNative.GetWindowHandle(this);
        uint current=GetCurrentThreadId(),other=GetWindowThreadProcessId(AutomationForeground(),out _);
        bool attached=other!=0&&other!=current&&AttachThreadInput(current,other,true);
        try {ShowWindow(handle,5);SetForegroundWindow(handle);}finally {if(attached)AttachThreadInput(current,other,false);}
    }
    private static IEnumerable<FrameworkElement> NumericElements(DependencyObject root)
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is FrameworkElement element)yield return element;foreach(var nested in NumericElements(child))yield return nested;}
    }
    internal async void CheckNumeric(string output)
    {
        var checks=new List<string>();
        void Check(bool passed,string message){if(!passed)throw new InvalidOperationException(message);checks.Add(message);File.AppendAllText(Path.Combine(output,"checks.txt"),message+"\n");}
        async Task Settle(){Root.UpdateLayout();await Task.Delay(100);Root.UpdateLayout();}
        async Task Message(uint message,nuint key){if(NumericRegressionProgram.DirectedInput){var destination=GetFocus();GetWindowThreadProcessId(destination,out uint owner);Check(destination!=0&&owner==Environment.ProcessId,"Directed text targets only isolated process");SendMessageW(destination,message,key,0);await Task.Delay(60);return;}NumericForeground();var handle=GetFocus();GetWindowThreadProcessId(handle,out uint process);Check(handle!=0&&process==Environment.ProcessId&&AutomationForeground()==WinRT.Interop.WindowNative.GetWindowHandle(this),"Keyboard input guarded by isolated foreground process");var events=message==0x102?new[]{AutomationInput.Unicode((char)key),AutomationInput.Unicode((char)key,true)}:new[]{AutomationInput.Key((ushort)key,message==0x101)};Check(AutomationInput.Dispatch(events)==events.Length,"Native keyboard input dispatched");await Task.Delay(60);}
        async Task Key(nuint key){if(NumericRegressionProgram.DirectedInput){var destination=GetFocus();GetWindowThreadProcessId(destination,out uint owner);Check(destination!=0&&owner==Environment.ProcessId,"Directed key targets only isolated process");SendMessageW(destination,0x100,key,0);SendMessageW(destination,0x101,key,0);await Task.Delay(100);return;}NumericForeground();await Task.Delay(30);Check(AutomationForeground()==WinRT.Interop.WindowNative.GetWindowHandle(this),"Key pair targets isolated foreground window");Check(AutomationInput.Dispatch(new[]{AutomationInput.Key((ushort)key),AutomationInput.Key((ushort)key,true)})==2,"Native key pair dispatched atomically");await Task.Delay(100);}
        async Task Focus(ScrubNumberInput number){NumericForeground();Check(number.DragSurface.Focus(FocusState.Programmatic),"Numeric drag surface focuses directly");await Settle();Check(!number.IsInteracting&&number.Editor.Visibility==Visibility.Collapsed,"Focus alone keeps text editor hidden");await Key(113);Check(!number.Editor.IsReadOnly&&number.Editor.Visibility==Visibility.Visible,"F2 enters numeric editing");Check(ReferenceEquals(FocusManager.GetFocusedElement(Root.XamlRoot),number.Editor),"Text editor retains real focus");}
        try {
            posterRepairCancellation.Cancel();ShutdownPerformanceMonitor();ShutdownWindowPlacement();
            var culture=CultureInfo.CurrentCulture;
            foreach(string locale in new[]{"zh-CN","en-US","de-DE"}) {
                CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo(locale);
                foreach(var sample in new[]{("-12.25",-50d,50d,2,-12.25),("２００",0d,200d,0,200d),("0.3333",0d,1d,2,.33),("12",10d,86400d,0,12d),("12.345",0d,60d,3,12.345)})
                    Check(NumericValueRules.TryRead(sample.Item1,sample.Item2,sample.Item3,sample.Item4,out double parsed,out _)&&parsed==sample.Item5,locale+" valid numeric "+sample.Item1);
                foreach(string invalid in new[]{"","-",".","12abc","中文","1e2","NaN","Infinity","1+2","1.2.3","1,000,000","999999999999999999999999999"})
                    Check(!NumericValueRules.TryRead(invalid,0,200,0,out _,out _),locale+" invalid numeric "+invalid);
                Check(!NumericValueRules.TryRead("2.5",0,2000,0,out _,out _),"Count rejects fractional input");
                Check(!NumericValueRules.TryRead("201",0,200,0,out _,out _),"Range rejects overflow");
            }
            CultureInfo.CurrentCulture=culture;
            var number=new ScrubNumberInput("测试数值",true) {Width=140};number.Configure(0,200,100,1,0,"");
            var neutral=new Button {Content="离开输入",Width=140};var host=new StackPanel {Spacing=12,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};host.Children.Add(number);host.Children.Add(neutral);Grid.SetRowSpan(host,2);Root.Children.Add(host);await Settle();
            int changes=0;number.ValueChanged+=_=>changes++;
            await Focus(number);Check(number.Editor.SelectionLength==number.Editor.Text.Length,"First focus selects whole value");
            number.BeginEdit();number.Editor.Text="";
            foreach(char ch in "150"){await Message(0x102,ch);await Message(0x101,ch);Check(number.Value==100&&changes==0,$"Key-up does not commit draft {ch} (value={number.Value}, changes={changes}, draft={number.Editor.Text})");}
            Check(number.Editor.Text=="150","Native character messages edit persistent text surface");
            await Key(13);Check(number.Value==150&&changes==1&&number.Editor.IsReadOnly,"Enter commits once and restores drag mode");
            Check(number.Editor.Visibility==Visibility.Collapsed&&number.DragSurface.Visibility==Visibility.Visible,"Accepted text returns to a non-text drag surface");
            number.BeginEdit();number.Editor.Text="201";await Key(13);Check(number.Value==150&&number.Editor.Text=="201"&&number.ValidationMessage.Length>0,"Invalid Enter preserves draft and reports range");
            neutral.Focus(FocusState.Programmatic);await Settle();Check(number.Value==150&&number.Editor.Text=="150","Invalid focus loss restores accepted value");
            await Focus(number);number.BeginEdit();number.Editor.Text="75";await Key(27);Check(number.Value==150&&number.Editor.Text=="150"&&changes==1,"Escape cancels without changing model");
            number.BeginEdit();number.Editor.Text="125";number.Configure(0,200,150,1,0,"");Check(number.Editor.Text=="125","Unchanged refresh preserves draft");
            await Key(9);await Settle();Check(number.Value==125&&changes==2&&!ReferenceEquals(FocusManager.GetFocusedElement(Root.XamlRoot),number.Editor),"Native Tab commits once and moves focus");
            await Focus(number);number.BeginEdit();number.Editor.Text="175";number.Configure(0,100,100,1,0,"");Check(number.Editor.Text=="100"&&number.Value==100,"Dynamic bounds replace obsolete draft");
            number.Configure(-50,50,0,.5,2,"");number.BeginEdit();number.Editor.Text="-";Check(number.Value==0,"Partial negative stays draft");number.BeginEdit();number.Editor.Text="-12.34";await Key(13);Check(number.Value==-12.34,"Signed decimals independent of drag step");
            number.BeginEdit();number.Editor.Text="20";number.IsEnabled=false;Check(number.Value==-12.34,"Disabling cancels draft");number.IsEnabled=true;
            var slider=new Slider {Minimum=0,Maximum=5,StepFrequency=.05,Value=1};var linked=new EffectValueInput {Target=slider,Precision=2,Unit="×"};host.Children.Add(linked);await Settle();
            var linkedNumber=NumericElements(linked).OfType<ScrubNumberInput>().Single();await Focus(linkedNumber);linkedNumber.BeginEdit();linkedNumber.Editor.Text="1.23";linkedNumber.CommitEdit(false);Check(slider.Value==1.23&&linkedNumber.Value==1.23,"Typed precision synchronizes with actual slider value");
            slider.Value=2;Check(linkedNumber.Value==2,"Slider still updates editor immediately");
            await Focus(number);number.BeginEdit();number.Editor.SelectAll();number.Editor.SelectedText="12abc";await Settle();bool invalidAccepted=number.CommitEdit(false);Check(!invalidAccepted&&number.Value==-12.34,$"Invalid pasted text is rejected as a whole (accepted={invalidAccepted}, value={number.Value}, text={number.Editor.Text})");number.CancelEdit();
            string beforePaste=number.Editor.Text;number.BeginEdit();number.Editor.SelectAll();number.Editor.SelectedText=new string('1',65);await Settle();Check(number.Editor.Text==beforePaste,"Overlong paste is rejected instead of truncated");
            var picker=new DiscColorPicker(Windows.UI.Color.FromArgb(255,20,30,40));host.Children.Add(picker);await Settle();int colors=0;picker.ColorChanged+=_=>colors++;
            var red=NumericElements(picker).OfType<ScrubNumberInput>().First(n=>Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(n.Editor)=="红色");await Focus(red);red.BeginEdit();red.Editor.Text="128";Check(picker.Color.R==20&&colors==0,"RGB remains draft before confirmation");await Key(13);Check(picker.Color.R==128&&colors==1,"RGB commits one valid channel change");
            var hex=NumericElements(picker).OfType<TextBox>().First(t=>Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(t)=="HEX 颜色值");hex.Focus(FocusState.Programmatic);await Settle();Check(hex.IsReadOnly,"HEX focus stays read-only");await Key(113);hex.Text="#123456";Check(picker.Color.R==128,"HEX remains draft");await Key(13);Check(picker.Color.R==18&&picker.Color.G==52&&picker.Color.B==86,"HEX confirms exact six-digit color");DoubleClickEdit.Begin(hex);hex.Text="bad";await Key(13);Check(hex.Text=="bad"&&picker.Color.R==18,"Invalid HEX Enter preserves draft");await Key(27);Check(hex.Text=="#123456","HEX Escape restores accepted color");
            host.Children.Remove(picker);
            var textField=new TextBox {Style=HomeStyle("UtilityTextBox"),Text="原名称"};host.Children.Add(textField);int acceptedText=0;DoubleClickEdit.OnAccepted(textField,()=>acceptedText++);await Settle();
            textField.Focus(FocusState.Programmatic);await Settle();Check(textField.IsReadOnly,"Plain text focus does not enable typing");
            await Key(113);textField.Text="取消名称";await Key(27);Check(textField.Text=="原名称"&&acceptedText==0&&textField.IsReadOnly,"Plain text Escape cancels without saving");
            await Key(113);textField.Text="新名称";await Key(13);Check(textField.Text=="新名称"&&acceptedText==1&&textField.IsReadOnly,"Plain text Enter saves once and exits editing");
            var outputField=new TextBox {Style=HomeStyle("UtilityTextBox"),Text="只读结果",IsReadOnly=true};host.Children.Add(outputField);await Settle();Check(!DoubleClickEdit.Begin(outputField)&&outputField.IsReadOnly,"Read-only output cannot be unlocked");
            Root.Children.Remove(host);await Settle();
            string screen=SelectedScreen!.DeviceName;store.SaveTargetEffects(screen,null,new(FillMode.Contain,new VideoEffects {Collection=new() {Added=EffectKind.Color},Brightness=.02}),VideoEffects.Original);
            Navigate("display");LoadEffectDraft(true);OpenEffect(EffectKind.Color);await Settle();
            var brightness=NumericElements((DependencyObject)BrightnessInput.Content).OfType<ScrubNumberInput>().Single();
            await Focus(brightness);brightness.BeginEdit();brightness.Editor.Text="-12";Check(BrightnessSlider.Value==2,"Actual effect remains unchanged while typing");await Key(13);
            Check(BrightnessSlider.Value==-12&&Math.Abs(store.ScreenProfile(screen)!.Effects.Brightness+.12)<.0001,"Effect commits and saves one value");
            await RestoreHistory(false);Check(BrightnessSlider.Value==2,"One undo restores entire numeric edit");await RestoreHistory(true);Check(BrightnessSlider.Value==-12,"One redo restores committed numeric edit");
            OpenEffect(EffectKind.Color);await Settle();await Focus(brightness);brightness.BeginEdit();brightness.Editor.Text="8";Navigate("widgets");Check(Math.Abs(store.ScreenProfile(screen)!.Effects.Brightness-.08)<.0001,"Page switch commits before changing scope");
            widgets=new WidgetController(store,action=>DispatcherQueue.TryEnqueue(()=>action()));widgets.Save(new WidgetConfiguration {Widgets=[new WidgetInstance {Id="numeric-widget",Kind=WidgetKind.Text,Screen=screen,Enabled=false}]});RefreshWidgets();OpenWidget("numeric-widget");await Settle();
            var x=NumericElements(WidgetEditorPanel).OfType<WidgetPositionInput>().First();await Focus(x);x.BeginEdit();x.Editor.Text="23";x.CommitEdit(false);Check(x.Value==23,"Component position accepts typed pixels");
            Check(widgets.ProcessId==null,"Disabled fixtures do not launch component host");
            Navigate("wallpaper");PlaybackIntervalBox.IsEnabled=true;await Focus(PlaybackIntervalBox);PlaybackIntervalBox.BeginEdit();PlaybackIntervalBox.Editor.Text="12";PlaybackIntervalBox.CommitEdit(false);Check(playbackDraft.IntervalSeconds==12,"Playback accepts seconds independent of five-second drag step");
            var bitmap=new RenderTargetBitmap();await bitmap.RenderAsync(Root);using(var stream=File.Create(Path.Combine(output,"numeric-home.png"))){var encoder=await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId,stream.AsRandomAccessStream());encoder.SetPixelData(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Premultiplied,(uint)bitmap.PixelWidth,(uint)bitmap.PixelHeight,96,96,(await bitmap.GetPixelsAsync()).ToArray());await encoder.FlushAsync();}
            Check(wallpapers==null&&widgets.ProcessId==null,"No production hosts started");
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new {passed=true,checks,productionDataAccessed=false,actualInstallationRun=false,nativeInput=NumericRegressionProgram.DirectedInput?"Process-guarded Win32 messages to isolated input window; no system-wide input":"Foreground-guarded Win32 keyboard input to isolated test window; no mouse injection"},new JsonSerializerOptions {WriteIndented=true}));
            widgets.Dispose();ShutdownImports();store.Dispose();App.ChildProcesses.Dispose();Close();NumericRegressionProgram.RestoreForeground();Environment.Exit(0);
        }catch(Exception e){NumericRegressionProgram.Fail(e);}
    }
}



