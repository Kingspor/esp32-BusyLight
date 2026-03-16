using System.Text;

namespace BusyLight.Services;

/// <summary>
/// Simple thread-safe append-only file logger.
/// Log file: %AppData%\BusyLight\busylight.log
/// Rotates automatically when the file exceeds 500 KB (keeps the second half).
/// </summary>
public static class LogService
{
    private static readonly string LogPath;
    private static readonly object Lock = new();

    private const long MaxFileSizeBytes = 500 * 1024; // 500 KB

    static LogService()
    {
        LogPath = Path.Combine(
            ConfigurationService.GetConfigDirectory(),
            "busylight.log");
    }

    /// <summary>Absolute path to the log file.</summary>
    public static string LogFilePath => LogPath;

    /// <summary>Write a timestamped log line to the log file and to Debug output.</summary>
    public static void Log(string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
        Debug.WriteLine(line);

        lock (Lock)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                TrimIfNeeded();
                File.AppendAllText(LogPath, line + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // Never throw from logging
            }
        }
    }

    /// <summary>Open the log file with the system's default text editor.</summary>
    public static void OpenLogFile()
    {
        // Ensure at least an empty file exists so the editor doesn't fail
        lock (Lock)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                if (!File.Exists(LogPath))
                    File.WriteAllText(LogPath, "", Encoding.UTF8);
            }
            catch { }
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName        = LogPath,
            UseShellExecute = true,
        });
    }

    private static void TrimIfNeeded()
    {
        if (!File.Exists(LogPath)) return;
        if (new FileInfo(LogPath).Length < MaxFileSizeBytes) return;

        // Keep only the second half of the file to limit growth
        var lines = File.ReadAllLines(LogPath, Encoding.UTF8);
        File.WriteAllLines(LogPath, lines.Skip(lines.Length / 2), Encoding.UTF8);
    }
}
