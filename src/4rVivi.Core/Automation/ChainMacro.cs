namespace FourRVivi.Core.Automation;

public sealed class ChainStep { public string Key { get; set; } = ""; public int GapMs { get; set; } = 120; }

/// <summary>A named key sequence: equip/skill switch, auto-vend, auto-storage, combos, reconnect.</summary>
public sealed class ChainMacro
{
    public string Name { get; set; } = "macro";
    public List<ChainStep> Steps { get; set; } = new();
    public bool LoopWhileOn { get; set; }
    public int IntervalMs { get; set; } = 5000;
}
