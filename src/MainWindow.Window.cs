using System.Windows;
using DiskSpaceAnalyzer.Interop;
using I18n = DiskSpaceAnalyzer.Core.Localization;

namespace DiskSpaceAnalyzer;

public partial class MainWindow
{
    private WindowPlacement? windowPlacement;
    private void InitializeWindowPlacement() => windowPlacement = new WindowPlacement(this);
    private bool isFullScreen;
    private WindowState previousWindowState;
    private WindowStyle previousWindowStyle;
    private ResizeMode previousResizeMode;
    private Rect previousWindowBounds;

    private void FullScreenClicked(object sender, RoutedEventArgs e) => ToggleFullScreen();

    private void ToggleFullScreen()
    {
        if (!isFullScreen)
        {
            previousWindowState = WindowState;
            previousWindowStyle = WindowStyle;
            previousResizeMode = ResizeMode;
            previousWindowBounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            // Maximize after removing the native frame so the treemap fills the monitor.
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
            isFullScreen = true;
            FullScreenButton.Content = I18n.T("Text.1D287C2B2B13");
        }
        else
        {
            WindowState = WindowState.Normal;
            WindowStyle = previousWindowStyle;
            ResizeMode = previousResizeMode;
            if (!previousWindowBounds.IsEmpty)
            {
                Left = previousWindowBounds.Left; Top = previousWindowBounds.Top;
                Width = previousWindowBounds.Width; Height = previousWindowBounds.Height;
            }
            WindowState = previousWindowState;
            isFullScreen = false;
            FullScreenButton.Content = I18n.T("Text.451354ACB402");
            windowPlacement?.EnsureVisible();
        }
    }
}
