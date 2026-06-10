using System.IO;
using System.Runtime.InteropServices;
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

            long size = 0;
            long count = 0;

            var info = new NativeMethods.SHQUERYRBINFO
            {
                cbSize = Marshal.SizeOf<NativeMethods.SHQUERYRBINFO>()
            };

            // pszRootPath = null → toutes les corbeilles de toutes les unités.
            // SHQueryRecycleBin renvoie S_OK (0) OU S_FALSE (1) en succès : sur
            // certaines configs Windows 11 il renvoie S_FALSE alors que la corbeille
            // contient bien des fichiers. On accepte donc 0 et 1.
            int hr = NativeMethods.SHQueryRecycleBin(null, ref info);
            if ((hr == 0 || hr == 1) && info.i64Size >= 0 && info.i64NumItems >= 0)
            {
                size = info.i64Size;
                count = info.i64NumItems;
            }

            // Repli robuste : si l'API renvoie 0 (ou échoue), on mesure directement
            // le contenu des dossiers $Recycle.Bin de chaque lecteur fixe. Cela évite
            // l'affichage erroné « 0 o / — » quand la corbeille n'est pas vide.
            if (count == 0 || size == 0)
            {
                var (fsSize, fsCount) = MeasureRecycleBinFromDisk(ct);
                if (fsCount > 0)
                {
                    size = fsSize;
                    count = fsCount;
                }
            }

            if (count == 0)
                return ScanResult.Empty(Id, DisplayName);

            var item = new CleanableItem(
                path: $"Corbeille ({count} élément(s), toutes les unités)",
                sizeBytes: size,
                category: CleaningCategory.Corbeille,
                isDirectory: false,
                providerId:  Id);

            return new ScanResult(Id, DisplayName, new[] { item }, Array.Empty<string>());
        }, ct);
    }

    /// <summary>
    /// Mesure le contenu réel des corbeilles en parcourant les dossiers
    /// <c>&lt;lecteur&gt;:\$Recycle.Bin</c> de toutes les unités fixes. Les fichiers
    /// de données portent le préfixe <c>$R</c> ; les métadonnées <c>$I</c> sont
    /// minuscules et ignorées dans le décompte des éléments.
    /// </summary>
    private static (long size, long count) MeasureRecycleBinFromDisk(CancellationToken ct)
    {
        long size = 0;
        long count = 0;

        foreach (var drive in DriveInfo.GetDrives())
        {
            ct.ThrowIfCancellationRequested();
            if (!drive.IsReady || drive.DriveType != DriveType.Fixed) continue;

            var binRoot = Path.Combine(drive.RootDirectory.FullName, "$Recycle.Bin");
            if (!Directory.Exists(binRoot)) continue;

            // Chaque sous-dossier correspond au SID d'un utilisateur.
            IEnumerable<string> sidDirs;
            try { sidDirs = Directory.EnumerateDirectories(binRoot); }
            catch { continue; }

            foreach (var sidDir in sidDirs)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    foreach (var file in Directory.EnumerateFiles(sidDir, "*", SearchOption.AllDirectories))
                    {
                        try
                        {
                            var fi = new FileInfo(file);
                            size += fi.Length;
                            // On compte les fichiers de données $R… comme « éléments ».
                            if (Path.GetFileName(file).StartsWith("$R", StringComparison.OrdinalIgnoreCase))
                                count++;
                        }
                        catch { /* fichier verrouillé / inaccessible : ignoré */ }
                    }
                }
                catch { /* accès refusé sur la corbeille d'un autre utilisateur : ignoré */ }
            }
        }

        return (size, count);
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
}
