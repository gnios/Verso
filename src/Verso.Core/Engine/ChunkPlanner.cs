using Verso.Core.Data.Entities;

namespace Verso.Core.Engine;

/// <summary>
/// Trecho de áudio (já sem silêncio) posicionado dentro de uma parte concatenada:
/// onde ele começa na parte (segundos) e seu offset original no áudio completo.
/// Usado para mapear timestamps do whisper de volta ao tempo real.
/// </summary>
public sealed record ChunkSpan(double OriginalOffsetSec, double StartInPartSec, double DurationSec);

/// <summary>
/// Uma parte a ser decodificada em uma única chamada do whisper: samples concatenados
/// dos trechos que a compõem + a lista de ChunkSpan para mapear timestamps.
/// </summary>
public sealed record WhisperPart(float[] Samples, IReadOnlyList<ChunkSpan> Chunks);

public static class ChunkPlanner
{
    // O whisper.net (e o whisper.cpp por baixo) não é seguro para decodificação nativa
    // concorrente: chamadas paralelas a whisper_init_state/whisper_full_with_state a
    // partir do mesmo contexto/modelo podem corromper memória nativa e derrubar o
    // processo com "Internal CLR error"/access violation em pontos aparentemente
    // aleatórios, muito depois da transcrição já ter "terminado com sucesso" (ver
    // sandrohanea/whisper.net#341 — issue em aberto, reproduzida até na CI oficial da
    // lib rodando só em CPU, sem GPU envolvido). Uma tentativa anterior de mitigação
    // (serializar apenas o primeiro MoveNextAsync de cada worker) não foi sufficiente
    // na prática. Por isso as partes são sempre decodificadas em série (Paralelismo
    // = 1); cada decodificação individual usa todos os núcleos disponíveis (o próprio
    // whisper.cpp paraleliza internamente por thread dentro de uma única chamada),
    // que é o modo de uso suportado e testado pela biblioteca.
    public static (int MaxPartes, int Paralelismo, int ThreadsPorJob) CalculateParallelLimits(ExecutionDevice device)
    {
        var threads = Environment.ProcessorCount;
        var maxPartes = Math.Clamp(threads, 4, 8);
        return (maxPartes, 1, threads);
    }

    // Agrupa trechos (já sem silêncio) em até maxPartes partes, concatenando os samples
    // de cada trecho. Como a concatenação REMOVE o silêncio entre os trechos, o timeline
    // interno de uma parte é mais COMPACTO que o áudio original — sem o mapeamento por
    // ChunkSpan, os timestamps do whisper para trechos depois do primeiro da parte
    // ficariam adiantados pelo silêncio acumulado removido, dessincronizando do áudio
    // (sintoma: começa certo, no fim totalmente fora). Cada ChunkSpan registra o offset
    // original do trecho e onde ele começa dentro da parte, para mapear de volta.
    public static List<WhisperPart> GroupParts(
        List<(double OffsetSec, float[] Samples)> trechos,
        int maxPartes)
    {
        if (trechos.Count <= maxPartes || maxPartes <= 1)
        {
            return trechos
                .Select(t => new WhisperPart(
                    t.Samples,
                    new[] { new ChunkSpan(t.OffsetSec, 0, t.Samples.Length / (double)AudioLoader.SampleRate) }))
                .ToList();
        }

        var totalSamples = trechos.Sum(t => (long)t.Samples.Length);
        var targetSamples = totalSamples / (double)maxPartes;

        var agrupadas = new List<WhisperPart>();
        var buffer = new List<(double OffsetSec, float[] Samples)>();
        var bufferLen = 0;

        foreach (var trecho in trechos)
        {
            buffer.Add(trecho);
            bufferLen += trecho.Samples.Length;

            var ultimaParte = agrupadas.Count >= maxPartes - 1;
            var atingiuAlvo = bufferLen >= targetSamples;

            if (!ultimaParte && atingiuAlvo)
            {
                agrupadas.Add(BuildPart(buffer));
                buffer.Clear();
                bufferLen = 0;
            }
        }

        if (bufferLen > 0)
        {
            agrupadas.Add(BuildPart(buffer));
        }

        return agrupadas;
    }

    private static WhisperPart BuildPart(List<(double OffsetSec, float[] Samples)> chunks)
    {
        var totalLen = chunks.Sum(c => c.Samples.Length);
        var samples = new float[totalLen];
        var spans = new List<ChunkSpan>(chunks.Count);
        var pos = 0;

        foreach (var (offset, chunk) in chunks)
        {
            Array.Copy(chunk, 0, samples, pos, chunk.Length);
            spans.Add(new ChunkSpan(
                offset,
                pos / (double)AudioLoader.SampleRate,
                chunk.Length / (double)AudioLoader.SampleRate));
            pos += chunk.Length;
        }

        return new WhisperPart(samples, spans);
    }

    // Mapeia um timestamp do whisper (segundos dentro da parte concatenada) de volta
    // para o tempo real no áudio original, usando o ChunkSpan do trecho onde o tempo
    // cai. Segmentos que caiem no último trecho (ou além) usam o último ChunkSpan.
    internal static double MapToRealTime(double timeInPart, IReadOnlyList<ChunkSpan> chunks)
    {
        if (chunks.Count == 0)
        {
            return timeInPart;
        }

        foreach (var c in chunks)
        {
            if (timeInPart < c.StartInPartSec + c.DurationSec)
            {
                return c.OriginalOffsetSec + (timeInPart - c.StartInPartSec);
            }
        }

        var last = chunks[^1];
        return last.OriginalOffsetSec + (timeInPart - last.StartInPartSec);
    }
}