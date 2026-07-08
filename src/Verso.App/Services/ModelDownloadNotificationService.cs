using Verso.App.ViewModels;
using Verso.Core.Data.Entities;
using Verso.Core.Engine;

namespace Verso.App.Services;

public sealed class ModelDownloadNotificationService : IModelDownloadNotifier
{
    private readonly ModelDownloadModalViewModel _modal;

    public ModelDownloadNotificationService(ModelDownloadModalViewModel modal)
    {
        _modal = modal;
    }

    public void DownloadStarted(ModelQuality quality) =>
        UiThread.Invoke(() => _modal.Show(quality));

    public void DownloadCompleted() =>
        UiThread.Invoke(() => _modal.Hide());
}
