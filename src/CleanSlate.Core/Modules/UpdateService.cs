using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;

namespace CleanSlate.Core.Modules;

public record UpdateInfo(string Version, string DownloadUrl, string ReleaseNotes, bool IsNewer);

public interface IUpdateService
{
    string CurrentVersion { get; }
    Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken ct);
    Task<string> DownloadAsync(UpdateInfo update, IProgress<double>? progress, CancellationToken ct);
    void LaunchInstaller(string exePath);
}

public sealed class GitHubUpdateService : IUpdateService
{
    private const string Owner = "dorianskyfr";
    private const string Repo  = "CLEANSLATE";

    public string CurrentVersion => "0.1"; // mis à jour à chaque release

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

    public void LaunchInstaller(string exePath)
    {
        if (!File.Exists(exePath)) return;
        Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
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
