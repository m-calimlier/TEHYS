namespace OzelEgitimHakedis.Core.Services;

/// <summary>Explicit installer-only cleanup of known locations for a single Windows account.</summary>
public static class OehsCleanInstallService
{
    public static void CleanProfile(string localParent, string roamingParent, string desktopParent, string? installationDirectory = null)
    {
        var parents = new[] { ValidateParent(localParent), ValidateParent(roamingParent), ValidateParent(desktopParent) };
        for (var i=0;i<parents.Length;i++)
            for (var j=i+1;j<parents.Length;j++)
                if (Contains(parents[i],parents[j]) || Contains(parents[j],parents[i]))
                    throw new ArgumentException("Kullanıcı veri kökleri çakışamaz.");
        string? install = null;
        if (!string.IsNullOrWhiteSpace(installationDirectory))
        {
            install = ValidateParent(installationDirectory);
            if (parents.Any(parent => Contains(install,parent)))
                throw new ArgumentException("Program klasörü kullanıcı veri köklerini kapsayamaz.");
        }
        // Build the complete bounded list before deleting anything.
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var parent in parents.Take(2))
        {
            candidates.Add(Path.Combine(parent,"OEHS"));
            if (!Directory.Exists(parent)) continue;
            foreach (var pattern in new[] { "OEHS.before-*", "OEHS.restore-*" })
                foreach (var path in Directory.EnumerateDirectories(parent,pattern)) candidates.Add(path);
            foreach (var path in Directory.EnumerateFiles(parent,"OEHS-Recovery-*.oehs-backup.zip")) candidates.Add(path);
        }
        var desktop=parents[2];
        foreach (var name in new[] { OehsRuntimePaths.ProductName, OehsRuntimePaths.LegacyDesktopFolderName })
        {
            candidates.Add(Path.Combine(desktop, name));
            if (Directory.Exists(desktop))
                foreach (var suffix in new[] { ".pre-reset-*", ".before-*", ".restore-*" })
                    foreach (var path in Directory.EnumerateDirectories(desktop, name + suffix)) candidates.Add(path);
        }
        if (install is not null && (File.Exists(Path.Combine(install,"TEHYS.exe")) || File.Exists(Path.Combine(install,"OEHS.exe")) ||
            File.Exists(Path.Combine(install,OehsRuntimePaths.TestPackageMarkerFileName))))
        {
            // A custom installation folder may also contain unrelated personal files.
            foreach (var name in new[] { "TEHYS.exe", "OEHS.exe", "OEHS.dll", "OEHS.pdb", "OEHS.deps.json", "OEHS.runtimeconfig.json",
                "OzelEgitimHakedis.Core.dll", "OzelEgitimHakedis.Documents.dll", "KULLANIM.txt", "OEHS_DENEME_PAKETI.flag", "DenemeVerisi" })
                candidates.Add(Path.Combine(install,name));
        }
        foreach (var path in candidates) DeleteWithoutFollowingLinks(path);
    }

    private static string ValidateParent(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("Mutlak klasör yolu gerekli.");
        var full=Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (string.Equals(full,Path.TrimEndingDirectorySeparator(Path.GetPathRoot(full)!),StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Disk kökü temizleme hedefi olamaz.");
        for (var current=full;!string.IsNullOrEmpty(current);current=Path.GetDirectoryName(current))
            if (Directory.Exists(current) && new DirectoryInfo(current).LinkTarget is not null)
                throw new IOException("Bağlantılı kullanıcı kökü temizlenemez: "+current);
        return full;
    }
    private static bool Contains(string root,string path) => string.Equals(root,path,StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
    private static void DeleteWithoutFollowingLinks(string path)
    {
        FileAttributes attributes;
        try { attributes=File.GetAttributes(path); }
        catch(FileNotFoundException) {return;}
        catch(DirectoryNotFoundException) {return;}
        var directory=(attributes&FileAttributes.Directory)!=0;
        FileSystemInfo info=directory?new DirectoryInfo(path):new FileInfo(path);
        if (info.LinkTarget is not null)
        {
            if(directory)Directory.Delete(path);else File.Delete(path);
            return;
        }
        if(directory)
        {
            foreach(var child in Directory.EnumerateFileSystemEntries(path))DeleteWithoutFollowingLinks(child);
            Directory.Delete(path);
        }
        else
        {
            if((attributes&FileAttributes.ReadOnly)!=0)File.SetAttributes(path,attributes&~FileAttributes.ReadOnly);
            File.Delete(path);
        }
    }
}
