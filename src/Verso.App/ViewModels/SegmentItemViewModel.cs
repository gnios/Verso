using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Verso.Core.Data.Entities;

namespace Verso.App.ViewModels;

public partial class SegmentItemViewModel : ViewModelBase
{
    private readonly EditorViewModel _editor;

    public Guid Id { get; }
    public double StartSeconds { get; }
    public string TimeDisplay { get; }

    [ObservableProperty]
    private string _text;

    [ObservableProperty]
    private string _speakerName = "";

    [ObservableProperty]
    private string _speakerColorHex = "#2eaadc";

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _isEditing;

    public Guid? SpeakerId { get; private set; }

    internal int CaretIndex;

    public SpeakerDropdownViewModel SpeakerDropdown => _editor.SpeakerDropdown;

    public SegmentItemViewModel(EditorViewModel editor, Segment segment)
    {
        _editor = editor;
        Id = segment.Id;
        StartSeconds = segment.StartSeconds;
        TimeDisplay = EditorViewModel.FormatSegmentTime(segment.StartSeconds);
        _text = segment.Text;
        UpdateSpeaker(segment.Speaker);
    }
    internal void UpdateSpeaker(Speaker? speaker)
    {
        SpeakerId = speaker?.Id;
        SpeakerName = speaker?.Name ?? "";
        SpeakerColorHex = speaker?.ColorHex ?? "#2eaadc";
    }

    internal void RenameSpeaker(string newName)
    {
        SpeakerName = newName;
    }

    [RelayCommand]
    private void Click() => _editor.OnSegmentClicked(this);
    [RelayCommand]
    private async Task SplitAsync() => await _editor.SplitSegmentForAsync(this);

    [RelayCommand]
    private async Task MergeAsync() => await _editor.MergeSegmentForAsync(this);

    [RelayCommand]
    private void OpenLocutor() => _editor.OpenLocutorFor(this);

    internal void NotifyFocused(int caretIndex)
    {
        CaretIndex = caretIndex;
        _editor.OnSegmentFocused(this, caretIndex);
    }

    internal void CommitText() => _editor.OnSegmentTextCommitted(this, Text);
}
