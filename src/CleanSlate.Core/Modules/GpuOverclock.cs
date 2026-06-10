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
/// API propriétaires du constructeur : NVAPI pour NVIDIA, ADL (OverdriveN) pour AMD.
/// Ces deux API valident la version/taille de chaque structure : en cas
/// d'incompatibilité, l'appel échoue PROPREMENT sans modifier le matériel. Un Reset
/// remet les fréquences/limites à leurs valeurs d'usine.
///
/// Intel : l'application automatique n'est pas disponible (IGCL non implémenté, API
/// trop récente/instable pour une intégration fiable) — on renvoie un message clair
/// invitant à suivre les étapes guidées.
/// </summary>
public interface IGpuOverclocker
{
    /// <summary>Vrai si l'application automatique est possible pour cette carte.</summary>
    bool CanApply(GpuInfo gpu);

    /// <summary>Applique les offsets cœur/mémoire (et la limite de puissance) du profil.</summary>
    OverclockApplyResult Apply(GpuInfo gpu, OverclockProfile profile);

    /// <summary>Remet les fréquences et limites à leurs valeurs d'usine (annule l'overclock).</summary>
    OverclockApplyResult Reset(GpuInfo gpu);
}

[SupportedOSPlatform("windows")]
public sealed class GpuOverclocker : IGpuOverclocker
{
    public bool CanApply(GpuInfo gpu)
    {
        if (gpu.IsIntegrated) return false;
        return gpu.Vendor switch
        {
            GpuVendor.Nvidia => NvApi.IsAvailable(),
            GpuVendor.Amd    => AdlApi.IsAvailable(),
            _ => false,
        };
    }

    public OverclockApplyResult Apply(GpuInfo gpu, OverclockProfile profile)
    {
        if (gpu.IsIntegrated)
            return new OverclockApplyResult(false, "L'overclocking d'un GPU intégré n'est pas pris en charge.");

        return gpu.Vendor switch
        {
            GpuVendor.Nvidia => NvApi.ApplyOffsets(gpu.Name, profile.CoreOffsetMhz, profile.MemoryOffsetMhz),
            GpuVendor.Amd    => AdlApi.ApplyOffsets(gpu.Name, profile.CoreOffsetMhz, profile.MemoryOffsetMhz, profile.PowerLimitPercent),
            _ => new OverclockApplyResult(false,
                "Application automatique disponible uniquement pour les cartes NVIDIA et AMD dédiées pour le moment. " +
                "Pour cette carte, suivez les étapes guidées (le profil reste optimal)."),
        };
    }

    public OverclockApplyResult Reset(GpuInfo gpu)
    {
        if (gpu.IsIntegrated)
            return new OverclockApplyResult(false, "Reset automatique disponible uniquement pour les cartes dédiées NVIDIA et AMD.");

        return gpu.Vendor switch
        {
            GpuVendor.Nvidia => NvApi.ApplyOffsets(gpu.Name, 0, 0),
            GpuVendor.Amd    => AdlApi.Reset(gpu.Name),
            _ => new OverclockApplyResult(false, "Reset automatique disponible uniquement pour les cartes dédiées NVIDIA et AMD."),
        };
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

/// <summary>
/// Couche d'interopérabilité minimale avec AMD ADL (atiadlxx.dll), API « OverdriveN »
/// (Polaris/Vega/Navi et au-delà). On applique un offset de fréquence en décalant
/// TOUS les paliers de performance (P-states) cœur/mémoire renvoyés par le pilote,
/// après un Reset préalable pour repartir des valeurs d'usine (offsets non cumulatifs
/// d'un profil à l'autre). La limite de puissance est posée séparément (en %, relatif
/// à la valeur par défaut). En cas d'échec à n'importe quelle étape, aucune
/// modification n'est appliquée et un message clair est renvoyé.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class AdlApi
{
    private const string Dll = "atiadlxx.dll";

    // Tailles fixes des structures ADLODNPerformanceLevelsX2 (en-tête) et
    // ADLODNPerformanceLevelX2 (par palier), toutes deux composées d'entiers 32 bits.
    private const int HeaderSize = 12; // iSize, iMode, iNumberOfPerformanceLevels
    private const int LevelSize  = 12; // iClock, iVddc, iEnabled
    private const int MaxLevels  = 16; // garde-fou (les GPU récents en ont 2 à 8)

    private const int ADL_OK = 0;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr ADL_Main_Memory_Alloc(int size);

    [DllImport(Dll)] private static extern int ADL2_Main_Control_Create(ADL_Main_Memory_Alloc callback, int enumConnectedAdapters, out IntPtr context);
    [DllImport(Dll)] private static extern int ADL2_Main_Control_Destroy(IntPtr context);
    [DllImport(Dll)] private static extern int ADL2_Adapter_NumberOfAdapters_Get(IntPtr context, out int numAdapters);
    [DllImport(Dll)] private static extern int ADL2_Adapter_AdapterInfo_Get(IntPtr context, IntPtr info, int inputSize);
    [DllImport(Dll)] private static extern int ADL2_OverdriveN_CapabilitiesX2_Get(IntPtr context, int adapterIndex, ref ADLODNCapabilitiesX2 caps);
    [DllImport(Dll)] private static extern int ADL2_OverdriveN_SystemClocksX2_Get(IntPtr context, int adapterIndex, IntPtr levels);
    [DllImport(Dll)] private static extern int ADL2_OverdriveN_SystemClocksX2_Set(IntPtr context, int adapterIndex, IntPtr levels);
    [DllImport(Dll)] private static extern int ADL2_OverdriveN_SystemClocksX2_Reset(IntPtr context, int adapterIndex);
    [DllImport(Dll)] private static extern int ADL2_OverdriveN_MemoryClocksX2_Get(IntPtr context, int adapterIndex, IntPtr levels);
    [DllImport(Dll)] private static extern int ADL2_OverdriveN_MemoryClocksX2_Set(IntPtr context, int adapterIndex, IntPtr levels);
    [DllImport(Dll)] private static extern int ADL2_OverdriveN_MemoryClocksX2_Reset(IntPtr context, int adapterIndex);
    [DllImport(Dll)] private static extern int ADL2_OverdriveN_PowerLimit_Set(IntPtr context, int adapterIndex, ref int powerLimitPercentOffset);
    [DllImport(Dll)] private static extern int ADL2_OverdriveN_PowerLimit_Reset(IntPtr context, int adapterIndex);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct AdapterInfo
    {
        public int iSize;
        public int iAdapterIndex;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string strUDID;
        public int iBusNumber;
        public int iDeviceNumber;
        public int iFunctionNumber;
        public int iVendorID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string strAdapterName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string strDisplayName;
        public int iPresent;
        public int iExist;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string strDriverPath;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string strDriverPathExt;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string strPNPString;
        public int iOSDisplayIndex;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ADLODNExtSingleInitSetting
    {
        public int iMin;
        public int iMax;
        public int iStep;
        public int iDefault;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ADLODNCapabilitiesX2
    {
        public ADLODNExtSingleInitSetting sEngineClockRange;
        public ADLODNExtSingleInitSetting sMemoryClockRange;
        public ADLODNExtSingleInitSetting sVddcRange;
        public int iMaximumNumberOfPerformanceLevels;
        public int iExtPowerControlMin;
        public int iExtPowerControlMax;
        public int iExtPowerControlStep;
        public int iExtFanControlMode;
        public ADLODNExtSingleInitSetting sExtFanControlMinFanLimit;
        public int powerTuneTemperatureMin;
        public int powerTuneTemperatureMax;
        public int iFlags;
    }

    public static bool IsAvailable()
    {
        try
        {
            if (ADL2_Main_Control_Create(Alloc, 1, out var ctx) != ADL_OK || ctx == IntPtr.Zero)
                return false;
            ADL2_Main_Control_Destroy(ctx);
            return true;
        }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
        catch { return false; }
    }

    private static IntPtr Alloc(int size) => Marshal.AllocHGlobal(size);

    public static OverclockApplyResult ApplyOffsets(string gpuName, int coreOffsetMhz, int memOffsetMhz, int powerLimitPercent)
    {
        var ctx = IntPtr.Zero;
        try
        {
            if (ADL2_Main_Control_Create(Alloc, 1, out ctx) != ADL_OK || ctx == IntPtr.Zero)
                return new OverclockApplyResult(false, "ADL indisponible sur ce système (atiadlxx.dll).");

            int adapterIndex = FindAdapterIndex(ctx, gpuName);
            if (adapterIndex < 0)
                return new OverclockApplyResult(false, "Aucune carte AMD détectée par ADL.");

            var caps = new ADLODNCapabilitiesX2();
            if (ADL2_OverdriveN_CapabilitiesX2_Get(ctx, adapterIndex, ref caps) != ADL_OK)
                return new OverclockApplyResult(false,
                    "OverdriveN non pris en charge par ce pilote/cette carte. " +
                    "Aucune modification effectuée. Suivez les étapes guidées.");

            int maxLevels = caps.iMaximumNumberOfPerformanceLevels;
            if (maxLevels <= 0 || maxLevels > MaxLevels)
                return new OverclockApplyResult(false, "Réponse inattendue du pilote AMD (niveaux de performance).");

            // On repart des valeurs d'usine pour que les offsets ne soient jamais
            // cumulatifs d'un profil à l'autre.
            ADL2_OverdriveN_SystemClocksX2_Reset(ctx, adapterIndex);
            ADL2_OverdriveN_MemoryClocksX2_Reset(ctx, adapterIndex);
            ADL2_OverdriveN_PowerLimit_Reset(ctx, adapterIndex);

            var engine = ShiftClocks(ctx, adapterIndex, maxLevels, coreOffsetMhz * 100,
                caps.sEngineClockRange.iMin, caps.sEngineClockRange.iMax, isMemory: false);
            if (!engine.Success) return engine;

            var memory = ShiftClocks(ctx, adapterIndex, maxLevels, memOffsetMhz * 100,
                caps.sMemoryClockRange.iMin, caps.sMemoryClockRange.iMax, isMemory: true);
            if (!memory.Success) return memory;

            // Limite de puissance : best-effort (un échec ici n'invalide pas le reste).
            int powerOffset = Math.Clamp(powerLimitPercent - 100, caps.iExtPowerControlMin, caps.iExtPowerControlMax);
            ADL2_OverdriveN_PowerLimit_Set(ctx, adapterIndex, ref powerOffset);

            var what = $"Overclock appliqué : cœur +{coreOffsetMhz} MHz, mémoire +{memOffsetMhz} MHz, " +
                       $"limite de puissance {powerLimitPercent} %.";
            return new OverclockApplyResult(true, what);
        }
        catch (DllNotFoundException)
        {
            return new OverclockApplyResult(false, "Pilote AMD introuvable (atiadlxx.dll absent).");
        }
        finally
        {
            if (ctx != IntPtr.Zero) ADL2_Main_Control_Destroy(ctx);
        }
    }

    public static OverclockApplyResult Reset(string gpuName)
    {
        var ctx = IntPtr.Zero;
        try
        {
            if (ADL2_Main_Control_Create(Alloc, 1, out ctx) != ADL_OK || ctx == IntPtr.Zero)
                return new OverclockApplyResult(false, "ADL indisponible sur ce système (atiadlxx.dll).");

            int adapterIndex = FindAdapterIndex(ctx, gpuName);
            if (adapterIndex < 0)
                return new OverclockApplyResult(false, "Aucune carte AMD détectée par ADL.");

            int hr1 = ADL2_OverdriveN_SystemClocksX2_Reset(ctx, adapterIndex);
            int hr2 = ADL2_OverdriveN_MemoryClocksX2_Reset(ctx, adapterIndex);
            int hr3 = ADL2_OverdriveN_PowerLimit_Reset(ctx, adapterIndex);

            if (hr1 != ADL_OK && hr2 != ADL_OK && hr3 != ADL_OK)
                return new OverclockApplyResult(false,
                    "Le pilote AMD a refusé la réinitialisation (OverdriveN non pris en charge).");

            return new OverclockApplyResult(true, "Overclock réinitialisé (valeurs d'usine restaurées).");
        }
        catch (DllNotFoundException)
        {
            return new OverclockApplyResult(false, "Pilote AMD introuvable (atiadlxx.dll absent).");
        }
        finally
        {
            if (ctx != IntPtr.Zero) ADL2_Main_Control_Destroy(ctx);
        }
    }

    /// <summary>
    /// Lit les paliers de performance courants (cœur ou mémoire), décale chaque
    /// palier de <paramref name="offset10kHz"/> (en unités de 10 kHz, conformément à
    /// l'API ADL) en le bornant à [<paramref name="rangeMin"/>, <paramref name="rangeMax"/>],
    /// puis renvoie le tout au pilote.
    /// </summary>
    private static OverclockApplyResult ShiftClocks(
        IntPtr ctx, int adapterIndex, int maxLevels, int offset10kHz, int rangeMin, int rangeMax, bool isMemory)
    {
        var what = isMemory ? "mémoire" : "cœur";
        var bufferSize = HeaderSize + maxLevels * LevelSize;
        var buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            // En-tête pré-rempli (motif « cbSize ») pour que le pilote valide la taille du buffer.
            Marshal.WriteInt32(buffer, 0, bufferSize);
            Marshal.WriteInt32(buffer, 4, 0);
            Marshal.WriteInt32(buffer, 8, maxLevels);

            int hr = isMemory
                ? ADL2_OverdriveN_MemoryClocksX2_Get(ctx, adapterIndex, buffer)
                : ADL2_OverdriveN_SystemClocksX2_Get(ctx, adapterIndex, buffer);
            if (hr != ADL_OK)
                return new OverclockApplyResult(false, $"Lecture des fréquences AMD ({what}) impossible (OverdriveN).");

            int numLevels = Marshal.ReadInt32(buffer, 8);
            if (numLevels <= 0 || numLevels > maxLevels)
                return new OverclockApplyResult(false, $"Réponse inattendue du pilote AMD (niveaux {what}).");

            for (int i = 0; i < numLevels; i++)
            {
                int clockOffset = HeaderSize + i * LevelSize;
                int clock = Marshal.ReadInt32(buffer, clockOffset);
                int shifted = Math.Clamp(clock + offset10kHz, rangeMin, rangeMax);
                Marshal.WriteInt32(buffer, clockOffset, shifted);
            }

            hr = isMemory
                ? ADL2_OverdriveN_MemoryClocksX2_Set(ctx, adapterIndex, buffer)
                : ADL2_OverdriveN_SystemClocksX2_Set(ctx, adapterIndex, buffer);

            return hr == ADL_OK
                ? new OverclockApplyResult(true, "ok")
                : new OverclockApplyResult(false,
                    $"Le pilote AMD a refusé l'application des fréquences {what} (OverdriveN). " +
                    "Aucune autre modification effectuée.");
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>Trouve l'index ADL de l'adaptateur correspondant au GPU détecté via WMI.</summary>
    private static int FindAdapterIndex(IntPtr ctx, string gpuName)
    {
        if (ADL2_Adapter_NumberOfAdapters_Get(ctx, out int numAdapters) != ADL_OK || numAdapters <= 0)
            return -1;

        int size = Marshal.SizeOf<AdapterInfo>();
        var buffer = Marshal.AllocHGlobal(size * numAdapters);
        try
        {
            if (ADL2_Adapter_AdapterInfo_Get(ctx, buffer, size * numAdapters) != ADL_OK)
                return -1;

            int fallback = -1;
            for (int i = 0; i < numAdapters; i++)
            {
                var info = Marshal.PtrToStructure<AdapterInfo>(IntPtr.Add(buffer, i * size));
                if (info.iPresent == 0) continue;
                if (fallback < 0) fallback = info.iAdapterIndex;
                if (NamesMatch(info.strAdapterName, gpuName))
                    return info.iAdapterIndex;
            }
            return fallback;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static bool NamesMatch(string a, string b)
    {
        a = a.ToLowerInvariant();
        b = b.ToLowerInvariant();
        if (a.Contains(b) || b.Contains(a)) return true;
        foreach (var token in b.Split(' '))
            if (token.Length >= 4 && token.All(char.IsDigit) && a.Contains(token))
                return true;
        return false;
    }
}
