using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace FourRVivi.App.Controls;

/// <summary>Paired slider and numeric input for pixel/timing values that must update live.</summary>
public partial class SliderWithBox : UserControl
{
    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<SliderWithBox, string>(nameof(Label), "");

    public static readonly StyledProperty<string> UnitProperty =
        AvaloniaProperty.Register<SliderWithBox, string>(nameof(Unit), "");

    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<SliderWithBox, double>(nameof(Minimum), 0);

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<SliderWithBox, double>(nameof(Maximum), 100);

    public static readonly StyledProperty<double> StepProperty =
        AvaloniaProperty.Register<SliderWithBox, double>(nameof(Step), 1);

    public static readonly StyledProperty<double> LargeStepProperty =
        AvaloniaProperty.Register<SliderWithBox, double>(nameof(LargeStep), 10);

    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<SliderWithBox, double>(
            nameof(Value),
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<bool> SnapToStepProperty =
        AvaloniaProperty.Register<SliderWithBox, bool>(nameof(SnapToStep), true);

    public static readonly StyledProperty<bool> ShowRangeProperty =
        AvaloniaProperty.Register<SliderWithBox, bool>(nameof(ShowRange), true);

    public static readonly StyledProperty<string> FormatStringProperty =
        AvaloniaProperty.Register<SliderWithBox, string>(nameof(FormatString), "0");

    public string Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public string Unit { get => GetValue(UnitProperty); set => SetValue(UnitProperty, value); }
    public double Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double Step { get => GetValue(StepProperty); set => SetValue(StepProperty, value); }
    public double LargeStep { get => GetValue(LargeStepProperty); set => SetValue(LargeStepProperty, value); }
    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public bool SnapToStep { get => GetValue(SnapToStepProperty); set => SetValue(SnapToStepProperty, value); }
    public bool ShowRange { get => GetValue(ShowRangeProperty); set => SetValue(ShowRangeProperty, value); }
    public string FormatString { get => GetValue(FormatStringProperty); set => SetValue(FormatStringProperty, value); }

    public string RangeText => Unit.Length == 0
        ? $"{Minimum:0} - {Maximum:0}"
        : $"{Minimum:0} - {Maximum:0} {Unit}";

    public SliderWithBox()
    {
        InitializeComponent();
        this.GetObservable(MinimumProperty).Subscribe(new Observer<double>(_ => RaiseRangeChanged()));
        this.GetObservable(MaximumProperty).Subscribe(new Observer<double>(_ => RaiseRangeChanged()));
        this.GetObservable(UnitProperty).Subscribe(new Observer<string>(_ => RaiseRangeChanged()));
    }

    private void RaiseRangeChanged() => RaisePropertyChanged(RangeTextProperty, "", RangeText);

    private static readonly DirectProperty<SliderWithBox, string> RangeTextProperty =
        AvaloniaProperty.RegisterDirect<SliderWithBox, string>(nameof(RangeText), o => o.RangeText);

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private sealed class Observer<T> : System.IObserver<T>
    {
        private readonly System.Action<T> _onNext;
        public Observer(System.Action<T> onNext) => _onNext = onNext;
        public void OnCompleted() { }
        public void OnError(System.Exception error) { }
        public void OnNext(T value) => _onNext(value);
    }
}
