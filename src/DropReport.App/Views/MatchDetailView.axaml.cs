using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DropReport.App.Views;

public partial class MatchDetailView : UserControl
{
    public MatchDetailView() => InitializeComponent();
    private void OnFit(object sender, RoutedEventArgs e) => Map.Fit();
}
