using CommunityToolkit.Mvvm.ComponentModel;
using Verso.Core.Data.Entities;
using Verso.Core.Engine;

namespace Verso.App.ViewModels;

public partial class ModelDownloadModalViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string _message = "";

    public void Show(ModelQuality quality)
    {
        if (quality == ModelQuality.PtBrTurbo)
        {
            Message = "O modelo pt-BR Turbo (distil, ~538 MB) está sendo baixado do HuggingFace. " +
                "Isso pode levar alguns minutos e ocorre apenas na primeira transcrição com este modelo.";
        }
        else
        {
            var ggmlType = ModelManager.MapQualityToGgmlType(quality);
            var sizeMb = ModelManager.GetMinimumModelSizeBytes(ggmlType) / 1_000_000;
            var name = QualityDisplayName(quality);
            Message = $"O modelo {name} (~{sizeMb} MB) está sendo baixado via Whisper.net. " +
                "Isso pode levar alguns minutos e ocorre apenas na primeira transcrição com esta qualidade.";
        }

        IsOpen = true;
    }

    private static string QualityDisplayName(ModelQuality quality) => quality switch
    {
        ModelQuality.Tiny => "Tiny",
        ModelQuality.TinyEn => "Tiny (EN)",
        ModelQuality.Base => "Base",
        ModelQuality.BaseEn => "Base (EN)",
        ModelQuality.Standard => "Padrão (Small)",
        ModelQuality.SmallEn => "Small (EN)",
        ModelQuality.Medium => "Medium",
        ModelQuality.MediumEn => "Medium (EN)",
        ModelQuality.High => "Alta (LargeV3)",
        ModelQuality.LargeV1 => "LargeV1",
        ModelQuality.LargeV2 => "LargeV2",
        ModelQuality.LargeV3Turbo => "LargeV3 Turbo",
        _ => quality.ToString(),
    };

    public void Hide() => IsOpen = false;
}
