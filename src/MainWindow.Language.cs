using Localization = DiskSpaceAnalyzer.Core.Localization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using DiskSpaceAnalyzer.Core;
using DiskSpaceAnalyzer.Interop;

namespace DiskSpaceAnalyzer;

public partial class MainWindow
{
    private void InitializeLanguage()
    {
        LanguageButton.Content = Localization.Language.ToUpperInvariant() + " ▾";
        Localization.LanguageChanged += RefreshLanguage;
    }
    private void LanguageClicked(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = LanguageButton, Placement = PlacementMode.Bottom };
        foreach (var (code, name) in new[] { ("ko", "한국어"), ("en", "English") })
        {
            var item = new MenuItem { Header = name, IsCheckable = true, IsChecked = Localization.Language == code, ToolTip = name };
            item.Click += (_, _) => Localization.SetLanguage(code);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }
    private void RefreshLanguage()
    {
        LanguageButton.Content = Localization.Language.ToUpperInvariant() + " ▾";
        FullScreenButton.Content = Localization.T(isFullScreen ? "Text.1D287C2B2B13" : "Text.451354ACB402");
        DetailsToggleButton.Content = Localization.T(DetailsPane.Visibility == Visibility.Visible ? "Text.06AAB2473E72" : "Text.856901810098");
        PopulateDrives();
        if (searchResult is { } search)
        {
            Breadcrumbs.Children.Clear();
            Breadcrumbs.Children.Add(new TextBlock { Text = Localization.T("Text.BE88EA441E38"), VerticalAlignment = VerticalAlignment.Center });
            Title = Localization.F("Text.4EC2563DDEFC", search.Query);
            ScopeLabel.Text = Localization.F("Text.E8C1FF2E4479", search.Query);
            DetailsHeading.Text = Localization.T("Text.F39DB5A38CA6");
            Subtitle.Text = Localization.T("Text.62E385AA6417");
            SizeStat.Text = Format.Bytes(search.Bytes); CountStat.Text = $"{search.Files.Count:N0} / 0";
            EmptyTitle.Text = Localization.T(search.Files.Count == 0 ? "Text.E2D351A4A4F5" : "Text.D52A3FD3EDF6");
            EmptyDescription.Text = Localization.T(search.Files.Count == 0 ? "Text.2EA4368A0947" : "Text.2002229BA8CE");
            if (selected is null) { DetailName.Text = Localization.T("Text.7B003A42ADFB"); DetailPath.Text = search.Query; }
        }
        else if (current is not null)
        {
            UpdateStatistics(); UpdateBreadcrumbs();
            DetailsHeading.Text = Localization.T("Text.CE7E399B90FA");
            Subtitle.Text = index!.Root.Unknown > 0 ? Localization.F("Text.E2008153E7B8", index.Root.Unknown) : Localization.T("Text.873D5423331C");
            if (analysisFilter.IsExcluded(index.Root.Path))
            {
                EmptyTitle.Text = Localization.T("Text.DF2203628E4A");
                EmptyDescription.Text = Localization.T("Text.ECC00219B361");
            }
        }
        if (TimeStat.Tag is TimeSpan elapsed) TimeStat.Text = Localization.F("Text.A0CDF56961C1", elapsed.TotalSeconds);
        ApplyFilter();
        if (selected is not null) SelectEntry(selected);
        if (!busy) StatusText.Text = searchResult is { } result ? SearchStatus(result) : index is not null ? ScanStatus(TimeStat.Tag is TimeSpan scanTime ? scanTime : TimeSpan.Zero) : Localization.T("Text.7824F460E049");
        try { if (ExplorerIntegration.GetState().Current) ExplorerIntegration.Register(); } catch { /* UI language remains usable without shell access. */ }
    }
}
