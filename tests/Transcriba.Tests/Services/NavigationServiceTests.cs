using Microsoft.Extensions.DependencyInjection;
using Transcriba.App;
using Transcriba.App.Services;
using Transcriba.App.ViewModels;

namespace Transcriba.Tests.Services;

public class NavigationServiceTests
{
    private static NavigationService CreateNavigationService()
    {
        var services = new ServiceCollection();
        services.AddTranscribaAppServices();
        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<NavigationService>();
    }

    [Fact]
    public void NavigateTo_ChangesCurrentScreen()
    {
        var navigation = CreateNavigationService();

        navigation.NavigateTo(ScreenKey.Settings);

        Assert.Equal(ScreenKey.Settings, navigation.CurrentScreen);
    }

    [Fact]
    public void NavigateTo_ResolvesCorrectViewModelPerScreenKey()
    {
        var navigation = CreateNavigationService();

        navigation.NavigateTo(ScreenKey.Dashboard);
        Assert.IsType<DashboardViewModel>(navigation.CurrentViewModel);

        navigation.NavigateTo(ScreenKey.Editor);
        Assert.IsType<EditorViewModel>(navigation.CurrentViewModel);

        navigation.NavigateTo(ScreenKey.Recording);
        Assert.IsType<RecordingViewModel>(navigation.CurrentViewModel);
    }

    [Fact]
    public void NavigateTo_StoresNavigationParameter()
    {
        var navigation = CreateNavigationService();
        var parameter = new NavigationParameter(TagId: 3);

        navigation.NavigateTo(ScreenKey.Dashboard, parameter);

        Assert.Same(parameter, navigation.NavigationParameter);
    }
}
