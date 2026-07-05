using Transcriba.Core.Data.Entities;

namespace Transcriba.Core.Engine;

public interface IModelDownloadNotifier
{
    void DownloadStarted(ModelQuality quality);

    void DownloadCompleted();
}
