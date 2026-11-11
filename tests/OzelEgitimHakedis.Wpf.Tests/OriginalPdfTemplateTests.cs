using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using OzelEgitimHakedis.Core.Models;
using Xunit;
namespace OzelEgitimHakedis.Wpf.Tests;
public sealed partial class OfficialDocumentOutputTests
{
    [Theory]
    [InlineData(TransportationProgramType.BasicOrSecondary)]
    [InlineData(TransportationProgramType.SpecialEducation)]
    public void OriginalPdfTemplate_ShouldUseReadableTypographyAndRemoveRouteMetadata(TransportationProgramType program)
    {
        RunOnSta(() =>
        {
            var payment=CreatePayment(new ProgressPaymentInput(1,192009.97m,0,7365.35m,2640320,false,0,0,0,ProgramType:program));
            var route="Cinoğlu Köyü Merkez Hat-1-Cinoğlu Köyü Merkez Hat-2-Eldeniz Köyü Merkez Hat-1-Eldeniz Köyü Merkez Hat-2-Hanoğlu Köyü Merkez-Hanoğlu Köyü Pınarcı Mh.-Hanoğlu Köyü Tülüce Mh.-Ketenlik Köyü Merkez-Ketenlik Köyü Hurdacılar Mh.-Hanoğlu Köyü Merkez-Özbeyli Köyü Merkez-Banaz Ulupınar Köyü Fakıdamı Mh.-Merkez Akıncılar Mh. Çiftlikler Bölgesi";
            payment=payment with {PeriodDate=new DateOnly(2026,9,30)};
            var context=CreateDocumentContext(payment) with {RouteName=route,WorkName=route+" Hattı Taşıma İşi",ContractorName="S.S. Yeşil Sivaslı Min.Mot.Taş.Kooperatifi",IsCombined=true,GeneratedDate=new DateOnly(2026,11,4),TenderDate=new DateOnly(2026,8,26),ContractDate=new DateOnly(2026,9,14),ContractStartDate=new DateOnly(2026,9,14),ContractEndDate=new DateOnly(2027,6,25),ContractWorkDays=181};
            var doc=OehsOfficialDocumentFactory.BuildCombined(context);
            var narrative=Assert.Single(doc.Pages[2].Child.Children.OfType<TextBlock>().Where(t=>t.Text.StartsWith("İlçe Milli Eğitim Müdürlüğü",StringComparison.Ordinal)));
            Assert.True(narrative.FontSize>=13, $"Narrative font is {narrative.FontSize}");
            var label=Assert.Single(doc.Pages[1].Child.Children.OfType<TextBlock>().Where(t=>t.Text=="Yapılan Hizmetin Adı"));
            Assert.InRange(FixedPage.GetLeft(label),94,99);
            Assert.True(label.FontSize>=11);
            foreach(var output in OehsOfficialDocumentFactory.BuildSeparate(context with {IsCombined=false}))
                Assert.DoesNotContain("Hat No :",GetPageText(output.Document.Pages[0].Child),StringComparison.Ordinal);
            var root=new DirectoryInfo(AppContext.BaseDirectory);
            while(root.Parent is not null&&!File.Exists(Path.Combine(root.FullName,"TEHYS.sln")))root=root.Parent;
            PdfVectorWriter.Save(doc,Path.Combine(root.FullName,".superpowers","original-template-2026-11-04",program+".pdf"));
        });
    }
}
