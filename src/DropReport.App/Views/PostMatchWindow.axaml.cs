using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using DropReport.App.ViewModels;

namespace DropReport.App.Views;

/// <summary>Slides into the bottom-right corner without taking focus (never pulls you out of the game), closes itself
/// after 15 s unless the pointer is on it.</summary>
public partial class PostMatchWindow : Window
{
    private DispatcherTimer _life;

    public PostMatchWindow()
    {
        InitializeComponent();
        Opened += (_, _) => PlaceAndShow();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is PostMatchViewModel vm) vm.CloseRequested += Close;
    }

    private void PlaceAndShow()
    {
        var card = this.FindControl<Border>("Card")!;
        card.Opacity = 0;
        card.RenderTransform = TransformOperations.Parse("translateY(24px)");
        Dispatcher.UIThread.Post(() =>
        {
            var screen = Screens.Primary;
            if (screen != null)
            {
                var wa = screen.WorkingArea; var s = screen.Scaling;
                Position = new PixelPoint(wa.Right - (int)(Bounds.Width * s) - (int)(18 * s), wa.Bottom - (int)(Bounds.Height * s) - (int)(18 * s));
            }
            card.Opacity = 1;
            card.RenderTransform = TransformOperations.Parse("translateY(0px)");
        }, DispatcherPriority.Background);
        _life = new DispatcherTimer(TimeSpan.FromSeconds(15), DispatcherPriority.Normal, (_, _) => { if (!IsPointerOver) Close(); });
        _life.Start();
    }

    protected override void OnClosed(EventArgs e) { _life?.Stop(); base.OnClosed(e); }
}
