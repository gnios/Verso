namespace Verso.E2E.Support;

/// <summary>Gera WAV PCM mono 16-bit silencioso (arquivo grande, header válido).</summary>
public static class WavFixture
{
    public static string WriteSilentWav(string filePath, TimeSpan duration, int sampleRate = 16_000)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

        var sampleCount = (int)(duration.TotalSeconds * sampleRate);
        var dataBytes = sampleCount * 2; // 16-bit mono
        var fileSize = 44 + dataBytes;

        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read);
        using var bw = new BinaryWriter(fs);

        // RIFF header
        bw.Write("RIFF"u8.ToArray());
        bw.Write(fileSize - 8);
        bw.Write("WAVE"u8.ToArray());

        // fmt
        bw.Write("fmt "u8.ToArray());
        bw.Write(16);
        bw.Write((short)1); // PCM
        bw.Write((short)1); // mono
        bw.Write(sampleRate);
        bw.Write(sampleRate * 2); // byte rate
        bw.Write((short)2); // block align
        bw.Write((short)16); // bits

        // data
        bw.Write("data"u8.ToArray());
        bw.Write(dataBytes);

        var zeros = new byte[64 * 1024];
        var remaining = dataBytes;
        while (remaining > 0)
        {
            var chunk = Math.Min(zeros.Length, remaining);
            bw.Write(zeros, 0, chunk);
            remaining -= chunk;
        }

        return filePath;
    }
}
