using CleanSlate.Core.Modules;
using Xunit;

namespace CleanSlate.Core.Tests;

/// <summary>
/// Tests de la persistance de la notification de mise à jour : une mise à jour
/// détectée mais pas installée doit rester signalée même après fermeture/relance
/// de l'application (jusqu'à installation effective).
/// </summary>
public class UpdateServiceTests
{
    private static GitHubUpdateService CreateService(out string stateFile)
    {
        stateFile = Path.Combine(Path.GetTempPath(), $"cleanslate-update-test-{Guid.NewGuid():N}.json");
        return new GitHubUpdateService(stateFile);
    }

    [Fact]
    public void LoadPendingUpdate_SansFichier_RenvoieNull()
    {
        var svc = CreateService(out var stateFile);
        try
        {
            Assert.Null(svc.LoadPendingUpdate());
        }
        finally { File.Delete(stateFile); }
    }

    [Fact]
    public void SaveThenLoad_MiseAJourPlusRecente_EstPersistee()
    {
        var svc = CreateService(out var stateFile);
        try
        {
            var info = new UpdateInfo("99.0.0", "https://example.com/CleanSlate.exe", "Notes", IsNewer: true);
            svc.SavePendingUpdate(info);

            var pending = svc.LoadPendingUpdate();

            Assert.NotNull(pending);
            Assert.Equal("99.0.0", pending!.Version);
            Assert.Equal(info.DownloadUrl, pending.DownloadUrl);
        }
        finally { File.Delete(stateFile); }
    }

    [Fact]
    public void SavePendingUpdate_NonPlusRecente_NEcritRien()
    {
        var svc = CreateService(out var stateFile);
        try
        {
            var info = new UpdateInfo(svc.CurrentVersion, "https://example.com/CleanSlate.exe", "Notes", IsNewer: false);
            svc.SavePendingUpdate(info);

            Assert.Null(svc.LoadPendingUpdate());
            Assert.False(File.Exists(stateFile));
        }
        finally { File.Delete(stateFile); }
    }

    [Fact]
    public void SavePendingUpdate_Null_EfaceUneMiseAJourEnAttente()
    {
        var svc = CreateService(out var stateFile);
        try
        {
            svc.SavePendingUpdate(new UpdateInfo("99.0.0", "https://example.com/CleanSlate.exe", "Notes", IsNewer: true));
            Assert.NotNull(svc.LoadPendingUpdate());

            svc.SavePendingUpdate(null);

            Assert.Null(svc.LoadPendingUpdate());
            Assert.False(File.Exists(stateFile));
        }
        finally { File.Delete(stateFile); }
    }

    [Fact]
    public void LoadPendingUpdate_VersionPersisteeDejaInstallee_EstNettoyee()
    {
        var svc = CreateService(out var stateFile);
        try
        {
            // Une version persistée qui n'est plus plus récente que CurrentVersion
            // signifie que la mise à jour a déjà été appliquée entre-temps.
            var stale = new UpdateInfo(svc.CurrentVersion, "https://example.com/CleanSlate.exe", "Notes", IsNewer: true);
            File.WriteAllText(stateFile,
                System.Text.Json.JsonSerializer.Serialize(
                    new PendingUpdateState(stale.Version, stale.DownloadUrl, stale.ReleaseNotes)));

            Assert.Null(svc.LoadPendingUpdate());
            Assert.False(File.Exists(stateFile));
        }
        finally { if (File.Exists(stateFile)) File.Delete(stateFile); }
    }
}
