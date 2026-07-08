using System.Diagnostics;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Verso.Core.Engine;

namespace Verso.Core.Media;

/// <summary>
/// Reprodutor de áudio baseado em NAudio + Media Foundation do Windows, no lugar do LibVLC.
///
/// Motivo da troca: uma investigação extensa de crashes recorrentes de "Internal CLR error
/// (0x80131506)" (corrupção real do heap gerenciado, confirmada via !VerifyHeap em memory
/// dumps) mostrou que o problema acontecia com qualquer backend de execução do Whisper.net
/// (CPU, CUDA e Vulkan todos reproduziram o mesmo crash, no mesmo endereço nativo), ou seja,
/// não era um bug de GPU. O LibVLC era a única biblioteca nativa que ficava permanentemente
/// ativa e cruzando a fronteira nativo/gerenciado (eventos e callbacks) em paralelo com o
/// processamento do Whisper em todos esses cenários, o que a tornou a suspeita mais forte
/// remanescente. NAudio usa interop muito mais simples (P/Invoke direto para WASAPI/waveOut e
/// Media Foundation do próprio Windows, sem um runtime nativo de terceiros com seu próprio
/// gerenciamento de memória/threads), reduzindo bastante essa superfície de risco.
/// </summary>
public sealed class NAudioPlaybackService : IMediaPlaybackService, IDisposable
{
    private static readonly HashSet<string> NativelySupportedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".wav", ".mp3", ".m4a", ".mp4", ".aac", ".wma" };

    private readonly FfmpegLocator _ffmpegLocator;
    private readonly object _sync = new();
    private readonly Timer _positionTimer;

    private AudioFileReader? _reader;
    private WaveOutEvent? _waveOut;
    private string? _tempTranscodedPath;
    private float _playbackRate = 1f;
    private int _volume = 100;
    private bool _disposed;

    public NAudioPlaybackService(FfmpegLocator ffmpegLocator)
    {
        _ffmpegLocator = ffmpegLocator;
        _positionTimer = new Timer(OnPositionTimerTick, null, Timeout.Infinite, Timeout.Infinite);
    }

    public event EventHandler<TimeSpan>? PositionChanged;

    public TimeSpan Duration
    {
        get
        {
            lock (_sync)
            {
                return _reader?.TotalTime ?? TimeSpan.Zero;
            }
        }
    }

    public bool IsPlaying
    {
        get
        {
            lock (_sync)
            {
                return _waveOut?.PlaybackState == PlaybackState.Playing;
            }
        }
    }

    public float PlaybackRate
    {
        get => _playbackRate;
        set
        {
            lock (_sync)
            {
                _playbackRate = value <= 0 ? 1f : value;
                if (_reader is null || _waveOut is null)
                {
                    return;
                }

                var wasPlaying = _waveOut.PlaybackState == PlaybackState.Playing;
                RebuildPipelineNoLock(resumePlaying: wasPlaying);
            }
        }
    }

    public int Volume
    {
        get => _volume;
        set
        {
            lock (_sync)
            {
                _volume = Math.Clamp(value, 0, 100);
                if (_reader is not null)
                {
                    _reader.Volume = _volume / 100f;
                }
            }
        }
    }

    public async Task LoadAsync(string filePath)
    {
        ObjectDisposedThrowIf(_disposed);
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        var extension = Path.GetExtension(filePath);
        var reader = NativelySupportedExtensions.Contains(extension) ? TryCreateReader(filePath) : null;
        string? tempPath = null;

        if (reader is null)
        {
            // Formatos que o Media Foundation do Windows não decodifica de forma confiável em
            // toda instalação (ex.: WebM/Opus, OGG/Vorbis), ou qualquer falha ao abrir
            // diretamente: transcodifica com o ffmpeg (já usado pelo pipeline de transcrição)
            // para um WAV temporário antes de tocar.
            tempPath = await TranscodeToWavAsync(filePath).ConfigureAwait(false);
            reader = new AudioFileReader(tempPath);
        }

        lock (_sync)
        {
            DisposePlaybackResourcesNoLock();
            _reader = reader;
            _reader.Volume = _volume / 100f;
            _tempTranscodedPath = tempPath;
            RebuildPipelineNoLock(resumePlaying: false);
        }
    }

    public Task UnloadAsync()
    {
        ObjectDisposedThrowIf(_disposed);
        lock (_sync)
        {
            DisposePlaybackResourcesNoLock();
        }

        return Task.CompletedTask;
    }

    public void Play()
    {
        ObjectDisposedThrowIf(_disposed);
        lock (_sync)
        {
            _waveOut?.Play();
        }

        StartPositionTimer();
    }

    public void Pause()
    {
        ObjectDisposedThrowIf(_disposed);
        lock (_sync)
        {
            _waveOut?.Pause();
        }

        StopPositionTimer();
    }

    public void SeekTo(TimeSpan position)
    {
        ObjectDisposedThrowIf(_disposed);

        TimeSpan clamped;
        lock (_sync)
        {
            if (_reader is null)
            {
                return;
            }

            clamped = TimeSpan.FromTicks(Math.Clamp(position.Ticks, 0, _reader.TotalTime.Ticks));
            _reader.CurrentTime = clamped;
        }

        PositionChanged?.Invoke(this, clamped);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        lock (_sync)
        {
            DisposePlaybackResourcesNoLock();
        }

        _positionTimer.Dispose();
    }

    private void RebuildPipelineNoLock(bool resumePlaying)
    {
        if (_reader is null)
        {
            return;
        }

        if (_waveOut is not null)
        {
            _waveOut.PlaybackStopped -= OnPlaybackStopped;
            _waveOut.Stop();
            _waveOut.Dispose();
        }

        _waveOut = new WaveOutEvent();
        _waveOut.PlaybackStopped += OnPlaybackStopped;

        ISampleProvider provider = _reader;
        if (Math.Abs(_playbackRate - 1f) > 0.001f)
        {
            var claimedRate = Math.Max(1000, (int)(_reader.WaveFormat.SampleRate * _playbackRate));
            provider = new WdlResamplingSampleProvider(
                new RateOverrideSampleProvider(_reader, claimedRate),
                _reader.WaveFormat.SampleRate);
        }

        _waveOut.Init(provider);

        if (resumePlaying)
        {
            _waveOut.Play();
        }
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        StopPositionTimer();

        lock (_sync)
        {
            if (_reader is null)
            {
                return;
            }

            if (_reader.Position >= _reader.Length)
            {
                PositionChanged?.Invoke(this, _reader.TotalTime);
            }
        }
    }

    private void OnPositionTimerTick(object? state)
    {
        if (_disposed)
        {
            return;
        }

        TimeSpan position;
        lock (_sync)
        {
            if (_waveOut is null || _reader is null || _waveOut.PlaybackState != PlaybackState.Playing)
            {
                return;
            }

            position = _reader.CurrentTime;
        }

        PositionChanged?.Invoke(this, position);
    }

    private void StartPositionTimer() => _positionTimer.Change(TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(250));

    private void StopPositionTimer() => _positionTimer.Change(Timeout.Infinite, Timeout.Infinite);

    private void DisposePlaybackResourcesNoLock()
    {
        StopPositionTimer();

        if (_waveOut is not null)
        {
            _waveOut.PlaybackStopped -= OnPlaybackStopped;
            _waveOut.Stop();
            _waveOut.Dispose();
            _waveOut = null;
        }

        _reader?.Dispose();
        _reader = null;

        if (_tempTranscodedPath is not null)
        {
            TryDeleteFile(_tempTranscodedPath);
            _tempTranscodedPath = null;
        }
    }

    private async Task<string> TranscodeToWavAsync(string inputPath)
    {
        var ffmpeg = _ffmpegLocator.EnsureFfmpeg();
        var tempPath = Path.Combine(Path.GetTempPath(), $"transcriba-playback-{Guid.NewGuid():N}.wav");

        var psi = new ProcessStartInfo
        {
            FileName = ffmpeg,
            Arguments = $"-nostdin -y -i \"{inputPath}\" -vn -acodec pcm_s16le \"{tempPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Não foi possível iniciar o ffmpeg para decodificar o áudio.");

        var stderrTask = process.StandardError.ReadToEndAsync();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync().ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        await stdoutTask.ConfigureAwait(false);

        if (process.ExitCode != 0 || !File.Exists(tempPath))
        {
            TryDeleteFile(tempPath);
            throw new InvalidOperationException($"ffmpeg falhou ao preparar o áudio para reprodução:\n{stderr}");
        }

        return tempPath;
    }

    private static AudioFileReader? TryCreateReader(string path)
    {
        try
        {
            return new AudioFileReader(path);
        }
        catch
        {
            return null;
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // best-effort: arquivo temporário de playback, sem impacto funcional se sobrar.
        }
    }

    private static void ObjectDisposedThrowIf(bool disposed)
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(NAudioPlaybackService));
        }
    }

    /// <summary>
    /// Faz o <see cref="WdlResamplingSampleProvider"/> enxergar a fonte como se estivesse numa
    /// taxa de amostragem diferente da real, sem reamostrar nada aqui. O resampler downstream
    /// então converte dessa taxa "mentirosa" para a taxa real de saída, o que faz a reprodução
    /// consumir a fonte mais rápido/devagar que o tempo real (efeito colateral: pitch muda um
    /// pouco em velocidades diferentes de 1x — troca aceitável para não depender de uma lib
    /// nativa de time-stretch, como fazia o LibVLC).
    /// </summary>
    private sealed class RateOverrideSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;

        public RateOverrideSampleProvider(ISampleProvider source, int overriddenSampleRate)
        {
            _source = source;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(overriddenSampleRate, source.WaveFormat.Channels);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count) => _source.Read(buffer, offset, count);
    }
}
