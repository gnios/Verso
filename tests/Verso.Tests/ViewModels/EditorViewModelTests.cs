using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Verso.App;
using Verso.App.Services;
using Verso.App.ViewModels;
using Verso.Core;
using Verso.Core.Services;
using Verso.Core.Data;
using Verso.Core.Data.Entities;
using Verso.Core.Engine;
using System.Text;
using Verso.Tests.Services;

namespace Verso.Tests.ViewModels;

public class EditorViewModelTests
{
    internal static async Task<(IServiceProvider Provider, string Directory, Guid TranscriptionId)>
        CreateEditorProviderAsync(TranscriptionStatus status, Action<Transcription>? configure = null)
    {
        var (baseProvider, directory) = await TestDbHelper.CreateIsolatedDatabaseAsync();
        var dbPath = Path.Combine(directory, "verso.db");

        var services = new ServiceCollection();
        services.AddVersoDatabase(dbPath);
        services.AddVersoEngine();
        services.AddVersoServices();
        services.AddVersoAppServices();
        var provider = services.BuildServiceProvider();
        await DbBootstrapper.MigrateAsync(provider);

        var transcriptionId = Guid.NewGuid();
        await using (var ctx = await TestDbHelper.GetFactory(provider).CreateDbContextAsync())
        {
            var transcription = new Transcription
            {
                Id = transcriptionId,
                Title = "Entrevista teste",
                Icon = "🎤",
                Status = status,
                ErrorMessage = status == TranscriptionStatus.Error ? "falha simulada" : null,
                CreatedAt = DateTime.UtcNow,
                DurationSeconds = 120,
            };

            if (status == TranscriptionStatus.Done)
            {
                transcription.Segments.Add(new Segment
                {
                    Id = Guid.NewGuid(),
                    TranscriptionId = transcriptionId,
                    StartSeconds = 0,
                    EndSeconds = 2.5,
                    Text = "Primeiro segmento",
                    SortOrder = 0,
                });
            }

            configure?.Invoke(transcription);
            ctx.Transcriptions.Add(transcription);
            await ctx.SaveChangesAsync();
        }

        return (provider, directory, transcriptionId);
    }

    private static async Task<EditorViewModel> CreateEditorAsync(
        IServiceProvider provider,
        Guid transcriptionId)
    {
        var navigation = provider.GetRequiredService<NavigationService>();
        navigation.NavigateTo(
            ScreenKey.Editor,
            new NavigationParameter(TranscriptionId: transcriptionId));

        var editor = Assert.IsType<EditorViewModel>(navigation.CurrentViewModel);
        await Task.Delay(50);
        return editor;
    }

    private static async Task<(IServiceProvider Provider, string Directory, Guid TranscriptionId)>
        CreateEditorProviderWithFileSaveAsync(
            TranscriptionStatus status,
            FakeFileSaveService fileSave,
            Action<Transcription>? configure = null)
    {
        var (baseProvider, directory) = await TestDbHelper.CreateIsolatedDatabaseAsync();
        var dbPath = Path.Combine(directory, "verso.db");

        var services = new ServiceCollection();
        services.AddVersoDatabase(dbPath);
        services.AddVersoEngine();
        services.AddVersoServices();
        services.AddVersoAppServices();
        services.AddSingleton<IFileSaveService>(fileSave);
        var provider = services.BuildServiceProvider();
        await DbBootstrapper.MigrateAsync(provider);

        var transcriptionId = Guid.NewGuid();
        await using (var ctx = await TestDbHelper.GetFactory(provider).CreateDbContextAsync())
        {
            var transcription = new Transcription
            {
                Id = transcriptionId,
                Title = "Entrevista teste",
                Icon = "🎤",
                Status = status,
                ErrorMessage = status == TranscriptionStatus.Error ? "falha simulada" : null,
                CreatedAt = DateTime.UtcNow,
                DurationSeconds = 120,
            };

            if (status == TranscriptionStatus.Done)
            {
                transcription.Segments.Add(new Segment
                {
                    Id = Guid.NewGuid(),
                    TranscriptionId = transcriptionId,
                    StartSeconds = 0,
                    EndSeconds = 2.5,
                    Text = "Primeiro segmento",
                    SortOrder = 0,
                });
            }

            configure?.Invoke(transcription);
            ctx.Transcriptions.Add(transcription);
            await ctx.SaveChangesAsync();
        }

        return (provider, directory, transcriptionId);
    }

    [Fact]
    public async Task Initialize_InProgress_ShowsProgressIndicator()
    {
        var (provider, directory, transcriptionId) = await CreateEditorProviderAsync(TranscriptionStatus.InProgress);
        try
        {
            var editor = await CreateEditorAsync(provider, transcriptionId);

            Assert.True(editor.IsInProgress);
            Assert.Equal("Transcrição em andamento…", editor.StatusMessage);
            Assert.False(editor.HasSegments);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task Initialize_Done_LoadsSegments()
    {
        var (provider, directory, transcriptionId) = await CreateEditorProviderAsync(TranscriptionStatus.Done);
        try
        {
            var editor = await CreateEditorAsync(provider, transcriptionId);

            Assert.False(editor.IsInProgress);
            Assert.True(editor.HasSegments);
            Assert.Equal("Primeiro segmento", editor.Segments[0].Text);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task StatusChanged_Done_LoadsSegmentsWithoutManualReload()
    {
        var (provider, directory, transcriptionId) = await CreateEditorProviderAsync(TranscriptionStatus.InProgress);
        try
        {
            var editor = await CreateEditorAsync(provider, transcriptionId);
            var queue = provider.GetRequiredService<TranscriptionQueueService>();

            await using (var ctx = await TestDbHelper.GetFactory(provider).CreateDbContextAsync())
            {
                await ctx.Segments
                    .Where(s => s.TranscriptionId == transcriptionId)
                    .ExecuteDeleteAsync();

                await ctx.Transcriptions
                    .Where(t => t.Id == transcriptionId)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(t => t.Status, TranscriptionStatus.Done));

                ctx.Segments.Add(new Segment
                {
                    Id = Guid.NewGuid(),
                    TranscriptionId = transcriptionId,
                    StartSeconds = 0,
                    EndSeconds = 1.2,
                    Text = "Segmento gerado",
                    SortOrder = 0,
                });
                await ctx.SaveChangesAsync();
            }

            typeof(TranscriptionQueueService)
                .GetMethod(
                    "RaiseStatusChanged",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(queue, [transcriptionId, TranscriptionStatusChanged.Done, null]);

            await Task.Delay(50);

            Assert.False(editor.IsInProgress);
            Assert.True(editor.HasSegments);
            Assert.Equal("Segmento gerado", editor.Segments[0].Text);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task CommitSegmentText_PersistsToDatabase()
    {
        var (provider, directory, transcriptionId) = await CreateEditorProviderAsync(TranscriptionStatus.Done);
        try
        {
            var editor = await CreateEditorAsync(provider, transcriptionId);
            var segment = editor.Segments[0];
            segment.Text = "Texto corrigido";
            segment.CommitText();
            await Task.Delay(50);

            await using var ctx = await TestDbHelper.GetFactory(provider).CreateDbContextAsync();
            var saved = await ctx.Segments.SingleAsync(s => s.Id == segment.Id);
            Assert.Equal("Texto corrigido", saved.Text);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task SplitSegment_UsesFocusedSegmentCaret_NotPlaybackActiveSegment()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();

        var (provider, directory, transcriptionId) = await CreateEditorProviderAsync(
            TranscriptionStatus.Done,
            transcription =>
            {
                transcription.Segments.Clear();
                transcription.Segments.AddRange(
                [
                    new Segment
                    {
                        Id = firstId,
                        TranscriptionId = transcription.Id,
                        StartSeconds = 0,
                        EndSeconds = 5,
                        Text = "alpha beta",
                        SortOrder = 0,
                    },
                    new Segment
                    {
                        Id = secondId,
                        TranscriptionId = transcription.Id,
                        StartSeconds = 10,
                        EndSeconds = 15,
                        Text = "gamma",
                        SortOrder = 1,
                    },
                ]);
            });

        try
        {
            var editor = await CreateEditorAsync(provider, transcriptionId);
            editor.SetPlaybackPosition(TimeSpan.FromSeconds(12), markStarted: true);
            editor.OnSegmentFocused(editor.Segments[0], caretIndex: 5);
            await editor.SplitSegmentCommand.ExecuteAsync(null);
            await Task.Delay(50);

            await using var ctx = await TestDbHelper.GetFactory(provider).CreateDbContextAsync();
            var segments = await ctx.Segments
                .Where(s => s.TranscriptionId == transcriptionId)
                .OrderBy(s => s.SortOrder)
                .ToListAsync();

            Assert.Equal(3, segments.Count);
            Assert.Equal("alpha", segments[0].Text);
            Assert.Equal("beta", segments[1].Text);
            Assert.Equal("gamma", segments[2].Text);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task MergeSegment_UsesPlaybackActiveSegment()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();

        var (provider, directory, transcriptionId) = await CreateEditorProviderAsync(
            TranscriptionStatus.Done,
            transcription =>
            {
                transcription.Segments.Clear();
                transcription.Segments.AddRange(
                [
                    new Segment
                    {
                        Id = firstId,
                        TranscriptionId = transcription.Id,
                        StartSeconds = 0,
                        EndSeconds = 5,
                        Text = "primeiro",
                        SortOrder = 0,
                    },
                    new Segment
                    {
                        Id = secondId,
                        TranscriptionId = transcription.Id,
                        StartSeconds = 10,
                        EndSeconds = 15,
                        Text = "segundo",
                        SortOrder = 1,
                    },
                ]);
            });

        try
        {
            var editor = await CreateEditorAsync(provider, transcriptionId);
            editor.SetPlaybackPosition(TimeSpan.FromSeconds(12), markStarted: true);
            await editor.MergeSegmentCommand.ExecuteAsync(null);
            await Task.Delay(50);

            await using var ctx = await TestDbHelper.GetFactory(provider).CreateDbContextAsync();
            var segments = await ctx.Segments
                .Where(s => s.TranscriptionId == transcriptionId)
                .OrderBy(s => s.SortOrder)
                .ToListAsync();

            Assert.Single(segments);
            Assert.Equal("primeiro segundo", segments[0].Text);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task SplitSegmentForAsync_ActsOnExplicitSegmentRegardlessOfFocusOrPlayback()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();

        var (provider, directory, transcriptionId) = await CreateEditorProviderAsync(
            TranscriptionStatus.Done,
            transcription =>
            {
                transcription.Segments.Clear();
                transcription.Segments.AddRange(
                [
                    new Segment
                    {
                        Id = firstId,
                        TranscriptionId = transcription.Id,
                        StartSeconds = 0,
                        EndSeconds = 5,
                        Text = "alpha beta",
                        SortOrder = 0,
                    },
                    new Segment
                    {
                        Id = secondId,
                        TranscriptionId = transcription.Id,
                        StartSeconds = 10,
                        EndSeconds = 15,
                        Text = "gamma",
                        SortOrder = 1,
                    },
                ]);
            });

        try
        {
            var editor = await CreateEditorAsync(provider, transcriptionId);
            // Playback + foco no SEGUNDO segmento — mas o split explícito é do PRIMEIRO.
            editor.SetPlaybackPosition(TimeSpan.FromSeconds(12), markStarted: true);
            editor.OnSegmentFocused(editor.Segments[1], caretIndex: 2);
            editor.Segments[0].CaretIndex = 5;

            await editor.SplitSegmentForAsync(editor.Segments[0]);
            await Task.Delay(50);

            await using var ctx = await TestDbHelper.GetFactory(provider).CreateDbContextAsync();
            var segments = await ctx.Segments
                .Where(s => s.TranscriptionId == transcriptionId)
                .OrderBy(s => s.SortOrder)
                .ToListAsync();

            Assert.Equal(3, segments.Count);
            Assert.Equal("alpha", segments[0].Text);
            Assert.Equal("beta", segments[1].Text);
            Assert.Equal("gamma", segments[2].Text);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task MergeSegmentForAsync_MergesExplicitSegmentWithPrevious()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();

        var (provider, directory, transcriptionId) = await CreateEditorProviderAsync(
            TranscriptionStatus.Done,
            transcription =>
            {
                transcription.Segments.Clear();
                transcription.Segments.AddRange(
                [
                    new Segment
                    {
                        Id = firstId,
                        TranscriptionId = transcription.Id,
                        StartSeconds = 0,
                        EndSeconds = 5,
                        Text = "primeiro",
                        SortOrder = 0,
                    },
                    new Segment
                    {
                        Id = secondId,
                        TranscriptionId = transcription.Id,
                        StartSeconds = 10,
                        EndSeconds = 15,
                        Text = "segundo",
                        SortOrder = 1,
                    },
                ]);
            });

        try
        {
            var editor = await CreateEditorAsync(provider, transcriptionId);
            // Playback no PRIMEIRO segmento, mas o merge explícito é do SEGUNDO.
            editor.SetPlaybackPosition(TimeSpan.FromSeconds(2), markStarted: true);

            await editor.MergeSegmentForAsync(editor.Segments[1]);
            await Task.Delay(50);

            await using var ctx = await TestDbHelper.GetFactory(provider).CreateDbContextAsync();
            var segments = await ctx.Segments
                .Where(s => s.TranscriptionId == transcriptionId)
                .OrderBy(s => s.SortOrder)
                .ToListAsync();

            Assert.Single(segments);
            Assert.Equal("primeiro segundo", segments[0].Text);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task SplitSegment_PersistsContiguousNonOverlappingTimeRanges()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();

        var (provider, directory, transcriptionId) = await CreateEditorProviderAsync(
            TranscriptionStatus.Done,
            transcription =>
            {
                transcription.Segments.Clear();
                transcription.Segments.AddRange(
                [
                    new Segment
                    {
                        Id = firstId,
                        TranscriptionId = transcription.Id,
                        StartSeconds = 0,
                        EndSeconds = 10,
                        Text = "alpha beta",
                        SortOrder = 0,
                    },
                    new Segment
                    {
                        Id = secondId,
                        TranscriptionId = transcription.Id,
                        StartSeconds = 20,
                        EndSeconds = 30,
                        Text = "gamma",
                        SortOrder = 1,
                    },
                ]);
            });

        try
        {
            var editor = await CreateEditorAsync(provider, transcriptionId);
            editor.OnSegmentFocused(editor.Segments[0], caretIndex: 5);
            await editor.SplitSegmentCommand.ExecuteAsync(null);
            await Task.Delay(50);

            await using var ctx = await TestDbHelper.GetFactory(provider).CreateDbContextAsync();
            var segments = await ctx.Segments
                .Where(s => s.TranscriptionId == transcriptionId)
                .OrderBy(s => s.SortOrder)
                .ToListAsync();

            // 3 segmentos: antes [0,5], depois [5,10], gamma [20,30] — contíguos, sem
            // sobreposição (antes do fix ambos herdados [0,10] quebravam o destaque).
            Assert.Equal(3, segments.Count);
            Assert.Equal(0, segments[0].StartSeconds);
            Assert.Equal(5, segments[0].EndSeconds);
            Assert.Equal(5, segments[1].StartSeconds);
            Assert.Equal(10, segments[1].EndSeconds);
            Assert.Equal(20, segments[2].StartSeconds);
            Assert.Equal(30, segments[2].EndSeconds);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task MergeSegment_PersistsMergedTimeRangeCoveringBothSegments()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();

        var (provider, directory, transcriptionId) = await CreateEditorProviderAsync(
            TranscriptionStatus.Done,
            transcription =>
            {
                transcription.Segments.Clear();
                transcription.Segments.AddRange(
                [
                    new Segment
                    {
                        Id = firstId,
                        TranscriptionId = transcription.Id,
                        StartSeconds = 0,
                        EndSeconds = 6,
                        Text = "primeiro",
                        SortOrder = 0,
                    },
                    new Segment
                    {
                        Id = secondId,
                        TranscriptionId = transcription.Id,
                        StartSeconds = 8,
                        EndSeconds = 15,
                        Text = "segundo",
                        SortOrder = 1,
                    },
                ]);
            });

        try
        {
            var editor = await CreateEditorAsync(provider, transcriptionId);
            editor.SetPlaybackPosition(TimeSpan.FromSeconds(10), markStarted: true);
            await editor.MergeSegmentCommand.ExecuteAsync(null);
            await Task.Delay(50);

            await using var ctx = await TestDbHelper.GetFactory(provider).CreateDbContextAsync();
            var segment = await ctx.Segments.SingleAsync(s => s.TranscriptionId == transcriptionId);

            // Mesclado cobre [0, 15] (início do primeiro até fim do segundo), não [0, 6].
            Assert.Equal(0, segment.StartSeconds);
            Assert.Equal(15, segment.EndSeconds);
            Assert.Equal("primeiro segundo", segment.Text);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task SplitSegmentForAsync_UsesUncommittedEditedTextForSplit()
    {
        // Simula o caso que bugava: o usuário edita o texto do trecho (o bind oninput
        // atualiza SegmentItemViewModel.Text, mas o CommitText só roda no blur) e clica
        // em Dividir sem sair do campo. O split deve usar o texto atual, não o staler da
        // entidade em memória.
        var firstId = Guid.NewGuid();

        var (provider, directory, transcriptionId) = await CreateEditorProviderAsync(
            TranscriptionStatus.Done,
            transcription =>
            {
                transcription.Segments.Clear();
                transcription.Segments.Add(new Segment
                {
                    Id = firstId,
                    TranscriptionId = transcription.Id,
                    StartSeconds = 0,
                    EndSeconds = 10,
                    Text = "alpha beta",
                    SortOrder = 0,
                });
            });

        try
        {
            var editor = await CreateEditorAsync(provider, transcriptionId);
            // Edita o texto via o ViewModel (como o @bind oninput faria) sem commitar.
            editor.Segments[0].Text = "alpha corrigido beta";
            editor.Segments[0].CaretIndex = 15; // logo após "alpha corrigido"

            await editor.SplitSegmentForAsync(editor.Segments[0]);
            await Task.Delay(50);

            await using var ctx = await TestDbHelper.GetFactory(provider).CreateDbContextAsync();
            var segments = await ctx.Segments
                .Where(s => s.TranscriptionId == transcriptionId)
                .OrderBy(s => s.SortOrder)
                .ToListAsync();

            Assert.Equal(2, segments.Count);
            Assert.Equal("alpha corrigido", segments[0].Text);
            Assert.Equal("beta", segments[1].Text);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task Export_WithoutSegments_DisablesExportCommand()
    {
        var (provider, directory, transcriptionId) = await CreateEditorProviderAsync(TranscriptionStatus.InProgress);
        try
        {
            var editor = await CreateEditorAsync(provider, transcriptionId);

            Assert.False(editor.CanExport);
            Assert.False(editor.ExportCommand.CanExecute(null));
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task ExportWithFormat_Txt_WritesFileViaExportService()
    {
        var fileSave = new FakeFileSaveService();
        var (provider, directory, transcriptionId) =
            await CreateEditorProviderWithFileSaveAsync(TranscriptionStatus.Done, fileSave);
        var outputPath = Path.Combine(directory, "export.txt");
        fileSave.NextPath = outputPath;

        try
        {
            var editor = await CreateEditorAsync(provider, transcriptionId);

            Assert.True(editor.CanExport);
            await editor.ExportWithFormatAsync(ExportFormat.Txt);

            Assert.Equal(ExportFormat.Txt, fileSave.LastFormat);
            Assert.Equal("Entrevista teste", fileSave.LastSuggestedName);
            Assert.True(File.Exists(outputPath));
            Assert.Equal(outputPath, editor.ExportSavedPath);
            Assert.True(editor.IsExportDialogOpen);

            var lines = await File.ReadAllLinesAsync(outputPath, Encoding.UTF8);
            Assert.Contains(lines, l => l.Contains("Primeiro segmento"));
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task ExportWithFormat_Srt_CallsExportServiceWithSrtExtension()
    {
        var fileSave = new FakeFileSaveService();
        var (provider, directory, transcriptionId) =
            await CreateEditorProviderWithFileSaveAsync(TranscriptionStatus.Done, fileSave);
        var outputPath = Path.Combine(directory, "export.srt");
        fileSave.NextPath = outputPath;

        try
        {
            var editor = await CreateEditorAsync(provider, transcriptionId);
            await editor.ExportWithFormatAsync(ExportFormat.Srt);

            Assert.Equal(ExportFormat.Srt, fileSave.LastFormat);
            Assert.True(File.Exists(outputPath));
            var content = await File.ReadAllTextAsync(outputPath, Encoding.UTF8);
            Assert.Contains("Primeiro segmento", content);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task ExportWithFormat_Vtt_CallsExportServiceWithVttExtension()
    {
        var fileSave = new FakeFileSaveService();
        var (provider, directory, transcriptionId) =
            await CreateEditorProviderWithFileSaveAsync(TranscriptionStatus.Done, fileSave);
        var outputPath = Path.Combine(directory, "export.vtt");
        fileSave.NextPath = outputPath;

        try
        {
            var editor = await CreateEditorAsync(provider, transcriptionId);
            await editor.ExportWithFormatAsync(ExportFormat.Vtt);

            Assert.Equal(ExportFormat.Vtt, fileSave.LastFormat);
            Assert.True(File.Exists(outputPath));
            var content = await File.ReadAllTextAsync(outputPath, Encoding.UTF8);
            Assert.StartsWith("WEBVTT", content);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task ExportWithFormat_CancelledSavePath_DoesNotCreateFile()
    {
        var fileSave = new FakeFileSaveService { NextPath = null };
        var (provider, directory, transcriptionId) =
            await CreateEditorProviderWithFileSaveAsync(TranscriptionStatus.Done, fileSave);

        try
        {
            var editor = await CreateEditorAsync(provider, transcriptionId);
            await editor.ExportWithFormatAsync(ExportFormat.Txt);

            Assert.False(Directory.GetFiles(directory, "export.*").Any());
            Assert.True(editor.IsExportDialogOpen);
            Assert.Contains("destino", editor.ExportError, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task ExportCommand_WithSegments_OpensExportDialog()
    {
        var (provider, directory, transcriptionId) = await CreateEditorProviderAsync(TranscriptionStatus.Done);
        try
        {
            var editor = await CreateEditorAsync(provider, transcriptionId);

            Assert.True(editor.CanExport);
            editor.ExportCommand.Execute(null);

            Assert.True(editor.IsExportDialogOpen);
            Assert.Equal("", editor.ExportError);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task ExportWithFormat_WhenSaveFails_ReopensDialogWithError()
    {
        var fileSave = new FakeFileSaveService
        {
            ExceptionToThrow = new InvalidOperationException("PhotinoWindow ainda não foi anexada."),
        };
        var (provider, directory, transcriptionId) =
            await CreateEditorProviderWithFileSaveAsync(TranscriptionStatus.Done, fileSave);

        try
        {
            var editor = await CreateEditorAsync(provider, transcriptionId);
            await editor.ExportWithFormatAsync(ExportFormat.Txt);

            Assert.True(editor.IsExportDialogOpen);
            Assert.Contains("PhotinoWindow", editor.ExportError, StringComparison.Ordinal);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task CommitTitle_UpdatesBreadcrumbTitle()
    {
        var (provider, directory, transcriptionId) = await CreateEditorProviderAsync(TranscriptionStatus.Done);
        try
        {
            var editor = await CreateEditorAsync(provider, transcriptionId);
            editor.Title = "Novo título";
            await editor.CommitTitleCommand.ExecuteAsync(null);
            await Task.Delay(50);

            Assert.Equal("Novo título", editor.Title);

            await using var ctx = await TestDbHelper.GetFactory(provider).CreateDbContextAsync();
            var saved = await ctx.Transcriptions.SingleAsync(t => t.Id == transcriptionId);
            Assert.Equal("Novo título", saved.Title);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    private static WordLikeKeyContext WordKey(
        string key,
        int start,
        int end,
        int length,
        bool shift = false,
        bool firstLine = true,
        bool lastLine = true,
        int column = 0) =>
        new(
            Key: key,
            Shift: shift,
            CaretStart: start,
            CaretEnd: end,
            TextLength: length,
            IsFirstLine: firstLine,
            IsLastLine: lastLine,
            Column: column,
            IsFirstSegment: false,
            IsLastSegment: false);

    private static async Task<(IServiceProvider Provider, string Directory, Guid TranscriptionId, Guid FirstId, Guid SecondId)>
        CreateTwoSegmentEditorAsync(
            string firstText = "primeiro",
            string secondText = "segundo",
            Guid? firstSpeakerId = null,
            Guid? secondSpeakerId = null,
            Action<Transcription>? extra = null)
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var (provider, directory, transcriptionId) = await CreateEditorProviderAsync(
            TranscriptionStatus.Done,
            transcription =>
            {
                transcription.Segments.Clear();
                extra?.Invoke(transcription);
                transcription.Segments.AddRange(
                [
                    new Segment
                    {
                        Id = firstId,
                        TranscriptionId = transcription.Id,
                        StartSeconds = 0,
                        EndSeconds = 5,
                        Text = firstText,
                        SortOrder = 0,
                        SpeakerId = firstSpeakerId,
                    },
                    new Segment
                    {
                        Id = secondId,
                        TranscriptionId = transcription.Id,
                        StartSeconds = 10,
                        EndSeconds = 15,
                        Text = secondText,
                        SortOrder = 1,
                        SpeakerId = secondSpeakerId,
                    },
                ]);
            });
        return (provider, directory, transcriptionId, firstId, secondId);
    }

    [Fact]
    public async Task ApplyWordLikeKey_Enter_SplitsAndFocusesNewSegmentAtZero()
    {
        var (provider, directory, transcriptionId, firstId, _) =
            await CreateTwoSegmentEditorAsync("alpha beta", "gamma");
        try
        {
            var editor = await CreateEditorAsync(provider, transcriptionId);
            (Guid Id, int Caret)? focus = null;
            editor.FocusSegmentRequested += (_, e) => focus = e;

            await editor.ApplyWordLikeKeyAsync(
                editor.Segments[0],
                WordKey("Enter", start: 5, end: 5, length: 10));
            await Task.Delay(50);

            await using var ctx = await TestDbHelper.GetFactory(provider).CreateDbContextAsync();
            var segments = await ctx.Segments
                .Where(s => s.TranscriptionId == transcriptionId)
                .OrderBy(s => s.SortOrder)
                .ToListAsync();

            Assert.Equal(3, segments.Count);
            Assert.Equal("alpha", segments[0].Text);
            Assert.Equal("beta", segments[1].Text);
            Assert.NotNull(focus);
            Assert.Equal(segments[1].Id, focus.Value.Id);
            Assert.Equal(0, focus.Value.Caret);
            Assert.NotEqual(firstId, focus.Value.Id);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task ApplyWordLikeKey_BackspaceAtStart_MergesWithPreviousAndFocusesJoin()
    {
        var (provider, directory, transcriptionId, firstId, _) =
            await CreateTwoSegmentEditorAsync();
        try
        {
            var editor = await CreateEditorAsync(provider, transcriptionId);
            (Guid Id, int Caret)? focus = null;
            editor.FocusSegmentRequested += (_, e) => focus = e;
            var expectedCaret = SegmentEditingService.CaretAfterJoin("primeiro", "segundo");

            await editor.ApplyWordLikeKeyAsync(
                editor.Segments[1],
                WordKey("Backspace", start: 0, end: 0, length: 7));
            await Task.Delay(50);

            await using var ctx = await TestDbHelper.GetFactory(provider).CreateDbContextAsync();
            var segment = await ctx.Segments.SingleAsync(s => s.TranscriptionId == transcriptionId);
            Assert.Equal("primeiro segundo", segment.Text);
            Assert.Equal(firstId, segment.Id);
            Assert.NotNull(focus);
            Assert.Equal(firstId, focus.Value.Id);
            Assert.Equal(expectedCaret, focus.Value.Caret);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task ApplyWordLikeKey_DeleteAtEnd_MergesWithNextAndFocusesJoin()
    {
        var (provider, directory, transcriptionId, firstId, _) =
            await CreateTwoSegmentEditorAsync();
        try
        {
            var editor = await CreateEditorAsync(provider, transcriptionId);
            (Guid Id, int Caret)? focus = null;
            editor.FocusSegmentRequested += (_, e) => focus = e;
            var expectedCaret = SegmentEditingService.CaretAfterJoin("primeiro", "segundo");

            await editor.ApplyWordLikeKeyAsync(
                editor.Segments[0],
                WordKey("Delete", start: 8, end: 8, length: 8));
            await Task.Delay(50);

            await using var ctx = await TestDbHelper.GetFactory(provider).CreateDbContextAsync();
            var segment = await ctx.Segments.SingleAsync(s => s.TranscriptionId == transcriptionId);
            Assert.Equal("primeiro segundo", segment.Text);
            Assert.Equal(firstId, segment.Id);
            Assert.NotNull(focus);
            Assert.Equal(firstId, focus.Value.Id);
            Assert.Equal(expectedCaret, focus.Value.Caret);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task ApplyWordLikeKey_MergeKeepsPreviousSpeakerWhenDifferent()
    {
        var anaId = Guid.NewGuid();
        var betoId = Guid.NewGuid();
        var (provider, directory, transcriptionId, firstId, _) =
            await CreateTwoSegmentEditorAsync(
                firstSpeakerId: anaId,
                secondSpeakerId: betoId,
                extra: transcription =>
                {
                    transcription.Speakers.Add(new Speaker
                    {
                        Id = anaId,
                        TranscriptionId = transcription.Id,
                        Name = "Ana",
                        ColorHex = "#2eaadc",
                    });
                    transcription.Speakers.Add(new Speaker
                    {
                        Id = betoId,
                        TranscriptionId = transcription.Id,
                        Name = "Beto",
                        ColorHex = "#e74c3c",
                    });
                });
        try
        {
            var editor = await CreateEditorAsync(provider, transcriptionId);
            await editor.ApplyWordLikeKeyAsync(
                editor.Segments[1],
                WordKey("Backspace", start: 0, end: 0, length: 7));
            await Task.Delay(50);

            await using var ctx = await TestDbHelper.GetFactory(provider).CreateDbContextAsync();
            var segment = await ctx.Segments.SingleAsync(s => s.TranscriptionId == transcriptionId);
            Assert.Equal(firstId, segment.Id);
            Assert.Equal(anaId, segment.SpeakerId);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task ApplyWordLikeKey_ArrowRightAtEnd_FocusesNextWithoutPersisting()
    {
        var (provider, directory, transcriptionId, _, secondId) =
            await CreateTwoSegmentEditorAsync();
        try
        {
            var editor = await CreateEditorAsync(provider, transcriptionId);
            (Guid Id, int Caret)? focus = null;
            editor.FocusSegmentRequested += (_, e) => focus = e;

            await editor.ApplyWordLikeKeyAsync(
                editor.Segments[0],
                WordKey("ArrowRight", start: 8, end: 8, length: 8));

            Assert.Equal(2, editor.Segments.Count);
            Assert.NotNull(focus);
            Assert.Equal(secondId, focus.Value.Id);
            Assert.Equal(0, focus.Value.Caret);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task ApplyWordLikeKey_ArrowLeftAtStart_FocusesPreviousAtEnd()
    {
        var (provider, directory, transcriptionId, firstId, _) =
            await CreateTwoSegmentEditorAsync();
        try
        {
            var editor = await CreateEditorAsync(provider, transcriptionId);
            (Guid Id, int Caret)? focus = null;
            editor.FocusSegmentRequested += (_, e) => focus = e;

            await editor.ApplyWordLikeKeyAsync(
                editor.Segments[1],
                WordKey("ArrowLeft", start: 0, end: 0, length: 7));

            Assert.NotNull(focus);
            Assert.Equal(firstId, focus.Value.Id);
            Assert.Equal("primeiro".Length, focus.Value.Caret);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task ApplyWordLikeKey_ArrowUpOnFirstLine_FocusesPreviousAtMinColumn()
    {
        var (provider, directory, transcriptionId, firstId, _) =
            await CreateTwoSegmentEditorAsync();
        try
        {
            var editor = await CreateEditorAsync(provider, transcriptionId);
            (Guid Id, int Caret)? focus = null;
            editor.FocusSegmentRequested += (_, e) => focus = e;

            await editor.ApplyWordLikeKeyAsync(
                editor.Segments[1],
                WordKey("ArrowUp", start: 3, end: 3, length: 7, firstLine: true, lastLine: true, column: 3));

            Assert.NotNull(focus);
            Assert.Equal(firstId, focus.Value.Id);
            Assert.Equal(3, focus.Value.Caret);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task ApplyWordLikeKey_ArrowDownOnLastLine_FocusesNextAtMinColumn()
    {
        var (provider, directory, transcriptionId, _, secondId) =
            await CreateTwoSegmentEditorAsync();
        try
        {
            var editor = await CreateEditorAsync(provider, transcriptionId);
            (Guid Id, int Caret)? focus = null;
            editor.FocusSegmentRequested += (_, e) => focus = e;

            await editor.ApplyWordLikeKeyAsync(
                editor.Segments[0],
                WordKey("ArrowDown", start: 8, end: 8, length: 8, firstLine: true, lastLine: true, column: 20));

            Assert.NotNull(focus);
            Assert.Equal(secondId, focus.Value.Id);
            Assert.Equal("segundo".Length, focus.Value.Caret);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task TryConsumePendingFocus_ReturnsOnceThenFalse()
    {
        var (provider, directory, transcriptionId, firstId, _) =
            await CreateTwoSegmentEditorAsync();
        try
        {
            var editor = await CreateEditorAsync(provider, transcriptionId);
            editor.RequestFocus(firstId, 4);

            Assert.True(editor.TryConsumePendingFocus(firstId, out var caret));
            Assert.Equal(4, caret);
            Assert.False(editor.TryConsumePendingFocus(firstId, out _));
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task SplitSegmentForAsync_RequestsFocusOnNewSegment()
    {
        var (provider, directory, transcriptionId, _, _) =
            await CreateTwoSegmentEditorAsync("alpha beta", "gamma");
        try
        {
            var editor = await CreateEditorAsync(provider, transcriptionId);
            (Guid Id, int Caret)? focus = null;
            editor.FocusSegmentRequested += (_, e) => focus = e;
            editor.Segments[0].CaretIndex = 5;

            await editor.SplitSegmentForAsync(editor.Segments[0]);
            await Task.Delay(50);

            Assert.NotNull(focus);
            Assert.Equal(editor.Segments[1].Id, focus.Value.Id);
            Assert.Equal(0, focus.Value.Caret);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task AddTagCommand_PersistsTagAndUpdatesObservable()
    {
        var (provider, directory, transcriptionId) = await CreateEditorProviderAsync(TranscriptionStatus.Done);
        try
        {
            var editor = await CreateEditorAsync(provider, transcriptionId);
            Assert.Empty(editor.Tags);

            editor.NewTagInput = "entrevista";
            await editor.AddTagCommand.ExecuteAsync(null);

            Assert.Single(editor.Tags);
            Assert.Equal("entrevista", editor.Tags[0].Name);
            Assert.Equal("", editor.NewTagInput);

            await using var ctx = await TestDbHelper.GetFactory(provider).CreateDbContextAsync();
            var saved = await ctx.Transcriptions.Include(t => t.Tags).SingleAsync(t => t.Id == transcriptionId);
            Assert.Single(saved.Tags);
            Assert.Equal("entrevista", saved.Tags[0].Name);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task RemoveTagCommand_RemovesTagFromTranscription()
    {
        var (provider, directory, transcriptionId) = await CreateEditorProviderAsync(TranscriptionStatus.Done);
        try
        {
            var editor = await CreateEditorAsync(provider, transcriptionId);
            editor.NewTagInput = "remover";
            await editor.AddTagCommand.ExecuteAsync(null);
            Assert.Single(editor.Tags);

            await editor.RemoveTagCommand.ExecuteAsync(editor.Tags[0]);

            Assert.Empty(editor.Tags);
            await using var ctx = await TestDbHelper.GetFactory(provider).CreateDbContextAsync();
            var saved = await ctx.Transcriptions.Include(t => t.Tags).SingleAsync(t => t.Id == transcriptionId);
            Assert.Empty(saved.Tags);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task ChangeFolderCommand_AssignsAndUnassignsTranscription()
    {
        var (provider, directory, transcriptionId) = await CreateEditorProviderAsync(TranscriptionStatus.Done);
        try
        {
            var editor = await CreateEditorAsync(provider, transcriptionId);
            Assert.Null(editor.SelectedFolderId);
            Assert.False(editor.HasFolderBreadcrumb);

            var folderService = provider.GetRequiredService<FolderService>();
            var folder = await folderService.CreateAsync("Tese alpha", "🔬", "green");

            // Recarrega para popular FolderOptions com a nova pasta — aguarda a
            // Task interna (LoadAsync → LoadFolderOptionsAsync) em vez de um Delay fixo.
            await (Task)typeof(EditorViewModel)
                .GetMethod("LoadAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(editor, null)!;

            await editor.ChangeFolderCommand.ExecuteAsync(folder.Id);

            Assert.Equal(folder.Id, editor.SelectedFolderId);
            Assert.True(editor.HasFolderBreadcrumb);
            Assert.Equal("Tese alpha", editor.FolderTitle);

            await using var ctx = await TestDbHelper.GetFactory(provider).CreateDbContextAsync();
            var saved = await ctx.Transcriptions.SingleAsync(t => t.Id == transcriptionId);
            Assert.Equal(folder.Id, saved.FolderId);

            // Desatribui.
            await editor.ChangeFolderCommand.ExecuteAsync(null);
            Assert.Null(editor.SelectedFolderId);
            Assert.False(editor.HasFolderBreadcrumb);

            await using var ctx2 = await TestDbHelper.GetFactory(provider).CreateDbContextAsync();
            var saved2 = await ctx2.Transcriptions.SingleAsync(t => t.Id == transcriptionId);
            Assert.Null(saved2.FolderId);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

}
