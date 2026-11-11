namespace DiskSpaceAnalyzer.Core;

/// <summary>Physical-pixel rectangles; do not mix with WPF device-independent coordinates.</summary>
public readonly record struct WindowBounds(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;
    public int Bottom => Top + Height;
    public static WindowBounds Fit(WindowBounds requested, WindowBounds workArea, bool center)
    {
        if (workArea.Width <= 0 || workArea.Height <= 0) throw new ArgumentOutOfRangeException(nameof(workArea));
        int width = Math.Clamp(requested.Width, 1, workArea.Width), height = Math.Clamp(requested.Height, 1, workArea.Height);
        return new(center ? workArea.Left + (workArea.Width - width) / 2 : Math.Clamp(requested.Left, workArea.Left, workArea.Right - width),
            center ? workArea.Top + (workArea.Height - height) / 2 : Math.Clamp(requested.Top, workArea.Top, workArea.Bottom - height), width, height);
    }
}
