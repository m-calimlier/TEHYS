using System.Windows;
using System.Windows.Documents;

namespace OzelEgitimHakedis.Wpf;

public partial class DocumentPreviewWindow : Window
{
    public DocumentPreviewWindow(FixedDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        InitializeComponent();
        TextSharpnessService.Apply(this);
        Viewer.Document = document;
        Loaded += (_, _) => FitToOwner();
    }

    private void Print_Click(object sender, RoutedEventArgs e) => Viewer.Print();
    private void First_Click(object sender, RoutedEventArgs e) => Viewer.FirstPage();
    private void Previous_Click(object sender, RoutedEventArgs e) => Viewer.PreviousPage();
    private void Next_Click(object sender, RoutedEventArgs e) => Viewer.NextPage();
    private void Last_Click(object sender, RoutedEventArgs e) => Viewer.LastPage();
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => Viewer.Zoom = Math.Max(25, Viewer.Zoom - 25);
    private void ZoomIn_Click(object sender, RoutedEventArgs e) => Viewer.Zoom = Math.Min(400, Viewer.Zoom + 25);
    private void Fit_Click(object sender, RoutedEventArgs e) => Viewer.FitToWidth();

    private void FitToOwner()
    {
        var area = SystemParameters.WorkArea;
        var availableWidth = Math.Max(1d, (Owner?.ActualWidth > 0 ? Owner.ActualWidth : area.Width) - 40d);
        var availableHeight = Math.Max(1d, (Owner?.ActualHeight > 0 ? Owner.ActualHeight : area.Height) - 40d);
        MinWidth = Math.Min(MinWidth, availableWidth);
        MinHeight = Math.Min(MinHeight, availableHeight);
        Width = Math.Min(Width, availableWidth);
        Height = Math.Min(Height, availableHeight);
        Left = (Owner?.Left ?? area.Left) + ((Owner?.ActualWidth > 0 ? Owner.ActualWidth : area.Width) - Width) / 2d;
        Top = (Owner?.Top ?? area.Top) + ((Owner?.ActualHeight > 0 ? Owner.ActualHeight : area.Height) - Height) / 2d;
        Viewer.FitToWidth();
    }
}
