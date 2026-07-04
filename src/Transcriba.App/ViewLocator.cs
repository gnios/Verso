using System;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Transcriba.App.ViewModels;

namespace Transcriba.App;

public class ViewLocator : IDataTemplate
{
    public Control? Build(object? data)
    {
        if (data is null)
        {
            return null;
        }

        var viewName = data.GetType().FullName!.Replace("ViewModel", "View", StringComparison.Ordinal);
        var viewType = Type.GetType(viewName);

        if (viewType is not null)
        {
            return (Control)Activator.CreateInstance(viewType)!;
        }

        return new TextBlock { Text = $"View não encontrada: {viewName}" };
    }

    public bool Match(object? data) => data is ViewModelBase;
}
