using Transcriba.Core.Data.Entities;
using Transcriba.Core.Services;

namespace Transcriba.Tests.Services;

public class SegmentEditingServiceTests
{
    private readonly SegmentEditingService _service = new();

    private static Segment CreateSegment(
        double start,
        string text,
        int sortOrder = 0,
        Guid? id = null) =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            TranscriptionId = Guid.NewGuid(),
            StartSeconds = start,
            EndSeconds = start + 5,
            Text = text,
            SortOrder = sortOrder
        };

    [Fact]
    public void GetActiveSegment_ReturnsLastSegmentWithStartLessOrEqualPosition()
    {
        var segments = new List<Segment>
        {
            CreateSegment(0, "A", 0),
            CreateSegment(10, "B", 1),
            CreateSegment(20, "C", 2)
        };

        var active = _service.GetActiveSegment(segments, TimeSpan.FromSeconds(15));

        Assert.NotNull(active);
        Assert.Equal("B", active.Text);
    }

    [Fact]
    public void GetActiveSegment_ReturnsNullWhenNoSegments()
    {
        var active = _service.GetActiveSegment([], TimeSpan.FromSeconds(5));
        Assert.Null(active);
    }

    [Fact]
    public void GetActiveSegment_ReturnsNullWhenPositionBeforeFirstSegment()
    {
        var segments = new List<Segment> { CreateSegment(10, "A") };
        var active = _service.GetActiveSegment(segments, TimeSpan.FromSeconds(5));
        Assert.Null(active);
    }

    [Fact]
    public void SplitAtCaret_SplitsTextAtCaretPosition()
    {
        var segment = CreateSegment(0, "hello world");

        var result = _service.SplitAtCaret(segment, 5);

        Assert.NotNull(result);
        Assert.Equal("hello", result.Value.Before.Text);
        Assert.Equal("world", result.Value.After.Text);
        Assert.Equal(segment.StartSeconds, result.Value.After.StartSeconds);
        Assert.Equal(segment.SpeakerId, result.Value.After.SpeakerId);
    }

    [Fact]
    public void SplitAtCaret_ReturnsNullWhenBeforePartWouldBeEmpty()
    {
        var segment = CreateSegment(0, "  hello");
        Assert.Null(_service.SplitAtCaret(segment, 0));
    }

    [Fact]
    public void SplitAtCaret_ReturnsNullWhenAfterPartWouldBeEmpty()
    {
        var segment = CreateSegment(0, "hello  ");
        Assert.Null(_service.SplitAtCaret(segment, segment.Text.Length));
    }

    [Fact]
    public void MergeWithPrevious_ConcatenatesActiveIntoPreviousWithSpace()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var segments = new List<Segment>
        {
            CreateSegment(0, "primeira", 0, firstId),
            CreateSegment(5, "segunda", 1, secondId)
        };

        var merged = _service.MergeWithPrevious(segments, segments[1]);

        Assert.NotNull(merged);
        Assert.Equal("primeira segunda", merged.Text);
    }

    [Fact]
    public void MergeWithPrevious_ReturnsNullWhenActiveIsFirstSegment()
    {
        var segments = new List<Segment> { CreateSegment(0, "único") };
        Assert.Null(_service.MergeWithPrevious(segments, segments[0]));
    }

    [Fact]
    public void AssignSpeaker_SetsSpeakerOnSegment()
    {
        var segment = CreateSegment(0, "texto");
        var speaker = new Speaker { Id = Guid.NewGuid(), Name = "Maria", ColorHex = "#2eaadc" };

        _service.AssignSpeaker(segment, speaker);

        Assert.Equal(speaker.Id, segment.SpeakerId);
        Assert.Same(speaker, segment.Speaker);
    }

    [Fact]
    public void SplitAtCaret_ReturnsNullWhenWhitespaceOnlyPartRemainsAfterTrim()
    {
        var segment = CreateSegment(0, "abc   ");
        Assert.Null(_service.SplitAtCaret(segment, 3));
    }
}
