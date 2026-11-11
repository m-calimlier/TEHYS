using System;
using System.IO;
using System.Windows;
using DesktopWorkspace;

namespace OzelEgitimHakedis.Wpf;

/// <summary>All exported copies remain inside the application's desktop workspace.</summary>
internal sealed class WorkspaceSaveFileDialog
{
    private readonly Microsoft.Win32.SaveFileDialog inner = new();
    public string Filter { get => inner.Filter; set => inner.Filter = value; }
    public string FileName { get => inner.FileName; set => inner.FileName = value; }
    public string Title { get => inner.Title; set => inner.Title = value; }
    public string DefaultExt { get => inner.DefaultExt; set => inner.DefaultExt = value; }
    public string InitialDirectory { get => inner.InitialDirectory; set => inner.InitialDirectory = value; }
    public bool AddExtension { get => inner.AddExtension; set => inner.AddExtension = value; }
    public bool OverwritePrompt { get => inner.OverwritePrompt; set => inner.OverwritePrompt = value; }
    public bool CheckPathExists { get => inner.CheckPathExists; set => inner.CheckPathExists = value; }
    public bool RestoreDirectory { get => inner.RestoreDirectory; set => inner.RestoreDirectory = value; }
    public bool? ShowDialog(Window? owner = null)
    {
        var root = OzelEgitimHakedis.Core.Services.OehsRuntimePaths.WorkspaceRoot;
        var output = Path.Combine(root, "Belgeler", "Dışa Aktarılanlar");
        if (!WorkspaceOutputPath.IsAllowed(root, Path.Combine(output, "probe.tmp"))) throw new IOException("Masaüstü veri klasörü bağlantılı veya erişilemez.");
        Directory.CreateDirectory(output);
        if (!WorkspaceOutputPath.IsAllowed(root, Path.Combine(inner.InitialDirectory.Length == 0 ? output : inner.InitialDirectory, "probe.tmp"))) inner.InitialDirectory = output;
        if (inner.InitialDirectory.Length == 0) inner.InitialDirectory = output;
        inner.FileName = Path.GetFileName(inner.FileName);
        inner.FileOk += (_, e) =>
        {
            if (WorkspaceOutputPath.IsAllowed(root, inner.FileName)) return;
            e.Cancel = true;
            MessageBox.Show("Tüm kayıtlar aynı yerde tutulur. Dosyayı masaüstündeki program klasörünün içine kaydedin:\n" + root, "Kayıt konumu", MessageBoxButton.OK, MessageBoxImage.Information);
        };
        return owner is null ? inner.ShowDialog() : inner.ShowDialog(owner);
    }
}