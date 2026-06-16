using System.Net.Http;

namespace CleanSlate.Core.Modules;

/// <summary>
/// Téléchargement HTTPS générique avec progression, vers le dossier « Téléchargements »
/// de l'utilisateur. Utilisé pour récupérer les binaires des jeux open-source depuis
/// leurs dépôts officiels. Suit les redirections (ex. SourceForge « latest »).
/// </summary>
public interface IFileDownloadService
{
    /// <summary>
    /// Télécharge <paramref name="url"/> et renvoie le chemin local du fichier obtenu.
    /// <paramref name="suggestedFileName"/> est utilisé si le serveur n'indique pas de nom.
    /// </summary>
    Task<string> DownloadAsync(string url, string suggestedFileName, IProgress<double>? progress, CancellationToken ct);
}

public sealed class FileDownloadService : IFileDownloadService
{
    private readonly string _destDir;

    public FileDownloadService(string? destDir = null)
    {
        _destDir = destDir ?? DefaultDownloadsFolder();
    }

    public async Task<string> DownloadAsync(string url, string suggestedFileName, IProgress<double>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(_destDir);

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        // Un User-Agent navigateur Windows : SourceForge « latest » sert alors le binaire Windows.
        http.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) CleanSlate");

        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var fileName = ResolveFileName(response, suggestedFileName);
        var dest = Path.Combine(_destDir, fileName);

        var total = response.Content.Headers.ContentLength ?? -1L;
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var file = File.Create(dest);

        var buffer = new byte[81920];
        long downloaded = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            downloaded += read;
            if (total > 0) progress?.Report((double)downloaded / total * 100);
        }

        return dest;
    }

    /// <summary>Nom de fichier : Content-Disposition, sinon dernier segment de l'URL finale, sinon suggéré.</summary>
    private static string ResolveFileName(HttpResponseMessage response, string suggested)
    {
        var cd = response.Content.Headers.ContentDisposition?.FileNameStar
                 ?? response.Content.Headers.ContentDisposition?.FileName;
        if (!string.IsNullOrWhiteSpace(cd))
            return Sanitize(cd.Trim('"'));

        try
        {
            var finalUri = response.RequestMessage?.RequestUri;
            var last = finalUri is not null ? Path.GetFileName(finalUri.LocalPath) : null;
            if (!string.IsNullOrWhiteSpace(last) && last.Contains('.'))
                return Sanitize(last);
        }
        catch { }

        return Sanitize(suggested);
    }

    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "download.bin" : name;
    }

    private static string DefaultDownloadsFolder()
    {
        try
        {
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(profile))
            {
                var downloads = Path.Combine(profile, "Downloads");
                return Path.Combine(downloads, "CleanSlate");
            }
        }
        catch { }
        return Path.Combine(Path.GetTempPath(), "CleanSlate");
    }
}
