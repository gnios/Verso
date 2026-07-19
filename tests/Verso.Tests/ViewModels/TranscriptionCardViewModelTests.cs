using System.Globalization;
using Verso.App.ViewModels;
using Verso.Core.Data.Entities;
using Verso.Core.Engine;
using Verso.Core.Services;

namespace Verso.Tests.ViewModels;

public class TranscriptionCardViewModelTests
{
    [Fact]
    public void CanCancel_WhenInProgressWithHandler_IsTrue()
    {
        var card = CreateCard(TranscriptionStatus.InProgress, cancelHandler: _ => { });

        Assert.True(card.CanCancel);
        Assert.True(card.CancelCommand.CanExecute(null));
    }

    [Fact]
    public void CanCancel_WhenDone_IsFalse()
    {
        var card = CreateCard(TranscriptionStatus.Done, cancelHandler: _ => { });

        Assert.False(card.CanCancel);
    }

    [Fact]
    public void CancelCommand_SetsIsCancelling_AndInvokesHandler()
    {
        Guid? cancelledId = null;
        var card = CreateCard(TranscriptionStatus.InProgress, cancelHandler: id => cancelledId = id);

        card.CancelCommand.Execute(null);

        Assert.True(card.IsCancelling);
        Assert.False(card.CanCancel);
        Assert.Equal("Cancelando…", card.ProgressLabel);
        Assert.Equal(card.Id, cancelledId);
    }

    [Fact]
    public void StatusLabel_WhenCancelada_ShowsCancelada()
    {
        var card = CreateCard(TranscriptionStatus.Error, errorMessage: "Cancelada", retryHandler: _ => { });

        Assert.Equal("Cancelada", card.StatusLabel);
        Assert.True(card.CanRetry);
    }

    [Fact]
    public void StatusLabel_WhenGenericError_ShowsErro()
    {
        var card = CreateCard(TranscriptionStatus.Error, errorMessage: "ffmpeg indisponível");

        Assert.Equal("Erro", card.StatusLabel);
    }

    // --- CARD-01: áudio rotulado ---

    [Fact]
    public void Duration_WhenPositive_UsesAudioPrefix()
    {
        var card = CreateCard(TranscriptionStatus.Done, durationSeconds: 180);

        Assert.Equal("Áudio · 3 min", card.Duration);
    }

    [Fact]
    public void Duration_WhenZeroOrNegative_ShowsEmDash()
    {
        var card = CreateCard(TranscriptionStatus.Done, durationSeconds: 0);

        Assert.Equal("Áudio · —", card.Duration);
    }

    // --- CARD-02 / CARD-03: estimativa só com RTF aprendido ---

    [Fact]
    public void EstimatedTimeLabel_WhenInProgressAndLearned_UsesEstimatePrefixWithTilde()
    {
        TranscriptionEstimator.RecordRtf(ModelQuality.High, ExecutionDevice.Cuda, actualRtf: 0.5);
        var card = CreateCard(
            TranscriptionStatus.InProgress,
            durationSeconds: 120,
            quality: ModelQuality.High,
            device: ExecutionDevice.Cuda);

        Assert.Equal("Estimativa · ~1min 0s", card.EstimatedTimeLabel);
    }

    [Fact]
    public void EstimatedTimeLabel_WhenInProgressAndNotLearned_IsNull()
    {
        var card = CreateCard(
            TranscriptionStatus.InProgress,
            durationSeconds: 120,
            quality: ModelQuality.LargeV3Turbo,
            device: ExecutionDevice.Vulkan);

        Assert.Null(card.EstimatedTimeLabel);
    }

    [Fact]
    public void EstimatedTimeLabel_WhenDone_IsNull()
    {
        TranscriptionEstimator.RecordRtf(ModelQuality.Medium, ExecutionDevice.Cpu, actualRtf: 1.0);
        var card = CreateCard(
            TranscriptionStatus.Done,
            durationSeconds: 60,
            quality: ModelQuality.Medium,
            device: ExecutionDevice.Cpu);

        Assert.Null(card.EstimatedTimeLabel);
    }

    // --- CARD-04: estimativa fixa (não muda com %) ---

    [Fact]
    public void EstimatedTimeLabel_DoesNotChange_WhenProgressPercentUpdates()
    {
        TranscriptionEstimator.RecordRtf(ModelQuality.Base, ExecutionDevice.Cuda, actualRtf: 0.5);
        var card = CreateCard(
            TranscriptionStatus.InProgress,
            durationSeconds: 60,
            quality: ModelQuality.Base,
            device: ExecutionDevice.Cuda);
        var before = card.EstimatedTimeLabel;

        card.ApplyProgress(new TranscriptionProgressEventArgs(card.Id, "transcribing", 1, 2));

        Assert.Equal(before, card.EstimatedTimeLabel);
        Assert.Equal("Estimativa · ~30s", card.EstimatedTimeLabel);
    }

    // --- CARD-09 / CARD-06: ShowDate só em Done ---

    [Fact]
    public void ShowDate_WhenDone_IsTrue()
    {
        var card = CreateCard(TranscriptionStatus.Done);

        Assert.True(card.ShowDate);
    }

    [Fact]
    public void ShowDate_WhenInProgress_IsFalse()
    {
        var card = CreateCard(TranscriptionStatus.InProgress);

        Assert.False(card.ShowDate);
    }

    [Fact]
    public void ShowDate_WhenError_IsFalse()
    {
        var card = CreateCard(TranscriptionStatus.Error, errorMessage: "falha");

        Assert.False(card.ShowDate);
    }

    // --- CARD-10: Date atribuída ---

    [Fact]
    public void Date_IsFormattedFromSummary_InPtBr()
    {
        var date = new DateTime(2026, 7, 19);
        var card = CreateCard(TranscriptionStatus.Done, date: date);

        Assert.Equal(date.ToString("d MMM yyyy", CultureInfo.GetCultureInfo("pt-BR")), card.Date);
    }

    // --- CARD-11 / CARD-12: Preview ---

    [Fact]
    public void Preview_WhenDone_UsesSummaryPreview()
    {
        var card = CreateCard(TranscriptionStatus.Done, preview: "Olá mundo");

        Assert.Equal("Olá mundo", card.Preview);
    }

    [Fact]
    public void Preview_WhenInProgress_MatchesProgressLabel()
    {
        var card = CreateCard(TranscriptionStatus.InProgress, preview: "ignorado");
        card.ApplyProgress(new TranscriptionProgressEventArgs(card.Id, "preparing", null, null));

        Assert.Equal("Preparando áudio…", card.Preview);
        Assert.Equal(card.ProgressLabel, card.Preview);
    }

    [Fact]
    public void Preview_WhenInProgressTranscribing_IncludesPercent()
    {
        var card = CreateCard(TranscriptionStatus.InProgress);
        card.ApplyProgress(new TranscriptionProgressEventArgs(card.Id, "transcribing", 2, 5));

        Assert.Equal("Transcrevendo… 40%", card.Preview);
    }

    [Fact]
    public void Preview_WhenDoneAndEmpty_IsEmpty()
    {
        var card = CreateCard(TranscriptionStatus.Done, preview: "");

        Assert.Equal("", card.Preview);
    }

    private static TranscriptionCardViewModel CreateCard(
        TranscriptionStatus status,
        string? errorMessage = null,
        Action<Guid>? retryHandler = null,
        Action<Guid>? cancelHandler = null,
        double durationSeconds = 60,
        ModelQuality quality = ModelQuality.Standard,
        ExecutionDevice device = ExecutionDevice.Cpu,
        DateTime? date = null,
        string preview = "preview")
    {
        var summary = new TranscriptionSummary(
            Guid.NewGuid(),
            "Título",
            "📝",
            status,
            errorMessage,
            date ?? DateTime.UtcNow,
            DurationSeconds: durationSeconds,
            SpeakersCount: 0,
            quality,
            device,
            Tags: [],
            Preview: preview);

        return new TranscriptionCardViewModel(
            summary,
            openHandler: _ => { },
            retryHandler: retryHandler,
            deleteHandler: _ => { },
            cancelHandler: cancelHandler);
    }
}
