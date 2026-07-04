using System.ComponentModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Transcriba.App.Services;

namespace Transcriba.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    public NavigationService Navigation { get; }
    public ThemeService Theme { get; }

    public string ThemeIcon => Theme.IsDark ? "☀️" : "🌙";

    public MainWindowViewModel(NavigationService navigation, ThemeService theme)
    {
        Navigation = navigation;
        Theme = theme;
        Theme.PropertyChanged += OnThemePropertyChanged;
    }

    private void OnThemePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ThemeService.IsDark))
        {
            OnPropertyChanged(nameof(ThemeIcon));
        }
    }

    [RelayCommand]
    private async Task ToggleThemeAsync() => await Theme.ToggleAsync();
}
