using System.Windows;
using System.IO;
using OzelEgitimHakedis.Core.Services;
namespace OzelEgitimHakedis.Wpf;
public partial class App : Application
{
    public App()
    {
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => NightTheme.SetTitleBar((Window)sender)));
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Length > 0 && e.Args[0] is "--installer-clean" or "--installer-prepare")
        {
            if (e.Args.Length != 2) { Shutdown(2); return; }
            try
            {
                // Restart Manager closes the installed application first; also
                // refuse to clean while a different portable copy is still open.
                foreach (var name in new[] { "TEHYS", "OEHS" })
                    foreach (var process in System.Diagnostics.Process.GetProcessesByName(name))
                        using (process)
                            if (process.Id != Environment.ProcessId) { Shutdown(3); return; }
                if (e.Args[0] == "--installer-clean")
                    OehsCleanInstallService.CleanProfile(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), e.Args[1]);
                OehsRuntimePaths.PrepareDesktopStorage(e.Args[1]);
                Shutdown(0);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
            { Shutdown(4); }
            return;
        }
        try { OehsRuntimePaths.PrepareDesktopStorage(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            NightMessageBox.Show("Masaüstü veri klasörü hazırlanamadı. Eski dosyalarınızı koruyun; program açılmadı.\n\n" + error.Message,
                "TEHYS — Veri taşıma durduruldu", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }
        base.OnStartup(e);
    }
}
