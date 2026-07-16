namespace Verso.Core.Engine;

// Resolve o número efetivo de threads de transcrição (AD-003): env var
// VERSO_WHISPER_N_THREADS > UserSettings.MaxTranscriptionThreads > default (0 =
// automático, ChunkPlanner decide). Valor inválido (não-inteiro, ≤ 0) na env var é
// ignorado e cai para o próximo nível.
public static class TranscriptionThreadsResolver
{
    public const string EnvVarName = "VERSO_WHISPER_N_THREADS";

    public static int Resolve(int settingsMaxThreads)
    {
        var envValue = Environment.GetEnvironmentVariable(EnvVarName);
        if (int.TryParse(envValue, out var parsedEnvThreads) && parsedEnvThreads >= 1)
        {
            return parsedEnvThreads;
        }

        return settingsMaxThreads > 0 ? settingsMaxThreads : 0;
    }
}
