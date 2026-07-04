using Avalonia.Controls;
using Avalonia.Interactivity;
using Transcriba.App.ViewModels;

namespace Transcriba.App.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private void OnProfileFieldLostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel settings)
        {
            settings.SaveProfileCommand.Execute(null);
        }
    }
}
