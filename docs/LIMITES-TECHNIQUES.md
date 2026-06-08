# Limites techniques — l'honnêteté avant le marketing

Ce document décrit, pour chaque fonctionnalité, **ce qui est réellement faisable**,
**comment**, et **où sont les limites**. C'est un parti pris du projet : ne jamais
promettre de « magie ».

---

## 1. Nettoyage de fichiers temporaires ✅ (réaliste et efficace)

**Ce que ça fait vraiment** : supprime des fichiers réellement inutiles dans des
emplacements bien connus. C'est la fonctionnalité la plus fiable d'un nettoyeur PC.

**Cibles et réalités** :
- `%TEMP%` / `%SystemRoot%\Temp` : fichiers temporaires. Certains sont **verrouillés**
  par des processus actifs → on les ignore proprement (échec attendu, pas une erreur).
- **Caches navigateurs** (Chrome, Edge, Firefox) : efficaces à vider, mais
  **fermer le navigateur** est recommandé sinon nombreux fichiers verrouillés.
  Vider le cache = re-téléchargement ultérieur (léger ralentissement temporaire).
- **Logs Windows** (`%SystemRoot%\Logs`, `CBS.log`…) : nécessitent souvent
  **les droits administrateur** ; certains sont tenus ouverts par des services.
- **Prefetch** (`%SystemRoot%\Prefetch`) : ⚠️ le supprimer **n'accélère pas** le PC.
  Ces fichiers servent justement à accélérer le démarrage des applications ; Windows
  les régénère. À proposer avec un avertissement, pas activé par défaut.
- **Miniatures** (`thumbcache_*.db`) : régénérées automatiquement, gain d'espace réel.
- **Corbeille** : vidée via l'API shell `SHEmptyRecycleBin` (plus correct qu'une
  suppression de fichiers à la main). **Action irréversible** → confirmation forcée.

**Limites** :
- Fichiers verrouillés non supprimables sans redémarrage (on ne tente pas de forcer).
- Le gain d'espace est réel ; le gain de **performance** est généralement marginal.

---

## 2. Détection de pilotes obsolètes 🟡 (le point le plus délicat — soyons clairs)

**Le problème de fond, sans détour** :
> ❗ **Il n'existe AUCUNE API universelle, gratuite et fiable** qui donne « la dernière
> version officielle du pilote X pour le composant Y ». Les utilitaires commerciaux
> qui le prétendent s'appuient sur des **bases de données propriétaires** qu'ils
> constituent et maintiennent eux-mêmes (souvent imparfaites, parfois trompeuses).

**Ce qu'on PEUT faire honnêtement** :
1. **Inventorier** les pilotes installés (nom, version, date, éditeur, périphérique)
   via WMI (`Win32_PnPSignedDriver`) ou l'API SetupAPI / `pnputil /enum-drivers`.
   → **Fiable et complet.**
2. **Déléguer la recherche de mises à jour à Windows Update** via l'agent WUA
   (`Microsoft.Update.Session`, catégorie « Drivers »). C'est la source la plus
   légitime et sûre. → **Réaliste, mais ne couvre pas tous les pilotes** (beaucoup de
   fabricants ne publient pas sur Windows Update, ou avec retard).
3. **Renvoyer l'utilisateur vers les pages officielles constructeurs** (Intel, NVIDIA,
   AMD, Realtek…) en pré-remplissant le modèle de matériel détecté. → Pas de
   comparaison automatique de version, mais **aucune fausse promesse**.

**Ce qu'on s'INTERDIT** :
- Inventer un numéro de « dernière version » sans source vérifiable.
- Télécharger des pilotes depuis des dépôts tiers non officiels (risque sécurité réel :
  les faux « driver updaters » sont un vecteur classique de logiciels indésirables).

**Limite assumée** : CleanSlate présente un **inventaire + état Windows Update +
liens constructeurs**, pas un « tout est à jour / X pilotes obsolètes » magique.

---

## 3. Surveillance et libération de RAM 🟡 (mesure utile, « libération » honnête)

**Surveillance** : parfaitement faisable et utile — usage RAM total/disponible
(`GlobalMemoryStatusEx`), par processus (`Process.WorkingSet64`). Rafraîchissement
temps réel via un timer.

**« Libération » de RAM — la vérité** :
> ⚠️ Sur Windows moderne, forcer la libération de RAM est **généralement inutile,
> voire contre-productif**.
- La technique classique (appeler `EmptyWorkingSet` / `SetProcessWorkingSetSize`)
  ne fait que **forcer la pagination** du working set vers le disque. La mémoire
  semble « libérée » dans le gestionnaire des tâches, mais les pages seront
  **rechargées depuis le disque** dès que l'application en a besoin → **ralentissement**.
- Windows gère déjà très bien la mémoire. La RAM « utilisée » par le cache (Standby)
  est une **bonne chose** : elle accélère les accès.

**Position de CleanSlate** : on affiche une mesure claire, et si l'utilisateur insiste
on propose la libération **avec un avertissement explicite** sur son inefficacité.
On ne la met **pas** en avant comme un « boost ».

---

## 4. Mode Jeu 🟡 (gains variables, sécurité par conception)

**Ce qu'on peut faire** :
- **Suspendre** (et non tuer) des processus non essentiels identifiés (ex. clients de
  mise à jour, indexeurs) via `NtSuspendProcess`, puis les **reprendre** à la sortie.
- Activer le **Mode Jeu Windows** natif et la priorité GPU si disponibles.
- Mettre en pause des **notifications** (Focus Assist) et des **services non critiques**
  choisis dans une **liste sûre**, restaurés automatiquement.

**Garantie de restauration** : l'état initial (services démarrés, processus suspendus,
réglages de notifications) est **capturé avant** activation et **restauré** à la
fermeture du mode — y compris si l'app se ferme (restauration au prochain démarrage
via un fichier d'état persistant).

**Limites honnêtes** :
- Les gains FPS sont **très variables** : significatifs sur machine modeste chargée,
  quasi nuls sur machine puissante peu chargée.
- Suspendre le mauvais processus peut casser des fonctionnalités (overlay, audio…) →
  **liste blanche conservatrice**, jamais de processus système critiques.

---

## 5. Optimisation système 🟡

### 5a. Programmes au démarrage ✅ (utile et sûr)
Lister/activer/désactiver les entrées de démarrage (clés `Run` HKCU/HKLM, dossier
*Startup*, tâches planifiées au logon). **Désactiver** plutôt que supprimer (réversible).
Gain réel sur le **temps de démarrage**. C'est l'optimisation la plus rentable.

### 5b. Nettoyage du registre ⚠️ (risque réel, bénéfice quasi nul)
> ❗ **À dire franchement** : nettoyer le registre n'apporte **pratiquement aucun gain
> de performance** sur un Windows moderne. Le registre fait des centaines de Mo ;
> supprimer quelques clés orphelines est imperceptible.
- Le **risque** (supprimer une clé encore utilisée) est, lui, bien réel.
- Donc CleanSlate impose une **sauvegarde obligatoire** (export `.reg` via `IBackupService`)
  **avant toute** modification, avec restauration en un clic. Ciblage **conservateur**
  (entrées manifestement orphelines uniquement).

**Position** : fonctionnalité fournie car attendue, mais **présentée pour ce qu'elle
est** — cosmétique et risquée, jamais vendue comme un accélérateur.

---

## Récapitulatif

| Fonctionnalité | Gain réel | Risque | Verdict honnête |
|---|---|---|---|
| Nettoyage fichiers temporaires | Espace 👍 / Perf 😐 | Faible | **Recommandé** |
| Programmes au démarrage | Démarrage 👍 | Faible (réversible) | **Recommandé** |
| Inventaire pilotes | Information 👍 | Nul | Utile (pas de magie) |
| Surveillance RAM | Information 👍 | Nul | Utile |
| Libération RAM | ≈ Nul / négatif | Faible | Déconseillé (mais dispo + averti) |
| Mode Jeu | Variable | Moyen | Conditionnel, restauration garantie |
| Nettoyage registre | ≈ Nul | Moyen | Cosmétique, sauvegarde obligatoire |
