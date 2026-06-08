using CleanSlate.Core.Abstractions;
using CleanSlate.Core.Models;

namespace CleanSlate.Core.Cleaning;

/// <summary>
/// Nettoyage des fichiers temporaires utilisateur et système.
/// C'est la cible la plus sûre et la plus utile d'un nettoyeur PC.
/// </summary>
public sealed class TempFilesProvider : FileCleaningProviderBase
{
    public TempFilesProvider(IActionLogger logger) : base(logger) { }

    public override string Id => "temp-files";
    public override string DisplayName => "Fichiers temporaires";
    public override CleaningCategory Category => CleaningCategory.FichiersTemporaires;
    public override CleaningSeverity Severity => CleaningSeverity.Sur;

    public override string Description =>
        "Supprime les fichiers temporaires de votre session et du système. " +
        "Certains fichiers en cours d'utilisation sont verrouillés et seront ignorés " +
        "(c'est normal). Gain d'espace réel, sans risque.";

    // Windows\Temp nécessite généralement les droits administrateur.
    public override bool RequiresAdministrator => false;

    protected override IReadOnlyList<CleaningTarget> Targets { get; } = new[]
    {
        // Dossier temporaire de l'utilisateur courant (%TEMP%).
        new CleaningTarget("%TEMP%", CleaningCategory.FichiersTemporaires),

        // Dossier temporaire système (souvent C:\Windows\Temp) — droits admin utiles.
        new CleaningTarget(@"%SystemRoot%\Temp", CleaningCategory.FichiersTemporaires),
    };
}
