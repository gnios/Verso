using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Verso.Core;
using Verso.Core.Data;
using Verso.Core.Data.Entities;
using Verso.Core.Engine;

namespace Verso.E2E.Support;

public sealed record E2ESeedResult(
    string DataRoot,
    Guid TranscriptionId,
    string Title,
    string MediaPath,
    long MediaBytes);

public static class E2ESeed
{
    public const string SampleTitle = "E2E Abrir Transcrição";

    public static async Task<E2ESeedResult> CreateIsolatedWorkspaceAsync(
        string? dataRoot = null,
        TimeSpan? mediaDuration = null)
    {
        dataRoot ??= Path.Combine(Path.GetTempPath(), "verso-e2e", Guid.NewGuid().ToString("N"));
        dataRoot = Path.GetFullPath(dataRoot);
        if (Directory.Exists(dataRoot))
        {
            Directory.Delete(dataRoot, recursive: true);
        }

        Directory.CreateDirectory(dataRoot);
        Environment.SetEnvironmentVariable(VersoPaths.DataRootEnvironmentVariable, dataRoot);

        var duration = mediaDuration ?? TimeSpan.FromSeconds(90);
        var transcriptionId = Guid.NewGuid();
        var mediaDir = Path.Combine(dataRoot, "media", transcriptionId.ToString("N"));
        Directory.CreateDirectory(mediaDir);
        var mediaPath = Path.Combine(mediaDir, "audio.wav");
        WavFixture.WriteSilentWav(mediaPath, duration);
        var mediaBytes = new FileInfo(mediaPath).Length;

        var dbPath = Path.Combine(dataRoot, "verso.db");
        var services = new ServiceCollection();
        services.AddVersoDatabase(dbPath);
        await using var provider = services.BuildServiceProvider();
        await DbBootstrapper.MigrateAsync(provider);

        await using (var ctx = await provider.GetRequiredService<IDbContextFactory<VersoDbContext>>()
                         .CreateDbContextAsync())
        {
            ctx.Transcriptions.Add(new Transcription
            {
                Id = transcriptionId,
                Title = SampleTitle,
                Icon = "🎧",
                Status = TranscriptionStatus.Done,
                MediaFilePath = mediaPath,
                DurationSeconds = duration.TotalSeconds,
                CreatedAt = DateTime.UtcNow,
                Quality = ModelQuality.Base,
                Language = "pt",
                Segments =
                [
                    new Segment
                    {
                        Id = Guid.NewGuid(),
                        TranscriptionId = transcriptionId,
                        StartSeconds = 0,
                        EndSeconds = 5,
                        Text = "Primeiro segmento E2E.",
                        SortOrder = 0,
                    },
                    new Segment
                    {
                        Id = Guid.NewGuid(),
                        TranscriptionId = transcriptionId,
                        StartSeconds = 5,
                        EndSeconds = 12,
                        Text = "Segundo segmento para validar a lista.",
                        SortOrder = 1,
                    },
                ],
            });
            await ctx.SaveChangesAsync();
        }

        return new E2ESeedResult(dataRoot, transcriptionId, SampleTitle, mediaPath, mediaBytes);
    }
}
