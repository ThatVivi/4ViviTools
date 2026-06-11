// MainForm.cs — modern sidebar shell for 4rVivi that HOSTS the real 4RTools feature forms.
// Each feature form (Autopot, Spammer, Switcher, Songs, Autobuff, etc.) is embedded as a
// child control, wired to a shared Subject + process/profile selection (same as the original
// Container). Plus 4rVivi-only tabs: Database, Macros, Scanner.
// .NET Framework 4.x, x86. namespace _4rVivi.Forms.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using Newtonsoft.Json;
using _4RTools.Forms;     // AutopotForm, AHKForm, MacroSwitchForm, ... (the working features)
using _4RTools.Model;     // Client, ClientSingleton, ClientListSingleton, ClientDTO, Profile, ProfileSingleton
using _4RTools.Utils;     // Subject, Message, MessageCode
using _4rVivi.UI;
using _4rVivi.Data;
using _4rVivi.Macros;
using Message = _4RTools.Utils.Message;        // disambiguate from System.Windows.Forms.Message

namespace _4rVivi.Forms
{
    public class MainForm : Form
    {
        private readonly Subject subject = new Subject();
        private Panel _sidebar, _content, _topbar;
        private FlowLayoutPanel _nav;
        private ComboBox _processCB, _profileCB;
        private CheckBox _master;
        private Label _status;
        private readonly Dictionary<string, Control> _pages = new Dictionary<string, Control>();
        private readonly List<NavButton> _navButtons = new List<NavButton>();

        private DataService _data;
        private readonly Credentials _creds = new Credentials();
        private readonly MacroRecorder _recorder = new MacroRecorder();
        private readonly MacroPlayer _player = new MacroPlayer();
        private MacroRecording _lastRecording;

        public MainForm()
        {
            Text = "4rVivi";
            Width = 960; Height = 640;
            MinimumSize = new Size(820, 560);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9f);

            LoadServers();
            try { _data = new DataService(); } catch { _data = null; }

            BuildTopBar();
            BuildSidebar();
            BuildContent();

            // ---- real 4RTools feature forms ----
            AddFormPage("Autopot", new AutopotForm(subject, false));
            AddFormPage("Autopot Ygg", new AutopotForm(subject, true));
            AddFormPage("Buff Skills", new SkillAutoBuffForm(subject));
            AddFormPage("Buff Items", new StuffAutoBuffForm(subject));
            AddFormPage("Debuff Cure", new DebuffRecoveryForm(subject));
            AddFormPage("Spammer", new AHKForm(subject));
            AddFormPage("Switcher", new MacroSwitchForm(subject));
            AddFormPage("Songs", new MacroSongForm(subject));
            AddFormPage("ATK / DEF", new ATKDEFForm(subject));
            AddFormPage("Skill Timer", new SkillTimerForm(subject));
            AddFormPage("Servers", new ServersForm(subject));
            // ---- 4rVivi-only pages ----
            AddPage("Macros", BuildMacrosPage());
            AddPage("Database", BuildDatabasePage());
            AddPage("Scanner", BuildScannerPage());

            InitSelections();
            if (_navButtons.Count > 0) ShowPage(_navButtons[0].Text.Trim());
        }

        // ---------------- server / process / profile wiring ----------------
        private void LoadServers()
        {
            try
            {
                foreach (ClientDTO dto in LocalServerManager.GetLocalClients())
                    try { ClientListSingleton.AddClient(new Client(dto)); } catch { }
            }
            catch { }
            try
            {
                var list = JsonConvert.DeserializeObject<List<ClientDTO>>(
                    _4RTools.Resources._4RTools.ETCResource.supported_servers);
                if (list != null)
                    foreach (ClientDTO dto in list)
                        try { ClientListSingleton.AddClient(new Client(dto)); } catch { }
            }
            catch { }
        }

        private void InitSelections()
        {
            try { ProfileSingleton.Create("Default"); } catch { }
            RefreshProcessList();
            RefreshProfileList();
            try { _profileCB.SelectedItem = "Default"; } catch { }
        }

        private void RefreshProcessList()
        {
            _processCB.Items.Clear();
            foreach (Process p in Process.GetProcesses())
            {
                try
                {
                    if (p.MainWindowTitle != "" && ClientListSingleton.ExistsByProcessName(p.ProcessName))
                        _processCB.Items.Add(string.Format("{0}.exe - {1}", p.ProcessName, p.Id));
                }
                catch { }
            }
        }

        private void RefreshProfileList()
        {
            _profileCB.Items.Clear();
            foreach (string p in Profile.ListAll()) _profileCB.Items.Add(p);
        }

        // ---------------- shell ----------------
        private void BuildTopBar()
        {
            _topbar = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Theme.Surface };

            var title = new Label { Text = "  4rVivi", Dock = DockStyle.Left, Width = 120,
                TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                ForeColor = Theme.Text };

            var lblP = new Label { Text = "Process:", Left = 130, Top = 16, Width = 56, ForeColor = Theme.TextMuted };
            _processCB = new ComboBox { Left = 188, Top = 13, Width = 230, DropDownStyle = ComboBoxStyle.DropDownList };
            _processCB.SelectedIndexChanged += (s, e) =>
            {
                try
                {
                    ClientSingleton.Instance(new Client(_processCB.SelectedItem.ToString()));
                    subject.Notify(new Message(MessageCode.PROCESS_CHANGED, null));
                    SetStatus("Process selected: " + _processCB.SelectedItem);
                }
                catch (Exception ex) { SetStatus("Process error: " + ex.Message); }
            };
            var btnR = new Button { Text = "↻", Left = 422, Top = 12, Width = 30, Height = 26 };
            btnR.Click += (s, e) => RefreshProcessList();

            var lblPr = new Label { Text = "Profile:", Left = 466, Top = 16, Width = 48, ForeColor = Theme.TextMuted };
            _profileCB = new ComboBox { Left = 516, Top = 13, Width = 150, DropDownStyle = ComboBoxStyle.DropDownList };
            _profileCB.SelectedIndexChanged += (s, e) =>
            {
                try
                {
                    if (_profileCB.Text.Length == 0) return;
                    ProfileSingleton.Load(_profileCB.Text);
                    subject.Notify(new Message(MessageCode.PROFILE_CHANGED, null));
                    SetStatus("Profile: " + _profileCB.Text);
                }
                catch (Exception ex) { SetStatus("Profile error: " + ex.Message); }
            };

            _master = new CheckBox { Text = "OFF  (toggle)", Appearance = Appearance.Button, Dock = DockStyle.Right,
                Width = 150, TextAlign = ContentAlignment.MiddleCenter, FlatStyle = FlatStyle.Flat,
                BackColor = Theme.Surface2, ForeColor = Theme.Text };
            _master.FlatAppearance.BorderColor = Theme.Border;
            _master.CheckedChanged += (s, e) =>
            {
                _master.Text = _master.Checked ? "ON" : "OFF  (toggle)";
                _master.BackColor = _master.Checked ? Theme.Ok : Theme.Surface2;
                subject.Notify(new Message(_master.Checked ? MessageCode.TURN_ON : MessageCode.TURN_OFF, null));
            };

            _topbar.Controls.AddRange(new Control[] { lblP, _processCB, btnR, lblPr, _profileCB, _master, title });
            Controls.Add(_topbar);

            // F12 = panic off
            KeyPreview = true;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.F12) _master.Checked = false; };
        }

        private void BuildSidebar()
        {
            _sidebar = new Panel { Dock = DockStyle.Left, Width = 168, BackColor = Theme.Surface };
            _nav = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
                WrapContents = false, AutoScroll = true, BackColor = Theme.Surface, Padding = new Padding(6, 8, 6, 8) };
            _sidebar.Controls.Add(_nav);
            Controls.Add(_sidebar);
        }

        private void BuildContent()
        {
            _content = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
            Controls.Add(_content);
            _content.BringToFront();
        }

        private void AddPage(string name, Control page)
        {
            page.Dock = DockStyle.Fill;
            page.Visible = false;
            _content.Controls.Add(page);
            _pages[name] = page;
            AddNav(name);
        }

        private void AddFormPage(string name, Form f)
        {
            var host = new Panel { Dock = DockStyle.Fill, Visible = false, AutoScroll = true, BackColor = Color.White };
            f.TopLevel = false;
            f.FormBorderStyle = FormBorderStyle.None;
            f.Dock = DockStyle.Fill;
            host.Controls.Add(f);
            _content.Controls.Add(host);
            _pages[name] = host;
            f.Show();
            AddNav(name);
        }

        private void AddNav(string name)
        {
            var nav = new NavButton(name) { Dock = DockStyle.None, Width = 150, Height = 36, Margin = new Padding(2, 2, 2, 0) };
            nav.Click += (s, e) => ShowPage(name);
            _nav.Controls.Add(nav);
            _navButtons.Add(nav);
        }

        private void ShowPage(string name)
        {
            foreach (var kv in _pages) kv.Value.Visible = (kv.Key == name);
            foreach (var nav in _navButtons) nav.SetActive(nav.Text.Trim() == name);
        }

        private void SetStatus(string s) { if (_status != null) _status.Text = s; }

        // ---------------- 4rVivi-only pages ----------------
        private Control BuildScannerPage()
        {
            var p = new Panel { BackColor = Color.White };
            p.Controls.Add(new Label { Text = "Memory Scanner", Top = 12, Left = 12, AutoSize = true, Font = new Font("Segoe UI", 14f, FontStyle.Bold) });
            p.Controls.Add(new Label { Text = "Find your HP/SP memory address (ArtMoney-style) if your server's offset is unknown.", Top = 48, Left = 12, AutoSize = true });
            var b = new Button { Text = "Open Scanner", Left = 12, Top = 84, Width = 160, Height = 32 };
            b.Click += (s, e) => { using (var f = new ScannerForm()) f.ShowDialog(this); };
            p.Controls.Add(b);
            return p;
        }

        private Control BuildMacrosPage()
        {
            var p = new Panel { BackColor = Color.White, AutoScroll = true };
            p.Controls.Add(new Label { Text = "Macros & auto-reconnect", Top = 12, Left = 12, AutoSize = true, Font = new Font("Segoe UI", 14f, FontStyle.Bold) });
            p.Controls.Add(new Label { Text = "Username", Top = 56, Left = 12, AutoSize = true });
            var user = new TextBox { Left = 110, Top = 52, Width = 200 };
            p.Controls.Add(user);
            p.Controls.Add(new Label { Text = "Password", Top = 88, Left = 12, AutoSize = true });
            var pass = new TextBox { Left = 110, Top = 84, Width = 200, UseSystemPasswordChar = true };
            p.Controls.Add(pass);
            p.Controls.Add(new Label { Text = "(stored DPAPI-encrypted, never plaintext)", Top = 88, Left = 320, AutoSize = true, ForeColor = Color.Gray });
            var saveCreds = new Button { Text = "Save login", Left = 110, Top = 116, Width = 110, Height = 28 };
            saveCreds.Click += (s, e) => { _creds.Username = user.Text; _creds.SetPassword(pass.Text); _creds.Save("login.dat"); SetStatus("Login saved (encrypted)."); };
            p.Controls.Add(saveCreds);

            var rec = new Button { Text = "Record", Left = 12, Top = 166, Width = 90, Height = 30 };
            var stop = new Button { Text = "Stop & save", Left = 108, Top = 166, Width = 110, Height = 30, Enabled = false };
            var play = new Button { Text = "Test play", Left = 224, Top = 166, Width = 100, Height = 30, Enabled = false };
            rec.Click += (s, e) => { _recorder.Start("login"); rec.Enabled = false; stop.Enabled = true; SetStatus("Recording… do your login, then Stop & save."); };
            stop.Click += (s, e) => { _lastRecording = _recorder.Stop(); _lastRecording.Save("login_macro.json"); rec.Enabled = true; stop.Enabled = false; play.Enabled = true; SetStatus("Saved login_macro.json (" + _lastRecording.Events.Count + " events)."); };
            play.Click += (s, e) => { if (_lastRecording != null) System.Threading.Tasks.Task.Run(() => _player.Play(_lastRecording, () => _creds.Username, () => _creds.GetPassword())); };
            p.Controls.AddRange(new Control[] { rec, stop, play });
            p.Controls.Add(new Label { Text = "Reconnect / auto-sell / auto-storage = recordings bound to a condition (see TriggeredMacros.cs).", Top = 210, Left = 12, AutoSize = true, ForeColor = Color.Gray });
            return p;
        }

        private Control BuildDatabasePage()
        {
            var p = new Panel { BackColor = Color.White };
            p.Controls.Add(new Label { Text = "Game database (rAthena)", Top = 12, Left = 12, AutoSize = true, Font = new Font("Segoe UI", 14f, FontStyle.Bold) });
            var kind = new ComboBox { Left = 12, Top = 50, Width = 110, DropDownStyle = ComboBoxStyle.DropDownList };
            kind.Items.AddRange(new object[] { "Mobs", "Skills", "Items", "Maps" });
            kind.SelectedIndex = 0;
            var query = new TextBox { Left = 128, Top = 50, Width = 250 };
            var go = new Button { Text = "Search", Left = 384, Top = 49, Width = 90, Height = 26 };
            var list = new ListView { Left = 12, Top = 86, Width = 740, Height = 420, View = View.Details, FullRowSelect = true, GridLines = true, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom };
            list.Columns.Add("ID", 70); list.Columns.Add("Name", 240); list.Columns.Add("Info", 410);
            p.Controls.AddRange(new Control[] { kind, query, go, list });
            if (_data == null)
            {
                p.Controls.Add(new Label { Text = "gamedata.json not found next to the exe.", Top = 60, Left = 490, AutoSize = true, ForeColor = Color.Red });
                return p;
            }
            go.Click += (s, e) =>
            {
                list.Items.Clear();
                string q = query.Text.Trim(); if (q.Length == 0) return;
                switch (kind.SelectedItem.ToString())
                {
                    case "Mobs": foreach (var m in _data.SearchMobs(q)) Row(list, m.Id, m.Name, "Lv" + m.Level + " | HP " + m.Hp + " | " + m.Race + "/" + m.Element + " | EXP " + m.BaseExp); break;
                    case "Skills": foreach (var sk in _data.SearchSkills(q)) Row(list, sk.Id, sk.Name, "cast " + sk.CastTimeMs + "ms | delay " + sk.AfterCastDelayMs + "ms | cd " + sk.CooldownMs + "ms"); break;
                    case "Items": foreach (var it in _data.SearchItems(q)) Row(list, it.Id, it.Name, it.Type + " | slots " + it.Slots + " | wt " + it.Weight); break;
                    case "Maps": foreach (var mp in _data.SearchMaps(q)) Row(list, 0, mp, ""); break;
                }
                SetStatus(list.Items.Count + " result(s).");
            };
            return p;
        }

        private static void Row(ListView lv, int id, string name, string info)
        {
            var it = new ListViewItem(id == 0 ? "" : id.ToString());
            it.SubItems.Add(name ?? ""); it.SubItems.Add(info ?? "");
            lv.Items.Add(it);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try { if (_recorder.Recording) _recorder.Stop(); } catch { }
            try { _data?.Dispose(); } catch { }
            base.OnFormClosed(e);
        }
    }
}
