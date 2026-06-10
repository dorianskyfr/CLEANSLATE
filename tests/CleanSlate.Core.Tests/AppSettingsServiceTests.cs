using CleanSlate.Core.Modules;
using Xunit;

namespace CleanSlate.Core.Tests;

/// <summary>
/// Tests de la persistance des préférences utilisateur (thème, fenêtre) :
/// les choix doivent survivre au redémarrage de l'application.
/// </summary>
public class AppSettingsServiceTests
{
    private static AppSettingsService CreateService(out string file)
    {
        file = Path.Combine(Path.GetTempPath(), $"cleanslate-settings-test-{Guid.NewGuid():N}.json");
        return new AppSettingsService(file);
    }

    [Fact]
    public void Load_SansFichier_RenvoieLesValeursParDefaut()
    {
        var svc = CreateService(out var file);
        try
        {
            var settings = svc.Load();
            Assert.True(settings.IsDarkTheme);
            Assert.Equal(1060, settings.WindowWidth);
            Assert.Equal(720, settings.WindowHeight);
            Assert.False(settings.WindowMaximized);
        }
        finally { if (File.Exists(file)) File.Delete(file); }
    }

    [Fact]
    public void SavePuisLoad_RestitueLesPreferences()
    {
        var svc = CreateService(out var file);
        try
        {
            svc.Save(new AppSettings
            {
                IsDarkTheme = false,
                WindowWidth = 1400,
                WindowHeight = 900,
                WindowMaximized = true,
            });

            var loaded = svc.Load();

            Assert.False(loaded.IsDarkTheme);
            Assert.Equal(1400, loaded.WindowWidth);
            Assert.Equal(900, loaded.WindowHeight);
            Assert.True(loaded.WindowMaximized);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void Load_FichierCorrompu_RenvoieLesValeursParDefaut()
    {
        var svc = CreateService(out var file);
        try
        {
            File.WriteAllText(file, "{ pas du json ");
            var settings = svc.Load();
            Assert.True(settings.IsDarkTheme);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void Load_TailleAberrante_RetombeSurLesDimensionsParDefaut()
    {
        var svc = CreateService(out var file);
        try
        {
            File.WriteAllText(file,
                """{"IsDarkTheme":false,"WindowWidth":50,"WindowHeight":20,"WindowMaximized":false}""");

            var loaded = svc.Load();

            Assert.False(loaded.IsDarkTheme); // la préférence valide est conservée
            Assert.Equal(1060, loaded.WindowWidth);
            Assert.Equal(720, loaded.WindowHeight);
        }
        finally { File.Delete(file); }
    }
}
