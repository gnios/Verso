using System;
using Microsoft.EntityFrameworkCore;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Transcriba.App;
using Transcriba.App.Services;
using Transcriba.App.ViewModels;
using Transcriba.Core;
using Transcriba.Core.Data;
using Transcriba.Core.Data.Entities;
using Transcriba.Core.Engine;
using Transcriba.Core.Services;
using Transcriba.Tests.Engine;
using Transcriba.Tests.Services;

namespace Transcriba.Tests.ViewModels;

public class UploadViewModelTests
{
    private static async Task<(IServiceProvider Provider, string Directory, string MediaPath, int ResearchId)>
        CreateUploadProviderAsync()
    {
        var (baseProvider, directory) = await TestDbHelper.CreateIsolatedDatabaseAsync();
        var dbPath = Path.Combine(directory, "transcriba.db");
        var mediaDirectory = Path.Combine(directory, "media-source");
        Directory.CreateDirectory(mediaDirectory);
        var mediaPath = Path.Combine(mediaDirectory, "entrevista.mp3");
        await File.WriteAllBytesAsync(mediaPath, [0x49, 0x44, 0x33, 0x03]);

        var services = new ServiceCollection();
        services.AddTranscribaDatabase(dbPath);
        services.AddTranscribaEngine();
        services.AddSingleton<ITranscriptionEngine>(new SuccessTranscriptionEngine());
        services.AddTranscribaServices();
        services.AddSingleton(new MediaStorageService(Path.Combine(directory, "media-storage")));
        services.AddTranscribaAppServices();
        var provider = services.BuildServiceProvider();
        await DbBootstrapper.MigrateAsync(provider);

        var researchService = provider.GetRequiredService<ResearchService>();
        var research = await researchService.CreateAsync("Mobilidade urbana", "🚲", "green");

        await using (var ctx = await TestDbHelper.GetFactory(provider).CreateDbContextAsync())
        {
            ctx.UserSettings.Add(new UserSettings
            {
                Id = 1,
                DefaultLanguage = "es",
                IdentifySpeakersDefault = false,
                Device = ExecutionDevice.Cpu,
            });
            await ctx.SaveChangesAsync();
        }

        return (provider, directory, mediaPath, research.Id);
    }

    private static async Task<UploadViewModel> CreateUploadAsync(
        IServiceProvider provider,
        int? researchId = null)
    {
        var navigation = provider.GetRequiredService<NavigationService>();
        navigation.NavigateTo(
            ScreenKey.Upload,
            researchId is int id ? new NavigationParameter(ResearchId: id) : null);

        var upload = Assert.IsType<UploadViewModel>(navigation.CurrentViewModel);
        await Task.Delay(50);
        return upload;
    }

    [Fact]
    public async Task TrySelectFile_UnsupportedFormat_SetsValidationError()
    {
        var (provider, directory, _, _) = await CreateUploadProviderAsync();
        try
        {
            var upload = await CreateUploadAsync(provider);
            var invalidPath = Path.Combine(directory, "documento.pdf");
            await File.WriteAllTextAsync(invalidPath, "pdf");

            var accepted = upload.TrySelectFile(invalidPath);

            Assert.False(accepted);
            Assert.Contains("Formato não suportado", upload.ValidationError, StringComparison.Ordinal);
            Assert.False(upload.HasSelectedFile);
            Assert.False(upload.CanStart);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task TrySelectFile_SupportedFormat_EnablesStart()
    {
        var (provider, directory, mediaPath, _) = await CreateUploadProviderAsync();
        try
        {
            var upload = await CreateUploadAsync(provider);

            var accepted = upload.TrySelectFile(mediaPath);

            Assert.True(accepted);
            Assert.Equal("entrevista.mp3", upload.SelectedFileName);
            Assert.False(string.IsNullOrWhiteSpace(upload.SelectedFileSize));
            Assert.True(upload.HasSelectedFile);
            Assert.True(upload.CanStart);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task Initialize_AppliesSettingsDefaults()
    {
        var (provider, directory, _, _) = await CreateUploadProviderAsync();
        try
        {
            var upload = await CreateUploadAsync(provider);

            Assert.Equal("es", upload.Language);
            Assert.Equal(SpeakerMode.Off, upload.SpeakerMode);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task Initialize_WithResearchId_PreselectsResearch()
    {
        var (provider, directory, _, researchId) = await CreateUploadProviderAsync();
        try
        {
            var upload = await CreateUploadAsync(provider, researchId);

            Assert.Equal(researchId, upload.SelectedResearch?.Id);
            Assert.Equal("Mobilidade urbana", upload.SelectedResearch?.Name);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task StartTranscription_CreatesRecordEnqueuesAndNavigatesToDashboard()
    {
        var (provider, directory, mediaPath, researchId) = await CreateUploadProviderAsync();
        try
        {
            var upload = await CreateUploadAsync(provider, researchId);
            upload.TrySelectFile(mediaPath);
            upload.SelectedResearch = upload.ResearchOptions.First(option => option.Id == researchId);

            await upload.StartTranscriptionCommand.ExecuteAsync(null);

            var navigation = provider.GetRequiredService<NavigationService>();
            Assert.Equal(ScreenKey.Dashboard, navigation.CurrentScreen);
            var parameter = Assert.IsType<NavigationParameter>(navigation.NavigationParameter);
            Assert.Equal(LibraryStatusFilter.Progress, parameter.StatusFilter);

            await using var ctx = await TestDbHelper.GetFactory(provider).CreateDbContextAsync();
            var transcription = Assert.Single(ctx.Transcriptions);
            Assert.Equal(TranscriptionStatus.InProgress, transcription.Status);
            Assert.Equal(researchId, transcription.ResearchPageId);
            Assert.Equal("es", transcription.Language);
            Assert.Equal(SpeakerMode.Off, transcription.SpeakerMode);
            Assert.False(string.IsNullOrWhiteSpace(transcription.MediaFilePath));
            Assert.True(File.Exists(transcription.MediaFilePath));
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task TrySelectFile_AutoFillsTitleFromFileName()
    {
        var (provider, directory, mediaPath, _) = await CreateUploadProviderAsync();
        try
        {
            var upload = await CreateUploadAsync(provider);

            Assert.True(upload.TrySelectFile(mediaPath));
            Assert.Equal("entrevista", upload.Title);
            Assert.True(upload.CanStart);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }

    [Fact]
    public async Task StartTranscription_PersistsIconAndTagsAndCustomTitle()
    {
        var (provider, directory, mediaPath, _) = await CreateUploadProviderAsync();
        try
        {
            var upload = await CreateUploadAsync(provider);
            upload.TrySelectFile(mediaPath);
            upload.Title = "Entrevista piloto";
            upload.TagsText = "campo, piloto";
            upload.IconPicker.SelectedIcon = "🎙️";

            await upload.StartTranscriptionCommand.ExecuteAsync(null);

            await using var ctx = await TestDbHelper.GetFactory(provider).CreateDbContextAsync();
            var transcription = Assert.Single(await ctx.Transcriptions.Include(t => t.Tags).ToListAsync());
            Assert.Equal("Entrevista piloto", transcription.Title);
            Assert.Equal("🎙️", transcription.Icon);
            Assert.Equal(2, transcription.Tags.Count);
        }
        finally
        {
            TestDbHelper.Cleanup(directory);
        }
    }
}

internal sealed class SuccessTranscriptionEngine : ITranscriptionEngine
{
    public Task<TranscriptionResult> TranscribeAsync(
        TranscriptionJobRequest request,
        IProgress<EngineProgress>? progress,
        CancellationToken cancellationToken) =>
        Task.FromResult(new TranscriptionResult(
        [
            new TranscriptionSegmentResult(0, 1.5, "segmento ok")
        ]));
}
