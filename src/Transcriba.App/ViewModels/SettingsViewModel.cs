using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Transcriba.Core.Data.Entities;
using Transcriba.Core.Services;

namespace Transcriba.App.ViewModels;

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
