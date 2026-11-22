using System.Text.Json;
using LumaWall.Automation;
using LumaWall.Contracts;
using LumaWall.ControlCenter;

internal static class ExtendedChecks
{
    public static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        JsonElement J(string value)=>JsonDocument.Parse(value).RootElement.Clone();
        foreach(var name in new[]{"wallpaper_cancel","media_remove","wallpaper_edit_queue","history_create_checkpoint","ui_capture_desktop","ui_get_tree","task_get_operation","task_get_job","task_cancel_job"})check(ToolCatalog.All.Any(t=>t.Name==name),"Required automation surface: "+name);
        reject(()=>ToolCatalog.Validate("app_update_settings",J("{\"expectedRevision\":\"x\",\"automation.mcp.enabled\":true}")),"Settings cannot grant MCP access");
        reject(()=>ToolCatalog.Validate("app_restart",J("{\"expectedRevision\":\"x\",\"executable\":\"cmd.exe\"}")),"Restart cannot select executable");
        reject(()=>ToolCatalog.Validate("ui_capture_desktop",J("{\"screenId\":\"x\",\"outputPath\":\"C:/app.dll\"}")),"Capture cannot overwrite arbitrary paths");
        reject(()=>ToolCatalog.Validate("ui_perform_action",J("{\"controlId\":\"x\",\"treeRevision\":\"x\",\"action\":\"key\",\"key\":\"Win+R\"}")),"UI cannot invoke shell hotkey");
        var assets=new HashSet<string>{"a","b","c"};var folders=new HashSet<string>{"f"};var initial=new PlaybackSettings {AssetIds=["a","b"],Mode=PlaybackMode.Shuffle};
        var reordered=QueueEdit.Apply(initial,J("{\"action\":\"reorder\",\"assetIds\":[\"b\",\"a\"]}"),assets,folders);
        check(reordered.AssetIds.SequenceEqual(new[]{"b","a"})&&reordered.Mode==PlaybackMode.Shuffle&&initial.AssetIds[0]=="a","Queue reorder preserves options and original snapshot");
        reject(()=>QueueEdit.Apply(initial,J("{\"action\":\"reorder\",\"assetIds\":[\"b\"]}"),assets,folders),"Reorder rejects omission");
        reject(()=>QueueEdit.Apply(initial,J("{\"action\":\"add\",\"assetIds\":[\"missing\"]}"),assets,folders),"Queue rejects unknown asset");
        var folder=QueueEdit.Apply(initial,J("{\"action\":\"folder\",\"folderId\":\"f\"}"),assets,folders);
        check(folder.FolderId=="f"&&folder.AssetIds.SequenceEqual(initial.AssetIds)&&folder.CustomPlaylists!.Single().AssetIds.SequenceEqual(initial.AssetIds),"Switching to a folder preserves the dormant custom playlist for a later return");
        reject(()=>QueueEdit.Apply(folder,J("{\"action\":\"add\",\"assetIds\":[\"a\"]}"),assets,folders),"Folder source rejects implicit list mutation");
        var journal=new OperationJournal();var request=new OperationRequest("widget_add",J("{\"kind\":\"Text\",\"idempotencyKey\":\"retry\"}"));
        foreach(var name in new[]{"preset_save","config_commit_import"})
            check(journal.Begin(new(name,J("{}")),out _).StartsWith("PRE-"),"Preset/share operation uses preset page code: "+name);
        check(journal.Begin(new("ui_navigate",J("{\"page\":\"library\"}")),out _).StartsWith("LIB-"),"Navigation operation uses destination page code");
        string id=journal.Begin(request,out var replay);check(replay==null,"New operation is reserved");
        journal.Complete(id,OperationReply.Success(new {saved=true,widgetId="created"}),"revision");
        string same=journal.Begin(new("widget_add",J("{\"idempotencyKey\":\"retry\",\"kind\":\"Text\"}")),out replay);
        check(id==same&&replay!.Data.GetProperty("widgetId").GetString()=="created","Retry returns identical result despite JSON field ordering");
        reject(()=>journal.Begin(new("widget_remove",request.Arguments),out _),"Retry key rejects a different operation");
        string jobId=journal.Begin(new("media_import_batch",J("{}")),out _);
        journal.Complete(jobId,OperationReply.Success(new {jobId="job",status="running"}),"r0");
        check(journal.Read(jobId).GetProperty("finishedAt").ValueKind==JsonValueKind.Null,"Running job does not claim a completion timestamp");
        journal.Complete(jobId,OperationReply.Success(new {jobId="job",status="cancelled",saved=true}),"r1");
        check(journal.Read(jobId).GetProperty("reply").GetProperty("savedRevision").GetString()=="r1","Cancelled job retains and identifies committed changes");
        reject(()=>ToolCatalog.Validate("ui_perform_action",J("{\"controlId\":\"x\",\"treeRevision\":\"x\",\"action\":\"key\",\"key\":\"Tab\"}")),"UI focus cannot escape the authorized control through Tab");
        ToolCatalog.Validate("config_preview_import",J("{\"screenId\":\"x\",\"imageAssetId\":\"qr\"}"));
        check(true,"Share preview supports an imported QR image without requiring text");
        string root=Path.Combine(Path.GetTempPath(),"zzzwallpaper-automation-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try {
            string source=Path.Combine(root,"original.png");File.WriteAllText(source,"original file remains");
            check(MediaAccessPolicy.ValidatePath(source,[root],[])==source,"Authorized media path accepted");
            void Denied(Action action,string name) {try {action();}catch(UnauthorizedAccessException) {check(true,name);return;}throw new Exception("FAIL: "+name);}
            Denied(()=>MediaAccessPolicy.ValidatePath(source,[],[]),"Ungrantable media path rejected");
            Denied(()=>MediaAccessPolicy.ValidatePath(source,[root],[root]),"Protected root overrides a grant");
            Denied(()=>MediaAccessPolicy.ValidatePath(source,[root+"-other"],[]),"Root authorization uses path boundaries");
            using var store=new LibraryStore(Path.Combine(root,"data"));store.HistoryEnabled=true;
            store.Upsert(new("a",source,"asset",1,0,1,1,"png","SDR","image"));store.SavePlayback("screen",initial);store.SetSetting(ToolCatalog.EnabledSetting,"false");
            var snapshot=store.CaptureConfiguration();var before=store.ConfigurationRevision();
            store.SetSetting(ToolCatalog.EnabledSetting,"true");check(store.ConfigurationRevision()==before,"Security settings excluded from configuration revision");
            using(store.BeginUndoBatch("batch")) {store.Remove("a");store.SavePlayback("screen",initial with {AssetIds=["b"]});}
            store.ReplayUndo(false);check(store.Assets().Count==1&&store.Playback("screen").AssetIds.Contains("a"),"One undo restores media reference and queue together");
            store.Remove("a");store.RestoreConfiguration(snapshot);store.FlushConfiguration();
            check(store.Assets().Single().Id=="a"&&File.ReadAllText(source)=="original file remains","Checkpoint restores references without touching source bytes");
            check(store.GetSetting(ToolCatalog.EnabledSetting)=="true"&&store.UndoLabel==null,"Checkpoint preserves authorization and clears incompatible history");
            check(store.ConfigurationRevision()==before,"Checkpoint restores matching logical configuration revision");
            var mask=new EffectMask {Name="Unbound mask"};var profile=new WallpaperProfile(FillMode.Cover,VideoEffects.Original);
            store.SaveTargetEffects("screen","a",profile with {Effects=profile.Effects with {Masks=[mask]}},profile.Effects);
            check(store.EditingProfile("screen","a").Effects.Masks.Any(m=>m.Id==mask.Id),"Asset mask creation preserves a real reusable stored ID");
            var current=store.EditingProfile("screen","a");store.SaveTargetEffects("screen","a",current with {Effects=current.Effects with {Masks=[]}},current.Effects);
            check(!store.EditingProfile("screen","a").Effects.Masks.Any(m=>m.Id==mask.Id),"Removing an unbound mask removes the stored resource");
            string posterRevision=store.ConfigurationRevision();store.SetSetting("poster.choice.v1:a","{\"Mode\":\"manual\",\"Seconds\":2}");
            check(store.ConfigurationRevision()!=posterRevision,"Manual poster changes invalidate configuration revisions");
            var hidden=profile with {Fill=FillMode.Contain,Effects=profile.Effects with {Collection=profile.Effects.GetCollection() with {Added=EffectKind.HideTaskbar,HideTaskbarEnabled=true,HideTaskbar=1}}};
            store.SaveProfile("offline-display","",hidden);
            var read=store.ScreenProfile("offline-display")!;
            check(read.Fill==FillMode.Contain&&read.Effects.HidesTaskbar,"Screen options use saved profile including offline displays");
        } finally {Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(root,true);}
    }
}
