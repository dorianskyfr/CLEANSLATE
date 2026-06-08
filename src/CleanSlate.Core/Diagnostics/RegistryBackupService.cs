using System.Diagnostics;
using System.Runtime.Versioning;
using CleanSlate.Core.Abstractions;

namespace CleanSlate.Core.Diagnostics;

/// <summary>
/// Sauvegarde/restauration du registre via l'outil système `reg.exe`
/// (export/import .reg). C'est le garde-fou du module 5b : aucune modification
/// de registre n'est autorisée sans une sauvegarde réussie au préalable.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class RegistryBackupService : IBackupService
{
    private readonly string _backupDir;
    private readonly IActionLogger _logger;

    public RegistryBackupService(IActionLogger logger, string? backupDir = null)
    {
        _logger = logger;
        _backupDir = backupDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CleanSlate", "backups");
        Directory.CreateDirectory(_backupDir);
    }

    /// <param name="scopeKey">
    /// Clé racine à exporter, au format reg.exe (ex.
    /// "HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run").
    /// </param>
    public async Task<string> CreateBackupAsync(string scopeKey, CancellationToken ct)
    {
        var safeName = scopeKey.Replace('\\', '_').Replace(':', '_');
        var file = Path.Combine(_backupDir, $"{safeName}_{DateTime.Now:yyyyMMdd_HHmmss}.reg");

        // reg export "<clé>" "<fichier>" /y
        var exit = await RunRegAsync($"export \"{scopeKey}\" \"{file}\" /y", ct);
        if (exit != 0 || !File.Exists(file))
            throw new InvalidOperationException(
                $"Échec de la sauvegarde du registre pour '{scopeKey}' (code {exit}). " +
                "Aucune modification ne sera effectuée.");

        _logger.Info($"Sauvegarde registre créée : {file}");
        return file;
    }

    public async Task RestoreAsync(string backupFilePath, CancellationToken ct)
    {
        if (!File.Exists(backupFilePath))
            throw new FileNotFoundException("Fichier de sauvegarde introuvable.", backupFilePath);

        var exit = await RunRegAsync($"import \"{backupFilePath}\"", ct);
        if (exit != 0)
            throw new InvalidOperationException($"Échec de la restauration (code {exit}).");

        _logger.Info($"Sauvegarde registre restaurée : {backupFilePath}");
    }

    public IReadOnlyList<string> ListBackups(string scopeKey)
    {
        var safeName = scopeKey.Replace('\\', '_').Replace(':', '_');
        return Directory.EnumerateFiles(_backupDir, $"{safeName}_*.reg")
            .OrderByDescending(f => f)
            .ToList();
    }

    private static async Task<int> RunRegAsync(string arguments, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("reg.exe", arguments)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Impossible de lancer reg.exe.");
        await process.WaitForExitAsync(ct);
        return process.ExitCode;
    }
}
