# Architecture de CleanSlate

## Vue en couches

```
┌─────────────────────────────────────────────────────────────┐
│  CleanSlate.App  (WPF, MVVM)                                  │
│  ─────────────────────────────────────────────────────────   │
│  Views (XAML)  ──bind──►  ViewModels  ──appellent──►  Core    │
│  CleaningView             CleaningViewModel                   │
│  MainWindow               MainViewModel                       │
│                                                               │
│  Aucune logique métier ici : uniquement présentation,         │
│  navigation, et orchestration des confirmations utilisateur.  │
└───────────────────────────────┬───────────────────────────────┘
                                 │ dépend de
                                 ▼
┌─────────────────────────────────────────────────────────────┐
│  CleanSlate.Core  (bibliothèque, sans dépendance UI)          │
│  ─────────────────────────────────────────────────────────   │
│  Abstractions/  ICleaningProvider, IBackupService,            │
│                 IActionLogger, IDriverInventory…              │
│  Models/        CleanableItem, ScanResult, CleanResult,       │
│                 CleaningCategory, CleaningSeverity            │
│  Cleaning/      CleaningEngine + providers (Module 1)         │
│  Modules/       Interfaces des modules 2–5                    │
│  Native/        P/Invoke (shell32, psapi…)                    │
│  Diagnostics/   FileActionLogger                              │
└───────────────────────────────┬───────────────────────────────┘
                                 │ utilise
                                 ▼
        Windows : système de fichiers, API shell, WMI, registre
```

**Règle de dépendance** : `App → Core`. Le `Core` ne référence jamais WPF.
Cela permet de tester la logique sans UI et, à terme, de réutiliser le `Core`
dans une éventuelle version console ou un service.

## Flux de données — Module 1 (nettoyage)

```
Utilisateur clique « Analyser »
        │
        ▼
CleaningViewModel.ScanAsync()
        │  appelle
        ▼
CleaningEngine.ScanAsync(catégories sélectionnées, IProgress, CancellationToken)
        │  pour chaque provider activé
        ▼
ICleaningProvider.ScanAsync()  ──►  ScanResult { items[], tailleTotale, erreurs[] }
        │
        ▼
La VM agrège les résultats ► l'UI affiche l'APERÇU (taille, nb d'éléments)
        │
        │   *** AUCUNE suppression à ce stade ***
        ▼
Utilisateur coche/décoche, puis clique « Nettoyer »
        │
        ▼
Boîte de confirmation (nb d'éléments + taille)  ── Annuler ──► retour
        │ Confirmer
        ▼
CleaningEngine.CleanAsync(items confirmés)
        │  pour chaque provider
        ▼
ICleaningProvider.CleanAsync()  ──►  CleanResult { octetsLibérés, supprimés, échecs }
        │  + journalisation via IActionLogger
        ▼
L'UI affiche le bilan
```

## Abstractions clés

### `ICleaningProvider`
Contrat de tout module de nettoyage basé sur des éléments listables.
```csharp
Task<ScanResult> ScanAsync(IProgress<ScanProgress>? progress, CancellationToken ct);
Task<CleanResult> CleanAsync(IReadOnlyCollection<CleanableItem> items,
                             IProgress<CleanProgress>? progress, CancellationToken ct);
```
Le scan et le nettoyage sont **séparés** : c'est ce qui garantit l'aperçu avant
suppression exigé par le cahier des charges.

### `FileCleaningProviderBase`
Classe de base mutualisant :
- l'expansion des variables d'environnement (`%temp%`, `%LOCALAPPDATA%`…),
- l'énumération robuste (gestion des accès refusés, fichiers verrouillés),
- la **liste blanche de sécurité** (`IsPathSafeToDelete`) qui refuse tout chemin
  hors des racines déclarées et tout chemin « dangereux » (racine de disque,
  `Windows\System32`, dossier profil complet…).

### `IBackupService`
Pour les actions **réversibles obligatoires** (registre). Toute écriture de
registre passe par : `CreateBackupAsync()` → action → possibilité de `RestoreAsync()`.

## Extensibilité

Ajouter une catégorie de nettoyage = créer une classe qui hérite de
`FileCleaningProviderBase` et déclare ses `CleaningTarget`. L'`CleaningEngine`
la découvre via injection (liste de providers). Aucun changement d'UI nécessaire
au-delà de l'exposition de la nouvelle catégorie.

Les modules 2 à 5 suivent le même principe : une interface dédiée dans
`Modules/`, une implémentation testable, une VM/vue côté `App`.
