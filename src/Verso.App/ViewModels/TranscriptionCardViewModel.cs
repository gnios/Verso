using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Verso.Core.Catalogs;
using Verso.Core.Data.Entities;
using Verso.Core.Engine;
using Verso.Core.Export;
using Verso.Core.Services;

namespace Verso.App.ViewModels;

public partial class TranscriptionCardViewModel : ViewModelBase
{
    private readonly Action<Guid> _openHandler;
    private readonly Action<Guid>? _retryHandler;
    private readonly Action<Guid>? _deleteHandler;

    public Guid Id { get; }
    public string Title { get; }
    public string Icon { get; }
    public string Preview { get; }
    public IReadOnlyList<TranscriptionCardTagViewModel> Tags { get; }
    public string Date { get; }
    public string Duration { get; }
    public double DurationSeconds { get; }
    public string? EstimatedTimeLabel { get; private set; }
    public ModelQuality Quality { get; }
    public ExecutionDevice Device { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RetryCommand))]
    private TranscriptionStatus _status;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RetryCommand))]
    private string? _errorMessage;

    [ObservableProperty]
    private int? _progressPercent;

    [ObservableProperty]
    private string _progressStage = "";

    public string StatusLabel => Status switch
    {
        TranscriptionStatus.InProgress => "Em andamento",
        TranscriptionStatus.Done => "Concluída",
        _ => "Erro"
    };

    public bool IsInProgress => Status == TranscriptionStatus.InProgress;
    public bool IsDone => Status == TranscriptionStatus.Done;
    public bool IsError => Status == TranscriptionStatus.Error;
    public string StatusIcon => Status switch
    {
        TranscriptionStatus.InProgress => "⏳",
        TranscriptionStatus.Error => "❌",
        _ => "",
    };

    public bool CanRetry => IsError && _retryHandler is not null;
    public bool CanDelete => _deleteHandler is not null;

    public bool ShowProgress => IsInProgress;
    public bool IsProgressIndeterminate => IsInProgress && ProgressPercent is null;
    public int ProgressWidth => ProgressPercent ?? 0;
    public string ProgressLabel => ProgressStage switch
    {
        "loading" => "Carregando modelo…",
        "preparing" => "Preparando áudio…",
        "transcribing" => ProgressPercent is int p
            ? $"Transcrevendo… {p}%"
            : "Transcrevendo…",
        // "done" do motor = Whisper terminou; persistência ainda pode estar em andamento.
        "done" or "saving" => "Salvando resultados…",
        _ => "Em andamento…"
    };

    partial void OnProgressPercentChanged(int? value)
    {
        OnPropertyChanged(nameof(ProgressLabel));
        OnPropertyChanged(nameof(ProgressWidth));
        OnPropertyChanged(nameof(IsProgressIndeterminate));
    }
    partial void OnProgressStageChanged(string value)
    {
        OnPropertyChanged(nameof(ProgressLabel));
        UpdateEstimatedTime();
    }

    public TranscriptionCardViewModel(
        TranscriptionSummary summary,
        Action<Guid> openHandler,
        Action<Guid>? retryHandler = null,
        Action<Guid>? deleteHandler = null)
    {
        _openHandler = openHandler;
        _retryHandler = retryHandler;
        _deleteHandler = deleteHandler;

        Id = summary.Id;
        Title = summary.Title;
        Icon = string.IsNullOrWhiteSpace(summary.Icon) ? "📝" : summary.Icon;
        Quality = summary.Quality;
        Device = summary.Device;

        Tags = summary.Tags
            .Select(tag => new TranscriptionCardTagViewModel(tag, TagColorCatalog.GetColor(tag)))
            .ToList();

        Status = summary.Status;
        Duration = FormatDurationDisplay(summary.DurationSeconds);
        DurationSeconds = summary.DurationSeconds;
        UpdateEstimatedTime();
    }

    partial void OnStatusChanged(TranscriptionStatus value)
    {
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(IsInProgress));
        OnPropertyChanged(nameof(IsDone));
        OnPropertyChanged(nameof(IsError));
        OnPropertyChanged(nameof(CanRetry));
        OnPropertyChanged(nameof(ShowProgress));
        OnPropertyChanged(nameof(IsProgressIndeterminate));
        OnPropertyChanged(nameof(ProgressLabel));
        UpdateEstimatedTime();
    }

    partial void OnErrorMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(CanRetry));
    }

    private void UpdateEstimatedTime()
    {
        if (DurationSeconds <= 0 || !IsInProgress || !TranscriptionEstimator.IsLearned(Quality, Device))
        {
            EstimatedTimeLabel = null;
            return;
        }

        var rtf = TranscriptionEstimator.GetRtf(Quality, Device);
        var estimatedSeconds = DurationSeconds * rtf;
        var total = Math.Max(1, (int)estimatedSeconds);

        EstimatedTimeLabel = total < 60
            ? $"Estimativa: ~{total}s"
            : $"Estimativa: ~{total / 60}min {total % 60}s";

        OnPropertyChanged(nameof(EstimatedTimeLabel));
        OnPropertyChanged(nameof(ProgressLabel));
    }
    [RelayCommand]
    private void Open() => _openHandler(Id);

    [RelayCommand(CanExecute = nameof(CanRetry))]
    private void Retry()
    {
        if (_retryHandler is not null)
        {
            _retryHandler(Id);
        }
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void Delete() => _deleteHandler?.Invoke(Id);

    private static string FormatDurationDisplay(double seconds)
    {
        if (seconds <= 0)
            return "—";

        var duration = TimeSpan.FromSeconds(seconds);
        if (duration.TotalHours >= 1)
            return $"{(int)duration.TotalHours}h {duration.Minutes}min";

        if (duration.TotalMinutes >= 1)
            return $"{(int)duration.TotalMinutes} min";

        return TranscriptionTextFormatter.FormatDuration(duration);
    }
}
