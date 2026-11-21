using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Verso.App.ViewModels;
using Verso.Core;
using Verso.Core.Catalogs;
using Verso.Core.Data.Entities;
using Verso.Core.Engine;
using Verso.Core.Export;
using Verso.Core.Services;
using Verso.Core.Update;

namespace Verso.App.Services;

/// <summary>
/// Handlers de aplicação da bridge (métodos chamados pelo React) + push de eventos.
/// A UI React é fina: os ViewModels/serviços C# continuam sendo a fonte de verdade.
/// Métodos: app.info, theme.get/toggle, sidebar.load, navigation.navigate,
/// dashboard.load/action/search, transcription.action, ui.confirm.respond,
/// upload.pickFile/load/start, editor.*, settings.*, folder.*, feedback.open/send.
/// Eventos: navigation.changed, theme.changed, ui.confirm, dashboard.changed/progress,
/// folder.changed, queue.status, model.download.started/completed, media.command.
/// </summary>
public sealed class VersoBridge : IDisposable
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private readonly IServiceProvider _services;
    private readonly NativeBridge _bridge;
    private readonly ILogger<VersoBridge> _logger;
    private readonly BridgeConfirmationService _confirmation;
    private readonly TranscriptionQueueService? _queue;
    private readonly SidebarViewModel _sidebar;

    public VersoBridge(IServiceProvider services, NativeBridge bridge, ILogger<VersoBridge> logger)
    {
        _services = services;
        _bridge = bridge;
        _logger = logger;
        _confirmation = services.GetRequiredService<BridgeConfirmationService>();
        _confirmation.PropertyChanged += OnConfirmationPropertyChanged;
        _sidebar = services.GetRequiredService<SidebarViewModel>();
        _sidebar.PropertyChanged += OnSidebarPropertyChanged;

        _queue = services.GetService<TranscriptionQueueService>();
        if (_queue is not null)
        {
            _queue.StatusChanged += OnQueueStatusChanged;
            _queue.ProgressChanged += OnQueueProgressChanged;
        }
    }

    public void Attach()
    {
        RegisterCoreMethods();
        RegisterUploadMethods();
        RegisterEditorMethods();
        RegisterSettingsMethods();
        RegisterFolderMethods();
        RegisterFeedbackMethods();
    }

    public void Dispose()
    {
        _confirmation.PropertyChanged -= OnConfirmationPropertyChanged;
        _sidebar.PropertyChanged -= OnSidebarPropertyChanged;
        if (_queue is not null)
        {
            _queue.StatusChanged -= OnQueueStatusChanged;
            _queue.ProgressChanged -= OnQueueProgressChanged;
        }
    }

    // ------------------------------------------------------------------ core

    private void RegisterCoreMethods()
    {
        _bridge.Register("app.info", _ => Task.FromResult<object?>(new
        {
            version = AppVersion.Display(RunningAppVersion.Current),
            hasUpdateChannel = UpdateChannel.TryLoad(VersoPaths.AppDirectory) is not null,
        }));

        _bridge.Register("theme.get", async _ =>
        {
            var theme = _services.GetRequiredService<ThemeService>();
            await theme.InitializeAsync();
            return new { isDark = theme.IsDark };
        });

        _bridge.Register("theme.toggle", async _ =>
        {
            var theme = _services.GetRequiredService<ThemeService>();
            await theme.ToggleAsync();
            return new { isDark = theme.IsDark };
        });

        _bridge.Register("sidebar.load", async _ => await SnapshotSidebarAsync());
        _bridge.Register("updates.load", _ =>
        {
            var coordinator = _services.GetService<UpdateCoordinator>();
            var hasChannel = UpdateChannel.TryLoad(VersoPaths.AppDirectory) is not null;
            return Task.FromResult<object?>(new
            {
                version = _sidebar.AppVersionLabel,
                latestVersion = coordinator?.LatestPublishedVersion,
                hasChannel,
                status = UpdateStatusMessages.For(
                    coordinator?.Status ?? UpdateStatus.Idle, hasChannel, coordinator?.AvailableVersion),
                action = SnapshotUpdateAction(_sidebar),
            });
        });
        _bridge.Register("updates.check", async _ =>
        {
            if (_sidebar.CanUpdateNow && _services.GetService<UpdateSession>() is { } session)
                await session.CheckInBackgroundAsync();

            return null;
        });
        _bridge.Register("sidebar.updateNow", async _ =>
        {
            if (_sidebar.UpdateNowCommand.CanExecute(null))
                await _sidebar.UpdateNowCommand.ExecuteAsync(null);

            return null;
        });

        _bridge.Register("navigation.navigate", args =>
        {
            var navigation = _services.GetRequiredService<NavigationService>();
            var screen = ParseScreen(args);
            var parameter = ParseParameter(args);
            navigation.NavigateTo(screen, parameter);
            _bridge.SendEvent("navigation.changed", new { screen = screen.ToString(), parameter });
            return Task.FromResult<object?>(new { screen = screen.ToString() });
        });

        _bridge.Register("dashboard.load", async args =>
        {
            var navigation = _services.GetRequiredService<NavigationService>();
            var parameter = ParseParameter(args);
            var dashboard = GetDashboard(navigation, parameter);
            dashboard.SearchText = ParseSearchText(args);
            await dashboard.LoadAsync();
            return SnapshotDashboard(dashboard);
        });

        _bridge.Register("dashboard.action", async args =>
        {
            var navigation = _services.GetRequiredService<NavigationService>();
            var dashboard = GetDashboard(navigation, null);
            var action = GetString(args, "action");
            var id = GetGuid(args, "transcriptionId");
            switch (action)
            {
                case "open":
                    dashboard.OpenTranscription(id);
                    break;
                case "delete":
                    await dashboard.DeleteTranscriptionAsync(id);
                    break;
                case "retry":
                    await dashboard.RetryTranscriptionAsync(id);
                    break;
                case "cancel":
                    dashboard.CancelTranscription(id);
                    break;
            }

            return SnapshotDashboard(dashboard);
        });

        _bridge.Register("dashboard.search", async args =>
        {
            var navigation = _services.GetRequiredService<NavigationService>();
            var dashboard = GetDashboard(navigation, null);
            dashboard.SearchText = GetString(args, "searchText");
            await Task.Delay(400); // espelha o debounce do ViewModel (300ms)
            await dashboard.LoadAsync();
            return SnapshotDashboard(dashboard);
        });

        // Ação por transcrição fora do dashboard (pasta, editor etc.) — nível de serviço,
        // sem depender do ViewModel da tela corrente.
        _bridge.Register("transcription.action", async args =>
        {
            var action = GetString(args, "action");
            var id = GetGuid(args, "transcriptionId");
            await RunTranscriptionActionAsync(action, id);
            return null;
        });

        _bridge.Register("ui.confirm.respond", args =>
        {
            var ok = GetBool(args, "ok");
            if (ok)
            {
                _confirmation.Confirm();
            }
            else
            {
                _confirmation.Cancel();
            }

            return Task.FromResult<object?>(null);
        });
    }

    private async Task RunTranscriptionActionAsync(string? action, Guid id)
    {
        switch (action)
        {
            case "open":
                _services.GetRequiredService<NavigationService>().NavigateTo(
                    ScreenKey.Editor, new NavigationParameter(TranscriptionId: id));
                _bridge.SendEvent("navigation.changed", new
                {
                    screen = ScreenKey.Editor.ToString(),
                    parameter = new { transcriptionId = id.ToString() },
                });
                break;

            case "delete":
            {
                using var scope = _services.CreateScope();
                var library = scope.ServiceProvider.GetRequiredService<LibraryService>();
                var media = scope.ServiceProvider.GetRequiredService<MediaStorageService>();
                var transcription = await library.GetTranscriptionAsync(id);
                var title = transcription?.Title ?? "esta transcrição";
                if (!await _confirmation.ConfirmAsync(
                        "Excluir transcrição",
                        $"A transcrição \"{title}\" e todos os seus dados serão excluídos permanentemente. Deseja continuar?"))
                {
                    return;
                }

                _queue?.Cancel(id);
                await library.DeleteTranscriptionAsync(id);
                media.DeleteMedia(id);
                await _services.GetRequiredService<SidebarViewModel>().LoadAsync();
                break;
            }

            case "retry":
            {
                using var scope = _services.CreateScope();
                var library = scope.ServiceProvider.GetRequiredService<LibraryService>();
                var settings = scope.ServiceProvider.GetRequiredService<SettingsService>();
                var transcription = await library.GetTranscriptionAsync(id);
                if (transcription is null || string.IsNullOrWhiteSpace(transcription.MediaFilePath) || _queue is null)
                {
                    return;
                }

                var currentSettings = await settings.GetAsync();
                await library.ResetToInProgressAsync(id);
                _queue.Enqueue(new TranscriptionJobRequest(
                    id,
                    transcription.MediaFilePath,
                    transcription.Language,
                    transcription.Quality,
                    currentSettings.Device,
                    currentSettings.MaxTranscriptionThreads,
                    transcription.Engine,
                    transcription.ParakeetModel));
                break;
            }

            case "cancel":
                _queue?.Cancel(id);
                break;
        }
    }

    // ---------------------------------------------------------------- upload

    private void RegisterUploadMethods()
    {
        _bridge.Register("upload.pickFile", async _ =>
        {
            var fileOpen = _services.GetRequiredService<IFileOpenService>();
            var path = await fileOpen.PickMediaFileAsync();
            return DescribeFile(path);
        });

        // Arquivo já existente em disco (ex.: gravação do microfone salva pelo /recordings).
        _bridge.Register("upload.selectFile", async args =>
        {
            var path = GetString(args, "path");
            return DescribeFile(path);
        });

        _bridge.Register("upload.load", async args =>
        {
            using var scope = _services.CreateScope();
            var settingsService = scope.ServiceProvider.GetRequiredService<SettingsService>();
            var folderService = scope.ServiceProvider.GetRequiredService<FolderService>();
            var settings = await settingsService.GetAsync();
            var preselectedFolderId = GetNullableInt(args, "folderId");

            var folders = await folderService.GetAllAsync();
            return new
            {
                languages = new[]
                {
                    new { code = "pt", label = "Português (Brasil)" },
                    new { code = "es", label = "Español" },
                    new { code = "en", label = "English" },
                },
                models = ModelCatalog.All.Select(m => new
                {
                    value = m.Value.ToString(),
                    label = m.Label,
                    sizeLabel = m.SizeLabel,
                    speedHint = m.SpeedHint,
                    whenToUse = m.WhenToUse,
                    description = m.Description,
                }),
                engines = ModelCatalog.Engines.Select(e => new
                {
                    value = e.Value.ToString(),
                    label = e.Label,
                    hint = e.Hint,
                }),
                parakeetModels = ModelCatalog.ParakeetModels.Select(p => new
                {
                    value = p.Value.ToString(),
                    label = p.Label,
                    sizeLabel = p.SizeLabel,
                }),
                icons = IconCatalog.TransIcons,
                folders = folders.Select(f => new { id = (int?)f.Id, name = f.Title, icon = f.Icon })
                    .Prepend(new { id = (int?)null, name = "Nenhuma pasta", icon = "" }),
                defaults = new
                {
                    language = settings.DefaultLanguage,
                    quality = ModelCatalog.ResolveProfile(settings.DefaultQuality).ToString(),
                    engine = settings.DefaultEngine.ToString(),
                    parakeetModel = settings.DefaultParakeetModel.ToString(),
                    selectedFolderId = preselectedFolderId is int folderId ? (int?)folderId : null,
                },
                formats = UploadMediaFormats.DisplayList,
            };
        });

        _bridge.Register("upload.start", async args =>
        {
            var filePath = GetString(args, "filePath");
            var title = GetString(args, "title").Trim();
            if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(title) || !System.IO.File.Exists(filePath))
            {
                throw new InvalidOperationException("Arquivo ou título inválido.");
            }

            if (!UploadMediaFormats.IsSupported(filePath))
            {
                throw new InvalidOperationException($"Formato não suportado. Use: {UploadMediaFormats.DisplayList}.");
            }

            using var scope = _services.CreateScope();
            var mediaStorage = scope.ServiceProvider.GetRequiredService<MediaStorageService>();
            var settingsService = scope.ServiceProvider.GetRequiredService<SettingsService>();
            var libraryService = scope.ServiceProvider.GetRequiredService<LibraryService>();
            var audioLoader = scope.ServiceProvider.GetRequiredService<AudioLoader>();
            var settings = await settingsService.GetAsync();

            var language = GetString(args, "language") is { Length: > 0 } languageValue ? languageValue : settings.DefaultLanguage;
            var quality = ParseEnum<ModelQuality>(args, "quality") ?? ModelQuality.Standard;
            var engine = ParseEnum<TranscriptionEngineKind>(args, "engine") ?? settings.DefaultEngine;
            var parakeetModel = ParseEnum<ParakeetModel>(args, "parakeetModel") ?? settings.DefaultParakeetModel;
            var folderId = GetNullableInt(args, "folderId");
            var icon = GetNullableString(args, "icon");
            var tags = args?.TryGetProperty("tags", out var tagsElement) == true
                       && tagsElement.ValueKind == JsonValueKind.Array
                ? tagsElement.EnumerateArray().Select(t => t.GetString() ?? "").Where(t => t.Length > 0).ToArray()
                : [];

            var transcriptionId = Guid.NewGuid();
            var mediaPath = await mediaStorage.CopyToStorageAsync(filePath, transcriptionId);
            var durationSeconds = audioLoader.GetDuration(mediaPath);

            await libraryService.CreateForUploadAsync(
                transcriptionId,
                title,
                mediaPath,
                language,
                quality,
                settings.Device,
                settings.IdentifySpeakersDefault ? SpeakerMode.Automatic : SpeakerMode.Off,
                folderId,
                durationSeconds,
                icon,
                tags,
                engine,
                parakeetModel);

            _queue?.Enqueue(new TranscriptionJobRequest(
                transcriptionId,
                mediaPath,
                language,
                quality,
                settings.Device,
                settings.MaxTranscriptionThreads,
                engine,
                parakeetModel));

            await _services.GetRequiredService<SidebarViewModel>().LoadAsync();

            _services.GetRequiredService<NavigationService>().NavigateTo(
                ScreenKey.Dashboard,
                new NavigationParameter(StatusFilter: LibraryStatusFilter.Progress));
            _bridge.SendEvent("navigation.changed", new
            {
                screen = ScreenKey.Dashboard.ToString(),
                parameter = new { statusFilter = "Progress" },
            });

            return new { transcriptionId = transcriptionId.ToString() };
        });
    }

    private static object? DescribeFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
        {
            return new { path = (string?)null, fileName = "", fileSize = "", error = "Arquivo não encontrado." };
        }

        if (!UploadMediaFormats.IsSupported(path))
        {
            return new
            {
                path = (string?)null,
                fileName = "",
                fileSize = "",
                error = $"Formato não suportado. Use: {UploadMediaFormats.DisplayList}.",
            };
        }

        var sizeMb = new System.IO.FileInfo(path).Length / (1024d * 1024d);
        return new
        {
            path,
            fileName = System.IO.Path.GetFileName(path),
            fileSize = sizeMb.ToString("0.#", PtBr) + " MB",
            error = (string?)null,
        };
    }

    // ---------------------------------------------------------------- editor

    private void RegisterEditorMethods()
    {
        _bridge.Register("editor.load", async args =>
        {
            var transcriptionId = GetGuid(args, "transcriptionId");
            using var scope = _services.CreateScope();
            var library = scope.ServiceProvider.GetRequiredService<LibraryService>();
            var folderService = scope.ServiceProvider.GetRequiredService<FolderService>();
            var detail = await library.GetTranscriptionDetailAsync(transcriptionId);
            if (detail is null)
            {
                throw new InvalidOperationException("Transcrição não encontrada.");
            }

            string? mediaUrl = null;
            if (!string.IsNullOrWhiteSpace(detail.MediaFilePath))
            {
                mediaUrl = _services.GetRequiredService<LocalMediaServer>().BuildUrl(detail.MediaFilePath);
            }

            var folders = await folderService.GetAllAsync();
            var profile = ModelCatalog.Find(detail.Quality);
            return new
            {
                id = detail.Id.ToString(),
                title = detail.Title,
                icon = detail.Icon,
                status = detail.Status.ToString(),
                isInProgress = detail.Status == TranscriptionStatus.InProgress,
                isDone = detail.Status == TranscriptionStatus.Done,
                isError = detail.Status == TranscriptionStatus.Error,
                errorMessage = detail.ErrorMessage,
                meta = new
                {
                    date = detail.CreatedAt.ToString("d MMM yyyy", PtBr),
                    duration = FormatDurationLabel(detail.DurationSeconds),
                    durationSeconds = detail.DurationSeconds,
                    processingTime = detail.ProcessingDurationSeconds is { } processing
                        ? FormatDuration(processing)
                        : "",
                    speakerCount = detail.Speakers.Count,
                    model = profile.Label,
                },
                tags = detail.Tags.Select(t => new
                {
                    name = t.Name,
                    colorName = t.ColorName,
                }),
                folder = detail.Folder is { } folder
                    ? new { id = (int?)folder.Id, title = folder.Title }
                    : new { id = (int?)null, title = (string?)"Nenhuma pasta" },
                folders = folders.Select(f => new { id = (int?)f.Id, name = f.Title, icon = f.Icon })
                    .Prepend(new { id = (int?)null, name = "Nenhuma pasta", icon = "" }),
                icons = IconCatalog.TransIcons,
                speakers = detail.Speakers
                    .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(s => new { id = s.Id.ToString(), name = s.Name, colorHex = s.ColorHex }),
                segments = detail.Segments
                    .OrderBy(s => s.SortOrder)
                    .Select(SegmentDto),
                mediaUrl,
                hasSegments = detail.Segments.Count > 0,
            };
        });

        _bridge.Register("editor.saveText", async args =>
        {
            var segmentId = GetGuid(args, "segmentId");
            var text = GetString(args, "text");
            using var scope = _services.CreateScope();
            var library = scope.ServiceProvider.GetRequiredService<LibraryService>();
            await library.UpdateSegmentTextAsync(segmentId, text);
            return null;
        });

        _bridge.Register("editor.split", async args =>
        {
            var transcriptionId = GetGuid(args, "transcriptionId");
            var segmentId = GetGuid(args, "segmentId");
            var text = GetString(args, "text");
            var caretIndex = GetInt(args, "caretIndex");

            using var scope = _services.CreateScope();
            var library = scope.ServiceProvider.GetRequiredService<LibraryService>();
            var editing = scope.ServiceProvider.GetRequiredService<SegmentEditingService>();
            var detail = await library.GetTranscriptionDetailAsync(transcriptionId)
                ?? throw new InvalidOperationException("Transcrição não encontrada.");
            var entity = detail.Segments.FirstOrDefault(s => s.Id == segmentId)
                ?? throw new InvalidOperationException("Segmento não encontrado.");

            entity.Text = text;
            var split = editing.SplitAtCaret(entity, caretIndex);
            if (split is null)
            {
                return null;
            }

            await library.ApplySegmentSplitAsync(
                transcriptionId,
                entity.Id,
                split.Value.Before.Text,
                split.Value.Before.EndSeconds,
                split.Value.After);

            return new { before = SegmentDto(split.Value.Before), after = SegmentDto(split.Value.After) };
        });

        _bridge.Register("editor.merge", async args =>
        {
            var transcriptionId = GetGuid(args, "transcriptionId");
            var segmentId = GetGuid(args, "segmentId");
            var text = GetString(args, "text");

            using var scope = _services.CreateScope();
            var library = scope.ServiceProvider.GetRequiredService<LibraryService>();
            var editing = scope.ServiceProvider.GetRequiredService<SegmentEditingService>();
            var detail = await library.GetTranscriptionDetailAsync(transcriptionId)
                ?? throw new InvalidOperationException("Transcrição não encontrada.");
            var entity = detail.Segments.FirstOrDefault(s => s.Id == segmentId)
                ?? throw new InvalidOperationException("Segmento não encontrado.");

            entity.Text = text;
            var merged = editing.MergeWithPrevious(detail.Segments.OrderBy(s => s.SortOrder).ToList(), entity);
            if (merged is null)
            {
                return null;
            }

            await library.ApplySegmentMergeAsync(
                transcriptionId,
                merged.Id,
                merged.Text,
                entity.EndSeconds,
                entity.Id);

            return new { merged = SegmentDto(merged), removedId = segmentId.ToString() };
        });

        _bridge.Register("editor.assignSpeaker", async args =>
        {
            var segmentId = GetGuid(args, "segmentId");
            Guid? speakerId = args?.TryGetProperty("speakerId", out var speaker) == true
                && speaker.ValueKind == JsonValueKind.Null ? null : GetGuid(args, "speakerId");
            using var scope = _services.CreateScope();
            var library = scope.ServiceProvider.GetRequiredService<LibraryService>();
            await library.AssignSpeakerToSegmentAsync(segmentId, speakerId);
            return null;
        });

        _bridge.Register("editor.addSpeaker", async args =>
        {
            var transcriptionId = GetGuid(args, "transcriptionId");
            var name = GetString(args, "name").Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidOperationException("Informe o nome do locutor.");
            }

            using var scope = _services.CreateScope();
            var speakerService = scope.ServiceProvider.GetRequiredService<SpeakerService>();
            var speaker = await speakerService.CreateSpeakerAsync(transcriptionId, name);
            return new { id = speaker.Id.ToString(), name = speaker.Name, colorHex = speaker.ColorHex };
        });

        _bridge.Register("editor.renameSpeaker", async args =>
        {
            var speakerId = GetGuid(args, "speakerId");
            var name = GetString(args, "name").Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidOperationException("Informe o nome do locutor.");
            }

            using var scope = _services.CreateScope();
            var speakerService = scope.ServiceProvider.GetRequiredService<SpeakerService>();
            await speakerService.RenameSpeakerAsync(speakerId, name);
            return null;
        });

        _bridge.Register("editor.updateMeta", async args =>
        {
            var transcriptionId = GetGuid(args, "transcriptionId");
            using var scope = _services.CreateScope();
            var library = scope.ServiceProvider.GetRequiredService<LibraryService>();
            var folderService = scope.ServiceProvider.GetRequiredService<FolderService>();

            if (args?.TryGetProperty("title", out var titleElement) == true
                && !string.IsNullOrWhiteSpace(titleElement.GetString()))
            {
                await library.UpdateTranscriptionTitleAsync(transcriptionId, titleElement.GetString()!.Trim());
            }

            if (args?.TryGetProperty("icon", out var iconElement) == true)
            {
                await library.UpdateTranscriptionIconAsync(transcriptionId, iconElement.GetString());
            }

            if (args?.TryGetProperty("tags", out var tagsElement) == true && tagsElement.ValueKind == JsonValueKind.Array)
            {
                var tags = tagsElement.EnumerateArray()
                    .Select(t => t.GetString() ?? "")
                    .Where(t => t.Length > 0)
                    .ToArray();
                var colors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (args?.TryGetProperty("tagColors", out var colorElement) == true
                    && colorElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var entry in colorElement.EnumerateObject())
                    {
                        if (entry.Value.ValueKind == JsonValueKind.String
                            && entry.Value.GetString() is "blue" or "cyan" or "green" or "orange"
                                or "pink" or "purple" or "teal" or "yellow" or "red")
                        {
                            colors[entry.Name] = entry.Value.GetString()!;
                        }
                    }
                }

                await library.UpdateTranscriptionTagsAsync(transcriptionId, tags, colors);
                _bridge.SendEvent("sidebar.tagsChanged", await library.GetTagsAsync());
            }

            if (args?.TryGetProperty("folderId", out var folderElement) == true)
            {
                int? folderId = folderElement.ValueKind == JsonValueKind.Null
                    ? null
                    : folderElement.GetInt32();
                await folderService.AssignTranscriptionToFolderAsync(transcriptionId, folderId);
            }

            if (args is { } metadata && (metadata.TryGetProperty("title", out _)
                || metadata.TryGetProperty("icon", out _) || metadata.TryGetProperty("folderId", out _)))
            {
                await _sidebar.LoadAsync();
            }
            return null;
        });

        _bridge.Register("editor.export", async args =>
        {
            var transcriptionId = GetGuid(args, "transcriptionId");
            var format = GetString(args, "format");
            var exportFormat = format.ToLowerInvariant() switch
            {
                "srt" => ExportFormat.Srt,
                "vtt" => ExportFormat.Vtt,
                "rtf" => ExportFormat.Rtf,
                _ => ExportFormat.Txt,
            };

            using var scope = _services.CreateScope();
            var library = scope.ServiceProvider.GetRequiredService<LibraryService>();
            var exportService = scope.ServiceProvider.GetRequiredService<ExportService>();
            var fileSave = _services.GetRequiredService<IFileSaveService>();
            var detail = await library.GetTranscriptionDetailAsync(transcriptionId)
                ?? throw new InvalidOperationException("Transcrição não encontrada.");

            var destPath = await fileSave.PickSavePathAsync(detail.Title, exportFormat);
            if (string.IsNullOrWhiteSpace(destPath))
            {
                return new { path = (string?)null, error = "Não foi possível definir o arquivo de destino." };
            }

            try
            {
                switch (exportFormat)
                {
                    case ExportFormat.Txt:
                        await exportService.ExportTxtAsync(transcriptionId, destPath);
                        break;
                    case ExportFormat.Srt:
                        await exportService.ExportSrtAsync(transcriptionId, destPath);
                        break;
                    case ExportFormat.Vtt:
                        await exportService.ExportVttAsync(transcriptionId, destPath);
                        break;
                    case ExportFormat.Rtf:
                        await exportService.ExportRtfAsync(transcriptionId, destPath);
                        break;
                }

                fileSave.Reveal(destPath);
                return new { path = destPath, error = (string?)null };
            }
            catch (Exception ex)
            {
                return new { path = (string?)null, error = ex.Message };
            }
        });
    }

    private static object SegmentDto(Segment segment) => new
    {
        id = segment.Id.ToString(),
        start = segment.StartSeconds,
        end = segment.EndSeconds,
        text = segment.Text,
        speakerId = segment.SpeakerId?.ToString(),
        sortOrder = segment.SortOrder,
    };

    // -------------------------------------------------------------- settings

    private void RegisterSettingsMethods()
    {
        _bridge.Register("settings.load", async _ => await SnapshotSettingsAsync());

        _bridge.Register("settings.update", async args =>
        {
            using var scope = _services.CreateScope();
            var settingsService = scope.ServiceProvider.GetRequiredService<SettingsService>();
            await settingsService.UpdateAsync(settings =>
            {
                if (args?.TryGetProperty("name", out var name) == true)
                {
                    settings.Name = name.GetString() ?? "";
                }

                if (args?.TryGetProperty("email", out var email) == true)
                {
                    settings.Email = email.GetString() ?? "";
                }

                if (args?.TryGetProperty("institution", out var institution) == true)
                {
                    settings.Institution = institution.GetString() ?? "";
                }

                if (args?.TryGetProperty("defaultLanguage", out var language) == true)
                {
                    settings.DefaultLanguage = language.GetString() ?? "pt";
                }

                if (args?.TryGetProperty("liveTranscriptionEnabled", out var live) == true
                    && live.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    settings.LiveTranscriptionEnabled = live.GetBoolean();
                }

                if (args?.TryGetProperty("identifySpeakersDefault", out var identifySpeakers) == true
                    && identifySpeakers.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    settings.IdentifySpeakersDefault = identifySpeakers.GetBoolean();
                }

                if (ParseEnum<ExecutionDevice>(args, "device") is { } device)
                {
                    settings.Device = device;
                }

                if (ParseEnum<ModelQuality>(args, "defaultQuality") is { } quality)
                {
                    settings.DefaultQuality = quality;
                }

                if (ParseEnum<TranscriptionEngineKind>(args, "defaultEngine") is { } engine)
                {
                    settings.DefaultEngine = engine;
                }

                if (ParseEnum<ParakeetModel>(args, "defaultParakeetModel") is { } parakeetModel)
                {
                    settings.DefaultParakeetModel = parakeetModel;
                }
            });

            return await SnapshotSettingsAsync();
        });

        _bridge.Register("settings.openLogs", _ =>
        {
            try
            {
                Directory.CreateDirectory(VersoPaths.LogsDirectory);
                ProcessStartInfo psi = OperatingSystem.IsWindows()
                    ? new("explorer.exe", VersoPaths.LogsDirectory) { UseShellExecute = true }
                    : OperatingSystem.IsMacOS()
                        ? new("open", VersoPaths.LogsDirectory) { UseShellExecute = false }
                        : new("xdg-open", VersoPaths.LogsDirectory) { UseShellExecute = false };
                Process.Start(psi);
            }
            catch
            {
                // pasta pode não existir ainda
            }

            return Task.FromResult<object?>(null);
        });

        _bridge.Register("settings.verifyRuntime", async args =>
        {
            var device = ParseEnum<ExecutionDevice>(args, "device") ?? ExecutionDevice.Auto;
            var quality = ParseEnum<ModelQuality>(args, "quality") ?? ModelQuality.Standard;

            if (WhisperRuntimeInspector.LoadedRuntime is not null)
            {
                return new
                {
                    message = $"Runtime já carregado: {WhisperRuntimeInspector.LoadedRuntimeLabel} " +
                              $"({WhisperRuntimeInspector.LoadedBackend}). A troca só passa a valer após reiniciar o aplicativo.",
                };
            }

            var modelPath = Path.Combine(VersoPaths.ModelsDirectory, ModelManager.GetModelFileName(quality));
            if (!File.Exists(modelPath))
            {
                return new
                {
                    message = $"Nenhum modelo em {modelPath}. Transcreva algo uma vez para baixar o perfil, ou aguarde o download automático.",
                };
            }

            try
            {
                var loaded = await Task.Run(() => WhisperRuntimeInspector.ProbeRuntime(modelPath, device));
                return new
                {
                    message = loaded is null
                        ? "Não foi possível determinar o runtime carregado."
                        : $"Runtime carregado: {WhisperRuntimeInspector.GetRuntimeLabel(loaded.Value)} (backend {WhisperRuntimeInspector.GetBackend(loaded)}).",
                };
            }
            catch (Exception ex)
            {
                return new { message = $"Falha ao verificar runtime: {ex.Message}" };
            }
        });
    }

    private async Task<object> SnapshotSettingsAsync()
    {
        // Reaproveita o ViewModel: recomendação de perfil + info de desenvolvedor já calculadas.
        using var viewModel = _services.GetRequiredService<SettingsViewModel>();
        await viewModel.LoadAsync();
        return new
        {
            profile = new { name = viewModel.Name, email = viewModel.Email, institution = viewModel.Institution },
            languages = new[]
            {
                new { code = "pt", label = "Português (Brasil)" },
                new { code = "es", label = "Español" },
                new { code = "en", label = "English" },
            },
            defaultLanguage = viewModel.SelectedLanguageOption?.Code ?? "pt",
            liveTranscriptionEnabled = viewModel.LiveTranscriptionEnabled,
            identifySpeakersDefault = viewModel.IdentifySpeakersDefault,
            devices = viewModel.DeviceOptions.Select(d => new { value = d.Value.ToString(), label = d.Label }),
            device = viewModel.SelectedDeviceOption?.Value.ToString() ?? "Auto",
            models = ModelCatalog.All.Select(m => new
            {
                value = m.Value.ToString(),
                label = m.Label,
                sizeLabel = m.SizeLabel,
                speedHint = m.SpeedHint,
                whenToUse = m.WhenToUse,
                description = m.Description,
            }),
            defaultQuality = ModelCatalog.ResolveProfile(
                viewModel.SelectedModelOption?.Value ?? ModelQuality.Standard).ToString(),
            engines = ModelCatalog.Engines.Select(e => new { value = e.Value.ToString(), label = e.Label, hint = e.Hint }),
            defaultEngine = viewModel.SelectedEngineOption?.Value.ToString() ?? "Parakeet",
            parakeetModels = ModelCatalog.ParakeetModels.Select(p => new
            {
                value = p.Value.ToString(),
                label = p.Label,
                sizeLabel = p.SizeLabel,
            }),
            defaultParakeetModel = viewModel.SelectedParakeetModelOption?.Value.ToString() ?? "PtBrTagarela",
            recommendation = new
            {
                quality = viewModel.RecommendedModelOption?.Value.ToString(),
                reason = viewModel.ModelRecommendationReason,
                detectedRamGb = viewModel.DetectedRamGb,
            },
            developer = new
            {
                gpus = viewModel.Gpus.Select(g => new
                {
                    name = g.Name,
                    vendor = g.Vendor,
                    driverVersion = g.DriverVersion,
                    kindLabel = g.KindLabel,
                    ramLabel = g.RamLabel,
                }),
                runtimePreference = viewModel.RuntimePreferenceLabel,
                isRuntimeLoaded = viewModel.IsRuntimeLoaded,
                loadedRuntime = viewModel.LoadedRuntimeLabel,
                loadedBackend = viewModel.LoadedBackendLabel,
                activeGpu = viewModel.ActiveGpuLabel,
                activeGpuSource = viewModel.ActiveGpuSourceLabel,
                logDirectory = viewModel.LogDirectoryPath,
                appVersion = viewModel.AppVersionLabel,
                updateStatus = viewModel.UpdateStatusText,
            },
        };
    }

    // ---------------------------------------------------------------- folder

    private void RegisterFolderMethods()
    {
        _bridge.Register("folder.load", async args =>
        {
            var folderId = GetInt(args, "folderId");
            using var scope = _services.CreateScope();
            var folderService = scope.ServiceProvider.GetRequiredService<FolderService>();
            var library = scope.ServiceProvider.GetRequiredService<LibraryService>();
            var folder = await folderService.GetByIdAsync(folderId)
                ?? throw new InvalidOperationException("Pasta não encontrada.");

            var summaries = await library.GetTranscriptionsForFolderAsync(folderId);
            var cards = summaries
                .Select(summary => new TranscriptionCardViewModel(
                    summary,
                    id => _ = RunTranscriptionActionAsync("open", id),
                    id => _ = RunTranscriptionActionAsync("retry", id),
                    id => _ = RunTranscriptionActionAsync("delete", id),
                    id => _queue?.Cancel(id)))
                .ToList();

            return new
            {
                id = folder.Id,
                title = folder.Title,
                description = folder.Description ?? "",
                icon = folder.Icon,
                colorName = folder.ColorName,
                cards = cards.Select(CardDto),
                isEmpty = cards.Count == 0,
            };
        });

        _bridge.Register("folder.create", async args =>
        {
            var title = GetString(args, "title").Trim();
            if (string.IsNullOrWhiteSpace(title))
            {
                throw new InvalidOperationException("Informe o título da pasta.");
            }

            var icon = GetString(args, "icon") is { Length: > 0 } iconValue ? iconValue : IconCatalog.PageIcons[0];
            var colorName = GetString(args, "colorName") is { Length: > 0 } colorValue ? colorValue : ColorCatalog.PageColors[0].Name;

            using var scope = _services.CreateScope();
            var folderService = scope.ServiceProvider.GetRequiredService<FolderService>();
            var folder = await folderService.CreateAsync(title, icon, colorName);
            await _services.GetRequiredService<SidebarViewModel>().LoadAsync();
            return new { id = folder.Id };
        });

        _bridge.Register("folder.delete", async args =>
        {
            var folderId = GetInt(args, "folderId");
            using var scope = _services.CreateScope();
            var folderService = scope.ServiceProvider.GetRequiredService<FolderService>();
            var folder = await folderService.GetByIdAsync(folderId);
            if (folder is null)
            {
                return null;
            }

            var count = folder.Transcriptions.Count;
            var countMessage = count switch
            {
                0 => "Nenhuma transcrição está associada.",
                1 => "1 transcrição ficará avulsa na biblioteca.",
                _ => $"{count} transcrições ficarão avulsas na biblioteca.",
            };

            if (!await _confirmation.ConfirmAsync(
                    "Excluir pasta",
                    $"A pasta \"{folder.Title}\" será excluída. {countMessage} Deseja continuar?"))
            {
                return new { deleted = false };
            }

            await folderService.DeleteAsync(folderId);
            await _services.GetRequiredService<SidebarViewModel>().LoadAsync();

            _services.GetRequiredService<NavigationService>().NavigateTo(ScreenKey.Dashboard);
            _bridge.SendEvent("navigation.changed", new
            {
                screen = ScreenKey.Dashboard.ToString(),
                parameter = new { },
            });
            return new { deleted = true };
        });

        _bridge.Register("folder.action", async args =>
        {
            var action = GetString(args, "action");
            var id = GetGuid(args, "transcriptionId");
            await RunTranscriptionActionAsync(action, id);
            return null;
        });
    }

    // -------------------------------------------------------------- feedback

    private void RegisterFeedbackMethods()
    {
        _bridge.Register("feedback.open", _ =>
        {
            var viewModel = _services.GetRequiredService<FeedbackViewModel>();
            viewModel.Open();
            return Task.FromResult<object?>(new { statusMessage = viewModel.StatusMessage });
        });

        _bridge.Register("feedback.send", async args =>
        {
            var viewModel = _services.GetRequiredService<FeedbackViewModel>();
            viewModel.IsBug = GetBool(args, "isBug");
            viewModel.Title = GetString(args, "title");
            viewModel.Description = GetString(args, "description");
            await viewModel.SubmitCommand.ExecuteAsync(CancellationToken.None);
            return new { statusMessage = viewModel.StatusMessage, isSuccess = viewModel.IsSuccess };
        });
    }

    // ---------------------------------------------------------------- events

    private void OnSidebarPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SidebarViewModel.UpdateButtonLabel))
            _bridge.SendEvent("sidebar.updateChanged", null);
    }

    private void OnConfirmationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BridgeConfirmationService.IsOpen) && _confirmation.IsOpen)
        {
            _bridge.SendEvent("ui.confirm", new
            {
                title = _confirmation.Title,
                message = _confirmation.Message,
                danger = _confirmation.IsDanger,
            });
        }
    }

    private void OnQueueStatusChanged(object? sender, TranscriptionStatusChangedEventArgs e)
    {
        _bridge.SendEvent("queue.status", new
        {
            transcriptionId = e.TranscriptionId.ToString(),
            status = e.Status.ToString(),
            errorMessage = e.ErrorMessage,
        });
        _ = PushDashboardAsync();
        PushFolderChanged();
    }

    private void OnQueueProgressChanged(object? sender, TranscriptionProgressEventArgs e)
    {
        var navigation = _services.GetService<NavigationService>();
        if (navigation?.CurrentScreen == ScreenKey.Dashboard
            && navigation.CurrentViewModel is DashboardViewModel dashboard)
        {
            var card = dashboard.Cards.FirstOrDefault(c => c.Id == e.TranscriptionId);
            if (card is null)
            {
                return;
            }

            _bridge.SendEvent("dashboard.progress", new
            {
                transcriptionId = e.TranscriptionId,
                progressPercent = card.ProgressPercent,
                progressStage = card.ProgressStage,
                label = card.ProgressLabel,
            });
        }
        else
        {
            _bridge.SendEvent("queue.progress", new
            {
                transcriptionId = e.TranscriptionId,
                stage = e.Stage,
                percent = e.Percent,
            });
        }
    }

    private async Task PushDashboardAsync()
    {
        try
        {
            if (_services.GetService<NavigationService>() is not { CurrentScreen: ScreenKey.Dashboard } navigation
                || navigation.CurrentViewModel is not DashboardViewModel dashboard)
            {
                return;
            }

            await dashboard.LoadAsync();
            _bridge.SendEvent("dashboard.changed", SnapshotDashboard(dashboard));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Push de dashboard falhou");
        }
    }

    private void PushFolderChanged()
    {
        if (_services.GetService<NavigationService>() is { CurrentScreen: ScreenKey.Folder })
        {
            _bridge.SendEvent("folder.changed", new { });
        }
    }

    // -------------------------------------------------------------- helpers

    private DashboardViewModel GetDashboard(NavigationService navigation, NavigationParameter? parameter)
    {
        if (navigation.CurrentScreen == ScreenKey.Dashboard
            && navigation.CurrentViewModel is DashboardViewModel existing)
        {
            if (parameter is not null)
            {
                if (parameter.StatusFilter is { } status)
                {
                    existing.ActiveStatusFilter = status;
                }

                if (parameter.TagId is { } tagId)
                {
                    existing.TagFilterId = tagId;
                }

                existing.UnassignedOnly = parameter.UnassignedOnly;
            }

            return existing;
        }

        navigation.NavigateTo(ScreenKey.Dashboard, parameter);
        return (DashboardViewModel)navigation.CurrentViewModel!;
    }


    private static string GetString(JsonElement? args, string name) =>
        args is { } element && element.TryGetProperty(name, out var value) ? value.GetString() ?? "" : "";

    private static string? GetNullableString(JsonElement? args, string name) =>
        args is { } element && element.TryGetProperty(name, out var value) ? value.GetString() : null;

    private static Guid GetGuid(JsonElement? args, string name) =>
        Guid.Parse(GetString(args, name));

    private static int GetInt(JsonElement? args, string name) =>
        args is { } element && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : 0;

    private static int? GetNullableInt(JsonElement? args, string name) =>
        args is { } element && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : null;

    private static bool GetBool(JsonElement? args, string name) =>
        args is { } element && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static string ParseSearchText(JsonElement? args) => GetString(args, "searchText");

    private async Task<object> SnapshotSidebarAsync()
    {
        var viewModel = _services.GetRequiredService<SidebarViewModel>();
        await viewModel.LoadAsync();
        return new
        {
            folders = viewModel.Folders.Select(folder => new
            {
                id = folder.Id,
                title = folder.Title,
                icon = folder.Icon,
                colorHex = folder.ColorHex,
                transcriptionCount = folder.TranscriptionCount,
                isExpanded = folder.IsExpanded,
                transcriptions = folder.Transcriptions.Select(transcription => new
                {
                    id = transcription.Id,
                    title = transcription.Title,
                    icon = transcription.Icon,
                    isDone = transcription.IsDone,
                    statusIcon = transcription.StatusIcon,
                }),
            }),
            tags = viewModel.Tags.Select(tag => new
            {
                id = tag.Id,
                name = tag.Name,
                colorName = tag.ColorName,
                count = tag.Count,
            }),
            counts = new
            {
                total = viewModel.TotalCount,
                inProgress = viewModel.InProgressCount,
                done = viewModel.DoneCount,
                error = viewModel.ErrorCount,
                unassigned = viewModel.UnassignedCount,
            },
            versionLabel = viewModel.AppVersionLabel,
            update = SnapshotUpdateAction(viewModel),
        };
    }

    private object SnapshotUpdateAction(SidebarViewModel viewModel) => new
    {
        canUpdateNow = viewModel.CanUpdateNow,
        isReady = viewModel.UpdateIsReady,
        hasUpdate = _services.GetService<UpdateCoordinator>()?.AvailableVersion is { } available
            && AppVersion.IsNewer(available, viewModel.AppVersionLabel),
        title = viewModel.UpdateButtonTitle,
        label = viewModel.UpdateButtonLabel,
    };

    private static object SnapshotDashboard(DashboardViewModel viewModel) => new
    {
        activeStatusFilter = viewModel.ActiveStatusFilter.ToString(),
        isEmpty = viewModel.IsEmpty,
        cards = viewModel.Cards.Select(CardDto),
    };

    internal static object CardDto(TranscriptionCardViewModel card) => new
    {
        id = card.Id,
        title = card.Title,
        icon = card.Icon,
        preview = card.Preview,
        tags = card.Tags.Select(tag => new { name = tag.Name, colorName = tag.ColorName }),
        date = card.Date,
        duration = card.Duration,
        durationSeconds = card.DurationSeconds,
        status = card.Status.ToString(),
        statusLabel = card.StatusLabel,
        errorMessage = card.ErrorMessage,
        isInProgress = card.IsInProgress,
        isDone = card.IsDone,
        isError = card.IsError,
        canRetry = card.CanRetry,
        canDelete = card.CanDelete,
        canCancel = card.CanCancel,
        progressPercent = card.ProgressPercent,
        progressStage = card.ProgressStage,
        estimatedTimeLabel = card.EstimatedTimeLabel,
    };

    private static ScreenKey ParseScreen(JsonElement? args)
    {
        if (args is { } element
            && element.TryGetProperty("screen", out var screenElement)
            && Enum.TryParse<ScreenKey>(screenElement.GetString(), ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        return ScreenKey.Dashboard;
    }

    private static NavigationParameter ParseParameter(JsonElement? args)
    {
        if (args is not { } element)
        {
            return new NavigationParameter();
        }

        LibraryStatusFilter? statusFilter = null;
        if (element.TryGetProperty("statusFilter", out var statusElement))
        {
            if (statusElement.ValueKind == JsonValueKind.String
                && Enum.TryParse<LibraryStatusFilter>(statusElement.GetString(), ignoreCase: true, out var status))
            {
                statusFilter = status;
            }
            // Número: formato legado de quando a ponte serializava enums como int —
            // clientes com estado antigo em memória não devem quebrar o load.
            else if (statusElement.ValueKind == JsonValueKind.Number
                     && statusElement.TryGetInt32(out var statusValue)
                     && Enum.IsDefined((LibraryStatusFilter)statusValue))
            {
                statusFilter = (LibraryStatusFilter)statusValue;
            }
        }

        int? tagId = element.TryGetProperty("tagId", out var tagElement)
                     && tagElement.ValueKind == JsonValueKind.Number
                     && tagElement.TryGetInt32(out var tag)
            ? tag
            : null;
        int? folderId = element.TryGetProperty("folderId", out var folderElement)
                     && folderElement.ValueKind == JsonValueKind.Number
                     && folderElement.TryGetInt32(out var folder)
            ? folder
            : null;
        Guid? transcriptionId = element.TryGetProperty("transcriptionId", out var transcriptionElement)
                                && transcriptionElement.ValueKind == JsonValueKind.String
                                && Guid.TryParse(transcriptionElement.GetString(), out var transcription)
            ? transcription
            : null;
        var unassignedOnly = element.TryGetProperty("unassignedOnly", out var unassignedElement)
                             && unassignedElement.ValueKind == JsonValueKind.True;

        return new NavigationParameter(statusFilter, tagId, folderId, transcriptionId, unassignedOnly);
    }

    private static T? ParseEnum<T>(JsonElement? args, string property)
        where T : struct, Enum
    {
        if (args is { } element
            && element.TryGetProperty(property, out var valueElement)
            && valueElement.ValueKind == JsonValueKind.String
            && Enum.TryParse<T>(valueElement.GetString(), ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static string FormatDurationLabel(double seconds)
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

        return $"{Math.Round(duration.TotalSeconds)}s";
    }

    private static string FormatDuration(double seconds) => FormatDurationLabel(seconds);
}
