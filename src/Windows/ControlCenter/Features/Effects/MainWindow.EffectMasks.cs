using LumaWall.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Runtime.InteropServices;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private StackPanel maskEditor=null!;
    private ComboBox maskList=null!,effectMaskChoice=null!;
    private FrameworkElement effectMaskRow=null!;
    private TextBlock maskStatus=null!;
    private string? selectedMaskId;
    private bool updatingMaskUi,maskReferencePending;
    private EffectDefinition CreateMaskLibrary()
    {
        var panel=new StackPanel {Spacing=16};var enabled=WidgetToggle(UiLanguage.Text("遮罩"),true);enabled.Visibility=Visibility.Collapsed;panel.Children.Add(enabled);
        maskList=new ComboBox {Style=HomeStyle("UtilityComboBox"),HorizontalAlignment=HorizontalAlignment.Stretch};panel.Children.Add(WidgetField(UiLanguage.Text("选择遮罩"),maskList));
        maskList.SelectionChanged+=(_,_)=> {if(updatingMaskUi||loadingEffects)return;selectedMaskId=maskList.SelectedItem is ComboBoxItem item?item.Tag as string:null;BuildMaskEditor();effectPreviewFrameKey=null;};
        panel.Children.Add(WidgetButton(UiLanguage.Text("添加遮罩"),()=> {if(effectSession.ExtendedDraft.Masks.Length>=16) {maskStatus.Text=UiLanguage.Text("最多可添加 16 个遮罩。");return;}int n=1;while(effectSession.ExtendedDraft.Masks.Any(m=>m.Name==UiLanguage.Text("遮罩 ")+n))n++;var mask=new EffectMask {Name=UiLanguage.Text("遮罩 ")+n};selectedMaskId=mask.Id;effectSession.ExtendedDraft=effectSession.ExtendedDraft with {Masks=[..effectSession.ExtendedDraft.Masks,mask]};RefreshMaskLibrary();DraftChanged();}));
        maskEditor=new StackPanel {Spacing=14};panel.Children.Add(UtilityCard(maskEditor,UiLanguage.Text("遮罩参数")));maskStatus=UtilityText("");panel.Children.Add(maskStatus);EffectsDetailSections.Children.Add(panel);
        return new(EffectKind.MaskLibrary,panel,enabled);
    }
    private void InitializeMaskReference(StackPanel summary)
    {
        effectMaskChoice=new ComboBox {Style=HomeStyle("UtilityComboBox"),HorizontalAlignment=HorizontalAlignment.Stretch};effectMaskRow=WidgetField(UiLanguage.Text("引用遮罩"),effectMaskChoice);summary.Children.Add(effectMaskRow);
        effectMaskChoice.SelectionChanged+=(_,_)=> {
            if(updatingMaskUi||loadingEffects||selectedEffectKind is not { } kind)return;
            if(kind is EffectKind.MaskLibrary or EffectKind.AudioReactive or EffectKind.HideTaskbar ||
                effectMaskChoice.SelectedItem is not ComboBoxItem {Tag:string id})return;
            if(id.Length>0&&!effectSession.ExtendedDraft.Masks.Any(m=>m.Id==id))return;
            string previous=effectSession.ExtendedDraft.EffectMasks.GetValueOrDefault(kind)??"";
            if(previous==id)return;
            var links=new Dictionary<EffectKind,string>(effectSession.ExtendedDraft.EffectMasks);
            if(id.Length>0)links[kind]=id;else links.Remove(kind);
            effectSession.ExtendedDraft=effectSession.ExtendedDraft with {EffectMasks=links};DraftChanged();
        };
        // Let the popup finish selecting/closing before changing its item containers.
        effectMaskChoice.DropDownClosed+=(_,_)=>DispatcherQueue.TryEnqueue(()=> {if(maskReferencePending)RefreshMaskReference();});
    }
    private void RefreshMaskReference()
    {
        if(effectMaskChoice is null)return;
        bool supported=selectedEffectKind is { } kind&&kind is not (EffectKind.MaskLibrary or EffectKind.AudioReactive or EffectKind.HideTaskbar);
        effectMaskRow.Visibility=supported?Visibility.Visible:Visibility.Collapsed;
        if(effectMaskChoice.IsDropDownOpen) {maskReferencePending=true;return;}
        maskReferencePending=false;
        bool wasUpdating=updatingMaskUi;updatingMaskUi=true;
        try {
            SyncMaskChoices(effectMaskChoice,true);
            string? current=selectedEffectKind is { } k&&effectSession.ExtendedDraft.EffectMasks.TryGetValue(k,out var id)?id:null;
            int index=current is null?0:Array.FindIndex(effectSession.ExtendedDraft.Masks,m=>m.Id==current)+1;
            if(effectMaskChoice.SelectedIndex!=index)effectMaskChoice.SelectedIndex=index;
        }finally {updatingMaskUi=wasUpdating;}
    }
    private void SyncMaskChoices(ComboBox picker,bool includeNone)
    {
        var choices=effectSession.ExtendedDraft.Masks.Select(m=>(Id:m.Id,Name:m.Name)).ToList();
        if(includeNone)choices.Insert(0,("",UiLanguage.Text("无遮罩 · 全画面")));
        bool sameIds=picker.Items.Count==choices.Count&&picker.Items.OfType<ComboBoxItem>().Select(i=>i.Tag as string).SequenceEqual(choices.Select(c=>c.Id));
        if(!sameIds) {
            picker.IsDropDownOpen=false;
            picker.Items.Clear();
            foreach(var choice in choices)picker.Items.Add(new ComboBoxItem {Content=choice.Name,Tag=choice.Id});
        } else {
            for(int i=0;i<choices.Count;i++)if(picker.Items[i] is ComboBoxItem item&&(string?)item.Content!=choices[i].Name)item.Content=choices[i].Name;
        }
    }
    private void RefreshMaskLibrary()
    {
        if(maskList is null)return;bool wasUpdating=updatingMaskUi;updatingMaskUi=true;
        try {
            if(!effectSession.ExtendedDraft.Masks.Any(m=>m.Id==selectedMaskId))selectedMaskId=effectSession.ExtendedDraft.Masks.FirstOrDefault()?.Id;
            SyncMaskChoices(maskList,false);
            int index=Array.FindIndex(effectSession.ExtendedDraft.Masks,m=>m.Id==selectedMaskId);
            if(maskList.SelectedIndex!=index)maskList.SelectedIndex=index;
            BuildMaskEditor();
        }finally {updatingMaskUi=wasUpdating;}
        RefreshMaskReference();
    }
    private EffectMask? CurrentMask()=>effectSession.ExtendedDraft.Masks.FirstOrDefault(m=>m.Id==selectedMaskId);
    private void ChangeMask(string id,Func<EffectMask,EffectMask> change)
    {
        if(loadingEffects||updatingMaskUi||effectSession.ExtendedDraft.Masks.FirstOrDefault(m=>m.Id==id) is not { } current)return;
        var changed=change(current);effectSession.ExtendedDraft=effectSession.ExtendedDraft with {Masks=effectSession.ExtendedDraft.Masks.Select(m=>m.Id==current.Id?changed:m).ToArray()};DraftChanged();
    }
    private void BuildMaskEditor()
    {
        if(maskEditor is null)return;maskEditor.Children.Clear();maskStatus.Text="";
        if(CurrentMask() is not { } current) {maskEditor.Children.Add(UtilityText(UiLanguage.Text("暂无遮罩")));return;}
        var name=new TextBox {Style=HomeStyle("UtilityTextBox"),Text=current.Name,MaxLength=40};maskEditor.Children.Add(WidgetField(UiLanguage.Text("名称"),name));
        DoubleClickEdit.OnAccepted(name,()=> {
            if(loadingEffects||updatingMaskUi||effectSession.ExtendedDraft.Masks.FirstOrDefault(m=>m.Id==current.Id) is not { } saved)return;
            string value=name.Text.Trim();if(value==saved.Name)return;
            if(value.Length==0||effectSession.ExtendedDraft.Masks.Any(m=>m.Id!=current.Id&&string.Equals(m.Name,value,StringComparison.OrdinalIgnoreCase))) {maskStatus.Text=UiLanguage.Text("名称不能为空或重复。");name.Text=saved.Name;return;}
            effectSession.ExtendedDraft=effectSession.ExtendedDraft with {Masks=effectSession.ExtendedDraft.Masks.Select(m=>m.Id==current.Id?m with {Name=value}:m).ToArray()};
            foreach(var item in maskList.Items.OfType<ComboBoxItem>())if((string?)item.Tag==current.Id)item.Content=value;
            maskStatus.Text="";RefreshMaskReference();DraftChanged();
        });
        // Shape changes preserve the selector and focused controls; only visibility changes.
        var shapeGrid=new Grid {ColumnSpacing=8,RowSpacing=8};
        for(int i=0;i<3;i++) {shapeGrid.ColumnDefinitions.Add(new());shapeGrid.RowDefinitions.Add(new() {Height=GridLength.Auto});}
        var shapeButtons=new List<Button>();var rows=new List<(FrameworkElement Row,Func<int,bool> Show)>();
        void RefreshShape(int shape) {
            for(int i=0;i<shapeButtons.Count;i++) {var button=shapeButtons[i];bool selected=i==shape;ChoiceSelection.SetIsSelected(button,selected);Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(button,selected?UiLanguage.Text("已选中"):UiLanguage.Text("未选中"));}
            foreach(var row in rows)row.Row.Visibility=row.Show(shape)?Visibility.Visible:Visibility.Collapsed;
        }
        string[] shapes=[UiLanguage.Text("矩形"),UiLanguage.Text("椭圆"),UiLanguage.Text("圆角矩形"),UiLanguage.Text("菱形"),UiLanguage.Text("星形"),UiLanguage.Text("黑白图片"),UiLanguage.Text("流动波浪"),UiLanguage.Text("流动圆形"),UiLanguage.Text("流动条带")];
        for(int i=0;i<shapes.Length;i++) {int shape=i;var button=WidgetButton(shapes[i],()=> {ChangeMask(current.Id,m=>m with {Shape=shape});RefreshShape(shape);});Grid.SetColumn(button,i%3);Grid.SetRow(button,i/3);shapeGrid.Children.Add(button);shapeButtons.Add(button);}
        maskEditor.Children.Add(WidgetField(UiLanguage.Text("形状"),shapeGrid));
        void Number(string label,double min,double max,double step,string unit,Func<EffectMask,double> get,Func<EffectMask,double,EffectMask> set,Func<int,bool>? show=null) {
            var slider=new Slider {Style=HomeStyle("HomeSlider"),Minimum=min,Maximum=max,StepFrequency=step,Value=get(current)};
            slider.ValueChanged+=(_,_)=>ChangeMask(current.Id,m=>set(m,slider.Value));var row=NumericSliderRow(label,slider,unit,step>=1?0:2);maskEditor.Children.Add(row);rows.Add((row,show??(_=>true)));
        }
        Number(UiLanguage.Text("中心横坐标"),0,100,1,"%",m=>m.X*100,(m,v)=>m with {X=v/100},s=>s<5);Number(UiLanguage.Text("中心纵坐标"),0,100,1,"%",m=>m.Y*100,(m,v)=>m with {Y=v/100},s=>s<5);
        Number(UiLanguage.Text("宽度"),1,200,1,"%",m=>m.Width*100,(m,v)=>m with {Width=v/100},s=>s<5);Number(UiLanguage.Text("高度"),1,200,1,"%",m=>m.Height*100,(m,v)=>m with {Height=v/100},s=>s<5);
        Number(UiLanguage.Text("圆角"),0,100,1,"%",m=>m.Corner*100,(m,v)=>m with {Corner=v/100},s=>s==2);
        Number(UiLanguage.Text("旋转角度"),-180,180,1,"°",m=>m.Rotation,(m,v)=>m with {Rotation=v},s=>s!=5);
        Number(UiLanguage.Text("羽化"),0,100,1,"%",m=>m.Feather*100,(m,v)=>m with {Feather=v/100});
        Number(UiLanguage.Text("速度"),-2,2,.01,UiLanguage.Text("周期/秒"),m=>m.Speed,(m,v)=>m with {Speed=v},s=>s>=6);Number(UiLanguage.Text("覆盖范围"),0,100,1,"%",m=>m.Coverage*100,(m,v)=>m with {Coverage=v/100},s=>s>=6);Number(UiLanguage.Text("图案缩放"),.5,8,.1,"",m=>m.Scale,(m,v)=>m with {Scale=v},s=>s>=6);
        var invert=WidgetToggle(UiLanguage.Text("反转黑白"),current.Invert);invert.Toggled+=(_,_)=>ChangeMask(current.Id,m=>m with {Invert=invert.IsOn});maskEditor.Children.Add(WidgetToggleRow(UiLanguage.Text("反转黑白"),invert));
        var imageSection=new StackPanel {Spacing=10};imageSection.Children.Add(UtilityText(current.Image.Length==0?UiLanguage.Text("未上传图片 · 全黑遮罩"):UiLanguage.Text("图片已加载 · 拉伸铺满")));
        imageSection.Children.Add(WidgetButton(UiLanguage.Text("上传黑白图片…"),()=>ImportMaskImage(current.Id)));maskEditor.Children.Add(imageSection);rows.Add((imageSection,s=>s==5));RefreshShape(current.Shape);
        maskEditor.Children.Add(WidgetButton(UiLanguage.Text("删除此遮罩"),()=> {effectSession.ExtendedDraft=effectSession.ExtendedDraft with {Masks=effectSession.ExtendedDraft.Masks.Where(m=>m.Id!=current.Id).ToArray(),EffectMasks=effectSession.ExtendedDraft.EffectMasks.Where(p=>p.Value!=current.Id).ToDictionary(p=>p.Key,p=>p.Value)};RefreshMaskLibrary();DraftChanged();}));
    }
    private async void ImportMaskImage(string id)
    {
        try {
            using var picker=new System.Windows.Forms.OpenFileDialog {Filter=UiLanguage.Text("图片|*.png;*.jpg;*.jpeg;*.bmp"),Multiselect=false};if(picker.ShowDialog()!=System.Windows.Forms.DialogResult.OK)return;
            string draft=effectSession.Key??"";var data=await Task.Run(()=> {
                var file=new FileInfo(picker.FileName);if(file.Length>32*1024*1024)throw new ArgumentException(UiLanguage.Text("图片最多 32 MiB。"));
                using var source=System.Drawing.Image.FromFile(file.FullName);if((long)source.Width*source.Height>16_000_000)throw new ArgumentException(UiLanguage.Text("图片最多 1600 万像素。"));
                using var bitmap=new System.Drawing.Bitmap(MaskPixels.Size,MaskPixels.Size,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using(var graphics=System.Drawing.Graphics.FromImage(bitmap)) {graphics.Clear(System.Drawing.Color.Black);graphics.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;graphics.DrawImage(source,new System.Drawing.Rectangle(0,0,bitmap.Width,bitmap.Height));}
                var locked=bitmap.LockBits(new(0,0,bitmap.Width,bitmap.Height),System.Drawing.Imaging.ImageLockMode.ReadOnly,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                try {var pixels=new byte[MaskPixels.Bytes];var row=new byte[bitmap.Width*4];for(int y=0;y<bitmap.Height;y++) {Marshal.Copy(locked.Scan0+y*locked.Stride,row,0,row.Length);for(int x=0;x<bitmap.Width;x++)pixels[y*bitmap.Width+x]=(byte)((row[x*4+2]*54+row[x*4+1]*183+row[x*4]*19)/256);}return MaskPixels.Encode(pixels);}finally {bitmap.UnlockBits(locked);}
            });
            if((effectSession.Key??"")!=draft||!effectSession.ExtendedDraft.Masks.Any(m=>m.Id==id))return;
            effectSession.ExtendedDraft=effectSession.ExtendedDraft with {Masks=effectSession.ExtendedDraft.Masks.Select(m=>m.Id==id?m with {Image=data}:m).ToArray()};BuildMaskEditor();DraftChanged();
        }catch(Exception ex) {maskStatus.Text=UiLanguage.Text("读取遮罩图片失败：")+ex.Message;AppLog.Error("MaskImport","Cannot read mask image",ex);}
    }
}
