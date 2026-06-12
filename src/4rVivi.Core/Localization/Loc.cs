namespace FourRVivi.Core.Localization;

/// <summary>EN/AR (colloquial عامي) string lookup. T(en) returns Arabic when active, else English.</summary>
public sealed class Loc
{
    public string Lang { get; private set; } = "en";
    public bool IsArabic => Lang == "ar";
    public event Action? Changed;

    public void SetLang(string code) { Lang = code == "ar" ? "ar" : "en"; Changed?.Invoke(); }

    public string T(string en) => IsArabic && _ar.TryGetValue(en, out var v) ? v : en;
    public string this[string en] => T(en);

    private static readonly Dictionary<string, string> _ar = new()
    {
        ["Dashboard"] = "الرئيسية", ["Autopot"] = "أوتو بوشن", ["AutoPots+"] = "أوتو بوشن+",
        ["Buffs"] = "البافات", ["Skills"] = "السكِلات", ["Bot / Farm"] = "البوت / الفارم",
        ["Macros"] = "الماكروهات", ["RCX Overlay"] = "أوفرلاي RCX", ["Database"] = "قاعدة البيانات",
        ["Scanner"] = "السكانر", ["Servers"] = "السيرفرات", ["Stats"] = "الإحصائيات",
        ["Settings"] = "الإعدادات", ["Process"] = "العملية", ["Profile"] = "البروفايل",
        ["Opacity"] = "الشفافية", ["Language"] = "اللغة", ["Start"] = "ابدأ", ["Stop"] = "إيقاف",
        ["Search"] = "بحث", ["Reload database"] = "إعادة تحميل القاعدة",
        ["Turn everything ON"] = "تشغيل الكل", ["Turn everything OFF"] = "إيقاف الكل",
        ["Show overlay"] = "اظهر الأوفرلاي", ["Hide overlay"] = "اخفِ الأوفرلاي",
        ["POTIONS"] = "بوشنات", ["COMBAT"] = "قتال", ["AUTOMATION"] = "أتمتة",
        ["DATA"] = "بيانات", ["SYSTEM"] = "النظام",
    };
}
