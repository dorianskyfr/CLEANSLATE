using Microsoft.Win32;
using System.Runtime.Versioning;
using CleanSlate.Core.Abstractions;

namespace CleanSlate.Core.Modules;

/// <summary>
/// Module 5b — Nettoyage du registre (implémentation CONSERVATRICE).
///
/// ⚠️ Rappel honnête (docs/LIMITES-TECHNIQUES.md) : le gain de performance est
/// quasi nul, le risque est réel. On se limite donc à une définition stricte et
/// défendable d'« entrée orpheline » : une entrée de démarrage (clé Run, HKCU/HKLM)
/// qui pointe vers un exécutable QUI N'EXISTE PLUS sur le disque.
///
/// La correction est IMPOSSIBLE sans une sauvegarde réussie (voir
/// <see cref="FixAsync"/>) — c'est garanti par le contrat <see cref="IRegistryCleaner"/>.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class RegistryCleaner : IRegistryCleaner
{
    private const string RunSubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly IActionLogger _logger;

    public RegistryCleaner(IActionLogger logger) => _logger = logger;

    public Task<IReadOnlyList<RegistryIssue>> ScanAsync(CancellationToken ct)
    {
        return Task.Run<IReadOnlyList<RegistryIssue>>(() =>
        {
            var issues = new List<RegistryIssue>();
            ScanRunKey(Registry.CurrentUser, @"HKEY_CURRENT_USER\" + RunSubKey, issues, ct);
            ScanRunKey(Registry.LocalMachine, @"HKEY_LOCAL_MACHINE\" + RunSubKey, issues, ct);
            return issues;
        }, ct);
    }

    private void ScanRunKey(RegistryKey root, string fullKeyPath, List<RegistryIssue> issues, CancellationToken ct)
    {
        using var key = root.OpenSubKey(RunSubKey);
        if (key is null) return;

        foreach (var name in key.GetValueNames())
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(name)) continue;

            var command = key.GetValue(name)?.ToString();
            if (string.IsNullOrWhiteSpace(command)) continue;

            if (IsOrphanedRunCommand(command!, out var exe))
            {
                issues.Add(new RegistryIssue(
                    KeyPath: fullKeyPath,
                    ValueName: name,
                    Reason: $"L'exécutable référencé est introuvable : {exe}"));
            }
        }
    }

    public async Task<int> FixAsync(
        IReadOnlyCollection<RegistryIssue> issues, string backupFilePath, CancellationToken ct)
    {
        // GARDE-FOU : refus catégorique d'agir sans sauvegarde valide.
        if (string.IsNullOrWhiteSpace(backupFilePath) || !File.Exists(backupFilePath))
            throw new InvalidOperationException(
                "Sauvegarde du registre manquante ou invalide : la correction est annulée.");

        int fixedCount = 0;
        foreach (var issue in issues)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var (root, subKey) = ResolveKey(issue.KeyPath);
                using var key = root?.OpenSubKey(subKey, writable: true);
                if (key is null) continue;

                key.DeleteValue(issue.ValueName, throwOnMissingValue: false);
                fixedCount++;
            }
            catch (Exception ex)
            {
                _logger.Error($"Échec de correction registre : {issue.KeyPath}\\{issue.ValueName}", ex);
            }
        }

        _logger.Info($"Nettoyage registre : {fixedCount} entrée(s) orpheline(s) supprimée(s). " +
                     $"Sauvegarde : {backupFilePath}");
        return fixedCount;
    }

    /// <summary>
    /// Décide si une commande de démarrage est « orpheline » au sens STRICT et défendable :
    /// son exécutable pointe vers un chemin ABSOLU qui n'existe pas (après expansion des
    /// variables d'environnement type <c>%ProgramFiles%</c>).
    ///
    /// SÉCURITÉ : on ne signale JAMAIS une commande dont l'exécutable est un simple nom
    /// (ex. <c>rundll32.exe</c>, <c>powershell.exe</c>) résolu via le PATH — on ne peut pas
    /// vérifier son existence de façon fiable, et la signaler à tort pousserait l'utilisateur
    /// à supprimer une entrée de démarrage parfaitement valide. Évite ce faux positif.
    /// </summary>
    internal static bool IsOrphanedRunCommand(string command, out string expandedExe)
    {
        expandedExe = string.Empty;

        var exe = ExtractExecutablePath(command);
        if (string.IsNullOrWhiteSpace(exe)) return false;

        // Expansion des %VARIABLES% (ex. %ProgramFiles%\App\app.exe).
        exe = Environment.ExpandEnvironmentVariables(exe);
        expandedExe = exe;

        // Nom de commande nu (résolu via le PATH) : indécidable → on ne signale pas.
        if (!Path.IsPathRooted(exe)) return false;

        return !File.Exists(exe);
    }

    /// <summary>Extrait le chemin de l'exécutable d'une commande Run (gère les guillemets).</summary>
    internal static string? ExtractExecutablePath(string command)
    {
        command = command.Trim();
        if (command.Length == 0) return null;

        // Cas "C:\Chemin avec espaces\app.exe" -arg
        if (command[0] == '"')
        {
            var end = command.IndexOf('"', 1);
            return end > 1 ? command[1..end] : null;
        }

        // Cas C:\Chemin\app.exe -arg → on coupe au premier espace.
        var space = command.IndexOf(' ');
        return space > 0 ? command[..space] : command;
    }

    private static (RegistryKey? root, string subKey) ResolveKey(string fullKeyPath)
    {
        var idx = fullKeyPath.IndexOf('\\');
        if (idx < 0) return (null, string.Empty);

        var hive = fullKeyPath[..idx];
        var sub = fullKeyPath[(idx + 1)..];
        RegistryKey? root = hive.ToUpperInvariant() switch
        {
            "HKEY_CURRENT_USER" => Registry.CurrentUser,
            "HKEY_LOCAL_MACHINE" => Registry.LocalMachine,
            _ => null,
        };
        return (root, sub);
    }
}
