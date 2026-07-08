using CommunityToolkit.Mvvm.ComponentModel;
using Verso.Core.Data.Entities;

namespace Verso.App.ViewModels;

public partial class ModelDownloadModalViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string _message = "";

    public void Show(ModelQuality quality)
    {
        Message = quality switch
        {
            ModelQuality.PtBrTurbo =>
                "O modelo pt-BR Turbo (distil, ~538 MB) está sendo baixado do HuggingFace. " +
                "Isso pode levar alguns minutos e ocorre apenas na primeira transcrição com este modelo.",
            ModelQuality.High =>
                "O modelo de qualidade Alta (~3 GB) está sendo baixado via Whisper.net. " +
                "Isso pode levar alguns minutos e ocorre apenas na primeira transcrição com esta qualidade.",
            _ =>
                "O modelo de qualidade Padrão (~465 MB) está sendo baixado via Whisper.net. " +
                "Isso ocorre apenas na primeira transcrição com esta qualidade.",
        };
        IsOpen = true;
    }

    public void Hide() => IsOpen = false;
}
