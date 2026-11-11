using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DiskSpaceAnalyzer.Core;
using I18n = DiskSpaceAnalyzer.Core.Localization;

namespace DiskSpaceAnalyzer;

public partial class MainWindow
{
    private async void SearchClicked(object sender, RoutedEventArgs e) => await RunSearchAsync(EverythingSearchBox.Text);

    private async void EverythingSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await RunSearchAsync(EverythingSearchBox.Text);
    }

    private async Task RunSearchAsync(string query, bool automatic = false)
    {
        query = query.Trim();
        if (query.Length == 0) { StatusText.Text = I18n.T("Text.83965D24863A"); EverythingSearchBox.Focus(); return; }
        if (closed || automatic && (busy || refreshing)) return;
        var filter = analysisFilter; requestedQuery = query; requestedRoot = null;
        operation?.Cancel(); navigation?.Cancel(); liveOperation?.Cancel();
        int version = ++generation; navigationGeneration++;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        operation = cts; busy = true;
        if (!automatic)
        {
            ShowBusy(true); ScanProgress.IsIndeterminate = true;
            StatusText.Text = I18n.F("Text.67898A75FD60", query);
        }
        var clock = Stopwatch.StartNew();
        try
        {
            if (connecting is { IsCompleted: false }) await connecting.WaitAsync(cts.Token);
            if (!connected) await ConnectAsync(cts.Token);
            var progress = automatic ? null : new Progress<(long Done, long Total)>(p =>
            {
                if (closed || version != generation) return;
                ScanProgress.IsIndeterminate = false; ScanProgress.Maximum = Math.Max(1, p.Total); ScanProgress.Value = p.Done;
                StatusText.Text = I18n.F("Text.44B2D9AB949C", p.Done, p.Total, clock.Elapsed.TotalSeconds);
            });
            var result = await client.SearchAsync(query, progress, cts.Token, filter);
            cts.Token.ThrowIfCancellationRequested();
            if (closed || version != generation) return;
            bool changed = searchResult is null || searchResult.Query != query || !result.SameResults(searchResult);
            string? previousSelection = selected?.Path;
            bool newQuery = searchResult?.Query != query;
            monitor?.Dispose(); monitor = null; index = null; current = null; history.Clear();
            if (changed || !automatic) searchResult = result;
            if (changed || !automatic)
            {
                entries = result.Files; selected = null; Treemap.SelectedPath = null;
                if (newQuery) FilterBox.Text = "";
                Breadcrumbs.Children.Clear();
                Breadcrumbs.Children.Add(new TextBlock { Text = I18n.T("Text.BE88EA441E38"), VerticalAlignment = VerticalAlignment.Center });
                Title = I18n.F("Text.4EC2563DDEFC", query);
                ScopeLabel.Text = I18n.F("Text.E8C1FF2E4479", query);
                SizeStat.Text = Format.Bytes(result.Bytes); CountStat.Text = $"{result.Files.Count:N0} / 0";
                DetailsHeading.Text = I18n.T("Text.F39DB5A38CA6");
                Subtitle.Text = I18n.T("Text.62E385AA6417");
                DetailName.Text = I18n.T("Text.7B003A42ADFB"); DetailPath.Text = query;
                ApplyFilter();
                var matching = previousSelection is null ? null : entries.FirstOrDefault(x => x.Path.Equals(previousSelection, StringComparison.OrdinalIgnoreCase));
                if (matching is not null) SelectEntry(matching);
                EmptyOverlay.Visibility = result.Bytes > 0 ? Visibility.Collapsed : Visibility.Visible;
                EmptyTitle.Text = result.Files.Count == 0 ? I18n.T("Text.E2D351A4A4F5") : I18n.T("Text.D52A3FD3EDF6");
                EmptyDescription.Text = result.Files.Count == 0
                    ? I18n.T("Text.2EA4368A0947")
                    : I18n.T("Text.2002229BA8CE");
                if (!automatic && result.Files.Count > 0 && result.Bytes == 0 && DetailsPane.Visibility != Visibility.Visible) ToggleDetails();
            }
            TimeStat.Text = I18n.F("Text.A0CDF56961C1", clock.Elapsed.TotalSeconds);
            TimeStat.Tag = clock.Elapsed;
            StatusText.Text = SearchStatus(result);
        }
        catch (OperationCanceledException)
        {
            if (!closed && version == generation) StatusText.Text = I18n.T("Text.C2D01E8455BE");
        }
        catch (Exception ex)
        {
            if (!closed && version == generation) ShowError(ex, index is null && searchResult is null);
        }
        finally
        {
            if (version == generation)
            {
                lastSearch = DateTime.UtcNow; busy = false; ShowBusy(false); operation = null;
            }
        }
    }

    private string SearchStatus(SearchResults result) =>
        I18n.F("Text.0B97D93F838D", result.Files.Count, Format.Bytes(result.Bytes)) +
        (result.Unknown > 0 ? I18n.F("Text.A9B1D56CAFF6", result.Unknown) : "") +
        (result.ExcludedFolders > 0 ? I18n.F("Text.10A065574FBE", result.ExcludedFolders) : "") +
        $" · {LiveStatus()}";
}
