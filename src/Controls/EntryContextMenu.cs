using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DiskSpaceAnalyzer.Core;
using I18n = DiskSpaceAnalyzer.Core.Localization;

namespace DiskSpaceAnalyzer.Controls;

internal static class EntryContextMenu
{
    internal enum Command { Reveal, CopyPath, Open, Properties }
    internal static ContextMenu Create(ViewEntry entry, Action<ViewEntry, Command> execute)
    {
        var menu = new ContextMenu
        {
            Background = new SolidColorBrush(Color.FromRgb(17, 27, 36)), Foreground = Brushes.White,
            BorderBrush = Brushes.SlateGray, BorderThickness = new Thickness(1), Padding = new Thickness(0), Tag = entry
        };
        foreach (var (label, command) in new[]
        {
            (I18n.T("Text.A54D9818E6DD"), Command.Reveal), (I18n.T("Text.C9270BA5323A"), Command.CopyPath),
            (entry.IsFolder ? I18n.T("Text.3AA01F0BE206") : I18n.T("Text.BCF3F5DE4EA2"), Command.Open), (I18n.T("Text.BEF1671DBD86"), Command.Properties)
        })
        {
            string help = command switch { Command.Reveal => FeatureHelp.Reveal, Command.CopyPath => FeatureHelp.CopyPath, Command.Open => entry.IsFolder ? FeatureHelp.OpenFolder : FeatureHelp.OpenFile, _ => FeatureHelp.Properties };
            var item = new MenuItem { Header = label, ToolTip = $"{help}\n\n{entry.Path}", Tag = command };
            // Capture the right-clicked entry; a live refresh or later selection cannot change it.
            item.Click += (_, _) => execute(entry, command);
            menu.Items.Add(item);
        }
        return menu;
    }
}
