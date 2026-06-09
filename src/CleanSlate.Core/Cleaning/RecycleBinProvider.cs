using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using CleanSlate.Core.Abstractions;
using CleanSlate.Core.Models;
using CleanSlate.Core.Native;

namespace CleanSlate.Core.Cleaning;

/// <summary>
/// Vidage de la corbeille via l'API shell officielle (SHEmptyRecycleBin), plutôt
/// que la suppression manuelle de $Recycle.Bin (plus risquée et incorrecte).
///
/// Particularité : on ne peut pas lister fichier par fichier proprement. On
/// interroge donc la taille/le nombre via SHQueryRecycleBin pour l'aperçu, et le
/// vidage est une action « tout ou rien ». IRRÉVERSIBLE → confirmation forcée.
/// </summary>
public sealed class RecycleBinProvider : ICleaningProvider
{
    private readonly IActionLogger _logger;

    public RecycleBinProvider(IActionLogger logger) => _logger = logger;

    public string Id => "recycle-bin";
    public string DisplayName => "Corbeille";
    public CleaningCategory Category => CleaningCategory.Corbeille;
    public CleaningSeverity Severity => CleaningSeverity.Avertissement;
    public bool RequiresAdministrator => false;

    public string Description =>
        "Vide définitivement la corbeille (toutes les unités). ⚠️ Action " +
        "IRRÉVERSIBLE : les fichiers ne pourront plus être restaurés. Une " +
        "confirmation vous sera demandée.";

    public Task<ScanResult> ScanAsync(IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            progress?.Report(new ScanProgress(DisplayName, "Corbeille"));

            long totalSize  = 0;
            long totalItems = 0;

            // Méthode 1 : SHQueryRecycleBin (API officielle, null = toutes les unités).
            // HRESULT négatif = erreur ; S_FALSE (1) = succès sans éléments (on ignore).
            var info = new NativeMethods.SHQUERYRBINFO
            {
                cbSize = Marshal.SizeOf<NativeMethods.SHQUERYRBINFO>()
            };
            int hr = NativeMethods.SHQueryRecycleBin(null, ref info);

            if (hr >= 0 && info.i64NumItems > 0)
            {
                totalSize  = info.i64Size;
                totalItems = info.i64NumItems;
            }
            else
            {
                // Méthode 2 (repli) : cibler directement le dossier $Recycle.Bin de
                // l'utilisateur courant (via son SID). Plus fiable que d'énumérer tous
                // les sous-dossiers, qui peuvent être inaccessibles (autres utilisateurs).
                var currentSid = WindowsIdentity.GetCurrent().User?.ToString();

                foreach (var drive in DriveInfo.GetDrives()
                    .Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
                {
                    var rbRoot = Path.Combine(drive.Name, "$Recycle.Bin");
                    if (!Directory.Exists(rbRoot)) continue;

                    // Préférer le dossier SID courant ; sinon énumérer tous les dossiers.
                    IEnumerable<string> userDirs;
                    if (currentSid != null)
                    {
                        var userDir = Path.Combine(rbRoot, currentSid);
                        userDirs = Directory.Exists(userDir)
                            ? (IEnumerable<string>)new[] { userDir }
                            : GetSubDirectoriesSafe(rbRoot);
                    }
                    else
                    {
                        userDirs = GetSubDirectoriesSafe(rbRoot);
                    }

                    foreach (var dir in userDirs)
                    {
                        try
                        {
                            // Les fichiers recyclés portent le préfixe $R
                            foreach (var f in Directory.GetFiles(dir, "$R*", SearchOption.AllDirectories))
                            {
                                try { totalSize += new FileInfo(f).Length; totalItems++; }
                                catch { }
                            }
                        }
                        catch { }
                    }
                }
            }

            if (totalItems == 0)
                return ScanResult.Empty(Id, DisplayName);

            var item = new CleanableItem(
                path:        "Corbeille (toutes les unités)",
                sizeBytes:   totalSize,
                category:    CleaningCategory.Corbeille,
                isDirectory: false,
                providerId:  Id);

            return new ScanResult(Id, DisplayName, new[] { item }, Array.Empty<string>());
        }, ct);
    }

    public Task<CleanResult> CleanAsync(
        IReadOnlyCollection<CleanableItem> items,
        IProgress<CleanProgress>? progress,
        CancellationToken ct)
    {
        return Task.Run(() =>
        {
            if (items.Count == 0)
                return new CleanResult(0, 0, 0, Array.Empty<string>());

            long sizeBefore = items.Sum(i => i.SizeBytes);
            progress?.Report(new CleanProgress(0, 1, "Vidage de la corbeille"));

            var flags = NativeMethods.RecycleFlags.SHERB_NOCONFIRMATION
                      | NativeMethods.RecycleFlags.SHERB_NOPROGRESSUI
                      | NativeMethods.RecycleFlags.SHERB_NOSOUND;

            int hr = NativeMethods.SHEmptyRecycleBin(IntPtr.Zero, null, flags);
            progress?.Report(new CleanProgress(1, 1, "Vidage de la corbeille"));

            // S_OK (0) ou code « déjà vide » sont considérés comme un succès.
            if (hr == 0)
            {
                _logger.LogCleaning(Id, deleted: 1, failed: 0, freedBytes: sizeBefore);
                return new CleanResult(sizeBefore, 1, 0, Array.Empty<string>());
            }

            var err = $"Échec du vidage de la corbeille (HRESULT 0x{hr:X8}).";
            _logger.Error(err);
            return new CleanResult(0, 0, 1, new[] { err });
        }, ct);
    }

    private static IEnumerable<string> GetSubDirectoriesSafe(string path)
    {
        try { return Directory.GetDirectories(path); }
        catch { return Array.Empty<string>(); }
    }
}
