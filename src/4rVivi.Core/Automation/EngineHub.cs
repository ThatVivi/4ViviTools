using FourRVivi.Core.Common;
using FourRVivi.Core.Game;
using FourRVivi.Core.Input;

namespace FourRVivi.Core.Automation;

/// <summary>Owns all engines and the shared timing. Starts their loops once; the master flag gates actions.</summary>
public sealed class EngineHub
{
    public GameSession Session { get; }
    public HumanizedTiming Timing { get; }
    public AutopotEngine Autopot { get; }
    public BuffEngine SkillBuffs { get; }
    public BuffEngine ItemBuffs { get; }
    public SkillSpamEngine Spammer { get; }
    public BotFarmEngine BotFarm { get; }
    public SmartBotEngine SmartBot { get; }
    public TriggeredMacroEngine Macros { get; }
    public AtkDefEngine AtkDef { get; }
    public AutoStandEngine AutoStand { get; }
    public AutoYggEngine AutoYgg { get; }
    public AutoDebuffEngine AutoDebuff { get; }

    public event Action<string>? Status;
    private readonly KeySender _keys;
    private readonly MouseSender _mouse;

    /// <summary>The input backend every engine uses (SendInput / mouse_event+keybd_event / PostMessage).</summary>
    public FourRVivi.Core.Input.InputMethod InputMethod
    {
        get => _keys.Method;
        set { _keys.Method = value; _mouse.Method = value; }
    }

    public EngineHub(GameSession session)
    {
        Session = session;
        Timing = new HumanizedTiming();
        _keys = new KeySender();
        _mouse = new MouseSender();
        var keys = _keys; var mouse = _mouse;
        Autopot = new AutopotEngine(session, keys, Timing);
        SkillBuffs = new BuffEngine(session, keys, Timing);
        ItemBuffs = new BuffEngine(session, keys, Timing);
        Spammer = new SkillSpamEngine(session, keys, Timing);
        BotFarm = new BotFarmEngine(session, keys, Timing);
        SmartBot = new SmartBotEngine(session, keys, Timing);
        Macros = new TriggeredMacroEngine(session, keys, Timing);
        AtkDef = new AtkDefEngine(session, keys, mouse, Timing);
        AutoStand = new AutoStandEngine(session, keys, Timing);
        AutoYgg = new AutoYggEngine(session, keys, Timing);
        AutoDebuff = new AutoDebuffEngine(session, keys, Timing);
        foreach (var e in All()) e.Status += s => Status?.Invoke(s);
    }

    private IEnumerable<AutomationEngine> All()
    {
        yield return Autopot;
        yield return SkillBuffs;
        yield return ItemBuffs;
        yield return Spammer;
        yield return BotFarm;
        yield return SmartBot;
        yield return Macros;
        yield return AtkDef;
        yield return AutoStand;
        yield return AutoYgg;
        yield return AutoDebuff;
    }

    /// <summary>Start every engine loop once (each action gated by its own Enabled flag).</summary>
    public void StartAllLoops() { foreach (var e in All()) e.Start(); }
    /// <summary>Panic stop: disable every feature (loops keep running, but take no action).</summary>
    public void DisableAll() { foreach (var e in All()) e.Enabled = false; }
    /// <summary>Stop every engine loop entirely.</summary>
    public void StopAll() { foreach (var e in All()) e.Stop(); }
    /// <summary>Blank out every hotkey across all features (so nothing fires until re-bound).</summary>
    public void ClearAllKeys() { foreach (var e in All()) e.ClearKeys(); }
}
