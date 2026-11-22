using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ZZZwallpaper.InstallerPreview;

public partial class MainWindow : Window
{
    internal const int DesignWidth = 1080;
    internal const int DesignHeight = 600;
    private readonly PreviewSession session = new();
    private readonly IInstallerBackend backend;
    private CancellationTokenSource? activeInstall;
    private bool allowClose;
    private IInputElement? previousFocus;
    private readonly bool renderOnly;
    private static Brush Accent => SoftwareTheme.Selection;
    private static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(119, 119, 119));
    private static readonly Brush Light = new SolidColorBrush(Color.FromRgb(238, 238, 238));

    public MainWindow() : this(new NoOpInstallerBackend(), false) { }

    internal MainWindow(bool renderOnly) : this(new NoOpInstallerBackend(), renderOnly) { }

    internal MainWindow(IInstallerBackend backend, bool renderOnly = false)
    {
        this.backend = backend;
        this.renderOnly = renderOnly;
        InitializeComponent();
        var workArea = SystemParameters.WorkArea;
        var scale = Math.Min(1, Math.Min((workArea.Width - 40) / DesignWidth, (workArea.Height - 40) / DesignHeight));
        Width = DesignWidth * Math.Max(0.5, scale);
        Height = DesignHeight * Math.Max(0.5, scale);
        ProductVersion.Text = ProductIdentity.Current.DisplayVersion;
        LicenseSelector.ItemsSource = LegalDocuments.ThirdParty;
        LicenseSelector.SelectedIndex = 0;
        AcceptAgreement.Checked += AgreementChanged;
        AcceptAgreement.Unchecked += AgreementChanged;
        if (backend is BundledInstallerBackend)
        {
            try { session.InstallDirectory = InstallationUpgrade.PreferredDirectory(); }
            catch (InstallerException error) { session.FailInstallation(error.Message); }
        }
        Refresh(true);
        Loaded += (_, _) => { InstallPath.Focus(); UpdateMotion(); };
        StateChanged += (_, _) => UpdateMotion();
        IsVisibleChanged += (_, _) => UpdateMotion();
        SystemParameters.StaticPropertyChanged += SystemPreferenceChanged;
        Closed += (_, _) =>
        {
            CancelActiveInstallation();
            if (!SoftwareTheme.Selection.IsFrozen) SoftwareTheme.Selection.BeginAnimation(SolidColorBrush.ColorProperty, null);
            SystemParameters.StaticPropertyChanged -= SystemPreferenceChanged;
        };
    }

    private void ReadChoices()
    {
        session.InstallDirectory = InstallPath.Text;
        session.DesktopShortcut = DesktopShortcut.IsChecked == true;
        session.AgreementAccepted = AcceptAgreement.IsChecked == true;
    }

    private void Refresh(bool syncChoices = false)
    {
        var pages = new[] { SettingsPage, ProgressPage, CompletePage, CancelledPage, ErrorPage };
        foreach (var page in pages) page.Visibility = Visibility.Collapsed;
        pages[(int)session.Stage].Visibility = Visibility.Visible;
        if (syncChoices)
        {
            InstallPath.Text = session.InstallDirectory;
            DesktopShortcut.IsChecked = session.DesktopShortcut;
            AcceptAgreement.IsChecked = session.AgreementAccepted;
        }
        PathError.Text = session.PathError;
        PathError.Visibility = session.PathError.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        SettingsTitle.Text = session.Scenario == PreviewScenario.Upgrade ? "准备更新" : "准备安装";
        StartLabel.Text = FlowText.Get(session.Scenario == PreviewScenario.Upgrade ? "StartUpdate" : "StartInstall");
        UpdateStartButton();
        ProgressNumber.Text = session.Progress.ToString("0.0");
        var width = (ProgressTrack.ActualWidth > 0 ? ProgressTrack.ActualWidth : 661) * session.Progress / 100;
        if (!renderOnly && session.Stage == PreviewStage.Progress && SystemParameters.ClientAreaAnimation)
            ProgressFill.BeginAnimation(WidthProperty, new DoubleAnimation(width, TimeSpan.FromMilliseconds(180)));
        else { ProgressFill.BeginAnimation(WidthProperty, null); ProgressFill.Width = width; }
        ProgressDestination.Text = CompleteDestination.Text = session.InstallDirectory;
        ErrorTitle.Text = session.ErrorTitle;
        ErrorHeading.Text = session.ApplicationRunning ? "程序正在运行" : "安装未完成";
        ExitAndInstallButton.Visibility = session.ApplicationRunning ? Visibility.Visible : Visibility.Collapsed;
        ErrorBackButton.SetResourceReference(StyleProperty, session.ApplicationRunning ? "UtilityButton" : "Primary");

        var active = session.Stage == PreviewStage.Complete ? 2 :
            session.Stage == PreviewStage.Progress || session.Progress > 0 ? 1 : 0;
        var badges = new[] { Step1Badge, Step2Badge, Step3Badge };
        var numbers = new[] { Step1Number, Step2Number, Step3Number };
        var texts = new[] { Step1Text, Step2Text, Step3Text };
        for (var i = 0; i < 3; i++)
        {
            badges[i].Background = i == active ? Accent : Brushes.Transparent;
            badges[i].BorderThickness = new Thickness(1);
            badges[i].BorderBrush = i <= active ? Accent : new SolidColorBrush(Color.FromRgb(56, 56, 56));
            numbers[i].Foreground = i == active ? Brushes.Black : i < active ? Accent : Muted;
            texts[i].Foreground = i == active ? Light : Muted;
        }
        ProgressStatus.Text = session.Detail.Stage;
        ProgressFile.Text = session.Detail.File;
        ProgressFile.ToolTip = session.Detail.File;
        ProgressFiles.Text = session.Detail.TotalFiles > 0 ? $"{session.Detail.CompletedFiles} / {session.Detail.TotalFiles} 个文件" : "";
    }

    private async void StartClick(object sender, RoutedEventArgs e) => await StartInstallationAsync();

    private async void ExitAndInstallClick(object sender, RoutedEventArgs e)
    {
        if (session.ApplicationRunning) await StartInstallationAsync(closeRunningApplication: true);
    }

    private async Task StartInstallationAsync(bool closeRunningApplication = false)
    {
        if (activeInstall is not null || pathPickerOpen || ModalOverlay.Visibility == Visibility.Visible) return;
        if (closeRunningApplication && session.ApplicationRunning) session.BackToSettings();
        if (session.Stage != PreviewStage.Settings) return;
        ReadChoices();
        if (!session.AgreementAccepted) { AcceptAgreement.Focus(); return; }
        session.Start(closeRunningApplication);
        LaunchAfter.IsChecked = false;
        Refresh(true);
        if (session.Stage != PreviewStage.Progress || renderOnly)
        {
            if (session.PathError.Length > 0) InstallPath.Focus();
            return;
        }
        using var run = new CancellationTokenSource();
        activeInstall = run;
        CancelInstallButton.IsEnabled = true;
        var request = new InstallationRequest(session.InstallDirectory, session.DesktopShortcut, ProductIdentity.Current.AgreementVersion, closeRunningApplication);
        var progress = new Progress<InstallationProgress>(percent =>
        {
            if (!ReferenceEquals(activeInstall, run) || run.IsCancellationRequested) return;
            session.ReportProgress(percent);
            Refresh();
        });
        try
        {
            await backend.InstallAsync(request, progress, run.Token);
            if (!ReferenceEquals(activeInstall, run)) return;
            session.Complete();
            CloseModal(false);
            Refresh();
        }
        catch (OperationCanceledException) when (run.IsCancellationRequested)
        {
            if (!ReferenceEquals(activeInstall, run)) return;
            session.Cancel();
            CloseModal(false);
            Refresh();
        }
        catch (ApplicationRunningException)
        {
            if (!ReferenceEquals(activeInstall, run)) return;
            session.RequireApplicationExit();
            CloseModal(false);
            Refresh();
        }
        catch (Exception exception)
        {
            if (!ReferenceEquals(activeInstall, run)) return;
            session.FailInstallation(exception is InstallerException ? exception.Message : "安装失败，请检查权限与可用空间后重试。");
            CloseModal(false);
            Refresh();
        }
        finally
        {
            if (ReferenceEquals(activeInstall, run)) activeInstall = null;
        }
    }

    private void BackClick(object sender, RoutedEventArgs e)
    {
        session.BackToSettings();
        Refresh(true);
        InstallPath.Focus();
    }

    private void OpenModal(bool path)
    {
        previousFocus = Keyboard.FocusedElement;
        LegalDialog.Visibility = Visibility.Collapsed;
        ModalFrame.Width = 540;
        ModalFrame.Height = double.NaN;
        CancelDialog.Visibility = path ? Visibility.Collapsed : Visibility.Visible;
        PathDialog.Visibility = path ? Visibility.Visible : Visibility.Collapsed;
        ModalOverlay.Visibility = Visibility.Visible;
        MainContent.IsEnabled = false;
        StartButton.IsDefault = false;
        if (path) DialogPath.Focus(); else KeepInstallingButton.Focus();
    }

    private void AgreementChanged(object sender, RoutedEventArgs e)
    {
        session.AgreementAccepted = AcceptAgreement.IsChecked == true;
        UpdateStartButton();
    }

    private void UpdateStartButton()
    {
        StartButton.IsEnabled = session.Stage == PreviewStage.Settings && session.AgreementAccepted;
        StartButton.IsDefault = StartButton.IsEnabled && ModalOverlay.Visibility != Visibility.Visible;
    }

    private void LegalClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string section }) OpenLegal(section);
    }

    private void OpenLegal(string section)
    {
        previousFocus = Keyboard.FocusedElement;
        CancelDialog.Visibility = PathDialog.Visibility = Visibility.Collapsed;
        ModalFrame.Width = 900;
        ModalFrame.Height = 536;
        LegalDialog.Visibility = ModalOverlay.Visibility = Visibility.Visible;
        MainContent.IsEnabled = false;
        StartButton.IsDefault = false;
        ShowLegalSection(section);
        LegalText.Focus();
    }

    private void LegalTabClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string section }) ShowLegalSection(section);
    }

    private void ShowLegalSection(string section)
    {
        var thirdParty = section == "third-party";
        LicenseSelector.IsDropDownOpen = false;
        LicenseSelector.Visibility = thirdParty ? Visibility.Visible : Visibility.Collapsed;
        foreach (var tab in new[] { AgreementTab, PrivacyTab, ThirdPartyTab })
        {
            var selected = (string)tab.Tag == section;
            tab.SetResourceReference(StyleProperty, selected ? "Primary" : "UtilityButton");
            tab.Height = 44;
        }
        ShowLegalDocument(thirdParty
            ? (LegalDocument)LicenseSelector.SelectedItem
            : section == "privacy" ? LegalDocuments.Privacy : LegalDocuments.Agreement);
    }

    private void ShowLegalDocument(LegalDocument document)
    {
        LegalText.Text = document.Read();
        System.Windows.Automation.AutomationProperties.SetName(LegalText, document.Title);
        LegalText.Select(0, 0);
        LegalText.ScrollToHome();
    }

    private void LicenseSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LegalDialog.Visibility == Visibility.Visible && LicenseSelector.SelectedItem is LegalDocument document)
            ShowLegalDocument(document);
    }

    private void CloseModal(bool restoreFocus = true)
    {
        ModalOverlay.Visibility = Visibility.Collapsed;
        CancelDialog.Visibility = PathDialog.Visibility = LegalDialog.Visibility = Visibility.Collapsed;
        LicenseSelector.IsDropDownOpen = false;
        MainContent.IsEnabled = true;
        UpdateStartButton();
        if (restoreFocus && previousFocus is UIElement { IsVisible: true, IsEnabled: true } element) element.Focus();
        previousFocus = null;
    }

    private bool pathPickerOpen;
    private void ChangePathClick(object sender, RoutedEventArgs e)
    {
        if (pathPickerOpen || activeInstall != null || session.Stage != PreviewStage.Settings) return;
        pathPickerOpen = true;
        if (sender is Button button) button.IsEnabled = false;
        StartButton.IsDefault = false;
        try {
            var picker = new Microsoft.Win32.OpenFolderDialog {
                Title = "选择安装文件夹", Multiselect = false,
                InitialDirectory = InstallationDirectory.InitialDirectory(InstallPath.Text)
            };
            if (picker.ShowDialog(this) != true) return;
            string path = InstallationDirectory.FromSelectedFolder(picker.FolderName, InstallPath.Text);
            string error = PreviewSession.ValidatePath(path);
            if (error.Length != 0) { PathError.Text = error; PathError.Visibility = Visibility.Visible; return; }
            ReadChoices();
            session.InstallDirectory = path;
            session.BackToSettings();
            Refresh(true);
        } catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException or ArgumentException or System.Runtime.InteropServices.COMException) {
            PathError.Text = "无法选择文件夹，请直接填写完整安装路径。";
            PathError.Visibility = Visibility.Visible;
        } finally {
            pathPickerOpen = false;
            if (sender is Button changed) changed.IsEnabled = true;
            UpdateStartButton();
        }
    }

    private void DialogPathKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        SavePathClick(sender, e);
        e.Handled = true;
    }

    private void SavePathClick(object sender, RoutedEventArgs e)
    {
        var path = InstallationDirectory.Normalize(DialogPath.Text);
        var error = PreviewSession.ValidatePath(path);
        DialogPathError.Text = error;
        DialogPathError.Visibility = error.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (error.Length > 0) { DialogPath.Focus(); return; }
        ReadChoices();
        session.InstallDirectory = path;
        session.BackToSettings();
        CloseModal();
        Refresh(true);
    }

    private void CancelClick(object sender, RoutedEventArgs e) => OpenModal(false);
    private void DismissModalClick(object sender, RoutedEventArgs e) => CloseModal();
    private void ConfirmCancelClick(object sender, RoutedEventArgs e)
    {
        if (activeInstall is { } run)
        {
            run.Cancel();
            CloseModal(false);
            CancelInstallButton.IsEnabled = false;
            ProgressStatus.Text = "正在取消";
            return;
        }
        session.Cancel();
        CloseModal(false);
        Refresh();
    }

    private void CancelActiveInstallation()
    {
        var run = activeInstall;
        activeInstall = null;
        run?.Cancel();
    }

    private async void FinishClick(object sender, RoutedEventArgs e)
    {
        session.LaunchAfter = LaunchAfter.IsChecked == true;
        MainContent.IsEnabled = false;
        try
        {
            await backend.FinishAsync(new InstallationRequest(session.InstallDirectory, session.DesktopShortcut),
                session.LaunchAfter, CancellationToken.None);
            allowClose = true;
            Close();
        }
        catch (Exception)
        {
            session.FailInstallation("无法完成安装，请重试。");
            Refresh();
        }
        finally { MainContent.IsEnabled = true; }
    }

    private void CloseClick(object sender, RoutedEventArgs e) => Close();
    private void MinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (allowClose || session.Stage != PreviewStage.Progress) return;
        e.Cancel = true;
        if (activeInstall?.IsCancellationRequested != true) OpenModal(false);
    }

    private void WindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        if (LicenseSelector.IsDropDownOpen) LicenseSelector.IsDropDownOpen = false;
        else if (ModalOverlay.Visibility == Visibility.Visible) CloseModal();
        else Close();
        e.Handled = true;
    }

    private void HeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        for (var element = e.OriginalSource as DependencyObject; element is not null; element = VisualTreeHelper.GetParent(element))
            if (element is ButtonBase) return;
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void SystemPreferenceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.ClientAreaAnimation)) Dispatcher.Invoke(UpdateMotion);
    }

    private void UpdateMotion()
    {
        var brush = SoftwareTheme.Selection;
        brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
        if (renderOnly || !IsVisible || WindowState == WindowState.Minimized || !SystemParameters.ClientAreaAnimation) return;
        // Same linear yellow -> green in 1 second, back in 1 second as the app.
        brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation
        {
            From = SoftwareTheme.ColorOf(SoftwareTheme.Palette.Accent),
            To = SoftwareTheme.ColorOf(SoftwareTheme.Palette.AccentEnd),
            Duration = TimeSpan.FromSeconds(1), AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever
        });
    }

    // Rendering these native WPF controls does not create a window or touch an
    // installation location. Used only by the explicit --render-preview command.
    internal string[] VerifyLegalFlow()
    {
        if (!renderOnly) throw new InvalidOperationException("Legal verification requires render-only mode.");
        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
        session.Reset();
        Refresh(true);
        Require(!StartButton.IsEnabled && AcceptAgreement.IsChecked == false, "Consent cannot be preselected.");
        StartClick(StartButton, new RoutedEventArgs());
        Require(session.Stage == PreviewStage.Settings, "Start handler bypassed consent.");
        InstallPath.Text = @"D:\用户 目录\ZZZwallpaper";
        DesktopShortcut.IsChecked = false;
        foreach (var section in new[] { "agreement", "privacy", "third-party" })
        {
            OpenLegal(section);
            Require(!MainContent.IsEnabled && !StartButton.IsDefault && LegalText.Text.Length > 0,
                "Document overlay must disable installation behind it.");
            StartClick(StartButton, new RoutedEventArgs());
            Require(session.Stage == PreviewStage.Settings, "Document overlay did not block start.");
            CloseModal(false);
            Require(AcceptAgreement.IsChecked == false && !StartButton.IsEnabled, "Reading a document must not grant consent.");
        }
        Require(InstallPath.Text == @"D:\用户 目录\ZZZwallpaper" && DesktopShortcut.IsChecked == false,
            "Reading documents changed installation choices.");
        AcceptAgreement.IsChecked = true;
        Require(StartButton.IsEnabled, "Explicit agreement must enable start.");
        OpenLegal("privacy");
        ShowLegalSection("third-party");
        LicenseSelector.SelectedIndex = 5;
        Require(LegalText.Text == LegalDocuments.ThirdParty[5].Read(), "License selection displayed the wrong original.");
        CloseModal(false);
        Require(AcceptAgreement.IsChecked == true && StartButton.IsEnabled, "Reading reset existing acceptance.");
        AcceptAgreement.IsChecked = false;
        Require(!StartButton.IsEnabled, "Revoking acceptance must disable start.");
        AcceptAgreement.IsChecked = true;
        StartClick(StartButton, new RoutedEventArgs());
        Require(session.Stage == PreviewStage.Progress, "Explicit consent did not permit the no-op flow.");
        session.Cancel();
        session.BackToSettings();
        Refresh(true);
        Require(AcceptAgreement.IsChecked == true && InstallPath.Text == @"D:\用户 目录\ZZZwallpaper",
            "Returning to settings lost current choices.");
        LicenseSelector.SelectedIndex = 0;
        session.Reset();
        Refresh(true);
        return ["UI start guarded before consent", "reading does not accept terms", "document modal blocks start",
            "legal reading retains path and shortcut", "explicit consent toggles start", "third-party selector loads original",
            "no-op installation after consent", "cancel/back retains current choices"];
    }

    internal string[] VerifyRunningFlow()
    {
        if (!renderOnly) throw new InvalidOperationException("Running-flow verification requires render-only mode.");
        session.Reset();
        session.InstallDirectory = @"D:\应用 空格\ZZZwallpaper";
        session.DesktopShortcut = false;
        session.AgreementAccepted = true;
        session.Scenario = PreviewScenario.Running;
        Refresh(true);
        StartInstallationAsync().GetAwaiter().GetResult();
        if (!session.ApplicationRunning || ExitAndInstallButton.Visibility != Visibility.Visible || session.Stage != PreviewStage.Error)
            throw new InvalidOperationException("Running detection must present the explicit exit-and-install choice.");
        StartInstallationAsync().GetAwaiter().GetResult();
        if (!session.ApplicationRunning) throw new InvalidOperationException("Ordinary start must not confirm closing the application.");
        BackClick(ErrorBackButton, new RoutedEventArgs());
        if (session.ApplicationRunning || ExitAndInstallButton.Visibility != Visibility.Collapsed || session.Stage != PreviewStage.Settings)
            throw new InvalidOperationException("Returning to settings must clear the exit choice.");
        StartInstallationAsync().GetAwaiter().GetResult();
        StartInstallationAsync(closeRunningApplication: true).GetAwaiter().GetResult();
        if (session.Stage != PreviewStage.Progress || session.ApplicationRunning || !session.AgreementAccepted || session.DesktopShortcut || session.InstallDirectory != @"D:\应用 空格\ZZZwallpaper")
            throw new InvalidOperationException("Explicit exit choice must continue with the accepted agreement and original settings.");
        session.Reset(); Refresh(true);
        return ["running app exposes exit-and-install", "ordinary start cannot consent to closing", "back clears exit choice", "exit-and-install retains accepted settings"];
    }

    internal void PrepareSnapshot(string name)
    {
        CloseModal(false);
        session.Reset();
        session.AgreementAccepted = name != "settings";
        if (name == "upgrade") session.Scenario = PreviewScenario.Upgrade;
        if (name == "invalid")
        {
            session.Scenario = PreviewScenario.Upgrade;
            session.InstallDirectory = "relative";
            session.Start();
        }
        if (name == "failure") session.Scenario = PreviewScenario.Failure;
        if (name == "running") { session.Scenario = PreviewScenario.Running; session.Start(); }
        if (name is "progress" or "complete" or "cancel" or "cancelled" or "failure")
        {
            session.Start();
            var steps = name is "complete" or "failure" ? 50 : 21;
            for (var i = 0; i < steps; i++) session.Advance();
            if (session.Stage == PreviewStage.Progress) session.ReportProgress(new InstallationProgress(100d*643/1523, File: "Mcp\\Microsoft.Extensions.DependencyInjection.dll", CompletedFiles: 643, TotalFiles: 1523));
        }
        if (name == "cancelled") session.Cancel();
        Refresh(true);
        if (name == "cancel") OpenModal(false);
        if (name == "path")
        {
            DialogPath.Text = session.InstallDirectory;
            DialogPathError.Visibility = Visibility.Collapsed;
            OpenModal(true);
        }
        if (name is "agreement" or "privacy" or "third-party") OpenLegal(name);
        if (name == "license")
        {
            OpenLegal("third-party");
            LicenseSelector.SelectedIndex = 5;
        }
        PreviewRoot.Measure(new Size(DesignWidth, DesignHeight));
        PreviewRoot.Arrange(new Rect(0, 0, DesignWidth, DesignHeight));
        PreviewRoot.UpdateLayout();
        Refresh();
        PreviewRoot.UpdateLayout();
    }
}
