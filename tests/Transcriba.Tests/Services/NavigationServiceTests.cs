using Microsoft.Extensions.DependencyInjection;
using Transcriba.App;
using Transcriba.App.Services;
using Transcriba.App.ViewModels;
using Transcriba.Core;
using Transcriba.Core.Data;
using Transcriba.Core.Engine;

namespace Transcriba.Tests.Services;

public class NavigationServiceTests
{
    private static NavigationService CreateNavigationService()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"transcriba-nav-{Guid.NewGuid():N}", "transcriba.db");
        var services = new ServiceCollection();
        services.AddTranscribaDatabase(dbPath);
        services.AddTranscribaEngine();
        services.AddTranscribaServices();
        services.AddTranscribaAppServices();
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
