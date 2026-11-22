using LumaWall.Contracts;
using System.Text;
using Microsoft.UI.Xaml.Controls;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private const string AudioAutoAdaptKey="audioAutoAdapt";
    private ToggleSwitch? audioAutoAdaptSwitch;
    private bool syncingAudioProcessing;
    private string AudioProcessingPath=>Path.Combine(store.Root,"audio-processing.v1");

    private void PublishAudioProcessing(bool enabled)
    {
        string file=AudioProcessingPath,temporary=file+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {
            File.WriteAllText(temporary,enabled?"1":"0",Encoding.ASCII);
            File.Move(temporary,file,true);
        } finally {if(File.Exists(temporary))File.Delete(temporary);}
    }
    private void InitializeAudioProcessingSettings()
    {
        Environment.SetEnvironmentVariable("ZZZWALLPAPER_AUDIO_CONFIG",AudioProcessingPath);
        bool enabled=modules.Settings.Get(AudioAutoAdaptKey)!="false";
        try {
            PublishAudioProcessing(enabled);
            if(modules.Settings.Get(AudioAutoAdaptKey)==null)modules.Settings.Set(AudioAutoAdaptKey,enabled?"true":"false");
        } catch(Exception ex) {ShowError(UiLanguage.Text("音频处理设置未同步：")+ex.Message,ex);}
        var panel=new StackPanel {Spacing=12};panel.Children.Add(UtilityText(UiLanguage.Text("音频处理"),"UtilityHeading"));
        audioAutoAdaptSwitch=WidgetToggle(UiLanguage.Text("自动适应"),enabled);
        panel.Children.Add(WidgetToggleRow(UiLanguage.Text("自动适应"),audioAutoAdaptSwitch));SettingsAudioProcessingHost.Content=panel;
        audioAutoAdaptSwitch.Toggled+=(_,_)=> {
            if(syncingAudioProcessing)return;
            bool next=audioAutoAdaptSwitch.IsOn,previous=modules.Settings.Get(AudioAutoAdaptKey)!="false";
            if(next==previous)return;
            try {
                SetAudioAutoAdapt(next);
                OperationTrace.Record(OperationTrace.Current,"audio_processing","saved",next?"adaptive_enabled":"adaptive_disabled");
                AppLog.Info("AudioProcessing",$"Global automatic adaptation: {next}");
            } catch(Exception ex) {
                try {PublishAudioProcessing(previous);}catch(Exception rollback) {AppLog.Warning("AudioProcessing","Could not restore processing setting",rollback);}
                syncingAudioProcessing=true;audioAutoAdaptSwitch.IsOn=previous;syncingAudioProcessing=false;
                ShowError(UiLanguage.Text("音频处理设置未保存：")+ex.Message,ex);
            }
        };
    }
    private void SetAudioAutoAdapt(bool enabled)
    {
        bool previous=modules.Settings.Get(AudioAutoAdaptKey)!="false";
        try {PublishAudioProcessing(enabled);modules.Settings.Set(AudioAutoAdaptKey,enabled?"true":"false");}
        catch {PublishAudioProcessing(previous);throw;}
        syncingAudioProcessing=true;
        try {if(audioAutoAdaptSwitch!=null)audioAutoAdaptSwitch.IsOn=enabled;}
        finally {syncingAudioProcessing=false;}
    }
    private void SyncAudioProcessingSettings()
    {
        bool enabled=modules.Settings.Get(AudioAutoAdaptKey)!="false";
        PublishAudioProcessing(enabled);
        syncingAudioProcessing=true;
        try {if(audioAutoAdaptSwitch!=null)audioAutoAdaptSwitch.IsOn=enabled;}
        finally {syncingAudioProcessing=false;}
    }
}
