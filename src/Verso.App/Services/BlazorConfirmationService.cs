using System.ComponentModel;
using System.Threading.Tasks;

namespace Verso.App.Services;

/// <summary>
/// Implementação Blazor de <see cref="IConfirmationService"/>, no lugar do antigo
/// <c>WpfConfirmationService</c> (<c>MessageBox.Show</c> nativo, provisório desde a migração para
/// Blazor Hybrid — ver .specs/STATE.md AD-005). Padrão de "confirmation service" reativo:
/// <see cref="ConfirmAsync"/> guarda o texto atual (Title/Message) e um
/// <see cref="TaskCompletionSource{TResult}"/> pendente, expondo tudo via
/// <see cref="INotifyPropertyChanged"/>. Um único <c>ConfirmationDialog.razor</c>, montado uma vez
/// na shell (<c>MainLayout.razor</c>, integração central fora do escopo de T68), se inscreve no
/// evento, exibe o modal quando <see cref="IsOpen"/> vira <c>true</c> e resolve a pendência
/// chamando <see cref="Confirm"/>/<see cref="Cancel"/> conforme o clique do usuário.
///
/// Registrado como Singleton (mesma estratégia do <c>NewPageModalViewModel</c>/
/// <c>BlazorThemeApplicator</c>): só existe um diálogo de confirmação por vez no app, então não há
/// necessidade de escopo por navegação/tela.
/// </summary>
public sealed class BlazorConfirmationService : IConfirmationService, INotifyPropertyChanged
{
    private TaskCompletionSource<bool>? _pending;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsOpen { get; private set; }

    public string Title { get; private set; } = "";

    public string Message { get; private set; } = "";

    public Task<bool> ConfirmAsync(string title, string message)
    {
        // Nenhum fluxo real do app dispara duas confirmações em paralelo, mas por segurança
        // resolve qualquer pendência anterior como "cancelada" em vez de perdê-la silenciosamente.
        _pending?.TrySetResult(false);

        Title = title;
        Message = message;
        IsOpen = true;
        _pending = new TaskCompletionSource<bool>();

        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Message));
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
