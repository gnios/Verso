using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Verso.App.Services;
using Verso.Core.Catalogs;
using Verso.Core.Data.Entities;

namespace Verso.App.ViewModels;

public partial class SidebarFolderItemViewModel : ViewModelBase
{
    private readonly NavigationService _navigation;
    private readonly Action<int>? _deleteHandler;

    public int Id { get; }
    public string Title { get; }
    public string Icon { get; }
    public string ColorHex { get; }
    public int TranscriptionCount { get; }
    public IReadOnlyList<SidebarTranscriptionItemViewModel> Transcriptions { get; }

    public bool CanDelete => _deleteHandler is not null;

    [ObservableProperty]
    private bool _isExpanded = true;

    public SidebarFolderItemViewModel(
        Folder folder,
        NavigationService navigation,
        Action<int>? deleteHandler = null)
    {
        _navigation = navigation;
        _deleteHandler = deleteHandler;
        Id = folder.Id;
        Title = TruncateTitle(folder.Title);
        Icon = folder.Icon;
        ColorHex = ColorCatalog.PageColors.FirstOrDefault(c => c.Name == folder.ColorName).Hex
                   ?? ColorCatalog.PageColors[0].Hex;
        TranscriptionCount = folder.Transcriptions.Count;
        Transcriptions = folder.Transcriptions
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new SidebarTranscriptionItemViewModel(t, navigation))
            .ToList();
    }

    [RelayCommand]
    private void Navigate() =>
        _navigation.NavigateTo(ScreenKey.Folder, new NavigationParameter(FolderId: Id));

    [RelayCommand]
    private void ToggleExpand() => IsExpanded = !IsExpanded;

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void Delete() => _deleteHandler?.Invoke(Id);

    private static string TruncateTitle(string title) =>
        title.Length <= 18 ? title : $"{title[..18]}…";
}
