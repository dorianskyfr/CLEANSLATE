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

            var info = new NativeMethods.SHQUERYRBINFO
            {
                cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.SHQUERYRBINFO>()
            };

            // pszRootPath = null → toutes les corbeilles de toutes les unités.
            int hr = NativeMethods.SHQueryRecycleBin(null, ref info);
            if (hr != 0 || info.i64NumItems == 0)
                return ScanResult.Empty(Id, DisplayName);

            // On expose un unique « élément agrégé » représentant la corbeille,
            // dont la taille reflète le contenu réel (pour l'aperçu).
            var item = new CleanableItem(
                path: "Corbeille (toutes les unités)",
                sizeBytes: info.i64Size,
                category: CleaningCategory.Corbeille,
                isDirectory: false,
                providerId: Id);

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
}
