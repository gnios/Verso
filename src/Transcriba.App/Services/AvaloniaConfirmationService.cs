using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;

namespace Transcriba.App.Services;

public sealed class AvaloniaConfirmationService : IConfirmationService
{
    private Func<TopLevel?> _getTopLevel = () =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    public void SetTopLevelProvider(Func<TopLevel?> provider) => _getTopLevel = provider;

    public async Task<bool> ConfirmAsync(string title, string message)
    {
        var owner = _getTopLevel() as Window;
        if (owner is null)
        {
            return false;
        }

        var result = false;
        var dialog = new Window
        {
            Title = title,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
            Background = owner.Background,
        };

        var messageBlock = new TextBlock
        {
            Text = message,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Margin = new Avalonia.Thickness(24, 20, 24, 0),
        };

        var confirmButton = new Button
        {
            Content = "Excluir",
            MinWidth = 90,
        };
        confirmButton.Click += (_, _) =>
        {
            result = true;
            dialog.Close();
        };

        var cancelButton = new Button
        {
            Content = "Cancelar",
            MinWidth = 90,
        };
        cancelButton.Click += (_, _) => dialog.Close();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Avalonia.Thickness(24, 16, 24, 20),
            Children = { cancelButton, confirmButton },
        };

        dialog.Content = new StackPanel
        {
            Children = { messageBlock, buttons },
        };

        await dialog.ShowDialog(owner);
        return result;
    }
}
