using System.Text;
using System.Text.Json;
using LumaWall.Automation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    // The UI feed includes reads and rejected calls without consuming the operation
    // journal's mutation/idempotency retention. Both remain local to this instance.
    private sealed class AutomationActivity
    {
        public required string Tool;
        public required string Arguments;
        public required string Target;
        public bool ReadOnly;
        public DateTimeOffset StartedAt=DateTimeOffset.Now;
        public string Status="running";
        public string Result="等待执行结果";
        public string? OperationId;
    }
    private readonly List<AutomationActivity> automationActivities=[];
    private bool automationHistoryOpen;

    private AutomationActivity? BeginAutomationActivity(OperationRequest request)
    {
        try {
            var activity=new AutomationActivity {
                Tool=request.Name,Arguments=AutomationHistoryDetails(request.Arguments),Target=AutomationHistoryTarget(request.Arguments),
                ReadOnly=ToolCatalog.All.FirstOrDefault(t=>t.Name==request.Name)?.ReadOnly??false
            };
            if(automationActivities.Count>=256)automationActivities.RemoveAt(0);
            automationActivities.Add(activity);return activity;
        } catch(Exception ex) {AppLog.Warning("MCP","Cannot prepare activity display",ex);return null;}
    }
    private void CompleteAutomationActivity(AutomationActivity? activity,OperationReply reply,string? status=null)
    {
        if(activity==null)return;
        try {
            activity.Status=status??(activity.Tool=="task_cancel_job"&&!reply.IsError?"cancel_requested":null)
                ??(activity.ReadOnly?null:AutomationHistoryString(reply.Data,"status"))??(reply.IsError?"failed":"completed");
            activity.Result=AutomationHistoryDetails(reply.Data);
            // Cancelling is an acknowledged request; the original import entry owns the final task state.
            if(!activity.ReadOnly&&activity.Tool!="task_cancel_job")activity.OperationId=AutomationHistoryString(reply.Data,"operationId");
        } catch(Exception ex) {AppLog.Warning("MCP","Cannot update activity display",ex);}
    }
    private static string? AutomationHistoryString(JsonElement data,string key)=>
        data.ValueKind==JsonValueKind.Object&&data.TryGetProperty(key,out var value)&&value.ValueKind==JsonValueKind.String?value.GetString():null;

    private string AutomationHistoryTarget(JsonElement arguments)
    {
        var labels=new List<string>();
        foreach(string key in new[]{"screenId","assetId","widgetId","effect","kind","scope"}) {
            string? value=AutomationHistoryString(arguments,key);if(value==null)continue;
            string label=key switch {"screenId"=>"显示器","assetId"=>"素材","widgetId"=>"组件","effect"=>"效果","kind"=>"类型",_=>"范围"};
            if(key=="assetId")value=modules.Library.Assets().FirstOrDefault(a=>a.Id==value)?.Name??value;
            if(key=="scope")value=value switch {"screen"=>"显示器","asset"=>"素材","effective"=>"合成效果",_=>value};
            labels.Add(label+"："+value);
        }
        return string.Join(" · ",labels);
    }

    // Keep bounded text only: image/config payloads and large result lists must not
    // create another in-memory copy of binary data just to populate the history UI.
    private static string AutomationHistoryDetails(JsonElement data)
    {
        var text=new StringBuilder();int lines=0;
        void Append(JsonElement value,string path,int depth)
        {
            if(lines>=40)return;
            if(depth>=4&&(value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)) {text.AppendLine(path+"：…");lines++;return;}
            if(value.ValueKind==JsonValueKind.Object) {
                foreach(var item in value.EnumerateObject()) {
                    if(lines>=40)break;
                    if(item.Name is "idempotencyKey" or "expectedRevision" or "rendererRevisions" or "widgetRequestedRevision" or "widgetAppliedRevision" or "configurationRevision" or "savedRevision" or "instanceId")continue;
                    string child=string.IsNullOrEmpty(path)?item.Name:path+"."+item.Name;
                    if(item.Name.Contains("base64",StringComparison.OrdinalIgnoreCase)||item.Name is "imageData" or "shareText"||(item.Name=="code"&&string.IsNullOrEmpty(path))) {
                        text.AppendLine(child+"：已省略内容");lines++;continue;
                    }
                    Append(item.Value,child,depth+1);
                }
                return;
            }
            if(value.ValueKind==JsonValueKind.Array) {
                int index=0;
                foreach(var item in value.EnumerateArray()) {
                    if(index>=8||lines>=40)break;
                    Append(item,path+"["+index+++"]",depth+1);
                }
                if(value.GetArrayLength()>index&&lines<40) {text.AppendLine(path+"：共 "+value.GetArrayLength()+" 项，其余已省略");lines++;}
                return;
            }
            string content=value.ValueKind==JsonValueKind.String?value.GetString()??"":value.GetRawText();
            if(content.Length>240)content=content[..240]+"…（内容已截断）";
            text.AppendLine((string.IsNullOrEmpty(path)?"":path+"：")+content);lines++;
        }
        Append(data,"",0);
        if(lines>=40)text.AppendLine("更多内容已省略");
        return text.Length==0?"无":text.ToString().TrimEnd();
    }

    private async Task ShowAutomationHistory()
    {
        if(automationHistoryOpen)return;automationHistoryOpen=true;
        try {
            const int pageSize=20;int page=0;
            var body=new StackPanel {Spacing=16,Width=Math.Max(250,Math.Min(640,Root.ActualWidth-140))};
            body.Children.Add(UtilityText("最近 256 条 · 退出后清空"));
            var entries=new StackPanel {Spacing=12};
            var status=UtilityText("");
            var navigation=new Grid {ColumnSpacing=12};
            navigation.ColumnDefinitions.Add(new());navigation.ColumnDefinitions.Add(new());
            Button? previous=null,next=null;ScrollViewer? scroll=null;
            void Refresh(bool reset=false)
            {
                if(reset)page=0;
                var rows=automationActivities.AsEnumerable().Reverse().ToArray();
                int pageCount=Math.Max(1,(rows.Length+pageSize-1)/pageSize);page=Math.Clamp(page,0,pageCount-1);
                // Background jobs update the existing mutation journal when they finish.
                var journal=automationJournal.List().ToDictionary(item=>item.GetProperty("operationId").GetString()!);
                entries.Children.Clear();
                foreach(var activity in rows.Skip(page*pageSize).Take(pageSize)) {
                    if(activity.OperationId is { } id&&journal.TryGetValue(id,out var operation)) {
                        activity.Status=AutomationHistoryString(operation,"status")??activity.Status;
                        if(operation.TryGetProperty("reply",out var reply))activity.Result=AutomationHistoryDetails(reply);
                    }
                    var card=new StackPanel {Spacing=10};
                    card.Children.Add(AutomationHistoryText(AutomationHistoryTitle(activity.Tool),"UtilityLabel"));
                    card.Children.Add(AutomationHistoryText($"{activity.StartedAt:yyyy-MM-dd HH:mm:ss} · {AutomationHistoryStatus(activity.Status)}"));
                    if(activity.Target.Length>0)card.Children.Add(AutomationHistoryText(activity.Target));
                    var details=new StackPanel {Spacing=12};
                    details.Children.Add(AutomationHistoryText("MCP 工具："+activity.Tool));
                    details.Children.Add(AutomationHistoryText("调用参数\n"+activity.Arguments));
                    details.Children.Add(AutomationHistoryText("执行结果\n"+activity.Result));
                    card.Children.Add(new Expander {Style=HomeStyle("EffectsExpander"),Header=UtilityText("操作详情","UtilityLabel"),Content=details,HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch});
                    entries.Children.Add(new Border {Style=HomeStyle("LibraryInset"),Padding=new Thickness(16),Child=card});
                }
                if(rows.Length==0)entries.Children.Add(AutomationHistoryText("暂无 AI 操作记录"));
                status.Text=$"共 {rows.Length} 条 · 第 {page+1} / {pageCount} 页";
                previous!.IsEnabled=page>0;next!.IsEnabled=page+1<pageCount;
            }
            body.Children.Add(WidgetButton("刷新记录",()=>Refresh(true)));
            body.Children.Add(status);body.Children.Add(entries);
            previous=WidgetButton("上一页",()=> {page--;Refresh();scroll?.ChangeView(null,0,null,true);});next=WidgetButton("下一页",()=> {page++;Refresh();scroll?.ChangeView(null,0,null,true);});
            navigation.Children.Add(previous);Grid.SetColumn(next,1);navigation.Children.Add(next);body.Children.Add(navigation);
            Refresh();
            scroll=new ScrollViewer {Style=HomeStyle("HomeScrollViewer"),Content=body,MaxHeight=Math.Max(220,Root.ActualHeight-220),VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
            var dialog=new ContentDialog {XamlRoot=Root.XamlRoot,Title="AI 操作记录",Content=scroll,CloseButtonText="关闭",DefaultButton=ContentDialogButton.Close,Style=HomeStyle("LibraryDialog")};
            dialog.Resources["ContentDialogMaxWidth"]=780d;
            await dialog.ShowAsync();
        } finally {automationHistoryOpen=false;}
    }
    private static TextBlock AutomationHistoryText(string text,string style="UtilityHint")
    {
        var block=UtilityText(text,style);block.TextWrapping=TextWrapping.Wrap;block.TextLineBounds=TextLineBounds.Full;block.IsTextSelectionEnabled=true;return block;
    }
    private static string AutomationHistoryStatus(string status)=>status switch {
        "running"=>"执行中","queued"=>"等待执行","cancelling"=>"正在取消","cancelled"=>"已取消","cancel_requested"=>"已请求取消",
        "completed"=>"已完成","completed_with_errors"=>"部分失败","partially_completed"=>"部分完成",
        "saved_application_failed"=>"已保存，应用失败","failed"=>"未完成","restart_scheduled"=>"已安排重启",_=>status
    };
    private static string AutomationHistoryTitle(string tool)=>tool switch {
        "app_get_state"=>"读取软件状态","app_get_settings"=>"读取软件设置","app_update_settings"=>"修改软件设置",
        "app_flush_configuration"=>"保存当前配置","app_restart"=>"重启软件",
        "media_list"=>"读取素材库","media_import"=>"导入素材","media_import_batch"=>"批量导入素材",
        "media_remove"=>"移除素材引用","media_list_folders"=>"读取素材文件夹","media_relink"=>"重新关联素材文件",
        "media_refresh"=>"刷新素材信息","media_get_info"=>"读取素材详情","media_preview_poster"=>"预览素材封面",
        "media_set_poster"=>"设置素材封面","media_reset_poster"=>"恢复自动封面",
        "wallpaper_apply"=>"应用壁纸","wallpaper_cancel"=>"取消壁纸","wallpaper_set_paused"=>"调整壁纸暂停状态",
        "wallpaper_set_sound"=>"调整壁纸声音","wallpaper_step"=>"切换壁纸","wallpaper_set_playback"=>"修改播放方式",
        "wallpaper_set_fps_limit"=>"调整帧率上限","wallpaper_set_decoder_mode"=>"修改解码方式","wallpaper_edit_queue"=>"编辑播放队列",
        "wallpaper_get_screen_options"=>"读取显示器设置","wallpaper_set_screen_options"=>"修改显示器设置",
        "screen_set_features"=>"修改显示器总开关",
        "effect_get"=>"读取画面效果","effect_set"=>"调整画面效果","effect_set_scope"=>"修改效果作用范围",
        "effect_clear"=>"清空画面效果","effect_copy_screen_to_asset"=>"复制显示器效果到素材","effect_undo"=>"撤销效果修改",
        "effect_list_masks"=>"读取遮罩列表","effect_edit_mask"=>"编辑效果遮罩",
        "widget_get"=>"读取组件详情","widget_add"=>"添加桌面组件","widget_update"=>"修改桌面组件","widget_remove"=>"移除桌面组件",
        "widget_list_shortcuts"=>"读取快捷卡片","widget_remove_shortcut"=>"移除快捷卡片","widget_set_lock"=>"调整组件锁定状态",
        "widget_edit_group"=>"编辑组件组","widget_set_image"=>"设置组件图片","widget_reset_appearance"=>"恢复组件外观",
        "history_undo"=>"撤销上次操作","history_redo"=>"重做操作","history_create_checkpoint"=>"创建配置检查点",
        "history_restore_checkpoint"=>"恢复配置检查点","history_delete_checkpoint"=>"删除配置检查点",
        "config_export"=>"导出分享配置","config_preview_import"=>"预览导入配置","config_commit_import"=>"导入配置","config_cancel_import"=>"取消配置导入",
        "ui_capture_desktop"=>"截取桌面画面","ui_capture_window"=>"截取软件窗口","ui_get_tree"=>"读取界面控件","ui_perform_action"=>"操作软件界面",
        "audio_get_state"=>"读取音频状态","audio_list_devices"=>"读取音频设备","audio_set_capture_device"=>"选择声音采集设备",
        "audio_set_rule"=>"设置音频联动","audio_remove_rule"=>"移除音频联动","audio_play_test"=>"播放测试音","audio_stop_test"=>"停止测试音","audio_get_test_state"=>"读取测试音状态",
        "diagnostics_benchmark_decode"=>"测量解码性能","diagnostics_sample_performance"=>"读取运行性能","diagnostics_get_render_stats"=>"读取渲染状态",
        "diagnostics_get_logs"=>"读取诊断日志","diagnostics_export"=>"导出诊断信息",
        "task_get_operation"=>"读取操作结果","task_get_job"=>"读取任务进度","task_cancel_job"=>"取消后台任务",_=>tool
    };
}
