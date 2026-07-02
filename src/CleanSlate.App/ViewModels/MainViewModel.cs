using System.Collections.ObjectModel;
using System.Windows.Input;
using CleanSlate.Core.Modules;
using CleanSlate.App.Infrastructure;

namespace CleanSlate.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private const string PatchNotes =
        "─────────────────────────────\n" +
        "v2.5.0 (2026-07)\n" +
        "─────────────────────────────\n" +
        "Nouveautés :\n" +
        "• 🔍 Détecteur de doublons (onglet Espace disque) : trouve les fichiers\n" +
        "  identiques par CONTENU (empreinte SHA-256), avec l'espace récupérable.\n" +
        "  Lecture seule — vous décidez quoi supprimer.\n" +
        "• ❤️ Score de santé système (Accueil) : une note /100 honnête, agrégée\n" +
        "  à partir de l'espace disque, la charge RAM et l'uptime, avec conseils.\n" +
        "• 📄 Rapport système exportable : bouton « Exporter un rapport » qui\n" +
        "  enregistre un rapport texte sur le Bureau (dépannage/support).\n\n" +
        "Corrections :\n" +
        "• 🛠️ Réparation rapide : nettoyage des temporaires en arrière-plan\n" +
        "  (l'UI ne se fige plus), scan du journal d'événements optimisé, et\n" +
        "  correctif SFC (plus de conflit runas / redirection de sortie).\n" +
        "• 🔒 Garde-fous sur les préférences (seuils/intervalles) au chargement.\n\n" +

        "─────────────────────────────\n" +
        "v2.0.0 (2026-07)\n" +
        "─────────────────────────────\n" +
        "CleanSlate 2.0 — version majeure.\n\n" +
        "Nouveautés :\n" +
        "• 💽 Analyseur d'espace disque : nouvel onglet qui liste les plus gros\n" +
        "  dossiers/fichiers d'un lecteur (lecture seule, ouvre l'Explorateur).\n" +
        "• ⏱️ Entretien automatique programmé (Accueil) : nettoyage sûr + RAM\n" +
        "  toutes les 6/12/24/48 h, en arrière-plan. Désactivé par défaut.\n" +
        "• ↩️ Windows Debloat réversible : sauvegarde de l'état d'origine\n" +
        "  (registre, services, tâches) avant modif + bouton « Tout restaurer ».\n" +
        "• 🧹 Deux catégories de nettoyage : Cache de Windows Update et\n" +
        "  Vidages mémoire de plantage (CrashDumps/Minidump).\n\n" +
        "Interface :\n" +
        "• 🎨 Refonte visuelle : palettes sombre/claire modernisées, boutons\n" +
        "  arrondis avec états survol/pression, navigation plus lisible.\n\n" +
        "Sous le capot : libération propre des objets WMI (pilotes), version\n" +
        "d'exécutable 2.0.0, nouveaux tests.\n\n" +

        "─────────────────────────────\n" +
        "v1.5.0 (2026-06)\n" +
        "─────────────────────────────\n" +
        "Version de fiabilité — une passe d'améliorations sur tout CleanSlate.\n\n" +
        "Corrections :\n" +
        "• Pilotes NVIDIA : comparaison de version corrigée (566.14 était vu à tort\n" +
        "  comme plus ancien que 566.9 — certaines mises à jour étaient ratées).\n" +
        "• Cache navigateurs : Opera et Opera GX sont enfin réellement nettoyés.\n" +
        "• Nettoyage du registre : plus de faux positifs — les variables\n" +
        "  d'environnement sont développées et les commandes via PATH (rundll32…)\n" +
        "  ne sont jamais signalées comme orphelines.\n" +
        "• Optimisation RAM : le « +X libéré » est désormais présenté comme une\n" +
        "  variation indicative (chiffre honnête), et une fuite de handle est corrigée.\n" +
        "• Mode Jeu : restauration garantie même si l'activation est interrompue\n" +
        "  (instantané écrit au fur et à mesure, handles libérés proprement).\n" +
        "• Bloqueur de pub : la restauration du DNS d'origine est vérifiée — la\n" +
        "  sauvegarde est conservée en cas d'échec (config d'origine préservée).\n" +
        "• Windows Debloat : décompte réel des applications retirées + doublon retiré.\n" +
        "• Interface : les boutons « Optimiser la RAM » / « Réparer » ne restent plus\n" +
        "  grisés en cas d'erreur ; choix du fournisseur DNS protégé.\n\n" +
        "Sous le capot : HttpClient partagé (pas d'épuisement des sockets),\n" +
        "métadonnées de version correctes dans l'EXE (1.5.0), nouveaux tests.\n\n" +

        "─────────────────────────────\n" +
        "v1.3.8 (2026-06)\n" +
        "─────────────────────────────\n" +
        "• Mode Jeu : l'onglet « 🎮 Bibliothèque » est retiré. Le scan des jeux\n" +
        "  (Steam / Epic / Game Pass), la recherche dans le catalogue Steam, les\n" +
        "  jeux open-source et l'import de source JSON sont supprimés. Le Mode Jeu\n" +
        "  conserve la suspension d'applications, l'Overclocking et le DLSS Enabler.\n\n" +

        "─────────────────────────────\n" +
        "v1.3.7 (2026-06)\n" +
        "─────────────────────────────\n" +
        "• Bibliothèque : « 📥 Importer une source (JSON) » — chargez un fichier JSON\n" +
        "  listant des ressources et téléchargez-les en HTTPS direct, avec progression,\n" +
        "  vérification SHA-256 optionnelle et gestion d'erreurs. Les liens magnet/\n" +
        "  torrent sont ignorés : seuls les téléchargements HTTPS directs sont gérés.\n\n" +

        "─────────────────────────────\n" +
        "v1.3.6 (2026-06)\n" +
        "─────────────────────────────\n" +
        "• Bibliothèque : recherche mondiale — tapez un titre pour parcourir TOUS les\n" +
        "  jeux du monde (catalogue officiel Steam) avec leurs jaquettes. Vos jeux\n" +
        "  installés se lancent d'un clic (▶), les autres ouvrent leur page Steam (🛒).\n" +
        "• Bibliothèque : section « 🐧 Jeux open-source » — téléchargez et installez des\n" +
        "  jeux libres (SuperTuxKart, Wesnoth, Warzone 2100…) depuis leurs dépôts\n" +
        "  officiels, avec barre de progression. (Aucun contenu piraté.)\n" +
        "• Overclocking : VRAM enfin correcte sur les cartes ≥ 4 Go (RTX 3070, 4080…).\n" +
        "  CleanSlate lit maintenant la vraie valeur 64 bits du registre EN PRIORITÉ\n" +
        "  (WMI tronquait à ~4 Go même quand la valeur n'était pas exactement plafonnée).\n\n" +

        "─────────────────────────────\n" +
        "v1.3.5 (2026-06)\n" +
        "─────────────────────────────\n" +
        "• Mode Jeu : nouvel onglet « 🎮 Bibliothèque » — Hydra Launcher intégré.\n" +
        "  Détecte tous vos jeux (Steam / Epic / Xbox Game Pass), affiche les jaquettes\n" +
        "  et lance n'importe quel jeu d'un clic, sans quitter CleanSlate.\n" +
        "• Overclocking : correction de l'affichage de la VRAM sur les cartes ≥ 4 Go\n" +
        "  (RTX 3070, 4080…). WMI retournait 4 Go à cause d'un dépassement 32 bits ;\n" +
        "  CleanSlate lit désormais la vraie valeur 64 bits depuis le registre Windows.\n\n" +

        "─────────────────────────────\n" +
        "v1.3.0 (2026-06)\n" +
        "─────────────────────────────\n" +
        "• Mode Jeu : suspension d'applications repensée — liste à COCHER\n" +
        "  groupée par catégorie, avec 3 profils prêts à l'emploi\n" +
        "  (🍃 Léger / ⚖️ Équilibré / 🔥 Agressif). Sélection mémorisée.\n" +
        "• Overclocking : bouton « 📥 Importer depuis MSI Afterburner » —\n" +
        "  reprend votre offset cœur/mémoire dans le mode Personnalisé.\n" +
        "• DLSS Enabler : « Unreal Engine » n'est plus listé comme un jeu.\n" +
        "• Jaquettes : noms nettoyés avant recherche (Launcher, ™, éditions)\n" +
        "  → beaucoup plus de jeux hors Steam affichent leur visuel.\n\n" +

        "─────────────────────────────\n" +
        "v1.2.7 (2026-06)\n" +
        "─────────────────────────────\n" +
        "• DLSS Enabler : correction de faux négatifs de compatibilité — la\n" +
        "  recherche des composants DLSS/Streamline va maintenant 8 niveaux\n" +
        "  de profondeur (au lieu de 4), couvrant les jeux Unreal Engine 5\n" +
        "  récents (ex. Subnautica 2) qui les rangent très profondément.\n" +
        "• Le message « rien trouvé » précise désormais que ce n'est pas une\n" +
        "  certitude (la vérification a ses limites) — l'installation reste\n" +
        "  possible, sans risque et réversible.\n\n" +

        "─────────────────────────────\n" +
        "v1.2.6 (2026-06)\n" +
        "─────────────────────────────\n" +
        "• DLSS Enabler : vérification de compatibilité avant installation —\n" +
        "  le mod n'ajoute pas le DLSS à un jeu qui n'en a pas, c'est signalé\n" +
        "  clairement (compatible / possible via FSR-XeSS / improbable).\n" +
        "• Game Pass : les faux jeux du dossier XboxGames (ex. « GameSave »)\n" +
        "  ne sont plus listés.\n" +
        "• Jaquettes : les jeux sans image (Epic, Game Pass, manuels) récupèrent\n" +
        "  leur visuel via une recherche par nom sur Steam.\n" +
        "• Aide post-install : comment régler le Multi Frame Generation\n" +
        "  (x2 à x6, DLSS 4.5) dans l'overlay du mod.\n\n" +

        "─────────────────────────────\n" +
        "v1.2.5 (2026-06)\n" +
        "─────────────────────────────\n" +
        "• Overclocking : nouveau mode « Personnalisé » — fixez vous-même\n" +
        "  l'offset cœur, l'offset mémoire, la limite de puissance et la\n" +
        "  température via des curseurs bornés (intervalles sûrs selon votre\n" +
        "  marque). Appliqué directement sur NVIDIA/AMD dédiées, avec Reset.\n\n" +

        "─────────────────────────────\n" +
        "v1.2 (2026-06)\n" +
        "─────────────────────────────\n" +
        "• DLSS Enabler : le DLL est posé À CÔTÉ DE L'EXÉCUTABLE du jeu, localisé\n" +
        "  automatiquement (sous-dossiers Binaries\\Win64, bin\\x64, Content…).\n" +
        "• DLSS Enabler : vraie méthode Game Pass — test d'écriture, déverrouillage\n" +
        "  automatique du dossier (admin) ou instructions précises (app Xbox).\n" +
        "• Bloqueur de pub : choix du fournisseur DNS (AdGuard, AdGuard Family,\n" +
        "  Cloudflare Security, Quad9).\n" +
        "• RAM : optimisation automatique optionnelle au-delà de 90 % de charge.\n" +
        "• Nettoyage : nouvelles catégories « Cache des shaders » et « Rapports\n" +
        "  d'erreurs Windows ».\n" +
        "• Mode Jeu : ajoutez vos propres applications à suspendre (mémorisées).\n" +
        "• Accueil : conseil de redémarrage après 7 jours sans reboot.\n\n" +

        "─────────────────────────────\n" +
        "v1.1.5 (2026-06)\n" +
        "─────────────────────────────\n" +
        "• DLSS Enabler : le DLL officiel du mod est désormais intégré à CleanSlate\n" +
        "  (plus de téléchargement depuis GitHub). L'installation copie directement\n" +
        "  le bon fichier dans le dossier du jeu, sous le nom de proxy le plus sûr\n" +
        "  (winmm.dll, dbghelp.dll, version.dll ou dxgi.dll selon ce qui est libre),\n" +
        "  ou en plugin ASI si tous ces noms sont déjà pris par un autre mod.\n" +
        "• Détection des jeux installés via le Xbox Game Pass (dossier XboxGames),\n" +
        "  avec badge « 🎮 Game Pass » et avertissement : Windows peut supprimer les\n" +
        "  DLL ajoutées lors d'une vérification d'intégrité ou d'une mise à jour.\n\n" +

        "─────────────────────────────\n" +
        "v1.1 (2026-06)\n" +
        "─────────────────────────────\n" +
        "• DLSS Enabler : bibliothèque visuelle — les jeux détectés s'affichent\n" +
        "  en grille de jaquettes (artwork Steam officiel), avec badge\n" +
        "  « ✓ DLSS Enabler » sur les jeux où le mod est installé, recherche\n" +
        "  instantanée et détection automatique à l'ouverture du Mode Jeu.\n" +
        "  Les dossiers ajoutés à la main sont mémorisés.\n" +
        "• Optimisation RAM : un clic = optimisation immédiate, sans fenêtre\n" +
        "  de confirmation. Résultat affiché directement dans la page.\n\n" +

        "─────────────────────────────\n" +
        "v1.0 (2026-06)\n" +
        "─────────────────────────────\n" +
        "• Nouvel onglet « Accueil » : vue d'ensemble du système (Windows, CPU,\n" +
        "  GPU, RAM, disques, uptime) et « Entretien en 1 clic » — nettoyage des\n" +
        "  catégories sûres + optimisation RAM, avec bilan détaillé.\n" +
        "• Mode Jeu : nouveau sous-onglet « DLSS Enabler » — détecte vos jeux\n" +
        "  (Steam, Epic Games ou dossier manuel) et installe/désinstalle le mod\n" +
        "  open-source DLSS Enabler (Super Resolution + Frame Generation, y\n" +
        "  compris Multi Frame Generation x2/x3/x4, sur tout GPU DirectX 12)\n" +
        "  directement dans le dossier du jeu, comme\n" +
        "  DLSS Enabler Manager. Réservé aux jeux solo (anticheat).\n" +
        "• Vos préférences sont enfin mémorisées : thème sombre/clair et taille\n" +
        "  de fenêtre sont conservés d'une session à l'autre.\n\n" +

        "─────────────────────────────\n" +
        "v0.9.4 (2026-06)\n" +
        "─────────────────────────────\n" +
        "• Overclocking : application AUTOMATIQUE de l'overclock pour les cartes\n" +
        "  AMD Radeon dédiées (fréquences cœur/mémoire + limite de puissance via\n" +
        "  ADL OverdriveN) — boutons « Appliquer » + « Reset », comme pour NVIDIA.\n" +
        "  Intel reste en profil guidé.\n" +
        "• Mises à jour : la notification « Mise à jour disponible » reste affichée\n" +
        "  même après avoir quitté et relancé CleanSlate, tant qu'elle n'est pas\n" +
        "  installée.\n\n" +

        "─────────────────────────────\n" +
        "v0.9.3 (2026-06)\n" +
        "─────────────────────────────\n" +
        "• Bloqueur de pub : remplacement complet par une bascule du DNS système\n" +
        "  vers AdGuard DNS (94.140.14.14 / 94.140.15.15) — instantané, sans\n" +
        "  ralentissement, désactivable en un clic (DNS d'origine restauré).\n" +
        "  L'ancien blocage par fichier hosts est nettoyé automatiquement.\n" +
        "• Mises à jour : l'exécutable est désormais remplacé par la nouvelle\n" +
        "  version puis relancé — le raccourci/épingle reste à jour.\n" +
        "• Overclocking : 3 profils par carte (Sûr / Équilibré / Performance)\n" +
        "• Overclocking : Intel Iris Xe / Arc Graphics intégré reconnus avec\n" +
        "  un vrai profil « GPU Performance Boost »\n" +
        "• Overclocking : les écrans virtuels (Parsec, spacedesk, IDD…) ne\n" +
        "  s'affichent plus comme cartes graphiques\n" +
        "• Overclocking : bouton « MSI Afterburner » retiré (CleanSlate applique\n" +
        "  lui-même l'overclock sur les cartes NVIDIA compatibles)\n" +
        "• Overclocking : nouveau bouton « Vérifier le dernier pilote disponible »\n" +
        "  qui interroge directement NVIDIA (catalogue officiel) pour comparer votre\n" +
        "  pilote installé à la toute dernière version (au-delà de Windows Update,\n" +
        "  souvent en retard) — version, date, taille et téléchargement direct.\n" +
        "  Pour AMD/Intel : lien direct vers l'outil de détection officiel.\n\n" +

        "─────────────────────────────\n" +
        "v0.9.1 (2026-06)\n" +
        "─────────────────────────────\n" +
        "• Overclocking : application AUTOMATIQUE de l'overclock pour les cartes\n" +
        "  NVIDIA (offsets cœur/mémoire via NVAPI) — bouton « Appliquer » + « Reset »\n" +
        "  Les cartes AMD/Intel gardent le profil guidé à appliquer à la main.\n\n" +

        "─────────────────────────────\n" +
        "v0.9 (2026-06)\n" +
        "─────────────────────────────\n" +
        "• Nouveau logo CleanSlate comme icône de l'application\n" +
        "• Nettoyage : l'analyse scanne TOUTES les catégories — chaque ligne\n" +
        "  affiche sa taille réelle (fini les « — » sur corbeille, cache, etc.)\n" +
        "• Corbeille : détection corrigée (S_FALSE Windows 11) + repli via\n" +
        "  les dossiers $Recycle.Bin — la vraie taille s'affiche\n" +
        "• Lancement en administrateur par défaut + vérification automatique\n" +
        "  des mises à jour au démarrage\n" +
        "• Pilotes : interface repensée, centrée sur la mise à jour (on ne\n" +
        "  liste plus les pilotes) — recherche + installation en un clic\n" +
        "• Mode Jeu : nouvel onglet « Overclocking » — détecte votre carte\n" +
        "  graphique et propose le profil idéal (perf / stabilité)\n" +
        "• Optimisation : nouvel onglet « Windows Debloat » — anti-télémétrie,\n" +
        "  confidentialité, suppression du bloatware, au choix avant exécution\n\n" +

        "─────────────────────────────\n" +
        "v0.3 (2026-06)\n" +
        "─────────────────────────────\n" +
        "• Bloqueur de pub système (onglet 🛡️) — bloque ~130 000 domaines\n" +
        "  via le fichier hosts (AdGuard-style, tous navigateurs + apps)\n" +
        "• Cache navigateurs : détection multi-profils (Chrome, Edge, Brave,\n" +
        "  Vivaldi, Opera, Opera GX) — correction de l'affichage « 0 o »\n\n" +

        "─────────────────────────────\n" +
        "v0.2.7 (2026-06)\n" +
        "─────────────────────────────\n" +
        "• Notes de version complètes intégrées dans l'application\n\n" +

        "─────────────────────────────\n" +
        "v0.2.6 (2026-06)\n" +
        "─────────────────────────────\n" +
        "• Fix : le bouton « Analyser » scanne maintenant TOUTES les catégories\n" +
        "  (plus de « — » sur les lignes non cochées)\n" +
        "• Fix : détection de la corbeille via le dossier $Recycle.Bin\\{SID}\n" +
        "  de l'utilisateur — plus fiable sur Windows 11 multi-comptes\n\n" +

        "─────────────────────────────\n" +
        "v0.2.5 (2026-06)\n" +
        "─────────────────────────────\n" +
        "• Fix : corbeille affichait « — » même avec des fichiers présents\n" +
        "  (HRESULT S_FALSE sur certaines configs Windows 11)\n" +
        "• Mode Jeu : liste étendue de 6 à 22 processus suspendus\n" +
        "  (cloud, communication, lanceurs de jeux, Adobe…)\n\n" +

        "─────────────────────────────\n" +
        "v0.2 (2026-06)\n" +
        "─────────────────────────────\n" +
        "• Thème sombre / clair basculable à chaud\n" +
        "• Barre de progression avec % lors de l'analyse\n" +
        "• Vue des lecteurs disponibles avec espace libre\n" +
        "• Optimisation RAM avancée (purge Standby List)\n" +
        "• Mises à jour de pilotes via Windows Update (WUApi)\n" +
        "• Onglet Réparation rapide : diagnostic en 6 points\n" +
        "• Vérificateur de mises à jour intégré\n\n" +

        "─────────────────────────────\n" +
        "v0.1 (2026-06)\n" +
        "─────────────────────────────\n" +
        "• Nettoyage : fichiers temp, cache navigateurs, miniatures,\n" +
        "  corbeille, journaux Windows, prefetch\n" +
        "• Mémoire : surveillance en temps réel + optimisation avancée\n" +
        "• Pilotes : inventaire WMI\n" +
        "• Mode Jeu : suspension des processus non essentiels\n" +
        "• Optimisation : gestionnaire démarrage + nettoyage registre\n" +
        "• Réparation rapide : diagnostic système en 6 points";

    private readonly IUpdateService _updateService;
    private readonly IDialogService _dialogs;
    private readonly IAppSettingsService _settings;
    private NavigationItem _selectedItem = null!;
    private bool _isDark = App.IsDark;
    private string _updateStatus = string.Empty;
    private bool _isCheckingUpdate;

    public MainViewModel(
        DashboardViewModel dashboard,
        CleaningViewModel cleaning,
        MemoryViewModel memory,
        DriversViewModel drivers,
        DiskAnalyzerViewModel diskAnalyzer,
        GameModeViewModel gameMode,
        OptimizationViewModel optimization,
        QuickRepairViewModel quickRepair,
        AdBlockViewModel adBlock,
        IUpdateService updateService,
        IAppSettingsService settings,
        IDialogService dialogs)
    {
        _updateService = updateService;
        _settings = settings;
        _dialogs = dialogs;

        IsAdministrator = ElevationHelper.IsRunningAsAdministrator();

        Items = new ObservableCollection<NavigationItem>
        {
            new("Accueil",          "🏠", dashboard),
            new("Nettoyage",        "🧹", cleaning),
            new("Mémoire",          "📊", memory),
            new("Espace disque",    "💽", diskAnalyzer),
            new("Pilotes",          "🧩", drivers),
            new("Mode Jeu",         "🎮", gameMode),
            new("Optimisation",     "⚙️", optimization),
            new("Réparation rapide","🛠️", quickRepair),
            new("Bloqueur de pub",  "🛡️", adBlock),
        };
        _selectedItem = Items[0];

        RestartAsAdminCommand = new RelayCommand(() =>
        {
            if (!ElevationHelper.RestartAsAdministrator())
                dialogs.Warn("Élévation refusée",
                    "L'application continue sans droits administrateur. " +
                    "Certaines actions système ne seront pas disponibles.");
            else
                System.Windows.Application.Current.Shutdown();
        }, () => !IsAdministrator);

        ToggleThemeCommand = new RelayCommand(ToggleTheme);

        ShowAboutCommand = new RelayCommand(() => dialogs.Info("À propos de CleanSlate",
            $"CleanSlate v{_updateService.CurrentVersion}\n" +
            "Outil open-source d'optimisation et de nettoyage Windows.\n\n" +
            "GitHub : https://github.com/dorianskyfr/CLEANSLATE\n" +
            "Licence : MIT"));

        ShowPatchNotesCommand = new RelayCommand(() =>
            dialogs.Info("Notes de mise à jour", PatchNotes));

        CheckUpdatesCommand = new AsyncRelayCommand(CheckUpdatesAsync,
            () => !IsCheckingUpdate);
    }

    public ObservableCollection<NavigationItem> Items { get; }

    public NavigationItem SelectedItem
    {
        get => _selectedItem;
        set => SetProperty(ref _selectedItem, value);
    }

    public bool IsAdministrator { get; }
    public bool ShowElevationButton => !IsAdministrator;
    public string PrivilegeStatus => IsAdministrator ? "Mode administrateur" : "Mode utilisateur (droits limités)";

    public bool IsDark
    {
        get => _isDark;
        private set
        {
            if (SetProperty(ref _isDark, value))
                OnPropertyChanged(nameof(ThemeIcon));
        }
    }

    public string ThemeIcon => IsDark ? "☀️" : "🌙";

    public string UpdateStatus
    {
        get => _updateStatus;
        private set
        {
            if (SetProperty(ref _updateStatus, value))
                OnPropertyChanged(nameof(HasUpdateStatus));
        }
    }

    public bool HasUpdateStatus => !string.IsNullOrEmpty(_updateStatus);

    public bool IsCheckingUpdate
    {
        get => _isCheckingUpdate;
        private set
        {
            if (SetProperty(ref _isCheckingUpdate, value))
                CheckUpdatesCommand.RaiseCanExecuteChanged();
        }
    }

    public ICommand RestartAsAdminCommand { get; }
    public RelayCommand ToggleThemeCommand { get; }
    public RelayCommand ShowAboutCommand { get; }
    public RelayCommand ShowPatchNotesCommand { get; }
    public AsyncRelayCommand CheckUpdatesCommand { get; }

    private void ToggleTheme()
    {
        bool newDark = !IsDark;
        App.SwitchTheme(newDark);
        IsDark = newDark;

        // Le choix de thème survit au redémarrage (les autres préférences,
        // comme la taille de fenêtre, sont préservées telles quelles).
        _settings.Save(_settings.Load() with { IsDarkTheme = newDark });
    }

    /// <summary>
    /// Vérification automatique au démarrage : discrète. N'affiche un message que si
    /// une mise à jour est disponible ; reste silencieuse en cas d'échec réseau ou si
    /// l'application est déjà à jour (pas de pop-up intrusive au lancement).
    ///
    /// Une mise à jour détectée mais non installée lors d'une session précédente est
    /// rechargée immédiatement (persistée sur disque) : la notification « Mise à jour
    /// disponible » reste donc affichée même après avoir quitté puis relancé
    /// CleanSlate, jusqu'à ce qu'elle soit installée.
    /// </summary>
    public async Task CheckUpdatesOnStartupAsync()
    {
        var pending = _updateService.LoadPendingUpdate();
        if (pending is not null)
            UpdateStatus = PendingUpdateMessage(pending.Version);

        try
        {
            var info = await _updateService.CheckForUpdateAsync(CancellationToken.None);
            if (info is null) return;

            _updateService.SavePendingUpdate(info);
            if (!info.IsNewer)
            {
                if (pending is not null) UpdateStatus = string.Empty;
                return;
            }

            UpdateStatus = PendingUpdateMessage(info.Version);

            var download = _dialogs.Confirm("Mise à jour disponible",
                $"CleanSlate v{info.Version} est disponible (vous avez v{_updateService.CurrentVersion}).\n\n" +
                $"Notes :\n{info.ReleaseNotes}\n\nTélécharger et installer maintenant ?");
            if (!download) return;

            UpdateStatus = "Téléchargement en cours…";
            var progress = new Progress<double>(p => UpdateStatus = $"Téléchargement : {p:0}%…");
            var path = await _updateService.DownloadAsync(info, progress, CancellationToken.None);
            UpdateStatus = "Installation…";
            _updateService.SavePendingUpdate(null);
            _updateService.LaunchInstaller(path);
            System.Windows.Application.Current.Shutdown();
        }
        catch
        {
            // Échec réseau silencieux : la vérification manuelle reste disponible dans le menu.
            // Si une mise à jour était déjà connue, on garde la notification affichée.
            if (pending is null) UpdateStatus = string.Empty;
        }
    }

    private static string PendingUpdateMessage(string version) =>
        $"Mise à jour v{version} disponible — menu CleanSlate ▾ → Vérifier les mises à jour.";

    private async Task CheckUpdatesAsync()
    {
        IsCheckingUpdate = true;
        UpdateStatus = "Vérification des mises à jour…";
        try
        {
            var info = await _updateService.CheckForUpdateAsync(CancellationToken.None);
            if (info is null)
            {
                UpdateStatus = string.Empty;
                _dialogs.Info("Mises à jour", "Impossible de joindre le serveur de mise à jour.");
                return;
            }

            _updateService.SavePendingUpdate(info);

            if (!info.IsNewer)
            {
                UpdateStatus = string.Empty;
                _dialogs.Info("Mises à jour", $"CleanSlate est à jour (v{_updateService.CurrentVersion}).");
                return;
            }

            UpdateStatus = $"Mise à jour v{info.Version} disponible !";
            var download = _dialogs.Confirm("Mise à jour disponible",
                $"CleanSlate v{info.Version} est disponible (vous avez v{_updateService.CurrentVersion}).\n\n" +
                $"Notes :\n{info.ReleaseNotes}\n\nTélécharger et installer maintenant ?");

            if (!download)
            {
                // La notification reste affichée (et persistée) tant que la mise à
                // jour n'est pas installée — y compris après fermeture de l'application.
                UpdateStatus = PendingUpdateMessage(info.Version);
                return;
            }

            UpdateStatus = "Téléchargement en cours…";
            var progress = new Progress<double>(p => UpdateStatus = $"Téléchargement : {p:0}%…");
            var path = await _updateService.DownloadAsync(info, progress, CancellationToken.None);
            UpdateStatus = "Installation…";
            _updateService.SavePendingUpdate(null);
            _updateService.LaunchInstaller(path);
            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            UpdateStatus = string.Empty;
            _dialogs.Warn("Erreur de mise à jour", ex.Message);
        }
        finally
        {
            IsCheckingUpdate = false;
        }
    }
}
