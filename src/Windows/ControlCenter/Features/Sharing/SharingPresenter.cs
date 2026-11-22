using System.Text;
using System.Text.Json;
using LumaWall.Application;
using LumaWall.Contracts;
using LumaWall.Contracts.Sharing;

namespace LumaWall.ControlCenter;

internal sealed class SharingPresenter(SettingsService settings,ConfigurationImportService imports,SharingActivationCoordinator activation)
{
    internal sealed record Export(string Text,SharingQrExport? Qr);
    internal sealed record Applied(WidgetConfiguration Widgets,WallpaperProfile? Profile,string Warning);
    public SharingState State { get; }=new();
    public void LoadPreferences()
    {
        try {State.Preferences=JsonSerializer.Deserialize<ShareSelection>(settings.Get("sharing.options.v1")??"null")??new();}
        catch(Exception ex) {AppLog.Warning("Sharing","Could not read sharing preferences",ex);State.Preferences=new();}
    }
    public bool Preference(string key)=>key switch {
        "layout"=>State.Preferences.Layout,"parameters"=>State.Preferences.Parameters,
        "visibility"=>State.Preferences.Visibility,"text"=>State.Preferences.Text,
        "targets"=>State.Preferences.Targets,"images"=>State.Preferences.Images,_=>false};
    public void SetPreference(string key,bool value)
    {
        var previous=State.Preferences;
        var next=key switch {"layout"=>previous with {Layout=value},"parameters"=>previous with {Parameters=value},
            "visibility"=>previous with {Visibility=value},"text"=>previous with {Text=value},
            "targets"=>previous with {Targets=value},"images"=>previous with {Images=value},_=>previous};
        settings.Set("sharing.options.v1",JsonSerializer.Serialize(next));State.Preferences=next;
    }
    public Task<Export> Generate(VideoEffects effects,WidgetConfiguration widgets,ShareSelection selection)=>Task.Run(()=> {
        byte[] packet=ShareCode.EncodePacket(ShareProjection.Create(effects,widgets,selection));
        return new Export(ShareCode.ToText(packet),packet.Length<=ShareQrPayload.MaxPacketBytes?SharingQrImage.Create(packet):null);
    });
    public Task<SharedConfiguration> Decode(string text)=>Task.Run(()=>ShareCode.Decode(text));
    public static async Task<string> ReadText(string path)
    {
        using var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(input.Length>ShareCode.MaxCodeLength*2L+3)throw new IOException(UiLanguage.Text("分享码文件过大。"));
        using var reader=new StreamReader(input,Encoding.UTF8,true);var text=new StringBuilder();char[] buffer=new char[8192];int count;
        while((count=await reader.ReadAsync(buffer))>0) {
            if(text.Length+count>ShareCode.MaxCodeLength*2)throw new IOException(UiLanguage.Text("分享码文件过大。"));text.Append(buffer,0,count);
        }
        return text.ToString();
    }
    public Applied Apply(SharedConfiguration configuration,string screen,string? expectedRevision)
    {
        var saved=imports.Apply(configuration,screen,expectedRevision);
        return new(saved.Widgets,saved.Profile,activation.Activate(saved.Widgets,screen,saved.Profile));
    }
    public ConfigurationImportService.Backup? ReadBackup()=>imports.ReadBackup();
    public string Restore(string? expectedRevision)
    {
        var saved=imports.Restore(expectedRevision);
        return activation.Activate(saved.Widgets,saved.Screen,saved.Profile);
    }
}
