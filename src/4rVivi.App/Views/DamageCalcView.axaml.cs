using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace FourRVivi.App.Views;

public partial class DamageCalcView : UserControl
{
    public DamageCalcView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
