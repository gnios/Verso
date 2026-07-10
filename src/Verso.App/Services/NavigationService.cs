using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Verso.App.ViewModels;
using Verso.Core.Media;

namespace Verso.App.Services;

/// <summary>
/// Estado de navegação da shell, consumido por componentes Razor (T55).
/// Como esta classe já é um <see cref="ObservableObject"/> (CommunityToolkit.Mvvm), as
/// propriedades marcadas com <c>[ObservableProperty]</c> disparam <c>PropertyChanged</c>
/// automaticamente a cada troca de tela — um componente Razor raiz só precisa se inscrever
/// nesse evento e chamar <c>StateHasChanged()</c> (ver <c>Components/Layout/MainLayout.razor</c>)
/// para re-renderizar a área de conteúdo com a tela/ViewModel atual. A API pública
/// (<see cref="NavigateTo"/>, <see cref="CurrentScreen"/>) permanece inalterada em relação
/// à versão Avalonia.
/// </summary>
public partial class NavigationService : ObservableObject
{
    private readonly IServiceProvider _services;

    [ObservableProperty]
    private ScreenKey _currentScreen = ScreenKey.Dashboard;

    [ObservableProperty]
    private object? _currentViewModel;

    [ObservableProperty]
    private object? _navigationParameter;

    public NavigationService(IServiceProvider services)
    {
        _services = services;
    }

    public void NavigateTo(ScreenKey key, object? parameter = null)
    {
        if (CurrentScreen == ScreenKey.Editor && key != ScreenKey.Editor)
        {
            try
            {
                _services.GetService<IMediaPlaybackService>()?.UnloadAsync().GetAwaiter().GetResult();
            }
            catch (Exception)
            {
                // O serviço de playback pode estar indisponível fora do app desktop (ex.: testes).
            }
        }

        CurrentScreen = key;
        NavigationParameter = parameter;
        var viewModel = ResolveViewModel(key);
        if (viewModel is DashboardViewModel dashboard)
        {
            dashboard.Initialize(parameter as NavigationParameter);
        }
        else if (viewModel is FolderViewModel folderPage)
        {
            folderPage.Initialize(parameter as NavigationParameter);
        }
        else if (viewModel is UploadViewModel upload)
        {
            upload.Initialize(parameter as NavigationParameter);
        }
        else if (viewModel is EditorViewModel editor)
        {
            editor.Initialize(parameter as NavigationParameter);
        }

        CurrentViewModel = viewModel;
    }

    private object ResolveViewModel(ScreenKey key) => key switch
    {
        ScreenKey.Dashboard => _services.GetRequiredService<DashboardViewModel>(),
        ScreenKey.Folder => _services.GetRequiredService<FolderViewModel>(),
        ScreenKey.Upload => _services.GetRequiredService<UploadViewModel>(),
        ScreenKey.Recording => _services.GetRequiredService<RecordingViewModel>(),
        ScreenKey.Editor => _services.GetRequiredService<EditorViewModel>(),
        ScreenKey.Settings => _services.GetRequiredService<SettingsViewModel>(),
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, null)
    };
}
