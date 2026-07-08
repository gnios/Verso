using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Verso.Core;
using Verso.Core.Data.Entities;
using Verso.Core.Engine;
using Verso.Core.Services;

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
