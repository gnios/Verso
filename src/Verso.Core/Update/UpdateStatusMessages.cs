namespace Verso.Core.Update;

public static class UpdateStatusMessages
{
    public static string For(UpdateStatus status, bool hasChannel, string? availableVersion = null)
    {
        if (!hasChannel)
            return "Atualização automática nas versões instaladas e no zip de release.";

        var target = Normalize(availableVersion);

        return status switch
        {
            UpdateStatus.Checking => "Verificando atualizações…",
            UpdateStatus.Downloading => target is null
                ? "Baixando atualização…"
                : $"Baixando {target}…",
            UpdateStatus.Ready => target is null
                ? "Atualização pronta — clique em Atualizar na barra lateral."
                : $"Atualização {target} pronta — clique em Atualizar na barra lateral.",
            UpdateStatus.Applying => "Aplicando atualização…",
            UpdateStatus.UpToDate => "Verso está atualizado.",
            UpdateStatus.Failed => "Não foi possível atualizar agora. Tentaremos de novo na próxima abertura.",
            _ => "Aguardando verificação de atualizações."
        };
    }

    public static string RestartConfirmTitle => "Reiniciar para atualizar";

    public static string RestartConfirm(string? availableVersion)
    {
        var target = Normalize(availableVersion) ?? "nova versão";
        return $"O Verso precisa fechar para instalar a versão {target} e vai abrir de novo em seguida. Continuar?";
    }

    public static bool CanRequestUpdate(bool hasChannel, UpdateStatus status) =>
        hasChannel
        && status is not UpdateStatus.Checking
        && status is not UpdateStatus.Downloading
        && status is not UpdateStatus.Applying;

    public static string ActionLabel(bool hasChannel, UpdateStatus status, string? availableVersion = null, string? localVersion = null)
    {
        if (!hasChannel)
            return "Atualizar";

        var target = Normalize(availableVersion);
        var offerTarget = target is not null
            && (string.IsNullOrWhiteSpace(localVersion) || AppVersion.IsNewer(target, localVersion));

        return status switch
        {
            UpdateStatus.Checking => "Verificando…",
            UpdateStatus.Downloading => target is null ? "Baixando…" : $"Baixando {target}…",
            UpdateStatus.Applying => "Aplicando…",
            UpdateStatus.Ready => offerTarget ? $"Atualizar para {target}" : "Atualizar agora",
            UpdateStatus.Failed => offerTarget ? $"Atualizar para {target}" : "Tentar atualizar",
            _ => "Verificar atualizações"
        };
    }

    public static string ActionTitle(bool hasChannel, UpdateStatus status, string? availableVersion = null, string? localVersion = null)
    {
        if (!hasChannel)
            return "Atualização disponível nas versões instaladas e no zip de release";

        var target = Normalize(availableVersion);
        var offerTarget = target is not null
            && (string.IsNullOrWhiteSpace(localVersion) || AppVersion.IsNewer(target, localVersion));

        return status switch
        {
            UpdateStatus.Checking => "Verificando atualizações…",
            UpdateStatus.Downloading => target is null
                ? "Baixando atualização…"
                : $"Baixando a versão {target}…",
            UpdateStatus.Applying => "Aplicando atualização…",
            UpdateStatus.Ready => offerTarget
                ? $"Reiniciar e atualizar para {target}"
                : "Reiniciar e aplicar a atualização",
            UpdateStatus.Failed => offerTarget
                ? $"Tentar atualizar para {target}"
                : "Tentar baixar a atualização de novo",
            _ => "Verificar e instalar a versão mais recente"
        };
    }

    private static string? Normalize(string? availableVersion) =>
        string.IsNullOrWhiteSpace(availableVersion) ? null : availableVersion.Trim();
}
