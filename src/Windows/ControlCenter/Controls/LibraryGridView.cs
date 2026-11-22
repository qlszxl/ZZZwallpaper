using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using VirtualKey = Windows.System.VirtualKey;

namespace LumaWall.ControlCenter;

// Keep WinUI's extended selection (including keyboard/range selection), with
// a separate checkbox that toggles one item without replacing the selection.
public sealed class LibraryGridView : GridView
{
    private long selectionRevision, interactionRevision, keyboardInputRevision;
    private long keyboardSelectionBefore, keyboardSelectionInput;
    private long keyboardSelectionCompleted=-1;
    private bool keyboardSelectionPending;
    private ScrollViewer? cardScrollViewer;
    private long cardViewportToken;
    private bool cardResizeQueued;
    private bool motionEnabled;
    private bool pageActive;

    internal void SetPageActive(bool active)
    {
        pageActive=active;
        if(!active)SetMotionEnabled(false);
        else QueueCardResize();
    }

    internal void SetMotionEnabled(bool enabled)
    {
        if(motionEnabled==enabled)return;
        motionEnabled=enabled;
        ItemContainerTransitions=enabled
            ?new TransitionCollection {new AddDeleteThemeTransition(),new ContentThemeTransition(),new ReorderThemeTransition(),new EntranceThemeTransition {IsStaggeringEnabled=false}}
            :new TransitionCollection();
    }
    private int workspaceCardColumns = 1;
    private double workspaceCardWidth;
    public event Action<object>? SelectionInteractionCompleted;
    public object? InteractionItem {get;private set;}

    internal double CardViewportWidth => Math.Max(0,
        (cardScrollViewer is {ViewportWidth: > 0} viewer ? viewer.ViewportWidth : ActualWidth)
        - Padding.Left - Padding.Right);

    // The shell measures the complete workspace once for both cards and
    // details. Never divide this control's potentially narrower width again.
    internal void SetWorkspaceLayout(int columns, double cardWidth)
    {
        if(workspaceCardColumns==columns&&workspaceCardWidth==cardWidth)return;
        workspaceCardColumns=columns;
        workspaceCardWidth=cardWidth;
        QueueCardResize();
    }

    protected override void OnApplyTemplate()
    {
        if(cardScrollViewer!=null)
            cardScrollViewer.UnregisterPropertyChangedCallback(ScrollViewer.ViewportWidthProperty,cardViewportToken);
        base.OnApplyTemplate();
        cardScrollViewer=GetTemplateChild("ScrollViewer") as ScrollViewer;
        if(cardScrollViewer!=null)
            cardViewportToken=cardScrollViewer.RegisterPropertyChangedCallback(ScrollViewer.ViewportWidthProperty,(_,_)=>QueueCardResize());
        QueueCardResize();
    }

    internal void QueueCardResize()
    {
        if(cardResizeQueued||!pageActive)return;
        cardResizeQueued=true;
        if(!DispatcherQueue.TryEnqueue(()=> {
            cardResizeQueued=false;
            if(IsLoaded&&pageActive)ResizeCards();
        }))cardResizeQueued=false;
    }

    internal void ResizeCards()
    {
        if(ItemsPanelRoot is not ItemsWrapGrid panel || workspaceCardWidth<=0)return;
        // Containers have a 12 DIP trailing margin. Compensate for that last
        // empty gap while keeping the separate scrollbar lane available.
        var margin=new Thickness(0,0,-LibraryPresentation.CardSpacing,0);
        if(panel.Margin!=margin)panel.Margin=margin;
        if(panel.MaximumRowsOrColumns!=workspaceCardColumns)panel.MaximumRowsOrColumns=workspaceCardColumns;
        double itemWidth=workspaceCardWidth+LibraryPresentation.CardSpacing;
        if(panel.ItemWidth!=itemWidth)panel.ItemWidth=itemWidth;
    }

    public LibraryGridView()
    {
        SelectionChanged+=(_,_)=>selectionRevision++;
        AddHandler(PreviewKeyDownEvent,new KeyEventHandler((_,e)=> {
            if(e.Key is not (VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down or
                VirtualKey.Home or VirtualKey.End or VirtualKey.PageUp or VirtualKey.PageDown or VirtualKey.Space or VirtualKey.A))return;
            ResetInteraction();
            keyboardSelectionBefore=selectionRevision;
            keyboardSelectionInput=keyboardInputRevision;
            keyboardSelectionCompleted=-1;
            keyboardSelectionPending=true;
            long input=keyboardInputRevision;
            // WinUI first moves focus and changes its extended selection. Resolve
            // the keyboard endpoint once that input has completed, including a
            // Shift-range contraction that adds no new selected items. Queue
            // before SelectionChanged so this precedes its visibility request.
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.High,()=> {
                if(input!=keyboardInputRevision||selectionRevision!=keyboardSelectionCompleted||XamlRoot==null)return;
                for(var node=FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;node!=null&&node!=this;node=VisualTreeHelper.GetParent(node)) {
                    if(node is not GridViewItem container||ItemFromContainer(container) is not { } item)continue;
                    if(container.IsSelected)SelectionInteractionCompleted?.Invoke(item);
                    break;
                }
            });
        }),true);
        AddHandler(KeyDownEvent,new KeyEventHandler((_,_)=> {
            if(!keyboardSelectionPending)return;
            keyboardSelectionPending=false;
            if(keyboardSelectionInput==keyboardInputRevision&&selectionRevision!=keyboardSelectionBefore)
                keyboardSelectionCompleted=selectionRevision;
        }),true);
    }
    internal void RememberInteraction(object? item)
    {
        keyboardInputRevision++;
        interactionRevision++;
        InteractionItem=item;
    }
    internal void ForgetInteraction(object? item) {if(ReferenceEquals(InteractionItem,item))ClearPointerInteraction();}
    internal void QueueForgetInteraction(object? item)
    {
        long revision=interactionRevision;
        DispatcherQueue.TryEnqueue(()=> {if(revision==interactionRevision)ForgetInteraction(item);});
    }
    public object? ConsumeInteraction()
    {
        var item=InteractionItem;
        ClearPointerInteraction();
        return item;
    }
    internal void ClearPointerInteraction() {interactionRevision++;InteractionItem=null;}
    public void ResetInteraction()
    {
        keyboardInputRevision++;
        keyboardSelectionPending=false;
        ClearPointerInteraction();
    }
    protected override DependencyObject GetContainerForItemOverride()=>new LibraryGridViewItem(this);
}

internal sealed class LibraryGridViewItem : GridViewItem
{
    private readonly LibraryGridView owner;
    private CheckBox? selectionCheck;
    private bool deselectOnTap;

    internal LibraryGridViewItem(LibraryGridView owner)
    {
        this.owner=owner;
        RegisterPropertyChangedCallback(IsSelectedProperty,(_,_)=>SyncSelectionCheck());
        Unloaded+=(_,_)=>{deselectOnTap=false;owner.ForgetInteraction(Content);};
    }

    private void SyncSelectionCheck()
    {
        if(selectionCheck!=null)selectionCheck.IsChecked=IsSelected;
    }

    protected override void OnApplyTemplate()
    {
        if(selectionCheck!=null)selectionCheck.Click-=SelectionCheck_Click;
        base.OnApplyTemplate();
        selectionCheck=GetTemplateChild("LibrarySelectionToggle") as CheckBox;
        if(selectionCheck==null)return;

        selectionCheck.Visibility=Visibility.Visible;
        // CheckBox writes its own IsChecked value when clicked. Observe the
        // container directly so clearing/recycling selection always wins.
        SyncSelectionCheck();
        selectionCheck.Click+=SelectionCheck_Click;
    }

    protected override void OnPointerPressed(PointerRoutedEventArgs e)
    {
        deselectOnTap=false;
        if(e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) {
            bool modifier=(Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)&Windows.UI.Core.CoreVirtualKeyStates.Down)!=0
                ||(Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)&Windows.UI.Core.CoreVirtualKeyStates.Down)!=0;
            BeginSelectionTap(!modifier,e.OriginalSource as DependencyObject);
            owner.RememberInteraction(owner.ItemFromContainer(this));
        }
        base.OnPointerPressed(e);
    }
    private bool IsSelectionCheckSource(DependencyObject? source)
    {
        for(var node=source;node!=null&&node!=this;node=VisualTreeHelper.GetParent(node))if(node==selectionCheck)return true;
        return false;
    }
    internal void BeginSelectionTap(bool plainClick,DependencyObject? source)=>deselectOnTap=IsSelected&&plainClick&&!IsSelectionCheckSource(source);
    internal bool CompleteSelectionTap(DependencyObject? source)
    {
        bool deselect=deselectOnTap&&!IsSelectionCheckSource(source);deselectOnTap=false;
        if(!deselect)return false;
        IsSelected=false;owner.ForgetInteraction(owner.ItemFromContainer(this));return true;
    }
    protected override void OnTapped(TappedRoutedEventArgs e)
    {
        if(CompleteSelectionTap(e.OriginalSource as DependencyObject)) {e.Handled=true;return;}
        base.OnTapped(e);
    }

    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        // Keyboard selection has its own native anchor/focus; do not reuse a
        // pointer endpoint from a previous Shift-click.
        owner.ClearPointerInteraction();
        deselectOnTap=false;
        base.OnKeyDown(e);
    }

    protected override void OnPointerReleased(PointerRoutedEventArgs e)
    {
        // Retire the endpoint after native release/tap processing. A click that
        // leaves selection unchanged must not prefer this item in a later
        // programmatic change; the revision protects any newer interaction.
        try {base.OnPointerReleased(e);}
        finally {owner.QueueForgetInteraction(owner.ItemFromContainer(this));}
    }

    protected override void OnPointerCanceled(PointerRoutedEventArgs e)
    {
        owner.ForgetInteraction(owner.ItemFromContainer(this));
        deselectOnTap=false;
        base.OnPointerCanceled(e);
    }

    protected override void OnBringIntoViewRequested(BringIntoViewRequestedEventArgs e)
    {
        // Also cover native focus/ScrollIntoView requests after virtualization.
        // A selected card should be visible as soon as the details pane reflows.
        if(IsSelected)e.AnimationDesired=false;
        base.OnBringIntoViewRequested(e);
    }

    private void SelectionCheck_Click(object sender,RoutedEventArgs e)
    {
        owner.RememberInteraction(owner.ItemFromContainer(this));
        try {
            IsSelected=selectionCheck!.IsChecked==true;
            SyncSelectionCheck();
        } finally {owner.ForgetInteraction(owner.ItemFromContainer(this));}
    }
}
