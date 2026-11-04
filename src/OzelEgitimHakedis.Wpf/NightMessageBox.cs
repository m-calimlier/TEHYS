using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OzelEgitimHakedis.Wpf;

/// <summary>Application-owned notifications; return values retain WPF MessageBox semantics.</summary>
internal static class NightMessageBox
{
    internal static MessageBoxResult ClosingResult(MessageBoxButton buttons) => buttons switch
    {
        MessageBoxButton.YesNo => MessageBoxResult.No,
        MessageBoxButton.OK => MessageBoxResult.OK,
        _ => MessageBoxResult.Cancel
    };

    internal static MessageBoxResult Show(string message, string title, MessageBoxButton buttons = MessageBoxButton.OK,
        MessageBoxImage icon = MessageBoxImage.None, MessageBoxResult defaultResult = MessageBoxResult.None)
        => Show(null, message, title, buttons, icon, defaultResult);

    internal static MessageBoxResult Show(Window? owner, string message, string title, MessageBoxButton buttons = MessageBoxButton.OK,
        MessageBoxImage icon = MessageBoxImage.None, MessageBoxResult defaultResult = MessageBoxResult.None)
    {
        var window = Create(message, title, buttons, icon, defaultResult, out var answer);
        owner ??= Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
        if (owner?.IsVisible == true) window.Owner = owner;
        window.ShowDialog();
        return answer();
    }

    internal static Window Create(string message, string title, MessageBoxButton buttons, MessageBoxImage icon,
        MessageBoxResult defaultResult, out Func<MessageBoxResult> answer)
    {
        var result = ClosingResult(buttons);
        var window = new Window
        {
            Title = title, Width = 600, SizeToContent = SizeToContent.Height, MinWidth = 420,
            MaxHeight = Math.Max(320, SystemParameters.WorkArea.Height - 60),
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false, Background = NightTheme.Surface, Foreground = NightTheme.Text
        };
        var body = new DockPanel { Margin = new Thickness(24) };
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        DockPanel.SetDock(actions, Dock.Bottom); body.Children.Add(actions);
        var heading = new TextBlock { Text = (icon == MessageBoxImage.Warning ? "⚠  " : icon == MessageBoxImage.Error ? "✕  " : icon == MessageBoxImage.Information ? "ⓘ  " : "") + title, FontSize = 20, Foreground = NightTheme.Text,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) };
        DockPanel.SetDock(heading, Dock.Top); body.Children.Add(heading);
        var text = new TextBlock { Text = message, Foreground = NightTheme.Text, FontSize = 13,
            TextWrapping = TextWrapping.Wrap, LineHeight = 21 };
        body.Children.Add(new ScrollViewer { Content = text, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = Math.Max(150, SystemParameters.WorkArea.Height - 260) });
        var choices = buttons switch
        {
            MessageBoxButton.YesNo => new[] { MessageBoxResult.No, MessageBoxResult.Yes },
            MessageBoxButton.YesNoCancel => new[] { MessageBoxResult.Cancel, MessageBoxResult.No, MessageBoxResult.Yes },
            MessageBoxButton.OKCancel => new[] { MessageBoxResult.Cancel, MessageBoxResult.OK },
            _ => new[] { MessageBoxResult.OK }
        };
        var initial = defaultResult == MessageBoxResult.None ? ClosingResult(buttons) : defaultResult;
        foreach (var choice in choices)
        {
            var button = new Button
            {
                Content = choice switch { MessageBoxResult.Yes => "Evet", MessageBoxResult.No => "Hayır", MessageBoxResult.Cancel => "Vazgeç", _ => "Tamam" },
                MinWidth = 100, Height = 36, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(15, 4, 15, 4),
                IsDefault = choice == initial, IsCancel = choice == ClosingResult(buttons)
            };
            var styleKey = choice is MessageBoxResult.Yes or MessageBoxResult.OK ? "ModernButton" : "SecondaryButton";
            if (Application.Current?.TryFindResource(styleKey) is Style style) button.Style = style;
            button.Click += (_, _) => { result = choice; window.Close(); };
            actions.Children.Add(button);
        }
        window.Content = new Border { BorderBrush = NightTheme.Accent, BorderThickness = new Thickness(1), Child = body };
        window.Loaded += (_, _) => NightTheme.SetTitleBar(window);
        answer = () => result;
        return window;
    }
}
