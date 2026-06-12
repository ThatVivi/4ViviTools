using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace FourRVivi.App.Views;

public partial class AutopotView : UserControl
{
    public AutopotView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}