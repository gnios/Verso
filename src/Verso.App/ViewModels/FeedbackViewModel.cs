using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Verso.App.Services;
using Verso.Core;
using Verso.Core.Data;
using Verso.Core.Data.Entities;
using Verso.Core.Engine;
using Verso.Core.Services;

namespace Verso.App.ViewModels;

/// <summary>
/// ViewModel do diálogo "Reportar bug / Sugerir melhoria".
/// Envia via <see cref="FeedbackService"/> com informações do sistema
/// e das últimas transcrições anexadas. Rate limit local de 5 min.
/// NUNCA envia texto ou áudio das transcrições.
/// </summary>
public partial class FeedbackViewModel : ViewModelBase
{
    private const int RateLimitMinutes = 5;
    private const int MaxTranscriptionStats = 5;
    private static readonly string RateLimitFile = Path.Combine(VersoPaths.DataRoot, "feedback_last.txt");

    private readonly IServiceScopeFactory _scopeFactory;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private bool _isBug = true;

    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    private string _description = "";

    [ObservableProperty]
    private bool _isSubmitting;

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private bool _isSuccess;

    public FeedbackViewModel(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public void Open()
    {
        Title = "";
        Description = "";
        StatusMessage = "";
        IsSuccess = false;
        IsSubmitting = false;

        if (GetRemainingCooldown() is { } remaining)
        {
            StatusMessage = $"Aguarde {remaining} para enviar outro feedback.";
        }

        IsOpen = true;
    }

    [RelayCommand]
    private void Close() => IsOpen = false;

    [RelayCommand]
    private void SelectBug() => IsBug = true;

    [RelayCommand]
    private void SelectFeature() => IsBug = false;

    [RelayCommand]
    private async Task SubmitAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(Title))
        {
            StatusMessage = "Preencha o título.";
            return;
        }
        if (string.IsNullOrWhiteSpace(Description))
        {
            StatusMessage = "Preencha a descrição.";
            return;
        }
        if (GetRemainingCooldown() is { } cooldown)
        {
            StatusMessage = $"Aguarde {cooldown} para enviar outro feedback.";
            return;
        }

        IsSubmitting = true;
        StatusMessage = "Enviando…";
        OnPropertyChanged(nameof(StatusMessage));

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<FeedbackService>();

            var type = IsBug ? "bug" : "melhoria";
            var body = BuildBody(scope);

            var result = await service.SendAsync(type, Title, body, ct);

            if (result.IsSuccess)
            {
                IsSuccess = true;
                StatusMessage = "Feedback enviado com sucesso!";
                SaveLastSubmission();
            }
            else
            {
                StatusMessage = result.ErrorMessage ?? "Erro desconhecido.";
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Envio cancelado.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erro inesperado: {ex.Message}";
        }
        finally
        {
            IsSubmitting = false;
            OnPropertyChanged(nameof(StatusMessage));
        }
    }

    private string BuildBody(IServiceScope scope)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Description);
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine("## Informações do sistema");
        sb.AppendLine();
        AppendSystemInfo(sb, scope);
        sb.AppendLine();
        sb.AppendLine("## Últimas transcrições");
        sb.AppendLine();
        AppendTranscriptionStats(sb, scope);
        sb.AppendLine();
        sb.AppendLine("*Relatado pelo Verso*");
        return sb.ToString();
    }

    private static void AppendSystemInfo(StringBuilder sb, IServiceScope scope)
    {
        // CPU
        var cpuName = GetCpuName();
        sb.AppendLine($"- **CPU**: {cpuName} ({Environment.ProcessorCount} núcleos lógicos)");

        // RAM
        var ramGb = SystemMemory.TotalPhysicalMemoryGb;
        if (ramGb > 0)
            sb.AppendLine($"- **RAM**: {ramGb} GB");
        else
            sb.AppendLine("- **RAM**: não detectada");

        // GPU(s)
        try
        {
            var gpuDetector = scope.ServiceProvider.GetService<GpuDetector>();
            if (gpuDetector is not null)
            {
                var gpus = gpuDetector.Detect();
                if (gpus.Count > 0)
                {
                    sb.AppendLine("- **GPU(s)**:");
                    foreach (var gpu in gpus)
                    {
                        var tipo = gpu.KindLabel;
                        var ram = gpu.RamLabel;
                        sb.AppendLine($"  - {gpu.Name} ({tipo}, {ram})");
                    }
                }
                else
                {
                    sb.AppendLine("- **GPU(s)**: nenhuma detectada");
                }
            }
        }
        catch
        {
            sb.AppendLine("- **GPU(s)**: erro ao detectar");
        }

        // Runtime Whisper
        try
        {
            var loaded = WhisperRuntimeInspector.LoadedRuntimeLabel;
            var backend = WhisperRuntimeInspector.LoadedBackend ?? "—";
            sb.AppendLine($"- **Runtime Whisper**: {loaded} (backend {backend})");
        }
        catch
        {
            // ignora
        }

        // Versão do app
        var version = System.Reflection.Assembly.GetEntryAssembly()?.GetName()?.Version;
        if (version is not null)
            sb.AppendLine($"- **Versão**: {version.Major}.{version.Minor}.{version.Build}");
        else
            sb.AppendLine("- **Versão**: desconhecida");
        
        // SO
        sb.AppendLine($"- **SO**: {Environment.OSVersion} {(Environment.Is64BitOperatingSystem ? "64 bits" : "32 bits")}");
    }

    private static void AppendTranscriptionStats(StringBuilder sb, IServiceScope scope)
    {
        try
        {
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<VersoDbContext>>();
            using var context = dbFactory.CreateDbContext();

            var recent = context.Transcriptions
                .Where(t => t.Status == TranscriptionStatus.Done && t.ProcessingDurationSeconds.HasValue)
                .OrderByDescending(t => t.CreatedAt)
                .Take(MaxTranscriptionStats)
                .Select(t => new
                {
                    t.CreatedAt,
                    t.DurationSeconds,
                    t.ProcessingDurationSeconds,
                    t.Quality,
                    t.Device,
                })
                .ToList();

            if (recent.Count == 0)
            {
                sb.AppendLine("Nenhuma transcrição concluída ainda.");
                return;
            }

            sb.AppendLine("| # | Data | Modelo | Dispositivo | Áudio | Processamento |");
            sb.AppendLine("|---|------|--------|-------------|-------|---------------|");

            for (var i = 0; i < recent.Count; i++)
            {
                var r = recent[i];
                var date = r.CreatedAt.ToString("dd/MM");
                var model = ModelName(r.Quality);
                var device = DeviceLabel(r.Device);
                var audio = FormatDuration(r.DurationSeconds);
                var proc = r.ProcessingDurationSeconds.HasValue
                    ? FormatDuration(r.ProcessingDurationSeconds.Value)
                    : "—";

                sb.AppendLine($"| {i + 1} | {date} | {model} | {device} | {audio} | {proc} |");
            }
        }
        catch
        {
            sb.AppendLine("Erro ao buscar histórico de transcrições.");
        }
    }

    private static string GetCpuName()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                var name = key?.GetValue("ProcessorNameString")?.ToString();
                if (!string.IsNullOrWhiteSpace(name))
                    return name.Trim();
            }
            catch
            {
                // fallback
            }
        }

        return $"Processador ({Environment.ProcessorCount} núcleos)";
    }

    private static string ModelName(ModelQuality q) =>
        ModelCatalog.Find(q).Label;

    private static string DeviceLabel(ExecutionDevice d) => d switch
    {
        ExecutionDevice.Auto => "Auto",
        ExecutionDevice.Cpu => "CPU",
        ExecutionDevice.Cuda => "CUDA",
        ExecutionDevice.Vulkan => "Vulkan",
        ExecutionDevice.CoreMl => "Core ML",
        _ => d.ToString(),
    };

    private static string FormatDuration(double seconds)
    {
        var span = TimeSpan.FromSeconds(seconds);
        if (span.TotalHours >= 1)
            return $"{(int)span.TotalHours}h {span.Minutes}min";
        if (span.TotalMinutes >= 1)
            return $"{(int)span.TotalMinutes}min {span.Seconds}s";
        return $"{Math.Round(span.TotalSeconds)}s";
    }

    private static TimeSpan? GetRemainingCooldown()
    {
        try
        {
            if (!File.Exists(RateLimitFile))
                return null;

            var text = File.ReadAllText(RateLimitFile).Trim();
            if (!long.TryParse(text, out var ticks))
                return null;

            var elapsed = DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc);
            var remaining = TimeSpan.FromMinutes(RateLimitMinutes) - elapsed;
            return remaining > TimeSpan.Zero ? remaining : null;
        }
        catch
        {
            return null;
        }
    }

    private static void SaveLastSubmission()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RateLimitFile)!);
            File.WriteAllText(RateLimitFile, DateTime.UtcNow.Ticks.ToString());
        }
        catch
        {
            // falha na escrita não impede o envio
        }
    }
}
