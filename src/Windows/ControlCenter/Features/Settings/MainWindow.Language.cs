using LumaWall.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private void InitializeLanguageSetting()
    {
        var saved = UiLanguage.Preference(modules.Settings.Get(UiLanguage.SettingKey));
        ComboBoxItem Option(string tag) => LanguagePicker.Items.Cast<ComboBoxItem>().First(item => Equals(item.Tag, tag));
        LanguagePicker.SelectedItem = Option(saved);
        LanguagePicker.SelectionChanged += (_, _) =>
        {
            if (LanguagePicker.SelectedItem is not ComboBoxItem { Tag: string selected } || selected == saved) return;
            try
            {
                modules.Settings.Set(UiLanguage.SettingKey, selected);
                saved = selected;
                var pending = UiLanguage.Resolve(selected, UiLanguage.SystemLanguage) != UiLanguage.Current;
                LanguageRestartButton.Visibility = LanguageRestartHint.Visibility = pending ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception error)
            {
                LanguagePicker.SelectedItem = Option(saved);
                ShowError(UiLanguage.Text("无法保存语言设置：") + error.Message, error);
            }
        };
    }

    private void RestartForLanguage_Click(object sender, RoutedEventArgs args)
    {
        try { ScheduleRestart(); }
        catch (Exception error) { ShowError(UiLanguage.Text("无法重新启动：") + error.Message, error); }
    }
}
