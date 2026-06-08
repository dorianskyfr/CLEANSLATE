using CleanSlate.Core.Abstractions;
using CleanSlate.Core.Cleaning;
using CleanSlate.Core.Models;

namespace CleanSlate.Core.Tests;

/// <summary>Logger factice (no-op) pour les tests.</summary>
internal sealed class NullLogger : IActionLogger
{
    public void Info(string message) { }
    public void Warning(string message) { }
    public void Error(string message, Exception? ex = null) { }
    public void LogCleaning(string providerId, int deleted, int failed, long freedBytes) { }
}

/// <summary>
/// Provider de test pointant sur un dossier arbitraire, et exposant la vérification
/// de sécurité <see cref="FileCleaningProviderBase.IsPathSafeToDelete"/> (protégée).
/// </summary>
internal sealed class TestableFileProvider : FileCleaningProviderBase
{
    private readonly string _root;

    public TestableFileProvider(IActionLogger logger, string root) : base(logger)
        => _root = root;

    public override string Id => "test";
    public override string DisplayName => "Test";
    public override CleaningCategory Category => CleaningCategory.FichiersTemporaires;
    public override CleaningSeverity Severity => CleaningSeverity.Sur;
    public override string Description => "Provider de test.";

    protected override IReadOnlyList<CleaningTarget> Targets =>
        new[] { new CleaningTarget(_root, CleaningCategory.FichiersTemporaires) };

    /// <summary>Expose la vérification de sécurité pour les tests.</summary>
    public static bool CheckSafe(string path, string? expectedRoot) =>
        IsPathSafeToDelete(path, expectedRoot);
}
