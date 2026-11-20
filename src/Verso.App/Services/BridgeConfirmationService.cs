using System;
using System.ComponentModel;
using System.Threading.Tasks;

namespace Verso.App.Services;

/// <summary>
/// Implementação de <see cref="IConfirmationService"/> para a UI React: mesma estratégia do
/// antigo serviço Blazor (padrão reativo com <see cref="TaskCompletionSource{TResult}"/> pendente),
/// mas quem observa é a bridge — o <see cref="VersoBridge"/> envia <c>ui.confirm</c> ao JS quando
/// <see cref="IsOpen"/> vira <c>true</c> e resolve a pendência via <c>ui.confirm.respond</c>.
/// </summary>
public sealed class BridgeConfirmationService : IConfirmationService, INotifyPropertyChanged
{
    private TaskCompletionSource<bool>? _pending;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsOpen { get; private set; }

    public string Title { get; private set; } = "";

    public string Message { get; private set; } = "";

    public bool IsDanger { get; private set; } = true;

    public Task<bool> ConfirmAsync(string title, string message, bool danger = true)
    {
        // Nenhum fluxo real do app dispara duas confirmações em paralelo, mas por segurança
        // resolve qualquer pendência anterior como "cancelada" em vez de perdê-la silenciosamente.
        _pending?.TrySetResult(false);

        Title = title;
        Message = message;
        IsDanger = danger;
        IsOpen = true;
        _pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Message));
        OnPropertyChanged(nameof(IsDanger));
        OnPropertyChanged(nameof(IsOpen));

        return _pending.Task;
    }

    public void Confirm() => Resolve(true);

    public void Cancel() => Resolve(false);

    private void Resolve(bool result)
    {
        IsOpen = false;
        OnPropertyChanged(nameof(IsOpen));

        var pending = _pending;
        _pending = null;
        pending?.TrySetResult(result);
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}