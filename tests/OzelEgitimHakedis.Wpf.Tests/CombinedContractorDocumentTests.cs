using System.IO;
using System.Windows;
using OzelEgitimHakedis.Core.Models;
using OzelEgitimHakedis.Core.Services;
using Xunit;

namespace OzelEgitimHakedis.Wpf.Tests;

public sealed partial class HakedisCreateHybridUiContractTests
{
    [Theory]
    [InlineData(TransportationProgramType.SpecialEducation)]
    [InlineData(TransportationProgramType.BasicOrSecondary)]
    public void ContractorDocuments_ShouldCombineOnlyCalculatedBatchRoutes(TransportationProgramType program)
    {
        RunOnSta(() =>
        {
            var window = CreateEditorHarness();
            var archive = ArchiveProgressEditorContextTests.CreateArchive();
            Collection(window,"_contracts").Add(archive.Contract with {ProgramType=program});
            var batch = Guid.NewGuid();
            var names = new[] {"Cinoğlu Köyü Merkez Hat-1", "Cinoğlu Köyü Merkez Hat-2", "Eldeniz Köyü Merkez Hat-1", "Eldeniz Köyü Merkez Hat-2",
                "Hanoğlu Köyü Merkez", "Hanoğlu Köyü Pınarcı Mh.", "Hanoğlu Köyü Tülüce Mh.", "Ketenlik Köyü Merkez-Ketenlik Köyü Hurdacılar Mh.",
                "Hanoğlu Köyü Merkez-Özbeyli Köyü Merkez-Banaz Ulupınar Köyü Fakıdamı Mh.", "Merkez Akıncılar Mh. Çiftlikler Bölgesi"};
            var payments = names.Select((name,i) =>
            {
                var input = archive.ProgressPayment.Input with {ProgramType=program,WorkDays=18,DailyPrice=1000+i*100,
                    PriceDifference=100+i,GuideWorkDays=18,GuideDailyPrice=100,GuidePriceDifference=30,
                    TransportPeriods=null,GuidePeriods=null,ContractAmount=100000};
                return archive.ProgressPayment with {Id=Guid.NewGuid(),BatchId=batch,PeriodDate=new DateOnly(2026,10,1),
                    CreatedAt=new DateTime(2026,10,31,12,0,0,DateTimeKind.Utc),RouteNumber=(i+1).ToString("00",System.Globalization.CultureInfo.InvariantCulture),RouteName=name,ProgressPaymentNumber=i+1,
                    BatchRouteNumbers=string.Join("+",Enumerable.Range(1,names.Length)),
                    TenderNumber="IHALE-01",ContractNumber="SOZ-01",ContractorTaxOrIdentityNumber="12345678901",
                    Input=input,Calculation=ProgressPaymentCalculator.Calculate(input)};
            }).ToArray();
            foreach(var payment in payments)
            {
                var route=CreateRoute(payment.RouteNumber!,payment.RouteName!,"12345678901",program);
                Collection(window,"_routes").Add(route);
                Collection(window,"_progressHistory").Add(payment);
            }
            var unrelated=payments[0] with {Id=Guid.NewGuid(),RouteNumber="99",RouteName="Seçilmemiş hat",BatchId=Guid.NewGuid()};
            Collection(window,"_progressHistory").Add(unrelated);
            Collection(window,"_progressHistory").Add(payments[0] with {Id=Guid.NewGuid(),RouteNumber="98",ContractorTaxOrIdentityNumber="99999999999"});
            Collection(window,"_progressHistory").Add(payments[0] with {Id=Guid.NewGuid(),RouteNumber="97",TenderNumber="OTHER"});
            Collection(window,"_progressHistory").Add(payments[0] with {Id=Guid.NewGuid(),RouteNumber="96",ContractNumber="OTHER"});
            Collection(window,"_progressHistory").Add(payments[0] with {Id=Guid.NewGuid(),RouteNumber="95",PeriodDate=new DateOnly(2026,11,1)});
            Collection(window,"_progressHistory").Add(payments[0] with {Id=Guid.NewGuid(),RouteNumber="94",Input=payments[0].Input with {ProgramType=program==TransportationProgramType.SpecialEducation?TransportationProgramType.BasicOrSecondary:TransportationProgramType.SpecialEducation}});
            var context=Assert.IsType<OehsOfficialDocumentContext>(Invoke(window,"BuildOfficialDocumentContextForPayment",payments[1]));
            Assert.Equal(payments.Sum(p=>p.Calculation.NetPayable),context.Calculation.NetPayable);
            Assert.Equal(payments.Sum(p=>p.Calculation.TotalDeductions),context.Calculation.TotalDeductions);
            Assert.Equal(payments.Sum(ProgressPaymentReportService.CalculateWorkbookMonthlyPaymentAmount),context.ReportTotals.CumulativeServiceAmount);
            Assert.Equal(names.Length,context.Payments.Count);
            Assert.Equal(100000m*names.Length,context.ContractAmount);
            foreach(var property in typeof(ProgressPaymentCalculation).GetProperties().Where(p=>p.PropertyType==typeof(decimal)))
                Assert.Equal(payments.Sum(p=>(decimal)property.GetValue(p.Calculation)!),(decimal)property.GetValue(context.Calculation)!);
            Assert.Single(payments.Select(p=>OehsOutputPathService.BuildOfficialPdfDirectory(Path.GetTempPath(),p)).Distinct());
            foreach(var member in payments)
            {
                var same=Assert.IsType<OehsOfficialDocumentContext>(Invoke(window,"BuildOfficialDocumentContextForPayment",member));
                Assert.Equal(System.Text.Json.JsonSerializer.Serialize(context),System.Text.Json.JsonSerializer.Serialize(same));
            }
            foreach(var member in payments)
            {
                Collection(window,"_progressHistory").Add(member with {Id=Guid.NewGuid(),BatchId=Guid.NewGuid(),PeriodDate=new DateOnly(2026,9,1),CreatedAt=new DateTime(2026,9,30)});
                Collection(window,"_progressHistory").Add(member with {Id=Guid.NewGuid(),BatchId=Guid.NewGuid(),PeriodDate=new DateOnly(2026,11,1),CreatedAt=new DateTime(2026,11,30)});
            }
            context=Assert.IsType<OehsOfficialDocumentContext>(Invoke(window,"BuildOfficialDocumentContextForPayment",payments[1]));
            Assert.Equal(payments.Sum(ProgressPaymentReportService.CalculateWorkbookMonthlyPaymentTotal),context.ReportTotals.PreviousTotal);
            Assert.Equal(context.ReportTotals.PreviousTotal+payments.Sum(ProgressPaymentReportService.CalculateWorkbookMonthlyPaymentTotal),context.ReportTotals.CumulativeTotal);
            var document=OehsOfficialDocumentFactory.BuildCombined(context);
            Assert.Equal(4,document.Pages.Count);
            var repo=new DirectoryInfo(AppContext.BaseDirectory);
            while(repo.Parent is not null && !File.Exists(Path.Combine(repo.FullName,"TEHYS.sln")))repo=repo.Parent;
            var directory=Path.Combine(repo.FullName,".superpowers","combined-contractor-2026-11-02");Directory.CreateDirectory(directory);
            var path=Path.Combine(directory,$"{program}.pdf");PdfVectorWriter.Save(document,path);
            using var pdf=UglyToad.PdfPig.PdfDocument.Open(path);
            foreach(var page in pdf.GetPages())
            {
                var text=string.Concat(page.Letters.Select(l=>l.Value));
                foreach(var name in names)Assert.Contains(name.Replace(" ","",StringComparison.Ordinal),text.Replace(" ","",StringComparison.Ordinal),StringComparison.Ordinal);
                Assert.DoesNotContain("Seçilmemiş",text,StringComparison.Ordinal);
                Assert.Contains("HattıTaşımaİşi",text.Replace(" ","",StringComparison.Ordinal),StringComparison.Ordinal);
            }
            Assert.DoesNotContain("HatNo",pdf.GetPage(1).Text.Replace(" ","",StringComparison.Ordinal),StringComparison.Ordinal);
            Assert.DoesNotContain("Kasım",pdf.GetPage(4).Text,StringComparison.Ordinal);
        });
    }
}
