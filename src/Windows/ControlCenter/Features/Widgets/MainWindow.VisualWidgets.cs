using LumaWall.Contracts;
using LumaWall.WidgetSupport;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private Action BuildVisualWidgetSettings(WidgetInstance widget)
    {
        string id=widget.Id;var initial=widget.Visual;var sync=new List<Action<VisualWidgetOptions>>();
        void Change(Func<VisualWidgetOptions,VisualWidgetOptions> update)=>LiveWidget(id,w=>w with {Visual=update(w.Visual)});
        StackPanel Section(string title) {var p=new StackPanel {Spacing=16};p.Children.Add(UtilityText(title,"UtilityHeading"));WidgetEditorPanel.Children.Add(UtilityCard(p));return p;}
        Slider Add(StackPanel p,string title,string unit,double min,double max,double step,int precision,Func<VisualWidgetOptions,double> read,Func<VisualWidgetOptions,double,VisualWidgetOptions> set) {
            var control=new Slider {Style=HomeStyle("HomeSlider"),Minimum=min,Maximum=max,StepFrequency=step,Value=Math.Clamp(read(initial),min,max)};
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(control,title);p.Children.Add(NumericSliderRow(title,control,unit,precision));control.ValueChanged+=(_,_)=>Change(s=>set(s,control.Value));sync.Add(s=>control.Value=Math.Clamp(read(s),control.Minimum,control.Maximum));return control;
        }
        void Toggle(StackPanel p,string title,Func<VisualWidgetOptions,bool> read,Func<VisualWidgetOptions,bool,VisualWidgetOptions> set) {var c=WidgetToggle(title,read(initial));p.Children.Add(WidgetToggleRow(title,c));c.Toggled+=(_,_)=>Change(s=>set(s,c.IsOn));sync.Add(s=>c.IsOn=read(s));}
        void Choice(StackPanel p,string title,string[] labels,Func<VisualWidgetOptions,int> read,Func<VisualWidgetOptions,int,VisualWidgetOptions> set) {var c=new ComboBox {Style=HomeStyle("UtilityComboBox"),HorizontalAlignment=HorizontalAlignment.Stretch};foreach(var label in labels)c.Items.Add(label);c.SelectedIndex=read(initial);p.Children.Add(WidgetField(title,c));c.SelectionChanged+=(_,_)=> {if(c.SelectedIndex>=0)Change(s=>set(s,c.SelectedIndex));};sync.Add(s=>c.SelectedIndex=read(s));}
        void Color(StackPanel p,string title,Func<VisualWidgetOptions,string> read,Func<VisualWidgetOptions,string,VisualWidgetOptions> set)=>p.Children.Add(WidgetColorField(title,read(initial),()=>read(widgets!.State.Widgets.First(w=>w.Id==id).Visual),v=>Change(s=>set(s,v))));
        var dimensions=Section(UiLanguage.Text("尺寸"));var maximum=WidgetPlacement.MaximumSize(widget,WidgetPlacement.Resolve(widget.Screen));
        var width=Add(dimensions,UiLanguage.Text("宽度"),"DIP",24,maximum.Width,1,0,s=>s.Width,(s,v)=>s with {Width=(int)v});var height=Add(dimensions,UiLanguage.Text("高度"),"DIP",24,maximum.Height,1,0,s=>s.Height,(s,v)=>s with {Height=(int)v});
        
        if(widget.Kind==WidgetKind.Text) {
            var text=Section(UiLanguage.Text("文字内容"));var input=new TextBox {Style=HomeStyle("UtilityTextBox"),Text=initial.Text,MaxLength=2000,AcceptsReturn=true,Height=double.NaN,Padding=new Thickness(12,8,12,8),VerticalContentAlignment=VerticalAlignment.Top,TextWrapping=TextWrapping.Wrap,MinHeight=100};text.Children.Add(input);DoubleClickEdit.OnAccepted(input,()=>Change(s=>s with {Text=input.Text}));
            Add(text,UiLanguage.Text("字号"),"DIP",8,240,1,0,s=>s.FontSize,(s,v)=>s with {FontSize=v});Toggle(text,UiLanguage.Text("粗体"),s=>s.Bold,(s,v)=>s with {Bold=v});Choice(text,UiLanguage.Text("文字对齐"),[UiLanguage.Text("靠左"),UiLanguage.Text("居中"),UiLanguage.Text("靠右")],s=>s.Alignment,(s,v)=>s with {Alignment=v});Color(text,UiLanguage.Text("文字颜色"),s=>s.TextColor,(s,v)=>s with {TextColor=v});
        }
        if(TimeWidgets.IsTime(widget.Kind)) {var text=Section(UiLanguage.Text("文字样式"));Add(text,UiLanguage.Text("主数字字号"),"DIP",8,240,1,0,s=>s.FontSize,(s,v)=>s with {FontSize=v});Toggle(text,UiLanguage.Text("粗体"),s=>s.Bold,(s,v)=>s with {Bold=v});Choice(text,UiLanguage.Text("文字对齐"),[UiLanguage.Text("靠左"),UiLanguage.Text("居中"),UiLanguage.Text("靠右")],s=>s.Alignment,(s,v)=>s with {Alignment=v});Color(text,UiLanguage.Text("文字颜色"),s=>s.TextColor,(s,v)=>s with {TextColor=v});}
        if(widget.Kind==WidgetKind.Sticker) {
            var sticker=Section(UiLanguage.Text("贴纸内容"));var source=UtilityText(initial.Image==null?UiLanguage.Text("内置贴纸"):UiLanguage.Text("自定义图片"));sticker.Children.Add(source);
            Choice(sticker,UiLanguage.Text("内置形状"),[UiLanguage.Text("星星"),UiLanguage.Text("圆形"),UiLanguage.Text("爱心"),UiLanguage.Text("闪电")],s=>s.StickerShape,(s,v)=>s with {StickerShape=v,Image=null});Color(sticker,UiLanguage.Text("内置贴纸颜色"),s=>s.BorderColor,(s,v)=>s with {BorderColor=v});
            var upload=WidgetButton(UiLanguage.Text("上传图片…"),()=>{},"Image");upload.Click+=async (_,_)=> {
                upload.IsEnabled=false;
                try {var image=await PickStickerImage();if(image!=null&&widgets?.State.Widgets.Any(w=>w.Id==id)==true)Change(s=>s with {Image=image});}catch(Exception ex) {ShowError(ex.Message,ex);}finally {upload.IsEnabled=true;}
            };sticker.Children.Add(upload);sticker.Children.Add(WidgetButton(UiLanguage.Text("恢复内置贴纸"),()=>Change(s=>s with {Image=null})));
            Choice(sticker,UiLanguage.Text("图片填充"),[UiLanguage.Text("完整显示 · 保留比例"),UiLanguage.Text("填满 · 居中裁剪"),UiLanguage.Text("拉伸填满")],s=>s.ImageFit,(s,v)=>s with {ImageFit=v});Toggle(sticker,UiLanguage.Text("水平翻转"),s=>s.FlipHorizontal,(s,v)=>s with {FlipHorizontal=v});
            sync.Add(s=>source.Text=s.Image==null?UiLanguage.Text("内置贴纸"):UiLanguage.Text("自定义图片"));
        }
        var decoration=Section(widget.Kind==WidgetKind.Mask?UiLanguage.Text("遮罩外观"):UiLanguage.Text("背景与边框"));
        if(widget.Kind!=WidgetKind.Mask)Toggle(decoration,UiLanguage.Text("显示背景"),s=>s.Background,(s,v)=>s with {Background=v});
        else {Choice(decoration,UiLanguage.Text("形状"),[UiLanguage.Text("圆角矩形"),UiLanguage.Text("椭圆")],s=>s.MaskShape,(s,v)=>s with {MaskShape=v});Choice(decoration,UiLanguage.Text("遮罩渐变"),[UiLanguage.Text("纯色"),UiLanguage.Text("从左侧淡出"),UiLanguage.Text("从右侧淡出"),UiLanguage.Text("从顶部淡出"),UiLanguage.Text("从底部淡出")],s=>s.Gradient,(s,v)=>s with {Gradient=v});}
        Color(decoration,widget.Kind==WidgetKind.Mask?UiLanguage.Text("遮罩颜色"):UiLanguage.Text("背景颜色"),s=>s.FillColor,(s,v)=>s with {FillColor=v});Add(decoration,UiLanguage.Text("填充不透明度"),"%",0,100,1,0,s=>s.FillOpacity*100,(s,v)=>s with {FillOpacity=v/100});
        Add(decoration,UiLanguage.Text("圆角"),"DIP",0,300,1,0,s=>s.Radius,(s,v)=>s with {Radius=v});if(widget.Kind!=WidgetKind.Mask)Add(decoration,UiLanguage.Text("内边距"),"DIP",0,100,1,0,s=>s.Padding,(s,v)=>s with {Padding=v});
        Add(decoration,UiLanguage.Text("边框宽度"),"DIP",0,20,1,0,s=>s.BorderWidth,(s,v)=>s with {BorderWidth=v});Color(decoration,UiLanguage.Text("边框颜色"),s=>s.BorderColor,(s,v)=>s with {BorderColor=v});
        var interaction=Section(UiLanguage.Text("鼠标交互"));Toggle(interaction,UiLanguage.Text("鼠标穿透"),s=>s.ClickThrough,(s,v)=>s with {ClickThrough=v});
        return ()=> {var current=widgets?.State.Widgets.FirstOrDefault(w=>w.Id==id);if(current==null)return;var bounds=WidgetPlacement.MaximumSize(current,WidgetPlacement.Resolve(widgets!.State.LayoutGroups.FirstOrDefault(g=>g.Id==current.LayoutGroupId)?.Screen??current.Screen));width.Maximum=bounds.Width;height.Maximum=bounds.Height;foreach(var action in sync)action(current.Visual);};
    }
    private async Task<string?> PickStickerImage()
    {
        var picker=new FileOpenPicker();foreach(var extension in new[]{".png",".jpg",".jpeg",".webp"})picker.FileTypeFilter.Add(extension);
        WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(this));var file=await picker.PickSingleFileAsync();if(file==null)return null;
        using var input=await file.OpenAsync(FileAccessMode.Read);if(input.Size>32_000_000)throw new ArgumentException(UiLanguage.Text("图片文件最多 32 MB"));var decoder=await BitmapDecoder.CreateAsync(input);
        if(decoder.PixelWidth==0||decoder.PixelHeight==0||(ulong)decoder.PixelWidth*decoder.PixelHeight>32_000_000)throw new ArgumentException(UiLanguage.Text("图片最多 3200 万像素"));
        double scale=Math.Min(1,1024.0/Math.Max(decoder.PixelWidth,decoder.PixelHeight));uint width=Math.Max(1,(uint)(decoder.PixelWidth*scale)),height=Math.Max(1,(uint)(decoder.PixelHeight*scale));
        var pixels=await decoder.GetPixelDataAsync(BitmapPixelFormat.Rgba8,BitmapAlphaMode.Straight,new BitmapTransform {ScaledWidth=width,ScaledHeight=height,InterpolationMode=BitmapInterpolationMode.Fant},ExifOrientationMode.RespectExifOrientation,ColorManagementMode.ColorManageToSRgb);
        // RespectExifOrientation can swap the decoded dimensions.
        if(decoder.OrientedPixelWidth!=decoder.PixelWidth)(width,height)=(height,width);
        using var output=new InMemoryRandomAccessStream();var encoder=await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId,output);encoder.SetPixelData(BitmapPixelFormat.Rgba8,BitmapAlphaMode.Straight,width,height,96,96,pixels.DetachPixelData());await encoder.FlushAsync();
        if(output.Size>2_000_000)throw new ArgumentException(UiLanguage.Text("压缩后的贴纸超过 2 MB，请使用更小的图片"));output.Seek(0);using var reader=new DataReader(output.GetInputStreamAt(0));await reader.LoadAsync((uint)output.Size);var bytes=new byte[(int)output.Size];reader.ReadBytes(bytes);return Convert.ToBase64String(bytes);
    }
}
