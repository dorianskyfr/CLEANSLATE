using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CleanSlate.Core.Modules;

public record UpdateInfo(string Version, string DownloadUrl, string ReleaseNotes, bool IsNewer);

/// <summary>
/// Mise à jour détectée mais pas encore installée, persistée sur disque pour que la
/// notification reste affichée même après fermeture/relance de l'application.
/// </summary>
public record PendingUpdateState(string Version, string DownloadUrl, string ReleaseNotes);

public interface IUpdateService
{
    string CurrentVersion { get; }
    Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken ct);
    Task<string> DownloadAsync(UpdateInfo update, IProgress<double>? progress, CancellationToken ct);
    void LaunchInstaller(string exePath);

    /// <summary>
    /// Recharge une éventuelle mise à jour en attente (détectée lors d'une session
    /// précédente mais ni téléchargée ni installée). Renvoie null si aucune mise à
    /// jour n'est en attente, ou si la version persistée n'est plus plus récente que
    /// <see cref="CurrentVersion"/> (auquel cas l'état est nettoyé).
    /// </summary>
    PendingUpdateState? LoadPendingUpdate();

    /// <summary>
    /// Persiste (ou efface si <paramref name="update"/> est null ou pas plus récente)
    /// l'état de la mise à jour disponible, afin que la notification survive à la
    /// fermeture de l'application.
    /// </summary>
    void SavePendingUpdate(UpdateInfo? update);
}

public sealed class GitHubUpdateService : IUpdateService
{
    private const string Owner = "dorianskyfr";
    private const string Repo  = "CLEANSLATE";

    private readonly string _stateFile;

    public GitHubUpdateService(string? stateFile = null)
    {
        _stateFile = stateFile ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CleanSlate", "update-state.json");
    }

    public string CurrentVersion => "1.0.0";

    public async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken ct)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Add("User-Agent", "CleanSlate-Updater");
        http.Timeout = TimeSpan.FromSeconds(15);

        var url = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";
        var release = await http.GetFromJsonAsync<GitHubRelease>(url, ct);
        if (release is null) return null;

        var remoteVersion = release.TagName?.TrimStart('v') ?? string.Empty;
        var isNewer = IsVersionNewer(CurrentVersion, remoteVersion);

        // Chercher l'asset .exe
        var asset = release.Assets?.FirstOrDefault(a =>
            a.Name?.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true);

        return new UpdateInfo(
            Version:      remoteVersion,
            DownloadUrl:  asset?.BrowserDownloadUrl ?? release.HtmlUrl ?? string.Empty,
            ReleaseNotes: release.Body ?? string.Empty,
            IsNewer:      isNewer);
    }

    public async Task<string> DownloadAsync(UpdateInfo update, IProgress<double>? progress, CancellationToken ct)
    {
        var destDir = Path.GetTempPath();
        var dest = Path.Combine(destDir, $"CleanSlate-v{update.Version}.exe");

        using var http = new HttpClient();
        http.DefaultRequestHeaders.Add("User-Agent", "CleanSlate-Updater");

        using var response = await http.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? -1L;
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        await using var file = File.Create(dest);

        var buffer = new byte[81920];
        long downloaded = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read), ct);
            downloaded += read;
            if (total > 0) progress?.Report((double)downloaded / total * 100);
        }

        return dest;
    }

    /// <summary>
    /// Lance la mise à jour téléchargée. Pour que la mise à jour « reste » (le
    /// raccourci/épingle continue de pointer vers une version à jour), on remplace
    /// l'exécutable actuel par le nouveau via un script qui attend la fermeture du
    /// processus courant, déplace le nouvel exécutable à la place de l'ancien, puis
    /// relance l'application depuis cet emplacement.
    /// </summary>
    public void LaunchInstaller(string exePath)
    {
        if (!File.Exists(exePath)) return;

        var currentExe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(currentExe) || !currentExe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            // Repli : on ne sait pas où se trouve l'exécutable actuel, on lance simplement le nouveau.
            Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
            return;
        }

        var pid = Environment.ProcessId;
        var scriptPath = Path.Combine(Path.GetTempPath(), $"cleanslate_update_{pid}.bat");

        var sb = new StringBuilder();
        sb.AppendLine("@echo off");
        sb.AppendLine("setlocal");
        sb.AppendLine($"set PID={pid}");
        sb.AppendLine("set COUNT=0");
        sb.AppendLine(":wait");
        sb.AppendLine("set /a COUNT+=1");
        sb.AppendLine("if %COUNT% gtr 30 goto forcekill");
        sb.AppendLine($"tasklist /fi \"PID eq %PID%\" 2>nul | find \"%PID%\" >nul");
        sb.AppendLine("if not errorlevel 1 (");
        sb.AppendLine("  timeout /t 1 /nobreak >nul");
        sb.AppendLine("  goto wait");
        sb.AppendLine(")");
        sb.AppendLine("goto move");
        sb.AppendLine(":forcekill");
        sb.AppendLine("taskkill /f /pid %PID% >nul 2>nul");
        sb.AppendLine(":move");
        sb.AppendLine($"move /y \"{exePath}\" \"{currentExe}\" >nul");
        sb.AppendLine($"start \"\" \"{currentExe}\"");
        sb.AppendLine("del \"%~f0\"");

        File.WriteAllText(scriptPath, sb.ToString(), Encoding.ASCII);

        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{scriptPath}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden,
        });
    }

    public PendingUpdateState? LoadPendingUpdate()
    {
        try
        {
            if (!File.Exists(_stateFile)) return null;

            var state = JsonSerializer.Deserialize<PendingUpdateState>(File.ReadAllText(_stateFile));
            if (state is null || string.IsNullOrEmpty(state.Version)) return null;

            // La mise à jour persistée a déjà été installée (ou dépassée) entre-temps.
            if (!IsVersionNewer(CurrentVersion, state.Version))
            {
                ClearPendingUpdate();
                return null;
            }

            return state;
        }
        catch { return null; }
    }

    public void SavePendingUpdate(UpdateInfo? update)
    {
        try
        {
            if (update is null || !update.IsNewer)
            {
                ClearPendingUpdate();
                return;
            }

            var dir = Path.GetDirectoryName(_stateFile);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var state = new PendingUpdateState(update.Version, update.DownloadUrl, update.ReleaseNotes);
            File.WriteAllText(_stateFile, JsonSerializer.Serialize(state));
        }
        catch { /* la persistance est un confort, pas une garantie : on ignore les erreurs disque */ }
    }

    private void ClearPendingUpdate()
    {
        try { if (File.Exists(_stateFile)) File.Delete(_stateFile); }
        catch { /* ignoré */ }
    }

    private static bool IsVersionNewer(string current, string remote)
    {
        try
        {
            var c = Version.Parse(current);
            var r = Version.Parse(remote);
            return r > c;
        }
        catch { return false; }
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }

        [JsonPropertyName("assets")]
        public List<GitHubAsset>? Assets { get; set; }
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; set; }
    }
}
