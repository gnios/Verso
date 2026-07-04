using System;
using Avalonia.Controls;
using Avalonia.Input;
using Transcriba.App.ViewModels;

namespace Transcriba.App.Views;

public partial class PlayerBarView : UserControl
{
    public PlayerBarView()
    {
        InitializeComponent();
    }

    private void OnTrackPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control track || DataContext is not PlayerBarViewModel player)
        {
            return;
        }

        var position = e.GetPosition(track).X;
        var percent = position / Math.Max(1, track.Bounds.Width);
        player.SeekCommand.Execute(percent);
    }
}
