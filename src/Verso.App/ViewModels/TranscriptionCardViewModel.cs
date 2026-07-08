using System;

using System.Collections.Generic;

using System.Globalization;

using System.Linq;

using CommunityToolkit.Mvvm.ComponentModel;

using CommunityToolkit.Mvvm.Input;

using Verso.Core.Catalogs;

using Verso.Core.Data.Entities;

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

    public bool CanRetry => IsError && _retryHandler is not null;
    public bool CanDelete => _deleteHandler is not null;

    public bool ShowProgress => IsInProgress;
    public bool IsProgressIndeterminate => IsInProgress && ProgressPercent is null;
    public int ProgressWidth => ProgressPercent ?? 0;
    public string ProgressLabel => ProgressStage switch
    {
        "loading" => "Carregando modelo…",
        "preparing" => "Preparando áudio…",
        "transcribing" => ProgressPercent is int p ? $"Transcrevendo… {p}%" : "Transcrevendo…",
        "done" => "Concluído",
        _ => "Em andamento…"
    };
    partial void OnProgressPercentChanged(int? value) => OnPropertyChanged(nameof(ProgressLabel));
    partial void OnProgressStageChanged(string value) => OnPropertyChanged(nameof(ProgressLabel));



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

        Preview = summary.Preview;

        Tags = summary.Tags

            .Select(tag => new TranscriptionCardTagViewModel(tag, TagColorCatalog.GetColor(tag)))

            .ToList();

        Status = summary.Status;

        ErrorMessage = summary.ErrorMessage;

        Date = summary.Date.ToString("d MMM yyyy", CultureInfo.GetCultureInfo("pt-BR"));

        Duration = FormatDurationDisplay(summary.DurationSeconds);

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
    }



    partial void OnErrorMessageChanged(string? value)

    {

        OnPropertyChanged(nameof(CanRetry));

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

        {

            return "—";

        }



        var duration = TimeSpan.FromSeconds(seconds);

        if (duration.TotalHours >= 1)

        {

            return $"{(int)duration.TotalHours}h {duration.Minutes}min";

        }



        if (duration.TotalMinutes >= 1)

        {

            return $"{(int)duration.TotalMinutes} min";

        }



        return TranscriptionTextFormatter.FormatDuration(duration);

    }

}
