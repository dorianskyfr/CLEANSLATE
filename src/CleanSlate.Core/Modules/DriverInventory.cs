using System.Management;
using System.Runtime.Versioning;

namespace CleanSlate.Core.Modules;

/// <summary>Information sur un pilote installé.</summary>
public sealed record DriverInfo(
    string DeviceName,
    string? DriverVersion,
    DateTime? DriverDate,
    string? Manufacturer,
    string? DeviceClass);

/// <summary>
/// Module 2 — Détection de pilotes obsolètes.
///
/// ⚠️ LIMITE FONDAMENTALE (voir docs/LIMITES-TECHNIQUES.md) : il n'existe AUCUNE
/// API universelle et gratuite donnant « la dernière version officielle » d'un
/// pilote. Ce module fait donc ce qui est honnêtement faisable :
///   1. INVENTORIER les pilotes installés (fiable — implémenté ici via WMI).
///   2. Déléguer la recherche de mises à jour à Windows Update (à brancher sur
///      l'agent WUA — voir CheckUpdatesViaWindowsUpdateAsync).
///   3. Renvoyer vers les pages constructeurs (pas de comparaison automatique).
///
/// On s'INTERDIT d'inventer un numéro de version « cible » non vérifiable.
/// </summary>
public interface IDriverInventory
{
    /// <summary>Liste les pilotes signés installés. Fiable et complet.</summary>
    IReadOnlyList<DriverInfo> ListInstalledDrivers();
}

/// <summary>Implémentation par WMI (Win32_PnPSignedDriver).</summary>
[SupportedOSPlatform("windows")]
public sealed class WmiDriverInventory : IDriverInventory
{
    public IReadOnlyList<DriverInfo> ListInstalledDrivers()
    {
        var drivers = new List<DriverInfo>();

        // Win32_PnPSignedDriver expose les pilotes signés avec version et date.
        using var searcher = new ManagementObjectSearcher(
            "SELECT DeviceName, DriverVersion, DriverDate, Manufacturer, DeviceClass " +
            "FROM Win32_PnPSignedDriver");

        foreach (var obj in searcher.Get())
        {
            var name = obj["DeviceName"] as string;
            if (string.IsNullOrWhiteSpace(name))
                continue; // on ignore les entrées sans nom de périphérique

            drivers.Add(new DriverInfo(
                DeviceName: name!,
                DriverVersion: obj["DriverVersion"] as string,
                DriverDate: ParseWmiDate(obj["DriverDate"] as string),
                Manufacturer: obj["Manufacturer"] as string,
                DeviceClass: obj["DeviceClass"] as string));
        }

        return drivers
            .OrderBy(d => d.DeviceClass)
            .ThenBy(d => d.DeviceName)
            .ToList();
    }

    /// <summary>Convertit une date WMI (CIM_DATETIME) en DateTime.</summary>
    private static DateTime? ParseWmiDate(string? cimDate)
    {
        if (string.IsNullOrWhiteSpace(cimDate) || cimDate!.Length < 8)
            return null;
        try { return ManagementDateTimeConverter.ToDateTime(cimDate); }
        catch { return null; }
    }

    // ---------------------------------------------------------------------
    // Piste d'implémentation pour la recherche de mises à jour (étape 2).
    // À brancher sur l'agent Windows Update (COM "Microsoft.Update.Session",
    // critère "Type='Driver'"). Laissé NON implémenté car il interagit avec un
    // service système et mérite ses propres tests d'intégration.
    // ---------------------------------------------------------------------
    // public Task<IReadOnlyList<DriverUpdate>> CheckUpdatesViaWindowsUpdateAsync(
    //     CancellationToken ct) => throw new NotImplementedException(
    //     "À implémenter via WUApiLib (Microsoft.Update.Session).");
}
