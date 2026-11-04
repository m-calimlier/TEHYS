using System.Windows;
using System.Windows.Controls;
using System.IO;
using System.Windows.Documents;
using OzelEgitimHakedis.Core.Models;
using OzelEgitimHakedis.Core.Services;
using Xunit;

namespace OzelEgitimHakedis.Wpf.Tests;

public sealed partial class OfficialDocumentOutputTests
{
    [Theory]
    [InlineData(TransportationProgramType.SpecialEducation)]
    [InlineData(TransportationProgramType.BasicOrSecondary)]
    public void November3_JuneCompletionAndLongText_ShouldFitWithPreservedSignatures(TransportationProgramType program)
    {
        RunOnSta(() =>
        {
            var payment=CreatePayment(new ProgressPaymentInput(18,100,0,0,100000,true,0,0,0,ProgramType:program)) with {PeriodDate=new DateOnly(2027,6,1)};
            var name=string.Join(" - ",Enumerable.Range(1,18).Select(i=>$"Merkez Köyü Uzun Güzergâh Çiftlikler Bölgesi Hat-{i}"))+" Hattı Taşıma İşi";
            var context=CreateDocumentContext(payment) with {GeneratedDate=new DateOnly(2027,6,25),PeriodDate=new DateOnly(2027,6,1),ContractEndDate=new DateOnly(2027,6,25),WorkName=name,RouteName=name,IsCombined=true,ContractorName="S.S. Yeşil Sivaslı Min. Mot.Taş.Koop."};
            var document=OehsOfficialDocumentFactory.BuildCombined(context);
            var acceptance=document.Pages[2].Child;
            var cells=acceptance.Children.OfType<Border>().Select(b=>b.Child).OfType<TextBlock>().ToArray();
            Assert.Contains(cells,b=>b.Text=="25.06.2027 Cuma");
            Assert.Contains(cells,b=>b.Text=="Süre uzatımı verilmemiş olup, 25.06.2027 tarihinde bitmiştir");
            var nonJune=OehsOfficialDocumentFactory.BuildCombined(context with {PeriodDate=new DateOnly(2027,5,1)}).Pages[2].Child;
            Assert.DoesNotContain(nonJune.Children.OfType<Border>().Select(b=>b.Child).OfType<TextBlock>(),b=>b.Text.Contains("Süre uzatımı verilmemiş",StringComparison.Ordinal));
            foreach(var index in new[]{0,2})
            {
                var block=Assert.Single(document.Pages[index].Child.Children.OfType<TextBlock>().Where(b=>b.Text==context.ContractorName));
                Assert.Equal(TextAlignment.Center,block.TextAlignment);
                Assert.True(FixedPage.GetLeft(block)<100);
            }
            var root=new DirectoryInfo(AppContext.BaseDirectory);
            while(root.Parent is not null&&!File.Exists(Path.Combine(root.FullName,"OzelEgitimHakedisSistemi.sln")))root=root.Parent;
            var path=Path.Combine(root.FullName,".superpowers","corrections-2026-11-03",program+"-verified.pdf");
            PdfVectorWriter.Save(document,path);
            using var pdf=UglyToad.PdfPig.PdfDocument.Open(path);
            foreach(var page in pdf.GetPages())Assert.Contains("Hat-18",page.Text,StringComparison.Ordinal);
            Assert.Contains(context.PreparerName,pdf.GetPage(2).Text,StringComparison.Ordinal);
            Assert.Contains("100.000,00",pdf.GetPage(2).Text,StringComparison.Ordinal);
        });
    }
    [Theory]
    [InlineData(TransportationProgramType.SpecialEducation)]
    [InlineData(TransportationProgramType.BasicOrSecondary)]
    public void November3_OfficialIdentifiers_ShouldUsePaymentAndPrintedPageNumbers(TransportationProgramType program)
    {
        RunOnSta(() =>
        {
            var payment=CreatePayment(new ProgressPaymentInput(18,100,0,0,100000,true,0,0,0,ProgramType:program));
            var context=CreateDocumentContext(payment) with {TenderNumber="2026/1592223",RouteNumber="99",ProgressPaymentNumber=7};
            var combined=OehsOfficialDocumentFactory.BuildCombined(context);
            var service=combined.Pages[1].Child;
            Assert.Contains(service.Children.OfType<TextBlock>(),b=>b.Text=="7" && Math.Abs(System.Windows.Documents.FixedPage.GetTop(b)-99)<0.1);
            Assert.DoesNotContain(service.Children.OfType<TextBlock>(),b=>b.Text.Contains(context.TenderNumber,StringComparison.Ordinal));
            Assert.DoesNotContain(combined.Pages[2].Child.Children.OfType<TextBlock>(),b=>b.Text.Contains(context.TenderNumber,StringComparison.Ordinal));
            Assert.Contains(combined.Pages[3].Child.Children.OfType<Border>(),b=>b.Child is TextBlock {Text:"Sayfa No : 4"});
            var standalone=OehsOfficialDocumentFactory.BuildSeparate(context).Single(d=>d.Kind==OehsOfficialDocumentKind.HakedisIcmali);
            Assert.Contains(standalone.Document.Pages[0].Child.Children.OfType<Border>(),b=>b.Child is TextBlock {Text:"Sayfa No : 1"});
        });
    }
}

public sealed partial class HakedisCreateHybridUiContractTests
{
    [Theory]
    [InlineData(185)]
    [InlineData(215)]
    public void CreatorCredits_ShouldFitEntireFooterAtMinimumWindowHeight(double width)
    {
        RunOnSta(() =>
        {
            var credits=NamedElement(LoadMainWindowXaml(),"CreatorCreditsTextBlock");
            var navigation=credits.Ancestors().First(e=>e.Name.LocalName=="Grid");
            var layout=LoadLayoutFragment(navigation);
            ArrangeLayout(layout,width,526);
            var names=(TextBlock)layout.FindName("CreatorCreditsTextBlock");
            var footer=(Border)((FrameworkElement)names.Parent).Parent;
            var bounds=names.TransformToAncestor(footer).TransformBounds(new Rect(names.RenderSize));
            Assert.True(bounds.Bottom<=footer.ActualHeight-footer.Padding.Bottom-1, $"Names extend to {bounds.Bottom} in footer height {footer.ActualHeight}");
            var decoration=((StackPanel)names.Parent).Children.OfType<Border>().Single();
            var decorationBounds=decoration.TransformToAncestor(footer).TransformBounds(new Rect(decoration.RenderSize));
            Assert.True(decorationBounds.Bottom<=footer.ActualHeight-footer.Padding.Bottom+0.5);
            SavePaymentDetailsPreview(layout,$"credits-full-navigation-{width:0}.png");
        });
    }

    [Fact]
    public void November3_HybridColumns_ShouldSeparateNumberAndAllowManualSupport()
    {
        var grid=NamedElement(LoadMainWindowXaml(),"MultiRoutePaymentGrid");
        Assert.Single(grid.Descendants().Where(e=>(string?)e.Attribute("Header")=="Hat No"));
        Assert.Contains("Binding RouteNumber",(string?)grid.Descendants().Single(e=>(string?)e.Attribute("Header")=="Hat No").Attribute("Binding"),StringComparison.Ordinal);
        Assert.DoesNotContain(grid.Descendants(),e=>(string?)e.Attribute("Header")=="Asgari Ücret Desteği Kullan");
        Assert.Contains(grid.Descendants(),e=>(string?)e.Attribute("Header")=="Hakediş Gün Sayısı");
        var support=Assert.Single(grid.Descendants().Where(e=>(string?)e.Attribute("Header")=="Asgari Ücret Desteği"));
        Assert.DoesNotContain(support.Descendants(),e=>(string?)e.Attribute("Property")=="IsEnabled");
        var row=new MainWindow.MultiRoutePaymentDraftRow {MinimumWageSupportEnabled=false};
        row.MinimumWageSupport=150;
        Assert.Equal(150m,row.CreateInput().MinimumWageSupport);
        RunOnSta(() =>
        {
            var editor=CreateEditorHarness();
            Invoke(editor,"SetMinimumWageSupportInputMode",TransportationProgramType.BasicOrSecondary,false,0m);
            Assert.True(Field<TextBox>(editor,"MinimumWageSupportTextBox").IsEnabled);
            Assert.Equal(Visibility.Collapsed,Field<CheckBox>(editor,"MinimumWageSupportEnabledCheckBox").Visibility);
            var credits=NamedElement(LoadMainWindowXaml(),"CreatorCreditsTextBlock");
            Assert.Equal("Aşkın KESKİNBALTA & Murat ÇALIMLIER",((string?)credits.Attribute("Text"))?.Replace("\n"," ",StringComparison.Ordinal));
            var footer=LoadLayoutFragment(credits.Parent!.Parent!);
            ArrangeLayout(footer,215,100);
            SavePaymentDetailsPreview(footer,"nov3-credits.png");
        });
    }

    [Fact]
    public void November3_History_ShouldShowOneSummedRecordForEachCalculatedBatch()
    {
        RunOnSta(() =>
        {
            var window=CreateEditorHarness();
            var template=ArchiveProgressEditorContextTests.CreateArchive().ProgressPayment with {TenderNumber="IHALE-01",ContractNumber="SOZ-01",ContractorTaxOrIdentityNumber="12345678901",ContractorName="Firma"};
            var batch=Guid.NewGuid();
            var first=template with {Id=Guid.NewGuid(),BatchId=batch,RouteNumber="01"};
            var second=template with {Id=Guid.NewGuid(),BatchId=batch,RouteNumber="05"};
            var other=template with {Id=Guid.NewGuid(),BatchId=Guid.NewGuid(),RouteNumber="09"};
            foreach(var p in new[]{first,second,other})Collection(window,"_progressHistory").Add(p);
            Invoke(window,"UpdateHistoryView");
            var rows=Collection(window,"_historyRows");
            Assert.Equal(2,rows.Count);
            var net=rows.Cast<object>().Select(r=>(string)r.GetType().GetProperty("NetPayableText")!.GetValue(r)!).ToArray();
            Assert.Contains((first.Calculation.NetPayable+second.Calculation.NetPayable).ToString("N2",System.Globalization.CultureInfo.GetCultureInfo("tr-TR"))+" ₺",net);
        });
    }

    [Fact]
    public void November3_ArchivesAndOutputs_ShouldGroupSelectedBatchAndShowCombinedAmounts()
    {
        RunOnSta(() =>
        {
            var temp=Directory.CreateTempSubdirectory("oehs-nov3-groups-");
            try
            {
                var window=CreateEditorHarness();
                var archive=ArchiveProgressEditorContextTests.CreateArchive();
                var batch=Guid.NewGuid();
                var payments=new List<ProgressPaymentHistoryData>();
                var store=new ProgressPaymentArchiveStore(temp.FullName);
                Collection(window,"_contracts").Add(archive.Contract);
                for(var i=0;i<3;i++)
                {
                    var number=(i+1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    var payment=archive.ProgressPayment with {Id=Guid.NewGuid(),BatchId=i<2?batch:Guid.NewGuid(),TenderNumber="IHALE-01",ContractNumber="SOZ-01",ContractorTaxOrIdentityNumber="12345678901",ContractorName="Firma",RouteNumber=number};
                    payments.Add(payment);Collection(window,"_progressHistory").Add(payment);
                    Collection(window,"_routes").Add(CreateRoute(number,"Hat "+number,"12345678901"));
                    store.Save(archive with {ProgressPayment=payment,Route=archive.Route with {RouteNumber=number}});
                }
                var grouped=Assert.IsAssignableFrom<IReadOnlyList<ArchiveDisplayRow>>(Invoke(window,"GroupArchiveRows",ArchiveDisplayService.BuildRows(store)));
                Assert.Equal(2,grouped.Count);
                Assert.Contains(grouped,r=>r.NetPayableText==(payments[0].Calculation.NetPayable+payments[1].Calculation.NetPayable).ToString("N2",System.Globalization.CultureInfo.GetCultureInfo("tr-TR"))+" ₺");
                var tender=Field<ComboBox>(window,"OutputTenderListBox");tender.Items.Add(archive.Contract);tender.SelectedItem=archive.Contract;
                Invoke(window,"UpdateOutputRoutesForContract",archive.Contract);
                Assert.Single(Field<ComboBox>(window,"ReportRouteComboBox").Items.Cast<object>());
                Invoke(window,"RefreshOutputRecords");
                Assert.Equal(2,Collection(window,"_reportRows").Count);
                var selected=Assert.IsType<ProgressPaymentHistoryData>(Invoke(window,"GetSelectedOutputPayment"));
                var members=payments.Where(p=>p.BatchId==selected.BatchId).ToArray();
                Assert.Equal(members.Sum(p=>p.Calculation.GrossAmount).ToString("N2",System.Globalization.CultureInfo.GetCultureInfo("tr-TR"))+" ₺",Field<TextBlock>(window,"ReportGrossTextBlock").Text);
                Assert.Equal(members.Sum(p=>p.Calculation.NetPayable).ToString("N2",System.Globalization.CultureInfo.GetCultureInfo("tr-TR"))+" ₺",Field<TextBlock>(window,"ReportNetTextBlock").Text);
                SetField(window,"_archiveStore",store);
                Invoke(window,"UpdateHistoryView");Invoke(window,"UpdateArchiveView");
                foreach(var prefix in new[]{"History","Archive"})
                {
                    var layout=LoadLayoutFragment(NamedElement(LoadMainWindowXaml(),prefix+"Panel"));layout.Visibility=Visibility.Visible;
                    ((ItemsControl)layout.FindName(prefix+"ItemsControl")).ItemsSource=Collection(window,prefix=="History"?"_historyRows":"_archiveRows");
                    ((Border)layout.FindName(prefix+"EmptyStateBorder")).Visibility=Visibility.Collapsed;
                    ((TextBlock)layout.FindName(prefix+"CountTextBlock")).Text="2";
                    if(prefix=="History")((TextBlock)layout.FindName("HistoryNetTotalTextBlock")).Text=Field<TextBlock>(window,"HistoryNetTotalTextBlock").Text;
                    ArrangeLayout(layout,1100,720);SavePaymentDetailsPreview(layout,"nov3-"+prefix+".png");
                }
                var output=LoadLayoutFragment(NamedElement(LoadMainWindowXaml(),"ReportsPanel"));output.Visibility=Visibility.Visible;
                var outputTender=(ComboBox)output.FindName("OutputTenderListBox");outputTender.ItemsSource=new[]{archive.Contract};outputTender.SelectedIndex=0;
                var contractor=(ComboBox)output.FindName("ReportRouteComboBox");contractor.ItemsSource=Field<ComboBox>(window,"ReportRouteComboBox").ItemsSource;contractor.SelectedIndex=0;
                var reports=(ListBox)output.FindName("ReportItemsControl");reports.ItemsSource=Collection(window,"_reportRows");reports.SelectedIndex=0;
                ((Border)output.FindName("ReportEmptyStateBorder")).Visibility=Visibility.Collapsed;
                foreach(var label in new[]{"ReportCountTextBlock","ReportGrossTextBlock","ReportNetTextBlock","ReportDetailTitleTextBlock","ReportDetailMetaTextBlock"})
                    ((TextBlock)output.FindName(label)).Text=Field<TextBlock>(window,label).Text;
                ArrangeLayout(output,1100,720);SavePaymentDetailsPreview(output,"nov3-outputs.png");
            }
            finally {temp.Delete(true);}
        });
    }
}
