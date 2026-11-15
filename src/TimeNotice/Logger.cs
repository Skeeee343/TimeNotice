// Phase 6 hardening: one place for debug.log writes.
// Ensures %AppData%/TimeNotice exists and rotates at ~512KB so a long
// run never grows the file without bound. All logging stays fail-silent.
using System.IO;

namespace TimeNotice;

public static class Logger
{
    private const long MaxBytes = 512 * 1024;

    public static string LogPath => System.IO.Path.Combine(Settings.Dir, "debug.log");
    public static string LogOldPath => LogPath + ".old";

    public static void Clear()
    {
        try
        {
            Directory.CreateDirectory(Settings.Dir);
            File.WriteAllText(LogPath, "");
            Log("src=log-cleared");
        }
        catch { } // silent: clear is best-effort from the tray menu
    }

    public static void Log(string msg)
    {
        try
        {
            Directory.CreateDirectory(Settings.Dir);
            var file = LogPath;
            RotateIfNeeded(file);
            File.AppendAllText(file, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {msg}{Environment.NewLine}");
        }
        catch { } // silent: logging must never throw; a failed write is unobservable by design
    }

    // Flat sequential tries, never nested: copy failure still truncates like before (#42).
    private static void RotateIfNeeded(string file)
    {
        bool over = false;
        try
        {
            var info = new FileInfo(file);
            over = info.Exists && info.Length > MaxBytes;
        }
        catch { } // silent: size check failure means no rotation
        if (!over) return;
        try { File.Copy(file, file + ".old", overwrite: true); } catch { } // silent: keep current log when backup fails
        try { File.WriteAllText(file, ""); } catch { } // silent: rotation failure must not drop the caller's log line
    }
}
