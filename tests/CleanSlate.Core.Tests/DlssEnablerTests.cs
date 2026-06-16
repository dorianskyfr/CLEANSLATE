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
    public void ParseAppManifest_ExtraitAppIdNomEtDossier()
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

        var (appId, name, installDir) = DlssEnablerService.ParseAppManifest(acf);

        Assert.Equal("1091500", appId);
        Assert.Equal("Cyberpunk 2077", name);
        Assert.Equal("Cyberpunk 2077", installDir);
    }

    // ------------------------------------------------------------------
    //  Jaquettes (cache local Steam, CDN en repli)
    // ------------------------------------------------------------------

    [Fact]
    public void FindSteamCover_AncienFormat_TrouveLeFichierPlat()
    {
        var root = CreateGameDir();
        try
        {
            var cache = Path.Combine(root, "appcache", "librarycache");
            Directory.CreateDirectory(cache);
            var cover = Path.Combine(cache, "1091500_library_600x900.jpg");
            File.WriteAllText(cover, "jpg");

            Assert.Equal(cover, DlssEnablerService.FindSteamCover(root, "1091500"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void FindSteamCover_NouveauFormat_TrouveLeSousDossier()
    {
        var root = CreateGameDir();
        try
        {
            var sub = Path.Combine(root, "appcache", "librarycache", "1091500");
            Directory.CreateDirectory(sub);
            var cover = Path.Combine(sub, "library_600x900.jpg");
            File.WriteAllText(cover, "jpg");

            Assert.Equal(cover, DlssEnablerService.FindSteamCover(root, "1091500"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void FindSteamCover_RienEnCache_RenvoieNull()
    {
        var root = CreateGameDir();
        try
        {
            Assert.Null(DlssEnablerService.FindSteamCover(root, "1091500"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void SteamCoverUrl_PointeVersLeCdnOfficiel()
    {
        Assert.Equal(
            "https://cdn.cloudflare.steamstatic.com/steam/apps/1091500/library_600x900.jpg",
            DlssEnablerService.SteamCoverUrl("1091500"));
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

    [Theory]
    [InlineData("Unreal Engine", true)]
    [InlineData("Unreal Engine 5.4", true)]
    [InlineData("Unreal Editor", true)]
    [InlineData("UE_5.3", true)]
    [InlineData("Epic Games Launcher", true)]
    [InlineData("Fortnite", false)]
    [InlineData("Alan Wake 2", false)]
    public void IsEpicTooling_FiltreLeMoteurPasLesJeux(string name, bool expected)
    {
        Assert.Equal(expected, DlssEnablerService.IsEpicTooling(name));
    }

    [Theory]
    [InlineData("Minecraft Launcher", "Minecraft")]
    [InlineData("Cyberpunk 2077™", "Cyberpunk 2077")]
    [InlineData("DOOM Eternal: Deluxe Edition", "DOOM Eternal")]
    [InlineData("The Witcher 3: Wild Hunt", "The Witcher 3: Wild Hunt")]
    [InlineData("Minecraft for Windows 10", "Minecraft")]
    public void NormalizeForSearch_NettoieLesSuffixesEtMarques(string input, string expected)
    {
        Assert.Equal(expected, DlssEnablerService.NormalizeForSearch(input));
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

    // ------------------------------------------------------------------
    //  Installation depuis le DLL embarqué (choix du nom de proxy)
    // ------------------------------------------------------------------

    [Fact]
    public void EmbeddedVersion_RenvoieLaVersionDuDllEmbarque()
    {
        Assert.Equal("4.7.8.1", new DlssEnablerService().EmbeddedVersion);
    }

    [Fact]
    public void ChooseProxyFileName_DossierVierge_RenvoieLePremierNomDeLaListe()
    {
        var dir = CreateGameDir();
        try
        {
            Assert.Equal(DlssEnablerService.LoaderFiles[0], DlssEnablerService.ChooseProxyFileName(dir));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void ChooseProxyFileName_PremierNomPrisParAutreMod_RenvoieLeSuivantLibre()
    {
        var dir = CreateGameDir();
        try
        {
            // Un autre mod (ex. ReShade) occupe déjà le premier nom de la liste.
            File.WriteAllText(Path.Combine(dir, DlssEnablerService.LoaderFiles[0]), "reshade");

            Assert.Equal(DlssEnablerService.LoaderFiles[1], DlssEnablerService.ChooseProxyFileName(dir));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void ChooseProxyFileName_TousLesNomsPrisParAutresMods_RenvoieNull()
    {
        var dir = CreateGameDir();
        try
        {
            foreach (var loader in DlssEnablerService.LoaderFiles)
                File.WriteAllText(Path.Combine(dir, loader), "autre mod");

            Assert.Null(DlssEnablerService.ChooseProxyFileName(dir));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task InstallAsync_CopieLeDllEmbarqueSousLePremierNomLibre_EtDetecteLInstallation()
    {
        var dir = CreateGameDir();
        try
        {
            var svc = new DlssEnablerService();
            var result = await svc.InstallAsync(dir, CancellationToken.None);

            Assert.True(result.Success);
            Assert.Equal(DlssEnablerService.LoaderFiles[0], result.InstalledFile);
            Assert.Equal(dir, result.TargetDir);
            Assert.True(File.Exists(Path.Combine(dir, DlssEnablerService.LoaderFiles[0])));
            Assert.True(svc.GetStatus(dir).Installed);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task InstallAsync_TousLesProxyOccupes_PoseLePluginAsi()
    {
        var dir = CreateGameDir();
        try
        {
            foreach (var loader in DlssEnablerService.LoaderFiles)
                File.WriteAllText(Path.Combine(dir, loader), "autre mod");

            var svc = new DlssEnablerService();
            var result = await svc.InstallAsync(dir, CancellationToken.None);

            Assert.True(result.Success);
            Assert.Equal(@"plugins\dlss-enabler.asi", result.InstalledFile);
            Assert.True(File.Exists(Path.Combine(dir, "plugins", "dlss-enabler.asi")));
            // Les DLL des autres mods ne sont pas écrasées.
            foreach (var loader in DlssEnablerService.LoaderFiles)
                Assert.Equal("autre mod", File.ReadAllText(Path.Combine(dir, loader)));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task InstallAsync_DossierInexistant_EchoueAvecLaBonneRaison()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"cleanslate-dlss-test-{Guid.NewGuid():N}");
        var result = await new DlssEnablerService().InstallAsync(dir, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(DlssInstallFailure.GameDirMissing, result.Failure);
    }

    // ------------------------------------------------------------------
    //  Localisation de l'exécutable du jeu (le proxy doit être posé à côté)
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("unins000.exe", true)]
    [InlineData("setup.exe", true)]
    [InlineData("vc_redist.x64.exe", true)]
    [InlineData("DXSETUP.exe", true)]
    [InlineData("UnityCrashHandler64.exe", true)]
    [InlineData("CrashReportClient.exe", true)]
    [InlineData("EasyAntiCheat_Setup.exe", true)]
    [InlineData("Cyberpunk2077.exe", false)]
    [InlineData("Witcher3.exe", false)]
    [InlineData("eldenring.exe", false)]
    public void IsHelperExecutable_FiltreLesUtilitaires(string name, bool expected)
    {
        Assert.Equal(expected, DlssEnablerService.IsHelperExecutable(name));
    }

    [Fact]
    public void FindExecutableDir_ExeDansUnSousDossier_TrouveLeSousDossier()
    {
        // Disposition type Unreal Engine / CD Projekt : petit lanceur à la racine,
        // vrai binaire (plus volumineux) dans bin\x64.
        var dir = CreateGameDir();
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "launcher.exe"), new byte[10]);
            var binDir = Path.Combine(dir, "bin", "x64");
            Directory.CreateDirectory(binDir);
            File.WriteAllBytes(Path.Combine(binDir, "Game.exe"), new byte[100]);

            Assert.Equal(binDir, DlssEnablerService.FindExecutableDir(dir));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void FindExecutableDir_IgnoreLesUtilitaires_MemePlusGros()
    {
        var dir = CreateGameDir();
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "Game.exe"), new byte[50]);
            var redist = Path.Combine(dir, "tools");
            Directory.CreateDirectory(redist);
            File.WriteAllBytes(Path.Combine(redist, "vc_redist.x64.exe"), new byte[5000]);

            Assert.Equal(dir, DlssEnablerService.FindExecutableDir(dir));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void FindExecutableDir_AucunExe_RenvoieNull_EtResolveRetombeSurLaRacine()
    {
        var dir = CreateGameDir();
        try
        {
            Assert.Null(DlssEnablerService.FindExecutableDir(dir));
            Assert.Equal(dir, DlssEnablerService.ResolveInstallDir(dir));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task InstallAsync_ExeDansUnSousDossier_PoseLeProxyACoteDeLExe()
    {
        var dir = CreateGameDir();
        try
        {
            var binDir = Path.Combine(dir, "Binaries", "Win64");
            Directory.CreateDirectory(binDir);
            File.WriteAllBytes(Path.Combine(binDir, "Game-Win64-Shipping.exe"), new byte[100]);

            var svc = new DlssEnablerService();
            var result = await svc.InstallAsync(dir, CancellationToken.None);

            Assert.True(result.Success);
            Assert.Equal(binDir, result.TargetDir);
            Assert.True(File.Exists(Path.Combine(binDir, DlssEnablerService.LoaderFiles[0])));
            // Rien posé à la racine : le proxy doit être à côté de l'exe pour être chargé.
            Assert.False(File.Exists(Path.Combine(dir, DlssEnablerService.LoaderFiles[0])));

            // La détection et la désinstallation voient/retirent le mod dans le sous-dossier.
            Assert.True(svc.GetStatus(dir).Installed);
            Assert.True(await svc.UninstallAsync(dir, CancellationToken.None));
            Assert.False(File.Exists(Path.Combine(binDir, DlssEnablerService.LoaderFiles[0])));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void CanWriteTo_DossierTemporaire_RenvoieTrue()
    {
        var dir = CreateGameDir();
        try
        {
            Assert.True(DlssEnablerService.CanWriteTo(dir));
            // Le fichier témoin ne doit laisser aucune trace.
            Assert.Empty(Directory.GetFiles(dir));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // ------------------------------------------------------------------
    //  Filtrage des dossiers « XboxGames » (un vrai jeu, pas « GameSave »)
    // ------------------------------------------------------------------

    [Fact]
    public void IsGamePassGameFolder_AvecContentEtExe_EstUnJeu()
    {
        var dir = CreateGameDir();
        try
        {
            var content = Path.Combine(dir, "Content");
            Directory.CreateDirectory(content);
            File.WriteAllText(Path.Combine(content, "Game.exe"), "x");

            Assert.True(DlssEnablerService.IsGamePassGameFolder(dir));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void IsGamePassGameFolder_AvecManifesteXbox_EstUnJeu()
    {
        var dir = CreateGameDir();
        try
        {
            var content = Path.Combine(dir, "Content");
            Directory.CreateDirectory(content);
            File.WriteAllText(Path.Combine(content, "MicrosoftGame.config"), "<Game/>");

            Assert.True(DlssEnablerService.IsGamePassGameFolder(dir));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void IsGamePassGameFolder_DossierGameSave_NestPasUnJeu()
    {
        // Reproduit le cas signalé : « C:\XboxGames\GameSave » n'est pas un jeu.
        var parent = CreateGameDir();
        try
        {
            var gameSave = Path.Combine(parent, "GameSave");
            Directory.CreateDirectory(Path.Combine(gameSave, "Content"));

            Assert.False(DlssEnablerService.IsGamePassGameFolder(gameSave));
        }
        finally { Directory.Delete(parent, recursive: true); }
    }

    [Fact]
    public void IsGamePassGameFolder_SansContent_NestPasUnJeu()
    {
        var dir = CreateGameDir();
        try
        {
            Assert.False(DlssEnablerService.IsGamePassGameFolder(dir));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // ------------------------------------------------------------------
    //  Compatibilité DLSS (le mod n'ajoute pas le DLSS à un jeu qui n'en a pas)
    // ------------------------------------------------------------------

    [Fact]
    public void GetCompatibility_DlssNatif_EstCompatible()
    {
        var dir = CreateGameDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "nvngx_dlss.dll"), "x");

            var info = new DlssEnablerService().GetCompatibility(dir);

            Assert.Equal(DlssCompatibility.Compatible, info.Level);
            Assert.Contains("nvngx_dlss.dll", info.Evidence);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void GetCompatibility_UpscalerFsrXess_EstPeutEtre()
    {
        var dir = CreateGameDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "libxess.dll"), "x");

            var info = new DlssEnablerService().GetCompatibility(dir);

            Assert.Equal(DlssCompatibility.Maybe, info.Level);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void GetCompatibility_AucunUpscaler_EstImprobable()
    {
        var dir = CreateGameDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "Game.exe"), "x");
            File.WriteAllText(Path.Combine(dir, "engine.dll"), "x");

            var info = new DlssEnablerService().GetCompatibility(dir);

            Assert.Equal(DlssCompatibility.Unlikely, info.Level);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void GetCompatibility_DllDansSousDossier_EstTrouve()
    {
        var dir = CreateGameDir();
        try
        {
            var sub = Path.Combine(dir, "Engine", "Binaries", "ThirdParty");
            Directory.CreateDirectory(sub);
            File.WriteAllText(Path.Combine(sub, "nvngx_dlssg.dll"), "x");

            var info = new DlssEnablerService().GetCompatibility(dir);

            Assert.Equal(DlssCompatibility.Compatible, info.Level);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    /// <summary>
    /// Reproduction du cas Subnautica 2 (Unreal Engine 5) : le composant Streamline est
    /// rangé sous &lt;Jeu&gt;\Plugins\StreamlineCore\Binaries\ThirdParty\Win64, soit 6 niveaux
    /// sous la racine — au-delà de l'ancienne profondeur de recherche (4).
    /// </summary>
    [Fact]
    public void GetCompatibility_DllProfondementImbrique_EstTrouve()
    {
        var dir = CreateGameDir();
        try
        {
            var sub = Path.Combine(dir, "Subnautica2", "Plugins", "StreamlineCore", "Binaries", "ThirdParty", "Win64");
            Directory.CreateDirectory(sub);
            File.WriteAllText(Path.Combine(sub, "sl.interposer.dll"), "x");

            var info = new DlssEnablerService().GetCompatibility(dir);

            Assert.Equal(DlssCompatibility.Compatible, info.Level);
            Assert.Contains("sl.interposer.dll", info.Evidence);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    /// <summary>
    /// Les DLL situées dans des dossiers de contenu « cuit » (Content, Paks…) ne sont pas
    /// recherchées : ce ne sont jamais des plugins, et ces dossiers peuvent être énormes.
    /// </summary>
    [Fact]
    public void GetCompatibility_DllDansContentIgnore()
    {
        var dir = CreateGameDir();
        try
        {
            var sub = Path.Combine(dir, "Subnautica2", "Content", "Movies");
            Directory.CreateDirectory(sub);
            File.WriteAllText(Path.Combine(sub, "nvngx_dlss.dll"), "x");

            var info = new DlssEnablerService().GetCompatibility(dir);

            Assert.Equal(DlssCompatibility.Unlikely, info.Level);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
