using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using DiskSpaceAnalyzer.Core;
using I18n = DiskSpaceAnalyzer.Core.Localization;

namespace DiskSpaceAnalyzer.Controls;

public sealed class TreemapView : FrameworkElement
{
    private IReadOnlyList<ViewEntry> entries = [];
    private readonly List<(Rect Bounds, ViewEntry Owner)> hitRegions = [];
    private ViewEntry? hovered;
    private readonly TextBlock hoverText = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxWidth = 520, Foreground = Brushes.White };
    private readonly ToolTip hoverTip;
    private Window? hoverOwner;
    private DrawingGroup? scene;
    private bool sceneDirty = true;
    private int previewDepth = 2, drawnTiles;
    private string folderPath = "";
    private DriveCapacity? capacity;
    public DriveCapacity? Capacity { get => capacity; set { if (capacity == value) return; capacity = value; sceneDirty = true; ClearHover(); InvalidateVisual(); } }
    private double pixelScale = 1;
    private readonly Typeface face = new("Segoe UI, Malgun Gothic");
    private static readonly string[] palette = ["#34778E", "#66549B", "#3C8076", "#A07842", "#556D9B", "#985D72", "#62824F", "#567F94"];
    public event Action<ViewEntry>? EntryClicked;
    public event Action<ViewEntry?>? EntryHovered;
    public event Action<ViewEntry>? EntryContextRequested;
    public event Action<ViewEntry>? GroupClicked;
    public int PreviewDepth { get => previewDepth; set { if (previewDepth == value) return; previewDepth = value; sceneDirty = true; InvalidateVisual(); } }
    public string FolderPath { get => folderPath; set { if (folderPath == value) return; folderPath = value; sceneDirty = true; InvalidateVisual(); } }
    public string? SelectedPath { get; set; }
    public IReadOnlyList<ViewEntry> Entries
    {
        get => entries;
        set { entries = value; sceneDirty = true; hitRegions.Clear(); ClearHover(); InvalidateVisual(); }
    }
    public TreemapView()
    {
        ClipToBounds = true; Focusable = true;
        // Explicit popup opening avoids both the initial WPF delay and tile-to-tile delay.
        hoverTip = new ToolTip
        {
            Content = hoverText, PlacementTarget = this, Placement = PlacementMode.MousePoint,
            HorizontalOffset = 12, VerticalOffset = 16, StaysOpen = true, IsHitTestVisible = false,
            Padding = new Thickness(5, 3, 5, 3), Background = Brush("#111B24"),
            BorderBrush = Brush("#8297A7"), BorderThickness = new Thickness(1), HasDropShadow = false
        };
        MouseMove += (_, e) => UpdateHover(e.GetPosition(this));
        MouseLeave += (_, _) => ClearHover();
        Loaded += (_, _) => { hoverOwner = Window.GetWindow(this); if (hoverOwner is not null) hoverOwner.Deactivated += OwnerDeactivated; };
        Unloaded += (_, _) => { ClearHover(); if (ContextMenu is { } menu) menu.IsOpen = false; if (hoverOwner is not null) hoverOwner.Deactivated -= OwnerDeactivated; hoverOwner = null; };
        MouseLeftButtonUp += (_, e) =>
        {
            // Parents are registered before children, so the deepest visible folder wins.
            var target = EntryAt(e.GetPosition(this));
            ClearHover();
            if (target?.IsFreeSpace == true) return;
            if (target?.IsGroup == true) GroupClicked?.Invoke(target);
            else if (target is not null) EntryClicked?.Invoke(target);
        };
        MouseRightButtonUp += (_, e) => { RequestContextAt(e.GetPosition(this)); e.Handled = true; };
        SizeChanged += (_, _) => { sceneDirty = true; InvalidateVisual(); };
    }
    internal ToolTip HoverToolTip => hoverTip;
    internal int VisibleTileCount => drawnTiles;
    internal int SceneBuildCount { get; private set; }
    internal void UpdateHover(Point position)
    {
        if (ContextMenu?.IsOpen == true) return;
        var target = EntryAt(position);
        if (target == hovered) return;
        hoverTip.IsOpen = false;
        hovered = target;
        Cursor = target?.IsFolder == true || target?.IsGroup == true ? Cursors.Hand : Cursors.Arrow;
        if (target is not null)
        {
            hoverText.Text = $"{target.Name}\n{target.SizeText}";
            hoverText.Text += "\n" + (target.IsFreeSpace ? I18n.T("Space.TileHelp") : target.IsGroup ? FeatureHelp.GroupTile : target.IsFolder ? FeatureHelp.FolderTile : FeatureHelp.FileTile);
            hoverTip.IsOpen = true;
        }
        EntryHovered?.Invoke(target); InvalidateVisual();
    }
    internal void RequestContextAt(Point position)
    {
        var target = EntryAt(position);
        ClearHover();
        if (target is null || target.IsGroup || target.IsFreeSpace)
        {
            if (ContextMenu is { } menu) menu.IsOpen = false;
            ContextMenu = null; return;
        }
        SelectedPath = target.Path; InvalidateVisual();
        EntryContextRequested?.Invoke(target);
    }
    private void ClearHover()
    {
        hoverTip.IsOpen = false; hovered = null; Cursor = Cursors.Arrow;
        EntryHovered?.Invoke(null); InvalidateVisual();
    }
    private void OwnerDeactivated(object? sender, EventArgs e) => ClearHover();
    internal ViewEntry? EntryAt(Point position) => hitRegions.LastOrDefault(t => t.Bounds.Contains(position)).Owner;
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var dpi = VisualTreeHelper.GetDpi(this);
        double currentScale = dpi.DpiScaleX * dpi.DpiScaleY;
        if (currentScale != pixelScale) { pixelScale = currentScale; sceneDirty = true; }
        if (sceneDirty || scene is null)
        {
            hitRegions.Clear(); drawnTiles = 0;
            scene = new DrawingGroup();
            using (var drawing = scene.Open()) DrawScene(drawing);
            scene.Freeze(); sceneDirty = false; SceneBuildCount++;
        }
        // Mouse movement only repaints cached primitives and the outline, never sorts/layouts files again.
        dc.DrawDrawing(scene);
        foreach (var region in hitRegions)
            if ((!region.Owner.IsGroup && region.Owner.Path == SelectedPath) || region.Owner == hovered)
                dc.DrawRectangle(null, new Pen(Brush("#AEF5E5"), 2), Inset(region.Bounds, 1));
    }
    private void DrawScene(DrawingContext dc)
    {
        dc.DrawRectangle(Brush("#0D1721"), null, new Rect(0, 0, ActualWidth, ActualHeight));
        var bounds = new MapRect(0, 0, ActualWidth, ActualHeight);
        if (capacity is { FreeBytes: > 0, TotalBytes: > 0 })
        {
            double usedWidth = ActualWidth * ((double)capacity.UsedBytes / capacity.TotalBytes);
            var free = new ViewEntry(I18n.T("Space.FreeName"), "", capacity.FreeBytes, false) { IsFreeSpace = true };
            // Free space stays outside tiny-item grouping.
            DrawEntry(dc, free, new Rect(usedWidth, 0, ActualWidth - usedWidth, ActualHeight), ColorFor(free), 0);
            bounds = new MapRect(0, 0, usedWidth, ActualHeight);
            if (usedWidth <= 0) return;
        }
        var visible = TreemapGrouping.Create(entries, bounds, FolderPath, pixelScale);
        if (visible.Count == 0)
        {
            dc.PushClip(new RectangleGeometry(RectOf(bounds)));
            Text(dc, I18n.T("Text.8A851CFF9903"), new(30, 30), 18, Brushes.LightSlateGray, Math.Max(1, ActualWidth - 60));
            Text(dc, I18n.T("Text.7B42F1415F70"), new(30, 62), 12, Brushes.SlateGray, Math.Max(1, ActualWidth - 60));
            dc.Pop();
            return;
        }
        // Drawing primitives only: no per-file WPF controls or nested event handlers.
        foreach (var tile in TreemapLayout.Create(visible, x => x.Bytes, bounds))
        {
            Rect rect = RectOf(tile.Bounds);
            if (rect.Width < 0.8 || rect.Height < 0.8) continue;
            DrawEntry(dc, tile.Item, rect, ColorFor(tile.Item), 0);
        }
    }
    private void DrawEntry(DrawingContext dc, ViewEntry entry, Rect box, Color color, int depth)
    {
        Rect r = box;
        if (r.IsEmpty || r.Width <= 0 || r.Height <= 0) return;
        drawnTiles++;
        // Synthetic file/other preview blocks inherit their enclosing folder's target.
        bool interactive = depth == 0 || entry.IsFolder || entry.IsGroup;
        if (interactive) hitRegions.Add((r, entry));
        DrawEntryContents(dc, entry, r, color, depth);
    }
    private void DrawEntryContents(DrawingContext dc, ViewEntry entry, Rect r, Color color, int depth)
    {
        var fill = new SolidColorBrush(color); fill.Freeze();
        dc.DrawRectangle(fill, new Pen(Brush("#10202B"), 1), r);
        if (r.Width < 14 || r.Height < 14) return;
        const double header = 16;
        bool showInlineSize = r.Width >= 150;
        Brush textBrush = Brushes.White;
        Text(dc, entry.Name, new(r.X + 2, r.Y + 1), 11, textBrush, r.Width - (showInlineSize ? 76 : 4), depth == 0);
        if (showInlineSize) Text(dc, entry.SizeText, new(r.Right - 72, r.Y + 1), 10, textBrush, 70, right: true);
        bool nested = entry.Node is { Children.Count: > 0 } && depth < PreviewDepth && r.Width >= 40 && r.Height >= 36 && drawnTiles < 6000;
        if (!nested)
        {
            if (!showInlineSize && r.Height >= 30) Text(dc, entry.SizeText, new(r.X + 2, r.Y + 15), 11, textBrush, r.Width - 4);
            if (depth == 0 && r.Height >= (showInlineSize ? 30 : 44) && r.Width > 70 && entry.IsFolder)
                Text(dc, I18n.F("Text.15E787857F74", entry.Files), new(r.X + 2, r.Y + (showInlineSize ? 15 : 29)), 10, textBrush, r.Width - 4);
            return;
        }
        var children = entry.Node!.Children.Values.Select(c => new ViewEntry(c.Name, c.Path, c.Bytes, true, c.Files, c)).ToList();
        if (entry.Node.OwnBytes > 0) children.Add(new ViewEntry(I18n.F("Text.571338C8DD1D", entry.Node.OwnFiles), entry.Path, entry.Node.OwnBytes, false) { GroupCount = Math.Max(1, entry.Node.OwnFiles) });
        var inner = new MapRect(r.X, r.Y + header, r.Width, r.Height - header);
        children = TreemapGrouping.Create(children, inner, entry.Path, pixelScale, Math.Min(TreemapGrouping.MaxTilesPerLevel, Math.Max(1, 6000 - drawnTiles)));
        int index = 0;
        foreach (var tile in TreemapLayout.Create(children, x => x.Bytes, inner))
        {
            double shade = 0.82 + (index++ % 4) * 0.055;
            Color childColor = Color.FromRgb((byte)(color.R * shade), (byte)(color.G * shade), (byte)(color.B * shade));
            DrawEntry(dc, tile.Item, RectOf(tile.Bounds), childColor, depth + 1);
        }
    }
    public static Color ColorFor(ViewEntry entry)
    {
        if (entry.IsFreeSpace) return Color.FromRgb(112, 112, 112);
        if (entry.IsGroup) return (Color)ColorConverter.ConvertFromString("#485B66");
        uint hash = 2166136261;
        string key = entry.IsFolder ? entry.Name.ToLowerInvariant() : System.IO.Path.GetExtension(entry.Name).ToLowerInvariant();
        foreach (char c in key) hash = (hash ^ c) * 16777619;
        return (Color)ColorConverter.ConvertFromString(palette[hash % palette.Length]);
    }
    private void Text(DrawingContext dc, string text, Point point, double size, Brush foreground, double width, bool bold = false, bool right = false)
    {
        if (width < 4) return;
        var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            bold ? new Typeface(face.FontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal) : face,
            size, foreground, VisualTreeHelper.GetDpi(this).PixelsPerDip)
        { MaxTextWidth = width, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis, TextAlignment = right ? TextAlignment.Right : TextAlignment.Left };
        dc.DrawText(formatted, point);
    }
    private static Rect RectOf(MapRect r) => new(r.X, r.Y, Math.Max(0, r.Width), Math.Max(0, r.Height));
    private static Rect Inset(Rect r, double amount) => new(r.X + amount, r.Y + amount, Math.Max(0, r.Width - amount * 2), Math.Max(0, r.Height - amount * 2));
    private static SolidColorBrush Brush(string color) { var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); brush.Freeze(); return brush; }
}
