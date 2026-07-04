using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Transcriba.App.ViewModels;

public partial class SpeakerOptionViewModel : ViewModelBase
{
    private readonly Action<SpeakerOptionViewModel> _selectHandler;

    public Guid Id { get; }
    public string Name { get; }
    public string ColorHex { get; }

    [ObservableProperty]
    private bool _isActive;

    public SpeakerOptionViewModel(
        Guid id,
        string name,
        string colorHex,
        Action<SpeakerOptionViewModel> selectHandler)
    {
        Id = id;
        Name = name;
        ColorHex = colorHex;
        _selectHandler = selectHandler;
    }

    [RelayCommand]
    private void Select() => _selectHandler(this);
}
