using System;
using System.Globalization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Transcriba.App.Services;
using Transcriba.Core.Media;

namespace Transcriba.App.ViewModels;

public partial class PlayerBarViewModel : ViewModelBase
{
    private static readonly float[] Speeds = [1f, 1.25f, 1.5f, 2f];

    private readonly IMediaPlaybackService _playback;
    private int _speedIndex;

    public event EventHandler<TimeSpan>? PositionChanged;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private string _currentTimeDisplay = "00:00";

    [ObservableProperty]
    private string _totalTimeDisplay = "00:00";

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private string _speedLabel = "1×";

    [ObservableProperty]
    private int _volume = 100;

    public PlayerBarViewModel(IMediaPlaybackService playback)
    {
        _playback = playback;
        _playback.PositionChanged += OnPlaybackPositionChanged;
        Volume = _playback.Volume;
        _playback.PlaybackRate = Speeds[0];
    }

    public void SeekToTime(TimeSpan position)
    {
        _playback.SeekTo(position);
        var duration = _playback.Duration;
        var percent = duration > TimeSpan.Zero ? position.TotalSeconds / duration.TotalSeconds : 0;
        UpdateProgress(percent);
        UpdateCurrentTimeDisplay(position);
        PositionChanged?.Invoke(this, position);
    }

    public async Task LoadAsync(string? mediaPath)
    {
        if (string.IsNullOrWhiteSpace(mediaPath))
        {
            await UnloadAsync();
            return;
        }

        await _playback.LoadAsync(mediaPath);
        UpdateDurationDisplay();
        UpdateProgress(0);
        CurrentTimeDisplay = "00:00";
        IsPlaying = false;
    }

    public async Task UnloadAsync()
    {
        await _playback.UnloadAsync();
        TotalTimeDisplay = "00:00";
        CurrentTimeDisplay = "00:00";
        ProgressPercent = 0;
        IsPlaying = false;
    }

    [RelayCommand]
    private void TogglePlay()
    {
        if (_playback.IsPlaying)
        {
            _playback.Pause();
            IsPlaying = false;
            return;
        }

        _playback.Play();
        IsPlaying = true;
    }

    [RelayCommand]
    private void Seek(double percent)
    {
        var duration = _playback.Duration;
        if (duration <= TimeSpan.Zero)
        {
            return;
        }

        var clamped = Math.Clamp(percent, 0, 1);
        var position = TimeSpan.FromTicks((long)(duration.Ticks * clamped));
        _playback.SeekTo(position);
        UpdateProgress(clamped);
        UpdateCurrentTimeDisplay(position);
        PositionChanged?.Invoke(this, position);
    }

    [RelayCommand]
    private void CycleSpeed()
    {
        _speedIndex = (_speedIndex + 1) % Speeds.Length;
        var speed = Speeds[_speedIndex];
        _playback.PlaybackRate = speed;
        SpeedLabel = speed switch
        {
            1f => "1×",
            1.25f => "1.25×",
            1.5f => "1.5×",
            2f => "2×",
            _ => $"{speed.ToString("0.##", CultureInfo.InvariantCulture)}×"
        };
    }

    partial void OnVolumeChanged(int value) => _playback.Volume = value;

    internal void NotifyPlaybackStopped()
    {
        IsPlaying = false;
    }

    private void OnPlaybackPositionChanged(object? sender, TimeSpan position) =>
        // O serviço de playback dispara este evento a partir do timer de posição (thread pool),
        // não da UI. Repassar direto derruba o app com "The calling thread cannot access this
        // object because a different thread owns it" ao atualizar Bindings/Commands do Avalonia.
        UiThread.Invoke(() =>
        {
            var duration = _playback.Duration;
            var percent = duration > TimeSpan.Zero ? position.TotalSeconds / duration.TotalSeconds : 0;
            UpdateProgress(percent);
            UpdateCurrentTimeDisplay(position);
            PositionChanged?.Invoke(this, position);

            if (!_playback.IsPlaying)
            {
                IsPlaying = false;
            }

            if (duration > TimeSpan.Zero && position >= duration)
            {
                IsPlaying = false;
            }
        });

    private void UpdateDurationDisplay()
    {
        TotalTimeDisplay = FormatTime(_playback.Duration);
    }

    private void UpdateProgress(double percent) => ProgressPercent = Math.Clamp(percent * 100, 0, 100);

    private void UpdateCurrentTimeDisplay(TimeSpan position) =>
        CurrentTimeDisplay = FormatTime(position);

    private static string FormatTime(TimeSpan time)
    {
        if (time.TotalHours >= 1)
        {
            return $"{(int)time.TotalHours}:{time.Minutes:D2}:{time.Seconds:D2}";
        }

        return $"{time.Minutes:D2}:{time.Seconds:D2}";
    }
}
