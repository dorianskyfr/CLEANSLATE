using System.Runtime.InteropServices;
using System.Security.Principal;
using CleanSlate.Core.Native;

namespace CleanSlate.Core.Modules;

/// <summary>Instantané de l'état mémoire du système.</summary>
public readonly record struct MemorySnapshot(
    ulong TotalPhysicalBytes,
    ulong AvailablePhysicalBytes,
    uint MemoryLoadPercent)
{
    public ulong UsedPhysicalBytes => TotalPhysicalBytes - AvailablePhysicalBytes;
}

/// <summary>Résultat d'une optimisation mémoire.</summary>
public sealed record MemoryOptimizationResult(
    int  ProcessesEmptied,
    long FreedBytes,
    bool StandbyListCleared,
    string Message);

public interface IMemoryMonitor
{
    MemorySnapshot Read();

    /// <summary>
    /// Optimise la RAM : vide les working sets de tous les processus, puis — si
    /// <paramref name="clearStandbyList"/> et droits admin — purge la Standby List
    /// (mémoire inutilisée en cache, libérable immédiatement).
    /// Retourne les octets réellement libérés (différence avant/après).
    /// </summary>
    MemoryOptimizationResult OptimizeMemory(bool clearStandbyList);
}

/// <summary>Implémentation via GlobalMemoryStatusEx + NtSetSystemInformation.</summary>
public sealed class MemoryMonitor : IMemoryMonitor
{
    public MemorySnapshot Read()
    {
        var status = new NativeMethods.MEMORYSTATUSEX
        {
            dwLength = (uint)Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>()
        };
        if (!NativeMethods.GlobalMemoryStatusEx(ref status))
            return new MemorySnapshot(0, 0, 0);
        return new MemorySnapshot(status.ullTotalPhys, status.ullAvailPhys, status.dwMemoryLoad);
    }

    public MemoryOptimizationResult OptimizeMemory(bool clearStandbyList)
    {
        var before = Read();
        bool standbyCleared = false;

        // 1. Vider le working set de tous les processus accessibles.
        int emptied = 0;
        foreach (var proc in System.Diagnostics.Process.GetProcesses())
        {
            try { if (NativeMethods.EmptyWorkingSet(proc.Handle)) emptied++; }
            catch { }
            finally { proc.Dispose(); }
        }

        // 2. Si admin demandé : vider la Standby List (mémoire non utilisée en cache).
        if (clearStandbyList && IsAdmin())
        {
            try
            {
                // Activer le privilège SeProfileSingleProcessPrivilege si nécessaire.
                EnablePrivilege("SeProfileSingleProcessPrivilege");

                // a) Écrire la Modified List sur disque.
                uint cmd = NativeMethods.MemoryFlushModifiedList;
                NativeMethods.NtSetSystemInformation(
                    NativeMethods.SystemMemoryListInformation, ref cmd, sizeof(uint));

                // b) Purger la Standby List haute priorité.
                cmd = NativeMethods.MemoryPurgeStandbyList;
                NativeMethods.NtSetSystemInformation(
                    NativeMethods.SystemMemoryListInformation, ref cmd, sizeof(uint));

                // c) Purger la Standby List basse priorité.
                cmd = NativeMethods.MemoryPurgeLowPriorityStandbyList;
                NativeMethods.NtSetSystemInformation(
                    NativeMethods.SystemMemoryListInformation, ref cmd, sizeof(uint));

                standbyCleared = true;
            }
            catch { /* si le privilège est refusé, on continue sans */ }
        }

        var after = Read();
        long freed = (long)after.AvailablePhysicalBytes - (long)before.AvailablePhysicalBytes;
        if (freed < 0) freed = 0;

        var msg = standbyCleared
            ? $"{emptied} processus traités, Standby List vidée (+{FormatBytes(freed)} libérés)."
            : $"{emptied} processus traités (working sets). Standby List non vidée (droits admin requis).";

        return new MemoryOptimizationResult(emptied, freed, standbyCleared, msg);
    }

    private static bool IsAdmin()
    {
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    private static void EnablePrivilege(string privilegeName)
    {
        if (!NativeMethods.OpenProcessToken(
                System.Diagnostics.Process.GetCurrentProcess().Handle,
                NativeMethods.TOKEN_ADJUST_PRIVILEGES | NativeMethods.TOKEN_QUERY,
                out var token))
            return;

        if (!NativeMethods.LookupPrivilegeValue(null, privilegeName, out var luid))
            return;

        var tp = new NativeMethods.TOKEN_PRIVILEGES
        {
            PrivilegeCount = 1,
            Luid = luid,
            Attributes = NativeMethods.SE_PRIVILEGE_ENABLED,
        };
        NativeMethods.AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
    }

    private static string FormatBytes(long b)
    {
        string[] u = { "o", "Ko", "Mo", "Go" };
        double v = b; int i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return $"{v:0.#} {u[i]}";
    }
}
