using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Transcriba.Core.Services;

namespace Transcriba.App.ViewModels;

public partial class SpeakerDropdownViewModel : ViewModelBase
{
    private readonly IServiceScopeFactory _scopeFactory;
    private EditorViewModel? _editor;
    private Guid _transcriptionId;

    public ObservableCollection<SpeakerOptionViewModel> Speakers { get; } = [];

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string _newSpeakerName = "";

    public bool CanAssign => _editor?.HasActiveSegment == true;

    public SpeakerDropdownViewModel(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public void Initialize(EditorViewModel editor, Guid transcriptionId)
    {
        _editor = editor;
        _transcriptionId = transcriptionId;
    }

    public async Task LoadSpeakersAsync()
    {
        if (_transcriptionId == Guid.Empty)
        {
            Speakers.Clear();
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var speakerService = scope.ServiceProvider.GetRequiredService<SpeakerService>();
        var speakers = await speakerService.GetSpeakersAsync(_transcriptionId);

        Speakers.Clear();
        foreach (var speaker in speakers)
        {
            Speakers.Add(new SpeakerOptionViewModel(
                speaker.Id,
                speaker.Name,
                speaker.ColorHex,
                option => _ = SelectSpeakerAsync(option)));
        }

        RefreshActiveIndicator();
    }

    public void RefreshActiveIndicator()
    {
        var activeSpeakerId = _editor?.GetActiveSegmentEntity()?.SpeakerId;
        foreach (var speaker in Speakers)
        {
            speaker.IsActive = activeSpeakerId.HasValue && speaker.Id == activeSpeakerId.Value;
        }

        NotifyAssignAvailability();
    }

    [RelayCommand]
    private void Toggle()
    {
        IsOpen = !IsOpen;
        if (IsOpen)
        {
            _ = LoadSpeakersAsync();
        }
    }

    [RelayCommand]
    private void Close() => IsOpen = false;

    [RelayCommand(CanExecute = nameof(CanAssign))]
    private async Task SelectSpeakerAsync(SpeakerOptionViewModel? speaker)
    {
        if (speaker is null || _editor is null || !CanAssign)
        {
            return;
        }

        await _editor.AssignSpeakerToActiveSegmentAsync(speaker.Id);
        IsOpen = false;
        RefreshActiveIndicator();
    }

    [RelayCommand(CanExecute = nameof(CanAssign))]
    private async Task AddNewSpeakerAsync()
    {
        var name = NewSpeakerName.Trim();
        if (string.IsNullOrEmpty(name) || _editor is null || !CanAssign)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var speakerService = scope.ServiceProvider.GetRequiredService<SpeakerService>();
        var speaker = await speakerService.CreateSpeakerAsync(_transcriptionId, name);

        await _editor.AssignSpeakerToActiveSegmentAsync(speaker.Id);
        NewSpeakerName = "";
        await LoadSpeakersAsync();
        IsOpen = false;
    }

    // Renomear um locutor já existente (qualquer segmento que o use atualiza o display).
    [RelayCommand]
    private async Task RenameSpeakerAsync((SpeakerOptionViewModel Option, string NewName) arg)
    {
        var (option, newName) = arg;
        if (option is null)
        {
            return;
        }

        var name = newName?.Trim() ?? "";
        if (string.IsNullOrEmpty(name))
        {
            option.IsEditing = false;
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var speakerService = scope.ServiceProvider.GetRequiredService<SpeakerService>();
        await speakerService.RenameSpeakerAsync(option.Id, name);

        option.Name = name;
        option.IsEditing = false;
        _editor?.OnSpeakerRenamed(option.Id, name);
        RefreshActiveIndicator();
    }

    internal void NotifyAssignAvailability()
    {
        OnPropertyChanged(nameof(CanAssign));
        SelectSpeakerCommand.NotifyCanExecuteChanged();
        AddNewSpeakerCommand.NotifyCanExecuteChanged();
    }
}
