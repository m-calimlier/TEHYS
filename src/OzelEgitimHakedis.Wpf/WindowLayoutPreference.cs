using System.IO;
using System.Text.Json;

namespace OzelEgitimHakedis.Wpf;

public enum WindowLayoutMode
{
    Maximized,
    FullHd,
    Hd,
    StandardFourByThree,
    StandardWide
}

public readonly record struct WindowLayoutSize(double Width, double Height);

public sealed class WindowLayoutPreference
{
    public static WindowLayoutPreference Default { get; } = new(WindowLayoutMode.Maximized);

    public WindowLayoutPreference(WindowLayoutMode mode) => Mode = mode;

    public WindowLayoutMode Mode { get; init; }

    public WindowLayoutSize? GetRequestedSize() => Mode switch
    {
        WindowLayoutMode.FullHd => new WindowLayoutSize(1920, 1080),
        WindowLayoutMode.Hd => new WindowLayoutSize(1366, 768),
        WindowLayoutMode.StandardFourByThree => new WindowLayoutSize(1280, 1024),
        WindowLayoutMode.StandardWide => new WindowLayoutSize(1280, 768),
        _ => null
    };
}

public static class WindowLayoutMetrics
{
    public static double GetNavigationWidth(double windowWidth) =>
        windowWidth <= 1280 ? 185 : 215;
}

internal static class WindowLayoutPreferenceStore
{
    private static readonly string SettingsPath = Path.Combine(
        OzelEgitimHakedis.Core.Services.OehsRuntimePaths.DataRoot,
        "window-layout.json");

    public static WindowLayoutPreference Load()
    {
        try
        {
            var path = SettingsPath;
            if (!File.Exists(path))
                return WindowLayoutPreference.Default;

            return JsonSerializer.Deserialize<WindowLayoutPreference>(File.ReadAllText(path))
                ?? WindowLayoutPreference.Default;
        }
        catch
        {
            return WindowLayoutPreference.Default;
        }
    }

    public static void Save(WindowLayoutPreference preference)
    {
        var folder = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(folder);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(preference));
    }
}
