using System.ComponentModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ZZZwallpaper.InstallerPreview;

public partial class UninstallWindow : Window
{
    internal const int DesignWidth = 880, DesignHeight = 500;
    private readonly string directory;
    private readonly bool renderOnly;
    private readonly bool fatal;
    private bool retryAllowed=true;
    private bool cleanupPending;
    private string stage = "confirm", error = "";
    private string completion = "个人配置和数据已保留";

    internal UninstallWindow(string directory, bool renderOnly = false, string? initialError = null)
    {
        this.directory = directory;
        this.renderOnly = renderOnly;
        fatal = initialError is not null;
        InitializeComponent();
        var area = SystemParameters.WorkArea;
        var scale = Math.Clamp(Math.Min((area.Width - 40) / DesignWidth, (area.Height - 40) / DesignHeight), 0.5, 1);
        Width = DesignWidth * scale; Height = DesignHeight * scale;
        ProductVersion.Text = ProductIdentity.Current.DisplayVersion;
        InstallLocation.Text = directory;
        KeepPersonalData.Checked += (_, _) => Refresh();
        KeepPersonalData.Unchecked += (_, _) => Refresh();
        if (initialError is not null) { stage = "error"; error = initialError; }
        Refresh();
        Closed += (_, _) => BusyPosition.BeginAnimation(TranslateTransform.XProperty, null);
    }

    private void Refresh()
    {
        var busy = stage == "progress";
        var terminal=fatal||!retryAllowed;
        StageTitle.Text = stage switch { "progress" => "正在卸载", "complete" => "卸载完成", "error" => "卸载未完成", _ => "卸载 ZZZwallpaper" };
        StatusText.Text = stage switch { "error" => error, "complete" => completion, "progress" => "", _ => KeepPersonalData.IsChecked == true ? "卸载时会自动关闭软件及后台组件。" : "自动关闭软件，删除素材库记录、画面效果、组件设置、个人配置、缓存和日志；保留原始图片和视频。" };
        StatusText.Visibility = StatusText.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        KeepPersonalData.Visibility = stage is "confirm" or "error" && !terminal ? Visibility.Visible : Visibility.Collapsed;
        LocationPanel.Visibility = stage == "complete" ? Visibility.Collapsed : Visibility.Visible;
        BusyTrack.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        CloseButton.IsEnabled = !busy;
        CancelButton.Visibility = !busy&&cleanupPending?Visibility.Visible:busy || stage == "complete" || terminal ? Visibility.Collapsed : Visibility.Visible;
        CancelButton.Content = stage == "error"||cleanupPending ? "关闭" : "取消";
        ActionButton.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;
        ActionButton.Content = cleanupPending?"重试清理数据":terminal ? "关闭" : stage switch { "complete" => "完成", _ => KeepPersonalData.IsChecked == true ? "卸载" : "卸载并删除数据" };
        BusyPosition.BeginAnimation(TranslateTransform.XProperty, busy && !renderOnly && SystemParameters.ClientAreaAnimation
            ? new DoubleAnimation(-220, 760, TimeSpan.FromSeconds(1.7)) { RepeatBehavior = RepeatBehavior.Forever } : null);
    }

    private async void ActionClick(object sender, RoutedEventArgs e)
    {
        if (renderOnly || stage == "progress") return;
        if(cleanupPending) {
            stage="progress";Refresh();StageTitle.Text="正在清理个人数据";
            try {
                if(await UninstallBackend.RetryCleanupAsync(directory)) {cleanupPending=false;stage="complete";Close();return;}
                completion="软件已卸载，但个人数据仍未清理完整。可重试清理；这些设置可能在重新安装后恢复。";
            }catch(Exception ex) {completion="软件已卸载，个人数据清理失败："+ex.Message;}
            stage="complete";Refresh();return;
        }
        if (stage == "complete" || fatal || !retryAllowed) { Close(); return; }
        stage = "progress"; Refresh();
        try
        {
            var result = await UninstallBackend.ExecuteAsync(new UninstallRequest(directory, Confirmed: true, KeepPersonalData: KeepPersonalData.IsChecked == true),
                new Progress<string>(message => { if (stage == "progress") StageTitle.Text = message; }));
            cleanupPending=result.CleanupIncomplete;
            completion = result.CleanupIncomplete ? "软件已卸载，但个人数据未清理完整。可重试清理；这些设置可能在重新安装后恢复。" : result.KeptPersonalData ? "个人配置和数据已保留" : "素材库、画面效果、组件设置及个人配置已删除";
            stage = "complete";
            retryAllowed=false;
            if(!result.CleanupIncomplete) {Close();return;}
        }
        catch (Exception exception)
        {
            error = exception is InstallerException ? exception.Message : "请检查卸载文件与访问权限后重试。";
            retryAllowed=System.IO.File.Exists(System.IO.Path.Combine(directory,"unins000.exe"));
            stage = "error";
        }
        Refresh();
    }

    private void WindowClosing(object? sender, CancelEventArgs e) { if (stage == "progress" && !renderOnly) e.Cancel = true; }
    private void CloseClick(object sender, RoutedEventArgs e) => Close();
    private void MinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void WindowKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) { Close(); e.Handled = true; } }
    private void HeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        for (var element = e.OriginalSource as DependencyObject; element is not null; element = VisualTreeHelper.GetParent(element))
            if (element is ButtonBase) return;
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    internal void PrepareSnapshot(string state)
    {
        if (!renderOnly) throw new InvalidOperationException("Snapshots require the non-executing render mode.");
        stage = state switch { "delete-confirm" => "confirm", "delete-complete" or "cleanup-incomplete" => "complete", _ => state };
        KeepPersonalData.IsChecked = state is not ("delete-confirm" or "delete-complete" or "cleanup-incomplete");
        cleanupPending=state=="cleanup-incomplete";
        completion = cleanupPending ? "软件已卸载，但个人数据未清理完整。可重试清理；这些设置可能在重新安装后恢复。" : state == "delete-complete" ? "素材库、画面效果、组件设置及个人配置已删除" : "个人配置和数据已保留";
        error = "系统拒绝关闭后台进程，尚未删除程序文件。";
        Refresh();
        PreviewRoot.Measure(new Size(DesignWidth, DesignHeight));
        PreviewRoot.Arrange(new Rect(0, 0, DesignWidth, DesignHeight));
        PreviewRoot.UpdateLayout();
    }
}
