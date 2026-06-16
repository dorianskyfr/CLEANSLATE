using System.Management;
using System.Runtime.Versioning;
using Microsoft.Win32;

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
/// Un profil d'overclocking parmi plusieurs niveaux proposés (Sûr / Équilibré /
/// Performance). <see cref="Actionable"/> indique si une action d'overclocking a
/// vraiment du sens pour ce GPU (faux pour les iGPU non réglables, où un seul profil
/// « Aucun » est renvoyé). <see cref="IsDefault"/> marque le profil présélectionné.
/// </summary>
public sealed record OverclockProfile(
    string Name,
    int CoreOffsetMhz,
    int MemoryOffsetMhz,
    int PowerLimitPercent,
    int TempLimitC,
    string FanStrategy,
    string Rationale,
    IReadOnlyList<string> Steps,
    bool Actionable,
    bool IsDefault,
    bool IsCustom = false);

/// <summary>
/// Bornes de réglage pour le mode « Personnalisé » : intervalles autorisés (sûrs) que
/// l'utilisateur ne peut pas dépasser via les curseurs. Adaptées à la marque du GPU.
/// </summary>
public sealed record OverclockLimits(
    int CoreMinMhz,  int CoreMaxMhz,
    int MemMinMhz,   int MemMaxMhz,
    int PowerMinPct, int PowerMaxPct,
    int TempMinC,    int TempMaxC);

/// <summary>
/// Réglages d'overclock importés depuis un profil MSI Afterburner (offsets en MHz,
/// déjà convertis depuis les kHz du fichier .cfg). <paramref name="SourceProfile"/>
/// indique le fichier d'origine pour information.
/// </summary>
public sealed record AfterburnerImport(
    int CoreOffsetMhz,
    int MemoryOffsetMhz,
    int PowerLimitPercent,
    int TempLimitC,
    string SourceProfile);

/// <summary>
/// Module Overclocking (sous-catégorie du Mode Jeu).
///
/// ⚠️ HONNÊTETÉ TECHNIQUE : il n'existe AUCUNE API Windows universelle, gratuite et
/// sûre pour appliquer un overclock GPU (cela passe par NVAPI / ADL / pilotes
/// propriétaires, propre à chaque marque et risqué). CleanSlate fait donc le maximum
/// utile et SÛR : il détecte la carte graphique et propose plusieurs profils de
/// départ (du plus prudent au plus poussé). Sur NVIDIA et AMD dédiés, CleanSlate
/// applique lui-même les réglages (NVAPI / ADL OverdriveN) ; pour les autres cartes
/// (Intel notamment), le profil est à appliquer pas à pas dans l'outil officiel du
/// constructeur, avec un test de stabilité.
/// </summary>
public interface IOverclockingAdvisor
{
    /// <summary>Détecte les cartes graphiques physiques installées (WMI, hors écrans virtuels).</summary>
    IReadOnlyList<GpuInfo> DetectGpus();

    /// <summary>Calcule les profils d'overclock proposés pour une carte donnée.</summary>
    /// <param name="canAutoApply">Vrai si CleanSlate peut appliquer l'overclock lui-même (NVIDIA ou AMD dédié, via NVAPI/ADL).</param>
    IReadOnlyList<OverclockProfile> RecommendProfiles(GpuInfo gpu, bool canAutoApply);

    /// <summary>
    /// Bornes (sûres) du mode « Personnalisé » pour cette carte, adaptées à la marque.
    /// Renvoie null si le réglage manuel n'a pas de sens (iGPU non réglable, Intel boost).
    /// </summary>
    OverclockLimits? GetCustomLimits(GpuInfo gpu);

    /// <summary>
    /// Tente de lire le profil d'overclock actuel de MSI Afterburner (s'il est installé)
    /// pour pré-remplir le mode « Personnalisé ». Renvoie null si Afterburner n'est pas
    /// installé ou si aucun profil exploitable n'a été trouvé. Best-effort, sans exception.
    /// </summary>
    AfterburnerImport? TryImportAfterburnerProfile();
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
                if (IsVirtualAdapter(name!)) continue;

                var compat = (obj["AdapterCompatibility"] as string) ?? string.Empty;
                var vendor = DetectVendor(name + " " + compat);

                // La VRAM réelle est lue EN PRIORITÉ depuis le registre Windows
                // (HardwareInformation.qwMemorySize, QWORD 64 bits) : WMI AdapterRAM est un
                // UInt32 qui sature dès ~4 Go et reporte une valeur tronquée (souvent
                // 4 293 918 720 octets = 4095 Mio, PAS exactement uint.MaxValue), ce qui
                // faisait afficher « 4 Go » sur les cartes de 8/12/16 Go (RTX 3070, 4080…).
                long vram = TryGetVramFromRegistry(name!);
                if (vram <= 0)
                {
                    try
                    {
                        var raw = obj["AdapterRAM"];
                        if (raw != null) vram = Convert.ToInt64(raw);
                    }
                    catch { /* certains pilotes renvoient 0 */ }
                }

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

    public IReadOnlyList<OverclockProfile> RecommendProfiles(GpuInfo gpu, bool canAutoApply)
    {
        if (gpu.IsIntegrated)
        {
            if (IsTunableIntelIGpu(gpu.Name))
                return BuildIntelBoostProfiles(gpu, integrated: true);

            return new[]
            {
                new OverclockProfile(
                    Name: "Aucun",
                    CoreOffsetMhz: 0, MemoryOffsetMhz: 0, PowerLimitPercent: 0, TempLimitC: 0,
                    FanStrategy: "—",
                    Rationale: "Carte graphique intégrée détectée. L'overclocking de ce type de GPU " +
                               "n'apporte quasiment rien et dépend surtout du refroidissement du " +
                               "portable/PC. Recommandation : ne pas overclocker — privilégiez un " +
                               "bon profil d'alimentation « Performances » et un refroidissement propre.",
                    Steps: new[] { "Aucune action d'overclocking recommandée pour ce GPU intégré." },
                    Actionable: false,
                    IsDefault: true),
            };
        }

        return gpu.Vendor switch
        {
            GpuVendor.Nvidia => WithCustom(BuildNvidiaProfiles(gpu, canAutoApply), canAutoApply),
            GpuVendor.Amd    => WithCustom(BuildAmdProfiles(gpu, canAutoApply), canAutoApply),
            GpuVendor.Intel  => BuildIntelBoostProfiles(gpu, integrated: false),
            _                => WithCustom(BuildGenericProfiles(gpu), canAutoApply: false),
        };
    }

    public OverclockLimits? GetCustomLimits(GpuInfo gpu)
    {
        if (gpu.IsIntegrated) return null;
        return gpu.Vendor switch
        {
            // Cœur : on autorise un léger underclock (valeurs négatives) jusqu'à un OC franc.
            GpuVendor.Nvidia => new OverclockLimits(-200, 300, -1000, 1500, 70, 120, 60, 90),
            GpuVendor.Amd    => new OverclockLimits(-200, 200,  -500,  300, 80, 120, 60, 95),
            GpuVendor.Intel  => null, // réglage via curseur boost % (pas d'offset MHz)
            _                => new OverclockLimits(-100, 150,  -300,  400, 90, 115, 60, 90),
        };
    }

    /// <summary>
    /// Ajoute un profil « Personnalisé » éditable en fin de liste, initialisé sur les
    /// valeurs du profil par défaut (« Équilibré ») comme point de départ raisonnable.
    /// </summary>
    private static IReadOnlyList<OverclockProfile> WithCustom(
        IReadOnlyList<OverclockProfile> profiles, bool canAutoApply)
    {
        var basis = profiles.FirstOrDefault(p => p.IsDefault) ?? profiles[0];

        var steps = canAutoApply
            ? new[]
              {
                  "Réglez les curseurs (cœur, mémoire, limite de puissance) à votre convenance, " +
                  "par petits paliers.",
                  "Cliquez sur « Appliquer l'overclock » : CleanSlate pose VOS valeurs directement " +
                  "(NVAPI / ADL, aucun logiciel tiers).",
                  "Lancez un test de stabilité (benchmark ou jeu exigeant) 20-30 min en surveillant " +
                  "température et artefacts.",
                  "Instable (artefacts, écran noir, crash) ? Cliquez sur « Reset » pour tout annuler " +
                  "immédiatement, puis baissez les offsets.",
              }
            : new[]
              {
                  "Réglez les curseurs à votre convenance, puis « Copier le profil ».",
                  "Reportez les valeurs dans l'outil d'overclocking de votre constructeur, validez.",
                  "Lancez un test de stabilité 20-30 min ; en cas d'instabilité, réduisez les offsets.",
              };

        var custom = new OverclockProfile(
            Name: "Personnalisé",
            CoreOffsetMhz: basis.CoreOffsetMhz,
            MemoryOffsetMhz: basis.MemoryOffsetMhz,
            PowerLimitPercent: basis.PowerLimitPercent,
            TempLimitC: basis.TempLimitC,
            FanStrategy: basis.FanStrategy,
            Rationale: "Mode personnalisé : vous fixez vous-même les offsets cœur/mémoire, la limite " +
                       "de puissance et la température cible, dans des bornes sûres. Augmentez par " +
                       "petits paliers en testant la stabilité à chaque étape — c'est la méthode des " +
                       "overclockeurs. En cas de doute, repartez d'un profil prédéfini.",
            Steps: steps,
            Actionable: true,
            IsDefault: false,
            IsCustom: true);

        return profiles.Append(custom).ToList();
    }

    // ---------------------------------------------------------------- NVIDIA ----

    private static IReadOnlyList<OverclockProfile> BuildNvidiaProfiles(GpuInfo gpu, bool canAutoApply)
    {
        var fan = "Courbe agressive : ~60 % à 60 °C, 100 % à 80 °C";

        return new[]
        {
            BuildNvidiaProfile(gpu, "Sûr",         core: 60,  mem: 300, power: 105, temp: 80,
                "Courbe douce : ~55 % à 65 °C, 85 % à 80 °C", canAutoApply, isDefault: false,
                tierNote: "Gain modeste mais quasi garanti, idéal pour vérifier la stabilité de base."),
            BuildNvidiaProfile(gpu, "Équilibré",   core: 120, mem: 600, power: 110, temp: 83,
                fan, canAutoApply, isDefault: true,
                tierNote: "Le « sweet spot » : bon gain de FPS sans risque significatif sur une carte saine."),
            BuildNvidiaProfile(gpu, "Performance", core: 180, mem: 900, power: 115, temp: 87,
                "Courbe maximale : ~70 % à 55 °C, 100 % à 75 °C", canAutoApply, isDefault: false,
                tierNote: "Pousse la carte plus loin — testez la stabilité plus longtemps avant de garder ce profil."),
        };
    }

    private static OverclockProfile BuildNvidiaProfile(
        GpuInfo gpu, string name, int core, int mem, int power, int temp, string fan,
        bool canAutoApply, bool isDefault, string tierNote)
    {
        var rationale = $"Profil « {name} » pour une {gpu.Name} (architecture NVIDIA) : offset cœur " +
                         $"+{core} MHz, offset mémoire +{mem} MHz (la VRAM encaisse bien des offsets " +
                         $"plus généreux), limite de puissance à {power} % et température cible " +
                         $"{temp} °C. {tierNote}";

        var steps = canAutoApply
            ? new[]
              {
                  $"Sélectionnez le profil « {name} » puis cliquez sur « Appliquer l'overclock » : " +
                  "CleanSlate pose directement les offsets via NVAPI (aucun logiciel tiers requis).",
                  $"Offsets posés : cœur +{core} MHz, mémoire +{mem} MHz.",
                  "Lancez un test de stabilité (benchmark ou jeu exigeant) pendant 20-30 minutes en " +
                  "surveillant la température et les artefacts.",
                  "Stable, sans crash ni artefact ? Vous pouvez essayer le profil supérieur pour plus " +
                  "de performance.",
                  "Instable (artefacts, écran noir, crash, redémarrage du pilote) ? Cliquez sur « Reset » " +
                  "pour annuler immédiatement, ou repassez au profil inférieur.",
              }
            : new[]
              {
                  "NVAPI n'est pas disponible sur ce système : ouvrez le logiciel de contrôle fourni " +
                  "par le fabricant de votre carte.",
                  $"Réglez la limite de puissance sur {power} %, l'offset cœur sur +{core} MHz et " +
                  $"l'offset mémoire sur +{mem} MHz.",
                  "Appliquez une courbe de ventilation plus ferme et validez.",
                  "Lancez un test de stabilité 20-30 min en surveillant la température et les artefacts.",
                  "Instable ? Réduisez l'offset cœur de 30 MHz puis la mémoire jusqu'au retour à la " +
                  "stabilité.",
              };

        return new OverclockProfile(name, core, mem, power, temp, fan, rationale, steps,
            Actionable: true, IsDefault: isDefault);
    }

    // ------------------------------------------------------------------- AMD ----

    private static IReadOnlyList<OverclockProfile> BuildAmdProfiles(GpuInfo gpu, bool canAutoApply)
    {
        return new[]
        {
            BuildAmdProfile(gpu, "Sûr",         core: 40,  mem: 50,  power: 110, temp: 80, canAutoApply, isDefault: false,
                tierNote: "Gain modeste mais quasi garanti."),
            BuildAmdProfile(gpu, "Équilibré",   core: 75,  mem: 100, power: 115, temp: 85, canAutoApply, isDefault: true,
                tierNote: "Le « sweet spot » : bon compromis performance/stabilité."),
            BuildAmdProfile(gpu, "Performance", core: 110, mem: 150, power: 120, temp: 90, canAutoApply, isDefault: false,
                tierNote: "Pousse la carte plus loin — testez longuement avant de garder ce profil."),
        };
    }

    private static OverclockProfile BuildAmdProfile(
        GpuInfo gpu, string name, int core, int mem, int power, int temp, bool canAutoApply, bool isDefault, string tierNote)
    {
        var fan = "Courbe agressive : ~50 % à 60 °C, 100 % à 85 °C";
        var rationale = $"Profil « {name} » pour une {gpu.Name} (Radeon) : offset cœur +{core} MHz, " +
                         $"offset mémoire +{mem} MHz (Fast Timings), limite de puissance {power} % et " +
                         $"température cible {temp} °C. Un léger undervolt en complément augmente " +
                         $"souvent les FPS tout en supprimant les crashs. {tierNote}";

        var steps = canAutoApply
            ? new[]
              {
                  $"Sélectionnez le profil « {name} » puis cliquez sur « Appliquer l'overclock » : " +
                  "CleanSlate pose directement les fréquences via AMD ADL / OverdriveN (aucun logiciel " +
                  "tiers requis).",
                  $"Réglages posés : cœur +{core} MHz, mémoire +{mem} MHz, limite de puissance {power} %.",
                  "Lancez un test de stabilité (benchmark ou jeu exigeant) pendant 20-30 minutes en " +
                  "surveillant la température et les artefacts.",
                  "Stable, sans crash ni artefact ? Vous pouvez essayer le profil supérieur pour plus " +
                  "de performance.",
                  "Instable (artefacts, écran noir, crash, redémarrage du pilote) ? Cliquez sur « Reset » " +
                  "pour annuler immédiatement (retour aux valeurs d'usine), ou repassez au profil inférieur.",
              }
            : new[]
              {
                  $"Sélectionnez le profil « {name} » puis ouvrez AMD Software : Adrenalin Edition → " +
                  "Performances → Réglage.",
                  $"Réglez la limite de puissance sur {power} %.",
                  $"Appliquez un offset cœur (Core Clock) de +{core} MHz.",
                  $"Appliquez un offset mémoire (Memory Clock) de +{mem} MHz.",
                  "Appliquez une courbe de ventilation plus ferme et validez (✓ / Apply).",
                  "Lancez un test de stabilité 20-30 min en surveillant la température et les artefacts.",
                  "Instable ? Réduisez l'offset cœur de 25 MHz (puis la mémoire) jusqu'au retour à la stabilité.",
              };

        return new OverclockProfile(name, core, mem, power, temp, fan, rationale, steps,
            Actionable: true, IsDefault: isDefault);
    }

    // -------------------------------------------------------- Intel (boost %) ----

    /// <summary>
    /// Cartes/iGPU Intel récents (Arc dédié, Iris Xe, Arc Graphics intégré) : pas
    /// d'offset MHz exposé, mais un curseur « GPU Performance Boost » (en %) dans
    /// Intel Graphics Command Center / Arc Control. <see cref="OverclockProfile.PowerLimitPercent"/>
    /// porte ici le pourcentage de boost.
    /// </summary>
    private static IReadOnlyList<OverclockProfile> BuildIntelBoostProfiles(GpuInfo gpu, bool integrated)
    {
        var what = integrated ? "GPU intégré (Iris Xe / Arc Graphics)" : "Intel Arc";
        var coolingNote = integrated
            ? " Sur un portable, assurez un bon flux d'air et un profil d'alimentation « Performances »."
            : " Surveillez la température du boîtier (le partage de chaleur avec le CPU peut limiter le boost).";

        return new[]
        {
            BuildIntelBoostProfile(gpu, "Sûr",         boost: 105, temp: 85, isDefault: false,
                what, coolingNote, "Gain léger mais sûr."),
            BuildIntelBoostProfile(gpu, "Équilibré",   boost: 110, temp: 90, isDefault: true,
                what, coolingNote, "Le « sweet spot » recommandé."),
            BuildIntelBoostProfile(gpu, "Performance", boost: 115, temp: 95, isDefault: false,
                what, coolingNote, "Gain maximal — testez la stabilité plus longtemps."),
        };
    }

    private static OverclockProfile BuildIntelBoostProfile(
        GpuInfo gpu, string name, int boost, int temp, bool isDefault,
        string what, string coolingNote, string tierNote)
    {
        var rationale = $"Profil « {name} » pour {gpu.Name} ({what}) : « GPU Performance Boost » à " +
                         $"+{boost - 100} % et température cible {temp} °C. L'overclocking se fait via " +
                         "ce curseur de pourcentage (pas d'offset MHz)." + coolingNote + " " + tierNote;

        var steps = new[]
        {
            $"Sélectionnez le profil « {name} » puis ouvrez Intel Graphics Command Center (ou Arc " +
            "Control sur les puces récentes) → Performance.",
            $"Réglez « GPU Performance Boost » sur +{boost - 100} %.",
            "Lancez un test de stabilité (benchmark ou jeu exigeant) pendant 15-20 minutes.",
            "Aucun artefact ni ralentissement thermique excessif ? Vous pouvez essayer le profil " +
            "supérieur. Sinon, repassez au profil inférieur.",
        };

        return new OverclockProfile(name, CoreOffsetMhz: 0, MemoryOffsetMhz: 0, PowerLimitPercent: boost,
            TempLimitC: temp, FanStrategy: "Profil par défaut renforcé", Rationale: rationale, Steps: steps,
            Actionable: true, IsDefault: isDefault);
    }

    // --------------------------------------------------------------- Générique ----

    private static IReadOnlyList<OverclockProfile> BuildGenericProfiles(GpuInfo gpu)
    {
        return new[]
        {
            BuildGenericProfile(gpu, "Sûr",         core: 30, mem: 100, power: 103, temp: 80, isDefault: false),
            BuildGenericProfile(gpu, "Équilibré",   core: 50, mem: 200, power: 105, temp: 83, isDefault: true),
            BuildGenericProfile(gpu, "Performance", core: 70, mem: 300, power: 108, temp: 86, isDefault: false),
        };
    }

    private static OverclockProfile BuildGenericProfile(
        GpuInfo gpu, string name, int core, int mem, int power, int temp, bool isDefault)
    {
        var rationale = $"Carte « {gpu.Name} » non reconnue précisément. Profil « {name} » très prudent : " +
                         $"offset cœur +{core} MHz, offset mémoire +{mem} MHz, limite de puissance {power} %. " +
                         "Augmentez les valeurs par petits paliers en testant la stabilité.";

        var steps = new[]
        {
            $"Sélectionnez le profil « {name} » puis ouvrez l'outil d'overclocking de votre carte.",
            $"Réglez la limite de puissance sur {power} %.",
            $"Appliquez un offset cœur de +{core} MHz et un offset mémoire de +{mem} MHz.",
            "Lancez un test de stabilité 20-30 min en surveillant la température et les artefacts.",
            "Instable ? Réduisez les offsets jusqu'au retour à la stabilité.",
        };

        return new OverclockProfile(name, core, mem, power, temp, "Courbe agressive", rationale, steps,
            Actionable: true, IsDefault: isDefault);
    }

    // ----------------------------------------------------------------- Détection ----

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

    /// <summary>
    /// Vrai si l'iGPU Intel supporte le réglage « GPU Performance Boost » (Iris Xe et
    /// Arc Graphics intégré, contrairement aux anciens UHD/HD Graphics).
    /// </summary>
    internal static bool IsTunableIntelIGpu(string name)
    {
        var n = name.ToLowerInvariant();
        if (n.Contains("iris") && n.Contains("xe")) return true;
        if (n.Contains("arc") && n.Contains("graphics")) return true;
        return false;
    }

    /// <summary>
    /// Vrai si l'adaptateur n'est pas une carte graphique physique (écran virtuel
    /// type Parsec, IDD, etc.) — à exclure de la liste de sélection.
    /// </summary>
    internal static bool IsVirtualAdapter(string name)
    {
        var n = name.ToLowerInvariant();
        return n.Contains("parsec")
            || n.Contains("virtual display")
            || n.Contains("virtual monitor")
            || n.Contains("remote display")
            || n.Contains(" idd")
            || n.StartsWith("idd")
            || n.Contains("spacedesk");
    }

    /// <summary>
    /// Lit la VRAM réelle depuis le registre Windows (HardwareInformation.qwMemorySize, QWORD 64 bits) :
    /// source fiable au-delà de 4 Go, contrairement à WMI AdapterRAM (UInt32 tronqué). Renvoie 0 si
    /// la clé n'est pas trouvée (l'appelant retombe alors sur AdapterRAM).
    /// </summary>
    private static long TryGetVramFromRegistry(string gpuName)
    {
        try
        {
            const string classKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
            using var root = Registry.LocalMachine.OpenSubKey(classKey);
            if (root == null) return 0;

            foreach (var subName in root.GetSubKeyNames())
            {
                using var sub = root.OpenSubKey(subName);
                if (sub == null) continue;

                var driverDesc = sub.GetValue("DriverDesc") as string;
                if (string.IsNullOrEmpty(driverDesc)) continue;

                if (!driverDesc.Contains(gpuName, StringComparison.OrdinalIgnoreCase) &&
                    !gpuName.Contains(driverDesc, StringComparison.OrdinalIgnoreCase))
                    continue;

                var memVal = sub.GetValue("HardwareInformation.qwMemorySize");
                long qword = memVal switch
                {
                    long l              => l,
                    byte[] b when b.Length == 8 => BitConverter.ToInt64(b, 0),
                    _                   => 0L
                };
                if (qword > 0) return qword;
            }
        }
        catch { }
        return 0;
    }

    // ------------------------------------------------- Import MSI Afterburner ----

    public AfterburnerImport? TryImportAfterburnerProfile()
    {
        try
        {
            var dir = FindAfterburnerProfilesDir();
            if (dir is null) return null;

            // On retient le profil le plus récemment modifié contenant un offset non nul
            // (le plus susceptible d'être celui que l'utilisateur applique).
            foreach (var cfg in Directory.EnumerateFiles(dir, "*.cfg")
                         .OrderByDescending(f => { try { return File.GetLastWriteTimeUtc(f); } catch { return DateTime.MinValue; } }))
            {
                string content;
                try { content = File.ReadAllText(cfg); } catch { continue; }

                var import = ParseAfterburnerConfig(content, Path.GetFileNameWithoutExtension(cfg));
                if (import is not null &&
                    (import.CoreOffsetMhz != 0 || import.MemoryOffsetMhz != 0))
                    return import;
            }
        }
        catch { /* best effort */ }
        return null;
    }

    /// <summary>Localise le dossier « Profiles » de MSI Afterburner, s'il est installé.</summary>
    private static string? FindAfterburnerProfilesDir()
    {
        foreach (var pf in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                 })
        {
            if (string.IsNullOrEmpty(pf)) continue;
            var dir = Path.Combine(pf, "MSI Afterburner", "Profiles");
            if (Directory.Exists(dir)) return dir;
        }
        return null;
    }

    /// <summary>
    /// Analyse un fichier de profil MSI Afterburner (format INI). Les offsets cœur et
    /// mémoire (« CoreClkBoost » / « MemClkBoost ») sont stockés en kHz et convertis en
    /// MHz ; « PowerLimit » et « ThermalLimit » sont en %/°C. Renvoie null si aucune de
    /// ces clés n'est présente.
    /// </summary>
    internal static AfterburnerImport? ParseAfterburnerConfig(string content, string sourceName)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;

        int? coreKHz = null, memKHz = null, power = null, temp = null;

        foreach (var raw in content.Split('\n'))
        {
            var line = raw.Trim();
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;

            var key = line[..eq].Trim();
            var val = line[(eq + 1)..].Trim();
            if (!long.TryParse(val, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var num))
                continue;

            switch (key.ToLowerInvariant())
            {
                case "coreclkboost": coreKHz = (int)num; break;
                case "memclkboost":  memKHz  = (int)num; break;
                case "powerlimit":   power   = (int)num; break;
                case "thermallimit": temp    = (int)num; break;
            }
        }

        if (coreKHz is null && memKHz is null && power is null && temp is null)
            return null;

        // kHz → MHz (1000 kHz = 1 MHz). Afterburner stocke les offsets en kHz.
        return new AfterburnerImport(
            CoreOffsetMhz:    (coreKHz ?? 0) / 1000,
            MemoryOffsetMhz:  (memKHz  ?? 0) / 1000,
            PowerLimitPercent: power ?? 0,
            TempLimitC:        temp  ?? 0,
            SourceProfile:     sourceName);
    }
}
