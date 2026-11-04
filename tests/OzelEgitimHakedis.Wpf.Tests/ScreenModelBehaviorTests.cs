using OzelEgitimHakedis.Core.Models;
using OzelEgitimHakedis.Wpf.ScreenModels;
using Xunit;

namespace OzelEgitimHakedis.Wpf.Tests;

public sealed partial class OfficialDocumentOutputTests
{
    [Fact]
    public void ContractScreenModel_ShouldDeduplicateNamesAndPreserveLegacyFallback()
    {
        var contract = ArchiveProgressEditorContextTests.CreateArchive().Contract;
        Assert.Equal(contract.WorkName, ContractScreenModel.WorkName(contract, Array.Empty<string>()));
        Assert.Equal("Merkez Hattı Öğrenci Taşıma İşi", ContractScreenModel.WorkName(contract, new[] { "Merkez", "Merkez", " " }));
    }

    [Fact]
    public void PaymentScreenModel_ShouldNotSelectAnotherProgramOrContractor()
    {
        var archive = ArchiveProgressEditorContextTests.CreateArchive();
        var selected = archive.ProgressPayment with { TenderNumber = archive.Contract.TenderNumber, ContractNumber = archive.Contract.ContractNumber, ContractorTaxOrIdentityNumber = archive.Contractor.TaxOrIdentityNumber };
        var route = archive.Route with { ContractorTaxOrIdentityNumber = archive.Contractor.TaxOrIdentityNumber };
        var other = selected with { Id = Guid.NewGuid(), CreatedAt = selected.CreatedAt.AddDays(1), ContractorTaxOrIdentityNumber = "11111111111" };
        var otherProgram = selected with { Id = Guid.NewGuid(), CreatedAt = selected.CreatedAt.AddDays(2), Input = selected.Input with { ProgramType = TransportationProgramType.BasicOrSecondary } };
        Assert.Equal(selected, PaymentScreenModel.LatestForRoute(new[] { selected, other, otherProgram }, archive.Contract, route));
    }

    [Fact]
    public void OutputScreenModel_ShouldKeepBatchesSeparateAndSumSelectedRoutes()
    {
        var original = ArchiveProgressEditorContextTests.CreateArchive().ProgressPayment with { TenderNumber = "2026/1", ContractNumber = "SOZ-1", ContractorTaxOrIdentityNumber = "12345678901" };
        var first = original with { Id = Guid.NewGuid(), BatchId = Guid.NewGuid(), RouteNumber = "1" };
        var second = first with { Id = Guid.NewGuid(), RouteNumber = "5" };
        var separate = first with { Id = Guid.NewGuid(), BatchId = Guid.NewGuid(), RouteNumber = "9" };
        var rows = OutputScreenModel.Records(new[] { first, second, separate });
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, row => row.Net == first.Calculation.NetPayable + second.Calculation.NetPayable);
        Assert.Equal(2, OutputScreenModel.BatchMembers(new[] { first, second, separate }, first).Length);
    }
}
