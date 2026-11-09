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
        catch { }
    }

    public static void Log(string msg)
    {
        try
        {
            Directory.CreateDirectory(Settings.Dir);
            var file = LogPath;
            try
            {
                var info = new FileInfo(file);
                if (info.Exists && info.Length > MaxBytes)
                {
                    var old = file + ".old";
                    try { File.Copy(file, old, overwrite: true); } catch { }
                    File.WriteAllText(file, "");
                }
            }
            catch { }
            File.AppendAllText(file, $"{DateTime.Now:HH:mm:ss} {msg}{Environment.NewLine}");
        }
        catch { }
    }
}
