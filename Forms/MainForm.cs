// MainForm.cs — modern sidebar shell for 4rVivi (WinForms, pure code, no .Designer).
// Wires Theme + NavButton + StatBar + the feature panels into one window.
// Database and Macros tabs are functionally wired; the others are structured panels
// ready for you to drop the existing 4RTools feature controls into.
// .NET Framework 4.x, x86. Put in 4rVivi/Forms/. MIT.
//
// To use: set this as the startup form in Program.cs:
//     Application.Run(new _4rVivi.Forms.MainForm());

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using _4rVivi.UI;
using _4rVivi.Data;
using _4rVivi.Macros;

namespace _4rVivi.Forms
{
    public class MainForm : Form
    {
        private Panel _sidebar, _content, _topbar;
        private Label _status;
        private CheckBox _masterToggle;
        private readonly Dictionary<string, Control> _pages = new Dictionary<string, Control>();
        private readonly List<NavButton> _navButtons = new List<NavButton>();

        // shared services
        private DataService _data;
        private readonly Credentials _creds = new Credentials();
        private readonly MacroRecorder _recorder = new MacroRecorder();
        private readonly MacroPlayer _player = new MacroPlayer();
        private MacroRecording _lastRecording;

        public MainForm()
        {
            Text = "4rVivi";
            Width = 900; Height = 600;
            MinimumSize = new Size(760, 520);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9f);

            TryLoadData();
            BuildTopBar();
            BuildSidebar();
            BuildContent();

            // page order = sidebar order
            AddPage("Autopot", "ti", BuildAutopotPage());
            AddPage("Autobuff", "", BuildPlaceholder("Autobuff", "HP/SP/status pots, self-buff skills & items, anti-debuff."));
            AddPage("Spammer", "", BuildPlaceholder("Skill spammer", "Single/multi-key spam. Delays auto-filled from the rAthena DB."));
            AddPage("Switcher", "", BuildPlaceholder("Item switcher", "Up to 8 gear sets, ATK/DEF modes, chain switch."));
            AddPage("Songs", "", BuildPlaceholder("Song spammer", "4 sets x 7 songs, auto weapon switch."));
            AddPage("Macros", "", BuildMacrosPage());
            AddPage("Bot / Farm", "", BuildPlaceholder("Bot / Farm", "Cruise control, mob-search, autoloot, anti-stuck. Wire Anti-GM guard first."));
            AddPage("Database", "", BuildDatabasePage());
            AddPage("Stats", "", BuildPlaceholder("Session stats", "EXP/hr, zeny/hr, kills, drop log."));
            AddPage("Settings", "", BuildSettingsPage());

            Theme.Apply(this);
            ShowPage("Autopot");
        }

        private void TryLoadData()
        {
            try { _data = new DataService(); }
            catch { _data = null; } // gamedata.db missing -> Database tab shows a hint
        }

        // ---------------- shell ----------------
        private void BuildTopBar()
        {
            _topbar = new Panel { Dock = DockStyle.Top, Height = 48, BackColor = Theme.Surface };
            var title = new Label { Text = "  4rVivi", Dock = DockStyle.Left, Width = 178,
                TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                ForeColor = Theme.Text };
            _masterToggle = new CheckBox { Text = "OFF  (F12 panic)", Appearance = Appearance.Button,
                Dock = DockStyle.Right, Width = 160, TextAlign = ContentAlignment.MiddleCenter,
                FlatStyle = FlatStyle.Flat, BackColor = Theme.Surface2, ForeColor = Theme.Text };
            _masterToggle.CheckedChanged += (s, e) =>
            {
                _masterToggle.Text = _masterToggle.Checked ? "ON" : "OFF  (F12 panic)";
                _masterToggle.BackColor = _masterToggle.Checked ? Theme.Ok : Theme.Surface2;
                SetStatus(_masterToggle.Checked ? "Running." : "Stopped.");
            };
            _status = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Theme.TextMuted, Text = "Ready." };
            _topbar.Controls.Add(_status);
            _topbar.Controls.Add(_masterToggle);
            _topbar.Controls.Add(title);
            Controls.Add(_topbar);

            // F12 = panic OFF
            KeyPreview = true;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.F12) _masterToggle.Checked = false; };
        }

        private void BuildSidebar()
        {
            _sidebar = new Panel { Dock = DockStyle.Left, Width = 178, BackColor = Theme.Surface };
            Controls.Add(_sidebar);
        }

        private void BuildContent()
        {
            _content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Padding = new Padding(18) };
            Controls.Add(_content);
            _content.BringToFront();
        }

        private void AddPage(string name, string icon, Control page)
        {
            page.Dock = DockStyle.Fill;
            page.Visible = false;
            _content.Controls.Add(page);
            _pages[name] = page;

            var nav = new NavButton(name);
            nav.Click += (s, e) => ShowPage(name);
            // sidebar stacks top-down; insert so first added sits on top
            _sidebar.Controls.Add(nav);
            nav.BringToFront();
            _navButtons.Add(nav);
        }

        private void ShowPage(string name)
        {
            foreach (var kv in _pages) kv.Value.Visible = kv.Key == name;
            foreach (var nav in _navButtons) nav.SetActive(nav.Text.Trim() == name);
        }

        private void SetStatus(string s) { if (_status != null) _status.Text = s; }

        // ---------------- pages ----------------
        private Control BuildPlaceholder(string title, string desc)
        {
            var p = new Panel();
            p.Controls.Add(new Label { Text = desc, Top = 44, Left = 4, AutoSize = true, ForeColor = Theme.TextMuted, MaximumSize = new Size(560, 0) });
            p.Controls.Add(new Label { Text = title, Top = 4, Left = 4, AutoSize = true, Font = new Font("Segoe UI", 15f, FontStyle.Bold), ForeColor = Theme.Text });
            p.Controls.Add(new Label { Text = "Drop the existing 4RTools control for this feature here.", Top = 88, Left = 4, AutoSize = true, ForeColor = Theme.TextMuted });
            return p;
        }

        private Control BuildAutopotPage()
        {
            var p = new Panel();
            p.Controls.Add(new Label { Text = "Autopot", Top = 4, Left = 4, AutoSize = true, Font = new Font("Segoe UI", 15f, FontStyle.Bold), ForeColor = Theme.Text });

            var hp = new Theme.StatBar { Caption = "HP", Value = 72, Fill = Theme.Danger, Left = 4, Top = 48, Width = 360 };
            var sp = new Theme.StatBar { Caption = "SP", Value = 48, Fill = Theme.Info, Left = 4, Top = 74, Width = 360 };
            p.Controls.Add(hp); p.Controls.Add(sp);

            p.Controls.Add(new Label { Text = "HP % threshold", Top = 116, Left = 4, AutoSize = true, ForeColor = Theme.TextMuted });
            p.Controls.Add(new NumericUpDown { Left = 140, Top = 112, Width = 60, Minimum = 1, Maximum = 100, Value = 50 });
            p.Controls.Add(new Label { Text = "HP key", Top = 116, Left = 220, AutoSize = true, ForeColor = Theme.TextMuted });
            p.Controls.Add(new TextBox { Left = 270, Top = 112, Width = 70, Text = "F1" });

            p.Controls.Add(new Label { Text = "SP % threshold", Top = 150, Left = 4, AutoSize = true, ForeColor = Theme.TextMuted });
            p.Controls.Add(new NumericUpDown { Left = 140, Top = 146, Width = 60, Minimum = 1, Maximum = 100, Value = 40 });
            p.Controls.Add(new Label { Text = "SP key", Top = 150, Left = 220, AutoSize = true, ForeColor = Theme.TextMuted });
            p.Controls.Add(new TextBox { Left = 270, Top = 146, Width = 70, Text = "2" });

            var scan = new Button { Text = "Find HP/SP offset (Scanner)", Left = 4, Top = 188, Width = 220, Height = 30 };
            scan.Click += (s, e) => { using (var f = new ScannerForm()) f.ShowDialog(this); };
            p.Controls.Add(scan);
            return p;
        }

        private Control BuildMacrosPage()
        {
            var p = new Panel();
            p.Controls.Add(new Label { Text = "Macros & auto-reconnect", Top = 4, Left = 4, AutoSize = true, Font = new Font("Segoe UI", 15f, FontStyle.Bold), ForeColor = Theme.Text });

            // credentials
            p.Controls.Add(new Label { Text = "Username", Top = 50, Left = 4, AutoSize = true, ForeColor = Theme.TextMuted });
            var user = new TextBox { Left = 110, Top = 46, Width = 200 };
            p.Controls.Add(user);
            p.Controls.Add(new Label { Text = "Password", Top = 82, Left = 4, AutoSize = true, ForeColor = Theme.TextMuted });
            var pass = new TextBox { Left = 110, Top = 78, Width = 200, UseSystemPasswordChar = true };
            p.Controls.Add(pass);
            p.Controls.Add(new Label { Text = "(stored DPAPI-encrypted, never plaintext)", Top = 82, Left = 320, AutoSize = true, ForeColor = Theme.TextMuted });

            var saveCreds = new Button { Text = "Save login", Left = 110, Top = 110, Width = 110, Height = 28 };
            saveCreds.Click += (s, e) =>
            {
                _creds.Username = user.Text;
                _creds.SetPassword(pass.Text);
                _creds.Save("login.dat");
                SetStatus("Login saved (encrypted).");
            };
            p.Controls.Add(saveCreds);

            // recorder
            var rec = new Button { Text = "Record", Left = 4, Top = 160, Width = 90, Height = 30 };
            var stop = new Button { Text = "Stop & save", Left = 100, Top = 160, Width = 110, Height = 30, Enabled = false };
            var play = new Button { Text = "Test play", Left = 216, Top = 160, Width = 100, Height = 30, Enabled = false };
            rec.Click += (s, e) =>
            {
                _recorder.Start("login");
                rec.Enabled = false; stop.Enabled = true;
                SetStatus("Recording… perform your login, then Stop & save. (Type your user/pass manually now; replace with tokens in the saved file to auto-fill.)");
            };
            stop.Click += (s, e) =>
            {
                _lastRecording = _recorder.Stop();
                _lastRecording.Save("login_macro.json");
                rec.Enabled = true; stop.Enabled = false; play.Enabled = true;
                SetStatus($"Saved login_macro.json ({_lastRecording.Events.Count} events). Edit it to insert TypeUsername/TypePassword where the fields are.");
            };
            play.Click += (s, e) =>
            {
                if (_lastRecording != null)
                    System.Threading.Tasks.Task.Run(() =>
                        _player.Play(_lastRecording, () => _creds.Username, () => _creds.GetPassword()));
            };
            p.Controls.Add(rec); p.Controls.Add(stop); p.Controls.Add(play);

            p.Controls.Add(new Label
            {
                Text = "Reconnect, auto-sell and auto-storage are recordings bound to a condition\n(see TriggeredMacros.cs). Record each flow, then bind it in Settings.",
                Top = 204, Left = 4, AutoSize = true, ForeColor = Theme.TextMuted
            });
            return p;
        }

        private Control BuildDatabasePage()
        {
            var p = new Panel();
            p.Controls.Add(new Label { Text = "Game database (rAthena)", Top = 4, Left = 4, AutoSize = true, Font = new Font("Segoe UI", 15f, FontStyle.Bold), ForeColor = Theme.Text });

            var kind = new ComboBox { Left = 4, Top = 46, Width = 110, DropDownStyle = ComboBoxStyle.DropDownList };
            kind.Items.AddRange(new object[] { "Mobs", "Skills", "Items", "Maps" });
            kind.SelectedIndex = 0;
            var query = new TextBox { Left = 120, Top = 46, Width = 260 };
            var go = new Button { Text = "Search", Left = 386, Top = 45, Width = 90, Height = 26 };
            var list = new ListView { Left = 4, Top = 82, Width = 600, Height = 360, View = View.Details, FullRowSelect = true, GridLines = true, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom };
            list.Columns.Add("ID", 70); list.Columns.Add("Name", 220); list.Columns.Add("Info", 300);
            p.Controls.AddRange(new Control[] { kind, query, go, list });

            if (_data == null)
            {
                p.Controls.Add(new Label { Text = "gamedata.db not found — place it next to the exe (see build guide).", Top = 460, Left = 4, AutoSize = true, ForeColor = Theme.Danger });
                return p;
            }

            go.Click += (s, e) =>
            {
                list.Items.Clear();
                string q = query.Text.Trim();
                if (q.Length == 0) return;
                switch (kind.SelectedItem.ToString())
                {
                    case "Mobs":
                        foreach (var m in _data.SearchMobs(q))
                            Row(list, m.Id, m.Name, $"Lv{m.Level} · HP {m.Hp} · {m.Race}/{m.Element} · EXP {m.BaseExp}");
                        break;
                    case "Skills":
                        foreach (var sk in _data.SearchSkills(q))
                            Row(list, sk.Id, sk.Name, $"cast {sk.CastTimeMs}ms · delay {sk.AfterCastDelayMs}ms · cd {sk.CooldownMs}ms");
                        break;
                    case "Items":
                        foreach (var it in _data.SearchItems(q))
                            Row(list, it.Id, it.Name, $"{it.Type} · slots {it.Slots} · wt {it.Weight}");
                        break;
                    case "Maps":
                        foreach (var mp in _data.SearchMaps(q))
                            Row(list, 0, mp, "");
                        break;
                }
                SetStatus($"{list.Items.Count} result(s).");
            };
            return p;
        }

        private static void Row(ListView lv, int id, string name, string info)
        {
            var it = new ListViewItem(id == 0 ? "" : id.ToString());
            it.SubItems.Add(name ?? ""); it.SubItems.Add(info ?? "");
            lv.Items.Add(it);
        }

        private Control BuildSettingsPage()
        {
            var p = new Panel();
            p.Controls.Add(new Label { Text = "Settings", Top = 4, Left = 4, AutoSize = true, Font = new Font("Segoe UI", 15f, FontStyle.Bold), ForeColor = Theme.Text });
            p.Controls.Add(new CheckBox { Text = "Run as Administrator reminder", Top = 50, Left = 4, AutoSize = true, Checked = true, ForeColor = Theme.Text });
            p.Controls.Add(new CheckBox { Text = "Humanize timing (anti-detection)", Top = 80, Left = 4, AutoSize = true, Checked = true, ForeColor = Theme.Text });
            p.Controls.Add(new Label { Text = "Bind triggered macros (reconnect / auto-sell / auto-storage) here.", Top = 120, Left = 4, AutoSize = true, ForeColor = Theme.TextMuted });
            return p;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (_recorder.Recording) _recorder.Stop();
            _data?.Dispose();
            base.OnFormClosed(e);
        }
    }
}
