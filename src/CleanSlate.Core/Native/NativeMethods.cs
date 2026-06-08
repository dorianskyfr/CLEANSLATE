using System.Runtime.InteropServices;

namespace CleanSlate.Core.Native;

/// <summary>
/// Déclarations P/Invoke regroupées. On privilégie les API officielles
/// (shell, psapi) plutôt que des bidouilles, conformément à la philosophie
/// du projet.
/// </summary>
internal static class NativeMethods
{
    // ---------------------------------------------------------------------
    // Corbeille — SHEmptyRecycleBin (shell32). On vide la corbeille via l'API
    // officielle plutôt que de supprimer des fichiers à la main dans $Recycle.Bin.
    // ---------------------------------------------------------------------
    [Flags]
    public enum RecycleFlags : uint
    {
        SHERB_NOCONFIRMATION = 0x00000001, // pas de confirmation Windows (la nôtre suffit)
        SHERB_NOPROGRESSUI   = 0x00000002, // pas d'UI de progression Windows
        SHERB_NOSOUND        = 0x00000004, // pas de son
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    public static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, RecycleFlags dwFlags);

    [StructLayout(LayoutKind.Sequential)]
    public struct SHQUERYRBINFO
    {
        public int cbSize;
        public long i64Size;     // taille totale dans la corbeille
        public long i64NumItems; // nombre d'éléments
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    public static extern int SHQueryRecycleBin(string? pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

    // ---------------------------------------------------------------------
    // Mémoire — module 3 (surveillance). GlobalMemoryStatusEx (kernel32).
    // ---------------------------------------------------------------------
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;          // % d'utilisation mémoire
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    /// <summary>
    /// Vide le working set d'un processus (psapi). ⚠️ Voir LIMITES-TECHNIQUES :
    /// cela force surtout la pagination vers le disque, gain réel généralement nul.
    /// </summary>
    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EmptyWorkingSet(IntPtr hProcess);

    // ---------------------------------------------------------------------
    // Mode Jeu (module 4) — suspension/reprise de processus via ntdll.
    // On SUSPEND (réversible) plutôt que de tuer. Nt* renvoie un NTSTATUS
    // (0 = STATUS_SUCCESS).
    // ---------------------------------------------------------------------
    [DllImport("ntdll.dll")]
    public static extern uint NtSuspendProcess(IntPtr processHandle);

    [DllImport("ntdll.dll")]
    public static extern uint NtResumeProcess(IntPtr processHandle);
}
