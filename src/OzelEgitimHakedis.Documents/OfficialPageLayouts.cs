namespace OzelEgitimHakedis.Wpf;

/// <summary>Approved geometry in 96-DPI units. Changing a page's layout is explicit and reviewable.</summary>
internal sealed record OfficialPageLayout(double Left, double Width, double TitleTop, double FrameTop);

internal static class OfficialPageLayouts
{
    internal const double PageWidth = 793.7007874d;
    internal const double PageHeight = 1122.519685d;
    internal static readonly OfficialPageLayout PaymentReport = new(67, 659, 48, 71);
    internal static readonly OfficialPageLayout ServiceReport = new(95, 604, 39, 38);
    internal static readonly OfficialPageLayout Acceptance = new(50, 693, 38, 38);
    internal static readonly OfficialPageLayout Summary = new(20, 754, 16, 47);
}
