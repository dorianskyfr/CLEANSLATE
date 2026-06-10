using CleanSlate.Core.Cleaning;
using CleanSlate.Core.Modules;
using Xunit;

namespace CleanSlate.Core.Tests;

/// <summary>
/// Tests de l'entretien en 1 clic : seules les catégories sûres sont nettoyées,
/// et le bilan reflète ce qui a réellement été fait.
/// </summary>
public class MaintenanceServiceTests
{
    private sealed class FakeMemoryMonitor : IMemoryMonitor
    {
        public bool OptimizeCalled { get; private set; }

        public MemorySnapshot Read() => new(16UL * 1024 * 1024 * 1024, 8UL * 1024 * 1024 * 1024, 50);

        public MemoryOptimizationResult OptimizeMemory(bool clearStandbyList)
        {
            OptimizeCalled = true;
            return new MemoryOptimizationResult(3, 1024, clearStandbyList, "OK (test)");
        }
    }

    [Fact]
    public async Task Run_NettoieLesFichiersSursEtOptimiseLaRam()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cleanslate-maintenance-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "a.tmp"), new string('x', 2048));
            File.WriteAllText(Path.Combine(root, "b.tmp"), new string('y', 1024));

            var logger = new NullLogger();
            var engine = new CleaningEngine(new[] { new TestableFileProvider(logger, root) }, logger);
            var memory = new FakeMemoryMonitor();
            var svc = new MaintenanceService(engine, memory);

            var report = await svc.RunAsync(null, CancellationToken.None);

            Assert.True(report.FreedBytes > 0);
            Assert.Equal(2, report.DeletedCount);
            Assert.Equal(0, report.FailedCount);
            Assert.True(memory.OptimizeCalled);
            Assert.False(File.Exists(Path.Combine(root, "a.tmp")));
            Assert.False(File.Exists(Path.Combine(root, "b.tmp")));
            Assert.Contains(report.Steps, s => s.Label == "Mémoire");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Run_RienATrouver_RapporteZeroSansEchec()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cleanslate-maintenance-vide-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var logger = new NullLogger();
            var engine = new CleaningEngine(new[] { new TestableFileProvider(logger, root) }, logger);
            var svc = new MaintenanceService(engine, new FakeMemoryMonitor());

            var report = await svc.RunAsync(null, CancellationToken.None);

            Assert.Equal(0, report.FreedBytes);
            Assert.Equal(0, report.DeletedCount);
            Assert.NotEmpty(report.Steps);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
