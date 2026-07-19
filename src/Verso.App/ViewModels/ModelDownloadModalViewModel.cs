using CommunityToolkit.Mvvm.ComponentModel;
using Verso.Core.Data.Entities;
using Verso.Core.Engine;

namespace Verso.App.ViewModels;

public partial class ModelDownloadModalViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string _title = "Preparando reconhecimento de fala…";

    [ObservableProperty]
    private string _message = "";

    public void Show(ModelQuality quality)
    {
        var profile = ModelCatalog.Find(quality);
        var ggmlType = ModelManager.MapQualityToGgmlType(profile.Value);
        var sizeMb = ModelManager.GetMinimumModelSizeBytes(ggmlType) / 1_000_000;

        Title = "Preparando reconhecimento de fala…";
        Message =
            $"Na primeira vez o Verso baixa os arquivos do perfil {profile.Label} (~{sizeMb} MB). " +
            "Isso acontece só uma vez por perfil e pode levar alguns minutos.";

        IsOpen = true;
    }

    public void Hide() => IsOpen = false;
}
