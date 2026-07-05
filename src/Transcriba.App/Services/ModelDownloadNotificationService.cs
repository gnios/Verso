using Transcriba.App.ViewModels;
using Transcriba.Core.Data.Entities;
using Transcriba.Core.Engine;

namespace Transcriba.App.Services;

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
