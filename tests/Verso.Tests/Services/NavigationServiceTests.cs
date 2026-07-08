using Microsoft.Extensions.DependencyInjection;
using Verso.App;
using Verso.App.Services;
using Verso.App.ViewModels;
using Verso.Core;
using Verso.Core.Data;
using Verso.Core.Engine;

namespace Verso.Tests.Services;

public class NavigationServiceTests
{
    private static NavigationService CreateNavigationService()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"transcriba-nav-{Guid.NewGuid():N}", "verso.db");
        var services = new ServiceCollection();
        services.AddVersoDatabase(dbPath);
        services.AddVersoEngine();
        services.AddVersoServices();
        services.AddVersoAppServices();
        var provider = services.BuildServiceProvider();
        DbBootstrapper.MigrateAsync(provider).GetAwaiter().GetResult();
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
