using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Verso.App.Services;

namespace Verso.App.ViewModels;

public sealed record LiveSegmentViewModel(string Speaker, string Text, bool IsSpeakerA);

public partial class WaveformBarViewModel : ObservableObject
{
    [ObservableProperty]
    private double _height = 6;

    [ObservableProperty]
    private bool _isActive;
}

public partial class RecordingViewModel : ViewModelBase, IDisposable
{
    private const int WaveformBarCount = 48;

    private static readonly (string Speaker, string Text)[] LivePhrases =
    [
        ("Entrevistador", "Então, quando é que o senhor começou a se interessar por esses temas?"),
        ("Dr. Silva", "Foi ainda na graduação, por volta de 2003, quando fiz uma disciplina eletiva sobre políticas públicas."),
        ("Entrevistador", "E como foi essa experiência inicial?"),
        ("Dr. Silva", "Foi transformadora. Percebi que a pesquisa acadêmica podia ter um impacto real."),
        ("Entrevistador", "Você sentiu alguma dificuldade em conciliar a teoria com a prática?"),
        ("Dr. Silva", "Com certeza. A maior parte da literatura era estrangeira e os contextos eram muito diferentes."),
    ];

    private readonly NavigationService _navigation;
    private readonly Random _random = new();
    private CancellationTokenSource? _sessionCts;
    private int _elapsedSeconds;
    private int _livePhraseIndex;

    public ObservableCollection<WaveformBarViewModel> WaveformBars { get; } = [];
    public ObservableCollection<LiveSegmentViewModel> LiveSegments { get; } = [];

    public IReadOnlyList<string> MicrophoneOptions { get; } =
    [
        "Microfone interno",
        "Microfone USB — Yeti",
    ];

    public IReadOnlyList<string> SourceOptions { get; } =
    [
        "Somente áudio",
        "Câmera FaceTime HD",
    ];

    [ObservableProperty]
    private string _timerDisplay = "00:00";

    [ObservableProperty]
    private string _statusText = "Pronto para gravar";

    [ObservableProperty]
    private bool _isLive;

    [ObservableProperty]
    private bool _isRecording;

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    private bool _showPauseStop;

    [ObservableProperty]
    private bool _showLiveSection;

    [ObservableProperty]
    private bool _isRecordButtonRecording;

    [ObservableProperty]
    private string _selectedMicrophone = "Microfone interno";

    [ObservableProperty]
    private string _selectedSource = "Somente áudio";

    internal TimeSpan StopNavigationDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    public RecordingViewModel(NavigationService navigation)
    {
        _navigation = navigation;
        InitializeWaveform(idle: true);
    }

    [RelayCommand]
    private void StartRecording()
    {
        if (IsRecording)
        {
            return;
        }

        _elapsedSeconds = 0;
        _livePhraseIndex = 0;
        UpdateTimerDisplay();
        LiveSegments.Clear();

        IsRecording = true;
        IsPaused = false;
        IsLive = true;
        ShowPauseStop = true;
        ShowLiveSection = true;
        IsRecordButtonRecording = true;
        StatusText = "Gravando…";

        AddLivePhrase();
        StartBackgroundLoops();
    }

    [RelayCommand]
    private void TogglePause()
    {
        if (!IsRecording)
        {
            return;
        }

        IsPaused = !IsPaused;
        IsLive = !IsPaused;
        StatusText = IsPaused ? "Pausado" : "Gravando…";
    }

    [RelayCommand]
    private async Task StopRecordingAsync()
    {
        if (!IsRecording)
        {
            return;
        }

        StopBackgroundLoops();
        ResetRecordingUi();
        await Task.Delay(StopNavigationDelay);
        _navigation.NavigateTo(ScreenKey.Editor);
    }

    internal void ProcessTimerTick()
    {
        if (!IsRecording)
        {
            return;
        }

        AnimateWaveform(recording: true);

        if (IsPaused)
        {
            return;
        }

        _elapsedSeconds++;
        UpdateTimerDisplay();
    }

    internal void ProcessLivePhraseTick()
    {
        if (!IsRecording)
        {
            return;
        }

        AddLivePhrase();
    }

    private void StartBackgroundLoops()
    {
        StopBackgroundLoops();
        _sessionCts = new CancellationTokenSource();
        var token = _sessionCts.Token;

        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (await timer.WaitForNextTickAsync(token))
            {
                await UiThread.InvokeAsync(ProcessTimerTick);
            }
        }, token);

        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(4500));
            while (await timer.WaitForNextTickAsync(token))
            {
                await UiThread.InvokeAsync(ProcessLivePhraseTick);
            }
        }, token);
    }

    private void StopBackgroundLoops()
    {
        _sessionCts?.Cancel();
        _sessionCts?.Dispose();
        _sessionCts = null;
    }

    private void ResetRecordingUi()
    {
        IsRecording = false;
        IsPaused = false;
        IsLive = false;
        ShowPauseStop = false;
        ShowLiveSection = false;
        IsRecordButtonRecording = false;
        StatusText = "Pronto para gravar";
        _elapsedSeconds = 0;
        UpdateTimerDisplay();
        InitializeWaveform(idle: true);
        LiveSegments.Clear();
    }

    private void InitializeWaveform(bool idle)
    {
        if (WaveformBars.Count == 0)
        {
            for (var i = 0; i < WaveformBarCount; i++)
            {
                WaveformBars.Add(new WaveformBarViewModel());
            }
        }

        foreach (var bar in WaveformBars)
        {
            bar.Height = idle ? 3 + _random.NextDouble() * 8 : 3 + _random.NextDouble() * 8;
            bar.IsActive = false;
        }
    }

    private void AnimateWaveform(bool recording)
    {
        foreach (var bar in WaveformBars)
        {
            bar.Height = recording
                ? 3 + _random.NextDouble() * 50
                : 3 + _random.NextDouble() * 8;
            bar.IsActive = recording && _random.NextDouble() > 0.4;
        }
    }

    private void AddLivePhrase()
    {
        if (_livePhraseIndex >= LivePhrases.Length)
        {
            _livePhraseIndex = 0;
        }

        var phrase = LivePhrases[_livePhraseIndex++];
        LiveSegments.Add(new LiveSegmentViewModel(
            phrase.Speaker,
            phrase.Text,
            phrase.Speaker == "Entrevistador"));
    }

    private void UpdateTimerDisplay()
    {
        var minutes = _elapsedSeconds / 60;
        var seconds = _elapsedSeconds % 60;
        TimerDisplay = $"{minutes:00}:{seconds:00}";
    }

    public void Dispose() => StopBackgroundLoops();
}
