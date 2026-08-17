using Verso.Core.Data.Entities;

namespace Verso.Core.Engine;

public interface IModelDownloadNotifier
{
    void DownloadStarted(ModelQuality quality);

    void DownloadStarted(string displayName, string detail);

    void DownloadCompleted();
}
