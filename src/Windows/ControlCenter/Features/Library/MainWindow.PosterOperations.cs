using System.Text.Json;
using LumaWall.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private PosterCatalogService posterCatalog=null!;
    private readonly CancellationTokenSource mediaOperationLifetime=new();
    private PosterChoice? ReadPosterChoice(string id)=>posterCatalog.ReadPosterChoice(id);
    private Task<ProbeResult> ProbePreservingPoster(string path,string id,CancellationToken token,bool automatic=false,bool requireExisting=false,Action? beforeCommit=null)
        =>posterCatalog.ProbePreservingPoster(path,id,token,automatic,requireExisting,beforeCommit);
    private Task<PosterChoice> SavePosterFrame(MediaAsset asset,double seconds,CancellationToken token,Action? beforeCommit=null)
        =>posterCatalog.SavePosterFrame(asset,seconds,token,beforeCommit);
    private Task<PosterChoice> ResetPoster(MediaAsset asset,CancellationToken token,Action? beforeCommit=null)
        =>posterCatalog.ResetPoster(asset,token,beforeCommit);
    private void RefreshPosterDisplays(string id)
    {
        if(libraryShutdown)return;
        var rows=LibraryList.ItemsSource as IEnumerable<LibraryRow>??LibraryList.Items.OfType<LibraryRow>();
        foreach(var row in rows.Where(row=>row.Asset.Id==id))row.RefreshPoster();
        foreach(var row in playbackQueueRows.Where(row=>row.Id==id))row.RefreshPoster();
        if(librarySelectionId==id)RefreshLibraryInformation();
        var playing=SelectedScreen==null?null:wallpapers?.Running.GetValueOrDefault(SelectedScreen.DeviceName)?.Asset;
        if(HomePreviewTarget(playing)?.Id==id)RefreshHomePreview(HomePreviewTarget(playing),playing,forcePoster:true);
    }
    private async void LibraryPoster_Click(object sender,RoutedEventArgs e)
    {
        if(SelectedLibraryAssets() is not [var asset]||asset.IsImage)return;
        try {await ShowPosterPicker(asset);}catch(Exception ex) {ShowError(UiLanguage.Text("封面选择失败：")+ex.Message,ex);}
    }
    private async Task ShowPosterPicker(MediaAsset asset)
    {
        if(!File.Exists(asset.Path))throw new IOException(UiLanguage.Text("素材源文件不可用"));
        using var lifetime=CancellationTokenSource.CreateLinkedTokenSource(mediaOperationLifetime.Token);
        string preview=Path.Combine(store.Root,"effect-preview","poster-"+Guid.NewGuid().ToString("N")+".bmp");
        Directory.CreateDirectory(Path.GetDirectoryName(preview)!);
        using var session=new PosterPreviewSession(asset.Path,preview);
        var image=new Image {Stretch=Stretch.Uniform,Height=Math.Clamp(Root.ActualHeight*.34,120,300)};
        var inset=new Border {Style=HomeStyle("LibraryInset"),CornerRadius=new CornerRadius(12),Padding=new Thickness(3),Child=image};
        var status=UtilityText(UiLanguage.Text("正在准备画面…"));
        var time=new ScrubNumberInput(UiLanguage.Text("封面位置，秒"));time.Configure(0,Math.Max(0,Math.Floor((asset.Duration-.000001)*1000)/1000),0,.001,3,UiLanguage.Text("秒"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(time,UiLanguage.Text("封面位置，秒"));
        var slider=new Slider {Style=HomeStyle("HomeSlider"),Minimum=0,Maximum=Math.Max(.001,asset.Duration-.000001),StepFrequency=.001};
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(slider,UiLanguage.Text("封面时间轴"));
        Button Button(string text) {var b=new Button {Style=HomeStyle("UtilityButton"),HorizontalAlignment=HorizontalAlignment.Stretch};UtilityButtonContent(b,text);return b;}
        var previous=Button(UiLanguage.Text("上一帧"));var play=Button(UiLanguage.Text("播放"));var next=Button(UiLanguage.Text("下一帧"));
        var controls=new Grid {ColumnSpacing=8};for(int i=0;i<3;i++)controls.ColumnDefinitions.Add(new());
        controls.Children.Add(previous);Grid.SetColumn(play,1);controls.Children.Add(play);Grid.SetColumn(next,2);controls.Children.Add(next);
        var stack=new StackPanel {Spacing=12,Width=Math.Max(260,Math.Min(640,Root.ActualWidth-120))};
        stack.Children.Add(UtilityText(asset.Name,"UtilityHeading"));stack.Children.Add(inset);stack.Children.Add(slider);stack.Children.Add(time);stack.Children.Add(controls);stack.Children.Add(status);
        var scroll=new ScrollViewer {Content=stack,MaxHeight=Math.Max(220,Root.ActualHeight-220),VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Style=HomeStyle("HomeScrollViewer")};
        var dialog=new ContentDialog {XamlRoot=Root.XamlRoot,Title=UiLanguage.Text("选择封面帧"),Content=scroll,PrimaryButtonText=UiLanguage.Text("设为封面"),SecondaryButtonText=UiLanguage.Text("自动选封面"),CloseButtonText=UiLanguage.Text("取消"),DefaultButton=ContentDialogButton.None,Style=HomeStyle("LibraryDialog"),IsPrimaryButtonEnabled=false};
        dialog.Resources["ContentDialogMaxWidth"]=760d;
        bool closed=false,busy=false,sync=false,playing=false,committing=false;
        int revision=0,direction=0;double requested=ReadPosterChoice(asset.Id)?.Seconds??0;
        PosterFrameResult? selected=null;
        var debounce=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(250)};
        var clock=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(125)};
        void StopPlay() {playing=false;clock.Stop();UtilityButtonContent(play,UiLanguage.Text("播放"));}
        async Task Load()
        {
            if(closed||busy||committing)return;
            busy=true;int generation=revision;double wanted=requested;int step=direction;
            dialog.IsPrimaryButtonEnabled=false;status.Text=UiLanguage.Text("正在定位画面…");
            try {
                var result=await session.Frame(wanted,step,lifetime.Token);
                if(closed||generation!=revision)return;
                byte[] bytes=await File.ReadAllBytesAsync(preview,lifetime.Token);
                using var memory=new MemoryStream(bytes);
                var bitmap=new BitmapImage();await bitmap.SetSourceAsync(memory.AsRandomAccessStream());
                if(closed||generation!=revision)return;
                image.Source=bitmap;selected=result;
                sync=true;slider.Value=result.Seconds;if(!time.IsInteracting)time.Value=result.Seconds;sync=false;
                status.Text=UiLanguage.Format($"{TimeSpan.FromSeconds(result.Seconds):hh\\:mm\\:ss\\.fff} / {TimeSpan.FromSeconds(asset.Duration):hh\\:mm\\:ss} · 原始画面");
            }catch(OperationCanceledException) {if(!closed)status.Text=UiLanguage.Text("预览已取消，请关闭后重新打开");}
             catch(Exception ex) {selected=null;status.Text=UiLanguage.Text("无法读取画面：")+Translate(ex.Message);}
            finally {
                busy=false;if(!closed&&!committing) {
                    dialog.IsPrimaryButtonEnabled=selected!=null&&generation==revision;
                    if(generation!=revision)_=Load();
                }
            }
        }
        void Request(double seconds,int step=0,bool delayed=false)
        {
            if(closed||committing)return;
            requested=Math.Clamp(seconds,0,Math.Max(0,asset.Duration-.000001));direction=step;++revision;dialog.IsPrimaryButtonEnabled=false;
            debounce.Stop();if(delayed)debounce.Start();else _=Load();
        }
        debounce.Tick+=(_,_)=> {debounce.Stop();_=Load();};
        slider.ValueChanged+=(_,_)=> {if(!sync) {StopPlay();Request(slider.Value,delayed:true);}};
        time.ValueChanged+=value=> {if(!sync&&double.IsFinite(value)) {StopPlay();Request(value,delayed:true);}};
        previous.Click+=(_,_)=> {StopPlay();Request(selected?.Seconds??requested,-1);};
        next.Click+=(_,_)=> {StopPlay();Request(selected?.Seconds??requested,1);};
        play.Click+=(_,_)=> {if(playing)StopPlay();else {playing=true;UtilityButtonContent(play,UiLanguage.Text("暂停"));clock.Start();}};
        clock.Tick+=(_,_)=> {if(busy||selected==null)return;if(selected.Seconds+Math.Max(.125,selected.FrameDuration)>=asset.Duration) {StopPlay();return;}Request(selected.Seconds+Math.Max(.125,selected.FrameDuration));};
        async Task Commit(ContentDialogButtonClickEventArgs args,bool automatic)
        {
            if(!automatic&&!time.CommitEdit(false)){args.Cancel=true;return;}
            // A just-committed time queues a preview before busy becomes true.
            // Keep the dialog open until that requested frame is ready.
            if(committing||(!automatic&&(busy||selected==null||!dialog.IsPrimaryButtonEnabled))) {args.Cancel=true;return;}
            var deferral=args.GetDeferral();StopPlay();debounce.Stop();committing=true;
            dialog.IsPrimaryButtonEnabled=dialog.IsSecondaryButtonEnabled=false;status.Text=UiLanguage.Text("正在保存封面…");
            try {
                if(automatic)await ResetPoster(asset,lifetime.Token);else await SavePosterFrame(asset,selected!.Seconds,lifetime.Token);
                Inform(automatic?UiLanguage.Text("已重新自动选择封面。"):UiLanguage.Text("已保存手动封面。"));
            }catch(Exception ex) {args.Cancel=true;status.Text=UiLanguage.Text("封面未保存：")+Translate(ex.Message);}
            finally {committing=false;dialog.IsPrimaryButtonEnabled=selected!=null;dialog.IsSecondaryButtonEnabled=true;deferral.Complete();}
        }
        dialog.PrimaryButtonClick+=async(_,args)=>await Commit(args,false);
        dialog.SecondaryButtonClick+=async(_,args)=>await Commit(args,true);
        dialog.Closing+=(_,_)=> {closed=true;StopPlay();debounce.Stop();lifetime.Cancel();session.Dispose();};
        Request(requested);
        try {await dialog.ShowAsync();}finally {closed=true;clock.Stop();debounce.Stop();lifetime.Cancel();}
    }
}
