using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace FourRVivi.App.Views;

public partial class FourRToolsShellView : UserControl
{
    public FourRToolsShellView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
