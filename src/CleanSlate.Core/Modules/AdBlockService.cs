using System.Diagnostics;
using System.Net.Http;
using System.Text;

namespace CleanSlate.Core.Modules;

public interface IAdBlockService
{
    bool IsEnabled { get; }
    int BlockedDomainCount { get; }
    Task EnableAsync(IProgress<string>? progress, CancellationToken ct);
    Task DisableAsync(CancellationToken ct);
    Task UpdateListAsync(IProgress<string>? progress, CancellationToken ct);
}

public sealed class HostsAdBlockService : IAdBlockService
{
    private const string StartMarker = "# ==== CleanSlate AdBlock START ====";
    private const string EndMarker   = "# ==== CleanSlate AdBlock END ====";
    private const string HostsListUrl =
        "https://raw.githubusercontent.com/StevenBlack/hosts/master/hosts";

    private static readonly string HostsPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                     "drivers", "etc", "hosts");

    private static readonly string CachePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "CleanSlate", "adblock_hosts.txt");

    public bool IsEnabled => ReadHosts().Contains(StartMarker, StringComparison.Ordinal);

    public int BlockedDomainCount
    {
        get
        {
            var hosts = ReadHosts();
            if (!hosts.Contains(StartMarker, StringComparison.Ordinal)) return 0;
            return hosts.Split('\n')
                        .Count(l => !l.StartsWith('#') && l.TrimStart().StartsWith("0.0.0.0"));
        }
    }

    public async Task EnableAsync(IProgress<string>? progress, CancellationToken ct)
    {
        progress?.Report("Chargement de la liste de blocage…");
        var blockList = await GetOrDownloadListAsync(progress, ct);

        progress?.Report("Mise à jour du fichier hosts…");
        ApplyToHosts(blockList);

        progress?.Report("Vidage du cache DNS…");
        FlushDns();
    }

    public Task DisableAsync(CancellationToken ct)
    {
        RemoveFromHosts();
        FlushDns();
        return Task.CompletedTask;
    }

    public async Task UpdateListAsync(IProgress<string>? progress, CancellationToken ct)
    {
        progress?.Report("Téléchargement de la liste mise à jour…");
        var blockList = await DownloadListAsync(progress, ct);

        Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
        await File.WriteAllTextAsync(CachePath, blockList, Encoding.UTF8, ct);

        if (IsEnabled)
        {
            progress?.Report("Application de la nouvelle liste…");
            ApplyToHosts(blockList);
            progress?.Report("Vidage du cache DNS…");
            FlushDns();
        }
    }

    private async Task<string> GetOrDownloadListAsync(IProgress<string>? progress, CancellationToken ct)
    {
        if (File.Exists(CachePath))
        {
            progress?.Report("Utilisation de la liste en cache…");
            return await File.ReadAllTextAsync(CachePath, ct);
        }

        var list = await DownloadListAsync(progress, ct);
        Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
        await File.WriteAllTextAsync(CachePath, list, Encoding.UTF8, ct);
        return list;
    }

    private static async Task<string> DownloadListAsync(IProgress<string>? progress, CancellationToken ct)
    {
        progress?.Report("Téléchargement de la liste StevenBlack (~130 000 domaines)…");
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Add("User-Agent", "CleanSlate-AdBlock");
        http.Timeout = TimeSpan.FromSeconds(60);
        return await http.GetStringAsync(HostsListUrl, ct);
    }

    private static void ApplyToHosts(string blockList)
    {
        var current = ReadHosts();
        var stripped = StripExistingBlock(current);

        var entries = ExtractEntries(blockList);

        var sb = new StringBuilder(stripped.TrimEnd());
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine(StartMarker);
        foreach (var entry in entries)
            sb.AppendLine(entry);
        sb.AppendLine(EndMarker);

        File.WriteAllText(HostsPath, sb.ToString(), Encoding.UTF8);
    }

    private static void RemoveFromHosts()
    {
        var current = ReadHosts();
        File.WriteAllText(HostsPath, StripExistingBlock(current).TrimEnd() + Environment.NewLine, Encoding.UTF8);
    }

    private static string StripExistingBlock(string content)
    {
        var start = content.IndexOf(StartMarker, StringComparison.Ordinal);
        if (start < 0) return content;
        var end = content.IndexOf(EndMarker, start, StringComparison.Ordinal);
        if (end < 0) return content[..start];
        return content[..start] + content[(end + EndMarker.Length)..];
    }

    private static IEnumerable<string> ExtractEntries(string hostsContent)
    {
        foreach (var line in hostsContent.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("0.0.0.0", StringComparison.Ordinal))
                yield return trimmed;
        }
    }

    private static string ReadHosts()
    {
        try { return File.ReadAllText(HostsPath); }
        catch { return string.Empty; }
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
}
