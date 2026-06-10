using CleanSlate.Core.Modules;
using Xunit;

namespace CleanSlate.Core.Tests;

/// <summary>
/// Tests du gestionnaire DLSS Enabler : parsing des bibliothèques Steam/Epic,
/// détection de la présence du mod, et prudence de la désinstallation manuelle
/// (ne jamais supprimer les DLL d'un autre mod).
/// </summary>
public class DlssEnablerTests
{
    // ------------------------------------------------------------------
    //  Parsing Steam (format Valve KeyValues)
    // ------------------------------------------------------------------

    [Fact]
    public void ParseLibraryFolders_ExtraitLesCheminsDesBibliotheques()
    {
        const string vdf = """
            "libraryfolders"
            {
                "0"
                {
                    "path"		"C:\\Program Files (x86)\\Steam"
                    "label"		""
                }
                "1"
                {
                    "path"		"D:\\SteamLibrary"
                    "label"		"Jeux"
                }
            }
            """;

        var paths = DlssEnablerService.ParseLibraryFolders(vdf);

        Assert.Equal(2, paths.Count);
        Assert.Contains(@"C:\Program Files (x86)\Steam", paths);
        Assert.Contains(@"D:\SteamLibrary", paths);
    }

    [Fact]
    public void ParseAppManifest_ExtraitNomEtDossier()
    {
        const string acf = """
            "AppState"
            {
                "appid"		"1091500"
                "name"		"Cyberpunk 2077"
                "StateFlags"		"4"
                "installdir"		"Cyberpunk 2077"
            }
            """;

        var (name, installDir) = DlssEnablerService.ParseAppManifest(acf);

        Assert.Equal("Cyberpunk 2077", name);
        Assert.Equal("Cyberpunk 2077", installDir);
    }

    [Theory]
    [InlineData("Steamworks Common Redistributables", true)]
    [InlineData("Steam Linux Runtime 3.0 (sniper)", true)]
    [InlineData("Proton 9.0", true)]
    [InlineData("Cyberpunk 2077", false)]
    [InlineData("The Witcher 3: Wild Hunt", false)]
    public void IsSteamTooling_FiltreLesOutilsNonJeux(string name, bool expected)
    {
        Assert.Equal(expected, DlssEnablerService.IsSteamTooling(name));
    }

    // ------------------------------------------------------------------
    //  Parsing Epic Games (manifestes .item JSON)
    // ------------------------------------------------------------------

    [Fact]
    public void ParseEpicManifest_ExtraitNomEtDossier()
    {
        const string json = """
            {
                "FormatVersion": 0,
                "DisplayName": "Alan Wake 2",
                "InstallLocation": "D:\\Epic\\AlanWake2",
                "bIsIncompleteInstall": false
            }
            """;

        var (name, installDir) = DlssEnablerService.ParseEpicManifest(json);

        Assert.Equal("Alan Wake 2", name);
        Assert.Equal(@"D:\Epic\AlanWake2", installDir);
    }

    // ------------------------------------------------------------------
    //  Détection de l'état du mod dans un dossier de jeu
    // ------------------------------------------------------------------

    private static string CreateGameDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"cleanslate-dlss-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void GetStatus_DossierVierge_NonInstalle()
    {
        var dir = CreateGameDir();
        try
        {
            var status = new DlssEnablerService().GetStatus(dir);
            Assert.False(status.Installed);
            Assert.Empty(status.DetectedFiles);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void GetStatus_FichiersDistinctifsPresents_Installe()
    {
        var dir = CreateGameDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "nvngx-wrapper.dll"), "x");
            File.WriteAllText(Path.Combine(dir, "dlssg_to_fsr3_amd_is_better.dll"), "x");

            var status = new DlssEnablerService().GetStatus(dir);

            Assert.True(status.Installed);
            Assert.Contains("nvngx-wrapper.dll", status.DetectedFiles);
            Assert.Contains("dlssg_to_fsr3_amd_is_better.dll", status.DetectedFiles);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void GetStatus_VariantePluginAsi_Installe()
    {
        var dir = CreateGameDir();
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "plugins"));
            File.WriteAllText(Path.Combine(dir, "plugins", "dlss-enabler.asi"), "x");

            var status = new DlssEnablerService().GetStatus(dir);

            Assert.True(status.Installed);
            Assert.Contains(@"plugins\dlss-enabler.asi", status.DetectedFiles);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void GetStatus_ChargeursSeuls_NonInstalle()
    {
        // version.dll / dxgi.dll seuls appartiennent probablement à un autre mod
        // (ReShade…) : ils ne suffisent pas à conclure que DLSS Enabler est là.
        var dir = CreateGameDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "version.dll"), "x");
            File.WriteAllText(Path.Combine(dir, "dxgi.dll"), "x");

            var status = new DlssEnablerService().GetStatus(dir);

            Assert.False(status.Installed);
            Assert.Empty(status.DetectedFiles);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // ------------------------------------------------------------------
    //  Désinstallation manuelle (repli sans désinstallateur Inno)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Uninstall_SupprimeLesFichiersDuMod_MaisJamaisCeuxDesAutresMods()
    {
        var dir = CreateGameDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "nvngx-wrapper.dll"), "x");
            File.WriteAllText(Path.Combine(dir, "dlss-enabler-upscaler.dll"), "x");
            Directory.CreateDirectory(Path.Combine(dir, "plugins"));
            File.WriteAllText(Path.Combine(dir, "plugins", "dlss-enabler.asi"), "x");
            // DLL chargeur SANS métadonnées DLSS Enabler : appartient à un autre mod.
            File.WriteAllText(Path.Combine(dir, "version.dll"), "reshade");
            // Fichier du jeu : ne doit évidemment pas bouger.
            File.WriteAllText(Path.Combine(dir, "game.exe"), "x");

            var svc = new DlssEnablerService();
            var ok = await svc.UninstallAsync(dir, CancellationToken.None);

            Assert.True(ok);
            Assert.False(File.Exists(Path.Combine(dir, "nvngx-wrapper.dll")));
            Assert.False(File.Exists(Path.Combine(dir, "dlss-enabler-upscaler.dll")));
            Assert.False(File.Exists(Path.Combine(dir, "plugins", "dlss-enabler.asi")));
            Assert.True(File.Exists(Path.Combine(dir, "version.dll")));  // mod tiers préservé
            Assert.True(File.Exists(Path.Combine(dir, "game.exe")));
            Assert.False(svc.GetStatus(dir).Installed);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task Uninstall_RienAInstalle_RenvoieFalse()
    {
        var dir = CreateGameDir();
        try
        {
            var ok = await new DlssEnablerService().UninstallAsync(dir, CancellationToken.None);
            Assert.False(ok);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
