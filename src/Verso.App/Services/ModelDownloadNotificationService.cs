using Verso.App.ViewModels;
using Verso.Core.Data.Entities;
using Verso.Core.Engine;

namespace Verso.App.Services;

/// <summary>
/// Notificador de download de modelo para a UI React: emite eventos bridge
/// <c>model.download.started</c>/{title,message} e <c>model.download.completed</c>,
/// substituindo o modal Blazor (ModelDownloadModalViewModel fica para compatibilidade de testes).
/// </summary>
public sealed class ModelDownloadNotificationService : IModelDownloadNotifier
{
    private readonly BridgeHost _host;

    public ModelDownloadNotificationService(BridgeHost host)
    {
        _host = host;
    }

    public void DownloadStarted(ModelQuality quality)
    {
        var profile = ModelCatalog.Find(quality);
        var ggmlType = ModelManager.MapQualityToGgmlType(profile.Value);
        var sizeMb = ModelManager.GetMinimumModelSizeBytes(ggmlType) / 1_000_000;

        Send(
            "Preparando reconhecimento de fala…",
            $"Na primeira vez o Verso baixa os arquivos do perfil {profile.Label} (~{sizeMb} MB). " +
            "Isso acontece só uma vez por perfil e pode levar alguns minutos.");
    }

    public void DownloadStarted(string displayName, string detail) =>
        Send("Preparando reconhecimento de fala…", detail);

    public void DownloadCompleted() =>
        UiThread.Invoke(() => _host.Bridge.SendEvent("model.download.completed", null));

    private void Send(string title, string message) =>
        UiThread.Invoke(() => _host.Bridge.SendEvent("model.download.started", new { title, message }));
}