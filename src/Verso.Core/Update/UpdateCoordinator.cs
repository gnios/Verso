using System.IO.Compression;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Verso.Core.Update;

public sealed class UpdateCoordinator
{
    public const string LockFileName = "update.lock";
    public const string ReadyFileName = "ready.json";
    public const string PackageFileName = "package.zip";
    public const string PayloadFolderName = "payload";

    private readonly IGitHubReleaseClient _releases;
    private readonly IUpdatePackageDownloader _downloader;
    private readonly OverlayUpdateApplier _applier;
    private readonly IUpdateIdleSignal _idle;
    private readonly Func<string> _appDirectory;
    private readonly Func<string> _localVersion;
    private readonly ILogger _logger;
    private readonly object _statusLock = new();

    public UpdateCoordinator(
        IGitHubReleaseClient releases,
        IUpdatePackageDownloader downloader,
        OverlayUpdateApplier applier,
        IUpdateIdleSignal idle,
        Func<string>? appDirectory = null,
        Func<string>? localVersion = null,
        ILogger<UpdateCoordinator>? logger = null)
    {
        _releases = releases;
        _downloader = downloader;
        _applier = applier;
        _idle = idle;
        _appDirectory = appDirectory ?? (() => VersoPaths.AppDirectory);
        _localVersion = localVersion ?? (() => "0.0.0");
        _logger = logger ?? NullLogger<UpdateCoordinator>.Instance;
    }

    public UpdateStatus Status { get; private set; } = UpdateStatus.Idle;

    public string? StatusDetail { get; private set; }

    public string? AvailableVersion { get; private set; }

    public event EventHandler? StatusChanged;

    public string StagingDirectory =>
        Path.Combine(_appDirectory(), OverlayUpdateApplier.StagingFolderName);

    public string PayloadDirectory => Path.Combine(StagingDirectory, PayloadFolderName);

    public bool HasPendingApply() =>
        File.Exists(Path.Combine(StagingDirectory, ReadyFileName))
        && OverlayUpdateApplier.HasAppHost(PayloadDirectory);

    public OverlayApplyResult ApplyPending()
    {
        if (!HasPendingApply())
            return OverlayApplyResult.Aborted("sem update pendente");

        SetStatus(UpdateStatus.Applying, null);
        var result = _applier.Apply(PayloadDirectory, _appDirectory());
        if (result.Success)
        {
            TryDeleteStaging();
            RememberAvailable(null);
            SetStatus(UpdateStatus.Idle, null);
        }
        else
        {
            SetStatus(UpdateStatus.Failed, result.Error);
        }

        return result;
    }

    public async Task<UpdateCheckResult> CheckAndPrepareAsync(CancellationToken cancellationToken = default)
    {
        var appDir = _appDirectory();
        var channel = UpdateChannel.TryLoad(appDir);
        if (channel is null)
        {
            RememberAvailable(null);
            SetStatus(UpdateStatus.Idle, "sem canal");
            return new UpdateCheckResult(UpdateStatus.Idle, false, "sem canal");
        }

        SetStatus(UpdateStatus.Checking, null);

        var lockPath = Path.Combine(appDir, LockFileName);
        FileStream? gate = null;
        try
        {
            gate = new FileStream(
                lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                1,
                FileOptions.DeleteOnClose);
        }
        catch (IOException)
        {
            _logger.LogInformation("Update já em andamento em outra instância.");
            return new UpdateCheckResult(Status, false, "lock");
        }

        try
        {
            if (HasPendingApply())
            {
                RememberAvailable(TryReadReadyTag());
                SetStatus(UpdateStatus.Ready, AvailableVersion);
                return ReadyResult();
            }

            var latest = await _releases.GetLatestAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (latest is null)
            {
                SetStatus(UpdateStatus.Failed, "consulta à release falhou");
                return new UpdateCheckResult(UpdateStatus.Failed, false, StatusDetail);
            }

            if (!AppVersion.IsNewer(latest.TagName, _localVersion()))
            {
                RememberAvailable(latest.TagName);
                SetStatus(UpdateStatus.UpToDate, latest.TagName);
                return new UpdateCheckResult(UpdateStatus.UpToDate, false, latest.TagName);
            }

            var asset = _releases.FindChannelAsset(latest, channel);
            if (asset is null)
            {
                _logger.LogWarning(
                    "Release {Tag} sem asset do canal {Variant}-{Rid}.",
                    latest.TagName,
                    channel.Variant,
                    channel.Rid);
                SetStatus(UpdateStatus.Failed, "asset do canal ausente");
                return new UpdateCheckResult(UpdateStatus.Failed, false, StatusDetail);
            }

            RememberAvailable(latest.TagName);
            SetStatus(UpdateStatus.Downloading, asset.Name);
            Directory.CreateDirectory(StagingDirectory);
            var zipPath = Path.Combine(StagingDirectory, PackageFileName);

            try
            {
                await _downloader.DownloadAsync(asset.BrowserDownloadUrl, zipPath, cancellationToken)
                    .ConfigureAwait(false);
                ExtractPackage(zipPath, PayloadDirectory);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or OperationCanceledException)
            {
                _logger.LogWarning(ex, "Falha ao baixar ou extrair update.");
                TryDeleteFile(zipPath);
                TryDeleteDirectory(PayloadDirectory);
                SetStatus(UpdateStatus.Failed, "download ou extração falhou");
                return new UpdateCheckResult(UpdateStatus.Failed, false, StatusDetail);
            }

            TryDeleteFile(zipPath);
            if (!OverlayUpdateApplier.HasAppHost(PayloadDirectory))
            {
                TryDeleteDirectory(PayloadDirectory);
                SetStatus(UpdateStatus.Failed, "pacote sem Verso.App");
                return new UpdateCheckResult(UpdateStatus.Failed, false, StatusDetail);
            }

            File.WriteAllText(
                Path.Combine(StagingDirectory, ReadyFileName),
                $"{{\"tag\":\"{latest.TagName}\"}}");
            RememberAvailable(latest.TagName);
            SetStatus(UpdateStatus.Ready, latest.TagName);
            return ReadyResult();
        }
        finally
        {
            gate.Dispose();
        }
    }

    private UpdateCheckResult ReadyResult() =>
        new(UpdateStatus.Ready, !_idle.HasActiveWork, StatusDetail);

    private void SetStatus(UpdateStatus status, string? detail)
    {
        lock (_statusLock)
        {
            Status = status;
            StatusDetail = detail;
        }

        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RememberAvailable(string? tag)
    {
        lock (_statusLock)
        {
            AvailableVersion = string.IsNullOrWhiteSpace(tag) ? null : AppVersion.Display(tag);
        }
    }

    private string? TryReadReadyTag()
    {
        var path = Path.Combine(StagingDirectory, ReadyFileName);
        try
        {
            if (!File.Exists(path))
                return null;

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.TryGetProperty("tag", out var tag)
                ? tag.GetString()
                : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return null;
        }
    }

    private static void ExtractPackage(string zipPath, string payloadDirectory)
    {
        if (Directory.Exists(payloadDirectory))
            Directory.Delete(payloadDirectory, true);
        Directory.CreateDirectory(payloadDirectory);

        var payloadRoot = Path.GetFullPath(payloadDirectory);
        using var zip = ZipFile.OpenRead(zipPath);
        foreach (var entry in zip.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name) && entry.FullName.EndsWith('/'))
            {
                var dirRel = entry.FullName.TrimEnd('/');
                if (OverlayUpdateApplier.ShouldSkipRelative(dirRel))
                    continue;
            }

            if (OverlayUpdateApplier.ShouldSkipRelative(entry.FullName))
                continue;
            if (string.IsNullOrEmpty(entry.Name))
                continue;

            var dest = Path.GetFullPath(Path.Combine(payloadDirectory, entry.FullName));
            if (!dest.StartsWith(payloadRoot, StringComparison.OrdinalIgnoreCase))
                continue;

            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            entry.ExtractToFile(dest, overwrite: true);
        }
    }

    private void TryDeleteStaging()
    {
        TryDeleteDirectory(StagingDirectory);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch (IOException)
        {
        }
    }
}
