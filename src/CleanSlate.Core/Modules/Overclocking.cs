using System.Management;
using System.Runtime.Versioning;

namespace CleanSlate.Core.Modules;

/// <summary>Carte graphique détectée sur la machine.</summary>
public sealed record GpuInfo(
    string Name,
    GpuVendor Vendor,
    long VramBytes,
    string? DriverVersion,
    bool IsIntegrated)
{
    public string VramDisplay
    {
        get
        {
            if (VramBytes <= 0) return "inconnue";
            double gb = VramBytes / 1024d / 1024d / 1024d;
            return gb >= 1 ? $"{gb:0.#} Go" : $"{VramBytes / 1024d / 1024d:0} Mo";
        }
    }
}

public enum GpuVendor { Nvidia, Amd, Intel, Unknown }

/// <summary>
/// Profil d'overclocking RECOMMANDÉ — le « sweet spot » entre gain de performance
/// et stabilité (pas de crash). Ce sont des valeurs de DÉPART prudentes à appliquer
/// dans un outil dédié (MSI Afterburner, AMD Adrenalin, Intel Arc Control), pas des
/// réglages appliqués automatiquement.
/// </summary>
public sealed record OverclockProfile(
    int CoreOffsetMhz,
    int MemoryOffsetMhz,
    int PowerLimitPercent,
    int TempLimitC,
    string FanStrategy,
    string Rationale,
    IReadOnlyList<string> Steps,
    bool Recommended);

/// <summary>
/// Module Overclocking (sous-catégorie du Mode Jeu).
///
/// ⚠️ HONNÊTETÉ TECHNIQUE : il n'existe AUCUNE API Windows universelle, gratuite et
/// sûre pour appliquer un overclock GPU (cela passe par NVAPI / ADL / pilotes
/// propriétaires, propre à chaque marque et risqué). CleanSlate fait donc le maximum
/// utile et SÛR : il détecte la carte graphique et propose un profil de départ
/// équilibré, à appliquer pas à pas dans l'outil officiel du constructeur, avec un
/// test de stabilité. On ne touche jamais au matériel directement.
/// </summary>
public interface IOverclockingAdvisor
{
    /// <summary>Détecte les cartes graphiques installées (WMI).</summary>
    IReadOnlyList<GpuInfo> DetectGpus();

    /// <summary>Calcule un profil d'overclock recommandé pour une carte donnée.</summary>
    OverclockProfile Recommend(GpuInfo gpu);
}

[SupportedOSPlatform("windows")]
public sealed class OverclockingAdvisor : IOverclockingAdvisor
{
    public IReadOnlyList<GpuInfo> DetectGpus()
    {
        var gpus = new List<GpuInfo>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, AdapterRAM, DriverVersion, AdapterCompatibility, VideoProcessor " +
                "FROM Win32_VideoController");

            foreach (var obj in searcher.Get())
            {
                var name = (obj["Name"] as string)?.Trim();
                if (string.IsNullOrWhiteSpace(name)) continue;

                var compat = (obj["AdapterCompatibility"] as string) ?? string.Empty;
                var vendor = DetectVendor(name + " " + compat);

                long vram = 0;
                try { vram = Convert.ToInt64(obj["AdapterRAM"] ?? 0L); } catch { /* certains pilotes renvoient 0 */ }

                gpus.Add(new GpuInfo(
                    Name: name!,
                    Vendor: vendor,
                    VramBytes: vram,
                    DriverVersion: obj["DriverVersion"] as string,
                    IsIntegrated: IsIntegrated(name!, vendor)));
            }
        }
        catch { /* WMI indisponible : liste vide */ }

        // Carte dédiée en premier (la plus pertinente pour l'overclocking).
        return gpus
            .OrderBy(g => g.IsIntegrated)
            .ThenByDescending(g => g.VramBytes)
            .ToList();
    }

    public OverclockProfile Recommend(GpuInfo gpu)
    {
        // Les iGPU (graphiques intégrés) ne s'overclockent pas utilement / sûrement.
        if (gpu.IsIntegrated)
        {
            return new OverclockProfile(
                CoreOffsetMhz: 0, MemoryOffsetMhz: 0, PowerLimitPercent: 0, TempLimitC: 0,
                FanStrategy: "—",
                Rationale: "Carte graphique intégrée détectée. L'overclocking d'un iGPU " +
                           "n'apporte quasiment rien et dépend surtout du refroidissement du " +
                           "portable/PC. Recommandation : ne pas overclocker — privilégiez un " +
                           "bon profil d'alimentation « Performances » et un refroidissement propre.",
                Steps: new[] { "Aucune action d'overclocking recommandée pour un GPU intégré." },
                Recommended: false);
        }

        // Échelle prudente selon la marque. Les offsets mémoire sont plus tolérants
        // que le cœur ; on vise la stabilité (pas de crash) avant le pic de FPS.
        return gpu.Vendor switch
        {
            GpuVendor.Nvidia => new OverclockProfile(
                CoreOffsetMhz: 120,
                MemoryOffsetMhz: 600,
                PowerLimitPercent: 110,
                TempLimitC: 83,
                FanStrategy: "Courbe agressive : ~60 % à 60 °C, 100 % à 80 °C",
                Rationale: $"Pour une {gpu.Name} (architecture NVIDIA), le meilleur compromis " +
                           "performance/stabilité passe par un léger offset cœur, un offset " +
                           "mémoire plus généreux (la VRAM encaisse bien), la limite de " +
                           "puissance au maximum et une courbe de ventilation plus ferme. " +
                           "Astuce avancée : un undervolt (courbe V/F) donne des FPS similaires " +
                           "avec moins de chaleur et zéro crash.",
                Steps: BuildSteps("MSI Afterburner", 120, 600, 110),
                Recommended: true),

            GpuVendor.Amd => new OverclockProfile(
                CoreOffsetMhz: 75,
                MemoryOffsetMhz: 100,
                PowerLimitPercent: 115,
                TempLimitC: 85,
                FanStrategy: "Courbe agressive : ~50 % à 60 °C, 100 % à 85 °C",
                Rationale: $"Pour une {gpu.Name} (Radeon), on augmente la fréquence cœur " +
                           "maximale modérément, on monte la limite de puissance, et on applique " +
                           "un léger undervolt qui, sur Radeon, augmente souvent les FPS tout en " +
                           "supprimant les crashs. La mémoire (Fast Timings) apporte un petit gain.",
                Steps: BuildSteps("AMD Software : Adrenalin Edition (onglet Performances → Réglage)", 75, 100, 115),
                Recommended: true),

            GpuVendor.Intel => new OverclockProfile(
                CoreOffsetMhz: 0,
                MemoryOffsetMhz: 0,
                PowerLimitPercent: 110,
                TempLimitC: 90,
                FanStrategy: "Courbe par défaut renforcée",
                Rationale: $"Pour une {gpu.Name} (Intel Arc), l'overclocking se fait via le " +
                           "« GPU Performance Boost » (un curseur de pourcentage, pas d'offset MHz) " +
                           "et la limite de puissance. Gains modestes mais sûrs. Montez le boost " +
                           "par paliers de 5 % en testant la stabilité.",
                Steps: new[]
                {
                    "Ouvrez Intel Arc Control → Performance.",
                    "Montez « GPU Performance Boost » à +10 % et la limite de puissance à 110 %.",
                    "Lancez un benchmark (3DMark, ou un jeu exigeant) 15-20 min.",
                    "Aucun artefact / crash ? Montez de +5 % et retestez. Sinon, redescendez de 5 %.",
                },
                Recommended: true),

            _ => new OverclockProfile(
                CoreOffsetMhz: 50,
                MemoryOffsetMhz: 200,
                PowerLimitPercent: 105,
                TempLimitC: 83,
                FanStrategy: "Courbe agressive",
                Rationale: $"Carte « {gpu.Name} » non reconnue précisément. Profil générique très " +
                           "prudent. Augmentez les valeurs par petits paliers en testant la stabilité.",
                Steps: BuildSteps("l'outil d'overclocking de votre carte", 50, 200, 105),
                Recommended: true),
        };
    }

    private static IReadOnlyList<string> BuildSteps(string tool, int core, int mem, int power) => new[]
    {
        $"Installez et ouvrez {tool}.",
        $"Réglez la limite de puissance (Power Limit) sur {power} % (le maximum disponible si inférieur).",
        $"Appliquez un offset cœur (Core Clock) de +{core} MHz.",
        $"Appliquez un offset mémoire (Memory Clock) de +{mem} MHz.",
        "Appliquez une courbe de ventilation plus ferme et validez (✓ / Apply).",
        "Lancez un test de stabilité 20-30 min (benchmark ou jeu exigeant) en surveillant la température et les artefacts.",
        "Stable, sans crash ni artefact ? Vous pouvez tenter +15 MHz cœur / +50 MHz mémoire et retester.",
        "Crash, écran noir, artefacts ou redémarrage du pilote ? Réduisez l'offset cœur de 30 MHz (puis la mémoire) jusqu'au retour à la stabilité.",
        "Une fois un réglage stable trouvé, enregistrez-le comme profil et activez « appliquer au démarrage ».",
    };

    private static GpuVendor DetectVendor(string text)
    {
        var t = text.ToLowerInvariant();
        if (t.Contains("nvidia") || t.Contains("geforce") || t.Contains("rtx") || t.Contains("gtx") || t.Contains("quadro"))
            return GpuVendor.Nvidia;
        if (t.Contains("amd") || t.Contains("radeon") || t.Contains("ati ") || t.Contains("rx "))
            return GpuVendor.Amd;
        if (t.Contains("intel") || t.Contains("arc") || t.Contains("iris") || t.Contains("uhd") || t.Contains("hd graphics"))
            return GpuVendor.Intel;
        return GpuVendor.Unknown;
    }

    private static bool IsIntegrated(string name, GpuVendor vendor)
    {
        var n = name.ToLowerInvariant();
        if (vendor == GpuVendor.Intel)
            return n.Contains("uhd") || n.Contains("iris") || n.Contains("hd graphics");
        // APU AMD (« Radeon Graphics » sans modèle RX) ou Vega intégré.
        if (vendor == GpuVendor.Amd)
            return n.Contains("radeon graphics") || n.Contains("radeon(tm) graphics") || n.Contains("vega") && !n.Contains("rx");
        return false;
    }
}
