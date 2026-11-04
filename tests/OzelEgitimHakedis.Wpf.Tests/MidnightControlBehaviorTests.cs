using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Xunit;
namespace OzelEgitimHakedis.Wpf.Tests;
public sealed partial class HakedisCreateHybridUiContractTests
{
    [Fact]
    public void MidnightInputs_ShouldRetainSelectionAndDateEditing()
    {
        RunOnSta(() =>
        {
            var root = LoadLayoutFragment(NamedElement(LoadMainWindowXaml(), "ContractPanel"));
            var combo = new ComboBox { Style = (Style)root.FindResource("FieldComboBox") };
            combo.Items.Add("Birinci"); combo.Items.Add("İkinci");
            root.Children.Add(combo); combo.ApplyTemplate(); combo.SelectedIndex = 1;
            Assert.Equal("İkinci", combo.SelectedItem);
            Assert.NotNull(combo.Template.FindName("PART_Popup", combo));
            var date = new DatePicker { Style = (Style)root.FindResource("FieldDatePicker") };
            root.Children.Add(date); date.ApplyTemplate();
            date.SelectedDate = new DateTime(2026, 11, 4);
            Assert.Equal(new DateTime(2026, 11, 4), date.SelectedDate);
            Assert.NotNull(date.Template.FindName("PART_TextBox", date));
            Assert.NotNull(date.Template.FindName("PART_Popup", date));
        });
    }
    [Fact]
    public void MidnightDocumentViewer_ShouldKeepPaperAndPageNavigation()
    {
        RunOnSta(() =>
        {
            var document = new FixedDocument();
            for (int i = 0; i < 4; i++)
            {
                var page = new FixedPage { Width = 794, Height = 1123, Background = Brushes.White };
                page.Children.Add(new TextBlock { Text = "Resmî belge", Foreground = Brushes.Black });
                document.Pages.Add(new PageContent { Child = page });
            }
            var source = System.Xml.Linq.XDocument.Load(LoadProjectFile("src", "OzelEgitimHakedis.Wpf", "DocumentPreviewWindow.xaml"));
            var viewerSource = source.Descendants().Single(e => e.Name.LocalName == "DocumentViewer");
            var root = LoadLayoutFragment(viewerSource);
            var viewer = Assert.IsType<DocumentViewer>(root.FindName("Viewer"));
            viewer.Document = document;
            var host = new Window { Content = root, Width = 900, Height = 700, ShowActivated = false,
                ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000 };
            try
            {
                host.Show();
                System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                viewer.ApplyTemplate(); root.UpdateLayout();
                Assert.NotNull(viewer.Template.FindName("PART_ContentHost", viewer));
                Assert.NotNull(viewer.Template.FindName("PART_FindToolBarHost", viewer));
                Assert.NotEmpty(VisualDescendants<DocumentPageView>(viewer));
                Assert.Equal(4, document.Pages.Count);
                foreach (var page in document.Pages)
                    Assert.Equal(Colors.White, ((SolidColorBrush)page.Child.Background).Color);
            }
            finally { host.Close(); }
        });
    }
}
