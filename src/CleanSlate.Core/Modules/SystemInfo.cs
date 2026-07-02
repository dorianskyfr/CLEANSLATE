using System.Runtime.Versioning;
using Microsoft.Win32;

namespace CleanSlate.Core.Modules;

/// <summary>Vue d'ensemble d'un disque fixe (pour le tableau de bord).</summary>
public sealed record DriveOverview(string Name, string? Label, long TotalBytes, long FreeBytes)
{
    public long UsedBytes => TotalBytes - FreeBytes;
    public double UsedPercent => TotalBytes <= 0 ? 0 : (double)UsedBytes / TotalBytes * 100;
}

/// <summary>Vue d'ensemble du système affichée sur la page d'accueil.</summary>
public sealed record SystemOverview(
    string OsName,
    string CpuName,
    int LogicalCores,
    ulong TotalRamBytes,
    TimeSpan Uptime,
    IReadOnlyList<DriveOverview> Drives,
    uint MemoryLoadPercent = 0);

public interface ISystemInfoService
{
    SystemOverview Read();
}

/// <summary>
/// Lecture des informations système : registre (nom de Windows et du CPU),
/// GlobalMemoryStatusEx (RAM totale, via <see cref="IMemoryMonitor"/>),
/// <see cref="DriveInfo"/> (disques fixes) et tick système (uptime).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SystemInfoService : ISystemInfoService
{
    private readonly IMemoryMonitor _memory;

    public SystemInfoService(IMemoryMonitor? memory = null)
        => _memory = memory ?? new MemoryMonitor();

    public SystemOverview Read()
    {
        var mem = _memory.Read();
        return new SystemOverview(
            OsName:            ReadOsName(),
            CpuName:           ReadCpuName(),
            LogicalCores:      Environment.ProcessorCount,
            TotalRamBytes:     mem.TotalPhysicalBytes,
            Uptime:            TimeSpan.FromMilliseconds(Environment.TickCount64),
            Drives:            ReadDrives(),
            MemoryLoadPercent: mem.MemoryLoadPercent);
    }

    private static string ReadOsName()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            var product = key?.GetValue("ProductName") as string ?? "Windows";
            var display = key?.GetValue("DisplayVersion") as string;
            var build   = key?.GetValue("CurrentBuildNumber") as string;

            // Le registre annonce encore « Windows 10 » sur Windows 11 :
            // la distinction officielle se fait sur le numéro de build (>= 22000).
            if (int.TryParse(build, out var b) && b >= 22000)
                product = product.Replace("Windows 10", "Windows 11");

            return string.IsNullOrEmpty(display) ? product : $"{product} {display}";
        }
        catch { return Environment.OSVersion.VersionString; }
    }

    private static string ReadCpuName()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            return (key?.GetValue("ProcessorNameString") as string)?.Trim() ?? "Processeur inconnu";
        }
        catch { return "Processeur inconnu"; }
    }

    private static IReadOnlyList<DriveOverview> ReadDrives()
    {
        var drives = new List<DriveOverview>();
        try
        {
            foreach (var d in DriveInfo.GetDrives())
            {
                try
                {
                    if (!d.IsReady || d.DriveType != DriveType.Fixed) continue;
                    drives.Add(new DriveOverview(
                        d.Name.TrimEnd('\\'),
                        string.IsNullOrWhiteSpace(d.VolumeLabel) ? null : d.VolumeLabel,
                        d.TotalSize,
                        d.AvailableFreeSpace));
                }
                catch { /* lecteur inaccessible : ignoré */ }
            }
        }
        catch { }
        return drives;
    }
}
