using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Transcriba.Core.Catalogs;
using Transcriba.Core.Data.Entities;
using Transcriba.Core.Export;
using Transcriba.Core.Services;

namespace Transcriba.App.ViewModels;

public partial class TranscriptionCardViewModel : ViewModelBase
{
    private readonly Action<Guid> _openHandler;

    public Guid Id { get; }
    public string Title { get; }
    public string Icon { get; }
    public string Preview { get; }
    public IReadOnlyList<TranscriptionCardTagViewModel> Tags { get; }
    public string Date { get; }
    public string Duration { get; }

    [ObservableProperty]
    private TranscriptionStatus _status;

    public string StatusLabel => Status switch
    {
        TranscriptionStatus.InProgress => "Em andamento",
        TranscriptionStatus.Done => "Concluída",
        _ => "Erro"
    };

    public bool IsInProgress => Status == TranscriptionStatus.InProgress;
    public bool IsDone => Status == TranscriptionStatus.Done;

    public TranscriptionCardViewModel(TranscriptionSummary summary, Action<Guid> openHandler)
    {
        _openHandler = openHandler;
        Id = summary.Id;
        Title = summary.Title;
        Icon = string.IsNullOrWhiteSpace(summary.Icon) ? "📝" : summary.Icon;
        Preview = summary.Preview;
        Tags = summary.Tags
            .Select(tag => new TranscriptionCardTagViewModel(tag, TagColorCatalog.GetColor(tag)))
            .ToList();
        Status = summary.Status;
        Date = summary.Date.ToString("d MMM yyyy", CultureInfo.GetCultureInfo("pt-BR"));
        Duration = FormatDurationDisplay(summary.DurationSeconds);
    }

    partial void OnStatusChanged(TranscriptionStatus value)
    {
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(IsInProgress));
        OnPropertyChanged(nameof(IsDone));
    }

    [RelayCommand]
    private void Open() => _openHandler(Id);

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
