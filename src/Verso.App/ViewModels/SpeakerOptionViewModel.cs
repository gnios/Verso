using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Verso.App.ViewModels;

public partial class SpeakerOptionViewModel : ViewModelBase
{
    private readonly Action<SpeakerOptionViewModel> _selectHandler;

    public Guid Id { get; }

    [ObservableProperty]
    private string _name;

    public string ColorHex { get; }

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _isEditing;

    public SpeakerOptionViewModel(
        Guid id,
        string name,
        string colorHex,
        Action<SpeakerOptionViewModel> selectHandler)
    {
        Id = id;
        _name = name;
        ColorHex = colorHex;
        _selectHandler = selectHandler;
    }

    [RelayCommand]
    private void Select() => _selectHandler(this);
}