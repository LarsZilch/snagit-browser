using System.IO;
using System.Runtime.InteropServices;

namespace SnagitBrowser.App;

/// <summary>Löschen über den Windows-Papierkorb – Dateien lassen sich dort wiederherstellen.</summary>
public static class RecycleBin
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT op);

    private const uint FO_DELETE = 3;
    private const ushort FOF_SILENT = 0x0004, FOF_NOCONFIRMATION = 0x0010, FOF_ALLOWUNDO = 0x0040,
                         FOF_NOERRORUI = 0x0400, FOF_WANTNUKEWARNING = 0x4000;

    /// <summary>
    /// Verschiebt Dateien in den Papierkorb. Ginge das nicht (z. B. Netzlaufwerk), fragt Windows
    /// selbst nach, bevor endgültig gelöscht wird (FOF_WANTNUKEWARNING).
    /// Gibt die Dateien zurück, die danach tatsächlich weg sind.
    /// </summary>
    public static List<string> Delete(IReadOnlyCollection<string> paths, IntPtr owner)
    {
        if (paths.Count == 0) return [];
        var op = new SHFILEOPSTRUCT
        {
            hwnd = owner,
            wFunc = FO_DELETE,
            pFrom = string.Join("\0", paths) + "\0\0",
            fFlags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI | FOF_WANTNUKEWARNING),
        };
        int rc = SHFileOperation(ref op);
        var gone = paths.Where(p => !File.Exists(p)).ToList();
        if (rc != 0 && gone.Count == 0)
            throw new IOException(Loc.F("Del_Failed", rc));
        return gone;
    }
}
