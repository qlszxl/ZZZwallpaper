using System.Text.Json;
using LumaWall.Contracts;
using LumaWall.Contracts.Sharing;

namespace LumaWall.Application;

// Commits configuration only. UI and MCP activate hosts after a successful commit;
// a host failure must not roll back saved settings or erase unrelated history.
public sealed class ConfigurationImportService(IConfigurationStore store)
{
    public sealed record Backup(int Version,WidgetConfiguration Widgets,string? Screen,string? Asset,WallpaperProfile? Profile);
    public sealed record Imported(WidgetConfiguration Widgets,WallpaperProfile? Profile);
    public Imported Apply(SharedConfiguration configuration,string screen,string? expectedRevision=null)
    {
        var widgets=configuration.OnScreen(screen);
        var baseline=store.ScreenProfile(screen)??new(FillMode.Cover,VideoEffects.Original);
        var profile=configuration.HasEffects?baseline with {Effects=configuration.Effects}:null;
        Apply(widgets,screen,profile,expectedRevision);return new(widgets,profile);
    }
    public void Apply(WidgetConfiguration widgets,string screen,WallpaperProfile? profile,string? expectedRevision=null)
    {
        RequireRevision(expectedRevision);
        var backup=new Backup(1,WidgetConfiguration.FromJson(store.GetSetting("widgets.v1")),screen,null,
            profile==null?null:store.ScreenProfile(screen)??new(FillMode.Cover,VideoEffects.Original));
        store.SaveSharedConfiguration(widgets,screen,null,profile,JsonSerializer.Serialize(backup));
    }
    public Backup? ReadBackup()
    {
        var json=store.GetSetting("sharing.undo.v1");if(json==null)return null;
        var backup=JsonSerializer.Deserialize<Backup>(json)??throw new IOException("导入备份不可读取。");
        if(backup.Version!=1)throw new IOException("备份格式需要更新的软件版本。");
        backup.Widgets.Validate();
        if(backup.Profile is { } profile) {
            // A current snapshot is restored exactly, including an implicit
            // collection. Migration materializes legacy membership only for an
            // older format; read its original JSON so retired fields survive.
            if(profile.Effects.Version==VideoEffects.Original.Version)profile.Effects.Validate();
            else {
                using var document=JsonDocument.Parse(json);
                var effects=VideoEffects.FromJson(document.RootElement.GetProperty(nameof(Backup.Profile)).GetProperty(nameof(WallpaperProfile.Effects)).GetRawText());
                backup=backup with {Profile=profile with {Effects=effects}};
            }
        }
        return backup;
    }
    public Backup Restore(string? expectedRevision=null)
    {
        RequireRevision(expectedRevision);
        var backup=ReadBackup()??throw new InvalidOperationException("没有可恢复的导入备份。");
        store.SaveSharedConfiguration(backup.Widgets,backup.Screen,backup.Asset,backup.Profile,null);return backup;
    }
    private void RequireRevision(string? expected)
    {
        if(expected!=null&&store.ConfigurationRevision() is var actual&&expected!=actual)
            throw new ConfigurationConflictException(expected,actual,"app_get_state");
    }
}
