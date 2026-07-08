using System;
using System.Threading.Tasks;
using Microsoft.JSInterop;

namespace Verso.App.Services;

/// <summary>
/// Alterna a classe <c>dark</c> no elemento raiz do documento hospedado pelo BlazorWebView
/// (equivalente ao <c>html.dark</c> do protótipo), via <c>window.versoInterop.setDarkTheme</c>
/// (ver <c>wwwroot/index.html</c>). Substitui o placeholder <c>WpfThemeApplicator</c> da T51-T54.
///
/// Como esta classe é registrada como singleton no mesmo container de DI do Generic Host, e
/// <see cref="IJSRuntime"/> só existe dentro do escopo de renderização do BlazorWebView, ela não
/// recebe o <see cref="IJSRuntime"/> via construtor: o componente raiz (<c>MainLayout.razor</c>)
/// injeta o seu próprio <see cref="IJSRuntime"/> e o repassa via <see cref="AttachJsRuntime"/> no
/// primeiro render. Chamadas a <see cref="Apply"/> antes disso (ex.: <c>ThemeService.InitializeAsync</c>
/// rodando em <c>OnInitializedAsync</c>, antes do primeiro <c>OnAfterRenderAsync</c>) ficam
/// pendentes e são aplicadas assim que o runtime estiver disponível.
/// </summary>
public sealed class BlazorThemeApplicator : IThemeApplicator
{
    private IJSRuntime? _jsRuntime;
    private bool? _pendingDarkTheme;

    public void AttachJsRuntime(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
        if (_pendingDarkTheme is { } pending)
        {
            _pendingDarkTheme = null;
            Apply(pending);
        }
    }

    public void Apply(bool darkTheme)
    {
        if (_jsRuntime is null)
        {
            _pendingDarkTheme = darkTheme;
            return;
        }

        _ = ApplyCoreAsync(_jsRuntime, darkTheme);
    }

    private static async Task ApplyCoreAsync(IJSRuntime jsRuntime, bool darkTheme)
    {
        try
        {
            await jsRuntime.InvokeVoidAsync("versoInterop.setDarkTheme", darkTheme);
        }
        catch (JSDisconnectedException)
        {
            // WebView foi fechado/recarregado entre o Apply e a execução do interop — sem ação.
        }
        catch (ObjectDisposedException)
        {
            // Mesmo cenário acima, em outra forma de exceção dependendo do estado do WebView.
        }
    }
}
