using System.Security.Cryptography;

namespace OzelEgitimHakedis.Core.Services;

/// <summary>Copies and verifies the complete migration before removing any old source file.</summary>
public static class OehsDesktopStorageMigration
{
    private static readonly HashSet<string> LiveNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "oehs-data.json", "oehs-data.json.bak", "oehs-special-education.json", "oehs-special-education.json.bak",
        "oehs-basic-secondary.json", "oehs-basic-secondary.json.bak", "official-document-settings.json",
        "window-layout.json", "window-layout-preference.json", "period-info.json", "last-backup.txt", ".fresh-start-version"
    };
    private static readonly HashSet<string> ManagedNames = new(StringComparer.OrdinalIgnoreCase)
        { "Kayitlar", "Belgeler", "Yedekler", "Calisma" };

    public static void Ensure(OehsRuntimePathSet paths, string legacyLocalRoot, string legacyRoamingRoot, string? isolatedArchiveRoot = null)
    {
        var workspace = Path.GetFullPath(paths.WorkspaceRoot);
        var moves = new List<(string Source, string Target)>();
        var oldRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void AddTree(string source, string destination)
        {
            if (!Directory.Exists(source)) return;
            oldRoots.Add(source);
            foreach (var file in Files(source))
                moves.Add((file, Path.Combine(destination, Path.GetRelativePath(source, file))));
        }
        foreach (var legacy in new[] { (Root: legacyLocalRoot, Name: "Local"), (Root: legacyRoamingRoot, Name: "Roaming") })
        {
            if (Inside(workspace, legacy.Root) || Inside(legacy.Root, workspace))
                throw new IOException("Eski veri klasörü ile masaüstü çalışma klasörü çakışıyor.");
            if (Directory.Exists(legacy.Root))
            {
            oldRoots.Add(legacy.Root);
            foreach (var file in Files(legacy.Root))
            {
                var relative = Path.GetRelativePath(legacy.Root, file);
                var target = string.IsNullOrEmpty(Path.GetDirectoryName(relative)) && LiveNames.Contains(Path.GetFileName(file))
                    ? Path.Combine(paths.DataRoot, Path.GetFileName(file))
                    : Path.Combine(workspace, "Yedekler", "EskiKonumlar", legacy.Name, relative);
                moves.Add((file, target));
            }
            }
            var legacyParent = Path.GetDirectoryName(Path.GetFullPath(legacy.Root))!;
            if (!Directory.Exists(legacyParent)) continue;
            foreach (var pattern in new[] { "OEHS.before-*", "OEHS.restore-*" })
                foreach (var old in Directory.EnumerateDirectories(legacyParent, pattern))
                    AddTree(old, Path.Combine(workspace, "Yedekler", "EskiKonumlar", legacy.Name, Path.GetFileName(old)));
            foreach (var recovery in Directory.EnumerateFiles(legacyParent, "OEHS-Recovery-*.oehs-backup.zip"))
                moves.Add((recovery, Path.Combine(workspace, "Yedekler", "EskiKonumlar", legacy.Name, Path.GetFileName(recovery))));
        }
        AddTree(Path.Combine(workspace, "Ciktilar"), Path.Combine(paths.DesktopRoot, "Ciktilar"));
        AddTree(Path.Combine(workspace, "Arsiv"), Path.Combine(paths.DesktopRoot, "Arsiv"));
        if (isolatedArchiveRoot is not null)
        {
            var source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(isolatedArchiveRoot));
            if (!string.Equals(Path.GetFileName(source), "DenemeVerisi", StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(Path.Combine(Path.GetDirectoryName(source)!, OehsRuntimePaths.TestPackageMarkerFileName)))
                throw new IOException("Eski deneme paketi doğrulanamadı.");
            var key = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(source)))[..16];
            AddTree(source, Path.Combine(workspace, "Yedekler", "EskiDenemePaketleri", key));
        }
        var parent = Path.GetDirectoryName(workspace)!;
        if (Directory.Exists(parent))
            foreach (var suffix in new[] { ".pre-reset-*", ".before-*", ".restore-*" })
                foreach (var old in Directory.EnumerateDirectories(parent, Path.GetFileName(workspace) + suffix))
                    AddTree(old, Path.Combine(workspace, "Yedekler", "OncekiSifirlama", Path.GetFileName(old)));
        foreach (var name in ManagedNames)
        {
            var path = Path.Combine(workspace, name); RejectLinks(path); Directory.CreateDirectory(path);
        }

        var verified = new List<(string Source, string Target, string Hash)>();
        foreach (var move in moves)
        {
            RejectLinks(move.Source); RejectLinks(move.Target);
            if (!Inside(workspace, move.Target)) throw new IOException("Taşıma hedefi çalışma klasörünün dışında.");
            var hash = Hash(move.Source);
            var target = move.Target;
            if (File.Exists(target) && Hash(target) != hash)
            {
                // Keep current live data and retain the different older copy separately.
                var key = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(move.Source)))[..16];
                target = Path.Combine(workspace, "Yedekler", "CakisanEskiDosyalar", key, Path.GetFileName(move.Source));
                if (File.Exists(target) && Hash(target) != hash) target += "." + Guid.NewGuid().ToString("N");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (!File.Exists(target))
            {
                var pending = target + ".migration-" + Guid.NewGuid().ToString("N");
                try
                {
                    File.Copy(move.Source, pending);
                    if (Hash(pending) != hash) throw new IOException("Eski dosya taşıma sırasında değişti; kaynak korunuyor.");
                    File.Move(pending, target, overwrite: false);
                }
                finally { if (File.Exists(pending)) File.Delete(pending); }
            }
            if (Hash(target) != hash) throw new IOException("Taşınan dosya doğrulanamadı; kaynak korunuyor.");
            verified.Add((move.Source, target, hash));
        }
        // A failed copy/verification above never reaches the removal phase.
        foreach (var file in verified)
        {
            if (Hash(file.Source) != file.Hash || Hash(file.Target) != file.Hash)
                throw new IOException("Dosya taşıma sırasında değişti; eski dosya korunuyor.");
        }
        foreach (var file in verified)
        {
            // Claim the exact source first. A concurrently recreated original path
            // is never deleted; changed bytes cause the old path to be restored.
            var claimed = file.Source + ".tehys-migration-" + Guid.NewGuid().ToString("N");
            File.Move(file.Source, claimed);
            try
            {
                if (Hash(claimed) != file.Hash || Hash(file.Target) != file.Hash)
                    throw new IOException("Dosya taşıma sırasında değişti; eski dosya korunuyor.");
                File.Delete(claimed);
            }
            catch
            {
                if (!File.Exists(file.Source)) File.Move(claimed, file.Source);
                throw;
            }
        }
        foreach (var root in oldRoots)
            RemoveEmptyDirectories(root);
        foreach (var name in ManagedNames) Directory.CreateDirectory(Path.Combine(workspace, name));
    }

    private static bool Inside(string root, string path) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)), StringComparison.OrdinalIgnoreCase) ||
        Path.GetFullPath(path).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static string Hash(string path)
    { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    private static IEnumerable<string> Files(string root)
    {
        RejectLinks(root);
        foreach (var entry in Directory.EnumerateFileSystemEntries(root))
        {
            RejectLinks(entry);
            if (Directory.Exists(entry)) { foreach (var file in Files(entry)) yield return file; }
            else yield return entry;
        }
    }
    private static void RejectLinks(string path)
    {
        for (var current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if ((Directory.Exists(current) && new DirectoryInfo(current).LinkTarget is not null) ||
                (File.Exists(current) && new FileInfo(current).LinkTarget is not null))
                throw new IOException("Bağlantılı klasör/dosya otomatik taşınamaz: " + current);
    }
    private static void RemoveEmptyDirectories(string root)
    {
        if (!Directory.Exists(root)) return;
        RejectLinks(root);
        foreach (var child in Directory.EnumerateDirectories(root)) RemoveEmptyDirectories(child);
        if (!Directory.EnumerateFileSystemEntries(root).Any()) Directory.Delete(root);
    }
}
