using System;
using System.IO;

namespace DesktopWorkspace;

public static class WorkspaceOutputPath
{
    public static bool IsAllowed(string root, string path)
    {
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(path)) return false;
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        var normalized = Path.GetFullPath(path);
        if (!normalized.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return false;
        for (var current = Path.GetDirectoryName(normalized); current is not null; current = Path.GetDirectoryName(current))
            if (Directory.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return false;
        return !File.Exists(normalized) || (File.GetAttributes(normalized) & FileAttributes.ReparsePoint) == 0;
    }
}
