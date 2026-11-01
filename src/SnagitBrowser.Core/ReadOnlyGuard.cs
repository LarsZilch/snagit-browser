namespace SnagitBrowser.Core;

/// <summary>
/// Schutz der Snagit-Quelldaten: Jeder Schreibvorgang der App muss hierüber laufen.
/// Ziele innerhalb des Quellordners werden abgelehnt.
/// </summary>
public static class ReadOnlyGuard
{
    public static bool IsInside(string path, string root)
    {
        var p = Normalize(path);
        var r = Normalize(root);
        return p.Equals(r, StringComparison.OrdinalIgnoreCase) ||
               p.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    public static void EnsureOutside(string target, string sourceRoot)
    {
        if (IsInside(target, sourceRoot))
            throw new InvalidOperationException(
                $"Schreiben in den Snagit-Quellordner ist nicht erlaubt: {target}");
    }

    private static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}

/// <summary>Exportfunktionen – schreiben nie in den Quellordner.</summary>
public static class Exporter
{
    public static void WriteBytes(string targetFile, byte[] data, string sourceRoot)
    {
        ReadOnlyGuard.EnsureOutside(targetFile, sourceRoot);
        File.WriteAllBytes(targetFile, data);
    }

    /// <summary>Kopiert Dateien in einen Zielordner. Vorhandene Dateien werden nicht überschrieben.</summary>
    public static int CopyFiles(IEnumerable<string> sources, string targetDir, string sourceRoot)
    {
        ReadOnlyGuard.EnsureOutside(targetDir, sourceRoot);
        Directory.CreateDirectory(targetDir);
        int n = 0;
        foreach (var src in sources)
        {
            var dest = UniquePath(Path.Combine(targetDir, Path.GetFileName(src)));
            ReadOnlyGuard.EnsureOutside(dest, sourceRoot);
            File.Copy(src, dest, overwrite: false);
            n++;
        }
        return n;
    }

    /// <summary>Speichert die gerenderten Bilder (mit Annotationen) als PNG in einen Zielordner.</summary>
    public static int ExportImages(IEnumerable<string> snagxFiles, string targetDir, string sourceRoot)
    {
        ReadOnlyGuard.EnsureOutside(targetDir, sourceRoot);
        Directory.CreateDirectory(targetDir);
        int n = 0;
        foreach (var src in snagxFiles)
        {
            var bytes = SnagxReader.ReadRenderedImage(src);
            if (bytes == null) continue;
            var dest = UniquePath(Path.Combine(targetDir, Path.GetFileNameWithoutExtension(src) + ".png"));
            WriteBytes(dest, bytes, sourceRoot);
            n++;
        }
        return n;
    }

    public static string UniquePath(string path)
    {
        if (!File.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (int i = 2; ; i++)
        {
            var p = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(p)) return p;
        }
    }
}
