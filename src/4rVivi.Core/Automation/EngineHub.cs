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
    public FocusGate FocusGate { get; }

    public event Action<string>? Status;
    private readonly KeySender _keys;
    private readonly MouseSender _mouse;
    private readonly VirtualHidInput _virtualHid;
    private readonly ViiperInput _viiper;

    /// <summary>The input backend every engine uses (SendInput / mouse_event+keybd_event / PostMessage).</summary>
    public FourRVivi.Core.Input.InputMethod InputMethod
    {
        get => _keys.Method;
        set
        {
            _keys.Method = value;
            _mouse.Method = value;
            UpdateInputRuntimeStatus();
        }
    }

    public EngineHub(GameSession session)
    {
        Session = session;
        Timing = new HumanizedTiming();
        _virtualHid = new VirtualHidInput();
        _viiper = new ViiperInput();
        _keys = new KeySender();
        _mouse = new MouseSender();
        FocusGate = new FocusGate(session);
        _keys.FocusGate = FocusGate;
        _mouse.FocusGate = FocusGate;
        _keys.VirtualHid = _virtualHid;
        _keys.Viiper = _viiper;
        _mouse.VirtualHid = _virtualHid;
        _mouse.Viiper = _viiper;
        InputMethod = FourRVivi.Core.Input.InputMethod.Viiper;
        _keys.FallbackToNormalKeyWhenVirtualHidFails = true;
        _mouse.FallbackToNormalClickWhenReWasdRunning = true;
        _mouse.FallbackToNormalClickWhenVirtualHidFails = true;
        var keys = _keys; var mouse = _mouse;
        Autopot = new AutopotEngine(session, keys, mouse, Timing);
        SkillBuffs = new BuffEngine(session, keys, Timing);
        ItemBuffs = new BuffEngine(session, keys, Timing);
        Spammer = new SkillSpamEngine(session, keys, Timing);
        BotFarm = new BotFarmEngine(session, keys, Timing);
        SmartBot = new SmartBotEngine(session, keys, mouse, Timing);
        Macros = new TriggeredMacroEngine(session, keys, Timing);
        AtkDef = new AtkDefEngine(session, keys, mouse, Timing);
        AutoStand = new AutoStandEngine(session, keys, Timing);
        AutoYgg = new AutoYggEngine(session, keys, Timing);
        AutoDebuff = new AutoDebuffEngine(session, keys, Timing);
        foreach (var e in All()) e.Status += s => Status?.Invoke(s);
        UpdateInputRuntimeStatus();
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
    public bool IsVirtualDriverInstalled() => _mouse.IsVirtualDriverInstalled();
    public bool IsVirtualDriverReady() => _mouse.IsVirtualDriverReady();
    public bool EnableVirtualController() => _mouse.EnableVirtualController();
    public bool IsVirtualHidInstalled() => _mouse.IsVirtualHidInstalled();
    public bool IsVirtualHidReady() => _mouse.IsVirtualHidReady();
    public bool EnableVirtualHid() => _mouse.EnableVirtualHid();
    public bool IsViiperInstalled() => _mouse.IsViiperInstalled();
    public bool IsViiperReady() => _mouse.IsViiperReady();
    public bool EnableViiper() => _mouse.EnableViiper();
    public bool IsReWasdRunning() => _mouse.IsReWasdRunning();
    public IReadOnlyList<string> VirtualClickButtonNames => ReWasdMouseMap.ButtonNames;
    public string VirtualClickButton
    {
        get => _mouse.VirtualLeftClickButton;
        set => _mouse.VirtualLeftClickButton = value;
    }
    public int VirtualClickHoldMs
    {
        get => _mouse.VirtualClickHoldMs;
        set => _mouse.VirtualClickHoldMs = value;
    }
    public bool VirtualClickFallback
    {
        get => _mouse.FallbackToNormalClickWhenReWasdRunning || _mouse.FallbackToNormalClickWhenVirtualHidFails;
        set
        {
            _mouse.FallbackToNormalClickWhenReWasdRunning = value;
            _mouse.FallbackToNormalClickWhenVirtualHidFails = value;
            _keys.FallbackToNormalKeyWhenVirtualHidFails = value;
            UpdateInputRuntimeStatus();
        }
    }

    private void UpdateInputRuntimeStatus()
    {
        bool fallback = _mouse.FallbackToNormalClickWhenReWasdRunning || _mouse.FallbackToNormalClickWhenVirtualHidFails;
        string tail = fallback ? " -> normal fallback" : "";
        (string mouse, string keyboard) = InputMethod switch
        {
            InputMethod.Viiper => ($"Mouse: VIIPER USB -> FakerInput/vmouse -> ViGEm{tail}", $"Keyboard: VIIPER USB -> FakerInput{tail}"),
            InputMethod.VirtualHid => ($"Mouse: FakerInput/vmouse -> ViGEm{tail}", $"Keyboard: FakerInput -> controller map{tail}"),
            InputMethod.ReWasdClick => ($"Mouse: ViGEm virtual controller{tail}", $"Keyboard: controller map / SendInput{tail}"),
            InputMethod.PostMessage => ("Mouse: PostMessage", "Keyboard: PostMessage"),
            InputMethod.MouseKeyEvent => ("Mouse: mouse_event", "Keyboard: keybd_event"),
            _ => ("Mouse: SendInput", "Keyboard: SendInput")
        };
        InputRuntimeStatus.SetConfigured(mouse, keyboard);
    }
    public bool TestVirtualLeftClick()
    {
        DebugTrace.Write("EngineHub", $"TestVirtualLeftClick button={VirtualClickButton} hold={VirtualClickHoldMs}.");
        if (!_mouse.IsVirtualDriverReady() && !_mouse.EnableVirtualController())
        {
            DebugTrace.Write("EngineHub", "TestVirtualLeftClick failed: virtual driver not ready.");
            return false;
        }

        _mouse.TapVirtualLeftClick();
        return true;
    }
    public bool TestVirtualButton(string buttonName)
    {
        DebugTrace.Write("EngineHub", $"TestVirtualButton button={buttonName} hold={VirtualClickHoldMs}.");
        if (!_mouse.IsVirtualDriverReady() && !_mouse.EnableVirtualController())
        {
            DebugTrace.Write("EngineHub", "TestVirtualButton failed: virtual driver not ready.");
            return false;
        }

        _mouse.TapVirtualButton(buttonName);
        return true;
    }

    public bool TestVirtualHidClick()
    {
        DebugTrace.Write("EngineHub", $"TestVirtualHidClick hold={VirtualClickHoldMs}.");
        if (!_mouse.EnableVirtualHid())
        {
            DebugTrace.Write("EngineHub", "TestVirtualHidClick failed: virtual HID not ready.");
            return false;
        }

        var prev = _mouse.Method;
        try
        {
            _mouse.Method = InputMethod.VirtualHid;
            var size = _mouse.ClientSize(Session.WindowHandle);
            _mouse.Click(Session.WindowHandle, Math.Max(4, size.w / 2), Math.Max(4, size.h / 2));
            return true;
        }
        finally { _mouse.Method = prev; }
    }

    public bool TestVirtualHidKey(string key)
    {
        DebugTrace.Write("EngineHub", $"TestVirtualHidKey key={key}.");
        if (!_mouse.EnableVirtualHid())
        {
            DebugTrace.Write("EngineHub", "TestVirtualHidKey failed: virtual HID not ready.");
            return false;
        }
        return _keys.VirtualHid?.TapKey(key, 80) == true;
    }

    public bool TestViiperInput()
    {
        DebugTrace.Write("EngineHub", "TestViiperInput.");
        if (!_mouse.EnableViiper())
        {
            DebugTrace.Write("EngineHub", "TestViiperInput failed: VIIPER not ready.");
            return false;
        }

        var prev = _mouse.Method;
        try
        {
            _mouse.Method = InputMethod.Viiper;
            _keys.Method = InputMethod.Viiper;
            var size = _mouse.ClientSize(Session.WindowHandle);
            _mouse.Click(Session.WindowHandle, Math.Max(4, size.w / 2), Math.Max(4, size.h / 2));
            _keys.Tap(Session.WindowHandle, KeyName.ToVk("F2"), 80);
            return true;
        }
        finally
        {
            _mouse.Method = prev;
            _keys.Method = prev;
        }
    }
    /// <summary>Panic stop: disable every feature (loops keep running, but take no action).</summary>
    public void DisableAll() { foreach (var e in All()) e.Enabled = false; }
    /// <summary>Stop every engine loop entirely.</summary>
    public void StopAll() { foreach (var e in All()) e.Stop(); }
    /// <summary>Full process shutdown: disable features, cancel loops, release virtual inputs/devices.</summary>
    public void Shutdown()
    {
        DebugTrace.Write("EngineHub", "Shutdown requested: disabling engines and releasing virtual input.");
        DisableAll();
        StopAll();
        try { _virtualHid.Dispose(); } catch (Exception ex) { DebugTrace.Write("EngineHub", "Virtual HID shutdown failed.", ex); }
        try { _viiper.Dispose(); } catch (Exception ex) { DebugTrace.Write("EngineHub", "VIIPER shutdown failed.", ex); }
        try { _mouse.ShutdownVirtualController(); } catch (Exception ex) { DebugTrace.Write("EngineHub", "Virtual controller shutdown failed.", ex); }
    }
    /// <summary>Blank out every hotkey across all features (so nothing fires until re-bound).</summary>
    public void ClearAllKeys() { foreach (var e in All()) e.ClearKeys(); }
}
