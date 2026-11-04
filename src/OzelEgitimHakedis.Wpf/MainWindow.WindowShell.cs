using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Documents;
using OzelEgitimHakedis.Core.Models;
using OzelEgitimHakedis.Core.Services;

namespace OzelEgitimHakedis.Wpf;

public partial class MainWindow
{
    private void ConfigureWindow()
    {
        SizeToContent = SizeToContent.Manual;
        WindowStartupLocation = WindowStartupLocation.Manual;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        ShowInTaskbar = true;
        Background = NightTheme.Surface;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        _windowLayoutPreference = WindowLayoutPreferenceStore.Load();
        ApplyWindowLayoutPreference(_windowLayoutPreference);
        UpdateMaximizeRestoreButton();
    }

    private void ApplyWindowLayoutPreference(WindowLayoutPreference preference)
    {
        _windowLayoutPreference = preference;

        if (preference.GetRequestedSize() is not { } requestedSize)
        {
            WindowState = WindowState.Maximized;
            SelectWindowLayoutMode(preference.Mode);
            return;
        }

        WindowState = WindowState.Normal;
        var workArea = SystemParameters.WorkArea;

        Width = Math.Min(requestedSize.Width, workArea.Width);
        Height = Math.Min(requestedSize.Height, workArea.Height);

        Left = workArea.Left + ((workArea.Width - Width) / 2.0);
        Top = workArea.Top + ((workArea.Height - Height) / 2.0);
        SelectWindowLayoutMode(preference.Mode);
    }

    private void SelectWindowLayoutMode(WindowLayoutMode mode)
    {
        if (WindowLayoutModeComboBox is null)
            return;

        _suppressWindowLayoutSelectionChanged = true;
        try
        {
            WindowLayoutModeComboBox.SelectedValue = mode.ToString();
        }
        finally
        {
            _suppressWindowLayoutSelectionChanged = false;
        }
    }

    private void WindowLayoutModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressWindowLayoutSelectionChanged ||
            WindowLayoutModeComboBox.SelectedValue is not string selectedValue ||
            !Enum.TryParse<WindowLayoutMode>(selectedValue, out var mode))
        {
            return;
        }

        var preference = new WindowLayoutPreference(mode);
        try
        {
            WindowLayoutPreferenceStore.Save(preference);
            ApplyWindowLayoutPreference(preference);
            WindowLayoutStatusText.Text = "Pencere tercihi kaydedildi. Windows ekran çözünürlüğü değişmedi.";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            SelectWindowLayoutMode(_windowLayoutPreference.Mode);
            WindowLayoutStatusText.Text = $"Pencere tercihi kaydedilemedi; önceki seçim korundu. {error.Message}";
        }
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        _windowSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        _windowSource?.AddHook(WindowMessageHook);
    }

    private IntPtr WindowMessageHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmGetMinMaxInfo || lParam == IntPtr.Zero)
            return IntPtr.Zero;

        var minMax = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);

        if (monitor != IntPtr.Zero)
        {
            var monitorInfo = new MonitorInfo
            {
                cbSize = Marshal.SizeOf<MonitorInfo>()
            };

            if (GetMonitorInfo(monitor, ref monitorInfo))
            {
                var work = monitorInfo.rcWork;
                var bounds = monitorInfo.rcMonitor;
                minMax.ptMaxPosition.x = work.left - bounds.left;
                minMax.ptMaxPosition.y = work.top - bounds.top;
                minMax.ptMaxSize.x = work.right - work.left;
                minMax.ptMaxSize.y = work.bottom - work.top;
            }
        }

        Marshal.StructureToPtr(minMax, lParam, false);
        handled = true;
        return IntPtr.Zero;
    }

    private void UpdateMaximizeRestoreButton()
    {
        if (MaximizeRestoreButton is null || MaximizeRestoreIcon is null)
            return;

        var maximized = WindowState == WindowState.Maximized;
        MaximizeRestoreButton.ToolTip = maximized ? "Geri yükle" : "Tam ekran";
        MaximizeRestoreIcon.Text = maximized ? "\uE923" : "\uE922";
    }

}
