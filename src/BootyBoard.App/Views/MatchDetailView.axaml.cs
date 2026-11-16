using Avalonia.Controls;
using Avalonia.Interactivity;

namespace BootyBoard.App.Views;

public partial class MatchDetailView : UserControl
{
    public MatchDetailView() => InitializeComponent();
    private void OnFit(object sender, RoutedEventArgs e) => Map.Fit();
}
