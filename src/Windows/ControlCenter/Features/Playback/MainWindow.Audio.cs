using LumaWall.Contracts;
namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private void WallpaperSound_Click(object sender,Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if(SelectedScreen is not { } screen)return;
        bool enabled=!(wallpapers?.VideoSoundEnabled(screen.DeviceName)??modules.Settings.Get("videoSound:"+screen.DeviceName)!="0");
        if(wallpapers!=null)wallpapers.SetVideoSound(screen.DeviceName,enabled);
        else modules.Settings.Set("videoSound:"+screen.DeviceName,enabled?"1":"0");
        RefreshStatus();
    }
    private NativePlayback? audioFeedbackPlayer;
    private DateTime lastAudioSignal=DateTime.UtcNow;

    private void RefreshAudioStatus()
    {
        if(!effectsReady)return;
        if(navigationPage!="display") {
            if(audioFeedbackPlayer!=null)audioFeedbackPlayer.AudioLevelsChanged-=RefreshAudioStatus;
            audioFeedbackPlayer=null;
            return;
        }
        var current=SelectedScreen==null?null:wallpapers?.Running.GetValueOrDefault(SelectedScreen.DeviceName);
        var player=effectTargetAssetId==null||current?.Asset.Id==effectTargetAssetId?current?.Player:null;
        if(!ReferenceEquals(player,audioFeedbackPlayer)) {
            if(audioFeedbackPlayer!=null)audioFeedbackPlayer.AudioLevelsChanged-=RefreshAudioStatus;
            audioFeedbackPlayer=player;lastAudioSignal=DateTime.UtcNow;
            if(player!=null)player.AudioLevelsChanged+=RefreshAudioStatus;
        }
        bool receiving=AudioReactiveSwitch.IsOn&&player is {HasAudioLevels:true,AudioActive:true}
            &&player.AudioStatusCode is "system-listening" or "video-listening";
        UpdateAudioRuleMeters(player,receiving);
        if(!receiving) {
            AudioInputMeter.Value=AudioResponseMeter.Value=0;AudioBeatIndicator.Opacity=.2;
            AudioInputLabel.Text=UiLanguage.Text("声音输入 · 等待");AudioResponseLabel.Text=UiLanguage.Text("节奏响应 · 0%");
        }
        AudioBeatLabel.Text=UiLanguage.Format($"已触发 {player?.AudioBeats??0} 次");
        AudioFeedbackText.Visibility=AudioReactiveSwitch.IsOn?Microsoft.UI.Xaml.Visibility.Visible:Microsoft.UI.Xaml.Visibility.Collapsed;
        if(!AudioReactiveSwitch.IsOn) {
            AudioStatusText.Text=UiLanguage.Text("未开启");
            AudioFeedbackText.Text="";return;
        }
        if(effectSession.Collection.Strength(LumaWall.Contracts.EffectKind.AudioReactive)==0) {
            AudioStatusText.Text=UiLanguage.Text("强度 · 0%");AudioFeedbackText.Text=UiLanguage.Text("已暂停");return;
        }
        if(player==null) {
            AudioStatusText.Text=UiLanguage.Text("未播放");
            AudioFeedbackText.Text=UiLanguage.Text("已保存");return;
        }
        AudioStatusText.Text=player.AudioStatusCode=="system-listening"?UiLanguage.Text("系统声音"):player.AudioStatusCode=="video-listening"?UiLanguage.Text("视频音轨"):UiLanguage.Text("未就绪");
        if(player.AudioStatusCode is not ("system-listening" or "video-listening")) {
            AudioFeedbackText.Text=player.AudioStatusCode switch {
                "no-targets"=>UiLanguage.Text("无启用的联动参数"),
                "paused"=>UiLanguage.Text("已暂停"),
                "system-unavailable"=>UiLanguage.Text("系统声音不可用"),
                "video-unavailable"=>UiLanguage.Text("无可用音轨"),
                _=>UiLanguage.Text("正在同步")
            };return;
        }
        if(!player.HasAudioLevels||!player.AudioActive) {AudioFeedbackText.Text=UiLanguage.Text("正在准备");return;}
        double input=player.AudioInput;
        double decibels=input>0?20*Math.Log10(input):-120;
        // A logarithmic input meter keeps ordinary low-volume music visible.
        AudioInputMeter.Value=Math.Clamp((decibels+60)/60*100,0,100);
        AudioInputLabel.Text=input<.0001?UiLanguage.Text("声音输入 · 静音 / 很弱"):UiLanguage.Format($"声音输入 · {decibels:0} dBFS");
        double response=Math.Clamp(.25*player.AudioLevel+.75*player.AudioPulse,0,1)*AudioAmountSlider.Value*effectSession.Collection.Strength(LumaWall.Contracts.EffectKind.AudioReactive);
        AudioResponseMeter.Value=response;
        AudioResponseLabel.Text=UiLanguage.Format($"节奏响应 · {response:0}%");
        AudioBeatIndicator.Opacity=.2+.8*player.AudioPulse;
        if(input>=.00015)lastAudioSignal=DateTime.UtcNow;
        if((DateTime.UtcNow-lastAudioSignal).TotalSeconds>1.5) {
            AudioFeedbackText.Text=player.AudioStatusCode=="system-listening"
                ?UiLanguage.Text("静音")
                :UiLanguage.Text("静音");
        } else if(player.AudioLevel<.015&&input>=.00015) {
            AudioFeedbackText.Text=UiLanguage.Text("已收到声音 · 响应较弱");
        } else {
            AudioFeedbackText.Text=UiLanguage.Text("正在响应");
        }
    }
}
