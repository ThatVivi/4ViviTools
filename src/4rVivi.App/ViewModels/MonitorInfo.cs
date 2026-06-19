namespace FourRVivi.App.ViewModels;

public sealed class MonitorInfo
{
    public string Name { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int W { get; set; }
    public int H { get; set; }
    public override string ToString() => Name;
}
