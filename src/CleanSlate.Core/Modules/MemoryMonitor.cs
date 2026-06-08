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

/// <summary>
/// Module 3 — Surveillance de la RAM.
///
/// La SURVEILLANCE est fiable et utile (implémentée ici). La « libération » de RAM
/// est volontairement absente de l'implémentation par défaut : voir
/// docs/LIMITES-TECHNIQUES.md. Sur Windows moderne, forcer la libération du working
/// set ne fait que paginer vers le disque, ce qui RALENTIT généralement le système.
/// Si elle est ajoutée un jour, elle devra l'être avec un avertissement explicite.
/// </summary>
public interface IMemoryMonitor
{
    /// <summary>Lit l'état mémoire courant. Rafraîchissable par un timer côté UI.</summary>
    MemorySnapshot Read();
}

/// <summary>Implémentation via GlobalMemoryStatusEx (kernel32). Fiable.</summary>
public sealed class MemoryMonitor : IMemoryMonitor
{
    public MemorySnapshot Read()
    {
        var status = new NativeMethods.MEMORYSTATUSEX
        {
            dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>()
        };

        if (!NativeMethods.GlobalMemoryStatusEx(ref status))
            return new MemorySnapshot(0, 0, 0);

        return new MemorySnapshot(
            status.ullTotalPhys,
            status.ullAvailPhys,
            status.dwMemoryLoad);
    }
}
