using System.Diagnostics;
using System.Runtime.Versioning;
using System.ServiceProcess;
using System.Text.Json;
using CleanSlate.Core.Abstractions;
using CleanSlate.Core.Native;

namespace CleanSlate.Core.Modules;

/// <summary>
/// Implémentation du Mode Jeu : suspension/reprise de processus (ntdll) + arrêt/
/// redémarrage de services (ServiceController), avec persistance du snapshot pour
/// une restauration fiable même après fermeture brutale.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class GameModeService : IGameMode
{
    private readonly IActionLogger _logger;
    private readonly string _stateFile;
    private GameModeSnapshot? _active;

    public GameModeService(IActionLogger logger, string? stateFile = null)
    {
        _logger = logger;
        _stateFile = stateFile ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CleanSlate", "gamemode-state.json");
    }

    public bool IsActive => _active is not null;

    public Task<GameModeSnapshot> ActivateAsync(GameModeOptions options, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            var snapshot = new GameModeSnapshot();

            // L'état est marqué actif et persisté DÈS qu'au moins un élément a été suspendu/arrêté,
            // et ré-persisté dans le finally quoi qu'il arrive (annulation, exception) : un processus
            // suspendu doit TOUJOURS rester récupérable, jamais gelé sans trace de restauration.
            try
            {
                // 1. Suspendre les processus de la liste blanche.
                foreach (var name in options.ProcessNamesToSuspend)
                {
                    ct.ThrowIfCancellationRequested();
                    foreach (var proc in Process.GetProcessesByName(name))
                    {
                        using (proc) // libère le handle kernel ouvert par Process
                        {
                            try
                            {
                                var status = NativeMethods.NtSuspendProcess(proc.Handle);
                                if (status == 0)
                                {
                                    snapshot.SuspendedProcessIds.Add(proc.Id);
                                    _active = snapshot;
                                    PersistSnapshot(snapshot); // récupérable immédiatement
                                }
                            }
                            catch (Exception ex)
                            {
                                // Processus protégé / déjà parti : on ignore, jamais de force.
                                _logger.Warning($"Mode Jeu : impossible de suspendre {name} ({proc.Id}) : {ex.Message}");
                            }
                        }
                    }
                }

                // 2. Arrêter les services déclarés (s'ils tournent).
                foreach (var serviceName in options.ServiceNamesToStop)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        using var sc = new ServiceController(serviceName);
                        if (sc.Status == ServiceControllerStatus.Running && sc.CanStop)
                        {
                            sc.Stop();
                            sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
                            snapshot.StoppedServices.Add(serviceName);
                            _active = snapshot;
                            PersistSnapshot(snapshot);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Mode Jeu : impossible d'arrêter le service {serviceName} : {ex.Message}");
                    }
                }
            }
            finally
            {
                if (snapshot.SuspendedProcessIds.Count > 0 || snapshot.StoppedServices.Count > 0)
                {
                    _active = snapshot;
                    PersistSnapshot(snapshot);
                }
            }

            _logger.Info($"Mode Jeu activé : {snapshot.SuspendedProcessIds.Count} processus suspendu(s), " +
                         $"{snapshot.StoppedServices.Count} service(s) arrêté(s).");
            return snapshot;
        }, ct);
    }

    public Task RestoreAsync(CancellationToken ct)
    {
        return Task.Run(() =>
        {
            var snapshot = _active ?? LoadSnapshot();
            if (snapshot is null)
                return;

            RestoreSnapshot(snapshot, ct);

            _active = null;
            DeletePersistedSnapshot();
            _logger.Info("Mode Jeu désactivé : état restauré.");
        }, ct);
    }

    public Task<bool> TryRecoverAsync(CancellationToken ct)
    {
        return Task.Run(() =>
        {
            var snapshot = LoadSnapshot();
            if (snapshot is null)
                return false;

            _logger.Warning("Mode Jeu : snapshot orphelin détecté (fermeture brutale ?). Restauration…");
            RestoreSnapshot(snapshot, ct);
            DeletePersistedSnapshot();
            return true;
        }, ct);
    }

    // ----------------------------------------------------------------- privé

    private void RestoreSnapshot(GameModeSnapshot snapshot, CancellationToken ct)
    {
        // Reprendre les processus suspendus.
        foreach (var pid in snapshot.SuspendedProcessIds)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var proc = Process.GetProcessById(pid);
                NativeMethods.NtResumeProcess(proc.Handle);
            }
            catch (Exception ex)
            {
                // Le processus a pu se terminer entre-temps : sans gravité.
                _logger.Warning($"Mode Jeu : reprise du processus {pid} impossible : {ex.Message}");
            }
        }

        // Redémarrer les services arrêtés.
        foreach (var serviceName in snapshot.StoppedServices)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var sc = new ServiceController(serviceName);
                if (sc.Status == ServiceControllerStatus.Stopped)
                {
                    sc.Start();
                    sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Mode Jeu : redémarrage du service {serviceName} impossible : {ex.Message}");
            }
        }
    }

    private void PersistSnapshot(GameModeSnapshot snapshot)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_stateFile)!);
            File.WriteAllText(_stateFile, JsonSerializer.Serialize(snapshot));
        }
        catch (Exception ex)
        {
            _logger.Error("Mode Jeu : échec de persistance du snapshot.", ex);
        }
    }

    private GameModeSnapshot? LoadSnapshot()
    {
        try
        {
            if (!File.Exists(_stateFile)) return null;
            return JsonSerializer.Deserialize<GameModeSnapshot>(File.ReadAllText(_stateFile));
        }
        catch
        {
            return null;
        }
    }

    private void DeletePersistedSnapshot()
    {
        try { if (File.Exists(_stateFile)) File.Delete(_stateFile); }
        catch { /* sans gravité */ }
    }
}
