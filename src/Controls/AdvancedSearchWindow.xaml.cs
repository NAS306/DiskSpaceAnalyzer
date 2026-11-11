using System.IO;
using System.Windows;
using DiskSpaceAnalyzer.Core;
using Microsoft.Win32;
using I18n = DiskSpaceAnalyzer.Core.Localization;

namespace DiskSpaceAnalyzer.Controls;

public partial class AdvancedSearchWindow : Window
{
    private static readonly string[] Comparisons = ["", ">", ">=", "<", "<=", "="];
    private bool ready;
    public string SearchQuery { get; private set; } = "";
    public SearchQueryOptions Options { get; private set; }
    public bool ExcludeWindows => ExcludeWindowsBox.IsChecked == true;

    public AdvancedSearchWindow(string? currentFolder, bool excludeWindows, SearchQueryOptions? previous = null)
    {
        Options = previous ?? new();
        InitializeComponent();
        MaxHeight = Math.Max(350, SystemParameters.WorkArea.Height - 24);
        ExtensionsBox.ItemsSource = new[] { "*", "zip", "zip;7z;rar", "jpg;jpeg;png;webp", "mp4;mkv;avi;mov", "mp3;flac;wav", "pdf;docx;xlsx;pptx", "exe;msi;iso" };
        ComparisonBox.ItemsSource = new[] { I18n.T("Text.0D10AEF3DA14"), I18n.T("Text.1E72EA7BA0B5"), I18n.T("Text.3DB696DF05CB"), I18n.T("Text.973314B48FF1"), I18n.T("Text.600051253D63"), I18n.T("Text.A3B0C8A35AB1") };
        UnitBox.ItemsSource = new[] { "B", "KB", "MB", "GB", "TB" };
        ScopeBox.ItemsSource = new[] { I18n.T("Text.8CC6583D730A"), I18n.T("Text.24C18E752C4C"), I18n.T("Text.2E7942FA0D82") };
        DriveBox.ItemsSource = DriveInfo.GetDrives().Where(d => { try { return d.IsReady; } catch (IOException) { return false; } }).Select(d => d.Name).ToArray();
        DriveBox.SelectedIndex = 0;
        FolderBox.Text = Options.ScopePath ?? currentFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        ExtensionsBox.Text = Options.Extensions;
        ComparisonBox.SelectedIndex = Math.Max(0, Array.IndexOf(Comparisons, Options.Comparison));
        SizeBox.Text = Options.Size; UnitBox.SelectedItem = Options.Unit.ToUpperInvariant();
        ScopeBox.SelectedIndex = Options.ScopePath is null ? 0 : 2;
        ExcludeWindowsBox.IsChecked = excludeWindows;
        ready = true; UpdatePreview();
    }

    private SearchQueryOptions ReadOptions() => new(ExtensionsBox.Text,
        Comparisons[Math.Max(0, ComparisonBox.SelectedIndex)], SizeBox.Text, UnitBox.SelectedItem as string ?? "MB",
        ScopeBox.SelectedIndex switch { 1 => DriveBox.SelectedItem as string ?? throw new ArgumentException(I18n.T("Text.173A091F5DEB")), 2 => string.IsNullOrWhiteSpace(FolderBox.Text) ? throw new ArgumentException(I18n.T("Text.FC7E1B9BE08E")) : FolderBox.Text, _ => null });

    private void ConditionChanged(object sender, RoutedEventArgs e) { if (ready) UpdatePreview(); }

    private void UpdatePreview()
    {
        DriveBox.Visibility = ScopeBox.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        FolderPanel.Visibility = ScopeBox.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        SizeBox.IsEnabled = UnitBox.IsEnabled = ComparisonBox.SelectedIndex > 0;
        ExclusionNote.Text = ExcludeWindows ? I18n.T("Text.70C8EBBF7C5A") : "";
        try
        {
            SearchQuery = SearchQueryBuilder.Build(ReadOptions());
            QueryPreview.Text = SearchQuery; ErrorText.Text = ""; SearchButton.IsEnabled = true;
        }
        catch (ArgumentException ex) { QueryPreview.Text = ""; ErrorText.Text = ex.Message; SearchButton.IsEnabled = false; }
    }

    private void BrowseClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = I18n.T("Text.AC21369A20D1"), Multiselect = false };
        if (dialog.ShowDialog(this) == true) FolderBox.Text = dialog.FolderName;
    }

    private void SearchClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var options = ReadOptions();
            if (options.ScopePath is { } path && !Directory.Exists(path)) throw new ArgumentException(I18n.T("Text.F885177669C0"));
            SearchQuery = SearchQueryBuilder.Build(options); Options = options;
            DialogResult = true;
        }
        catch (ArgumentException ex) { ErrorText.Text = ex.Message; }
    }
}
