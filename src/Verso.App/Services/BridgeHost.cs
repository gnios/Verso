using System;

namespace Verso.App.Services;

/// <summary>
/// Holder da <see cref="NativeBridge"/> para DI: a bridge precisa do
/// <see cref="Photino.NET.PhotinoWindow"/>, que só existe depois do provider montado.
/// Serviços dependentes resolvem o holder e usam <see cref="Bridge"/> lazy.
/// </summary>
public sealed class BridgeHost
{
    private NativeBridge? _bridge;

    public void Attach(NativeBridge bridge) => _bridge = bridge;

    public NativeBridge Bridge =>
        _bridge ?? throw new InvalidOperationException("NativeBridge ainda não foi anexada.");
}