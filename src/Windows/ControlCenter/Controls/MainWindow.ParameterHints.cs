using LumaWall.Contracts;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace LumaWall.ControlCenter;
public sealed partial class MainWindow
{
    internal const string HelpHintsKey="ui.helpHints.enabled";
    private static WeakReference<MainWindow>? parameterHintOwner;
    private static readonly ConditionalWeakTable<TextBlock,Grid> parameterHintHosts=new();
    private const string InlineHintTag="parameter-inline-hint";
    private readonly List<WeakReference<Button>> helpButtons=[];
    private ToggleSwitch? helpHintsSwitch;
    private bool helpHintsEnabled=true;
    private static bool TryParameterHintHost(TextBlock label,out Grid host)
    {
        // A WinRT element can receive a new managed wrapper after collection.
        // The native parent marker, unlike a weak-table key, survives that.
        if(label.Parent is Grid {Tag:InlineHintTag} parent) {
            host=parent;if(!parameterHintHosts.TryGetValue(label,out _))parameterHintHosts.Add(label,parent);return true;
        }
        return parameterHintHosts.TryGetValue(label,out host!);
    }
    private static FrameworkElement ParameterLabelHost(FrameworkElement label)=>label is TextBlock text&&TryParameterHintHost(text,out var host)?host:label;
    private static MainWindow? ParameterOwner(FrameworkElement element)
    {
        if(parameterHintOwner is not { } reference||!reference.TryGetTarget(out var owner))return null;
        for(var node=element.Parent;node!=null;node=VisualTreeHelper.GetParent(node))
            if(node==owner.DisplayPanel||node==owner.WidgetsPanel||node==owner.SettingsWorkspace)return owner;
        return null;
    }
    // The control and its help use disjoint layout cells and hit targets.
    private static void AddExternalHint(Grid row, Button hint, bool leading)
    {
        row.Tag=ExternalHintTag;
        var contentColumn=new ColumnDefinition();
        var hintColumn=new ColumnDefinition {Width=GridLength.Auto};
        row.ColumnDefinitions.Add(leading?hintColumn:contentColumn);
        row.ColumnDefinitions.Add(leading?contentColumn:hintColumn);
        if(leading) foreach(var child in row.Children) Grid.SetColumn((FrameworkElement)child,1);
        Grid.SetColumn(hint,leading?0:1);
        hint.Margin=leading?new Thickness(0,0,4,0):new Thickness(4,0,0,0);
        row.Children.Add(hint);
    }
    private const string ExternalHintTag="external-control-help";
    private static UIElement SectionHeaderWithHelp(UIElement header,ContentControl card,TextBlock label)
    {
        var row=new Grid();
        row.Children.Add(header);
        row.Loaded+=(_,_)=> {
            if(TryParameterHintHost(label,out _) || ParameterOwner(card) is not { } owner)return;
            string title=SectionChrome.GetTitle(card);
            string description=ParameterHelpText.Describe(title,[]);
            if(title!=UiLanguage.Text("层级")&&description.Length>0)PlaceTitleHint(row,label,owner.HelpIcon(title+UiLanguage.Text("说明"),description));
        };
        return row;
    }
    // Reserve space after the title, with a sibling hit target above the card.
    // Reposition only on size or attachment changes, never on a render timer.
    private static void PlaceTitleHint(Grid card,TextBlock title,Button hint)
    {
        var anchor=new Border {Width=16,Height=16,VerticalAlignment=VerticalAlignment.Center,IsHitTestVisible=false};
        anchor.SetBinding(UIElement.VisibilityProperty,new Microsoft.UI.Xaml.Data.Binding {Source=hint,Path=new PropertyPath("Visibility")});
        if(title.IsLoaded)InlineHint(title,anchor);
        else title.Loaded+=AttachAnchor;
        void AttachAnchor(object sender,RoutedEventArgs args) {
            title.Loaded-=AttachAnchor;
            InlineHint(title,anchor);
        }
        var overlay=new Canvas();
        overlay.Children.Add(hint);
        card.Children.Add(overlay);
        void Position() {
            if(!anchor.IsLoaded || !overlay.IsLoaded)return;
            var point=anchor.TransformToVisual(overlay).TransformPoint(default);
            Canvas.SetLeft(hint,point.X);
            Canvas.SetTop(hint,point.Y);
        }
        anchor.Loaded+=(_,_)=>Position();
        overlay.Loaded+=(_,_)=>Position();
        title.SizeChanged+=(_,_)=>Position();
        anchor.SizeChanged+=(_,_)=>Position();
        card.SizeChanged+=(_,_)=>Position();
    }
    private static Grid? InlineHint(TextBlock label,FrameworkElement hint)
    {
        if(TryParameterHintHost(label,out var existing))return existing;
        if(label.Parent is not Panel parent)return null;
        int index=parent.Children.IndexOf(label);if(index<0)return null;
        var host=new Grid {Tag=InlineHintTag,ColumnSpacing=6,Margin=label.Margin,VerticalAlignment=label.VerticalAlignment,HorizontalAlignment=HorizontalAlignment.Left};
        host.ColumnDefinitions.Add(new() {Width=GridLength.Auto});host.ColumnDefinitions.Add(new() {Width=GridLength.Auto});
        Grid.SetColumn(host,Grid.GetColumn(label));Grid.SetRow(host,Grid.GetRow(label));Grid.SetColumnSpan(host,Grid.GetColumnSpan(label));Grid.SetRowSpan(host,Grid.GetRowSpan(label));
        parent.Children.RemoveAt(index);label.Margin=new Thickness(0);label.HorizontalAlignment=HorizontalAlignment.Left;Grid.SetColumn(label,0);Grid.SetRow(label,0);Grid.SetColumnSpan(label,1);Grid.SetRowSpan(label,1);
        host.Children.Add(label);Grid.SetColumn(hint,1);host.Children.Add(hint);parameterHintHosts.Add(label,host);parent.Children.Insert(index,host);return host;
    }
    internal static void AttachParameterHint(TextBlock label)
    {
        if(label.Text == UiLanguage.Text("层级") || label.Text == UiLanguage.Text("组件层级")||!label.IsLoaded||label.Visibility!=Visibility.Visible||string.IsNullOrWhiteSpace(label.Text)
            ||TryParameterHintHost(label,out _))return;
        if(parameterHintOwner is not { } reference||!reference.TryGetTarget(out var owner))return;
        bool inEditor=false,inSettings=false;
        for(var node=label.Parent;node!=null;node=VisualTreeHelper.GetParent(node)) {
            if(node is ButtonBase or ComboBoxItem)return;
            if(node==owner.DisplayPanel||node==owner.WidgetsPanel) {inEditor=true;break;}
            if(node==owner.SettingsWorkspace) {inEditor=inSettings=true;break;}
        }
        if(!inEditor||label.Parent is not Panel parent)return;
        if(IsSectionHeading(label)&&!inSettings)return;
        if(inSettings&&!SettingsHelpText.TryDescribe(label.Text,out _))return;
        var siblingControls=parent.Children.OfType<FrameworkElement>().Where(child=>child!=label).ToArray();
        string description=inSettings&&SettingsHelpText.TryDescribe(label.Text,out var settingText)?settingText:ParameterHelpText.Describe(label.Text,siblingControls);
        if(description.Length>0)InlineHint(label,owner.HelpIcon(label.Text+UiLanguage.Text("说明"),description));
    }
    private void SetHelpHintsEnabled(bool enabled,bool save=true)
    {
        if(save)modules.Settings.Set(HelpHintsKey,enabled?"true":"false");
        helpHintsEnabled=enabled;DismissHelp();
        for(int i=helpButtons.Count-1;i>=0;i--) {
            if(helpButtons[i].TryGetTarget(out var button))button.Visibility=enabled?Visibility.Visible:Visibility.Collapsed;
            else helpButtons.RemoveAt(i);
        }
        if(helpHintsSwitch!=null&&helpHintsSwitch.IsOn!=enabled)helpHintsSwitch.IsOn=enabled;
    }
    private void InitializeHelpHintsSetting(StackPanel settings)
    {
        helpHintsSwitch=WidgetToggle(UiLanguage.Text("显示小说明"),helpHintsEnabled);
        settings.Children.Add(WidgetToggleRow(UiLanguage.Text("显示小说明"),helpHintsSwitch));
        helpHintsSwitch.Toggled+=(_,_)=> {if(helpHintsSwitch.IsOn!=helpHintsEnabled)SetHelpHintsEnabled(helpHintsSwitch.IsOn);};
        settings.Children.Add(WidgetButton(UiLanguage.Text("新手引导"),()=>_=ShowOnboarding()));
    }
    private void AddCardHelp(Grid card,string title)
    {
        string description=ParameterHelpText.Describe(title,[]);
        if(description.Length==0)return;
        var hint=HelpIcon(title+UiLanguage.Text("说明"),description);
        if(card.Children.OfType<Button>().FirstOrDefault()?.Content is FrameworkElement content &&
            EffectsLogicalChildren(content).OfType<TextBlock>().FirstOrDefault(IsSectionHeading) is { } label)
            PlaceTitleHint(card,label,hint);
        else AddExternalHint(card,hint,true);
    }
    private void RefreshSettingsHints()
    {
        // Visit only the active settings body. The category strip is navigation.
        FrameworkElement body=navigationPage switch {
            "performance"=>PerformancePanel, "appearance"=>AppearancePanel,
            "packs"=>ExpansionPanel, "history"=>HistoryPanel, _=>SettingsPanel
        };
        foreach(var button in EffectsLogicalChildren(body).OfType<Button>().ToArray()) {
            if(button.Style==HomeStyle("CompactHelpButton") || button.Parent is not Panel parent || parent.Tag is ExternalHintTag)continue;
            string title=button.Content as string??Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(button);
            if(!SettingsHelpText.TryDescribe(title,out var description))continue;
            int index=parent.Children.IndexOf(button);
            if(index<0)continue;
            bool tile=skinButtons.ContainsValue(button);
            var row=new Grid {HorizontalAlignment=tile?HorizontalAlignment.Stretch:HorizontalAlignment.Left,VerticalAlignment=button.VerticalAlignment,Margin=button.Margin};
            Grid.SetColumn(row,Grid.GetColumn(button));Grid.SetRow(row,Grid.GetRow(button));
            Grid.SetColumnSpan(row,Grid.GetColumnSpan(button));Grid.SetRowSpan(row,Grid.GetRowSpan(button));
            parent.Children.RemoveAt(index);
            button.Margin=new Thickness(0);Grid.SetColumn(button,0);Grid.SetRow(button,0);Grid.SetColumnSpan(button,1);Grid.SetRowSpan(button,1);
            row.Children.Add(button);
            AddExternalHint(row,HelpIcon(title+UiLanguage.Text("说明"),description),tile);
            parent.Children.Insert(index,row);
        }
        foreach(var label in EffectsLogicalChildren(body).OfType<TextBlock>().ToArray())
            AttachParameterHint(label);
    }
}
