using System.Windows;
namespace OzelEgitimHakedis.Wpf;
public partial class App : Application
{
    public App()
    {
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => NightTheme.SetTitleBar((Window)sender)));
    }
}
