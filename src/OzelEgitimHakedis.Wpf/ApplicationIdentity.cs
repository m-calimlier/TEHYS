using System.Reflection;

namespace OzelEgitimHakedis.Wpf;

public static class ApplicationIdentity
{
    public static string Version => typeof(ApplicationIdentity).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    public static string HeaderTitle => "TEHYS " + Version;
}
