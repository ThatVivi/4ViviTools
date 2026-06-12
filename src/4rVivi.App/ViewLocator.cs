using Avalonia.Controls;
using Avalonia.Controls.Templates;
using FourRVivi.App.ViewModels;

namespace FourRVivi.App;

/// <summary>Maps a XxxViewModel instance to its XxxView by naming convention.</summary>
public sealed class ViewLocator : IDataTemplate
{
    public Control Build(object? data)
    {
        if (data is null) return new TextBlock { Text = "null" };
        var name = data.GetType().FullName!
            .Replace("ViewModels.", "Views.", StringComparison.Ordinal)
            .Replace("ViewModel", "View", StringComparison.Ordinal);
        var type = Type.GetType(name);
        if (type is not null) return (Control)Activator.CreateInstance(type)!;
        return new TextBlock { Text = "View not found: " + name };
    }

    public bool Match(object? data) => data is ViewModelBase;
}
