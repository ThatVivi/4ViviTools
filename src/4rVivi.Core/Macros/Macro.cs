namespace FourRVivi.Core.Macros;

public sealed class MacroStep
{
    public int Vk { get; set; }
    public int HoldMs { get; set; } = 15;
    public int GapMs { get; set; } = 80;
}

public sealed class MacroRecording
{
    public string Name { get; set; } = "macro";
    public List<MacroStep> Steps { get; set; } = new();
}
