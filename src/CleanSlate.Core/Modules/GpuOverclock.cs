using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace CleanSlate.Core.Modules;

/// <summary>Résultat d'une tentative d'application d'overclock.</summary>
public sealed record OverclockApplyResult(bool Success, string Message);

/// <summary>
/// Applique RÉELLEMENT un overclock GPU.
///
/// ⚠️ EXPÉRIMENTAL. L'application automatique des offsets de fréquence passe par les
/// API propriétaires du constructeur (NVAPI pour NVIDIA). Implémenté pour NVIDIA
/// (la majorité des cartes gaming). NVAPI valide la version de chaque structure :
/// en cas d'incompatibilité, l'appel échoue PROPREMENT sans modifier le matériel.
/// Un Reset remet les offsets à zéro.
///
/// AMD / Intel : l'application automatique n'est pas disponible (ADL/IGCL non
/// implémentés) — on renvoie un message clair invitant à suivre les étapes guidées.
/// </summary>
public interface IGpuOverclocker
{
    /// <summary>Vrai si l'application automatique est possible pour cette carte.</summary>
    bool CanApply(GpuInfo gpu);

    /// <summary>Applique les offsets cœur/mémoire du profil.</summary>
    OverclockApplyResult Apply(GpuInfo gpu, OverclockProfile profile);

    /// <summary>Remet les offsets cœur/mémoire à zéro (annule l'overclock).</summary>
    OverclockApplyResult Reset(GpuInfo gpu);
}

[SupportedOSPlatform("windows")]
public sealed class GpuOverclocker : IGpuOverclocker
{
    public bool CanApply(GpuInfo gpu) =>
        gpu.Vendor == GpuVendor.Nvidia && !gpu.IsIntegrated && NvApi.IsAvailable();

    public OverclockApplyResult Apply(GpuInfo gpu, OverclockProfile profile)
    {
        if (gpu.Vendor != GpuVendor.Nvidia)
            return new OverclockApplyResult(false,
                "Application automatique disponible uniquement pour les cartes NVIDIA pour le moment. " +
                "Pour cette carte, suivez les étapes guidées (le profil reste optimal).");

        if (gpu.IsIntegrated)
            return new OverclockApplyResult(false, "L'overclocking d'un GPU intégré n'est pas pris en charge.");

        return NvApi.ApplyOffsets(gpu.Name, profile.CoreOffsetMhz, profile.MemoryOffsetMhz);
    }

    public OverclockApplyResult Reset(GpuInfo gpu)
    {
        if (gpu.Vendor != GpuVendor.Nvidia || gpu.IsIntegrated)
            return new OverclockApplyResult(false, "Reset automatique disponible uniquement pour les cartes NVIDIA dédiées.");

        return NvApi.ApplyOffsets(gpu.Name, 0, 0);
    }
}

/// <summary>
/// Couche d'interopérabilité minimale avec NVAPI (nvapi64.dll). On n'utilise que
/// ce qui est nécessaire pour poser un offset de fréquence sur l'état P0 :
/// Initialize, EnumPhysicalGPUs, GetFullName, SetPstates20.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class NvApi
{
    // nvapi64.dll n'exporte qu'une seule fonction : on récupère les autres par ID.
    [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr NvAPI_QueryInterface(uint id);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int InitializeDelegate();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int UnloadDelegate();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int EnumPhysicalGPUsDelegate([Out] IntPtr[] handles, out int count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetFullNameDelegate(IntPtr gpu, StringBuilder name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SetPstates20Delegate(IntPtr gpu, IntPtr pstatesInfo);

    // Identifiants de fonction NVAPI (stables, publics dans la communauté).
    private const uint ID_Initialize       = 0x0150E828;
    private const uint ID_Unload           = 0xD22BDD7E;
    private const uint ID_EnumPhysicalGPUs = 0xE5AC921F;
    private const uint ID_GetFullName      = 0xCEEE8E9F;
    private const uint ID_SetPstates20     = 0x0F4DAE6B;

    private const int NVAPI_OK = 0;
    private const int MAX_GPUS = 64;

    // ----- Dimensions exactes de NV_GPU_PERF_PSTATES20_INFO_V1 (octets) -----
    // PARAM_DELTA = 12 ; CLOCK_ENTRY = 44 ; pstate = 8 + 8*44 + 4*24 = 456 ;
    // header = 20 ; total = 20 + 16*456 = 7316.
    private const int PSTATES20_SIZE = 7316;
    private const uint PSTATES20_VER = PSTATES20_SIZE | (1u << 16); // MAKE_NVAPI_VERSION(size, 1)

    public static bool IsAvailable()
    {
        try { return NvAPI_QueryInterface(ID_Initialize) != IntPtr.Zero; }
        catch (DllNotFoundException) { return false; }
        catch { return false; }
    }

    private static T? GetDelegate<T>(uint id) where T : class
    {
        var ptr = NvAPI_QueryInterface(id);
        return ptr == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer<T>(ptr);
    }

    public static OverclockApplyResult ApplyOffsets(string gpuName, int coreOffsetMhz, int memOffsetMhz)
    {
        InitializeDelegate? init;
        EnumPhysicalGPUsDelegate? enumGpus;
        SetPstates20Delegate? setPstates;
        GetFullNameDelegate? getName;
        UnloadDelegate? unload;

        try
        {
            init       = GetDelegate<InitializeDelegate>(ID_Initialize);
            enumGpus   = GetDelegate<EnumPhysicalGPUsDelegate>(ID_EnumPhysicalGPUs);
            setPstates = GetDelegate<SetPstates20Delegate>(ID_SetPstates20);
            getName    = GetDelegate<GetFullNameDelegate>(ID_GetFullName);
            unload     = GetDelegate<UnloadDelegate>(ID_Unload);
        }
        catch (DllNotFoundException)
        {
            return new OverclockApplyResult(false, "Pilote NVIDIA introuvable (nvapi64.dll absent).");
        }

        if (init is null || enumGpus is null || setPstates is null)
            return new OverclockApplyResult(false, "NVAPI indisponible sur ce système.");

        if (init() != NVAPI_OK)
            return new OverclockApplyResult(false, "Échec de l'initialisation de NVAPI.");

        try
        {
            var handles = new IntPtr[MAX_GPUS];
            if (enumGpus(handles, out int count) != NVAPI_OK || count == 0)
                return new OverclockApplyResult(false, "Aucune carte NVIDIA détectée par NVAPI.");

            // Choisit la carte dont le nom correspond, sinon la première.
            IntPtr target = handles[0];
            if (getName is not null)
            {
                for (int i = 0; i < count; i++)
                {
                    var sb = new StringBuilder(128);
                    if (getName(handles[i], sb) == NVAPI_OK &&
                        NamesMatch(sb.ToString(), gpuName))
                    {
                        target = handles[i];
                        break;
                    }
                }
            }

            var buffer = BuildPstates20(coreOffsetMhz, memOffsetMhz);
            var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                int status = setPstates(target, handle.AddrOfPinnedObject());
                if (status == NVAPI_OK)
                {
                    var what = (coreOffsetMhz == 0 && memOffsetMhz == 0)
                        ? "Overclock réinitialisé (offsets remis à 0)."
                        : $"Overclock appliqué : cœur +{coreOffsetMhz} MHz, mémoire +{memOffsetMhz} MHz.";
                    return new OverclockApplyResult(true, what);
                }

                return new OverclockApplyResult(false,
                    $"NVAPI a refusé l'application (code {status}). " +
                    "Aucune modification effectuée. Suivez les étapes guidées si le problème persiste.");
            }
            finally
            {
                handle.Free();
            }
        }
        finally
        {
            try { unload?.Invoke(); } catch { /* ignore */ }
        }
    }

    private static bool NamesMatch(string a, string b)
    {
        a = a.ToLowerInvariant();
        b = b.ToLowerInvariant();
        if (a.Contains(b) || b.Contains(a)) return true;
        // Compare sur un mot-clé de modèle commun (ex. « 4070 », « 3060 »).
        foreach (var token in b.Split(' '))
            if (token.Length >= 4 && token.All(char.IsDigit) && a.Contains(token))
                return true;
        return false;
    }

    /// <summary>
    /// Construit le buffer NV_GPU_PERF_PSTATES20_INFO_V1 pour poser un offset sur le
    /// P-state P0 : 1 pstate, 2 clocks (cœur domaine 0, mémoire domaine 4).
    /// </summary>
    private static byte[] BuildPstates20(int coreOffsetMhz, int memOffsetMhz)
    {
        var buf = new byte[PSTATES20_SIZE];

        void U32(int off, uint v) => BitConverter.GetBytes(v).CopyTo(buf, off);
        void S32(int off, int v) => BitConverter.GetBytes(v).CopyTo(buf, off);

        // En-tête.
        U32(0,  PSTATES20_VER); // version
        // [4] bIsEditable/reserved = 0
        U32(8,  1);             // numPstates
        U32(12, 2);             // numClocks (cœur + mémoire)
        U32(16, 0);             // numBaseVoltages

        // pstate[0] commence à l'offset 20.
        const int p0 = 20;
        U32(p0 + 0, 0);         // pstateId = P0
        // [p0+4] bitfield = 0
        // clocks[] commencent à p0+8.
        const int clock0 = p0 + 8;          // = 28 (cœur)
        const int clock1 = clock0 + 44;     // = 72 (mémoire)

        // Cœur (domaine GRAPHICS = 0), type SINGLE = 0, freqDelta.value à +12.
        U32(clock0 + 0, 0);                 // domainId
        U32(clock0 + 4, 0);                 // typeId
        S32(clock0 + 12, coreOffsetMhz * 1000); // freqDelta_kHz.value

        // Mémoire (domaine MEMORY = 4).
        U32(clock1 + 0, 4);                 // domainId
        U32(clock1 + 4, 0);                 // typeId
        S32(clock1 + 12, memOffsetMhz * 1000);

        return buf;
    }
}
