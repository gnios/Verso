using System;
using Photino.NET;

namespace Verso.App.Services;

public sealed class PhotinoWindowAccessor
{
    private PhotinoWindow? _window;

    public void Attach(PhotinoWindow window) => _window = window;

    public PhotinoWindow Window =>
        _window ?? throw new InvalidOperationException("PhotinoWindow ainda não foi anexada.");
}
