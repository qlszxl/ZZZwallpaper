using LumaWall.Contracts;
namespace LumaWall.ControlCenter;
internal static class SettingsHelpText
{
    private static readonly Dictionary<string,string> descriptions=new() {
        [UiLanguage.Text("开机自动启动")]=UiLanguage.Text("登录 Windows 后自动启动软件，壁纸按保存的配置恢复。"),
        [UiLanguage.Text("延时启动")]=UiLanguage.Text("在登录后等待指定秒数再启动软件，减轻同时启动的负担。"),
        [UiLanguage.Text("延时")]=UiLanguage.Text("从登录 Windows 到启动软件之间的等待秒数。"),
        [UiLanguage.Text("显示小说明")]=UiLanguage.Text("统一显示或隐藏效果、组件和设置中，文字旁的说明图标。"),
        [UiLanguage.Text("托盘与驻留")]=UiLanguage.Text("关闭主窗口后软件继续在托盘运行，可从托盘菜单暂停或退出。"),
        [UiLanguage.Text("其他应用全屏或最大化时暂停")]=UiLanguage.Text("其他窗口覆盖对应显示器时暂停该屏幕的壁纸，离开后恢复。"),
        [UiLanguage.Text("锁屏时暂停")]=UiLanguage.Text("锁定 Windows 时暂停壁纸，解锁后按其他暂停条件恢复。"),
        [UiLanguage.Text("熄屏时暂停")]=UiLanguage.Text("显示器关闭时暂停壁纸，亮屏后按其他暂停条件恢复。"),
        [UiLanguage.Text("休眠时暂停")]=UiLanguage.Text("系统休眠期间暂停，唤醒后按其他暂停条件恢复。"),
        [UiLanguage.Text("退出软件")]=UiLanguage.Text("结束软件及其壁纸、组件进程，恢复由软件接管的桌面功能。"),
        [UiLanguage.Text("退出并恢复系统桌面")]=UiLanguage.Text("结束软件及其壁纸、组件进程，恢复由软件接管的桌面功能。"),
        [UiLanguage.Text("画面帧率限制")]=UiLanguage.Text("为视频壁纸设置最高输出帧率，降低绘制负载。"),
        [UiLanguage.Text("限制画面帧率")]=UiLanguage.Text("开启后限制壁纸输出帧率；关闭后使用原有播放调度。"),
        [UiLanguage.Text("FPS 上限")]=UiLanguage.Text("每秒最多输出的画面帧数，不会提高素材原有帧率。"),
        [UiLanguage.Text("视频解码")]=UiLanguage.Text("选择视频由显卡硬件或 CPU 解码；自动模式优先尝试硬件。"),
        [UiLanguage.Text("解码方式")]=UiLanguage.Text("选择视频解码策略，硬件支持因显卡、驱动和视频编码而异。"),
        [UiLanguage.Text("音频处理")]=UiLanguage.Text("统一处理音频输入，所有音频联动和频谱共用处理结果。"),
        [UiLanguage.Text("自动适应")]=UiLanguage.Text("根据近期音量与频谱调整音频响应，减少不同歌曲间反复调参。"),
        [UiLanguage.Text("品牌底纹")]=UiLanguage.Text("显示主界面和参数面板的品牌底纹。开启后会增加软件界面的绘制开销。"),
        [UiLanguage.Text("界面动效")]=UiLanguage.Text("播放页面切换和操作反馈动画。开启后会增加软件界面的渲染开销。"),
        [UiLanguage.Text("预览动效")]=UiLanguage.Text("允许效果预览播放动画，关闭时使用静态展示。"),
        [UiLanguage.Text("日志与问题反馈")]=UiLanguage.Text("查看运行日志或导出诊断包，导出时可以选择包含的信息。"),
        [UiLanguage.Text("高负载自动暂停")]=UiLanguage.Text("检测其他程序的 CPU 或 GPU 负载，并按条件暂停壁纸。"),
        [UiLanguage.Text("其他程序高 CPU / GPU 占用时暂停壁纸")]=UiLanguage.Text("其他程序持续占用较高资源时暂停壁纸，负载降低后恢复。"),
        [UiLanguage.Text("AI 操作")]=UiLanguage.Text("通过本机 MCP 接口控制软件；写操作和目录访问受授权设置限制。"),
        [UiLanguage.Text("启用 AI 操作")]=UiLanguage.Text("启动本机 MCP 服务，允许已授权客户端调用软件功能。"),
        [UiLanguage.Text("允许修改配置")]=UiLanguage.Text("允许 AI 执行配置写入，关闭后仅保留可用的只读查询。"),
        [UiLanguage.Text("允许读取日志")]=UiLanguage.Text("允许 AI 查询诊断信息与日志，日志可能包含本机路径。"),
        [UiLanguage.Text("允许截图")]=UiLanguage.Text("允许 AI 请求可见桌面的截图，可能包含其他窗口内容。"),
        [UiLanguage.Text("壁纸处理明细")]=UiLanguage.Text("查看运行中壁纸的解码、输出和资源占用数据。")
        ,["MCP"]=UiLanguage.Text("通过本机接口连接 AI 客户端，素材文件访问仅限用户授权目录。")
        ,[UiLanguage.Text("开启")]=UiLanguage.Text("开启或关闭本机 MCP 接口；开启后可复制连接提示词供 AI 客户端使用。")
        ,[UiLanguage.Text("授权 AI 素材文件夹")]=UiLanguage.Text("手动选择允许 AI 读取和导入素材的目录。")
        ,[UiLanguage.Text("清除 AI 素材目录授权")]=UiLanguage.Text("撤销此前授权的素材目录，并取消尚未完成的相关操作。")
        ,[UiLanguage.Text("复制 AI 连接提示词")]=UiLanguage.Text("复制当前软件 MCP 的连接方式，供支持 MCP 的 AI 客户端使用。")
        ,[UiLanguage.Text("AI 操作记录")]=UiLanguage.Text("查看 AI 请求的执行情况、关联操作编号和错误。")
        ,[UiLanguage.Text("效果预览图片")]=UiLanguage.Text("选择效果参数页的示例图片，不会替换桌面正在播放的素材。")
        ,[UiLanguage.Text("测试视频解码性能")]=UiLanguage.Text("使用所选视频比较解码性能，查看结果后再决定是否应用建议。")
        ,[UiLanguage.Text("导出诊断包")]=UiLanguage.Text("选择包含运行环境、配置等信息后导出诊断 ZIP，方便定位问题。")
    };
    internal static bool TryDescribe(string title,out string text)
    {
        if(descriptions.TryGetValue(title,out text!))return true;
        if(title.Length<96&&!title.Contains('\n')&&(title.StartsWith(UiLanguage.Text("显示器 "))||title.StartsWith(UiLanguage.Text("主屏幕 ")))) {text=UiLanguage.Text("控制是否在该显示器桌面的空白区域使用软件右键菜单。");return true;}
        text="";return false;
    }
}
