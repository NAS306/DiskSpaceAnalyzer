using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Interop;
using DiskSpaceAnalyzer.Core;
using DiskSpaceAnalyzer.Controls;
using DiskSpaceAnalyzer.Interop;
using Microsoft.Win32;
using I18n = DiskSpaceAnalyzer.Core.Localization;

namespace DiskSpaceAnalyzer;

public partial class MainWindow : Window
{
    private readonly EverythingClient client = new();
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? operation, navigation, liveOperation;
    private readonly DispatcherTimer liveTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private LiveMonitor? monitor;
    private DirectoryIndex? index;
    private DirectoryNode? current;
    private SearchResults? searchResult;
    private DateTime lastSearch = DateTime.MinValue;
    private List<ViewEntry> entries = [];
    private ViewEntry? selected;
    private readonly Stack<string> history = new();
    private bool busy, refreshing, connected, closed;
    private int generation, navigationGeneration;
    private DateTime lastFullScan = DateTime.MinValue;
    private Task? connecting;
    private double detailsWidth = 310;
    public MainWindow(string? startupRoot = null)
    {
        InitializeComponent();
        InitializeLanguage();
        InitializeWindowPlacement();
        requestedRoot = startupRoot;
        Treemap.PreviewDepth = 1;
        Treemap.EntryClicked += entry => { if (entry.IsFolder) _ = NavigateAsync(entry.Path, true); else SelectEntry(entry); };
        Treemap.EntryHovered += entry => HoverText.Text = entry is null ? I18n.T("Text.4E3A78F4B6F1") : $"{(entry.IsFreeSpace ? entry.Name : entry.Path)}  ·  {entry.SizeText}";
        Treemap.EntryContextRequested += entry => { SelectEntry(entry); ShowEntryContextMenu(Treemap, entry); };
        Treemap.GroupClicked += async entry =>
        {
            if (searchResult is not null)
            {
                if (DetailsPane.Visibility != Visibility.Visible) ToggleDetails();
                StatusText.Text = I18n.F("Text.C20C02BD5AE1", entry.GroupCount);
                return;
            }
            if (current?.Path != entry.Path) await NavigateAsync(entry.Path, true);
            if (DetailsPane.Visibility != Visibility.Visible) ToggleDetails();
            StatusText.Text = I18n.F("Text.E38FB9265568", entry.GroupCount);
        };
        EntryList.PreviewMouseRightButtonUp += (_, e) =>
        {
            if (busy) return;
            if (ItemsControl.ContainerFromElement(EntryList, e.OriginalSource as DependencyObject) is ListViewItem { DataContext: ViewEntry entry } row)
            {
                row.IsSelected = true; SelectEntry(entry); ShowEntryContextMenu(EntryList, entry);
            }
            e.Handled = true;
        };
        liveTimer.Tick += async (_, _) => await PollLiveAsync();
        Loaded += async (_, _) =>
        {
            PopulateDrives();
            connecting = ConnectAsync(lifetime.Token);
            try
            {
                await connecting;
                if (startupRoot is not null && !closed && generation == 0) await ScanRootAsync(startupRoot);
                else if (!busy && index is null && searchResult is null) StatusText.Text = I18n.T("Text.7824F460E049");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { ShowError(ex, true); }
            liveTimer.Start();
        };
        Closed += (_, _) =>
        {
            closed = true; lifetime.Cancel(); operation?.Cancel(); navigation?.Cancel(); liveOperation?.Cancel();
            I18n.LanguageChanged -= RefreshLanguage;
            liveTimer.Stop(); monitor?.Dispose(); client.Dispose();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.E && Keyboard.Modifiers == ModifierKeys.Control) { EverythingSearchBox.Focus(); EverythingSearchBox.SelectAll(); e.Handled = true; }
            else if (e.Key == Key.F5) { _ = RefreshAsync(); e.Handled = true; }
            else if (e.Key == Key.F9) { ToggleDetails(); e.Handled = true; }
            else if (e.Key == Key.F11) { ToggleFullScreen(); e.Handled = true; }
            else if (e.Key == Key.Escape && busy) { operation?.Cancel(); e.Handled = true; }
            else if (e.Key == Key.Escape && isFullScreen) { ToggleFullScreen(); e.Handled = true; }
            else if (e.SystemKey == Key.Left && Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) { GoBack(); e.Handled = true; }
            else if (e.Key == Key.Back && Keyboard.FocusedElement is not TextBox) { GoUp(); e.Handled = true; }
        };
    }
    private async Task ConnectAsync(CancellationToken token)
    {
        connected = false;
        await client.EnsureRunningAsync(new Progress<string>(s => { if (!closed) StatusText.Text = s; }), token);
        connected = true;
    }
    private void ExplorerMenuClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var state = ExplorerIntegration.GetState();
            var menu = new ContextMenu();
            menu.Items.Add(new MenuItem { Header = state.Current ? I18n.T("Text.3074591B0CD9") : state.Any ? I18n.T("Text.0679E29401F0") : I18n.T("Text.84E06797C8BE"), ToolTip = FeatureHelp.ExplorerState, IsEnabled = false });
            var add = new MenuItem { Header = state.Any ? I18n.T("Text.EC970130ED20") : I18n.T("Text.D13C9C11EB99"), ToolTip = FeatureHelp.RegisterExplorer };
            add.Click += (_, _) => ChangeExplorerMenu(true);
            var remove = new MenuItem { Header = I18n.T("Text.4A11D2097C62"), ToolTip = FeatureHelp.UnregisterExplorer, IsEnabled = state.Any };
            remove.Click += (_, _) => ChangeExplorerMenu(false);
            menu.Items.Add(add); menu.Items.Add(remove);
            menu.PlacementTarget = (Button)sender; menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }
        catch (Exception ex) { StatusText.Text = I18n.F("Text.AD4E83C78737", ex.Message); }
    }
    private void ChangeExplorerMenu(bool add)
    {
        try
        {
            if (add) ExplorerIntegration.Register(); else ExplorerIntegration.Unregister();
            StatusText.Text = add
                ? I18n.T("Text.D2A534AFE5DE")
                : I18n.T("Text.07AD25EFD63B");
        }
        catch (Exception ex) { StatusText.Text = I18n.F("Text.DDB3B18D596F", ex.Message); }
    }
    private void PopulateDrives()
    {
        DrivePanel.Children.Clear();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady || drive.DriveType == DriveType.CDRom) continue;
                string root = drive.RootDirectory.FullName;
                var button = new Button
                {
                    Content = $"▤  {drive.Name}", FontSize = 11,
                    Margin = new Thickness(0, 0, 2, 0), Padding = new Thickness(6, 2, 6, 2),
                    ToolTip = I18n.F("Text.3A9128755D04", FeatureHelp.ChooseDrive, root, drive.VolumeLabel, Format.Bytes(drive.TotalFreeSpace), Format.Bytes(drive.TotalSize))
                };
                button.Click += async (_, _) => await ScanRootAsync(root);
                DrivePanel.Children.Add(button);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
    private async Task ScanRootAsync(string root)
    {
        var filter = analysisFilter; requestedRoot = root; requestedQuery = null;
        operation?.Cancel(); navigation?.Cancel(); liveOperation?.Cancel(); generation++; navigationGeneration++;
        int version = generation;
        var cts = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        operation = cts; busy = true;
        string? previousPath = index?.Root.Path.Equals(DirectoryIndex.Normalize(root), StringComparison.OrdinalIgnoreCase) == true ? current?.Path : null;
        monitor?.Dispose(); monitor = null;
        ShowBusy(true); ScopeLabel.Text = root;
        ScanProgress.IsIndeterminate = true;
        if (index is null && searchResult is null) { EmptyTitle.Text = I18n.T("Text.8DC8DBA1DB0F"); EmptyDescription.Text = I18n.T("Text.7398D4628D54"); }
        var clock = Stopwatch.StartNew();
        try
        {
            if (connecting is { IsCompleted: false }) await connecting.WaitAsync(cts.Token);
            if (!connected) await ConnectAsync(cts.Token);
            // Start watching before the scan so writes during scanning are replayed.
            var newMonitor = new LiveMonitor(root);
            monitor = newMonitor;
            StatusText.Text = I18n.T("Text.6C4324119377");
            var progress = new Progress<(long Done, long Total)>(p =>
            {
                if (version != generation || closed) return;
                ScanProgress.IsIndeterminate = false; ScanProgress.Maximum = Math.Max(1, p.Total); ScanProgress.Value = p.Done;
                StatusText.Text = I18n.F("Text.64853FC0227F", p.Done, p.Total, clock.Elapsed.TotalSeconds);
            });
            var scan = await client.ScanAsync(root, progress, cts.Token, filter);
            cts.Token.ThrowIfCancellationRequested();
            if (version != generation) return;
            searchResult = null;
            DetailsHeading.Text = I18n.T("Text.CE7E399B90FA");
            index = scan; history.Clear();
            current = previousPath is not null && scan.Nodes.TryGetValue(previousPath, out var previous) ? previous : scan.Root;
            lastFullScan = DateTime.UtcNow;
            TimeStat.Text = I18n.F("Text.A0CDF56961C1", clock.Elapsed.TotalSeconds);
            TimeStat.Tag = clock.Elapsed;
            EmptyOverlay.Visibility = Visibility.Collapsed;
            await NavigateAsync(current.Path, false);
            if (version != generation || closed) return;
            StatusText.Text = ScanStatus(clock.Elapsed);
            Subtitle.Text = scan.Root.Unknown > 0 ? I18n.F("Text.E2008153E7B8", scan.Root.Unknown) : I18n.T("Text.873D5423331C");
            if (scan.Root.Files == 0 && scan.Root.Folders == 0)
            {
                StatusText.Text = filter.IsExcluded(root) ? I18n.T("Text.A05FCBF842EA") : I18n.T("Text.19A9EB6E6DB8");
                if (filter.IsExcluded(root))
                {
                    EmptyOverlay.Visibility = Visibility.Visible;
                    EmptyTitle.Text = I18n.T("Text.DF2203628E4A");
                    EmptyDescription.Text = I18n.T("Text.ECC00219B361");
                }
            }
        }
        catch (OperationCanceledException)
        {
            if (version == generation)
            {
                StatusText.Text = I18n.T("Text.151FDA219528");
                if (index is null && searchResult is null) { EmptyTitle.Text = I18n.T("Text.6A7A715E9BF4"); EmptyDescription.Text = I18n.T("Text.67047C3309EA"); }
                RestoreMonitor(); ScopeLabel.Text = index?.Root.Path ?? (searchResult is { } search ? I18n.F("Text.E8C1FF2E4479", search.Query) : I18n.T("Text.BF3C604213F2"));
            }
        }
        catch (Exception ex)
        {
            if (version == generation) { ShowError(ex, index is null && searchResult is null); RestoreMonitor(); ScopeLabel.Text = index?.Root.Path ?? (searchResult is { } search ? I18n.F("Text.E8C1FF2E4479", search.Query) : root); }
        }
        finally
        {
            if (version == generation) { busy = false; ShowBusy(false); operation = null; }
            cts.Dispose();
        }
    }
    private string ScanStatus(TimeSpan elapsed) => index is null ? I18n.T("Text.647E214CD12E") : I18n.F("Text.448F95296E7F", index.Root.Files + index.Root.Folders, Format.Bytes(index.Root.Bytes), elapsed.TotalSeconds, LiveStatus());
    private string LiveStatus() => (LiveToggle.IsChecked != true ? I18n.T("Text.36E3F1089944") : searchResult is not null ? I18n.T("Text.1F6E267DCCAE") : monitor?.IsWatching == true ? I18n.T("Text.A5D7CE897E87") : I18n.T("Text.DC90D6359671")) + (analysisFilter.ExcludeWindows ? I18n.T("Text.1510EE106292") : "");
    private void RestoreMonitor()
    {
        monitor?.Dispose(); monitor = searchResult is not null || index is null ? null : new LiveMonitor(index.Root.Path);
    }
    private void ShowBusy(bool value)
    {
        ScanProgress.Visibility = CancelButton.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        UpButton.IsEnabled = !value && current?.Parent is not null;
        BackButton.IsEnabled = !value && history.Count > 0;
        EntryList.IsEnabled = Treemap.IsEnabled = !value;
    }
    private async Task NavigateAsync(string path, bool record, bool preserveView = false)
    {
        if (index is null || !index.Nodes.TryGetValue(path, out var target) || closed) return;
        var filter = analysisFilter;
        navigation?.Cancel();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); navigation = cts;
        int navVersion = ++navigationGeneration, scopeVersion = generation;
        if (record && current is not null && current.Path != path) history.Push(current.Path);
        string? previousSelection = preserveView ? selected?.Path : null;
        current = target;
        if (!preserveView) { selected = null; Treemap.SelectedPath = null; FilterBox.Text = ""; }
        UpdateBreadcrumbs(); UpdateStatistics();
        entries = target.Children.Values.Select(c => new ViewEntry(c.Name, c.Path, c.Bytes, true, c.Files, c)).OrderByDescending(c => c.Bytes).ToList();
        ApplyFilter();
        if (previousSelection is null) { DetailName.Text = target.Name; DetailPath.Text = target.Path; }
        UpButton.IsEnabled = target.Parent is not null; BackButton.IsEnabled = history.Count > 0;
        try
        {
            var files = await client.ReadFilesAsync(path, cts.Token, filter);
            if (cts.IsCancellationRequested || navVersion != navigationGeneration || scopeVersion != generation || current?.Path != path) return;
            entries = entries.Concat(files).OrderByDescending(c => c.Bytes).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
            ApplyFilter();
            if (previousSelection is not null)
            {
                var updated = entries.FirstOrDefault(e => e.Path.Equals(previousSelection, StringComparison.OrdinalIgnoreCase));
                if (updated is not null) SelectEntry(updated);
                else { selected = null; Treemap.SelectedPath = null; DetailName.Text = target.Name; DetailPath.Text = target.Path; }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (navVersion == navigationGeneration && !closed) StatusText.Text = I18n.F("Text.F690664F0423", ex.Message); }
        finally { if (ReferenceEquals(navigation, cts)) navigation = null; cts.Dispose(); }
    }
    private void UpdateStatistics()
    {
        if (current is null) return;
        UpdateCapacityDisplay(); CountStat.Text = $"{current.Files:N0} / {current.Folders:N0}";
        Title = $"DiskSpace — {current.Path}";
    }
    private void UpdateBreadcrumbs()
    {
        Breadcrumbs.Children.Clear();
        var chain = new Stack<DirectoryNode>();
        for (var n = current; n is not null; n = n.Parent) chain.Push(n);
        while (chain.TryPop(out var node))
        {
            if (Breadcrumbs.Children.Count > 0) Breadcrumbs.Children.Add(new TextBlock { Text = " › ", VerticalAlignment = VerticalAlignment.Center, Foreground = System.Windows.Media.Brushes.SlateGray, Margin = new Thickness(4, 0, 4, 0) });
            var button = new Button { Content = node.Name, Padding = new Thickness(6, 2, 6, 2), Background = System.Windows.Media.Brushes.Transparent, BorderBrush = System.Windows.Media.Brushes.Transparent, ToolTip = $"{FeatureHelp.Breadcrumb}\n\n{node.Path}" };
            string path = node.Path; button.Click += async (_, _) => { if (!busy) await NavigateAsync(path, true); };
            Breadcrumbs.Children.Add(button);
        }
    }
    private void ApplyFilter()
    {
        if (Treemap is null || EntryList is null) return;
        Treemap.FolderPath = current?.Path ?? "";
        string filter = FilterBox.Text.Trim();
        long totalBytes = searchResult?.Bytes ?? current?.Bytes ?? 0;
        var visible = entries.Where(x => filter.Length == 0 || x.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .Select(x => x with { PercentText = totalBytes > 0 ? $"{100.0 * x.Bytes / totalBytes:0.0}%" : "0%" }).ToArray();
        Treemap.Entries = visible;
        UpdateCapacityDisplay();
        EntryList.ItemsSource = visible;
        ItemCount.Text = filter.Length == 0 ? I18n.F("Text.6289CD01BC45", visible.Length) : I18n.F("Text.2EC4DBFA1323", visible.Length, entries.Count);
        HoverText.Text = searchResult is not null ? I18n.T("Text.5A8B58A44E8B") : filter.Length > 0 ? I18n.T("Text.94556B9CFA8C") : I18n.T("Text.4E3A78F4B6F1");
    }
    private void SelectEntry(ViewEntry entry)
    {
        selected = entry; DetailName.Text = $"{entry.Name}  ·  {entry.SizeText}"; DetailPath.Text = entry.Path;
        Treemap.SelectedPath = entry.Path; Treemap.InvalidateVisual();
    }
    private async Task PollLiveAsync()
    {
        if (!closed && !busy && !refreshing && LiveToggle.IsChecked == true && DateTime.UtcNow - lastCapacityRefresh >= TimeSpan.FromSeconds(5))
            UpdateCapacityDisplay();
        if (searchResult is { } search)
        {
            if (!closed && !busy && !refreshing && LiveToggle.IsChecked == true && connected && DateTime.UtcNow - lastSearch >= TimeSpan.FromSeconds(5))
                await RunSearchAsync(search.Query, true);
            return;
        }
        if (closed || busy || refreshing || index is null || monitor is null || LiveToggle.IsChecked != true || !connected) return;
        var filter = analysisFilter;
        bool full = monitor.NeedsFullRefresh || DateTime.UtcNow - lastFullScan > TimeSpan.FromSeconds(60);
        string[] paths = monitor.Drain(full);
        if (paths.Length == 0) return;
        refreshing = true; int version = generation;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        liveOperation = cts;
        try
        {
            var processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in paths)
            {
                if (cts.IsCancellationRequested || closed || version != generation || index is null) return;
                if (full)
                {
                    var update = await client.ScanAsync(index.Root.Path, null, cts.Token, filter);
                    if (closed || version != generation) return;
                    index = update; lastFullScan = DateTime.UtcNow; break;
                }
                else
                {
                    string existing = path;
                    while (!index.Nodes.ContainsKey(existing)) existing = System.IO.Path.GetDirectoryName(existing) ?? index.Root.Path;
                    if (!processed.Add(existing)) continue;
                    var slice = await client.ReadDirectoryAsync(existing, cts.Token, filter);
                    if (closed || version != generation) return;
                    var added = new List<DirectoryIndex>();
                    foreach (string folder in slice.Folders)
                    {
                        if (!index.Nodes.ContainsKey(folder)) added.Add(await client.ScanAsync(folder, null, cts.Token, filter));
                        if (closed || version != generation) return;
                    }
                    index.ReconcileDirectory(existing, slice, added);
                }
            }
            if (version != generation || index is null) return;
            string target = current?.Path ?? index.Root.Path;
            while (!index.Nodes.ContainsKey(target)) target = System.IO.Path.GetDirectoryName(target) ?? index.Root.Path;
            await NavigateAsync(target, false, target == current?.Path);
            if (version != generation || closed) return;
            StatusText.Text = I18n.F("Text.F2FB86FAC219", DateTime.Now, index.Root.Files + index.Root.Folders, LiveStatus());
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (version == generation && !closed) { monitor?.Retry(paths); StatusText.Text = I18n.F("Text.EA14C0451805", ex.Message); }
        }
        finally { refreshing = false; if (ReferenceEquals(liveOperation, cts)) liveOperation = null; }
    }
    private void ShowError(Exception ex, bool overlay)
    {
        if (closed) return;
        StatusText.Text = ex.Message;
        if (overlay) { EmptyOverlay.Visibility = Visibility.Visible; EmptyTitle.Text = I18n.T("Text.F2CB550128C4"); EmptyDescription.Text = ex.Message; }
    }
    private async void ChooseFolderClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = I18n.T("Text.E9D05CBF94BD"), Multiselect = false };
        if (dialog.ShowDialog(this) == true) await ScanRootAsync(dialog.FolderName);
    }
    private Task RefreshAsync() => searchResult is { } search ? RunSearchAsync(search.Query) : index is not null ? ScanRootAsync(index.Root.Path) : requestedQuery is { } query ? RunSearchAsync(query) : requestedRoot is { } root ? ScanRootAsync(root) : RetryConnectAsync();
    private async Task RetryConnectAsync()
    {
        try { await ConnectAsync(lifetime.Token); StatusText.Text = I18n.T("Text.7824F460E049"); }
        catch (OperationCanceledException) { } catch (Exception ex) { ShowError(ex, true); }
    }
    private async void RefreshClicked(object sender, RoutedEventArgs e) => await RefreshAsync();
    private void GoUp() { if (!busy && current?.Parent is { } parent) _ = NavigateAsync(parent.Path, true); }
    private void GoBack()
    {
        if (busy || index is null) return;
        while (history.TryPop(out var path)) if (index.Nodes.ContainsKey(path)) { _ = NavigateAsync(path, false); return; }
        BackButton.IsEnabled = false;
    }
    private void BackClicked(object sender, RoutedEventArgs e) => GoBack();
    private void ToggleDetails()
    {
        bool show = DetailsPane.Visibility != Visibility.Visible;
        if (!show) detailsWidth = DetailsColumn.ActualWidth;
        DetailsColumn.MinWidth = show ? 260 : 0;
        DetailsColumn.MaxWidth = show ? 420 : double.PositiveInfinity;
        DetailsColumn.Width = new GridLength(show ? detailsWidth : 0);
        SplitterColumn.Width = new GridLength(show ? 8 : 0);
        DetailsPane.Visibility = PaneSplitter.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        DetailsToggleButton.Content = show ? I18n.T("Text.06AAB2473E72") : I18n.T("Text.856901810098");
    }
    private void ToggleDetailsClicked(object sender, RoutedEventArgs e) => ToggleDetails();
    private void UpClicked(object sender, RoutedEventArgs e) => GoUp();
    private void CancelClicked(object sender, RoutedEventArgs e) => operation?.Cancel();
    private void DepthChanged(object sender, RoutedEventArgs e) { if (Treemap is not null && sender is RadioButton { Tag: string depth }) { Treemap.PreviewDepth = int.Parse(depth); Treemap.InvalidateVisual(); } }
    private void FilterChanged(object sender, TextChangedEventArgs e) { if (IsLoaded) ApplyFilter(); }
    private void LiveChanged(object sender, RoutedEventArgs e) { if (IsLoaded) StatusText.Text = LiveStatus(); }
    private void EntrySelected(object sender, SelectionChangedEventArgs e) { if (EntryList.SelectedItem is ViewEntry entry) SelectEntry(entry); }
    private async void EntryListClicked(object sender, MouseButtonEventArgs e)
    {
        if (busy || e.ChangedButton != MouseButton.Left) return;
        if (ItemsControl.ContainerFromElement(EntryList, e.OriginalSource as DependencyObject) is ListViewItem { DataContext: ViewEntry { IsFolder: true } entry })
        { e.Handled = true; await NavigateAsync(entry.Path, true); }
    }
    private void CopyPathClicked(object sender, RoutedEventArgs e)
    {
        string? path = selected?.Path ?? current?.Path;
        if (path is null) return;
        try { Clipboard.SetText(path); StatusText.Text = I18n.T("Text.4823541F6727"); } catch (Exception ex) { StatusText.Text = ex.Message; }
    }
    private void OpenExplorerClicked(object sender, RoutedEventArgs e)
    {
        OpenInExplorer(selected?.Path ?? current?.Path);
    }
    private void ShowEntryContextMenu(FrameworkElement owner, ViewEntry entry)
    {
        if (entry.IsGroup) return;
        var menu = EntryContextMenu.Create(entry, (target, command) =>
        {
            try
            {
                switch (command)
                {
                    case EntryContextMenu.Command.Reveal: OpenInExplorer(target.Path); break;
                    case EntryContextMenu.Command.CopyPath: Clipboard.SetText(target.Path); StatusText.Text = I18n.T("Text.4823541F6727"); break;
                    case EntryContextMenu.Command.Open: Process.Start(new ProcessStartInfo(target.Path) { UseShellExecute = true, Verb = "open" }); break;
                    case EntryContextMenu.Command.Properties: WindowsShell.ShowProperties(new WindowInteropHelper(this).Handle, target.Path); break;
                }
            }
            catch (Exception ex) { StatusText.Text = ex.Message; }
        });
        menu.PlacementTarget = owner; menu.Placement = PlacementMode.MousePoint;
        owner.ContextMenu = menu; menu.IsOpen = true;
    }
    private void OpenInExplorer(string? path)
    {
        if (path is null) return;
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true }); }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }
}
