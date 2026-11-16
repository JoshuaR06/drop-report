using Avalonia.Controls;
using Avalonia.Controls.Templates;
using DropReport.App.ViewModels;

namespace DropReport.App;

/// <summary>Finds the view for a view model by name: SeasonViewModel -> SeasonView.</summary>
public sealed class ViewLocator : IDataTemplate
{
    private static readonly Dictionary<Type, Func<Control>> Views = new()
    {
        [typeof(HomeViewModel)] = () => new Views.HomeView(),
        [typeof(SeasonViewModel)] = () => new Views.SeasonView(),
        [typeof(MatchesViewModel)] = () => new Views.MatchesView(),
        [typeof(MatchDetailViewModel)] = () => new Views.MatchDetailView(),
        [typeof(WeaponsViewModel)] = () => new Views.WeaponsView(),
        [typeof(TreasureViewModel)] = () => new Views.TreasureView(),
        [typeof(SpotsViewModel)] = () => new Views.SpotsView(),
        [typeof(SettingsViewModel)] = () => new Views.SettingsView(),
        [typeof(AboutViewModel)] = () => new Views.AboutView(),
        [typeof(SetupViewModel)] = () => new Views.SetupView(),
    };

    public Control Build(object data) => data != null && Views.TryGetValue(data.GetType(), out var f) ? f() : new TextBlock { Text = data?.GetType().Name };
    public bool Match(object data) => data is ViewModelBase;
}
