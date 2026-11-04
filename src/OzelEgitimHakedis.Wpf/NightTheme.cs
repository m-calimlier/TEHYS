using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace OzelEgitimHakedis.Wpf;

internal static class NightTheme
{
    internal static readonly Brush Surface = Frozen("#09283F");
    internal static readonly Brush Text = Frozen("#F0F6FC");
    internal static readonly Brush Muted = Frozen("#B9CDDF");
    internal static readonly Brush Accent = Frozen("#A8D8F2");
    internal static readonly Brush Alternate = Frozen("#14374F");
    private static Brush Frozen(string value)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(value)!;
        brush.Freeze();
        return brush;
    }
    internal static void SetTitleBar(Window window)
    {
        if (window.WindowStyle == WindowStyle.None) return;
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        // Unsupported Windows builds simply keep their native title bar.
        int enabled = 1;
        _ = DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int));
        int navy = 0x00321E06;
        _ = DwmSetWindowAttribute(handle, 35, ref navy, sizeof(int));
    }
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
