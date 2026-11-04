using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using OzelEgitimHakedis.Core.Models;
using OzelEgitimHakedis.Core.Services;
using OzelEgitimHakedis.Wpf;
using Xunit;

namespace OzelEgitimHakedis.Wpf.Tests;

public sealed partial class HakedisCreateHybridUiContractTests
{
    private static readonly string[] SourceRouteNumbers = new[] { "01", "02" };
    private static readonly string[] RouteMoneyFieldNames = new[]{"RouteDailyPriceTextBox","RouteContractAmountTextBox","RouteGuideDailyPriceTextBox"};
    private static readonly string[] ExpectedExportHeaders = new[]{"Rehber Personel Günlük Fiyat","Hattın Adı","Döneme Esas THGS"};
    private static readonly string[] HomeFinancialCardNames = new[]{"Net","Gross","Deductions","Vat","Transport","Difference","Routes","Firms"};
    private static readonly double[] HybridRenderDpis = new[]{96d,120d,144d};
    private static readonly int[] SelectedRouteCounts = new[]{1,2};
    private static readonly string[] InvalidSharedInputNames = new[]{"WorkDaysTextBox","DailyPriceTextBox","PriceDifferenceTextBox","TaxDebtTextBox"};
    private static readonly string[] HomeDetailPanelNames = new[]{"HomeMonthlyGrid","HomePreflightPanel"};
    private static readonly string[] HomeOutputActionNames = new[]{"HomePreviewButton","HomeSavePdfsButton"};
    private static readonly string[] ProgressMoneyInputNames = new[] { "WorkDaysTextBox", "DailyPriceTextBox", "PriceDifferenceTextBox", "MinimumWageSupportTextBox" };
    private static readonly string[] InitiallyHiddenPanelNames = new[] { "HomePanel", "ProgressPanel", "SummaryPanel", "AcceptancePanel", "RecordsPanel", "PlaceholderPanel" };

    private static readonly XNamespace XamlNamespace =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void HybridSourceValues_ShouldRemainEditableAndNotBeOverwrittenOnReselection()
    {
        RunOnSta(() =>
        {
            var window = CreateEditorHarness();
            var selector = Field<ListBox>(window, "ProgressRouteComboBox");
            var route = CreateRoute("01", "Merkez", "12345678901");
            selector.Items.Add(route);
            Collection(window, "_routes").Add(route);
            selector.SelectedItems.Add(route);
            Invoke(window, "UpdateHybridRouteDraftRows");
            var rows = Field<System.Collections.ObjectModel.ObservableCollection<MainWindow.MultiRoutePaymentDraftRow>>(window, "_multiRoutePaymentRows");
            var row = Assert.Single(rows);
            row.DailyPrice = 1234.5678m;
            row.GuideDailyPrice = 333.25m;
            row.RouteName = "Yeni Merkez";
            Invoke(window, "UpdateHybridRouteDraftRows");
            Assert.Equal(1234.5678m, row.DailyPrice);
            Assert.Equal(333.25m, row.GuideDailyPrice);
            Assert.Equal("Yeni Merkez", row.RouteName);
        });
    }

    [Fact]
    public void HybridSourceUpdate_ShouldKeepCancelDirtyThenPersistAllSelectedChangesOnConfirmation()
    {
        RunOnSta(() =>
        {
            var temp = Directory.CreateTempSubdirectory("oehs-source-update-");
            try
            {
                var window = CreateEditorHarness();
                var store = new OehsProgramScopedApplicationDataStore(Path.Combine(temp.FullName, "data"));
                SetField(window, "_dataStore", store);
                SetField(window, "_calculationConstantsHistory", new System.Collections.Generic.List<ProgressPaymentCalculationConstantsChange>());
                var contract = ArchiveProgressEditorContextTests.CreateArchive().Contract;
                Collection(window, "_contracts").Add(contract);
                Collection(window, "_contractors").Add(CreateFirm("Test firma", "12345678901"));
                var tender = Field<ComboBox>(window, "ProgressTenderListBox");
                tender.Items.Add(contract); tender.SelectedItem = contract;
                var selector = Field<ListBox>(window, "ProgressRouteComboBox");
                foreach (var number in SourceRouteNumbers)
                {
                    var route = CreateRoute(number, "Hat " + number, "12345678901");
                    Collection(window, "_routes").Add(route);
                    selector.Items.Add(route); selector.SelectedItems.Add(route);
                }
                var month = new ProgressPaymentMonthOption(new DateOnly(2026,10,1),new DateOnly(2026,10,31));
                var months = Field<ComboBox>(window, "ProgressPeriodMonthComboBox");
                months.Items.Add(month); months.SelectedItem = month;
                Invoke(window, "UpdateHybridRouteDraftRows");
                var buttonField = typeof(MainWindow).GetField("UpdateHybridRoutesButton",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public);
                Assert.NotNull(buttonField);
                var button = Assert.IsType<Button>(buttonField!.GetValue(window));
                Assert.False(button.IsEnabled);
                var rows = Field<System.Collections.ObjectModel.ObservableCollection<MainWindow.MultiRoutePaymentDraftRow>>(window, "_multiRoutePaymentRows");
                rows[0].WorkDays = 18.5m; rows[0].DailyPrice = 1234.5678m;
                rows[0].GuideWorkDays = 17m; rows[0].GuideDailyPrice = 333.25m;
                rows[0].TaxDebt = 12m; rows[0].RouteName = "Yeni Merkez";
                rows[1].WorkDays = 14m; rows[1].GuideDailyPrice = 222m;
                Assert.True(button.IsEnabled);
                Assert.False((bool)Invoke(window,"ApplyHybridRouteChanges",false)!);
                Assert.True(button.IsEnabled);
                Assert.Equal(1000m,(decimal)Collection(window,"_routes")[0]!.GetType().GetProperty("DailyPrice")!.GetValue(Collection(window,"_routes")[0])!);
                Assert.Empty(store.Load().Routes);
                Assert.True((bool)Invoke(window,"ApplyHybridRouteChanges",true)!);
                Assert.False(button.IsEnabled);
                var saved = store.Load();
                Assert.Equal(2,saved.Routes.Count);
                var first = saved.Routes.Single(route => route.RouteNumber == "01");
                Assert.Equal("Yeni Merkez",first.Name);
                Assert.Equal(20m,first.TransportWorkDays);
                Assert.Equal(18.5m,rows[0].WorkDays);
                Assert.Equal(1234.5678m,first.DailyPrice);
                Assert.Equal(333.25m,first.GuideDailyPrice);
                Assert.Equal(20m,saved.Routes.Single(route=>route.RouteNumber=="02").TransportWorkDays);
                Assert.Empty(saved.ProgressPayments!);
                using var json = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(saved));
                var drafts = json.RootElement.GetProperty("HybridPaymentDrafts");
                Assert.Equal(2,drafts.GetArrayLength());
                var input = drafts.EnumerateArray().Single(d=>d.GetProperty("RouteKey").GetString()==rows[0].RouteKey).GetProperty("Input");
                Assert.Equal(17m,input.GetProperty("GuideWorkDays").GetDecimal());
                Assert.Equal(12m,input.GetProperty("TaxDebt").GetDecimal());
                rows[0].WorkDays=16m;
                Assert.True(button.IsEnabled);
                rows[0].WorkDays=18.5m;
                Assert.False(button.IsEnabled);
            }
            finally { temp.Delete(true); }
        });
    }

    [Theory]
    [InlineData("disk")]
    [InlineData("discount")]
    public void HybridSourceUpdate_ShouldRejectFailureWithoutMutatingSources(string failure)
    {
        RunOnSta(() =>
        {
            var temp=Directory.CreateTempSubdirectory("oehs-update-failure-");
            try
            {
                var root=Path.Combine(temp.FullName,"data");
                if(failure=="disk") File.WriteAllText(root,"not a directory");
                var window=CreateHybridUpdateHarness(root);
                var original=Collection(window,"_routes")[0];
                var row=Assert.Single(Field<System.Collections.ObjectModel.ObservableCollection<MainWindow.MultiRoutePaymentDraftRow>>(window,"_multiRoutePaymentRows"));
                row.GuideDailyPrice=300m;
                if(failure=="discount") row.DiscountAmount=-1m;
                Assert.False((bool)Invoke(window,"ApplyHybridRouteChanges",true)!);
                Assert.Same(original,Collection(window,"_routes")[0]);
                Assert.True(Field<Button>(window,"UpdateHybridRoutesButton").IsEnabled);
                Assert.Equal(300m,row.GuideDailyPrice);
                Assert.Empty(Field<System.Collections.Generic.List<HybridPaymentDraftData>>(window,"_savedHybridPaymentDrafts"));
            }
            finally {temp.Delete(true);}
        });
    }

    [Fact]
    public void HybridDraft_ShouldLoadOnFirstMonthSelectionAfterRouteSelection()
    {
        RunOnSta(() =>
        {
            var window=CreateHybridUpdateHarness("unused-test-root");
            var rows=Field<System.Collections.ObjectModel.ObservableCollection<MainWindow.MultiRoutePaymentDraftRow>>(window,"_multiRoutePaymentRows");
            var key=Assert.Single(rows).RouteKey;
            var input=new ProgressPaymentInput(19m,1234m,0,0,100000,false,27m,0,0,GuideWorkDays:17m,GuideDailyPrice:200m);
            SetField(window,"_savedHybridPaymentDrafts",new System.Collections.Generic.List<HybridPaymentDraftData>{new(key,new DateOnly(2026,10,1),input)});
            var months=Field<ComboBox>(window,"ProgressPeriodMonthComboBox");
            months.SelectedIndex=-1;
            rows.Clear();Field<RouteDraftCache<MainWindow.MultiRoutePaymentDraftRow>>(window,"_multiRouteDraftCache").Clear();
            Invoke(window,"UpdateHybridRouteDraftRows");
            months.SelectedIndex=0;
            // WPF harness bypasses initialization; exercise the transition body directly.
            Invoke(window,"RestoreHybridInputsForSelectedPeriod");
            var loaded=Assert.Single(rows);
            Assert.Equal(17m,loaded.GuideWorkDays);
            Assert.Equal(27m,loaded.TaxDebt);
            Assert.False(Field<Button>(window,"UpdateHybridRoutesButton").IsEnabled);
        });
    }

    [Fact]
    public void HybridSourceRefresh_ShouldAdoptCleanSourceFieldsWithoutLosingMonthlyEdits()
    {
        RunOnSta(() =>
        {
            var window=CreateHybridUpdateHarness("unused-test-root");
            var row=Assert.Single(Field<System.Collections.ObjectModel.ObservableCollection<MainWindow.MultiRoutePaymentDraftRow>>(window,"_multiRoutePaymentRows"));
            row.TaxDebt=27m;row.GuideWorkDays=17m;
            var updated=CreateRoute("01","Yeni kaynak","12345678901",dailyPrice:1600m,guidePrice:300m);
            Collection(window,"_routes")[0]=updated;
            var selector=Field<ListBox>(window,"ProgressRouteComboBox");
            selector.Items.Clear();selector.Items.Add(updated);selector.SelectedItems.Add(updated);
            Invoke(window,"UpdateHybridRouteDraftRows");
            Assert.Equal(1600m,row.DailyPrice);
            Assert.Equal(300m,row.GuideDailyPrice);
            Assert.Equal("Yeni kaynak",row.RouteName);
            Assert.Equal(27m,row.TaxDebt);
            Assert.Equal(17m,row.GuideWorkDays);
            Assert.True(Field<Button>(window,"UpdateHybridRoutesButton").IsEnabled);
        });
    }

    [Fact]
    public void LiveRoutePreview_ShouldDetectContractChangeAndPreserveUnchangedPeriodInputs()
    {
        RunOnSta(() =>
        {
            var window=CreateEditorHarness();
            var archive=ArchiveProgressEditorContextTests.CreateArchive();
            var input=archive.ProgressPayment.Input with { WorkDays=9m,DailyPrice=1200m,ContractAmount=100000m };
            var payment=archive.ProgressPayment with { Input=input,Calculation=ProgressPaymentCalculator.Calculate(input),
                TenderNumber="IHALE-01",ContractNumber="SOZ-01",ContractorTaxOrIdentityNumber="12345678901",ContractDate=new DateOnly(2026,1,1) };
            Collection(window,"_progressHistory").Add(payment);
            var route=Activator.CreateInstance(typeof(MainWindow).GetNestedType("RouteDraft",BindingFlags.NonPublic)!,
                "01","Hat 01",1000m,0m,20m,"IHALE-01","12345678901","Firma","SOZ-01",input.ProgramType,100000m,new DateOnly(2026,1,1))!;
            Collection(window,"_routes").Add(route);SetField(window,"_editingRoute",route);
            SetField(window,"_historyCorrectionTargetId",payment.Id);
            var section=typeof(MainWindow).GetNestedType("HistoryCorrectionSection",BindingFlags.NonPublic)!;
            SetField(window,"_historyCorrectionSection",Enum.Parse(section,"Route"));
            Field<TextBox>(window,"RouteNumberTextBox").Text="01";Field<TextBox>(window,"RouteNameTextBox").Text="Hat 01";
            Field<TextBox>(window,"RouteDailyPriceTextBox").Text="1.000,00";
            Field<TextBox>(window,"RouteGuideDailyPriceTextBox").Text="0";Field<TextBox>(window,"RouteContractAmountTextBox").Text="125.000,00";
            Field<DatePicker>(window,"RouteContractDatePicker").SelectedDate=new DateTime(2026,1,2);
            Assert.True((bool)Invoke(window,"HistoryCorrectionInputsDiffer")!);
            var args=new object?[]{null,null,null};
            var method=typeof(MainWindow).GetMethod("TryCreateHistoryCorrectionPreview",BindingFlags.NonPublic|BindingFlags.Instance)!;
            Assert.True((bool)method.Invoke(window,args)!);
            var preview=Assert.IsType<ProgressPaymentInput>(args[0]);
            Assert.Equal(9m,preview.WorkDays);Assert.Equal(1200m,preview.DailyPrice);Assert.Equal(125000m,preview.ContractAmount);
            Assert.Equal(10800m,Assert.IsType<ProgressPaymentCalculation>(args[1]).ServiceAmount);
        });
    }

    [Fact]
    public void ArchiveRoutePreview_ShouldDetectContractChangeAndPreserveUnchangedPeriodInputs()
    {
        RunOnSta(() =>
        {
            var window=CreateEditorHarness();
            var archive=ArchiveProgressEditorContextTests.CreateArchive();
            var input=archive.ProgressPayment.Input with { WorkDays=9m, DailyPrice=1200m, ContractAmount=100000m };
            archive=archive with { Route=archive.Route with { ContractAmount=100000m, ContractDate=new DateOnly(2026,1,1) },
                ProgressPayment=archive.ProgressPayment with { Input=input,Calculation=ProgressPaymentCalculator.Calculate(input),ContractDate=new DateOnly(2026,1,1) } };
            SetField(window,"_editingArchiveSnapshot",archive);
            var section=typeof(MainWindow).GetNestedType("ArchiveCorrectionSection",BindingFlags.NonPublic)!;
            SetField(window,"_archiveCorrectionSection",Enum.Parse(section,"Route"));
            Field<TextBox>(window,"RouteNumberTextBox").Text=archive.Route.RouteNumber;
            Field<TextBox>(window,"RouteNameTextBox").Text=archive.Route.Name;

            Field<TextBox>(window,"RouteDailyPriceTextBox").Text="1.000,00";
            Field<TextBox>(window,"RouteGuideDailyPriceTextBox").Text="0";
            Field<TextBox>(window,"RouteContractAmountTextBox").Text="125.000,00";
            Field<DatePicker>(window,"RouteContractDatePicker").SelectedDate=new DateTime(2026,1,2);
            Assert.True((bool)Invoke(window,"ArchiveCorrectionInputsDiffer")!);
            var args=new object?[]{null,null,null};
            var method=typeof(MainWindow).GetMethod("TryCreateArchiveCorrectionPreview",BindingFlags.NonPublic|BindingFlags.Instance)!;
            Assert.True((bool)method.Invoke(window,args)!);
            var preview=Assert.IsType<ProgressPaymentInput>(args[0]);
            Assert.Equal(9m,preview.WorkDays);Assert.Equal(1200m,preview.DailyPrice);Assert.Equal(125000m,preview.ContractAmount);
            Assert.Equal(10800m,Assert.IsType<ProgressPaymentCalculation>(args[1]).ServiceAmount);
        });
    }

    [Fact]
    public void RouteEditor_ShouldRejectExcessPrecisionForEachMoneyField()
    {
        RunOnSta(() =>
        {
            var temp=Directory.CreateTempSubdirectory("oehs-route-invalid-");
            try
            {
            var root=Path.Combine(temp.FullName,"data");
            var window=CreateHybridUpdateHarness(root);
            var contract=(ContractDraftData)Collection(window,"_contracts")[0]!;
            var firm=Collection(window,"_contractors")[0]!;
            var tender=Field<ComboBox>(window,"RouteTenderListBox");
            tender.Items.Add(contract);tender.SelectedItem=contract;
            var contractors=Field<ComboBox>(window,"RouteContractorListBox");
            contractors.ItemsSource=new[]{firm};contractors.SelectedItem=firm;
            Field<TextBox>(window,"RouteNumberTextBox").Text="02";
            Field<TextBox>(window,"RouteNameTextBox").Text="Kuzey";

            Field<TextBox>(window,"RouteDailyPriceTextBox").Text="1.234,56";
            Field<TextBox>(window,"RouteContractAmountTextBox").Text="12.536,48";
            Field<TextBox>(window,"RouteGuideDailyPriceTextBox").Text="100,00";
            Field<DatePicker>(window,"RouteContractDatePicker").SelectedDate=new DateTime(2026,1,1);
            foreach(var fieldName in RouteMoneyFieldNames)
            {
                var field=Field<TextBox>(window,fieldName);var original=field.Text;field.Text=fieldName=="RouteContractAmountTextBox"?"12,345":"12,34567";
                Invoke(window,"AddRouteButton_Click",window,new RoutedEventArgs());
                Assert.Single(Collection(window,"_routes"));
                var expected=fieldName switch { "RouteDailyPriceTextBox"=>"Taşıma Günlük Fiyatı", "RouteContractAmountTextBox"=>"Sözleşme Bedeli", _=>"Rehber Personel Günlük Fiyat" };
                Assert.Contains(expected,Field<TextBlock>(window,"RouteValidationMessageTextBlock").Text, StringComparison.Ordinal);
                field.Text=original;
            }
            var progressTender=Field<ComboBox>(window,"ProgressTenderListBox");
            progressTender.Items.Clear();progressTender.ItemsSource=Collection(window,"_contracts");
            var progressRoutes=Field<ListBox>(window,"ProgressRouteComboBox");
            progressRoutes.Items.Clear();progressRoutes.ItemsSource=Collection(window,"_routes");
            Field<ComboBox>(window,"ProgressPeriodMonthComboBox").Items.Clear();
            Invoke(window,"AddRouteButton_Click",window,new RoutedEventArgs());
            var saved=new OehsProgramScopedApplicationDataStore(root).Load();
            var added=Assert.Single(saved.Routes.Where(x=>x.RouteNumber=="02"));
            Assert.Equal(1234.56m,added.DailyPrice);
            Assert.Equal(12536.48m,added.ContractAmount);
            Assert.Equal(new DateOnly(2026,1,1),added.ContractDate);
            }
            finally { temp.Delete(true); }
        });
    }

    [Fact]
    public void ContractMetadataEdit_ShouldPreserveARoutesOwnDailyPrice()
    {
        RunOnSta(() =>
        {
            var temp=Directory.CreateTempSubdirectory("oehs-contract-price-");
            try
            {
                var window=CreateHybridUpdateHarness(Path.Combine(temp.FullName,"data"));
                var contract=(ContractDraftData)Collection(window,"_contracts")[0]!;
                Collection(window,"_routes")[0]=CreateRoute("01","Merkez","12345678901",dailyPrice:1600m);
                SetField(window,"_editingContractTender",contract.TenderNumber);
                SetField(window,"_editingContractNumber",contract.ContractNumber);
                SetField(window,"_editingContractProgramType",contract.ProgramType);
                Field<TextBox>(window,"TenderNumberTextBox").Text=contract.TenderNumber;
                Field<TextBox>(window,"ContractAmountTextBox").Text=(contract.ContractAmount+1).ToString(CultureInfo.InvariantCulture);
                Field<TextBox>(window,"ContractWorkDaysTextBox").Text=contract.ContractWorkDays.ToString(CultureInfo.InvariantCulture);
                Field<TextBox>(window,"ContractTransportDailyPriceTextBox").Text=contract.TransportDailyPrice.ToString(CultureInfo.InvariantCulture);
                Field<DatePicker>(window,"StartDatePicker").SelectedDate=contract.StartDate.ToDateTime(TimeOnly.MinValue);
                Field<DatePicker>(window,"EndDatePicker").SelectedDate=contract.EndDate.ToDateTime(TimeOnly.MinValue);
                var progressTenders=Field<ComboBox>(window,"ProgressTenderListBox");
                progressTenders.Items.Clear();progressTenders.ItemsSource=Collection(window,"_contracts");
                var progressRoutes=Field<ListBox>(window,"ProgressRouteComboBox");
                progressRoutes.Items.Clear();progressRoutes.ItemsSource=Collection(window,"_routes");
                Field<ComboBox>(window,"ProgressPeriodMonthComboBox").Items.Clear();
                Invoke(window,"SaveContractButton_Click",window,new RoutedEventArgs());
                var saved=new OehsProgramScopedApplicationDataStore(Path.Combine(temp.FullName,"data")).Load();
                Assert.Equal(1600m,Assert.Single(saved.Routes).DailyPrice);
            }
            finally {temp.Delete(true);}
        });
    }

    [Fact]
    public void HybridSavedDraft_ShouldFollowSubsequentSourcePriceChangesOnReload()
    {
        RunOnSta(() =>
        {
            var temp=Directory.CreateTempSubdirectory("oehs-source-reload-");
            try
            {
                var root=Path.Combine(temp.FullName,"data");
                var window=CreateHybridUpdateHarness(root);
                var row=Assert.Single(Field<System.Collections.ObjectModel.ObservableCollection<MainWindow.MultiRoutePaymentDraftRow>>(window,"_multiRoutePaymentRows"));
                row.GuideDailyPrice=200m;row.GuideWorkDays=17m;row.TaxDebt=27m;
                Assert.True((bool)Invoke(window,"ApplyHybridRouteChanges",true)!);
                Collection(window,"_routes")[0]=CreateRoute("01","Merkez","12345678901",guidePrice:300m);
                Assert.True((bool)Invoke(window,"SavePersistedData",false)!);
                var data=new OehsProgramScopedApplicationDataStore(root).Load();
                var saved=Assert.Single(data.HybridPaymentDrafts!);
                Assert.Equal(300m,saved.Input.GuideDailyPrice);
                Assert.Equal(17m,saved.Input.GuideWorkDays);
                Assert.Equal(27m,saved.Input.TaxDebt);
                var reopened=CreateHybridUpdateHarness(root);
                var updated=CreateRoute("01","Merkez","12345678901",guidePrice:300m);
                Collection(reopened,"_routes")[0]=updated;
                var selector=Field<ListBox>(reopened,"ProgressRouteComboBox");
                selector.Items.Clear();selector.Items.Add(updated);selector.SelectedItems.Add(updated);
                SetField(reopened,"_savedHybridPaymentDrafts",data.HybridPaymentDrafts!.ToList());
                Field<System.Collections.ObjectModel.ObservableCollection<MainWindow.MultiRoutePaymentDraftRow>>(reopened,"_multiRoutePaymentRows").Clear();
                Field<RouteDraftCache<MainWindow.MultiRoutePaymentDraftRow>>(reopened,"_multiRouteDraftCache").Clear();
                Invoke(reopened,"UpdateHybridRouteDraftRows");
                var restored=Assert.Single(Field<System.Collections.ObjectModel.ObservableCollection<MainWindow.MultiRoutePaymentDraftRow>>(reopened,"_multiRoutePaymentRows"));
                Assert.Equal(300m,restored.GuideDailyPrice);
                Assert.Equal(17m,restored.GuideWorkDays);
                Assert.Equal(27m,restored.TaxDebt);
            }
            finally {temp.Delete(true);}
        });
    }

    [Fact]
    public void HybridRouteIdentityEdit_ShouldKeepItsPersistedMonthlyDraftAndCachedEdits()
    {
        RunOnSta(() =>
        {
            var temp=Directory.CreateTempSubdirectory("oehs-draft-identity-");
            try
            {
                var root=Path.Combine(temp.FullName,"data");
                var window=CreateHybridUpdateHarness(root);
                var row=Assert.Single(Field<System.Collections.ObjectModel.ObservableCollection<MainWindow.MultiRoutePaymentDraftRow>>(window,"_multiRoutePaymentRows"));
                row.GuideDailyPrice=200m;row.TaxDebt=27m;
                Assert.True((bool)Invoke(window,"ApplyHybridRouteChanges",true)!);
                var oldKey=row.RouteKey;
                var original=Collection(window,"_routes")[0]!;
                var updated=CreateRoute("02","Merkez","12345678901",guidePrice:200m);
                Invoke(window,"RekeyHybridDrafts",original,updated);
                Collection(window,"_routes")[0]=updated;
                Assert.True((bool)Invoke(window,"SavePersistedData",false)!);
                var draft=Assert.Single(new OehsProgramScopedApplicationDataStore(root).Load().HybridPaymentDrafts!);
                Assert.NotEqual(oldKey,draft.RouteKey);
                Assert.Equal(row.RouteKey,draft.RouteKey);
                Assert.Equal(27m,draft.Input.TaxDebt);
                var cache=Field<RouteDraftCache<MainWindow.MultiRoutePaymentDraftRow>>(window,"_multiRouteDraftCache");
                Assert.False(cache.ContainsKey(oldKey));
                Assert.Same(row,cache[draft.RouteKey]);
            }
            finally {temp.Delete(true);}
        });
    }

    private static MainWindow CreateHybridUpdateHarness(string root)
    {
        var window=CreateEditorHarness();
        SetField(window,"_dataStore",new OehsProgramScopedApplicationDataStore(root));
        SetField(window,"_calculationConstantsHistory",new System.Collections.Generic.List<ProgressPaymentCalculationConstantsChange>());
        var contract=ArchiveProgressEditorContextTests.CreateArchive().Contract;
        Collection(window,"_contracts").Add(contract);
        Collection(window,"_contractors").Add(CreateFirm("Test firma","12345678901"));
        var tender=Field<ComboBox>(window,"ProgressTenderListBox");tender.Items.Add(contract);tender.SelectedItem=contract;
        var route=CreateRoute("01","Merkez","12345678901");
        Collection(window,"_routes").Add(route);
        var selector=Field<ListBox>(window,"ProgressRouteComboBox");selector.Items.Add(route);selector.SelectedItems.Add(route);
        var month=new ProgressPaymentMonthOption(new DateOnly(2026,10,1),new DateOnly(2026,10,31));
        var months=Field<ComboBox>(window,"ProgressPeriodMonthComboBox");months.Items.Add(month);months.SelectedItem=month;
        Invoke(window,"UpdateHybridRouteDraftRows");
        return window;
    }

    [Theory]
    [InlineData("WorkDays","18,5",18.5)]
    [InlineData("GuideWorkDays","18,5",18.5)]
    [InlineData("DailyPrice","1.234,5678",1234.5678)]
    [InlineData("GuideDailyPrice","1.234,5678",1234.5678)]
    public void HybridActualCellBinding_ShouldReadTurkishDaysAndPrices(string property,string entered,double expected)
    {
        RunOnSta(() =>
        {
            var layout=LoadLayoutFragment(NamedElement(LoadMainWindowXaml(),"ProgressPanel"));
            var grid=(DataGrid)layout.FindName("MultiRoutePaymentGrid");
            var column=grid.Columns.OfType<DataGridBoundColumn>().Single(column=>(column.Binding as Binding)?.Path?.Path==property);
            var binding=(Binding)column.Binding;
            var row=new MainWindow.MultiRoutePaymentDraftRow();
            binding.Source=row;
            var editor=new TextBox();
            layout.Children.Add(editor); // Inherit the same tr-TR XAML language as real grid editors.
            var expression=BindingOperations.SetBinding(editor,TextBox.TextProperty,binding);
            editor.SetCurrentValue(TextBox.TextProperty,entered);
            expression.UpdateSource();
            Assert.False(Validation.GetHasError(editor));
            Assert.Equal((decimal)expected,(decimal)typeof(MainWindow.MultiRoutePaymentDraftRow).GetProperty(property)!.GetValue(row)!);
        });
    }

    [Theory]
    [InlineData("DailyPrice")]
    [InlineData("DiscountAmount")]
    [InlineData("PriceDifference")]
    [InlineData("GuideDailyPrice")]
    [InlineData("GuideDiscountAmount")]
    [InlineData("GuidePriceDifference")]
    [InlineData("TaxDebt")]
    [InlineData("SocialSecurityDebt")]
    [InlineData("MinimumWageSupport")]
    [InlineData("InspectionPenalty")]
    public void HybridCurrencyCell_ShouldRejectMisgroupedAmountsWithoutChangingValue(string property)
    {
        RunOnSta(() =>
        {
            var layout=LoadLayoutFragment(NamedElement(LoadMainWindowXaml(),"ProgressPanel"));
            var grid=(DataGrid)layout.FindName("MultiRoutePaymentGrid");
            var column=grid.Columns.OfType<DataGridBoundColumn>().Single(column=>(column.Binding as Binding)?.Path?.Path==property);
            var binding=(Binding)column.Binding;
            var row=new MainWindow.MultiRoutePaymentDraftRow();
            binding.Source=row;
            var editor=new TextBox();
            layout.Children.Add(editor);
            var expression=BindingOperations.SetBinding(editor,TextBox.TextProperty,binding);
            editor.SetCurrentValue(TextBox.TextProperty,"12.536,48");
            expression.UpdateSource();
            Assert.False(Validation.GetHasError(editor));
            Assert.Equal(12536.48m,(decimal)typeof(MainWindow.MultiRoutePaymentDraftRow).GetProperty(property)!.GetValue(row)!);
            editor.SetCurrentValue(TextBox.TextProperty,"12.53,48");
            expression.UpdateSource();
            Assert.True(Validation.GetHasError(editor));
            Assert.Equal(12536.48m,(decimal)typeof(MainWindow.MultiRoutePaymentDraftRow).GetProperty(property)!.GetValue(row)!);
        });
    }

    [Fact]
    public void HybridCurrencyCell_RealGridCommitShouldPreserveEnteredKurus()
    {
        RunOnSta(() =>
        {
            var layout=LoadLayoutFragment(NamedElement(LoadMainWindowXaml(),"ProgressPanel"));
            var grid=(DataGrid)layout.FindName("MultiRoutePaymentGrid");
            ((Border)layout.FindName("MultiRouteEntryBorder")).Visibility=Visibility.Visible;
            var controller=CreateEditorHarness();
            grid.Loaded += (_,e) => Invoke(controller,"HybridGrid_Loaded",grid,e);
            var row=new MainWindow.MultiRoutePaymentDraftRow();
            grid.ItemsSource=new[] { row };
            var column=grid.Columns.OfType<DataGridBoundColumn>().Single(c=>(c.Binding as Binding)?.Path?.Path=="PriceDifference");
            var host=new Window{Content=layout,Width=1920,Height=1080,ShowInTaskbar=false};
            try
            {
                host.Show();
                grid.ScrollIntoView(row,column);
                host.UpdateLayout();
                grid.CurrentCell=new DataGridCellInfo(row,column);
                ((DataGridCell)column.GetCellContent(row).Parent).Focus();
                Assert.True(grid.BeginEdit());
                grid.UpdateLayout();
                var editor=Assert.IsType<TextBox>(column.GetCellContent(row));
                editor.Text="12.53,48";
                Assert.False(grid.CommitEdit(DataGridEditingUnit.Cell,true));
                Assert.Equal(0m,row.PriceDifference);
                editor.Text="12.536,";
                Assert.False(grid.CommitEdit(DataGridEditingUnit.Cell,true));
                editor.Text="12.536,48";
                Assert.True(grid.CommitEdit(DataGridEditingUnit.Cell,true));
                Assert.Equal(12536.48m,row.PriceDifference);
                Assert.True(grid.BeginEdit());
                grid.UpdateLayout();
                editor=Assert.IsType<TextBox>(column.GetCellContent(row));
                editor.Text="12.536,48701";
                Assert.False(grid.CommitEdit(DataGridEditingUnit.Cell,true));
                Assert.Equal(12536.48m,row.PriceDifference);
                editor.Text=" 12.536,48701";
                Assert.False(grid.CommitEdit(DataGridEditingUnit.Cell,true));
                editor.Text="12.536,48000";
                Assert.False(grid.CommitEdit(DataGridEditingUnit.Cell,true));
                editor.Text="12.536,4901";
                Assert.True(grid.CommitEdit(DataGridEditingUnit.Cell,true));
                Assert.Equal(12536.4901m,row.PriceDifference);

                row.PriceDifference=12536.487m; // Pre-existing legacy value may be kept unchanged.
                Assert.True(grid.BeginEdit());
                grid.UpdateLayout();
                editor=Assert.IsType<TextBox>(column.GetCellContent(row));
                Assert.Equal("12.536,487",editor.Text);
                Assert.True(grid.CommitEdit(DataGridEditingUnit.Cell,true));
                Assert.Equal(12536.487m,row.PriceDifference);
            }
            finally { host.Close(); }
        });
    }

    [Fact]
    public void RecordPeriodsAndPaymentActions_ShouldUseSharedPeriodControlsAndSingleEditEntry()
    {
        var document=LoadMainWindowXaml();
        foreach(var name in new[]{"HistoryMonthFilter","ArchiveMonthFilter"})
        {
            var control=NamedElement(document,name);
            Assert.Equal("ComboBox",control.Name.LocalName);
            Assert.Equal("DisplayText",(string?)control.Attribute("DisplayMemberPath"));
            Assert.Equal("Hakediş Dönemi",(string?)control.Parent!.Elements().First().Attribute("Text"));
        }
        Assert.DoesNotContain(document.Descendants(),e=>(string?)e.Attribute(XamlNamespace+"Name")=="EditCurrentProgressButton");
        Assert.Equal("HAKEDİŞ HESAPLA/KAYDET",(string?)NamedElement(document,"CalculateProgressButton").Attribute("Content"));
        RunOnSta(()=>
        {
            var window=CreateEditorHarness();
            Invoke(window,"SetProgressEditorMode",false,false);
            Assert.Equal("HAKEDİŞ HESAPLA/KAYDET",Field<Button>(window,"CalculateProgressButton").Content);
            Invoke(window,"SetProgressEditorMode",true,false);
            Assert.Equal("DÜZENLE",Field<Button>(window,"CalculateProgressButton").Content);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RecordPeriods_ShouldClipToContractAndFilterByMonth(bool archive)
    {
        RunOnSta(()=>
        {
            var directory=Directory.CreateTempSubdirectory("oehs-record-periods-");
            try
            {
                var window=CreateEditorHarness();
                SetField(window,"_archiveStore",new ProgressPaymentArchiveStore(directory.FullName));
                var sample=ArchiveProgressEditorContextTests.CreateArchive();
                var contract=sample.Contract with {StartDate=new DateOnly(2026,10,14),EndDate=new DateOnly(2026,12,10)};
                Collection(window,"_contracts").Add(contract);
                var program=Field<ComboBox>(window,"HomeProgramComboBox");
                program.Items.Add(new ComboBoxItem{Tag="SpecialEducation"});program.Items.Add(new ComboBoxItem{Tag="BasicOrSecondary"});program.SelectedIndex=0;
                Invoke(window,"UpdateRecordPeriodOptions",archive);
                var box=Field<ComboBox>(window,archive?"ArchiveMonthFilter":"HistoryMonthFilter");
                var options=box.Items.Cast<ProgressPaymentMonthOption>().ToArray();
                Assert.Equal(3,options.Length);
                Assert.Equal(new DateOnly(2026,10,14),options[0].StartDate);
                Assert.Equal(new DateOnly(2026,12,10),options[2].EndDate);
                Assert.Null(box.SelectedItem);
                box.SelectedIndex=0;
                var filter=Assert.IsType<PaymentFilter>(Invoke(window,"ReadRecordFilter",archive));
                Assert.Equal(new DateOnly(2026,10,31),filter.Month);
                Assert.True(PaymentOperationsService.Matches(sample.ProgressPayment with {PeriodDate=new DateOnly(2026,10,18)},filter));
                Assert.False(PaymentOperationsService.Matches(sample.ProgressPayment with {PeriodDate=new DateOnly(2026,11,18)},filter));
                Invoke(window,"UpdateRecordPeriodOptions",archive);
                Assert.NotNull(box.SelectedItem);
                program.SelectedIndex=1;Invoke(window,"UpdateRecordPeriodOptions",archive);
                Assert.Empty(box.Items.Cast<object>());Assert.Null(box.SelectedItem);
            }
            finally {directory.Delete(true);}
        });
    }

    [Fact]
    public void ContractorPanel_ShouldNotOfferRemovedContactFields()
    {
        var document = LoadMainWindowXaml();
        foreach (var name in new[] { "ContractorAddressTextBox", "ContractorPhoneTextBox", "ContractorEmailTextBox", "ContractorContactTextBox" })
            Assert.DoesNotContain(document.Descendants(), element => (string?)element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == name);
    }

    [Fact]
    public void ContractorEdit_ShouldPreserveLegacyContactDetails()
    {
        RunOnSta(() =>
        {
            var directory = Directory.CreateTempSubdirectory("oehs-contractor-legacy-");
            try
            {
                var root = Path.Combine(directory.FullName, "data");
                var window = CreateHybridUpdateHarness(root);
                Field<ComboBox>(window, "ProgressTenderListBox").Items.Clear();
                Field<ListBox>(window, "ProgressRouteComboBox").Items.Clear();
                Field<ComboBox>(window, "ProgressPeriodMonthComboBox").Items.Clear();
                var firms = Collection(window, "_contractors");
                firms.Clear();
                var legacy = Activator.CreateInstance(typeof(MainWindow).GetNestedType("ContractorDraft", BindingFlags.NonPublic)!,
                    "Eski firma", "12345678901", "Eski adres", "05321234567", "firma@example.com", "TR01", "Yetkili", TransportationProgramType.SpecialEducation, false)!;
                firms.Add(legacy);
                Invoke(window, "EditContractorButton_Click", new Button { Tag = legacy }, new RoutedEventArgs());
                Field<TextBox>(window, "ContractorNameTextBox").Text = "Yeni firma";
                Invoke(window, "SaveContractorButton_Click", window, new RoutedEventArgs());
                var saved = Assert.Single(new OehsProgramScopedApplicationDataStore(root).Load().Contractors!);
                Assert.Equal("Yeni firma", saved.Name);
                Assert.Equal("Eski adres", saved.Address);
                Assert.Equal("05321234567", saved.Phone);
                Assert.Equal("firma@example.com", saved.Email);
                Assert.Equal("Yetkili", saved.ContactPerson);
            }
            finally { directory.Delete(true); }
        });
    }



    [Fact]
    public void ContractorComparison_ShouldIgnorePrefixPresentationOnlyChanges()
    {
        RunOnSta(()=>
        {
            var window=CreateEditorHarness();var firm=ArchiveProgressEditorContextTests.CreateArchive().Contractor;
            var differences=Assert.IsAssignableFrom<System.Collections.Generic.IEnumerable<PaymentDifference>>(
                Invoke(window,"CompareContractorFields",firm with {Phone="+90 532 123 45 67"},firm with {Phone="05321234567"}));
            Assert.Empty(differences);
            var source=ReadMainWindowSource();
            Assert.Contains("CompareContractorFields(originalArchive.Contractor, updatedArchive.Contractor)",MethodBody(source,"SaveArchiveCorrectionSnapshot"),StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RecordPeriods_ShouldKeepLegacyAndDeletedContractRecordsAccessible(bool archive)
    {
        RunOnSta(()=>
        {
            var directory=Directory.CreateTempSubdirectory("oehs-legacy-period-");
            try
            {
                var window=CreateEditorHarness();var store=new ProgressPaymentArchiveStore(directory.FullName);
                SetField(window,"_archiveStore",store);
                var sample=ArchiveProgressEditorContextTests.CreateArchive();
                var contract=sample.Contract with {StartDate=new DateOnly(2026,12,10),EndDate=new DateOnly(2026,10,14)};
                var payment=sample.ProgressPayment with {PeriodDate=new DateOnly(2026,10,18),ContractStartDate=contract.StartDate,ContractEndDate=contract.EndDate};
                if(archive) store.Save(sample with {Contract=contract,ProgressPayment=payment});
                else Collection(window,"_progressHistory").Add(payment);
                var program=Field<ComboBox>(window,"HomeProgramComboBox");program.Items.Add(new ComboBoxItem{Tag="SpecialEducation"});program.SelectedIndex=0;
                Invoke(window,"UpdateRecordPeriodOptions",archive);
                var box=Field<ComboBox>(window,archive?"ArchiveMonthFilter":"HistoryMonthFilter");
                var option=Assert.Single(box.Items.Cast<ProgressPaymentMonthOption>());
                Assert.Equal(2026,option.StartDate.Year);Assert.Equal(10,option.StartDate.Month);
                box.SelectedItem=option;
                Field<TextBox>(window,archive?"ArchiveTenderFilter":"HistoryTenderFilter").Text="OTHER";
                Invoke(window,"UpdateRecordPeriodOptions",archive);
                Assert.Empty(box.Items.Cast<object>());Assert.Null(box.SelectedItem);
            }
            finally {directory.Delete(true);}
        });
    }


    [Fact]
    public void HybridCaptions_ShouldExactlyMatchTheirSourceFields()
    {
        var document=LoadMainWindowXaml();
        var grid=NamedElement(document,"MultiRoutePaymentGrid");
        string Header(string path)=>(string)grid.Descendants().Single(e=>
            ((string?)e.Attribute("Binding"))?.StartsWith("{Binding "+path+",",StringComparison.Ordinal)==true
            || string.Equals((string?)e.Attribute("Binding"),"{Binding "+path+"}",StringComparison.Ordinal)).Attribute("Header")!;
        Assert.Equal((string?)NamedElement(document,"RouteGuideDailyPriceLabel").Attribute("Text"),Header("GuideDailyPrice"));
        Assert.Equal((string?)NamedElement(document,"ProgressGuideWorkDaysLabel").Attribute("Text"),Header("GuideWorkDays"));
        Assert.Equal((string?)NamedElement(document,"WorkDaysLabel").Attribute("Text"),Header("WorkDays"));
        Assert.Equal((string?)NamedElement(document,"NetPayableLabel").Attribute("Text"),Header("NetPayableText"));
    }

    [Fact]
    public void RemovedInputAndPasteFeature_ShouldBeReplacedBySingleExcelExportAction()
    {
        var document=LoadMainWindowXaml();
        var names=document.Descendants().Select(e=>(string?)e.Attribute(XamlNamespace+"Name")).ToArray();
        Assert.DoesNotContain("WorkNameTextBox",names);
        Assert.DoesNotContain("HybridPasteButton",names);
        Assert.DoesNotContain("HybridUndoButton",names);
        var export=NamedElement(document,"HybridExportExcelButton");
        Assert.Equal("EXCEL’E AKTAR",(string?)export.Attribute("Content"));
        Assert.Equal("HybridExportExcelButton_Click",EventName(export,"Click"));
        Assert.Null(NamedElement(document,"MultiRoutePaymentGrid").Attribute("PreviewKeyDown"));
    }

    [Fact]
    public void HomePeriod_ShouldUseClippedContractMonthsAndClearIncompatibleSelection()
    {
        RunOnSta(() =>
        {
            var window=CreateEditorHarness();
            var contract=ArchiveProgressEditorContextTests.CreateArchive().Contract with
            {StartDate=new DateOnly(2026,10,14),EndDate=new DateOnly(2026,12,10)};
            Collection(window,"_contracts").Add(contract);
            var program=Field<ComboBox>(window,"HomeProgramComboBox");
            program.Items.Add(new ComboBoxItem{Tag="SpecialEducation"});program.Items.Add(new ComboBoxItem{Tag="BasicOrSecondary"});program.SelectedIndex=0;
            var tenders=Field<ComboBox>(window,"HomeTenderListBox");tenders.ItemsSource=Collection(window,"_contracts");tenders.SelectedItem=contract;
            Invoke(window,"UpdateHomePeriodOptions");
            var periods=Field<ComboBox>(window,"HomePeriodMonthComboBox");
            var options=periods.Items.Cast<ProgressPaymentMonthOption>().ToArray();
            Assert.Equal(3,options.Length);
            Assert.Equal(new DateOnly(2026,10,14),options[0].StartDate);
            Assert.Equal(new DateOnly(2026,10,31),options[0].EndDate);
            Assert.Equal(new DateOnly(2026,12,10),options[2].EndDate);
            Assert.Null(periods.SelectedItem);
            periods.SelectedIndex=0;Invoke(window,"UpdateHomePeriodOptions");
            Assert.Equal(new DateOnly(2026,10,14),Assert.IsType<ProgressPaymentMonthOption>(periods.SelectedItem).StartDate);
            program.SelectedIndex=1;Invoke(window,"UpdateHomePeriodOptions");
            Assert.Empty(periods.Items.Cast<object>());
            Assert.Null(periods.SelectedItem);
        });
    }

    [Fact]
    public void HybridGridExcel_ShouldExportCurrentVisibleOrderAndKeepUnsavedChanges()
    {
        RunOnSta(() =>
        {
            var temp=Directory.CreateTempSubdirectory("oehs-current-grid-");
            try
            {
                var root=Path.Combine(temp.FullName,"data");
                var window=CreateHybridUpdateHarness(root);
                var rows=Field<System.Collections.ObjectModel.ObservableCollection<MainWindow.MultiRoutePaymentDraftRow>>(window,"_multiRoutePaymentRows");
                var row=Assert.Single(rows);row.WorkDays=18.5m;row.GuideDailyPrice=1234.5678m;row.RouteName="Düzenlenmiş hat";
                var grid=Field<DataGrid>(window,"MultiRoutePaymentGrid");grid.AutoGenerateColumns=false;grid.CanUserAddRows=false;grid.ItemsSource=rows;
                var name=new DataGridTextColumn{Header="Hattın Adı",Binding=new Binding(nameof(row.RouteName)){Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.LostFocus}};
                var price=new DataGridTextColumn{Header="Rehber Personel Günlük Fiyat",Binding=new Binding(nameof(row.GuideDailyPrice))};
                var days=new DataGridTextColumn{Header="Döneme Esas THGS",Binding=new Binding(nameof(row.WorkDays))};
                grid.Columns.Add(name);grid.Columns.Add(price);grid.Columns.Add(days);
                grid.Columns.Add(new DataGridTextColumn{Header="Gizli",Binding=new Binding(nameof(row.TaxDebt)),Visibility=Visibility.Collapsed});
                price.DisplayIndex=0;
                var host=new Window{Content=grid,Width=700,Height=300,ShowInTaskbar=false};
                try
                {
                host.Show();
                ArrangeLayout(grid,700,300);
                grid.SelectedItem=row;
                grid.CurrentCell=new DataGridCellInfo(row,name);
                ((DataGridCell)name.GetCellContent(row).Parent).Focus();
                Assert.True(grid.BeginEdit());
                grid.UpdateLayout();
                var editor=Assert.Single(VisualDescendants<TextBox>(grid));
                editor.Text="Düzenlenmiş hat — hücre düzenlemesi";
                Assert.Equal("Düzenlenmiş hat",row.RouteName);
                var path=Path.Combine(temp.FullName,"grid.xlsx");
                Assert.True((bool)Invoke(window,"SaveHybridTableExcel",path)!);
                using var zip=System.IO.Compression.ZipFile.OpenRead(path);
                using var stream=zip.GetEntry("xl/worksheets/sheet1.xml")!.Open();
                var xml=XDocument.Load(stream);XNamespace ns="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
                Assert.Equal(ExpectedExportHeaders,xml.Descendants(ns+"row").First().Descendants(ns+"t").Take(ExpectedExportHeaders.Length).Select(e=>e.Value).ToArray());
                var cells=xml.Descendants(ns+"row").Last().Elements(ns+"c").ToArray();
                Assert.Equal(22,cells.Length);
                Assert.Equal("1234.5678",cells[0].Element(ns+"v")!.Value);
                Assert.Equal("Düzenlenmiş hat — hücre düzenlemesi",cells[1].Descendants(ns+"t").Single().Value);
                Assert.Equal("18.5",cells[2].Element(ns+"v")!.Value);
                Assert.True(row.HasChanges);
                Assert.True(Field<Button>(window,"UpdateHybridRoutesButton").IsEnabled);
                Assert.Empty(new OehsProgramScopedApplicationDataStore(root).Load().Routes);
                }
                finally {host.Close();}
            }
            finally {temp.Delete(true);}
        });
    }

    [Fact]
    public void MidnightFinance_ShouldKeepEightCardsWithVectorSymbolsAndPorcelainNetCard()
    {
        RunOnSta(() =>
        {
            var home=NamedElement(LoadMainWindowXaml(),"HomePanel");
            Assert.DoesNotContain(home.Descendants(),e=>(string?)e.Attribute(XamlNamespace+"Name")=="HomePendingButton");
            var cards=home.Descendants().Single(e=>(string?)e.Attribute(XamlNamespace+"Name")=="HomeFinanceCards");
            Assert.Equal("4",(string?)cards.Attribute("Columns"));
            Assert.Equal("2",(string?)cards.Attribute("Rows"));
            Assert.Equal(8,cards.Elements().Count());
            var layout=LoadLayoutFragment(home);
            foreach(var name in HomeFinancialCardNames)
            {
                var icon=(ContentControl)layout.FindName("Home"+name+"Icon");
                Assert.NotNull(icon);
                Assert.IsAssignableFrom<Geometry>(icon.Content);
                icon.ApplyTemplate();
                Assert.NotEmpty(VisualDescendants<System.Windows.Shapes.Path>(icon));
            }
            var net=(Border)layout.FindName("HomeNetCard");
            Assert.Equal(Color.FromRgb(241,245,249),Assert.IsType<SolidColorBrush>(net.Background).Color);
        });
    }

    [Fact]
    public void GoldFinance_ShouldFollowProgramTenderMonthAndIgnoreListSearch()
    {
        RunOnSta(() =>
        {
            var directory=Directory.CreateTempSubdirectory("oehs-finance-");
            try
            {
                var window=CreateEditorHarness();
                SetField(window,"_archiveStore",new ProgressPaymentArchiveStore(directory.FullName));
                SetField(window,"_calculationConstantsHistory",new System.Collections.Generic.List<ProgressPaymentCalculationConstantsChange>());
                var c=ArchiveProgressEditorContextTests.CreateArchive().Contract;
                var other=c with {TenderNumber="IHALE-02"};
                var basic=c with {ProgramType=TransportationProgramType.BasicOrSecondary};
                foreach(var item in new[]{c,other,basic}) Collection(window,"_contracts").Add(item);
                Collection(window,"_routes").Add(CreateRoute("01","Merkez","12345678901"));
                Collection(window,"_routes").Add(CreateRoute("02","Kuzey","12345678901"));
                Collection(window,"_routes").Add(CreateRoute("03","Güney","22222222222",tender:"IHALE-02"));
                Collection(window,"_routes").Add(CreateRoute("04","Temel","12345678901",TransportationProgramType.BasicOrSecondary));
                Collection(window,"_contractors").Add(CreateFirm("Firma 1","12345678901"));
                Collection(window,"_contractors").Add(CreateFirm("Firma 2","22222222222"));
                var sample=ArchiveProgressEditorContextTests.CreateArchive().ProgressPayment;
                var input=sample.Input with {WorkDays=18,DailyPrice=1234.5678m,PriceDifference=120,TaxDebt=25,GuideWorkDays=18,GuideDailyPrice=200};
                var p=sample with {Input=input,Calculation=ProgressPaymentCalculator.Calculate(input),TenderNumber=c.TenderNumber,ContractNumber=c.ContractNumber,ContractorTaxOrIdentityNumber="12345678901",PeriodDate=new DateOnly(2026,10,1)};
                var p2=p with {Id=Guid.NewGuid(),RouteNumber="03",TenderNumber=other.TenderNumber,ContractorTaxOrIdentityNumber="22222222222"};
                var pb=p with {Id=Guid.NewGuid(),RouteNumber="04",Input=input with {ProgramType=TransportationProgramType.BasicOrSecondary}};
                pb=pb with {Calculation=ProgressPaymentCalculator.Calculate(pb.Input)};
                foreach(var item in new[]{p,p2,pb,p with {Id=Guid.NewGuid(),CreatedAt=p.CreatedAt.AddDays(-1)},p with {Id=Guid.NewGuid(),PeriodDate=new DateOnly(2026,9,1)}}) Collection(window,"_progressHistory").Add(item);
                var program=Field<ComboBox>(window,"HomeProgramComboBox");
                program.Items.Add(new ComboBoxItem{Tag="SpecialEducation"});program.Items.Add(new ComboBoxItem{Tag="BasicOrSecondary"});program.SelectedIndex=0;
                SelectHomePeriod(window,new DateOnly(2026,10,1));
                SetField(window,"_operationsReady",true);
                string Money(decimal value)=>value.ToString("N2",CultureInfo.GetCultureInfo("tr-TR"))+" ₺";
                string Text(string key)=>Field<TextBlock>(window,"Home"+key+"Value").Text;
                Invoke(window,"RefreshHomeOperations");
                Assert.Equal(Money(2*p.Calculation.NetPayable),Text("Net"));
                Assert.Equal(Money(2*p.Calculation.GrossAmount),Text("Gross"));
                Assert.Equal(Money(2*p.Calculation.TotalDeductions),Text("Deductions"));
                Assert.Equal(Money(2*p.Calculation.Vat),Text("Vat"));
                Assert.Equal(Money(2*p.Calculation.ServiceAmount),Text("Transport"));
                Assert.Equal(Money(2*p.Calculation.CombinedPriceDifferenceExcludingVat),Text("Difference"));
                Assert.Equal("2 / 3",Text("Routes"));Assert.Equal("2 firma",Text("Firms"));
                Field<TextBox>(window,"HomeSearchBox").Text="bulunmayan";Invoke(window,"RefreshHomeOperations");
                Assert.Equal(Money(2*p.Calculation.NetPayable),Text("Net"));
                Field<ComboBox>(window,"HomeTenderListBox").SelectedItem=c;Invoke(window,"RefreshHomeOperations");
                Assert.Equal(Money(p.Calculation.NetPayable),Text("Net"));Assert.Equal("1 / 2",Text("Routes"));Assert.Equal("1 firma",Text("Firms"));
                program.SelectedIndex=1;Invoke(window,"RefreshHomeOperations");
                Assert.Equal(Money(pb.Calculation.NetPayable),Text("Net"));Assert.Equal("1 / 1",Text("Routes"));
                SelectHomePeriod(window,new DateOnly(2026,11,1));Invoke(window,"RefreshHomeOperations");
                Assert.Equal(Money(0),Text("Net"));Assert.Equal("0 / 1",Text("Routes"));
                program.SelectedIndex=-1;Invoke(window,"RefreshHomeOperations");
                Assert.Equal(Money(0),Text("Net"));Assert.Equal("0 / 0",Text("Routes"));Assert.Equal("0 firma",Text("Firms"));
            }
            finally {directory.Delete(true);}
        });
    }

    [Fact]
    public void HybridLayout_ShouldKeepPerRouteGridAndCalculateActionReachable()
    {
        RunOnSta(() =>
        {
            foreach(var size in new[]{(1024d,600d),(1366d,768d),(1920d,1080d)})
            {
                var layout=LoadLayoutFragment(NamedElement(LoadMainWindowXaml(),"ProgressPanel"));
                ((Grid)layout.FindName("SinglePaymentFieldsGrid")).Visibility=Visibility.Collapsed;
                ((Border)layout.FindName("MultiRouteEntryBorder")).Visibility=Visibility.Visible;
                var grid=(DataGrid)layout.FindName("MultiRoutePaymentGrid");
                var first=new MainWindow.MultiRoutePaymentDraftRow{RouteNumber="01",RouteName="Merkez İlkokulu",WorkDays=22,DailyPrice=19539.1608m,PriceDifference=150};
                var second=new MainWindow.MultiRoutePaymentDraftRow{RouteNumber="02",RouteName="Kuzey İlkokulu",WorkDays=18,DailyPrice=19539.1608m,PriceDifference=30,TaxDebt=25};
                first.Configure(TransportationProgramType.SpecialEducation,100000m,false,ProgressPaymentCalculationConstants.Defaults);
                second.Configure(TransportationProgramType.SpecialEducation,100000m,false,ProgressPaymentCalculationConstants.Defaults);
                var window=CreateEditorHarness();
                foreach(var field in typeof(MainWindow).GetFields(BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public))
                    if(layout.FindName(field.Name) is {} control && field.FieldType.IsInstanceOfType(control)) field.SetValue(window,control);
                var rows=Field<System.Collections.ObjectModel.ObservableCollection<MainWindow.MultiRoutePaymentDraftRow>>(window,"_multiRoutePaymentRows");
                rows.Add(first);rows.Add(second);grid.ItemsSource=rows;
                Invoke(window,"RefreshHybridTotals");
                ((TextBlock)layout.FindName("HybridSelectionSummaryTextBlock")).Text="2 seçili hat · Her satır ayrı hesaplanır · Henüz kaydedilmedi";
                ArrangeLayout(layout,size.Item1-215-38,size.Item2-60-38);
                var outer=VisualDescendants<ScrollViewer>(layout).First();
                var bounds=grid.TransformToAncestor(outer).TransformBounds(new Rect(grid.RenderSize));
                Assert.InRange(bounds.Right,1,outer.ActualWidth+1);
                var tableScroll=VisualDescendants<ScrollViewer>(grid).First();
                Assert.True(tableScroll.ScrollableWidth>0,"Detailed row fields must remain reachable by horizontal scrolling.");
                outer.ScrollToBottom();layout.UpdateLayout();
                var calculate=(Button)layout.FindName("CalculateProgressButton");
                var action=calculate.TransformToAncestor(outer).TransformBounds(new Rect(calculate.RenderSize));
                Assert.InRange(action.Bottom,1,outer.ActualHeight+1);
                // CI visual evidence at 100/125/150 percent render density. This
                // verifies WPF rasterization at these densities, NOT physical
                // PerMonitorV2 monitor switching or pixel-perfect reference matching.
                var preview=Environment.GetEnvironmentVariable("OEHS_HYBRID_PREVIEW");
                if(!string.IsNullOrWhiteSpace(preview))
                {
                    grid.BringIntoView();layout.UpdateLayout();
                    Directory.CreateDirectory(preview);
                    foreach(var dpi in HybridRenderDpis)
                    {
                        var scale=dpi/96d;
                        var width=(int)Math.Ceiling(layout.ActualWidth*scale);
                        var height=(int)Math.Ceiling(layout.ActualHeight*scale);
                        var image=new System.Windows.Media.Imaging.RenderTargetBitmap(width,height,dpi,dpi,PixelFormats.Pbgra32);
                        image.Render(layout);
                        Assert.Equal(width,image.PixelWidth);
                        Assert.Equal(height,image.PixelHeight);
                        var png=new System.Windows.Media.Imaging.PngBitmapEncoder();
                        png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
                        var filename=$"hybrid-{(int)size.Item1}x{(int)size.Item2}-{(int)(scale*100)}pct.png";
                        using var file=File.Create(Path.Combine(preview,filename));
                        png.Save(file);
                    }
                }
            }
        });
    }

    [Fact]
    public void HybridCalculation_ShouldPersistRoutePeriodDaysWithoutImportingAnnualGuideDays()
    {
        RunOnSta(() =>
        {
            foreach(var count in SelectedRouteCounts)
            {
                var temp=Directory.CreateTempSubdirectory("oehs-hybrid-save-");
                try
                {
                    var window=CreateEditorHarness();
                    SetField(window,"_dataStore",new OehsProgramScopedApplicationDataStore(Path.Combine(temp.FullName,"data")));
                    SetField(window,"_archiveStore",new ProgressPaymentArchiveStore(Path.Combine(temp.FullName,"archives")));
                    SetField(window,"_calculationConstantsHistory",new System.Collections.Generic.List<ProgressPaymentCalculationConstantsChange>());
                    var contract=ArchiveProgressEditorContextTests.CreateArchive().Contract
                        with { ContractorName="Sözleşme Ana Firması" };
                    Collection(window,"_contracts").Add(contract);
                    Collection(window,"_contractors").Add(CreateFirm("Seçilen Hat Firması","12345678901"));
                    Field<ComboBox>(window,"ProgressTenderListBox").Items.Add(contract);
                    Field<ComboBox>(window,"ProgressTenderListBox").SelectedItem=contract;
                    var routes=Field<ListBox>(window,"ProgressRouteComboBox");
                    for(var index=0;index<count;index++)
                    {
                        var route=CreateRoute((index+1).ToString("00",CultureInfo.InvariantCulture),"Hat "+index,"12345678901", transportDays: 16.5m + index);
                        routes.Items.Add(route);Collection(window,"_routes").Add(route);routes.SelectedItems.Add(route);
                    }
                    var period=new ProgressPaymentMonthOption(new DateOnly(2026,10,1),new DateOnly(2026,10,31));
                    Field<ComboBox>(window,"ProgressPeriodMonthComboBox").Items.Add(period);
                    Field<ComboBox>(window,"ProgressPeriodMonthComboBox").SelectedItem=period;
                    Invoke(window,"UpdateHybridRouteDraftRows");
                    var rows=Field<System.Collections.ObjectModel.ObservableCollection<MainWindow.MultiRoutePaymentDraftRow>>(window,"_multiRoutePaymentRows");
                    for(var index=0;index<count;index++)
                    {Assert.Equal(0m,rows[index].WorkDays);rows[index].WorkDays=16.5m+index;Assert.Equal(0m,rows[index].GuideWorkDays);rows[index].PriceDifference=100+index*50;rows[index].TaxDebt=3+index*4;}
                    foreach(var name in InvalidSharedInputNames)
                        Field<TextBox>(window,name).Text="geçersiz ortak alan";
                    Invoke(window,"CalculateProgressButton_Click",window,new RoutedEventArgs());
                    var saved=new OehsProgramScopedApplicationDataStore(Path.Combine(temp.FullName,"data")).Load();
                    Assert.Equal(count,saved.ProgressPayments!.Count);
                    // A selected firm's route must not inherit a different tender-level
                    // contractor name in the persisted batch identity.
                    foreach(var payment in saved.ProgressPayments)
                    {
                        Assert.Contains("Seçilen Hat Firması", payment.BatchDisplayName ?? string.Empty,
                            StringComparison.Ordinal);
                        Assert.DoesNotContain("Sözleşme Ana Firması", payment.BatchDisplayName ?? string.Empty,
                            StringComparison.Ordinal);
                        var index=int.Parse(payment.RouteNumber!,CultureInfo.InvariantCulture)-1;
                        Assert.Equal(16.5m+index,payment.Input.WorkDays);
                        Assert.Equal(0m,payment.Input.GuideWorkDays);
                        Assert.Equal((16.5m+index)*1000m,payment.Calculation.ServiceAmount);
                        Assert.Equal(100+index*50,payment.Input.PriceDifference);
                        Assert.Equal(3+index*4,payment.Input.TaxDebt);
                        Assert.Equal(ProgressPaymentCalculator.Calculate(payment.Input).NetPayable,payment.Calculation.NetPayable);
                    }
                }
                finally {temp.Delete(true);}
            }
        });
    }

    [Fact]
    public void HybridCalculation_ShouldPersistEachRoutesContractAmountAndDate()
    {
        RunOnSta(() =>
        {
            var temp=Directory.CreateTempSubdirectory("oehs-route-contracts-");
            try
            {
                foreach(var program in new[]{TransportationProgramType.SpecialEducation,TransportationProgramType.BasicOrSecondary})
                {
                var root=Path.Combine(temp.FullName,program.ToString());
                var window=CreateEditorHarness();
                SetField(window,"_dataStore",new OehsProgramScopedApplicationDataStore(root));
                SetField(window,"_archiveStore",new ProgressPaymentArchiveStore(Path.Combine(temp.FullName,"archives")));
                SetField(window,"_calculationConstantsHistory",new System.Collections.Generic.List<ProgressPaymentCalculationConstantsChange>());
                var contract=ArchiveProgressEditorContextTests.CreateArchive().Contract with { ProgramType=program };
                Collection(window,"_contracts").Add(contract);
                Collection(window,"_contractors").Add(Activator.CreateInstance(typeof(MainWindow).GetNestedType("ContractorDraft",BindingFlags.NonPublic)!,"Firma","12345678901","","","","","",program,false)!);
                var tender=Field<ComboBox>(window,"ProgressTenderListBox");tender.Items.Add(contract);tender.SelectedItem=contract;
                var selector=Field<ListBox>(window,"ProgressRouteComboBox");
                var routeType=typeof(MainWindow).GetNestedType("RouteDraft",BindingFlags.NonPublic)!;
                for(var i=0;i<2;i++)
                {
                    var route=Activator.CreateInstance(routeType,(i+1).ToString("00",CultureInfo.InvariantCulture),"Hat",1000m+i*500m,0m,20m,"IHALE-01","12345678901","Firma","SOZ-01",contract.ProgramType,100000m+i*50000m,new DateOnly(2026,1,1+i))!;
                    Collection(window,"_routes").Add(route);selector.Items.Add(route);selector.SelectedItems.Add(route);
                }
                var month=Field<ComboBox>(window,"ProgressPeriodMonthComboBox");
                var period=new ProgressPaymentMonthOption(new DateOnly(2026,10,1),new DateOnly(2026,10,31));month.Items.Add(period);month.SelectedItem=period;
                Invoke(window,"UpdateHybridRouteDraftRows");
                Invoke(window,"CalculateProgressButton_Click",window,new RoutedEventArgs());
                var payments=new OehsProgramScopedApplicationDataStore(root).Load().ProgressPayments!.OrderBy(x=>x.RouteNumber).ToArray();
                Assert.Equal(2,payments.Length);
                Assert.Equal(1000m,payments[0].Input.DailyPrice);Assert.Equal(100000m,payments[0].Input.ContractAmount);Assert.Equal(new DateOnly(2026,1,1),payments[0].ContractDate);
                Assert.Equal(1500m,payments[1].Input.DailyPrice);Assert.Equal(150000m,payments[1].Input.ContractAmount);Assert.Equal(new DateOnly(2026,1,2),payments[1].ContractDate);
                for(var i=0;i<payments.Length;i++)
                {
                    var context=Assert.IsType<OehsOfficialDocumentContext>(Invoke(window,"BuildOfficialDocumentContextForPayment",payments[i]));
                    var report=OehsOfficialDocumentFactory.BuildSeparate(context).Single(x=>x.Kind==OehsOfficialDocumentKind.HizmetIsleriHakedisRaporu);
                    var path=Path.Combine(temp.FullName,$"{program}-{i}.pdf");
                    PdfVectorWriter.Save(report.Document,path);
                    using var pdf=UglyToad.PdfPig.PdfDocument.Open(path);
                    var page=pdf.GetPage(1);
                    string InRect(Rect rect)=>string.Concat(page.Letters.Where(letter=>
                        letter.BoundingBox.Left>=rect.Left*0.75-0.5 && letter.BoundingBox.Right<=rect.Right*0.75+0.5 &&
                        letter.BoundingBox.Bottom>=page.Height-rect.Bottom*0.75-0.5 && letter.BoundingBox.Top<=page.Height-rect.Top*0.75+0.5).Select(letter=>letter.Value));
                    Assert.Contains("250.000,00",InRect(new Rect(398,268,297,22)),StringComparison.Ordinal);
                    Assert.Contains("01.01.2026",InRect(new Rect(398,312,297,22)),StringComparison.Ordinal);
                }
                }
            }
            finally {temp.Delete(true);}
        });
    }

    [Fact]
    public void HybridCalculation_ShouldPersistSingleAndMultipleRowsIgnoringInvalidSharedInputs()
    {
        RunOnSta(() =>
        {
            foreach(var count in SelectedRouteCounts)
            {
                var temp=Directory.CreateTempSubdirectory("oehs-hybrid-save-");
                try
                {
                    var window=CreateEditorHarness();
                    SetField(window,"_dataStore",new OehsProgramScopedApplicationDataStore(Path.Combine(temp.FullName,"data")));
                    SetField(window,"_archiveStore",new ProgressPaymentArchiveStore(Path.Combine(temp.FullName,"archives")));
                    SetField(window,"_calculationConstantsHistory",new System.Collections.Generic.List<ProgressPaymentCalculationConstantsChange>());
                    var contract=ArchiveProgressEditorContextTests.CreateArchive().Contract
                        with { ContractorName="Sözleşme Ana Firması" };
                    Collection(window,"_contracts").Add(contract);
                    Collection(window,"_contractors").Add(CreateFirm("Seçilen Hat Firması","12345678901"));
                    Field<ComboBox>(window,"ProgressTenderListBox").Items.Add(contract);
                    Field<ComboBox>(window,"ProgressTenderListBox").SelectedItem=contract;
                    var routes=Field<ListBox>(window,"ProgressRouteComboBox");
                    for(var index=0;index<count;index++)
                    {
                        var route=CreateRoute((index+1).ToString("00",CultureInfo.InvariantCulture),"Hat "+index,"12345678901");
                        routes.Items.Add(route);Collection(window,"_routes").Add(route);routes.SelectedItems.Add(route);
                    }
                    var period=new ProgressPaymentMonthOption(new DateOnly(2026,10,1),new DateOnly(2026,10,31));
                    Field<ComboBox>(window,"ProgressPeriodMonthComboBox").Items.Add(period);
                    Field<ComboBox>(window,"ProgressPeriodMonthComboBox").SelectedItem=period;
                    Invoke(window,"UpdateHybridRouteDraftRows");
                    var rows=Field<System.Collections.ObjectModel.ObservableCollection<MainWindow.MultiRoutePaymentDraftRow>>(window,"_multiRoutePaymentRows");
                    for(var index=0;index<count;index++)
                    {rows[index].WorkDays=16+index;rows[index].PriceDifference=100+index*50;rows[index].TaxDebt=3+index*4;}
                    foreach(var name in InvalidSharedInputNames)
                        Field<TextBox>(window,name).Text="geçersiz ortak alan";
                    Invoke(window,"CalculateProgressButton_Click",window,new RoutedEventArgs());
                    var saved=new OehsProgramScopedApplicationDataStore(Path.Combine(temp.FullName,"data")).Load();
                    Assert.Equal(count,saved.ProgressPayments!.Count);
                    // A selected firm's route must not inherit a different tender-level
                    // contractor name in the persisted batch identity.
                    foreach(var payment in saved.ProgressPayments)
                    {
                        Assert.Contains("Seçilen Hat Firması", payment.BatchDisplayName ?? string.Empty,
                            StringComparison.Ordinal);
                        Assert.DoesNotContain("Sözleşme Ana Firması", payment.BatchDisplayName ?? string.Empty,
                            StringComparison.Ordinal);
                        var index=int.Parse(payment.RouteNumber!,CultureInfo.InvariantCulture)-1;
                        Assert.Equal(16+index,payment.Input.WorkDays);
                        Assert.Equal(100+index*50,payment.Input.PriceDifference);
                        Assert.Equal(3+index*4,payment.Input.TaxDebt);
                        Assert.Equal(ProgressPaymentCalculator.Calculate(payment.Input).NetPayable,payment.Calculation.NetPayable);
                    }
                }
                finally {temp.Delete(true);}
            }
        });
    }

    [Fact]
    public void MonthlyDraft_ShouldResetWhenNewMonthFollowsTransientEmptySelection()
    {
        var afterTemporaryClear = MainWindow.ResolveMonthlyDraftPeriodTransition("2026-10", null);
        Assert.Equal("2026-10", afterTemporaryClear.LastMonthKey);
        Assert.False(afterTemporaryClear.ResetMonthlyDraft);

        var afterSelectingNovember = MainWindow.ResolveMonthlyDraftPeriodTransition(
            afterTemporaryClear.LastMonthKey, "2026-11");
        Assert.Equal("2026-11", afterSelectingNovember.LastMonthKey);
        Assert.True(afterSelectingNovember.ResetMonthlyDraft);

        var sameMonth = MainWindow.ResolveMonthlyDraftPeriodTransition("2026-11", "2026-11");
        Assert.False(sameMonth.ResetMonthlyDraft);
        var firstMonth = MainWindow.ResolveMonthlyDraftPeriodTransition(null, "2026-11");
        Assert.False(firstMonth.ResetMonthlyDraft);
    }

    [Fact]
    public void HybridSave_ShouldRejectDaysBeyondClippedContractMonthAndRetainZeroRecords()
    {
        RunOnSta(() =>
        {
            var temp = Directory.CreateTempSubdirectory("oehs-workday-guard-");
            try
            {
                var window = CreateEditorHarness();
                var root = Path.Combine(temp.FullName, "data");
                SetField(window, "_dataStore", new OehsProgramScopedApplicationDataStore(root));
                SetField(window, "_calculationConstantsHistory",
                    new System.Collections.Generic.List<ProgressPaymentCalculationConstantsChange>());
                var contract = ArchiveProgressEditorContextTests.CreateArchive().Contract with
                {
                    StartDate = new DateOnly(2026, 10, 14),
                    EndDate = new DateOnly(2026, 12, 31)
                };
                Collection(window, "_contracts").Add(contract);
                Collection(window, "_contractors").Add(CreateFirm("Seçilen Firma", "12345678901"));
                var tender = Field<ComboBox>(window, "ProgressTenderListBox");
                tender.Items.Add(contract);
                tender.SelectedItem = contract;
                var routes = Field<ListBox>(window, "ProgressRouteComboBox");
                var route = CreateRoute("01", "Hat 01", "12345678901");
                routes.Items.Add(route);
                Collection(window, "_routes").Add(route);
                routes.SelectedItems.Add(route);
                var period = new ProgressPaymentMonthOption(
                    new DateOnly(2026, 10, 14), new DateOnly(2026, 10, 31));
                var month = Field<ComboBox>(window, "ProgressPeriodMonthComboBox");
                month.Items.Add(period);
                month.SelectedItem = period;
                Invoke(window, "UpdateHybridRouteDraftRows");
                var rows = Field<System.Collections.ObjectModel.ObservableCollection<MainWindow.MultiRoutePaymentDraftRow>>(
                    window, "_multiRoutePaymentRows");
                Assert.Single(rows).WorkDays = 19m; // Only 18 calendar days remain in October
                Invoke(window, "CalculateProgressButton_Click", window, new RoutedEventArgs());
                Assert.Empty(new OehsProgramScopedApplicationDataStore(root).Load().ProgressPayments!);
                Assert.Contains("18 takvim",
                    Field<TextBlock>(window, "ProgressValidationMessageTextBlock").Text,
                    StringComparison.Ordinal);
            }
            finally { temp.Delete(true); }
        });
    }

    [Fact]
    public void HybridSaveButton_ShouldWarnThatCalculationPersistsARecord()
    {
        var xaml = LoadMainWindowXaml();
        var button = NamedElement(xaml, "CalculateProgressButton");
        Assert.Equal("HAKEDİŞ HESAPLA/KAYDET", (string?)button.Attribute("Content"));
        Assert.Equal("CalculateProgressButton_Click", EventName(button, "Click"));
        var grid = NamedElement(xaml, "MultiRoutePaymentGrid");
    }

    [Fact]
    public void HybridPreview_ShouldNotifyAndCalculateEachRoutesOwnValues()
    {
        var first=new MainWindow.MultiRoutePaymentDraftRow{WorkDays=22,DailyPrice=100,PriceDifference=50,TaxDebt=10};
        var second=new MainWindow.MultiRoutePaymentDraftRow{WorkDays=18,DailyPrice=120,PriceDifference=0,TaxDebt=20};
        var configure=first.GetType().GetMethod("Configure");Assert.NotNull(configure);
        foreach(var row in new[]{first,second}) configure!.Invoke(row,new object[]{TransportationProgramType.BasicOrSecondary,100000m,true,ProgressPaymentCalculationConstants.Defaults});
        var net=first.GetType().GetProperty("NetPayableText");Assert.NotNull(net);
        var notifications=new System.Collections.Generic.List<string?>();first.PropertyChanged+=(_,e)=>notifications.Add(e.PropertyName);
        var old=net!.GetValue(first);var other=net.GetValue(second);
        first.WorkDays=21;
        Assert.NotEqual(old,net.GetValue(first));Assert.Equal(other,net.GetValue(second));
        Assert.Contains("NetPayableText",notifications);
    }

    [Fact]
    public void Navigation_ShouldPlaceSignatoriesAndOutputsInRequestedGroupWithoutDocumentShortcuts()
    {
        var doc=LoadMainWindowXaml();
        var menu=NamedElement(doc,"NavContract").Parent!;
        var names=menu.Elements().Where(e=>e.Name.LocalName=="Button").Select(e=>(string?)e.Attribute(XamlNamespace+"Name")).ToArray();
        Assert.Equal(Array.IndexOf(names,"NavContract")+1,Array.IndexOf(names,"NavSignatories"));
        Assert.Equal(Array.IndexOf(names,"NavSignatories")+1,Array.IndexOf(names,"NavContractors"));
        Assert.Equal(Array.IndexOf(names,"NavProgress")+1,Array.IndexOf(names,"NavReports"));
        Assert.DoesNotContain("NavSummary",names);Assert.DoesNotContain("NavAcceptance",names);
        Assert.Equal("NavReports",(string?)NamedElement(doc,"NavProgress").ElementsAfterSelf().First().Attribute(XamlNamespace+"Name"));
        var signatory=NamedElement(doc,"SignatoryPreparerNameTextBox");
        Assert.Contains(NamedElement(doc,"SignatoriesPanel"),signatory.Ancestors());
        Assert.DoesNotContain(NamedElement(doc,"SettingsPanel"),signatory.Ancestors());
    }

    [Theory]
    [InlineData(TransportationProgramType.SpecialEducation)]
    [InlineData(TransportationProgramType.BasicOrSecondary)]
    public void HybridRows_ShouldStartWithNoPeriodDaysAndKeepEditedDraft(TransportationProgramType program)
    {
        RunOnSta(() =>
        {
            var window = CreateEditorHarness();
            var routes = Field<ListBox>(window, "ProgressRouteComboBox");
            var firstRoute = CreateRoute("01", "Merkez", "12345678901", program, transportDays: 18.5m);
            var secondRoute = CreateRoute("02", "Kuzey", "12345678901", program, transportDays: 12m);
            routes.Items.Add(firstRoute);
            routes.Items.Add(secondRoute);
            routes.SelectedItems.Add(firstRoute);
            Invoke(window, "UpdateHybridRouteDraftRows");
            var rows = Field<System.Collections.ObjectModel.ObservableCollection<MainWindow.MultiRoutePaymentDraftRow>>(window, "_multiRoutePaymentRows");
            var first = Assert.Single(rows);
            Assert.Equal(0m, first.WorkDays);
            Assert.Equal(0m, first.GuideWorkDays);
            Assert.Equal(0m, first.Preview!.ServiceAmount);
            first.WorkDays = 17m;
            first.GuideWorkDays = program == TransportationProgramType.SpecialEducation ? 15m : 0m;
            routes.SelectedItems.Add(secondRoute);
            Invoke(window, "UpdateHybridRouteDraftRows");
            Assert.Equal(0m, rows.Single(row => row.RouteNumber == "02").WorkDays);
            routes.SelectedItems.Remove(firstRoute);
            Invoke(window, "UpdateHybridRouteDraftRows");
            routes.SelectedItems.Add(firstRoute);
            Invoke(window, "UpdateHybridRouteDraftRows");
            Assert.Same(first, rows.Single(row => row.RouteNumber == "01"));
            Assert.Equal(17m, first.WorkDays);
            Assert.Equal(program == TransportationProgramType.SpecialEducation ? 15m : 0m, first.CreateInput().GuideWorkDays);
        });
    }

    [Fact]
    public void HybridRows_ShouldKeepSingleAndMultipleRouteEditsWithoutSharedFieldOverwrite()
    {
        RunOnSta(() =>
        {
            var window=CreateEditorHarness();
            var routes=Field<ListBox>(window,"ProgressRouteComboBox");
            var a=CreateRoute("01","Merkez","12345678901");var b=CreateRoute("02","Kuzey","12345678901");
            routes.Items.Add(a);routes.Items.Add(b);Collection(window,"_routes").Add(a);Collection(window,"_routes").Add(b);
            routes.SelectedItems.Add(a);
            SetField(window,"_lastProgressDefaultsRouteKey",OehsIdentityService.BuildRouteKey("IHALE-01","01","12345678901","SOZ-01",TransportationProgramType.SpecialEducation));
            Field<TextBox>(window,"WorkDaysTextBox").Text="999";
            Field<TextBox>(window,"PriceDifferenceTextBox").Text="999";
            Invoke(window,"UpdateHybridRouteDraftRows");
            Assert.Equal(Visibility.Visible,Field<Border>(window,"MultiRouteEntryBorder").Visibility);
            var rows=Field<System.Collections.ObjectModel.ObservableCollection<MainWindow.MultiRoutePaymentDraftRow>>(window,"_multiRoutePaymentRows");
            var first=Assert.Single(rows);Assert.Equal(0,first.WorkDays);Assert.Equal(0,first.PriceDifference);
            first.WorkDays=22;first.PriceDifference=150;first.TaxDebt=10;
            var grid=Field<DataGrid>(window,"MultiRoutePaymentGrid");
            var expression=BindingOperations.SetBinding(grid,FrameworkElement.TagProperty,new Binding(nameof(first.WorkDays)){Source=first});
            Validation.MarkInvalid(expression,new ValidationError(new ExceptionValidationRule(),expression,"Geçersiz sayı",null));
            routes.SelectedItems.Add(b);Invoke(window,"UpdateHybridRouteDraftRows");
            Assert.Single(rows);
            Validation.ClearInvalid(expression);
            Invoke(window,"UpdateHybridRouteDraftRows");
            Assert.Equal(2,rows.Count);Assert.Equal(22,rows[0].WorkDays);Assert.Equal(0,rows[1].WorkDays);
            rows[1].WorkDays=18;rows[1].PriceDifference=30;
            Field<TextBox>(window,"PriceDifferenceTextBox").Text="999";
            routes.SelectedItems.Clear();routes.SelectedItems.Add(b);Invoke(window,"UpdateHybridRouteDraftRows");
            Assert.Equal(30,Assert.Single(rows).PriceDifference);
            routes.SelectedItems.Add(a);Invoke(window,"UpdateHybridRouteDraftRows");
            Assert.Equal(150,rows.Single(r=>r.RouteNumber=="01").PriceDifference);
            Assert.Equal(30,rows.Single(r=>r.RouteNumber=="02").PriceDifference);
        });
    }

    [Fact]
    public void HybridCalculation_ShouldReadRowsBeforeSharedFormValidation()
    {
        var source=ReadMainWindowSource();
        var method=MethodBody(source,"CalculateProgressButton_Click");
        var early=method.IndexOf("TryCalculateHybridSelection(selectedContract)",StringComparison.Ordinal);
        Assert.True(early>=0);
        Assert.True(early<method.IndexOf("TryParseWorkDays(WorkDaysTextBox.Text",StringComparison.Ordinal));
    }

    [Fact]
    public void FilteredHistory_ShouldResolveSameIdInTheVisibleProgram()
    {
        RunOnSta(() =>
        {
            var directory=Directory.CreateTempSubdirectory("oehs-filter-identity-");
            try
            {
                var window=CreateEditorHarness();
                SetField(window,"_archiveStore",new ProgressPaymentArchiveStore(directory.FullName));
                var special=ArchiveProgressEditorContextTests.CreateArchive().ProgressPayment;
                var basic=special with {Input=special.Input with {ProgramType=TransportationProgramType.BasicOrSecondary}};
                Collection(window,"_progressHistory").Add(special);Collection(window,"_progressHistory").Add(basic);
                var program=Field<ComboBox>(window,"HomeProgramComboBox");
                program.Items.Add(new ComboBoxItem{Tag="BasicOrSecondary"});program.SelectedIndex=0;
                SetField(window,"_operationsReady",true);
                Invoke(window,"UpdateHistoryView");
                var row=Assert.Single(Collection(window,"_historyRows").Cast<object>());
                var selected=Assert.IsType<ProgressPaymentHistoryData>(Invoke(window,"GetHistoryItemFromRow",row));
                Assert.Equal(TransportationProgramType.BasicOrSecondary,selected.Input.ProgramType);
            }
            finally {directory.Delete(true);}
        });
    }

    [Fact]
    public void PracticalHome_SearchShouldClearOldPaymentAndActions()
    {
        RunOnSta(() =>
        {
            var directory=Directory.CreateTempSubdirectory("oehs-home-handler-");
            try
            {
                var window=CreateEditorHarness();
                SetField(window,"_archiveStore",new ProgressPaymentArchiveStore(directory.FullName));
                SetField(window,"_calculationConstantsHistory",new System.Collections.Generic.List<ProgressPaymentCalculationConstantsChange>());
                var c=ArchiveProgressEditorContextTests.CreateArchive().Contract;
                Collection(window,"_contracts").Add(c);
                Collection(window,"_routes").Add(CreateRoute("01","Merkez","12345678901"));
                Collection(window,"_contractors").Add(CreateFirm("IŞIK TAŞIMA","12345678901"));
                var program=Field<ComboBox>(window,"HomeProgramComboBox");
                program.Items.Add(new ComboBoxItem{Tag="SpecialEducation"});program.SelectedIndex=0;
                SelectHomePeriod(window,c.StartDate);
                SetField(window,"_operationsReady",true);
                Invoke(window,"RefreshHomeOperations");
                var grid=Field<DataGrid>(window,"HomeMonthlyGrid");
                Assert.Single(grid.Items.Cast<object>());
                grid.SelectedIndex=0;Invoke(window,"RefreshSelectedPayment");
                Assert.True(Field<Button>(window,"HomeNextActionButton").IsEnabled);
                Field<TextBox>(window,"HomeSearchBox").Text="olmayan firma";
                Invoke(window,"RefreshHomeOperations");
                Assert.Empty(grid.Items.Cast<object>());
                Assert.False(Field<Button>(window,"HomeSavePdfsButton").IsEnabled);
                Assert.False(Field<Button>(window,"HomePreviewButton").IsEnabled);
                Assert.False(Field<Button>(window,"HomeNextActionButton").IsEnabled);
                Assert.Contains("seçin",Field<TextBlock>(window,"HomePaymentSummary").Text,StringComparison.Ordinal);
            }
            finally{directory.Delete(true);}
        });
    }

    [Fact]
    public void PracticalHome_ShouldKeepActionsReachableAtSupportedSizes()
    {
        RunOnSta(() =>
        {
            foreach (var size in new[] { (1024d,600d), (1280d,720d), (1366d,768d), (1920d,1080d) })
            {
                var layout=LoadLayoutFragment(NamedElement(LoadMainWindowXaml(),"HomePanel"));
                ((ComboBox)layout.FindName("HomeProgramComboBox")).SelectedIndex=0;
                var tender=(ComboBox)layout.FindName("HomeTenderListBox");
                tender.Items.Add(ArchiveProgressEditorContextTests.CreateArchive().Contract with {TenderNumber="2026/123456"});tender.SelectedIndex=0;
                var periodBox=(ComboBox)layout.FindName("HomePeriodMonthComboBox");
                periodBox.ItemsSource=new[]{new ProgressPaymentMonthOption(new DateOnly(2026,10,1),new DateOnly(2026,10,31))};
                periodBox.SelectedIndex=0;
                ((Button)layout.FindName("HomeNextActionButton")).IsEnabled=true;
                ((Button)layout.FindName("HomeNextActionButton")).Content="Hakediş Oluştur";
                ((Border)layout.FindName("HomePreflightPanel")).Visibility=Visibility.Visible;
                var values=new[]{("Net","813.387,00 ₺"),("Gross","859.921,08 ₺"),("Deductions","46.534,08 ₺"),("Vat","78.174,64 ₺"),("Transport","781.566,43 ₺"),("Difference","180,00 ₺"),("Routes","4 / 6"),("Firms","3 firma")};
                foreach(var (key,value) in values) ((TextBlock)layout.FindName("Home"+key+"Value")).Text=value;
                ((TextBlock)layout.FindName("HomeRoutesCaption")).Text="2 hat için kayıt yok";
                ((TextBlock)layout.FindName("HomeFinanceScopeText")).Text="Örnek veriler · Program / İKN / ay toplamı";
                ((TextBlock)layout.FindName("HomePaymentSummary")).Text="IŞIK TAŞIMACILIK · Hat 1 — Merkez İlkokulu\nİKN: 2026/42 · Ekim 2026 · Hakediş 2";
                ((TextBlock)layout.FindName("HomePaymentAmounts")).Text="Taşıma: 16 gün × 19539,1608 ₺\nFiyat farkı: 7.365,35 ₺ · Kesinti: 2.847,16 ₺\nPDF net ödeme: 332.516,08 ₺";
                ((DataGrid)layout.FindName("HomeMonthlyGrid")).ItemsSource=new[]
                {
                    new {Tender="2026/123456",Firm="Anadolu Taşımacılık Ltd. Şti.",RouteTitle="H1 — Merkez Atatürk İlkokulu",NetText="245.320,00 ₺",Status="Kontrole hazır",Payment=new {Input=new {WorkDays=22m}}},
                    new {Tender="2026/123456",Firm="Yeşil Vadi Turizm A.Ş.",RouteTitle="H3 — Cumhuriyet Ortaokulu",NetText="198.600,00 ₺",Status="Kontrole hazır",Payment=new {Input=new {WorkDays=20m}}},
                    new {Tender="2026/123456",Firm="Mavi Tur Taşımacılık Ltd. Şti.",RouteTitle="H5 — Güneşköy Anadolu Lisesi",NetText="176.430,00 ₺",Status="Kontrole hazır",Payment=new {Input=new {WorkDays=18m}}},
                    new {Tender="2026/123456",Firm="Anadolu Taşımacılık Ltd. Şti.",RouteTitle="H6 — Organize Sanayi Mesleki Lise",NetText="193.037,00 ₺",Status="Kontrole hazır",Payment=new {Input=new {WorkDays=16m}}}
                };
                ArrangeLayout(layout,size.Item1-215-38,size.Item2-60-38);
                layout.Dispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                layout.UpdateLayout();
                var viewer=VisualDescendants<ScrollViewer>(layout).First();
                Assert.True(viewer.ViewportWidth>0);
                var cards=(System.Windows.Controls.Primitives.UniformGrid)layout.FindName("HomeFinanceCards");
                Assert.Equal(8,cards.Children.Count);
                foreach(Border card in cards.Children)
                {
                    var bounds=card.TransformToAncestor(viewer).TransformBounds(new Rect(card.RenderSize));
                    Assert.InRange(bounds.Right,1,viewer.ActualWidth+1);
                    Assert.True(card.ActualHeight>=124);
                }
                foreach(var name in HomeDetailPanelNames)
                {
                    var element=(FrameworkElement)layout.FindName(name);
                    var rect=element.TransformToAncestor(viewer).TransformBounds(new Rect(element.RenderSize));
                    Assert.InRange(rect.Right,1,viewer.ActualWidth+1);
                }
                viewer.ScrollToBottom();layout.UpdateLayout();
                foreach(var name in HomeOutputActionNames)
                {
                    var button=(Button)layout.FindName(name);
                    var rect=button.TransformToAncestor(viewer).TransformBounds(new Rect(button.RenderSize));
                    Assert.InRange(rect.Bottom,1,viewer.ActualHeight+1);
                    Assert.InRange(rect.Right,1,viewer.ActualWidth+1);
                }
                var output=Environment.GetEnvironmentVariable("OEHS_LAYOUT_PREVIEW");
                if(!string.IsNullOrWhiteSpace(output) && (size.Item1==1366 || size.Item1==1920))
                {
                    ((Border)layout.FindName("HomePreflightPanel")).Visibility=Visibility.Collapsed;
                    viewer.ScrollToTop();layout.UpdateLayout();
                    Directory.CreateDirectory(output);
                    var image=new System.Windows.Media.Imaging.RenderTargetBitmap((int)layout.ActualWidth,(int)layout.ActualHeight,96,96,PixelFormats.Pbgra32);
                    image.Render(layout);
                    var png=new System.Windows.Media.Imaging.PngBitmapEncoder();png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
                    using var file=File.Create(Path.Combine(output,$"gold-finance-home-{size.Item1:0}.png"));png.Save(file);
                }
            }
        });
    }

    [Fact]
    public void ResponsiveNavigation_ShouldScrollToLastTargetAtMinimumHeight()
    {
        var document = LoadMainWindowXaml();
        var navigation = NamedElement(document, "NavHome").Ancestors()
            .First(element => element.Name.LocalName == "Grid");
        var scroll = Assert.Single(navigation.Descendants().Where(element => element.Name.LocalName == "ScrollViewer"));
        Assert.Equal("Auto", (string?)scroll.Attribute("VerticalScrollBarVisibility"));
        Assert.Equal("Disabled", (string?)scroll.Attribute("HorizontalScrollBarVisibility"));
        foreach (var button in navigation.Descendants().Where(element => element.Name.LocalName == "Button"))
            Assert.Contains(scroll, button.Ancestors());

        RunOnSta(() =>
        {
            var layout = LoadLayoutFragment(navigation);
            ArrangeLayout(layout, 215, 600 - 60 - 14);
            var viewer = VisualDescendants<ScrollViewer>(layout).Single();
            Assert.True(viewer.ViewportHeight > 0, "Navigation must remain usable at 600px height.");
            viewer.ScrollToBottom();
            layout.UpdateLayout();
            var lastButton = (Button)layout.FindName("NavSettings");
            var bounds = lastButton.TransformToAncestor(viewer).TransformBounds(new Rect(lastButton.RenderSize));
            Assert.InRange(bounds.Bottom, 1, viewer.ViewportHeight + 1);
        });
    }

    [Theory]
    [InlineData(1024)]
    [InlineData(1366)]
    [InlineData(1920)]
    public void ResponsiveHistory_ShouldKeepRowActionsHorizontallyReachable(double windowWidth)
    {
        var history = NamedElement(LoadMainWindowXaml(), "HistoryPanel");
        var items = history.Descendants().Single(element => (string?)element.Attribute(XamlNamespace + "Name") == "HistoryItemsControl");
        Assert.Null(items.Attribute("Height")); // Table uses remaining space; actions stay below it.
        Assert.Equal("2", (string?)NamedElement(LoadMainWindowXaml(), "HistoryActionsPanel").Ancestors().First(element => element.Name.LocalName == "Border").Attribute("Grid.Row"));

        RunOnSta(() =>
        {
            var layout = LoadLayoutFragment(history);
            var control = (DataGrid)layout.FindName("HistoryItemsControl");
            control.Items.Add(new { DisplayTitle = "Test dönemi", CreatedAtText = "22.10.2026", TotalAmountText = "100,00 ₺", NetPayableText = "90,00 ₺" });
            ArrangeLayout(layout, windowWidth - 215 - 38, 600 - 60 - 38);
            var viewer = VisualDescendants<ScrollViewer>(control).First();
            Assert.InRange(control.ActualWidth, 1, windowWidth - 215 - 38);
            viewer.ScrollToRightEnd();
            layout.UpdateLayout();
            control.SelectedIndex = 0; layout.UpdateLayout();
            var panel = (WrapPanel)layout.FindName("HistoryActionsPanel");
            foreach (var button in panel.Children.OfType<Button>())
            {
                Assert.True(button.IsEnabled);
                var bounds = button.TransformToAncestor(panel).TransformBounds(new Rect(button.RenderSize));
                Assert.InRange(bounds.Left, 0, panel.ActualWidth);
                Assert.InRange(bounds.Right, 1, panel.ActualWidth);
            }
        });
    }

    [Theory]
    [InlineData("AddRouteButton", "CancelRouteEditButton", "ClearRouteButton", 240)]
    [InlineData("SaveContractorButton", "CancelContractorEditButton", "ClearContractorButton", 300)]
    [InlineData("CalculateProgressButton", "CancelProgressEditButton", "ArchiveCurrentProgressButton", 680)]
    public void ResponsiveActions_ShouldWrapEveryVisibleButtonWithinAvailableWidth(
        string firstName, string secondName, string lastName, double availableWidth)
    {
        var document = LoadMainWindowXaml();
        var group = NamedElement(document, firstName).Parent!;
        Assert.Equal("WrapPanel", group.Name.LocalName);
        Assert.Same(group, NamedElement(document, secondName).Parent);
        Assert.Same(group, NamedElement(document, lastName).Parent);

        RunOnSta(() =>
        {
            var layout = LoadLayoutFragment(group);
            var panel = Assert.IsType<WrapPanel>(layout.Children[0]);
            foreach (Button button in panel.Children)
                button.Visibility = Visibility.Visible;
            ArrangeLayout(layout, availableWidth, 300);
            var bounds = panel.Children.Cast<Button>().Select(button =>
                button.TransformToAncestor(panel).TransformBounds(new Rect(button.RenderSize))).ToArray();
            Assert.True(bounds.Select(rect => rect.Top).Distinct().Count() > 1, "Actions should wrap when the panel is narrow.");
            foreach (var rectangle in bounds)
            {
                Assert.InRange(rectangle.Left, 0, availableWidth);
                Assert.InRange(rectangle.Right, 1, availableWidth);
                Assert.True(rectangle.Height >= 30);
            }
        });
    }

    private static XElement NamedElement(XDocument document, string name) =>
        document.Descendants().Single(element => (string?)element.Attribute(XamlNamespace + "Name") == name);

    [Theory]
    [InlineData(1024, 600)]
    [InlineData(1280, 720)]
    [InlineData(1366, 768)]
    [InlineData(1920, 1080)]
    public void ResponsivePayment_ShouldKeepInputsResultsAndActionsWithinViewport(double width, double height)
    {
        RunOnSta(() =>
        {
            var layout = LoadLayoutFragment(NamedElement(LoadMainWindowXaml(), "ProgressPanel"));
            ArrangeLayout(layout, width - 215 - 38, height - 60 - 38);
            var viewer = VisualDescendants<ScrollViewer>(layout).First();
            foreach (var name in ProgressMoneyInputNames)
            {
                var input = (TextBox)layout.FindName(name);
                Assert.True(input.ActualWidth >= 90, $"{name} is too narrow at {width}px: {input.ActualWidth}px.");
                var bounds = input.TransformToAncestor(viewer).TransformBounds(new Rect(input.RenderSize));
                Assert.True(
                    bounds.Right >= 90 && bounds.Right <= viewer.ViewportWidth + 1,
                    $"{name}: right={bounds.Right}, viewport={viewer.ViewportWidth}, viewer width={viewer.ActualWidth}, input width={input.ActualWidth}.");
            }
            var label = (TextBlock)layout.FindName("GuidePriceDifferenceResultLabel");
            var value = (TextBlock)layout.FindName("GuidePriceDifferenceResultTextBlock");
            var labelBounds = label.TransformToAncestor(viewer).TransformBounds(new Rect(label.RenderSize));
            var valueBounds = value.TransformToAncestor(viewer).TransformBounds(new Rect(value.RenderSize));
            Assert.False(labelBounds.IntersectsWith(valueBounds), "The result label and amount must not overlap.");
            viewer.ScrollToBottom();
            layout.UpdateLayout();
            var actions = (Border)layout.FindName("ProgressActionPanel");
            var actionBounds = actions.TransformToAncestor(viewer).TransformBounds(new Rect(actions.RenderSize));
            // TransformToAncestor(viewer) includes the ScrollViewer's 10px content padding.
            // The action bar is reachable once its bottom sits inside that padded viewport.
            Assert.InRange(actionBounds.Bottom, 1, viewer.ViewportHeight + viewer.Padding.Top + 1);
            foreach (var button in VisualDescendants<Button>(actions).Where(button => button.Visibility == Visibility.Visible))
            {
                var bounds = button.TransformToAncestor(viewer).TransformBounds(new Rect(button.RenderSize));
                Assert.InRange(bounds.Left, 0, viewer.ViewportWidth);
                Assert.InRange(bounds.Right, 1, viewer.ViewportWidth + 1);
            }
        });
    }

    // Parse the production layout and styles without MainWindow's startup/persistence.
    // Only code-behind event subscriptions are removed; controls, bindings and templates remain.
    private static Grid LoadLayoutFragment(XElement source)
    {
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var fragment = new XElement(source);
        fragment.Attribute("Visibility")?.Remove();
        foreach (var element in fragment.DescendantsAndSelf())
        {
            var type = typeof(Control).Assembly.GetType("System.Windows.Controls." + element.Name.LocalName)
                ?? typeof(Control).Assembly.GetType("System.Windows.Controls.Primitives." + element.Name.LocalName);
            foreach (var attribute in element.Attributes().Where(attribute =>
                attribute.Name.Namespace == XNamespace.None && type?.GetEvent(attribute.Name.LocalName) is not null).ToArray())
                attribute.Remove();
        }
        var resources = XDocument.Load(LoadProjectFile("src", "OzelEgitimHakedis.Wpf", "App.xaml"))
            .Root!.Element(presentation + "Application.Resources")!;
        XNamespace local = "clr-namespace:OzelEgitimHakedis.Wpf;assembly=OEHS";
        var root = new XElement(presentation + "Grid",
            new XAttribute(XNamespace.Xmlns + "x", XamlNamespace),
            new XAttribute(XNamespace.Xmlns + "local", local.NamespaceName),
            new XAttribute(XNamespace.Xml+"lang","tr-TR"),
            new XElement(presentation + "Grid.Resources", resources.Elements(),
                new XElement(local + "ContractWorkNameConverter",new XAttribute(XamlNamespace+"Key","ContractWorkNameConverter")),
                new XElement(local + "TurkishMoneyConverter",new XAttribute(XamlNamespace+"Key","TurkishMoneyConverter"))), fragment);
        return (Grid)XamlReader.Parse(root.ToString());
    }

    private static void ArrangeLayout(FrameworkElement layout, double width, double height)
    {
        layout.Measure(new Size(width, height));
        layout.Arrange(new Rect(0, 0, width, height));
        layout.UpdateLayout();
    }

    private static System.Collections.Generic.IEnumerable<T> VisualDescendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
                yield return match;
            foreach (var descendant in VisualDescendants<T>(child))
                yield return descendant;
        }
    }

    [Theory]
    [InlineData("Signatories", "SignatoriesPanel", "Resmî Belge İmzacıları")]
    public void Navigation_ShouldExposeDedicatedButtonAndOnlyItsTargetPanel(string target, string panelName, string title)
    {
        var buttons = LoadMainWindowXaml().Descendants()
            .Where(element => element.Name.LocalName == "Button"
                && ((string?)element.Attribute(XamlNamespace + "Name"))?.StartsWith("Nav", StringComparison.Ordinal) == true)
            .ToArray();
        var targetButton = Assert.Single(buttons.Where(element => (string?)element.Attribute("Tag") == target));
        Assert.Equal("NavigationButton_Click", EventName(targetButton, "Click"));
        Assert.Contains(targetButton.Descendants(), element =>
            string.Equals((string?)element.Attribute("Text"), title.ToUpper(CultureInfo.GetCultureInfo("tr-TR")), StringComparison.Ordinal));

        RunOnSta(() =>
        {
            var window = CreateEditorHarness();
            foreach (var element in buttons)
                Field<Button>(window, (string)element.Attribute(XamlNamespace + "Name")!).Tag = (string?)element.Attribute("Tag");
            var button = Field<Button>(window, (string)targetButton.Attribute(XamlNamespace + "Name")!);
            // Start elsewhere so selection and stale visibility are both exercised.
            Invoke(window, "NavigationButton_Click", Field<Button>(window, "NavProgress"), new RoutedEventArgs());
            Invoke(window, "NavigationButton_Click", button, new RoutedEventArgs());

            Assert.Equal(title, Field<TextBlock>(window, "HeaderModuleText").Text);
            Assert.Equal("Active", button.Uid);
            Assert.Empty(Field<Button>(window, "NavProgress").Uid);
            foreach (var name in InitiallyHiddenPanelNames)
                Assert.Equal(name == panelName ? Visibility.Visible : Visibility.Collapsed, Field<FrameworkElement>(window, name).Visibility);
        });
    }

    [Theory]
    [InlineData(null, "2026-10-31", true, false)]
    [InlineData("2026-10-01", null, false, true)]
    [InlineData(null, null, true, true)]
    [InlineData("2026-10-31", "2026-10-01", true, true)]
    public void ContractSave_ShouldRejectMissingOrReversedDatesBeforePreparingDraft(
        string? start, string? end, bool invalidStart, bool invalidEnd)
    {
        RunOnSta(() =>
        {
            var window = CreateContractSaveHarness(start, end);
            // Number assignment is the first draft-preparation side effect, before
            // ContractDraftData construction or persistence. Fail safely if reached.
            Field<TextBox>(window, "ContractNumberTextBox").TextChanged += (_, _) => throw new DraftPreparationReachedException();

            Assert.Null(Record.Exception(() => Invoke(window, "SaveContractButton_Click", new Button(), new RoutedEventArgs())));

            Assert.Equal(Visibility.Visible, Field<Border>(window, "ContractValidationBanner").Visibility);
            Assert.False(string.IsNullOrWhiteSpace(Field<TextBlock>(window, "ContractValidationMessageTextBlock").Text));
            Assert.Equal(invalidStart, Field<DatePicker>(window, "StartDatePicker").ToolTip is not null);
            Assert.Equal(invalidEnd, Field<DatePicker>(window, "EndDatePicker").ToolTip is not null);
            Assert.Empty(Collection(window, "_contracts"));
            Assert.Empty(Field<TextBox>(window, "ContractNumberTextBox").Text);
        });
    }

    [Theory]
    [InlineData("2026-10-01", "2026-10-31")]
    [InlineData("2026-10-01", "2026-10-01")]
    public void ContractSave_ShouldAcceptOrderedOrEqualDates(string start, string end)
    {
        RunOnSta(() =>
        {
            var window = CreateContractSaveHarness(start, end);
            Field<TextBox>(window, "ContractNumberTextBox").TextChanged += (_, _) => throw new DraftPreparationReachedException();
            Assert.Throws<DraftPreparationReachedException>(() => Invoke(window, "SaveContractButton_Click", new Button(), new RoutedEventArgs()));
            Assert.Equal(Visibility.Collapsed, Field<Border>(window, "ContractValidationBanner").Visibility);
            Assert.Null(Field<DatePicker>(window, "StartDatePicker").ToolTip);
            Assert.Null(Field<DatePicker>(window, "EndDatePicker").ToolTip);
        });
    }

    [Theory]
    [InlineData(null, "2026-10-31", true, false)]
    [InlineData("2026-10-01", null, false, true)]
    [InlineData(null, null, true, true)]
    public void ArchivedContractSave_ShouldFlagMissingDates(string? start, string? end, bool invalidStart, bool invalidEnd)
    {
        RunOnSta(() =>
        {
            var window = CreateContractSaveHarness(start, end);
            var saved = ArchiveProgressEditorContextTests.CreateArchive();
            var archive = saved with
            {
                Contract = saved.Contract with { StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2026, 10, 31) },
                ProgressPayment = saved.ProgressPayment with { PeriodDate = new DateOnly(2026, 10, 1) },
                PreviousPayments = Array.Empty<ProgressPaymentHistoryData>()
            };
            SetField(window, "_editingArchiveSnapshot", archive);
            // Keep this validation-only harness outside archive persistence even
            // before required-date validation exists.
            Field<TextBox>(window, "ContractAmountTextBox").Text = "invalid";
            Invoke(window, "SaveArchivedContractCorrection");

            Assert.Equal(invalidStart, Field<DatePicker>(window, "StartDatePicker").ToolTip is not null);
            Assert.Equal(invalidEnd, Field<DatePicker>(window, "EndDatePicker").ToolTip is not null);
            Assert.Same(archive, Field<ProgressPaymentArchive>(window, "_editingArchiveSnapshot"));
        });
    }

    private sealed class DraftPreparationReachedException : Exception;

    private static MainWindow CreateContractSaveHarness(string? start, string? end)
    {
        var window = CreateEditorHarness();
        var program = new ComboBoxItem { Tag = "SpecialEducation" };
        Field<ComboBox>(window, "TransportProgramTypeComboBox").Items.Add(program);
        Field<ComboBox>(window, "TransportProgramTypeComboBox").SelectedItem = program;
        Field<TextBox>(window, "TenderNumberTextBox").Text = "IHALE-DATE-TEST";
        Field<TextBox>(window, "ContractAmountTextBox").Text = "10000";
        Field<TextBox>(window, "ContractWorkDaysTextBox").Text = "20";
        Field<TextBox>(window, "ContractTransportDailyPriceTextBox").Text = "500";
        Field<DatePicker>(window, "StartDatePicker").SelectedDate = start is null ? null : DateTime.Parse(start, CultureInfo.InvariantCulture);
        Field<DatePicker>(window, "EndDatePicker").SelectedDate = end is null ? null : DateTime.Parse(end, CultureInfo.InvariantCulture);
        return window;
    }

    [Theory]
    [InlineData(TransportationProgramType.BasicOrSecondary, "Hesaplama Bilgileri", "Hizmet İşleri Hakediş Raporu", Visibility.Collapsed)]
    [InlineData(TransportationProgramType.SpecialEducation, "Hesaplama", "Hizmetişleri Hakediş Raporu", Visibility.Visible)]
    public void PaymentLabels_ShouldFollowSelectedContractProgram(
        TransportationProgramType program, string calculationSheet, string serviceReport, Visibility guideVisibility)
    {
        RunOnSta(() =>
        {
            var window = CreateEditorHarness();
            var contract = ArchiveProgressEditorContextTests.CreateArchive().Contract with { ProgramType = program };
            Invoke(window, "UpdateProgressProgramTypeFields", contract);

            Assert.Equal("Taşıma Fiyat Farkı KDV Hariç", Field<TextBlock>(window, "PriceDifferenceLabel").Text);
            Assert.Equal(calculationSheet, Field<TextBlock>(window, "CalculationSheetNameTextBlock").Text);
            Assert.Equal(serviceReport, Field<TextBlock>(window, "ServiceReportSheetNameTextBlock").Text);
            Assert.Equal("Ele Geçen", Field<TextBlock>(window, "NetPayableLabel").Text);
            Assert.Equal("Ele Geçen", Field<TextBlock>(window, "NetControlLabel").Text);
            Assert.Equal(guideVisibility, Field<TextBlock>(window, "GuidePriceDifferenceLabel").Visibility);
            if (program == TransportationProgramType.SpecialEducation)
                Assert.Equal("Rehber Personel Fiyat Farkı KDV Hariç", Field<TextBlock>(window, "GuidePriceDifferenceLabel").Text);
        });
    }

    [Fact]
    public void ReportSheetLabel_ShouldFollowReportRouteEvenWhenPaymentProgramDiffers()
    {
        RunOnSta(() =>
        {
            var window = CreateEditorHarness();
            var contract = ArchiveProgressEditorContextTests.CreateArchive().Contract;
            Invoke(window, "UpdateProgressProgramTypeFields", contract);
            var route = CreateRoute("01", "Temel hat", "12345678901", TransportationProgramType.BasicOrSecondary);
            var selector = Field<ComboBox>(window, "ReportRouteComboBox");
            selector.Items.Add(route);
            selector.SelectedItem = route;
            Invoke(window, "UpdateReportView");

            Assert.Equal("Hizmet İşleri Hakediş Raporu", Field<TextBlock>(window, "ServiceReportSheetNameTextBlock").Text);
            Assert.Equal("Hesaplama", Field<TextBlock>(window, "CalculationSheetNameTextBlock").Text);
        });
    }

    [Fact]
    public void ContractDisplay_ShouldNotUseAnotherArchivesRouteName()
    {
        RunOnSta(() =>
        {
            var window = CreateEditorHarness();
            var archive = ArchiveProgressEditorContextTests.CreateArchive();
            SetField(window, "_editingArchiveSnapshot", archive);
            var otherContract = archive.Contract with { TenderNumber = "OTHER", WorkName = "Other legacy title" };

            Assert.Equal("Hat 01 Hattı Öğrenci Taşıma İşi", Invoke(window, "GetContractWorkName", archive.Contract));
            Assert.Equal("Other legacy title", Invoke(window, "GetContractWorkName", otherContract));
        });
    }

    [Theory]
    [InlineData("  Merkez  ", "Eski iş adı", "Merkez Hattı Öğrenci Taşıma İşi")]
    [InlineData("", "Eski iş adı", "Eski iş adı")]
    public void ContractDisplay_ShouldDeriveWorkNameWithoutChangingStoredLegacyValue(
        string routeName, string legacyWorkName, string expected)
    {
        RunOnSta(() =>
        {
            var window = CreateEditorHarness();
            var contract = ArchiveProgressEditorContextTests.CreateArchive().Contract with { WorkName = legacyWorkName };
            Collection(window, "_contracts").Add(contract);
            if (routeName.Length > 0)
                Collection(window, "_routes").Add(CreateRoute("01", routeName, "12345678901"));
            Invoke(window, "ApplyContractToForm", contract);

            Assert.Equal(expected, Invoke(window, "GetContractWorkName", contract));
            Assert.Equal(legacyWorkName, contract.WorkName);
        });
    }

    [Theory]
    [InlineData("")]
    [InlineData("Stale stored title")]
    public void ContractListsAndSummary_ShouldProjectAndRefreshRouteTitlesWithoutChangingStoredContracts(string storedTitle)
    {
        RunOnSta(() =>
        {
            var window = CreateEditorHarness();
            var contract = ArchiveProgressEditorContextTests.CreateArchive().Contract with { WorkName = storedTitle };
            Collection(window, "_contracts").Add(contract);
            var displayedTitle = new TextBlock();
            var converter = Field<ContractWorkNameConverter>(window, "_contractWorkNameConverter");
            var titleBinding = new MultiBinding { Converter = converter, Mode = BindingMode.OneWay };
            titleBinding.Bindings.Add(new Binding { Source = contract });
            titleBinding.Bindings.Add(new Binding(nameof(ContractWorkNameConverter.Titles)) { Source = converter });
            BindingOperations.SetBinding(displayedTitle, TextBlock.TextProperty, titleBinding);
            var routes = Collection(window, "_routes");
            routes.Add(CreateRoute("01", "Merkez", "12345678901"));
            Invoke(window, "UpdateContractSelectors");

            Assert.Contains("Özel Eğitim Taşıma", Field<TextBlock>(window, "HomeSelectedTenderSummaryTextBlock").Text, StringComparison.Ordinal);
            var titles = converter.Titles;
            Assert.Equal("Merkez Hattı Öğrenci Taşıma İşi", titles[contract]);
            Assert.Equal("Merkez Hattı Öğrenci Taşıma İşi", displayedTitle.Text);
            Assert.Same(contract, Field<ComboBox>(window, "HomeTenderListBox").SelectedItem);
            Assert.Same(contract, Assert.Single(Field<ItemsControl>(window, "ContractsListBox").Items.Cast<object>()));

            routes[0] = CreateRoute("01", "Yeni Merkez", "12345678901");
            Invoke(window, "UpdateRouteListView");
            titles = converter.Titles;
            Assert.Equal("Yeni Merkez Hattı Öğrenci Taşıma İşi", titles[contract]);
            Assert.Equal("Yeni Merkez Hattı Öğrenci Taşıma İşi", displayedTitle.Text);
            Assert.DoesNotContain("Yeni Merkez", Field<TextBlock>(window, "HomeSelectedTenderSummaryTextBlock").Text, StringComparison.Ordinal);

            routes.Clear();
            Invoke(window, "UpdateRouteListView");
            titles = converter.Titles;
            Assert.Equal(storedTitle, titles[contract]);
            Assert.Equal(storedTitle, displayedTitle.Text);
            Assert.Equal(storedTitle, contract.WorkName);
            Assert.Same(contract, Assert.Single(Collection(window, "_contracts").Cast<object>()));
        });
    }

    [Theory]
    [InlineData("HomeTenderListBox")]
    [InlineData("ContractsListBox")]
    public void ContractLists_ShouldUseContextAppropriatePresentation(string listName)
    {
        var list = LoadMainWindowXaml().Descendants()
            .Single(element => (string?)element.Attribute(XamlNamespace + "Name") == listName);
        if (listName == "HomeTenderListBox")
        {
            Assert.DoesNotContain(list.Descendants(), element => element.Name.LocalName == "MultiBinding");
            Assert.Contains(list.Descendants(), element => (string?)element.Attribute("Text") == "{Binding ProgramDisplayText}");
            return;
        }
        var binding = Assert.Single(list.Descendants().Where(element => element.Name.LocalName == "MultiBinding"));
        Assert.Equal("{StaticResource ContractWorkNameConverter}", (string?)binding.Attribute("Converter"));
        // ComboBox popup items cannot rely on finding their Window visually.
        Assert.Contains(binding.Elements(), element => (string?)element.Attribute("Path") == "Titles"
            && (string?)element.Attribute("Source") == "{StaticResource ContractWorkNameConverter}");
    }

    [Theory]
    [InlineData("Kayıtlı hat", "Canlı hat", "Eski iş", "Kayıtlı hat Hattı Öğrenci Taşıma İşi")]
    [InlineData("", "Canlı hat", "Eski iş", "Canlı hat Hattı Öğrenci Taşıma İşi")]
    [InlineData("", "", "Eski iş", "Eski iş")]
    public void OfficialOutput_ShouldDeriveWorkNameFromPaymentRouteThenLiveRouteWithLegacyFallback(
        string savedRoute, string liveRoute, string legacyWorkName, string expected)
    {
        RunOnSta(() =>
        {
            var window = CreateEditorHarness();
            var archive = ArchiveProgressEditorContextTests.CreateArchive();
            var payment = archive.ProgressPayment with
            {
                TenderNumber = "IHALE-01", ContractNumber = "SOZ-01", ContractorTaxOrIdentityNumber = "12345678901",
                RouteName = savedRoute, WorkName = legacyWorkName
            };
            Collection(window, "_contracts").Add(archive.Contract);
            Collection(window, "_routes").Add(CreateRoute("01", liveRoute, "12345678901"));
            var context = Assert.IsType<OehsOfficialDocumentContext>(Invoke(window, "BuildOfficialDocumentContextForPayment", payment));
            Assert.Equal(expected, context.WorkName);
            Assert.Equal(legacyWorkName, payment.WorkName);
        });
    }

    [Fact]
    public void PaymentNetSummary_ShouldUseOfficialNetLabel()
    {
        var summaryValue = LoadMainWindowXaml().Descendants()
            .Single(element => (string?)element.Attribute(XamlNamespace + "Name") == "ProgressNetSummaryTextBlock");
        Assert.Equal("Ele Geçen", (string?)summaryValue.Parent!.Elements().First().Attribute("Text"));
    }

    [Theory]
    [InlineData(true, null, false, 0)]
    [InlineData(false, null, true, 2000)]
    [InlineData(true, false, false, 0)]
    [InlineData(false, true, true, 2000)]
    public void ArchiveCalculation_ShouldKeepPaymentVatWithMissingOrConflictingLiveFirm(
        bool archivedVatExempt, bool? liveVatExempt, bool contractVatExempt, decimal expectedVat)
    {
        RunOnSta(() =>
        {
            var window = CreateEditorHarness();
            var archive = ArchiveProgressEditorContextTests.CreateArchive();
            archive = archive with
            {
                Contract = archive.Contract with { VatExempt = contractVatExempt },
                Contractor = archive.Contractor with { VatExempt = !archivedVatExempt },
                Route = archive.Route with { ContractorTaxOrIdentityNumber = "12345678901" },
                ProgressPayment = archive.ProgressPayment with
                {
                    Input = archive.ProgressPayment.Input with { IsVatExempt = archivedVatExempt }
                }
            };
            if (liveVatExempt.HasValue)
                Collection(window, "_contractors").Add(CreateFirm("Live firm", "12345678901", liveVatExempt.Value));
            SetField(window, "_editingArchiveSourcePath", "unused-archive.json");
            SetField(window, "_editingArchiveSnapshot", archive);
            Invoke(window, "BeginProgressHistoryEdit", archive.ProgressPayment);

            // Unchanged inputs calculate but never open the save-confirmation dialog.
            Assert.False((bool)Invoke(window, "ArchiveCorrectionInputsDiffer")!);
            Invoke(window, "CalculateProgressButton_Click", window, new RoutedEventArgs());

            Assert.Equal(expectedVat.ToString("N2", CultureInfo.GetCultureInfo("tr-TR")) + " ₺",
                Field<TextBlock>(window, "VatResultTextBlock").Text);
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ArchiveCalculationTrace_ShouldUsePaymentVatInsteadOfSnapshotFirm(bool archivedVatExempt)
    {
        RunOnSta(() =>
        {
            var window = CreateEditorHarness();
            var archive = ArchiveProgressEditorContextTests.CreateArchive();
            archive = archive with
            {
                Contractor = archive.Contractor with { VatExempt = !archivedVatExempt },
                ProgressPayment = archive.ProgressPayment with
                {
                    Input = archive.ProgressPayment.Input with { IsVatExempt = archivedVatExempt }
                }
            };
            SetField(window, "_editingArchiveSourcePath", "unused-archive.json");
            SetField(window, "_editingArchiveSnapshot", archive);
            Invoke(window, "BeginProgressHistoryEdit", archive.ProgressPayment);

            Assert.Equal(archivedVatExempt, Invoke(window, "IsVatExemptForCurrentFirm", archive.Contract));
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void HistoryCorrection_ShouldRestoreSavedFirmAndRouteForMultiFirmContract(bool resetDraft)
    {
        RunOnSta(() =>
        {
            var window = CreateEditorHarness();
            var archive = ArchiveProgressEditorContextTests.CreateArchive();
            var savedFirm = CreateFirm("Same name", "12345678901");
            var otherFirm = CreateFirm("Same name", "98765432109");
            var savedRoute = CreateRoute("01", "Saved route", "12345678901");
            var otherRoute = CreateRoute("01", "Other firm's route", "98765432109");
            var payment = archive.ProgressPayment with
            {
                TenderNumber = "IHALE-01", ContractNumber = "SOZ-01",
                ContractorTaxOrIdentityNumber = "123 456 789 01"
            };
            Collection(window, "_contracts").Add(archive.Contract);
            Collection(window, "_contractors").Add(savedFirm);
            Collection(window, "_contractors").Add(otherFirm);
            Collection(window, "_routes").Add(savedRoute);
            Collection(window, "_routes").Add(otherRoute);
            Collection(window, "_progressHistory").Add(payment);
            Field<ComboBox>(window, "ProgressTenderListBox").ItemsSource = Collection(window, "_contracts");
            Field<ComboBox>(window, "ProgressContractorComboBox").ItemsSource = Collection(window, "_contractors");
            Field<ComboBox>(window, "ProgressContractorComboBox").SelectedItem = otherFirm;

            if (resetDraft)
            {
                Invoke(window, "ResetNewProgressDraftAfterCalculation", payment);
                Assert.Null(Field<ComboBox>(window, "ProgressContractorComboBox").SelectedItem);
            }

            Invoke(window, "BeginProgressHistoryEdit", payment);

            Assert.Same(savedFirm, Field<ComboBox>(window, "ProgressContractorComboBox").SelectedItem);
            var routes = Field<ListBox>(window, "ProgressRouteComboBox");
            Assert.Same(savedRoute, Assert.Single(routes.SelectedItems.Cast<object>()));
            Assert.Same(savedRoute, Assert.Single(routes.Items.Cast<object>()));
            Assert.Equal(new DateOnly(2026, 10, 31),
                Assert.IsType<ProgressPaymentMonthOption>(Field<ComboBox>(window, "ProgressPeriodMonthComboBox").SelectedItem).PeriodDate);
        });
    }

    private static MainWindow CreateEditorHarness()
    {
        // Exercise real handlers and selectors without running the constructor's
        // fresh-start migration, persistence loading, or visible window setup.
        var window = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
        foreach (var field in typeof(MainWindow).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
        {
            if (typeof(FrameworkElement).IsAssignableFrom(field.FieldType)
                || field.FieldType == typeof(ContractWorkNameConverter)
                || field.FieldType.IsGenericType &&
                (field.FieldType.GetGenericTypeDefinition() == typeof(System.Collections.ObjectModel.ObservableCollection<>)
                 || field.FieldType.GetGenericTypeDefinition() == typeof(RouteDraftCache<>)))
                field.SetValue(window, Activator.CreateInstance(field.FieldType));
        }
        SetField(window, "_calculationConstants", ProgressPaymentCalculationConstants.Defaults);
        Field<ListBox>(window, "ProgressRouteComboBox").SelectionMode = SelectionMode.Extended;
        Field<Button>(window, "NavProgress").Tag = "Progress";
        return window;
    }

    private static object CreateFirm(string name, string identity, bool vatExempt = false) =>
        Activator.CreateInstance(typeof(MainWindow).GetNestedType("ContractorDraft", BindingFlags.NonPublic)!,
            name, identity, "", "", "", "", "", TransportationProgramType.SpecialEducation, vatExempt)!;

    private static object CreateRoute(string number, string name, string identity,
        TransportationProgramType program = TransportationProgramType.SpecialEducation, string tender = "IHALE-01",
        decimal transportDays = 20m, decimal dailyPrice = 1000m, decimal guidePrice = 0m) =>
        Activator.CreateInstance(typeof(MainWindow).GetNestedType("RouteDraft", BindingFlags.NonPublic)!,
            number, name, dailyPrice, guidePrice, transportDays, tender, identity, "Same name", "SOZ-01",
            program, null, null)!;

    private static T Field<T>(MainWindow window, string name) =>
        (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(window)!;

    private static IList Collection(MainWindow window, string name) => Field<IList>(window, name);

    private static void SetField(MainWindow window, string name, object value) =>
        typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, value);

    private static object? Invoke(MainWindow window, string name, params object[] arguments)
    {
        try
        {
            return typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(window, arguments);
        }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }
    }

    private static void RunOnSta(Action test)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { test(); }
            catch (Exception exception) { error = exception; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Editor handler did not finish within 30 seconds.");
        if (error is not null)
            ExceptionDispatchInfo.Capture(error).Throw();
    }

    [Fact]
    public void ProgressPanel_ShouldPlaceFourResponsiveZonesInWorkflowOrder()
    {
        var document = LoadMainWindowXaml();
        var progressPanel = document.Root!.Descendants()
            .Single(element => (string?)element.Attribute(XamlNamespace + "Name") == "ProgressPanel");
        var panelElements = progressPanel.Descendants().ToArray();

        var contextIndex = IndexOfNamedElement(panelElements, "ProgressContextPanel");
        var routeSelectionIndex = IndexOfNamedElement(panelElements, "ProgressRouteSelectionPanel");
        var componentsIndex = IndexOfNamedElement(panelElements, "ProgressComponentsPanel");
        var actionIndex = IndexOfNamedElement(panelElements, "ProgressActionPanel");
        var inputIndex = IndexOfNamedElement(panelElements, "WorkDaysTextBox");
        var resultIndex = IndexOfNamedElement(panelElements, "NetResultTextBlock");

        Assert.True(contextIndex < routeSelectionIndex,
            "The progress context strip must appear before route selection.");
        Assert.True(routeSelectionIndex < componentsIndex && componentsIndex < inputIndex,
            "Route selection must appear before the component and result zone.");
        foreach (var index in new[] { routeSelectionIndex, componentsIndex, actionIndex })
            Assert.Same(panelElements[contextIndex].Parent, panelElements[index].Parent);
        foreach (var selector in new[] { "ProgressTenderListBox", "ProgressContractorComboBox", "ProgressPeriodMonthComboBox" })
            Assert.Contains(panelElements[routeSelectionIndex], panelElements[IndexOfNamedElement(panelElements, selector)].Ancestors());
        Assert.Contains(panelElements[componentsIndex], panelElements[inputIndex].Ancestors());
        Assert.Contains(panelElements[componentsIndex], panelElements[resultIndex].Ancestors());
        Assert.True(inputIndex < resultIndex && resultIndex < actionIndex,
            "The action bar must appear after the input and result cards.");

        var actionPanel = panelElements[actionIndex];
        foreach (var buttonName in new[]
        {
            "CalculateProgressButton", "CancelProgressEditButton",
            "ArchiveCurrentProgressButton", "CorrectCurrentProgressButton"
        })
        {
            var button = panelElements[IndexOfNamedElement(panelElements, buttonName)];
            Assert.Equal("Button", button.Name.LocalName);
            Assert.True(button.Ancestors().Contains(actionPanel),
                $"{buttonName} must be inside ProgressActionPanel.");
        }

        var archiveCorrectionState = panelElements
            .Single(element => (string?)element.Attribute(XamlNamespace + "Name") == "ProgressArchiveCorrectionStateTextBlock");
        Assert.Contains("ARŞİV DÜZELTME", (string?)archiveCorrectionState.Attribute("Text"), StringComparison.Ordinal);
        Assert.Equal("#F4C247", (string?)archiveCorrectionState.Attribute("Foreground"));
        Assert.Contains("KİLİTLİ", (string?)archiveCorrectionState.Attribute("Text"), StringComparison.Ordinal);
        Assert.Null(archiveCorrectionState.Attribute("Visibility"));
        var archiveStateTrigger = Assert.Single(archiveCorrectionState.Descendants().Where(
            element => element.Name.LocalName == "DataTrigger"
                && string.Equals((string?)element.Attribute("Value"), "False", StringComparison.Ordinal)
                && ((string?)element.Attribute("Binding"))?.Contains("ProgressTenderListBox", StringComparison.Ordinal) == true
                && ((string?)element.Attribute("Binding"))?.Contains("Path=IsEnabled", StringComparison.Ordinal) == true));
        Assert.Contains(archiveStateTrigger.Elements(), element => element.Name.LocalName == "Setter"
            && string.Equals((string?)element.Attribute("Property"), "Visibility", StringComparison.Ordinal)
            && string.Equals((string?)element.Attribute("Value"), "Visible", StringComparison.Ordinal));
    }

    [Fact]
    public void HakedisCreate_ShouldKeepDesignOneCardsAndLiveExcelHybridControls()
    {
        var document = LoadMainWindowXaml();
        var root = document.Root;
        Assert.NotNull(root);

        var named = root!.Descendants()
            .Where(element => element.Attribute(XamlNamespace + "Name") is not null)
            .ToDictionary(
                element => (string)element.Attribute(XamlNamespace + "Name")!,
                StringComparer.Ordinal);

        Assert.Contains("ProgressTenderListBox", named.Keys);
        Assert.Equal("ProgressTenderListBox_SelectionChanged",
            EventName(named["ProgressTenderListBox"], "SelectionChanged"));
        Assert.Contains("ProgressNewTenderShortcutButton", named.Keys);
        Assert.Equal("Contract", (string?)named["ProgressNewTenderShortcutButton"].Attribute("Tag"));
        Assert.Equal("NavigationButton_Click",
            EventName(named["ProgressNewTenderShortcutButton"], "Click"));
        Assert.Contains("ProgressContractorComboBox", named.Keys);
        Assert.Contains("ProgressContractorDetailShortcutButton", named.Keys);
        Assert.Equal("Contractors", (string?)named["ProgressContractorDetailShortcutButton"].Attribute("Tag"));
        Assert.Equal("NavigationButton_Click",
            EventName(named["ProgressContractorDetailShortcutButton"], "Click"));
        Assert.Contains("ProgressPeriodMonthComboBox", named.Keys);
        Assert.Contains("ProgressWorkDaysCardTextBlock", named.Keys);
        Assert.Contains("ManualAdditionalGuaranteeTextBox", named.Keys);
        Assert.Equal("True", (string?)named["ManualAdditionalGuaranteeTextBox"].Attribute("IsReadOnly"));
        Assert.Equal("0,00 ₺", (string?)named["ManualAdditionalGuaranteeTextBox"].Attribute("Text"));
        Assert.Contains("Hesaplanan", (string?)named["ManualAdditionalGuaranteeLabel"].Attribute("Text"), StringComparison.Ordinal);
        Assert.Contains("I × %6",
            (string?)named["GuaranteeRuleHintTextBlock"].Attribute("Text"), StringComparison.Ordinal);
        Assert.Contains("ProgressRouteComboBox", named.Keys);
        Assert.Contains("ProgressRouteTableHeaderGrid", named.Keys);
        var routeTableHeaders = named["ProgressRouteTableHeaderGrid"].Descendants()
            .Where(element => element.Name.LocalName == "TextBlock")
            .Select(element => (string?)element.Attribute("Text"))
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToArray();
        Assert.Contains("Hat", routeTableHeaders);
        Assert.Contains("Hattın Adı", routeTableHeaders);
        Assert.DoesNotContain("Söz. Gün", routeTableHeaders);
        Assert.Contains("Günlük Fiyat", routeTableHeaders);
        Assert.Contains("Durum", routeTableHeaders);
        Assert.Contains("ProgressRouteRelationTextBlock", named.Keys);
        Assert.Contains("ProgressWorkDaysSummaryTextBlock", named.Keys);
        Assert.Contains("MultiRoutePaymentGrid", named.Keys);
        Assert.Contains("CalculateProgressButton", named.Keys);
        Assert.Equal("CalculateProgressButton_Click",
            EventName(named["CalculateProgressButton"], "Click"));
        Assert.Equal("False", (string?)named["MultiRoutePaymentGrid"].Attribute("CanUserAddRows"));
        Assert.Equal("False", (string?)named["MultiRoutePaymentGrid"].Attribute("CanUserDeleteRows"));
        Assert.Equal("CellOrRowHeader", (string?)named["MultiRoutePaymentGrid"].Attribute("SelectionUnit"));
        Assert.Equal("IncludeHeader", (string?)named["MultiRoutePaymentGrid"].Attribute("ClipboardCopyMode"));
        Assert.Equal("1", (string?)named["MultiRoutePaymentGrid"].Attribute("FrozenColumnCount"));
        Assert.Equal("False", (string?)named["MultiRoutePaymentGrid"].Attribute("AutoGenerateColumns"));
        Assert.Contains(named["MultiRoutePaymentGrid"].Descendants(),
            element => element.Name.LocalName == "DataGridTextColumn"
                && string.Equals((string?)element.Attribute("Header"), "Taşıma Günlük Fiyatı", StringComparison.Ordinal)
                && string.Equals((string?)element.Attribute("IsReadOnly"), "False", StringComparison.Ordinal));
        Assert.Contains(named["MultiRoutePaymentGrid"].Descendants(),
            element => element.Name.LocalName == "DataGridTextColumn"
                && string.Equals((string?)element.Attribute("Header"), "Taşıma Fiyat Farkı KDV Hariç", StringComparison.Ordinal)
                && ((string?)element.Attribute("Binding"))?.Contains("Mode=TwoWay", StringComparison.Ordinal) == true);

        var columns = named["MultiRoutePaymentGrid"].Descendants()
            .Where(element => element.Name.LocalName == "DataGridTextColumn")
            .ToDictionary(element => (string?)element.Attribute("Header") ?? string.Empty,
                StringComparer.Ordinal);
        foreach (var editableHeader in new[]
        {
            "Hattın Adı", "Taşıma Günlük Fiyatı", "Rehber Personel Günlük Fiyat", "Hakediş Gün Sayısı", "Taşıma İskonto", "Taşıma Fiyat Farkı KDV Hariç",
            "Rehber İş Günü (bu ay)", "Rehber İskonto", "Rehber Personel Fiyat Farkı KDV Hariç",
            "Vergi Borcu", "SGK Borcu", "Denetim Cezası"
        })
        {
            Assert.Contains(editableHeader, columns.Keys);
            Assert.Contains("Mode=TwoWay",
                (string?)columns[editableHeader].Attribute("Binding") ?? string.Empty, StringComparison.Ordinal);
        }

        foreach (var readOnlyHeader in new[] { "Hat No" })
        {
            Assert.Contains(readOnlyHeader, columns.Keys);
            Assert.Equal("True", (string?)columns[readOnlyHeader].Attribute("IsReadOnly"));
        }

        Assert.Equal("ProgressContractorComboBox_SelectionChanged",
            EventName(named["ProgressContractorComboBox"], "SelectionChanged"));
        Assert.Equal("ProgressPeriodMonthComboBox_SelectionChanged",
            EventName(named["ProgressPeriodMonthComboBox"], "SelectionChanged"));
        Assert.Equal("Extended", (string?)named["ProgressRouteComboBox"].Attribute("SelectionMode"));
        Assert.Equal("ProgressRouteComboBox_SelectionChanged",
            EventName(named["ProgressRouteComboBox"], "SelectionChanged"));

        var cardTitles = root.Descendants()
            .Where(element => element.Name.LocalName == "TextBlock")
            .Select(element => (string?)element.Attribute("Text"))
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToArray();

        Assert.Contains(cardTitles, text => text!.Contains("İKN VE SÖZLEŞME", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(cardTitles, text => text!.Contains("YÜKLENİCİ (FİRMA) BİLGİLERİ", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(cardTitles, text => text!.Contains("HAKEDİŞ DÖNEMİ", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(cardTitles, text => text!.Equals("Dönem, İKN ve Firma", StringComparison.Ordinal));
        Assert.Equal("250", (string?)named["MultiRoutePaymentGrid"].Attribute("MinHeight"));

        var vatCheckBox = root.Descendants()
            .Single(element => (string?)element.Attribute(XamlNamespace + "Name") == "ContractorVatExemptCheckBox");
        Assert.Equal("ContractorVatExemptCheckBox_Changed", EventName(vatCheckBox, "Checked"));
        Assert.Equal("ContractorVatExemptCheckBox_Changed", EventName(vatCheckBox, "Unchecked"));

        var firmCard = vatCheckBox.Ancestors()
            .FirstOrDefault(ancestor => ancestor.Name.LocalName == "Border"
                && ancestor.Descendants().Any(element =>
                    element.Name.LocalName == "TextBlock"
                    && string.Equals((string?)element.Attribute("Text"), "KDV İSTİSNASI", StringComparison.Ordinal))
                && ancestor.Descendants().Any(element =>
                    element.Name.LocalName == "Button"
                    && string.Equals((string?)element.Attribute("Click"), "SaveContractorButton_Click", StringComparison.Ordinal)));
        Assert.NotNull(firmCard);
        Assert.Contains(firmCard!.Descendants(), element =>
            element.Name.LocalName == "Button"
            && string.Equals((string?)element.Attribute("Click"), "SaveContractorButton_Click", StringComparison.Ordinal));
    }

    [Fact]
    public void ApplicationManifest_ShouldDeclarePerMonitorV2DpiAwareness()
    {
        var manifest = XDocument.Load(LoadProjectFile("src", "OzelEgitimHakedis.Wpf", "app.manifest"));
        XNamespace windowsSettings = "http://schemas.microsoft.com/SMI/2016/WindowsSettings";
        XNamespace legacyWindowsSettings = "http://schemas.microsoft.com/SMI/2005/WindowsSettings";

        Assert.Equal("PerMonitorV2",
            manifest.Descendants(windowsSettings + "dpiAwareness").Single().Value);
        Assert.Equal("true",
            manifest.Descendants(legacyWindowsSettings + "dpiAware").Single().Value);
    }

    [Fact]
    public void ArchiveProgressCorrection_ShouldLoadOnlySnapshotSelectorContext()
    {
        var source = ReadMainWindowSource();

        var historyEdit = MethodBody(source, "BeginProgressHistoryEdit");
        var contextLoader = MethodBody(source, "LoadArchiveProgressEditorContext");
        var editorMode = MethodBody(source, "SetProgressEditorMode");
        var multiRouteRows = MethodBody(source, "UpdateHybridRouteDraftRows");

        Assert.Contains("LoadArchiveProgressEditorContext(ArchiveProgressEditorContext.Create(archive));", historyEdit, StringComparison.Ordinal);
        Assert.Contains("ProgressTenderListBox.ItemsSource = new[] { context.Contract };", contextLoader, StringComparison.Ordinal);
        Assert.Contains("ProgressRouteComboBox.ItemsSource = new[] { snapshotRoute };", contextLoader, StringComparison.Ordinal);
        Assert.Contains("ProgressRouteComboBox.SelectedItems.Clear();", contextLoader, StringComparison.Ordinal);
        Assert.Contains("ProgressRouteComboBox.SelectedItem = snapshotRoute;", contextLoader, StringComparison.Ordinal);
        Assert.Contains("_multiRouteDraftCache.Clear();", contextLoader, StringComparison.Ordinal);
        Assert.Contains("UpdateProgressPeriodOptions(context.Contract, preserveSelection: false);", contextLoader, StringComparison.Ordinal);
        Assert.Contains("UpdateMultiRouteDraftRows();", contextLoader, StringComparison.Ordinal);
        Assert.Contains("ProgressTenderListBox.IsEnabled = !(editing && archiveCorrection);", editorMode, StringComparison.Ordinal);
        Assert.Contains("ProgressRouteComboBox.IsEnabled = !(editing && archiveCorrection);", editorMode, StringComparison.Ordinal);
        Assert.Contains("MultiRouteEntryBorder.Visibility = Visibility.Collapsed;", multiRouteRows, StringComparison.Ordinal);
        Assert.Contains("SetGlobalMonetaryFieldsVisible(true);", multiRouteRows, StringComparison.Ordinal);
    }

    [Fact]
    public void ArchiveProgressCorrection_ShouldRestoreLiveSelectorsWhenCorrectionStateClears()
    {
        var source = ReadMainWindowSource();

        var clearArchiveState = MethodBody(source, "ClearArchiveCorrectionState");
        var selectorRestore = MethodBody(source, "RestoreLiveProgressEditorSelectorContext");
        var saveArchiveCorrection = MethodBody(source, "SaveArchiveCorrectionSnapshot");
        var cancelProgressEdit = MethodBody(source, "CancelProgressEditButton_Click");

        Assert.Contains("RestoreLiveProgressEditorSelectorContext();", clearArchiveState, StringComparison.Ordinal);
        Assert.Contains("ProgressTenderListBox.ItemsSource = _contracts;", selectorRestore, StringComparison.Ordinal);
        Assert.Contains("ProgressTenderListBox.SelectedItem = null;", selectorRestore, StringComparison.Ordinal);
        Assert.Contains("ProgressRouteComboBox.SelectedItems.Clear();", selectorRestore, StringComparison.Ordinal);
        Assert.Contains("_multiRouteDraftCache.Clear();", selectorRestore, StringComparison.Ordinal);
        Assert.Contains("ClearArchiveCorrectionState();", saveArchiveCorrection, StringComparison.Ordinal);
        Assert.Contains("ClearArchiveCorrectionState();", cancelProgressEdit, StringComparison.Ordinal);
    }

    [Fact]
    public void NewProgressCalculation_ShouldClearDraftAndRetainCompletedRecordActions()
    {
        var source = ReadMainWindowSource();
        var reset = MethodBody(source, "ResetNewProgressDraftAfterCalculation");
        var clear = MethodBody(source, "ClearProgressEntryFields");

        Assert.Contains("ClearProgressEntryFields();", reset, StringComparison.Ordinal);
        Assert.Contains("_lastCalculatedHistoryId = lastCalculated.Id;", reset, StringComparison.Ordinal);
        Assert.True(reset.IndexOf("ClearProgressEntryFields();", StringComparison.Ordinal) <
                    reset.IndexOf("_lastCalculatedHistoryId = lastCalculated.Id;", StringComparison.Ordinal));
        Assert.True(reset.IndexOf("_lastCalculatedHistoryId = lastCalculated.Id;", StringComparison.Ordinal) <
                    reset.IndexOf("SetProgressEditorMode(false);", StringComparison.Ordinal));

        foreach (var clearedState in new[]
        {
            "ProgressTenderListBox.SelectedItem = null;",
            "ProgressContractorComboBox.SelectedItem = null;",
            "ProgressContractorComboBox.ItemsSource = Array.Empty<ContractorDraft>();",
            "ProgressPeriodMonthComboBox.SelectedItem = null;",
            "ProgressPeriodMonthComboBox.ItemsSource = Array.Empty<ProgressPaymentMonthOption>();",
            "ProgressRouteComboBox.SelectedItems.Clear();",
            "_multiRoutePaymentRows.Clear();", "_multiRouteDraftCache.Clear();",
            "ProgressNumberTextBox.Clear();", "textBox.Clear();"
        })
            Assert.Contains(clearedState, clear, StringComparison.Ordinal);

        Assert.True(clear.IndexOf("_suppressSelectionEvents = true;", StringComparison.Ordinal) <
                    clear.IndexOf("ProgressPeriodMonthComboBox.SelectedItem = null;", StringComparison.Ordinal));
        Assert.DoesNotContain("_lastCalculatedHistoryId = null;", clear, StringComparison.Ordinal);
        Assert.Contains("GetLastCalculatedProgress() is not null", MethodBody(source, "SetProgressEditorMode"), StringComparison.Ordinal);
        Assert.Contains("_progressHistory.FirstOrDefault(x => x.Id == id)", MethodBody(source, "GetLastCalculatedProgress"), StringComparison.Ordinal);
        Assert.Contains("GetLastCalculatedProgress()", MethodBody(source, "ArchiveCurrentProgressButton_Click"), StringComparison.Ordinal);
        Assert.Contains("GetLastCalculatedProgress()", MethodBody(source, "CorrectCurrentProgressButton_Click"), StringComparison.Ordinal);
    }

    [Fact]
    public void NewSingleRouteCalculation_ShouldResetOnlyAfterPersistenceAndOutsideCorrections()
    {
        var source = ReadMainWindowSource();
        var calculate = MethodBody(source, "CalculateProgressButton_Click");
        var resetIndex = calculate.IndexOf("ResetNewProgressDraftAfterCalculation(historyItem);", StringComparison.Ordinal);

        Assert.True(resetIndex >= 0, "A successful new single-route payment must reset its draft.");
        var persistenceFailure = BlockAfter(calculate, "if (!SavePersistedData())");
        Assert.Contains("return;", persistenceFailure, StringComparison.Ordinal);
        Assert.DoesNotContain("ResetNewProgressDraftAfterCalculation", persistenceFailure, StringComparison.Ordinal);
        Assert.True(resetIndex > calculate.IndexOf(persistenceFailure, StringComparison.Ordinal) + persistenceFailure.Length);
        Assert.Contains("ResetNewProgressDraftAfterCalculation(historyItem);",
            BlockAfter(calculate, "if (!editingHistoryId.HasValue)"), StringComparison.Ordinal);

        var archiveSave = calculate.IndexOf("SaveArchiveCorrectionSnapshot(", StringComparison.Ordinal);
        Assert.True(archiveSave >= 0 && archiveSave < resetIndex);
        Assert.True(calculate.IndexOf("return;", archiveSave, StringComparison.Ordinal) < resetIndex);
        Assert.DoesNotContain("ResetNewProgressDraftAfterCalculation", MethodBody(source, "SaveArchiveCorrectionSnapshot"), StringComparison.Ordinal);
    }

    [Fact]
    public void NewMultiRouteCalculation_ShouldResetOnlyAfterPersistenceAndKeepActionsVisible()
    {
        var source = ReadMainWindowSource();
        var save = MethodBody(source, "SaveMultiRouteProgressPayment");
        var resetIndex = save.IndexOf("ResetNewProgressDraftAfterCalculation(batch[^1]);", StringComparison.Ordinal);

        Assert.True(resetIndex >= 0, "A successful new multi-route batch must reset its draft.");
        var persistenceFailure = BlockAfter(save, "if (!SavePersistedData())");
        Assert.Contains("return;", persistenceFailure, StringComparison.Ordinal);
        Assert.DoesNotContain("ResetNewProgressDraftAfterCalculation", persistenceFailure, StringComparison.Ordinal);
        Assert.True(resetIndex > save.IndexOf(persistenceFailure, StringComparison.Ordinal) + persistenceFailure.Length);
        Assert.DoesNotContain("CorrectCurrentProgressButton.Visibility = Visibility.Collapsed;", save[resetIndex..], StringComparison.Ordinal);
        Assert.DoesNotContain("ArchiveCurrentProgressButton.Visibility = Visibility.Collapsed;", save[resetIndex..], StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(source, @"ResetNewProgressDraftAfterCalculation\((?:historyItem|batch\[\^1\])\);").Count);
    }

    private static string ReadMainWindowSource() => string.Join(Environment.NewLine,
        Directory.GetFiles(Path.GetDirectoryName(LoadProjectFile("src", "OzelEgitimHakedis.Wpf", "MainWindow.xaml.cs"))!, "MainWindow*.cs")
            .OrderBy(path => path, StringComparer.Ordinal).Select(File.ReadAllText));

    private static string BlockAfter(string source, string marker)
    {
        var markerIndex = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(markerIndex >= 0, $"Expected source to contain {marker}.");
        var bodyStart = source.IndexOf('{', markerIndex);
        var depth = 0;
        for (var index = bodyStart; index < source.Length; index++)
        {
            if (source[index] == '{')
                depth++;
            else if (source[index] == '}' && --depth == 0)
                return source[(bodyStart + 1)..index];
        }

        throw new InvalidOperationException($"Expected {marker} to have a closing brace.");
    }

    private static string EventName(XElement element, string eventAttribute) =>
        (string?)element.Attribute(eventAttribute) ?? string.Empty;

    private static int IndexOfNamedElement(XElement[] elements, string name)
    {
        var index = Array.FindIndex(elements,
            element => (string?)element.Attribute(XamlNamespace + "Name") == name);
        Assert.True(index >= 0, $"Expected ProgressPanel to contain '{name}'.");
        return index;
    }

    private static XDocument LoadMainWindowXaml() =>
        XDocument.Load(LoadProjectFile("src", "OzelEgitimHakedis.Wpf", "MainWindow.xaml"),
            LoadOptions.PreserveWhitespace);

    private static string MethodBody(string source, string methodName)
    {
        var methodMatch = Regex.Match(
            source,
            $@"private\s+(?:static\s+)?[\w<>?,]+\s+{Regex.Escape(methodName)}\(");
        Assert.True(methodMatch.Success, $"Expected MainWindow to define {methodName}.");
        var methodStart = methodMatch.Index;

        var bodyStart = source.IndexOf('{', methodStart);
        Assert.True(bodyStart >= 0, $"Expected {methodName} to have a body.");

        var depth = 0;
        for (var index = bodyStart; index < source.Length; index++)
        {
            if (source[index] == '{')
                depth++;
            else if (source[index] == '}' && --depth == 0)
                return source[(bodyStart + 1)..index];
        }

        throw new InvalidOperationException($"Expected {methodName} to have a closing brace.");
    }

    private static string LoadProjectFile(params string[] segments)
    {
        var candidates = new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory };
        foreach (var candidate in candidates)
        {
            var directory = new DirectoryInfo(candidate);
            while (directory is not null)
            {
                var path = segments.Aggregate(directory.FullName, Path.Combine);
                if (File.Exists(path))
                    return path;
                directory = directory.Parent;
            }
        }

        throw new FileNotFoundException(
            $"Project file '{Path.Combine(segments)}' was not found from the test working directory or test output directory.");
    }
    private static void SelectHomePeriod(MainWindow window, DateOnly period)
    {
        var box=Field<ComboBox>(window,"HomePeriodMonthComboBox");
        box.ItemsSource=new[]{new ProgressPaymentMonthOption(new DateOnly(period.Year,period.Month,1),new DateOnly(period.Year,period.Month,DateTime.DaysInMonth(period.Year,period.Month)))};
        box.SelectedIndex=0;
    }

}
