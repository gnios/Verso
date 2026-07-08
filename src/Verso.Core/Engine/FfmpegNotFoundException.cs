namespace Verso.Core.Engine;

public sealed class FfmpegNotFoundException : Exception
{
    public FfmpegNotFoundException()
        : base(
            "ffmpeg não encontrado e a instalação automática falhou.\n" +
            "Instale manualmente: winget install Gyan.FFmpeg\n" +
            "Depois reabra o terminal e tente novamente.")
    {
    }

    public FfmpegNotFoundException(string message) : base(message)
    {
    }
}
