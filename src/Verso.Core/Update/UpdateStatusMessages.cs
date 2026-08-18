namespace Verso.Core.Update;

public static class UpdateStatusMessages
{
    public static string For(UpdateStatus status, bool hasChannel)
    {
        if (!hasChannel)
            return "Atualização automática nas versões instaladas e no zip de release.";

        return status switch
        {
            UpdateStatus.Checking => "Verificando atualizações…",
            UpdateStatus.Downloading => "Baixando atualização…",
            UpdateStatus.Ready => "Atualização pronta — será aplicada ao reiniciar.",
            UpdateStatus.Applying => "Aplicando atualização…",
            UpdateStatus.UpToDate => "Verso está atualizado.",
            UpdateStatus.Failed => "Não foi possível atualizar agora. Tentaremos de novo na próxima abertura.",
            _ => "Aguardando verificação de atualizações."
        };
    }

    public static bool CanRequestUpdate(bool hasChannel, UpdateStatus status) =>
        hasChannel
        && status is not UpdateStatus.Checking
        && status is not UpdateStatus.Downloading
        && status is not UpdateStatus.Applying;

    public static string ActionTitle(bool hasChannel, UpdateStatus status)
    {
        if (!hasChannel)
            return "Atualização disponível nas versões instaladas e no zip de release";

        return status switch
        {
            UpdateStatus.Checking => "Verificando atualizações…",
            UpdateStatus.Downloading => "Baixando atualização…",
            UpdateStatus.Applying => "Aplicando atualização…",
            UpdateStatus.Ready => "Reiniciar e aplicar a atualização",
            _ => "Verificar e instalar a versão mais recente"
        };
    }
}
