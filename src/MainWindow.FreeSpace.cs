using System.Windows;
using DiskSpaceAnalyzer.Core;
using Localization = DiskSpaceAnalyzer.Core.Localization;

namespace DiskSpaceAnalyzer;

public partial class MainWindow
{
    private DateTime lastCapacityRefresh;
    private void FreeSpaceChanged(object sender, RoutedEventArgs e)
    {
        if (Treemap is not null) UpdateCapacityDisplay();
    }
    private void UpdateCapacityDisplay()
    {
        if (FreeSpaceToggle is null || Treemap is null || SizeLabel is null) return;
        lastCapacityRefresh = DateTime.UtcNow;
        var capacity = searchResult is null && current is not null
            ? DriveCapacity.Read(current.Path, index?.ExcludedBytes ?? 0) : null;
        bool show = capacity is not null && string.IsNullOrWhiteSpace(FilterBox.Text);
        FreeSpaceToggle.IsEnabled = show;
        Treemap.Capacity = show && FreeSpaceToggle.IsChecked == true ? capacity : null;
        SizeLabel.Text = Localization.T(capacity is not null ? "Space.UsedTotal" : "Text.E6AC1362406C");
        if (capacity is not null)
        {
            SizeStat.Text = capacity.Label;
            SizeStat.ToolTip = Localization.F("Space.CapacityHelp", Format.Bytes(capacity.UsedBytes), Format.Bytes(capacity.TotalBytes), Format.Bytes(capacity.FreeBytes), Format.Bytes(current!.Bytes))
                + (analysisFilter.ExcludeWindows ? "\n" + Localization.T("Space.ExcludedHelp") : "");
        }
        else
        {
            if (searchResult is not null) SizeStat.Text = Format.Bytes(searchResult.Bytes);
            else if (current is not null) SizeStat.Text = Format.Bytes(current.Bytes);
            SizeStat.ToolTip = Localization.T("Help.Size");
        }
    }
}
