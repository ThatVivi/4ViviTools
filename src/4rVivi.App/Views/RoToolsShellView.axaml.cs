using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace FourRVivi.App.Views;

public partial class RoToolsShellView : UserControl
{
    public RoToolsShellView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
