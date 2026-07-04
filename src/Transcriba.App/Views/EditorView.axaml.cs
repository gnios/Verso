using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Transcriba.App.ViewModels;

namespace Transcriba.App.Views;

public partial class EditorView : UserControl
{
    private EditorViewModel? _viewModel;

    public EditorView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.ScrollToSegmentRequested -= OnScrollToSegmentRequested;
        }

        _viewModel = DataContext as EditorViewModel;
        if (_viewModel is not null)
        {
            _viewModel.ScrollToSegmentRequested += OnScrollToSegmentRequested;
        }
    }

    private void OnScrollToSegmentRequested(object? sender, SegmentItemViewModel segment)
    {
        var container = SegmentsList.ContainerFromItem(segment);
        container?.BringIntoView();
    }

    private void OnTitleLostFocus(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _ = _viewModel.CommitTitleCommand.ExecuteAsync(null);
        }
    }

    private void OnTitleKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _viewModel is not null)
        {
            _ = _viewModel.CommitTitleCommand.ExecuteAsync(null);
        }
    }

    private void OnSegmentPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: SegmentItemViewModel segment })
        {
            return;
        }

        if (e.Source is TextBox)
        {
            return;
        }

        segment.ClickCommand.Execute(null);
    }

    private void OnSegmentTextGotFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox && textBox.Tag is SegmentItemViewModel segment)
        {
            segment.NotifyFocused(textBox.CaretIndex);
        }
    }

    private void OnSegmentTextLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox && textBox.Tag is SegmentItemViewModel segment)
        {
            segment.NotifyFocused(textBox.CaretIndex);
            segment.CommitText();
        }
    }
}
