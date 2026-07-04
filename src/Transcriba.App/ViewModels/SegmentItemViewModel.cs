using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Transcriba.Core.Data.Entities;

namespace Transcriba.App.ViewModels;

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
        SpeakerName = speaker?.Name ?? "";
        SpeakerColorHex = speaker?.ColorHex ?? "#2eaadc";
    }

    internal void NotifyTextChanged(string text) => Text = text;

    [RelayCommand]
    private void Click() => _editor.OnSegmentClicked(this);

    internal void NotifyFocused(int caretIndex) => _editor.OnSegmentFocused(this, caretIndex);

    internal void CommitText() => _editor.OnSegmentTextCommitted(this, Text);
}
