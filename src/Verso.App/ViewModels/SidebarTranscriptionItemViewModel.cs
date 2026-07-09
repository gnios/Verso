using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Verso.App.Services;
using Verso.Core.Data.Entities;

namespace Verso.App.ViewModels;

public partial class SidebarTranscriptionItemViewModel : ViewModelBase
{
    private readonly NavigationService _navigation;

    public Guid Id { get; }
    public string Title { get; }
    public string? Icon { get; }
    public bool IsDone { get; }
    public string StatusIcon { get; }

    public SidebarTranscriptionItemViewModel(Transcription transcription, NavigationService navigation)
    {
        _navigation = navigation;
        Id = transcription.Id;
        Title = TruncateTitle(transcription.Title);
        Icon = transcription.Icon;
        IsDone = transcription.Status == TranscriptionStatus.Done;
        StatusIcon = transcription.Status switch
        {
            TranscriptionStatus.InProgress => "⏳",
            TranscriptionStatus.Error => "❌",
            _ => "",
        };
    }

    [RelayCommand]
    private void Open() =>
        _navigation.NavigateTo(
            ScreenKey.Editor,
            new NavigationParameter(TranscriptionId: Id));

    private static string TruncateTitle(string title) =>
        title.Length <= 18 ? title : $"{title[..18]}…";
}
