using Avalonia;
using Avalonia.Styling;

namespace Transcriba.App.Services;

public class AvaloniaThemeApplicator : IThemeApplicator
{
    public void Apply(bool darkTheme)
    {
        if (Application.Current is null)
        {
            return;
        }

        Application.Current.RequestedThemeVariant =
            darkTheme ? ThemeVariant.Dark : ThemeVariant.Light;
    }
}
