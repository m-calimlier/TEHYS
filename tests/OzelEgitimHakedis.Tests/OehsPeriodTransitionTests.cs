using System.Text.Json;
using OzelEgitimHakedis.Core.Models;
using OzelEgitimHakedis.Core.Services;
using Xunit;

namespace OzelEgitimHakedis.Tests;

[CLSCompliant(false)]
public sealed class OehsPeriodTransitionTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("oehs-period-test-").FullName;
    private string Data => Path.Combine(_root, "data");
    private string Outputs => Path.Combine(_root, "outputs");
    private string Archive => Path.Combine(_root, "2025-2026.oehs-backup.zip");

    private void Seed()
    {
        var sample = PaymentOperationsTests.Data(PaymentOperationsTests.Payment);
        var basic = TransportationProgramType.BasicOrSecondary;
        var payment = sample.ProgressPayments![0];
        var input = payment.Input with { ProgramType = basic };
        new OehsProgramScopedApplicationDataStore(Data).Save(sample with
        {
            Routes = sample.Routes.Concat(sample.Routes.Select(r => r with { ProgramType = basic })).ToArray(),
            Contractors = sample.Contractors.Concat(sample.Contractors.Select(c => c with { ProgramType = basic })).ToArray(),
            Contracts = sample.Contracts!.Concat(sample.Contracts!.Select(c => c with { ProgramType = basic })).ToArray(),
            ProgressPayments = new[] { payment, payment with { Id = Guid.NewGuid(), Input = input, Calculation = ProgressPaymentCalculator.Calculate(input) } },
            HybridPaymentDrafts = new[] { new HybridPaymentDraftData("draft", new DateOnly(2026, 1, 1), payment.Input) },
            CalculationConstants = ProgressPaymentCalculationConstants.Defaults with { StampTaxRate = 0.02m }
        });
        File.WriteAllText(Path.Combine(Data, "official-document-settings.json"), "{\"preparerName\":\"Test hazırlayan\"}");
        File.WriteAllText(Path.Combine(Data, "window-layout.json"), "{}");
        Directory.CreateDirectory(Outputs);
        File.WriteAllText(Path.Combine(Outputs, "eski.pdf"), "%PDF old output");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Transition_ArchivesBothProgramsThenStartsEmptyAndCanRestore(bool preserveSettings)
    {
        Seed();
        var oldData = OutputEvidenceStore.FileHash(Path.Combine(Data, "oehs-special-education.json"));
        new OehsPeriodTransitionService(Data, Outputs).Start("2025-2026", "2026-2027", Archive, preserveSettings);
        var summary = OehsBackupService.Inspect(Archive);
        Assert.Equal(2, summary.RouteCount);
        Assert.Equal(2, summary.PaymentCount);
        Assert.Equal(1, summary.PdfCount);
        var empty = new OehsProgramScopedApplicationDataStore(Data).Load();
        Assert.Empty(empty.Routes); Assert.Empty(empty.Contractors); Assert.Empty(empty.Contracts!);
        Assert.Empty(empty.ProgressPayments!); Assert.Empty(empty.HybridPaymentDrafts!);
        Assert.True(empty.CalculationConstants is null || empty.CalculationConstants == ProgressPaymentCalculationConstants.Defaults);
        Assert.Empty(Directory.GetFiles(Outputs, "*", SearchOption.AllDirectories));
        Assert.Equal(preserveSettings, File.Exists(Path.Combine(Data, "official-document-settings.json")));
        Assert.Equal(preserveSettings, File.Exists(Path.Combine(Data, "window-layout.json")));
        Assert.Equal(OehsFreshStartService.CurrentResetVersion, File.ReadAllText(Path.Combine(Data, ".fresh-start-version")));
        Assert.False(OehsFreshStartService.EnsureFreshStart(Data, Outputs));
        Assert.Equal("2026-2027", OehsPeriodTransitionService.ReadCurrentPeriod(Data)?.CurrentPeriod);
        // Reopening does not reset newly saved work.
        new OehsProgramScopedApplicationDataStore(Data).Save(PaymentOperationsTests.Data());
        Assert.False(OehsFreshStartService.EnsureFreshStart(Data, Outputs));
        Assert.Single(new OehsProgramScopedApplicationDataStore(Data).Load().Routes);
        new OehsBackupService(Data, Outputs).Restore(Archive);
        Assert.Equal(oldData, OutputEvidenceStore.FileHash(Path.Combine(Data, "oehs-special-education.json")));
        Assert.Equal("%PDF old output", File.ReadAllText(Path.Combine(Outputs, "eski.pdf")));
        Assert.Single(new OehsProgramScopedApplicationDataStore(Data).Load().HybridPaymentDrafts!);
    }

    [Theory]
    [InlineData("2025/2026", "2026-2027")]
    [InlineData("2025-2027", "2027-2028")]
    [InlineData("2025-2026", "2027-2028")]
    public void InvalidPeriod_DoesNotTouchLiveData(string oldPeriod, string newPeriod)
    {
        Seed();
        Assert.Throws<ArgumentException>(() => new OehsPeriodTransitionService(Data, Outputs).Start(oldPeriod, newPeriod, Archive, true));
        Assert.False(File.Exists(Archive));
        Assert.Equal(2, new OehsProgramScopedApplicationDataStore(Data).Load().Routes.Count);
    }

    [Fact]
    public void ExistingArchive_IsNotOverwrittenAndDataIsNotReset()
    {
        Seed(); File.WriteAllText(Archive, "keep");
        Assert.Throws<IOException>(() => new OehsPeriodTransitionService(Data, Outputs).Start("2025-2026", "2026-2027", Archive, true));
        Assert.Equal("keep", File.ReadAllText(Archive));
        Assert.Equal(2, new OehsProgramScopedApplicationDataStore(Data).Load().Routes.Count);
    }

    [Fact]
    public void InvalidLiveSettings_StopBeforeReset()
    {
        Seed(); File.WriteAllText(Path.Combine(Data, "official-document-settings.json"), "bad json");
        Assert.Throws<InvalidDataException>(() => new OehsPeriodTransitionService(Data, Outputs).Start("2025-2026", "2026-2027", Archive, true));
        Assert.False(File.Exists(Archive));
        Assert.Equal(2, new OehsProgramScopedApplicationDataStore(Data).Load().Routes.Count);
    }

    [Fact]
    public void FailureReplacingOutputs_RestoresLiveDataAndKeepsVerifiedArchive()
    {
        Seed(); var before = OutputEvidenceStore.FileHash(Path.Combine(Data, "oehs-special-education.json"));
        Assert.Throws<IOException>(() => new OehsPeriodTransitionService(Data, Outputs).Start("2025-2026", "2026-2027", Archive, true,
            key => { if (key == "outputs") throw new IOException("injected"); }));
        Assert.Equal(before, OutputEvidenceStore.FileHash(Path.Combine(Data, "oehs-special-education.json")));
        Assert.Equal(2, OehsBackupService.Inspect(Archive).PaymentCount);
        Assert.True(File.Exists(Path.Combine(Outputs, "eski.pdf")));
    }

    [Fact]
    public void ArchiveInsideOutputs_IsRejectedBeforeTouchingRecords()
    {
        Seed();
        Assert.Throws<IOException>(() => new OehsPeriodTransitionService(Data, Outputs)
            .Start("2025-2026", "2026-2027", Path.Combine(Outputs, "archive.zip"), true));
        Assert.Equal(2, new OehsProgramScopedApplicationDataStore(Data).Load().Routes.Count);
        Assert.Single(Directory.GetFiles(Outputs));
    }

    [Fact]
    public void ActivePeriod_MustMatchAndIsIncludedInFutureBackups()
    {
        Seed();
        var service = new OehsPeriodTransitionService(Data, Outputs);
        service.Start("2025-2026", "2026-2027", Archive, true);
        Assert.Throws<InvalidOperationException>(() => service.Start("2025-2026", "2026-2027", Path.Combine(_root, "wrong.zip"), true));
        var nextArchive = Path.Combine(_root, "2026-2027.zip");
        service.Start("2026-2027", "2027-2028", nextArchive, true);
        Assert.Equal("2027-2028", OehsPeriodTransitionService.ReadCurrentPeriod(Data)?.CurrentPeriod);
        new OehsBackupService(Data, Outputs).Restore(nextArchive);
        Assert.Equal("2026-2027", OehsPeriodTransitionService.ReadCurrentPeriod(Data)?.CurrentPeriod);
    }

    public void Dispose() => Directory.Delete(_root, true);
}
