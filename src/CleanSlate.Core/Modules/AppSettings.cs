using System.Text.Json;

namespace CleanSlate.Core.Modules;

/// <summary>
/// Préférences utilisateur persistées entre les sessions (thème, taille de fenêtre,
/// dossiers de jeux ajoutés à la main dans DLSS Enabler).
/// </summary>
public sealed record AppSettings
{
    public bool IsDarkTheme { get; init; } = true;
    public double WindowWidth { get; init; } = 1060;
    public double WindowHeight { get; init; } = 720;
    public bool WindowMaximized { get; init; }

    /// <summary>Dossiers de jeux ajoutés manuellement dans l'onglet DLSS Enabler.</summary>
    public IReadOnlyList<string> ManualGameFolders { get; init; } = Array.Empty<string>();

    /// <summary>Optimisation RAM automatique quand la charge mémoire dépasse le seuil.</summary>
    public bool AutoMemoryOptimize { get; init; }

    /// <summary>Seuil de charge mémoire (%) déclenchant l'optimisation automatique.</summary>
    public int AutoMemoryOptimizeThreshold { get; init; } = 90;

    /// <summary>Applications supplémentaires (noms de processus) à suspendre en Mode Jeu.</summary>
    public IReadOnlyList<string> CustomSuspendProcesses { get; init; } = Array.Empty<string>();

    /// <summary>Fournisseur DNS choisi pour le blocage de pub (id, ex. « adguard »).</summary>
    public string AdBlockProvider { get; init; } = "adguard";
}

public interface IAppSettingsService
{
    /// <summary>Charge les préférences ; renvoie les valeurs par défaut si absentes ou illisibles.</summary>
    AppSettings Load();

    /// <summary>Persiste les préférences (best effort : une erreur disque est ignorée).</summary>
    void Save(AppSettings settings);
}

/// <summary>
/// Persistance JSON dans %LOCALAPPDATA%\CleanSlate\settings.json, comme les autres
/// états de l'application (mise à jour en attente, instantané Mode Jeu…).
/// </summary>
public sealed class AppSettingsService : IAppSettingsService
{
    private readonly string _file;

    public AppSettingsService(string? file = null)
    {
        _file = file ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CleanSlate", "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_file)) return new AppSettings();
            var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_file));
            if (loaded is null) return new AppSettings();

            // Garde-fou : une taille aberrante (écran débranché, fichier édité à la
            // main…) est ramenée aux dimensions par défaut.
            var defaults = new AppSettings();
            if (loaded.WindowWidth < 860 || loaded.WindowHeight < 560 ||
                double.IsNaN(loaded.WindowWidth) || double.IsNaN(loaded.WindowHeight))
            {
                loaded = loaded with
                {
                    WindowWidth = defaults.WindowWidth,
                    WindowHeight = defaults.WindowHeight,
                };
            }
            return loaded;
        }
        catch { return new AppSettings(); }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            var dir = Path.GetDirectoryName(_file);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(_file, JsonSerializer.Serialize(settings));
        }
        catch { /* préférence de confort : ne doit jamais faire échouer l'app */ }
    }
}
