using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Verso.App.Services;
using Verso.Core;
using Verso.Core.Data.Entities;
using Verso.Core.Engine;
using Verso.Core.Services;
using Whisper.net.LibraryLoader;

namespace Verso.App.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly IServiceScopeFactory _scopeFactory;
    private bool _suppressPersist;

    public IReadOnlyList<LanguageOptionViewModel> LanguageOptions { get; } =
    [
        new("pt", "Português (Brasil)"),
        new("es", "Español"),
        new("en", "English"),
    ];

    public IReadOnlyList<DeviceOptionViewModel> DeviceOptions { get; } =
    [
        new(ExecutionDevice.Auto, "Automático"),
        new(ExecutionDevice.Cpu, "CPU"),
        new(ExecutionDevice.Cuda, "CUDA"),
        new(ExecutionDevice.Vulkan, "Vulkan"),
    ];

    public IReadOnlyList<ModelOptionViewModel> ModelOptions { get; } = ModelCatalog.All;

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _email = "";

    [ObservableProperty]
    private string _institution = "";

    [ObservableProperty]
    private LanguageOptionViewModel? _selectedLanguageOption;

    [ObservableProperty]
    private bool _identifySpeakersDefault = true;

    [ObservableProperty]
    private bool _liveTranscriptionEnabled = true;

    [ObservableProperty]
    private DeviceOptionViewModel? _selectedDeviceOption;

    [ObservableProperty]
    private ModelOptionViewModel? _selectedModelOption;

    [ObservableProperty]
    private ModelOptionViewModel? _recommendedModelOption;

    [ObservableProperty]
    private string _modelRecommendationReason = "";


    public long DetectedRamGb { get; private set; }

    // --- Seção Desenvolvedor: runtime/GPU/logs ---

    public IReadOnlyList<GpuInfoViewModel> Gpus { get; private set; } = [];

    public string ConfiguredDeviceLabel =>
        DeviceOptions.FirstOrDefault(o => o.Value == (SelectedDeviceOption?.Value ?? ExecutionDevice.Auto))?.Label ?? "Automático";

    public string RuntimePreferenceLabel =>
        WhisperRuntimeInspector.DescribeRuntimeOrder(SelectedDeviceOption?.Value ?? ExecutionDevice.Auto);

    public bool IsRuntimeLoaded => WhisperRuntimeInspector.LoadedRuntime is not null;

    public string LoadedRuntimeLabel => WhisperRuntimeInspector.LoadedRuntimeLabel;

    public string LoadedBackendLabel =>
        WhisperRuntimeInspector.LoadedBackend ?? "—";

    public ActiveGpuInfo? ActiveGpu { get; private set; }

    public string ActiveGpuLabel =>
        ActiveGpu?.GpuName ?? "—";

    public string ActiveGpuSourceLabel =>
        ActiveGpu?.Source ?? "—";

    public string ActiveGpuNote =>
        ActiveGpu?.Note ?? "";

    public string LogDirectoryPath { get; } = VersoPaths.LogsDirectory;

    [ObservableProperty]
    private string _developerMessage = "";

    public SettingsViewModel(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
        _ = LoadAsync();
    }

    public async Task LoadAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var settingsService = scope.ServiceProvider.GetRequiredService<SettingsService>();
        var settings = await settingsService.GetAsync();

        _suppressPersist = true;
        Name = settings.Name;
        Email = settings.Email;
        Institution = settings.Institution;
        SelectedLanguageOption = LanguageOptions.FirstOrDefault(option => option.Code == settings.DefaultLanguage)
            ?? LanguageOptions[0];
        IdentifySpeakersDefault = settings.IdentifySpeakersDefault;
        LiveTranscriptionEnabled = settings.LiveTranscriptionEnabled;
        SelectedDeviceOption = DeviceOptions.FirstOrDefault(option => option.Value == settings.Device)
            ?? DeviceOptions[0];
        SelectedModelOption = ModelCatalog.Find(settings.DefaultQuality);
        RecomputeModelRecommendation();
        RefreshDeveloperInfoCore();
        _suppressPersist = false;
    }

    [RelayCommand]
    private async Task SaveProfileAsync()
    {
        await UpdateSettingsAsync(settings =>
        {
            settings.Name = Name;
            settings.Email = Email;
            settings.Institution = Institution;
        });
    }

    partial void OnSelectedLanguageOptionChanged(LanguageOptionViewModel? value)
    {
        if (_suppressPersist || value is null)
        {
            return;
        }

        _ = UpdateSettingsAsync(settings => settings.DefaultLanguage = value.Code);
    }

    partial void OnIdentifySpeakersDefaultChanged(bool value)
    {
        if (_suppressPersist)
        {
            return;
        }

        _ = UpdateSettingsAsync(settings => settings.IdentifySpeakersDefault = value);
    }

    partial void OnLiveTranscriptionEnabledChanged(bool value)
    {
        if (_suppressPersist)
        {
            return;
        }

        _ = UpdateSettingsAsync(settings => settings.LiveTranscriptionEnabled = value);
    }

    partial void OnSelectedDeviceOptionChanged(DeviceOptionViewModel? value)
    {
        if (_suppressPersist || value is null)
        {
            return;
        }

        _ = UpdateSettingsAsync(settings => settings.Device = value.Value);
        RecomputeModelRecommendation();
    }

    [RelayCommand]
    private void UseRecommendedModel()
    {
        if (RecommendedModelOption is null)
        {
            return;
        }

        SelectedModelOption = RecommendedModelOption;
    }

    partial void OnSelectedModelOptionChanged(ModelOptionViewModel? value)
    {
        if (_suppressPersist || value is null)
        {
            return;
        }

        _ = UpdateSettingsAsync(settings => settings.DefaultQuality = value.Value);
    }

    private void RecomputeModelRecommendation()
    {
        var ramBytes = SystemMemory.TotalPhysicalMemoryBytes;
        DetectedRamGb = ramBytes <= 0 ? 0 : ramBytes / (1024L * 1024L * 1024L);
        var device = SelectedDeviceOption?.Value ?? ExecutionDevice.Auto;
        var rec = ModelRecommender.Recommend(device, ramBytes);
        RecommendedModelOption = ModelCatalog.Find(rec.Quality);
        ModelRecommendationReason = rec.Reason;
    }

    // --- Seção Desenvolvedor: comandos ---

    [RelayCommand]
    private void RefreshDeveloperInfo()
    {
        RefreshDeveloperInfoCore();
    }

    private void RefreshDeveloperInfoCore()
    {
        using var scope = _scopeFactory.CreateScope();
        var gpuDetector = scope.ServiceProvider.GetRequiredService<GpuDetector>();
        var resolver = scope.ServiceProvider.GetRequiredService<ActiveGpuResolver>();
        Gpus = gpuDetector.Detect().Select(g => new GpuInfoViewModel(g)).ToList();

        // Resolve a GPU física que o backend ATIVO (ou o configurado, se nada carregado
        // ainda) de fato usará — CUDA → nvidia-smi device 0, Vulkan → adaptador dedicado.
        var backend = WhisperRuntimeInspector.LoadedBackend
            ?? WhisperRuntimeInspector.GetBackend(
                WhisperRuntimeConfigurator.ResolveRuntimeOrder(
                    SelectedDeviceOption?.Value ?? ExecutionDevice.Auto).FirstOrDefault());
        ActiveGpu = resolver.Resolve(backend);

        // Notifica mudanças nas propriedades computadas de runtime (não são
        // [ObservableProperty] — dependem do estado estático do whisper.net).
        OnPropertyChanged(nameof(Gpus));
        OnPropertyChanged(nameof(IsRuntimeLoaded));
        OnPropertyChanged(nameof(LoadedRuntimeLabel));
        OnPropertyChanged(nameof(LoadedBackendLabel));
        OnPropertyChanged(nameof(RuntimePreferenceLabel));
        OnPropertyChanged(nameof(ConfiguredDeviceLabel));
        OnPropertyChanged(nameof(ActiveGpu));
        OnPropertyChanged(nameof(ActiveGpuLabel));
        OnPropertyChanged(nameof(ActiveGpuSourceLabel));
        OnPropertyChanged(nameof(ActiveGpuNote));
    }

    [RelayCommand]
    private async Task VerifyRuntimeAsync()
    {
        var device = SelectedDeviceOption?.Value ?? ExecutionDevice.Auto;

        // Se já há um runtime carregado (transcrição/probe anterior), a lib nativa
        // não é descarregada — só reportamos o que está em uso.
        if (WhisperRuntimeInspector.LoadedRuntime is not null)
        {
            DeveloperMessage = $"Runtime já carregado: {WhisperRuntimeInspector.LoadedRuntimeLabel} " +
                               $"({WhisperRuntimeInspector.LoadedBackend}). A troca só passa a valer " +
                               "após reiniciar o aplicativo.";
            OnPropertyChanged(nameof(IsRuntimeLoaded));
            OnPropertyChanged(nameof(LoadedRuntimeLabel));
            OnPropertyChanged(nameof(LoadedBackendLabel));
            return;
        }

        var modelsDir = VersoPaths.ModelsDirectory;
        var quality = SelectedModelOption?.Value ?? ModelQuality.Standard;
        var modelPath = Path.Combine(modelsDir, ModelManager.GetModelFileName(quality));

        if (!File.Exists(modelPath))
        {
            DeveloperMessage = $"Nenhum modelo em {modelPath}. Baixe um modelo na aba " +
                                "“Modelo de transcrição” para verificar o runtime.";
            return;
        }

        DeveloperMessage = $"Verificando runtime para {device}…";
        OnPropertyChanged(nameof(DeveloperMessage));

        try
        {
            var loaded = await Task.Run(() => WhisperRuntimeInspector.ProbeRuntime(modelPath, device));
            DeveloperMessage = loaded is null
                ? "Não foi possível determinar o runtime carregado."
                : $"Runtime carregado: {WhisperRuntimeInspector.GetRuntimeLabel(loaded.Value)} " +
                  $"(backend {WhisperRuntimeInspector.GetBackend(loaded)}).";
        }
        catch (Exception ex)
        {
            DeveloperMessage = $"Falha ao verificar runtime: {ex.Message}";
        }

        // Após um probe, o backend carregado pode ter mudado — re-resolve a GPU ativa.
        RefreshActiveGpu();
        OnPropertyChanged(nameof(IsRuntimeLoaded));
        OnPropertyChanged(nameof(LoadedRuntimeLabel));
        OnPropertyChanged(nameof(LoadedBackendLabel));
        OnPropertyChanged(nameof(DeveloperMessage));
    }

    private void RefreshActiveGpu()
    {
        using var scope = _scopeFactory.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<ActiveGpuResolver>();
        var backend = WhisperRuntimeInspector.LoadedBackend
            ?? WhisperRuntimeInspector.GetBackend(
                WhisperRuntimeConfigurator.ResolveRuntimeOrder(
                    SelectedDeviceOption?.Value ?? ExecutionDevice.Auto).FirstOrDefault());
        ActiveGpu = resolver.Resolve(backend);
        OnPropertyChanged(nameof(ActiveGpu));
        OnPropertyChanged(nameof(ActiveGpuLabel));
        OnPropertyChanged(nameof(ActiveGpuSourceLabel));
        OnPropertyChanged(nameof(ActiveGpuNote));
    }

    [RelayCommand]
    private void OpenLogFolder()
    {
        try
        {
            Directory.CreateDirectory(LogDirectoryPath);
            Process.Start(new ProcessStartInfo("explorer.exe", LogDirectoryPath)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            DeveloperMessage = $"Não foi possível abrir a pasta de logs: {ex.Message}";
        }
    }

    private async Task UpdateSettingsAsync(System.Action<UserSettings> mutate)
    {
        using var scope = _scopeFactory.CreateScope();
        var settingsService = scope.ServiceProvider.GetRequiredService<SettingsService>();
        await settingsService.UpdateAsync(mutate);
    }
}

public sealed record DeviceOptionViewModel(ExecutionDevice Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// Adaptador de <see cref="GpuInfo"/> para a UI da seção Desenvolvedor. Mantém
/// os labels pré-formatados (tipo de placa, RAM) para o Razor renderizar direto.
/// </summary>
public sealed record GpuInfoViewModel(GpuInfo Source)
{
    public string Name => Source.Name;
    public string Vendor => Source.Vendor;
    public string DriverVersion => Source.DriverVersion;
    public string KindLabel => Source.KindLabel;
    public string RamLabel => Source.RamLabel;
}
