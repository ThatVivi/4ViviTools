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

    public event Action<string>? Status;

    public EngineHub(GameSession session)
    {
        Session = session;
        Timing = new HumanizedTiming();
        var keys = new KeySender();
        Autopot = new AutopotEngine(session, keys, Timing);
        SkillBuffs = new BuffEngine(session, keys, Timing);
        ItemBuffs = new BuffEngine(session, keys, Timing);
        Spammer = new SkillSpamEngine(session, keys, Timing);
        BotFarm = new BotFarmEngine(session, keys, Timing);
        SmartBot = new SmartBotEngine(session, keys, Timing);
        Macros = new TriggeredMacroEngine(session, keys, Timing);
        foreach (var e in All()) e.Status += s => Status?.Invoke(s);
    }

    private IEnumerable<AutomationEngine> All()
    { yield return Autopot; yield return SkillBuffs; yield return ItemBuffs; yield return Spammer; yield return BotFarm; yield return SmartBot; yield return Macros; }

    public void StartAllLoops() { foreach (var e in All()) e.Start(); }
    public void StopAllLoops() { foreach (var e in All()) e.Stop(); }
}
