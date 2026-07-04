using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Transcriba.App.ViewModels;
using Transcriba.Core.Services;

namespace Transcriba.App.Views;

public partial class UploadView : UserControl
{
    public UploadView()
    {
        InitializeComponent();
        DragDrop.SetAllowDrop(UploadZone, true);
        UploadZone.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        UploadZone.AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        UploadZone.AddHandler(DragDrop.DropEvent, OnDrop);
        UploadZone.AddHandler(InputElement.PointerPressedEvent, OnUploadZonePressed, RoutingStrategies.Tunnel);
    }

    private async void OnUploadZonePressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not UploadViewModel viewModel || viewModel.HasSelectedFile)
        {
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null)
        {
            return;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Selecionar arquivo de áudio ou vídeo",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Áudio/Vídeo")
                {
                    Patterns = UploadMediaFormats.Extensions
                        .Select(extension => $"*{extension}")
                        .ToList(),
                }
            ],
        });

        if (files.Count == 0)
        {
            return;
        }

        if (files[0].TryGetLocalPath() is string path)
        {
            viewModel.TrySelectFile(path);
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (DataContext is not UploadViewModel viewModel)
        {
            return;
        }

        if (e.DataTransfer.Formats.Contains(DataFormat.File))
        {
            e.DragEffects = DragDropEffects.Copy;
            viewModel.IsDragOver = true;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        if (DataContext is UploadViewModel viewModel)
        {
            viewModel.ClearDragOver();
        }
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not UploadViewModel viewModel)
        {
            return;
        }

        viewModel.ClearDragOver();

        var files = e.DataTransfer.TryGetFiles();
        if (files is null || files.Length == 0)
        {
            return;
        }

        if (files[0].TryGetLocalPath() is string path)
        {
            viewModel.TrySelectFile(path);
        }

        e.Handled = true;
    }
}
