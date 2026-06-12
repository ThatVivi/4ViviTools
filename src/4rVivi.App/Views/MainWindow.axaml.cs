using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace FourRVivi.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        // Real translucency where supported (Win11); silently falls back otherwise.
        TransparencyLevelHint = new[]
        {
            WindowTransparencyLevel.Mica,
            WindowTransparencyLevel.AcrylicBlur,
            WindowTransparencyLevel.None
        };
    }
}
