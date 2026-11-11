using System.Windows;
using DiskSpaceAnalyzer.Controls;
using DiskSpaceAnalyzer.Core;
using I18n = DiskSpaceAnalyzer.Core.Localization;

namespace DiskSpaceAnalyzer;

public partial class MainWindow
{
    private AnalysisFilter analysisFilter = AnalysisFilter.All;
    private SearchQueryOptions? builderOptions;
    private string? requestedRoot, requestedQuery;
    private bool suppressExclusionChanged;

    private async void AdvancedSearchClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new AdvancedSearchWindow(current?.Path ?? index?.Root.Path, analysisFilter.ExcludeWindows, builderOptions) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        builderOptions = dialog.Options;
        suppressExclusionChanged = true;
        try { ExcludeWindowsToggle.IsChecked = dialog.ExcludeWindows; }
        finally { suppressExclusionChanged = false; }
        if (analysisFilter.ExcludeWindows != dialog.ExcludeWindows) ResetForExclusion(new(dialog.ExcludeWindows));
        EverythingSearchBox.Text = dialog.SearchQuery;
        await RunSearchAsync(dialog.SearchQuery);
    }

    private async void ExclusionChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || suppressExclusionChanged) return;
        var next = new AnalysisFilter(ExcludeWindowsToggle.IsChecked == true);
        if (next == analysisFilter) return;
        string? root = requestedRoot ?? index?.Root.Path;
        string? query = requestedQuery ?? searchResult?.Query;
        string? previousPath = current?.Path;
        ResetForExclusion(next);
        if (query is not null) await RunSearchAsync(query);
        else if (root is not null)
        {
            await ScanRootAsync(root);
            if (analysisFilter == next && previousPath is not null && !next.IsExcluded(previousPath) && !busy && index?.Nodes.ContainsKey(previousPath) == true)
                await NavigateAsync(previousPath, false);
        }
        else StatusText.Text = next.ExcludeWindows ? I18n.T("Text.2B62C0B6072A") : I18n.T("Text.DD9E4E60D232");
    }

    private void ResetForExclusion(AnalysisFilter next)
    {
        operation?.Cancel(); navigation?.Cancel(); liveOperation?.Cancel();
        operation = navigation = liveOperation = null; generation++; navigationGeneration++;
        monitor?.Dispose(); monitor = null;
        analysisFilter = next; index = null; current = null; searchResult = null; selected = null; entries = []; history.Clear();

        Treemap.SelectedPath = null; Treemap.Entries = []; EntryList.ItemsSource = null; Breadcrumbs.Children.Clear();
        SizeStat.Text = CountStat.Text = "—"; TimeStat.Text = I18n.T("Text.331AA63969CE"); ItemCount.Text = I18n.T("Text.694F17379CF9");
        DetailName.Text = I18n.T("Text.2A1C5D714908"); DetailPath.Text = "";
        EmptyOverlay.Visibility = Visibility.Visible;
        EmptyTitle.Text = I18n.T("Text.4D9832923E3C");
        EmptyDescription.Text = next.ExcludeWindows ? I18n.T("Text.02C6B13049CB") : I18n.T("Text.42A00A382524");
        ScopeLabel.Text = next.ExcludeWindows ? I18n.T("Text.8CF5B1E8EEC8") : I18n.T("Text.DA821BED8F87");
        busy = false; ShowBusy(false);
    }
}
