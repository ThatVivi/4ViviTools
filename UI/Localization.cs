// Localization.cs — tiny EN/AR (colloquial / عامي) localization + persistence for 4rVivi.
// Lang.T("English text") returns the Arabic string when Arabic is selected, else the English.
// Choice is saved next to the exe (lang.cfg). namespace _4rVivi.UI. MIT.

using System;
using System.Collections.Generic;
using System.IO;

namespace _4rVivi.UI
{
    public static class Lang
    {
        private static string _cur = "en";
        public static bool IsArabic { get { return _cur == "ar"; } }

        static Lang()
        {
            try
            {
                string f = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lang.cfg");
                if (File.Exists(f)) { string v = File.ReadAllText(f).Trim(); if (v == "ar" || v == "en") _cur = v; }
            }
            catch { }
        }

        public static void Save(string code)
        {
            _cur = (code == "ar") ? "ar" : "en";
            try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lang.cfg"), _cur); } catch { }
        }

        public static string T(string en)
        {
            if (!IsArabic) return en;
            string v;
            return AR.TryGetValue(en, out v) ? v : en;
        }

        // Colloquial Arabic (عامي) — gamer-friendly, not فصحى.
        private static readonly Dictionary<string, string> AR = new Dictionary<string, string>
        {
            // sidebar
            { "Autopot", "أوتو بوشن" },
            { "Autopot Ygg", "أوتو يجدراسيل" },
            { "Buff Skills", "بافات سكِل" },
            { "Buff Items", "بافات أغراض" },
            { "Debuff Cure", "علاج الديبف" },
            { "Spammer", "سبام سكِل" },
            { "Switcher", "تبديل العتاد" },
            { "Songs", "الأغاني" },
            { "ATK / DEF", "هجوم / دفاع" },
            { "Skill Timer", "تايمر السكِل" },
            { "Servers", "السيرفرات" },
            { "Macros", "الماكروهات" },
            { "Database", "قاعدة البيانات" },
            { "Scanner", "السكانر" },
            { "Bot / Farm", "البوت / الفارم" },
            { "Stats", "الإحصائيات" },
            { "Settings", "الإعدادات" },
            // top bar
            { "Process:", "العملية:" },
            { "Profile:", "البروفايل:" },
            { "OFF  (toggle)", "مطفي (تبديل)" },
            { "ON", "شغّال" },
            // settings
            { "Language", "اللغة" },
            { "Humanize timing (jitter / anti-detection)", "توقيت بشري (تشويش / ضد الكشف)" },
            { "Run as Administrator reminder", "تذكير: شغّله كأدمن" },
            { "Panic key: F12 turns everything OFF.", "زر الطوارئ: F12 يطفّي كل شي." },
            { "Restart the app to apply the language.", "سكّر البرنامج وافتحه من جديد عشان تتغيّر اللغة." },
            // database
            { "Game database (rAthena)", "قاعدة بيانات اللعبة (rAthena)" },
            { "Mobs", "وحوش" }, { "Skills", "سكِلات" }, { "Items", "أغراض" }, { "Maps", "خرائط" },
            { "Search", "بحث" },
            { "gamedata.json not found next to the exe.", "ملف gamedata.json مو موجود جنب البرنامج." },
            // macros
            { "Macros & auto-reconnect", "الماكروهات وإعادة الاتصال" },
            { "Username", "اسم المستخدم" }, { "Password", "كلمة السر" },
            { "(stored DPAPI-encrypted, never plaintext)", "(مشفّرة DPAPI، مو نص واضح)" },
            { "Save login", "حفظ الدخول" }, { "Record", "تسجيل" }, { "Stop & save", "إيقاف وحفظ" }, { "Test play", "تجربة تشغيل" },
            // scanner
            { "Memory Scanner", "سكانر الذاكرة" },
            { "Find your HP/SP memory address (ArtMoney-style) if your server's offset is unknown.", "دوّر عنوان الـHP/SP بالذاكرة (مثل ArtMoney) إذا سيرفرك مو معروف." },
            { "Open Scanner", "افتح السكانر" },
            // bot / farm
            { "Target mob IDs (comma)", "أرقام الوحوش (بفواصل)" },
            { "Flee at HP %", "اهرب عند HP %" },
            { "Attack key", "زر الهجوم" }, { "Loot key", "زر اللوت" },
            { "Stop if a GM is detected (anti-GM)", "أوقف إذا ظهر مشرف (ضد GM)" },
            { "Humanized timing (anti-detection)", "توقيت بشري (ضد الكشف)" },
            { "Start farming", "ابدأ الفارم" },
            // stats
            { "Session time", "مدة الجلسة" }, { "Kills", "القتلات" }, { "Kills / hour", "قتلات/ساعة" },
            { "EXP / hour", "خبرة/ساعة" }, { "Zeny / hour", "زيني/ساعة" }, { "Drops logged", "الدروبات المسجلة" },
            { "Session stats", "إحصائيات الجلسة" },
            { "Pick your RO process in the top bar, then turn ON.", "اختر عملية RO من الشريط فوق، وبعدها شغّل." },
            { "TURN ON / OFF", "تشغيل / إيقاف" },
            { "Quick open", "فتح سريع" },
            { "Dashboard", "الرئيسية" },
            { "Advanced Autopot", "أوتو بوشن متقدم" },
            { "Reads HP/SP from memory. Set % or flat value, key, reaction (ms), use-delay (ms). Pick your process in the top bar first.", "يقرأ HP/SP من الذاكرة. حدّد % أو رقم، الزر، وقت رد الفعل، ومهلة الاستخدام. اختر العملية فوق أولاً." },
            { "AutoPots+", "أوتو بوشن+" },
            { "Start", "ابدأ" }, { "Stop", "إيقاف" }, { "Running", "شغّال" }, { "Stopped", "متوقف" },


        };
    }
}
