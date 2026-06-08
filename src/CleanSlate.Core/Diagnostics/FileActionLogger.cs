using CleanSlate.Core.Abstractions;

namespace CleanSlate.Core.Diagnostics;

/// <summary>
/// Journalisation simple dans %LOCALAPPDATA%\CleanSlate\logs\cleanslate-AAAAMMJJ.log.
/// Thread-safe par verrou. Suffisant pour la traçabilité utilisateur ; pourrait
/// être remplacé par Microsoft.Extensions.Logging ou Serilog ultérieurement.
/// </summary>
public sealed class FileActionLogger : IActionLogger
{
    private readonly string _logDirectory;
    private readonly object _gate = new();

    public FileActionLogger(string? logDirectory = null)
    {
        _logDirectory = logDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CleanSlate", "logs");
        Directory.CreateDirectory(_logDirectory);
    }

    public void Info(string message) => Write("INFO", message);
    public void Warning(string message) => Write("WARN", message);

    public void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message} | {ex}");

    public void LogCleaning(string providerId, int deleted, int failed, long freedBytes) =>
        Write("CLEAN",
            $"provider={providerId} supprimés={deleted} échecs={failed} " +
            $"libéré={FormatBytes(freedBytes)}");

    private void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}";
        var file = Path.Combine(_logDirectory, $"cleanslate-{DateTime.Now:yyyyMMdd}.log");
        lock (_gate)
        {
            try { File.AppendAllText(file, line + Environment.NewLine); }
            catch { /* la journalisation ne doit jamais faire planter l'application */ }
        }
    }

    /// <summary>Formate une taille en octets de façon lisible (Ko, Mo, Go…).</summary>
    public static string FormatBytes(long bytes)
    {
        string[] units = { "o", "Ko", "Mo", "Go", "To" };
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return $"{size:0.##} {units[unit]}";
    }
}
