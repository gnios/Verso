using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Transcriba.App.Services;
using Transcriba.Core.Data;
using Transcriba.Core.Data.Entities;
using Transcriba.Core.Engine;

namespace Transcriba.App.ViewModels;

public partial class EditorViewModel : ViewModelBase
{
    private readonly IServiceScopeFactory _scopeFactory;
    private Guid _transcriptionId;

    public ObservableCollection<EditorSegmentViewModel> Segments { get; } = [];

    [ObservableProperty]
    private bool _isInProgress;

    [ObservableProperty]
    private bool _isError;

    [ObservableProperty]
    private bool _hasSegments;

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private string _title = "";

    public EditorViewModel(IServiceScopeFactory scopeFactory, IServiceProvider serviceProvider)
    {
        _scopeFactory = scopeFactory;

        if (serviceProvider.GetService<TranscriptionQueueService>() is { } queueService)
        {
            queueService.StatusChanged += OnQueueStatusChanged;
        }
    }

    public void Initialize(NavigationParameter? parameter)
    {
        _transcriptionId = parameter?.TranscriptionId ?? Guid.Empty;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (_transcriptionId == Guid.Empty)
        {
            Segments.Clear();
            IsInProgress = false;
            HasSegments = false;
            StatusMessage = "";
            Title = "";
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TranscribaDbContext>>();
        await using var context = await factory.CreateDbContextAsync();

        var transcription = await context.Transcriptions
            .Include(t => t.Segments)
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == _transcriptionId);

        if (transcription is null)
        {
            return;
        }

        Title = transcription.Title;
        IsInProgress = transcription.Status == TranscriptionStatus.InProgress;
        IsError = transcription.Status == TranscriptionStatus.Error;
        StatusMessage = transcription.Status switch
        {
            TranscriptionStatus.InProgress => "Transcrição em andamento…",
            TranscriptionStatus.Error => transcription.ErrorMessage ?? "Erro na transcrição",
            _ => "",
        };

        Segments.Clear();
        if (transcription.Status == TranscriptionStatus.Done)
        {
            foreach (var segment in transcription.Segments.OrderBy(s => s.SortOrder))
            {
                Segments.Add(new EditorSegmentViewModel(segment));
            }
        }

        HasSegments = Segments.Count > 0;
    }

    private void OnQueueStatusChanged(object? sender, TranscriptionStatusChangedEventArgs e)
    {
        if (e.TranscriptionId != _transcriptionId)
        {
            return;
        }

        if (e.Status is TranscriptionStatusChanged.Done or TranscriptionStatusChanged.Error)
        {
            _ = LoadAsync();
            return;
        }

        IsInProgress = true;
        IsError = false;
        StatusMessage = "Transcrição em andamento…";
        Segments.Clear();
        HasSegments = false;
    }
}

public sealed class EditorSegmentViewModel
{
    public double StartSeconds { get; }
    public double EndSeconds { get; }
    public string Text { get; }

    public EditorSegmentViewModel(Segment segment)
    {
        StartSeconds = segment.StartSeconds;
        EndSeconds = segment.EndSeconds;
        Text = segment.Text;
    }
}
