using System;

namespace Verso.App.Services;

/// <summary>
/// Aplica o tema no shell React via evento bridge <c>theme.changed</c> ({ isDark }). O React
/// é dono do DOM (atributo do tema/classes); o C# só é a fonte de verdade persistida
/// (<see cref="ThemeService"/> + SettingsService).
/// </summary>
public sealed class NativeThemeApplicator : IThemeApplicator
{
    private readonly BridgeHost _host;

    public NativeThemeApplicator(BridgeHost host)
    {
        _host = host;
    }

    public void Apply(bool darkTheme)
    {
        try
        {
            _host.Bridge.SendEvent("theme.changed", new { isDark = darkTheme });
        }
        catch (InvalidOperationException)
        {
            // Bridge ainda não anexada (boot): o React chama theme.get no mount e aplica.
        }
    }
}