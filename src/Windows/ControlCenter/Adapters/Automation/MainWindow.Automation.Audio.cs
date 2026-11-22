using System.Text.Json;
using LumaWall.Automation;
using Windows.Devices.Enumeration;
using Windows.Media.Core;
using Windows.Media.Devices;
using Windows.Media.Playback;
using Windows.Storage;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private MediaPlayer? automationTestAudio;
    private string? automationTestAudioError,automationTestDevice;
    private static string? CaptureEndpointId(string id) {
        var parts=id.Split('#');return parts.Length>=3&&parts[2].StartsWith("{0.0.0.",StringComparison.OrdinalIgnoreCase)?parts[2]:null;
    }
    private void StopAutomationTestAudio() {automationTestAudio?.Pause();automationTestAudio?.Dispose();automationTestAudio=null;automationTestAudioError=null;automationTestDevice=null;}
    private async Task<OperationReply> AutomationAudio(OperationRequest request,CancellationToken token)
    {
        var a=request.Arguments;
        if(request.Name=="audio_stop_test") {StopAutomationTestAudio();return OperationReply.Success(new {stopped=true});}
        if(request.Name=="audio_get_test_state")return OperationReply.Success(new {state=automationTestAudio?.PlaybackSession.PlaybackState.ToString()??"Stopped",positionSeconds=automationTestAudio?.PlaybackSession.Position.TotalSeconds,durationSeconds=automationTestAudio?.PlaybackSession.NaturalDuration.TotalSeconds,deviceId=automationTestDevice,error=automationTestAudioError});
        var devices=await DeviceInformation.FindAllAsync(MediaDevice.GetAudioRenderSelector());token.ThrowIfCancellationRequested();
        if(request.Name=="audio_list_devices")return OperationReply.Success(new {defaultDeviceId=MediaDevice.GetDefaultAudioRenderId(AudioDeviceRole.Default),items=devices.Select(d=>new {deviceId=d.Id,name=d.Name,enabled=d.IsEnabled,captureSupported=CaptureEndpointId(d.Id)!=null,captureEndpointId=CaptureEndpointId(d.Id)})});
        string id=Optional(a,"deviceId")??"default";var device=id=="default"?null:devices.FirstOrDefault(d=>d.Id==id&&d.IsEnabled)??throw new OperationFault("resource_not_found","设备不存在或不可用",null);
        if(request.Name=="audio_set_capture_device") {
            RequireAutomationRevision(a);
            string screen=OperationScreen(Arg(a,"screenId")).DeviceName;string endpoint=device==null?"default":CaptureEndpointId(device.Id)??throw new OperationFault("invalid_argument","此设备不支持回环采集",null);
            modules.Settings.Set("audioCaptureDevice:"+screen,endpoint);wallpapers!.Running.GetValueOrDefault(screen)?.Player.SetAudioCaptureDevice(endpoint);
            return OperationReply.Success(new {saved=true,screenId=screen,requestedDevice=id,requestedEndpoint=endpoint,status="waiting_for_device",systemDefaultChanged=false});
        }
        if(request.Name!="audio_play_test")throw new OperationFault("invalid_argument","不支持的操作",null);
        string[] roots=JsonSerializer.Deserialize<string[]>(modules.Settings.Get("automation.mediaRoots")??"[]")??[];
        string path=MediaAccessPolicy.ValidatePath(Arg(a,"path"),roots,[AppContext.BaseDirectory,store.Root]);
        if(!new[]{".wav",".mp3",".flac",".m4a",".ogg",".aac"}.Contains(Path.GetExtension(path),StringComparer.OrdinalIgnoreCase))throw new OperationFault("invalid_argument","不支持的测试音频格式",null);
        var file=await StorageFile.GetFileFromPathAsync(path);token.ThrowIfCancellationRequested();
        var player=new MediaPlayer {AutoPlay=false,IsLoopingEnabled=Flag(a,"loop"),Volume=.5,AudioDevice=device};
        player.MediaFailed+=(_,error)=>DispatcherQueue.TryEnqueue(()=> {if(automationTestAudio==player)automationTestAudioError=error.ErrorMessage;});
        try {player.Source=MediaSource.CreateFromStorageFile(file);StopAutomationTestAudio();automationTestAudio=player;automationTestDevice=id;player.Play();}
        catch {player.Dispose();if(automationTestAudio==player)automationTestAudio=null;throw;}
        return OperationReply.Success(new {status="opening",deviceId=id,loop=player.IsLoopingEnabled,volume=player.Volume});
    }
}
