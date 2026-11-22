using LumaWall.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Automation;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using System.Runtime.InteropServices.WindowsRuntime;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private StackPanel? particleRainPanel;
    private readonly List<Action> particleRefreshers=[];
    private void InitializeParticleControls(StackPanel panel,ToggleSwitch enabled,StackPanel rain)
    {
        particleRainPanel=rain;
        var main=new StackPanel {Spacing=16};panel.Children.Insert(1,main);BuildParticleLayer(main,false);
        var subSwitch=AdvancedToggle(UiLanguage.Text("子粒子"),e=>e.Particles.SubLayer.Enabled,(e,v)=>e with {Particles=e.Particles with {SubLayer=e.Particles.SubLayer with {Enabled=v}}});
        panel.Children.Add(UtilityCard(subSwitch,UiLanguage.Text("子粒子图层")));
        var sub=new StackPanel {Spacing=16};panel.Children.Add(sub);BuildParticleLayer(sub,true);
        particleRefreshers.Add(()=>sub.Visibility=effectSession.ExtendedDraft.Particles.SubLayer.Enabled?Visibility.Visible:Visibility.Collapsed);
        extendedLoaders.Add(_=>RefreshParticleFields());RefreshParticleFields();
    }
    private void BuildParticleLayer(StackPanel host,bool sub)
    {
        ParticleLayerOptions Get(VideoEffects e)=>sub?e.Particles.SubLayer:e.Particles;
        VideoEffects Write(VideoEffects e,Func<ParticleLayerOptions,ParticleLayerOptions> update)=>e with {Particles=sub
            ?e.Particles with {SubLayer=update(e.Particles.SubLayer)}:(ParticleOptions)update(e.Particles)};
        FrameworkElement Choice(string label,string[] items,Func<ParticleLayerOptions,int> get,Func<ParticleLayerOptions,int,ParticleLayerOptions> set)=>
            AdvancedChoice(label,items,e=>get(Get(e)),(e,v)=>Write(e,p=>set(p,v)),stableText:true);
        FrameworkElement Toggle(string label,Func<ParticleLayerOptions,bool> get,Func<ParticleLayerOptions,bool,ParticleLayerOptions> set)=>
            AdvancedToggle(label,e=>get(Get(e)),(e,v)=>Write(e,p=>set(p,v)));
        FrameworkElement Slider(string label,double min,double max,double step,string unit,int digits,Func<ParticleLayerOptions,double> get,Func<ParticleLayerOptions,double,ParticleLayerOptions> set)=>
            ExtendedSlider(label,min,max,step,unit,digits,e=>get(Get(e)),(e,v)=>Write(e,p=>set(p,v)));
        StackPanel Section(StackPanel parent,string title) {
            var section=new StackPanel {Spacing=16};section.Children.Add(UtilityText(sub?UiLanguage.Text("子粒子 · ")+title:title,"UtilityHeading"));parent.Children.Add(UtilityCard(section));return section;
        }
        var appearance=Section(host,UiLanguage.Text("粒子外观"));
        
        var picker=new ComboBox {Style=HomeStyle("StringChoiceComboBox"),HorizontalAlignment=HorizontalAlignment.Stretch};
        var style=WidgetField(UiLanguage.Text("粒子样式"),picker);
        IEnumerable<ParticleAppearance> Styles()=>ParticleArtwork.All.Where(a=>!sub||a.Id!=5);
        bool refreshing=false;
        void LoadStyles() {
            refreshing=true;
            try {
                picker.Items.Clear();
                foreach(var item in Styles().Where(a=>a.File==null||ExpansionCatalog.ProvidesEffect(EffectKind.Particles,"particles.artwork")))
                    picker.Items.Add(new ComboBoxItem {Content=UiLanguage.Text(item.Name),Tag=item.Id});
                SelectStyle(Get(effectSession.ExtendedDraft).Style);
            } finally {refreshing=false;}
        }
        void SelectStyle(int id) {
            picker.SelectedItem=picker.Items.OfType<ComboBoxItem>().FirstOrDefault(item=>(int)item.Tag==id)??picker.Items[0];
        }
        LoadStyles();
        picker.SelectionChanged+=(_,_)=> {
            if(refreshing||loadingEffects||picker.SelectedItem is not ComboBoxItem item)return;
            effectSession.ExtendedDraft=Write(effectSession.ExtendedDraft,p=>p with {Style=(int)item.Tag});
            DraftChanged();
        };
        extendedLoaders.Add(e=>SelectStyle(Get(e).Style));
        appearance.Children.Add(style);
        appearance.Children.Add(Toggle(UiLanguage.Text("随机粒子"),p=>p.RandomAppearance,(p,v)=> {
            var choices=p.RandomStyles.Where(id=>id!=7||p.Image.Length>0).ToArray();
            return p with {RandomAppearance=v,RandomStyles=choices.Length==0?[0]:choices};
        }));
        var choicesPanel=new StackPanel {Spacing=12};appearance.Children.Add(choicesPanel);
        choicesPanel.Children.Add(UtilityText(UiLanguage.Text("候选外观"),"UtilityLabel"));
        var choicesGrid=new Grid {RowSpacing=8,ColumnSpacing=8};choicesPanel.Children.Add(choicesGrid);
        var checks=new List<(int Id,CheckBox Box)>();
        void SyncChoices() {
            if(!ExpansionCatalog.ProvidesEffect(EffectKind.Particles,"particles.artwork")) {
                foreach(var item in checks.Where(c=>c.Id>=ParticleArtwork.FirstStyle).ToArray()) {
                    choicesGrid.Children.Remove(item.Box);
                    checks.Remove(item);
                }
            }
            foreach(var item in Styles().Where(a=>a.Id!=5&&(a.File==null||ExpansionCatalog.ProvidesEffect(EffectKind.Particles,"particles.artwork"))&&checks.All(c=>c.Id!=a.Id))) {
                var content=new StackPanel {Orientation=Orientation.Horizontal,Spacing=8};
                if(item.File!=null)content.Children.Add(new Image {Width=32,Height=32,Source=new BitmapImage(new Uri(ParticleArtwork.ParticlePath(item.File)))});
                content.Children.Add(UtilityText(UiLanguage.Text(item.Name),"UtilityLabel"));
                var box=new CheckBox {Content=content,Style=HomeStyle("LibraryCheckBox"),HorizontalAlignment=HorizontalAlignment.Stretch,MinHeight=44};
                AutomationProperties.SetName(box,(sub?UiLanguage.Text("子粒子 "):UiLanguage.Text("粒子 "))+UiLanguage.Text(item.Name));
                checks.Add((item.Id,box));choicesGrid.Children.Add(box);
                void Changed(object sender,RoutedEventArgs args) {
                    if(loadingEffects||refreshing)return;
                    var p=Get(effectSession.ExtendedDraft);var next=checks.Where(c=>c.Box.IsChecked==true).Select(c=>c.Id).ToArray();
                    if(next.Length==0) {refreshing=true;box.IsChecked=true;refreshing=false;return;}
                    effectSession.ExtendedDraft=Write(effectSession.ExtendedDraft,current=>current with {RandomStyles=next});DraftChanged();
                }
                box.Checked+=Changed;box.Unchecked+=Changed;
            }
        }
        SyncChoices();
        void LayoutChoices(double width) {
            int columns=width>=600?3:width>=360?2:1;
            if(choicesGrid.ColumnDefinitions.Count==columns&&choicesGrid.RowDefinitions.Count==(checks.Count+columns-1)/columns)return;
            choicesGrid.ColumnDefinitions.Clear();choicesGrid.RowDefinitions.Clear();
            for(int i=0;i<columns;i++)choicesGrid.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(1,GridUnitType.Star)});
            for(int i=0;i<(checks.Count+columns-1)/columns;i++)choicesGrid.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});
            for(int i=0;i<checks.Count;i++){Grid.SetColumn(checks[i].Box,i%columns);Grid.SetRow(checks[i].Box,i/columns);}
        }
        choicesGrid.SizeChanged+=(_,args)=>LayoutChoices(args.NewSize.Width);LayoutChoices(0);
        var uploadPanel=new StackPanel {Spacing=12};appearance.Children.Add(uploadPanel);
        string? loadedImage=null;
        var imageName=UtilityText("");var preview=new Image {Width=80,Height=80,HorizontalAlignment=HorizontalAlignment.Left};
        appearance.Children.Add(preview);appearance.Children.Add(imageName);
        var upload=WidgetButton(UiLanguage.Text("上传图片…"),()=>{},"Image");uploadPanel.Children.Add(upload);
        upload.Click+=async (_,_)=> {
            upload.IsEnabled=false;
            try {
                var picked=await PickParticleImage();if(picked==null)return;
                effectSession.ExtendedDraft=Write(effectSession.ExtendedDraft,p=>p with {Image=picked.Value.Data,ImageName=picked.Value.Name,Style=7});
                loadingEffects=true;try {foreach(var load in extendedLoaders)load(effectSession.ExtendedDraft);}finally {loadingEffects=false;}DraftChanged();
            }catch(Exception ex){ShowError(ex.Message,ex);}finally{upload.IsEnabled=true;}
        };
        uploadPanel.Children.Add(WidgetButton(UiLanguage.Text("移除图片"),()=> {
            effectSession.ExtendedDraft=Write(effectSession.ExtendedDraft,p=> {
                var next=p.RandomStyles.Where(id=>id!=7).ToArray();
                return p with {Image="",ImageName="",RandomStyles=next.Length==0?[0]:next};
            });DraftChanged();
        }));
        async void RefreshPreview(ParticleLayerOptions p) {
            var artwork=ParticleArtwork.All.FirstOrDefault(a=>a.Id==p.Style);var key=p.RandomAppearance?"random":p.Style>=8&&ExpansionCatalog.ProvidesEffect(EffectKind.Particles,"particles.artwork")?artwork?.File:p.Style==7?p.Image:"";
            preview.Visibility=string.IsNullOrEmpty(key)||p.RandomAppearance?Visibility.Collapsed:Visibility.Visible;
            imageName.Visibility=!p.RandomAppearance&&p.Style==7?Visibility.Visible:Visibility.Collapsed;
            imageName.Text=p.Image.Length==0?UiLanguage.Text("未上传图片"):p.ImageName.Length==0?UiLanguage.Text("自定义图片"):p.ImageName;
            if(loadedImage==key)return;loadedImage=key;preview.Source=null;
            if(string.IsNullOrEmpty(key)||p.RandomAppearance)return;
            if(p.Style>=8){preview.Source=new BitmapImage(new Uri(ParticleArtwork.ParticlePath(artwork!.File!)));return;}
            try {
                var bytes=ParticlePixels.Decode(p.Image);
                using var bitmap=SoftwareBitmap.CreateCopyFromBuffer(bytes.AsBuffer(),BitmapPixelFormat.Rgba8,ParticlePixels.Size,ParticlePixels.Size,BitmapAlphaMode.Straight);
                using var converted=SoftwareBitmap.Convert(bitmap,BitmapPixelFormat.Bgra8,BitmapAlphaMode.Premultiplied);
                var source=new SoftwareBitmapSource();await source.SetBitmapAsync(converted);if(loadedImage==key)preview.Source=source;
            }catch(Exception ex){ShowError(ex.Message,ex);}
        }
        var appearanceDetails=new StackPanel {Spacing=16};appearance.Children.Add(appearanceDetails);
        appearanceDetails.Children.Add(Slider(UiLanguage.Text("粒子大小"),1,256,1,"px",0,p=>p.Size,(p,v)=>p with {Size=v}));
        appearanceDetails.Children.Add(Slider(UiLanguage.Text("大小随机"),0,100,1,"%",0,p=>p.SizeRandom*100,(p,v)=>p with {SizeRandom=v/100}));
        appearanceDetails.Children.Add(Toggle(UiLanguage.Text("粒子颜色"),p=>p.ColorEnabled,(p,v)=>p with {ColorEnabled=v}));
        var color=AdvancedColor(UiLanguage.Text("颜色"),e=>Get(e).Color,(e,v)=>Write(e,p=>p with {Color=v}));appearanceDetails.Children.Add(color);
        appearanceDetails.Children.Add(Slider(UiLanguage.Text("不透明度"),0,100,1,"%",0,p=>p.Opacity*100,(p,v)=>p with {Opacity=v/100}));
        var general=new StackPanel {Spacing=16};host.Children.Add(general);
        var emitter=Section(general,UiLanguage.Text("发射器"));
        emitter.Children.Add(Choice(UiLanguage.Text("发射类型"),[UiLanguage.Text("持续"),UiLanguage.Text("喷发")],p=>p.Emission,(p,v)=>p with {Emission=v}));
        emitter.Children.Add(Slider(UiLanguage.Text("粒子数量"),1,2000,1,UiLanguage.Text("个"),0,p=>p.Count,(p,v)=>p with {Count=(int)v}));
        emitter.Children.Add(Slider(UiLanguage.Text("存活时间"),.2,60,.1,UiLanguage.Text("秒"),1,p=>p.Lifetime,(p,v)=>p with {Lifetime=v}));
        var burst=Slider(UiLanguage.Text("喷发间歇"),0,60,.1,UiLanguage.Text("秒"),1,p=>p.BurstInterval,(p,v)=>p with {BurstInterval=v});emitter.Children.Add(burst);
        emitter.Children.Add(Choice(UiLanguage.Text("发射器形状"),[UiLanguage.Text("区域"),UiLanguage.Text("点"),UiLanguage.Text("横线")],p=>p.Emitter,(p,v)=>p with {Emitter=v}));
        var region=Toggle(UiLanguage.Text("指定区域"),p=>p.LocalRegion,(p,v)=>p with {LocalRegion=v});emitter.Children.Add(region);
        var bounds=new StackPanel {Spacing=16};emitter.Children.Add(bounds);
        bounds.Children.Add(Slider(UiLanguage.Text("位置 X"),0,100,1,"%",0,p=>p.CenterX*100,(p,v)=>p with {CenterX=v/100}));
        bounds.Children.Add(Slider(UiLanguage.Text("位置 Y"),0,100,1,"%",0,p=>p.CenterY*100,(p,v)=>p with {CenterY=v/100}));
        var width=Slider(UiLanguage.Text("发射器宽度"),5,100,1,"%",0,p=>p.Width*100,(p,v)=>p with {Width=v/100});bounds.Children.Add(width);
        var height=Slider(UiLanguage.Text("发射器高度"),5,100,1,"%",0,p=>p.Height*100,(p,v)=>p with {Height=v/100});bounds.Children.Add(height);
        emitter.Children.Add(Slider(UiLanguage.Text("发射方向"),0,360,1,"°",0,p=>p.Direction,(p,v)=>p with {Direction=v}));
        emitter.Children.Add(Slider(UiLanguage.Text("扩散角度"),0,360,1,"°",0,p=>p.Spread,(p,v)=>p with {Spread=v}));
        var motion=Section(general,UiLanguage.Text("粒子运动"));
        motion.Children.Add(Slider(UiLanguage.Text("移动速度"),0,600,1,"px/s",0,p=>p.Speed,(p,v)=>p with {Speed=v}));
        motion.Children.Add(Slider(UiLanguage.Text("速度随机"),0,100,1,"%",0,p=>p.SpeedRandom*100,(p,v)=>p with {SpeedRandom=v/100}));
        motion.Children.Add(Slider(UiLanguage.Text("重力"),-300,300,1,"px/s²",0,p=>p.Gravity,(p,v)=>p with {Gravity=v}));
        motion.Children.Add(Slider(UiLanguage.Text("摆动幅度"),0,100,1,"%",0,p=>p.Sway*100,(p,v)=>p with {Sway=v/100}));
        motion.Children.Add(Slider(UiLanguage.Text("旋转速度"),-180,180,1,"°/s",0,p=>p.Rotation,(p,v)=>p with {Rotation=v}));
        motion.Children.Add(Toggle(UiLanguage.Text("随机旋转"),p=>p.RandomRotation,(p,v)=>p with {RandomRotation=v}));
        motion.Children.Add(Slider(UiLanguage.Text("闪烁强度"),0,100,1,"%",0,p=>p.Twinkle*100,(p,v)=>p with {Twinkle=v/100}));
        motion.Children.Add(Toggle(UiLanguage.Text("淡入淡出"),p=>p.Fade,(p,v)=>p with {Fade=v}));
        general.Children.Add(UtilityCard(Choice(UiLanguage.Text("质量"),[UiLanguage.Text("低"),UiLanguage.Text("中"),UiLanguage.Text("高")],p=>p.Quality,(p,v)=>p with {Quality=v}),sub?UiLanguage.Text("子粒子 · 渲染质量"):UiLanguage.Text("渲染质量")));
        expansionRefreshers.Add(()=> {
            LoadStyles();SyncChoices();LayoutChoices(choicesGrid.ActualWidth);RefreshParticleFields();
        });
        particleRefreshers.Add(()=> {
            var p=Get(effectSession.ExtendedDraft);
            color.Visibility=p.ColorEnabled?Visibility.Visible:Visibility.Collapsed;
            style.Visibility=p.RandomAppearance?Visibility.Collapsed:Visibility.Visible;choicesPanel.Visibility=p.RandomAppearance?Visibility.Visible:Visibility.Collapsed;
            uploadPanel.Visibility=p.Style==7||p.RandomAppearance?Visibility.Visible:Visibility.Collapsed;
            appearanceDetails.Visibility=general.Visibility=p.IsRain?Visibility.Collapsed:Visibility.Visible;
            burst.Visibility=p.Emission==1?Visibility.Visible:Visibility.Collapsed;region.Visibility=p.Emitter==0?Visibility.Visible:Visibility.Collapsed;
            bounds.Visibility=p.Emitter!=0||p.LocalRegion?Visibility.Visible:Visibility.Collapsed;width.Visibility=p.Emitter!=1?Visibility.Visible:Visibility.Collapsed;height.Visibility=p.Emitter==0?Visibility.Visible:Visibility.Collapsed;
            refreshing=true;try {foreach(var c in checks){c.Box.IsChecked=p.RandomStyles.Contains(c.Id);c.Box.IsEnabled=c.Id!=7||p.Image.Length>0;}}finally{refreshing=false;}
            RefreshPreview(p);
        });
    }
    private void RefreshParticleFields() {
        if(particleRainPanel!=null)particleRainPanel.Visibility=effectSession.ExtendedDraft.Particles.IsRain?Visibility.Visible:Visibility.Collapsed;
        foreach(var refresh in particleRefreshers)refresh();
    }
    private async Task<(string Data,string Name)?> PickParticleImage() {
        var picker=new FileOpenPicker();foreach(var extension in new[]{".png",".jpg",".jpeg",".webp"})picker.FileTypeFilter.Add(extension);
        WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(this));var file=await picker.PickSingleFileAsync();if(file==null)return null;
        using var input=await file.OpenAsync(FileAccessMode.Read);if(input.Size>32_000_000)throw new ArgumentException(UiLanguage.Text("图片文件最多 32 MB"));var decoder=await BitmapDecoder.CreateAsync(input);
        if(decoder.PixelWidth==0||decoder.PixelHeight==0||(ulong)decoder.PixelWidth*decoder.PixelHeight>32_000_000)throw new ArgumentException(UiLanguage.Text("图片最多 3200 万像素"));
        double scale=512d/Math.Max(decoder.PixelWidth,decoder.PixelHeight);uint width=Math.Max(1,(uint)(decoder.PixelWidth*scale)),height=Math.Max(1,(uint)(decoder.PixelHeight*scale));
        var data=await decoder.GetPixelDataAsync(BitmapPixelFormat.Rgba8,BitmapAlphaMode.Straight,new BitmapTransform {ScaledWidth=width,ScaledHeight=height,InterpolationMode=BitmapInterpolationMode.Fant},ExifOrientationMode.RespectExifOrientation,ColorManagementMode.ColorManageToSRgb);
        if(decoder.OrientedPixelWidth!=decoder.PixelWidth)(width,height)=(height,width);
        var source=data.DetachPixelData();var pixels=new byte[ParticlePixels.Bytes];int x=(512-(int)width)/2,y=(512-(int)height)/2;
        for(int row=0;row<height;row++)Buffer.BlockCopy(source,row*(int)width*4,pixels,((y+row)*512+x)*4,(int)width*4);
        return (ParticlePixels.Encode(pixels),file.Name.Length>128?file.Name[..128]:file.Name);
    }
}
