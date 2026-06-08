using Microsoft.Win32;
using System.Runtime.Versioning;
using CleanSlate.Core.Abstractions;

namespace CleanSlate.Core.Modules;

/// <summary>
/// Module 5a — Gestion des programmes au démarrage (implémentation réelle).
///
/// Couvre les clés Run (HKCU + HKLM) et le dossier « Démarrage ».
/// Principe de SÛRETÉ : on DÉSACTIVE de façon RÉVERSIBLE, jamais on ne supprime
/// définitivement :
///   - entrées de registre : la valeur est déplacée vers une clé de sauvegarde
///     CleanSlate (HKCU\Software\CleanSlate\DisabledStartup), puis restaurée à la
///     réactivation ;
///   - raccourcis du dossier Démarrage : le fichier est déplacé dans un
///     sous-dossier « Désactivé (CleanSlate) », puis remis à la réactivation.
///
/// Les tâches planifiées au logon ne sont pas modifiées ici (nécessitent le
/// planificateur de tâches) — listées comme évolution possible.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class StartupManager : IStartupManager
{
    private const string RunSubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string BackupSubKeyCurrentUser = @"Software\CleanSlate\DisabledStartup\HKCU";
    private const string BackupSubKeyLocalMachine = @"Software\CleanSlate\DisabledStartup\HKLM";
    private const string DisabledFolderName = "Désactivé (CleanSlate)";

    private readonly IActionLogger _logger;

    public StartupManager(IActionLogger logger) => _logger = logger;

    public IReadOnlyList<StartupEntry> ListStartupEntries()
    {
        var entries = new List<StartupEntry>();

        // Entrées actives (clés Run).
        entries.AddRange(ReadRunKey(Registry.CurrentUser, RunSubKey,
            StartupLocation.RegistryRunCurrentUser, isEnabled: true));
        entries.AddRange(ReadRunKey(Registry.LocalMachine, RunSubKey,
            StartupLocation.RegistryRunLocalMachine, isEnabled: true));

        // Entrées désactivées (déplacées dans notre clé de sauvegarde).
        entries.AddRange(ReadRunKey(Registry.CurrentUser, BackupSubKeyCurrentUser,
            StartupLocation.RegistryRunCurrentUser, isEnabled: false));
        entries.AddRange(ReadRunKey(Registry.LocalMachine, BackupSubKeyLocalMachine,
            StartupLocation.RegistryRunLocalMachine, isEnabled: false));

        // Dossier Démarrage (raccourcis actifs + désactivés).
        entries.AddRange(ReadStartupFolder());

        return entries.OrderBy(e => e.Name).ToList();
    }

    public void SetEnabled(StartupEntry entry, bool enabled)
    {
        if (entry.IsEnabled == enabled)
            return; // rien à faire

        switch (entry.Location)
        {
            case StartupLocation.RegistryRunCurrentUser:
                ToggleRegistryEntry(Registry.CurrentUser, BackupSubKeyCurrentUser, entry, enabled);
                break;
            case StartupLocation.RegistryRunLocalMachine:
                ToggleRegistryEntry(Registry.LocalMachine, BackupSubKeyLocalMachine, entry, enabled);
                break;
            case StartupLocation.StartupFolder:
                ToggleStartupFolderEntry(entry, enabled);
                break;
            default:
                _logger.Warning($"Type d'entrée de démarrage non géré : {entry.Location}");
                break;
        }

        _logger.Info($"Démarrage : '{entry.Name}' {(enabled ? "activé" : "désactivé")}.");
    }

    // ----------------------------------------------------------------- Registre

    private static IEnumerable<StartupEntry> ReadRunKey(
        RegistryKey root, string subKey, StartupLocation location, bool isEnabled)
    {
        using var key = root.OpenSubKey(subKey);
        if (key is null) yield break;

        foreach (var name in key.GetValueNames())
        {
            if (string.IsNullOrEmpty(name)) continue;
            var command = key.GetValue(name)?.ToString() ?? string.Empty;
            yield return new StartupEntry(name, command, location, isEnabled);
        }
    }

    private void ToggleRegistryEntry(
        RegistryKey root, string backupSubKey, StartupEntry entry, bool enable)
    {
        // Source et destination selon le sens (activer = backup → Run ; désactiver = Run → backup).
        var (fromSub, toSub) = enable
            ? (backupSubKey, RunSubKey)
            : (RunSubKey, backupSubKey);

        using var from = root.OpenSubKey(fromSub, writable: true);
        using var to = root.CreateSubKey(toSub, writable: true);
        if (from is null || to is null)
            throw new InvalidOperationException("Impossible d'accéder aux clés de registre (droits administrateur requis pour HKLM ?).");

        var value = from.GetValue(entry.Name);
        if (value is null)
            return;

        to.SetValue(entry.Name, value);
        from.DeleteValue(entry.Name, throwOnMissingValue: false);
    }

    // --------------------------------------------------------------- Dossier Démarrage

    private static string StartupFolderPath => Environment.GetFolderPath(Environment.SpecialFolder.Startup);
    private static string DisabledFolderPath => Path.Combine(StartupFolderPath, DisabledFolderName);

    private static IEnumerable<StartupEntry> ReadStartupFolder()
    {
        var folder = StartupFolderPath;
        if (!Directory.Exists(folder)) yield break;

        // Raccourcis actifs (à la racine du dossier Démarrage).
        foreach (var file in Directory.EnumerateFiles(folder, "*.lnk", SearchOption.TopDirectoryOnly))
            yield return new StartupEntry(Path.GetFileNameWithoutExtension(file), file,
                StartupLocation.StartupFolder, IsEnabled: true);

        // Raccourcis désactivés (dans le sous-dossier CleanSlate).
        if (Directory.Exists(DisabledFolderPath))
            foreach (var file in Directory.EnumerateFiles(DisabledFolderPath, "*.lnk", SearchOption.TopDirectoryOnly))
                yield return new StartupEntry(Path.GetFileNameWithoutExtension(file), file,
                    StartupLocation.StartupFolder, IsEnabled: false);
    }

    private static void ToggleStartupFolderEntry(StartupEntry entry, bool enable)
    {
        // entry.Command contient le chemin actuel du .lnk.
        var current = entry.Command;
        if (!File.Exists(current)) return;

        var fileName = Path.GetFileName(current);
        string destination;
        if (enable)
        {
            destination = Path.Combine(StartupFolderPath, fileName);
        }
        else
        {
            Directory.CreateDirectory(DisabledFolderPath);
            destination = Path.Combine(DisabledFolderPath, fileName);
        }

        if (!string.Equals(current, destination, StringComparison.OrdinalIgnoreCase))
            File.Move(current, destination, overwrite: true);
    }
}
