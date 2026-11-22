using Microsoft.UI.Xaml.Controls;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private Grid LibraryFilterGrid => LibraryView.LibraryFilterGrid;
    private ComboBox LibraryFolderPicker => LibraryView.LibraryFolderPicker;
    private Grid LibraryFilterActions => LibraryView.LibraryFilterActions;
    private CheckBox IncludeSubfoldersCheck => LibraryView.IncludeSubfoldersCheck;
    private Button RefreshFolderButton => LibraryView.RefreshFolderButton;
    private Button RemoveFolderButton => LibraryView.RemoveFolderButton;
    private TextBlock FolderStatus => LibraryView.FolderStatus;
    private Grid LibraryWorkspace => LibraryView.LibraryWorkspace;
    private ColumnDefinition LibraryInfoColumn => LibraryView.LibraryInfoColumn;
    private LibraryGridView LibraryList => LibraryView.LibraryList;
    private ContentControl LibraryEmptyState => LibraryView.LibraryEmptyState;
    private TextBlock EmptyLibrary => LibraryView.EmptyLibrary;
    private Button LibraryEmptyImportButton => LibraryView.LibraryEmptyImportButton;
    private ContentControl LibraryInformationPanel => LibraryView.LibraryInformationPanel;
    private Border LibraryInfoPosterFrame => LibraryView.LibraryInfoPosterFrame;
    private Image LibraryInfoPoster => LibraryView.LibraryInfoPoster;
    private TextBlock LibraryInfoTitle => LibraryView.LibraryInfoTitle;
    private TextBlock LibraryInfoMetadata => LibraryView.LibraryInfoMetadata;
    private TextBlock LibraryInfoPath => LibraryView.LibraryInfoPath;
    private StackPanel LibrarySelectionActions => LibraryView.LibrarySelectionActions;
    private Button LibraryRevealSourceButton => LibraryView.LibraryRevealSourceButton;
    private Button LibraryApplyButton => LibraryView.LibraryApplyButton;
    private Button LibraryQueueButton => LibraryView.LibraryQueueButton;
    private Button LibraryPosterButton => LibraryView.LibraryPosterButton;
    private Button LibraryDecodeTestButton => LibraryView.LibraryDecodeTestButton;
    private Button LibraryRelinkButton => LibraryView.LibraryRelinkButton;
    private Button LibraryRemoveButton => LibraryView.LibraryRemoveButton;
    private Button LibraryDeleteSourceButton => LibraryView.LibraryDeleteSourceButton;
    private void AttachLibraryPage()
    {
        InitializeLibraryFilters();
        LibraryView.ActiveChanged+=active=> {
            libraryCacheIdle.Stop();
            if(active)return;
            librarySearchDelay.Stop();libraryCacheIdle.Start();
            libraryRefreshCancellation?.Cancel();
            LibraryView.NeedsRefresh=true;
        };
        LibraryView.LibraryFolderPicker.SelectionChanged += LibraryFolder_Changed;
        LibraryView.RefreshFolderButton.Click += RefreshFolder_Click;
        LibraryView.RemoveFolderButton.Click += RemoveFolder_Click;
        LibraryView.LibraryList.SelectionChanged += Library_Changed;
        LibraryView.LibraryList.SizeChanged += LibraryGrid_SizeChanged;
        LibraryView.LibraryEmptyImportButton.Click += Import_Click;
        LibraryView.LibraryRevealSourceButton.Click += LibraryRevealSource_Click;
        LibraryView.LibraryApplyButton.Click += SelectForApply_Click;
        LibraryView.LibraryQueueButton.Click += PlaybackAddSelected_Click;
        LibraryView.LibraryPosterButton.Click += LibraryPoster_Click;
        LibraryView.LibraryDecodeTestButton.Click += LibraryDecodeTest_Click;
        LibraryView.LibraryRelinkButton.Click += Relink_Click;
        LibraryView.LibraryRemoveButton.Click += Remove_Click;
        LibraryView.LibraryDeleteSourceButton.Click += LibraryDeleteSource_Click;
    }
}
