using System.Diagnostics;
using System.Management;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;

namespace CleanSlate.Core.Modules;

/// <summary>Fournisseur DNS filtrant proposé par le bloqueur de pub.</summary>
public sealed record DnsProviderOption(
    string Id, string Name, string Primary, string Secondary, string Description)
{
    public override string ToString() => Name;
}

/// <summary>
/// Bloqueur de publicités/traqueurs au niveau DNS : bascule le DNS système vers un
/// fournisseur filtrant au choix (AdGuard par défaut), qui filtre publicités, traqueurs
/// et/ou domaines malveillants pour tous les navigateurs et toutes les applications.
/// Contrairement à l'ancienne approche par fichier hosts (~130 000 entrées), cette
/// méthode est instantanée, n'alourdit aucun processus, et se désactive en un clic
/// (restauration du DNS d'origine sauvegardé avant activation).
/// </summary>
public interface IAdBlockService
{
    /// <summary>Vrai si CleanSlate a basculé le DNS système vers un fournisseur filtrant.</summary>
    bool IsEnabled { get; }

    /// <summary>Description courte de l'état DNS actuel (adaptateurs concernés).</summary>
    string StatusDetails { get; }

    /// <summary>Fournisseurs DNS filtrants proposés.</summary>
    IReadOnlyList<DnsProviderOption> Providers { get; }

    Task EnableAsync(DnsProviderOption provider, IProgress<string>? progress, CancellationToken ct);
    Task DisableAsync(IProgress<string>? progress, CancellationToken ct);
}

[SupportedOSPlatform("windows")]
public sealed class DnsAdBlockService : IAdBlockService
{
    public const string PrimaryDns = "94.140.14.14";
    public const string SecondaryDns = "94.140.15.15";

    /// <summary>Fournisseurs DNS filtrants proposés (le premier est le choix recommandé).</summary>
    public static readonly IReadOnlyList<DnsProviderOption> AllProviders = new[]
    {
        new DnsProviderOption("adguard", "AdGuard DNS", PrimaryDns, SecondaryDns,
            "Bloque publicités, traqueurs et domaines malveillants (recommandé)."),
        new DnsProviderOption("adguard-family", "AdGuard Family", "94.140.14.15", "94.140.15.16",
            "Comme AdGuard DNS, avec en plus le blocage des contenus pour adultes (contrôle parental)."),
        new DnsProviderOption("cloudflare-security", "Cloudflare Security", "1.1.1.2", "1.0.0.2",
            "Bloque uniquement les domaines malveillants — pas les publicités. Très rapide."),
        new DnsProviderOption("quad9", "Quad9", "9.9.9.9", "149.112.112.112",
            "Bloque les domaines malveillants — pas les publicités. Axé confidentialité."),
    };

    public IReadOnlyList<DnsProviderOption> Providers => AllProviders;

    private static readonly string BackupPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "CleanSlate", "adblock_dns_backup.json");

    // ---- Nettoyage de l'ancien blocage par fichier hosts (versions <= v0.9.2) ----
    private const string LegacyStartMarker = "# ==== CleanSlate AdBlock START ====";
    private const string LegacyEndMarker   = "# ==== CleanSlate AdBlock END ====";

    private static readonly string HostsPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                     "drivers", "etc", "hosts");

    public bool IsEnabled => File.Exists(BackupPath);

    public string StatusDetails
    {
        get
        {
            var adapters = new List<ManagementObject>();
            try
            {
                adapters = GetActiveAdapters();
                if (adapters.Count == 0) return "Aucun adaptateur réseau actif détecté.";

                var names = string.Join(", ", adapters.Select(DescribeAdapter));
                if (!IsEnabled)
                    return $"DNS système (par défaut/FAI) sur : {names}.";

                var provider = ReadActiveProvider();
                return $"DNS {provider.Name} ({provider.Primary} / {provider.Secondary}) actif sur : {names}.";
            }
            catch (Exception ex) { return $"Statut DNS indisponible : {ex.Message}"; }
            finally
            {
                foreach (var a in adapters) a.Dispose();
            }
        }
    }

    /// <summary>Fournisseur actif d'après la sauvegarde (AdGuard si format ancien/inconnu).</summary>
    private static DnsProviderOption ReadActiveProvider()
    {
        try
        {
            var (providerId, _) = ParseBackup(File.ReadAllText(BackupPath));
            return AllProviders.FirstOrDefault(p => p.Id == providerId) ?? AllProviders[0];
        }
        catch { return AllProviders[0]; }
    }

    public Task EnableAsync(DnsProviderOption provider, IProgress<string>? progress, CancellationToken ct) => Task.Run(() =>
    {
        progress?.Report("Lecture de la configuration réseau…");
        var adapters = GetActiveAdapters();
        try
        {
            if (adapters.Count == 0)
                throw new InvalidOperationException("Aucun adaptateur réseau actif détecté.");

            // Si un fournisseur est déjà actif, on conserve la sauvegarde du DNS
            // D'ORIGINE (celle du premier Enable) : changer de fournisseur ne doit
            // pas faire perdre la configuration initiale de l'utilisateur.
            Dictionary<string, string[]> backup;
            if (IsEnabled)
            {
                (_, backup) = ParseBackup(File.ReadAllText(BackupPath));
            }
            else
            {
                progress?.Report("Sauvegarde de la configuration DNS actuelle…");
                backup = new Dictionary<string, string[]>();
                foreach (var adapter in adapters)
                {
                    var settingId = (adapter["SettingID"] as string) ?? string.Empty;
                    if (string.IsNullOrEmpty(settingId)) continue;
                    var current = (adapter["DNSServerSearchOrder"] as string[]) ?? Array.Empty<string>();
                    backup[settingId] = current;
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(BackupPath)!);
            File.WriteAllText(BackupPath, SerializeBackup(provider.Id, backup), Encoding.UTF8);

            progress?.Report($"Application du DNS {provider.Name}…");
            foreach (var adapter in adapters)
                SetDns(adapter, new[] { provider.Primary, provider.Secondary });
        }
        finally
        {
            foreach (var a in adapters) a.Dispose();
        }

        progress?.Report("Vidage du cache DNS…");
        FlushDns();
    }, ct);

    public Task DisableAsync(IProgress<string>? progress, CancellationToken ct) => Task.Run(() =>
    {
        progress?.Report("Restauration de la configuration DNS d'origine…");

        Dictionary<string, string[]>? backup = null;
        if (File.Exists(BackupPath))
        {
            try { (_, backup) = ParseBackup(File.ReadAllText(BackupPath)); }
            catch { backup = null; }
        }

        var adapters = GetActiveAdapters();
        try
        {
            foreach (var adapter in adapters)
            {
                var settingId = (adapter["SettingID"] as string) ?? string.Empty;
                var original = backup is not null && backup.TryGetValue(settingId, out var dns)
                    ? dns
                    : Array.Empty<string>();
                SetDns(adapter, original);
            }
        }
        finally
        {
            foreach (var a in adapters) a.Dispose();
        }

        if (File.Exists(BackupPath)) File.Delete(BackupPath);

        progress?.Report("Vidage du cache DNS…");
        FlushDns();
    }, ct);

    /// <summary>Sérialise la sauvegarde DNS : fournisseur choisi + DNS d'origine par adaptateur.</summary>
    internal static string SerializeBackup(string providerId, Dictionary<string, string[]> adapters) =>
        JsonSerializer.Serialize(new BackupFile { Provider = providerId, Adapters = adapters });

    /// <summary>
    /// Lit une sauvegarde DNS. Deux formats acceptés : le format actuel (objet avec
    /// « Provider » + « Adapters ») et l'ancien format (dictionnaire adaptateur → DNS,
    /// versions ≤ v1.1.5, où le fournisseur était toujours AdGuard).
    /// </summary>
    internal static (string ProviderId, Dictionary<string, string[]> Adapters) ParseBackup(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind == JsonValueKind.Object &&
            doc.RootElement.TryGetProperty("Provider", out _))
        {
            var file = JsonSerializer.Deserialize<BackupFile>(json);
            return (file?.Provider ?? "adguard", file?.Adapters ?? new Dictionary<string, string[]>());
        }

        var legacy = JsonSerializer.Deserialize<Dictionary<string, string[]>>(json);
        return ("adguard", legacy ?? new Dictionary<string, string[]>());
    }

    private sealed class BackupFile
    {
        public string Provider { get; set; } = "adguard";
        public Dictionary<string, string[]> Adapters { get; set; } = new();
    }

    private static List<ManagementObject> GetActiveAdapters()
    {
        var list = new List<ManagementObject>();
        using var searcher = new ManagementObjectSearcher(
            "SELECT * FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = TRUE");
        foreach (var obj in searcher.Get())
            list.Add((ManagementObject)obj);
        return list;
    }

    private static string DescribeAdapter(ManagementObject adapter) =>
        (adapter["Description"] as string) ?? "Adaptateur réseau";

    private static void SetDns(ManagementObject adapter, string[] servers)
    {
        using var inParams = adapter.GetMethodParameters("SetDNSServerSearchOrder");
        inParams["DNSServerSearchOrder"] = servers.Length == 0 ? null : servers;
        adapter.InvokeMethod("SetDNSServerSearchOrder", inParams, null);
    }

    private static void FlushDns()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ipconfig", "/flushdns")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            })?.WaitForExit(5000);
        }
        catch { /* best effort */ }
    }

    /// <summary>
    /// Supprime le bloc d'entrées ajouté par l'ancienne version (fichier hosts,
    /// ~130 000 domaines) qui rendait le PC très lent et ne pouvait pas être
    /// désactivé sans Mode sans échec. Appelé une fois au démarrage.
    /// </summary>
    public static void CleanupLegacyHostsBlock()
    {
        try
        {
            var content = File.ReadAllText(HostsPath);
            var cleaned = StripLegacyBlock(content);
            if (cleaned == content) return;

            File.WriteAllText(HostsPath, cleaned, Encoding.UTF8);
            FlushDns();
        }
        catch { /* best effort : nécessite des droits administrateur */ }
    }

    /// <summary>Logique pure de suppression du bloc, testable indépendamment du fichier hosts.</summary>
    internal static string StripLegacyBlock(string hostsContent)
    {
        var start = hostsContent.IndexOf(LegacyStartMarker, StringComparison.Ordinal);
        if (start < 0) return hostsContent;

        var end = hostsContent.IndexOf(LegacyEndMarker, start, StringComparison.Ordinal);
        var cleaned = end < 0
            ? hostsContent[..start]
            : hostsContent[..start] + hostsContent[(end + LegacyEndMarker.Length)..];

        return cleaned.TrimEnd() + Environment.NewLine;
    }
}
