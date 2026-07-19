using Verso.App.ViewModels;
using Verso.Core.Data.Entities;
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

    private static TranscriptionCardViewModel CreateCard(
        TranscriptionStatus status,
        string? errorMessage = null,
        Action<Guid>? retryHandler = null,
        Action<Guid>? cancelHandler = null)
    {
        var summary = new TranscriptionSummary(
            Guid.NewGuid(),
            "Título",
            "📝",
            status,
            errorMessage,
            DateTime.UtcNow,
            DurationSeconds: 60,
            SpeakersCount: 0,
            ModelQuality.Standard,
            ExecutionDevice.Cpu,
            Tags: [],
            Preview: "preview");

        return new TranscriptionCardViewModel(
            summary,
            openHandler: _ => { },
            retryHandler: retryHandler,
            deleteHandler: _ => { },
            cancelHandler: cancelHandler);
    }
}
