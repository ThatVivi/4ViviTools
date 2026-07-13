using System.Drawing;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace VisionGrfPicker;

public sealed class MainForm : Form
{
    // dark black + soft crimson theme (matches 4rVivi)
    static readonly Color Bg = Color.FromArgb(16, 16, 18);
    static readonly Color Panel = Color.FromArgb(27, 27, 31);
    static readonly Color ListBg = Color.FromArgb(30, 30, 35);
    static readonly Color Fg = Color.FromArgb(228, 228, 231);
    static readonly Color Sub = Color.FromArgb(150, 150, 156);
    static readonly Color Red = Color.FromArgb(198, 64, 74);        // dimmer, pleasant crimson
    static readonly Color RedHover = Color.FromArgb(214, 82, 92);
    static readonly Color RedSel = Color.FromArgb(74, 40, 46);      // subtle row highlight
    static readonly Color BtnDark = Color.FromArgb(36, 36, 42);
    static readonly Color BtnBorder = Color.FromArgb(60, 60, 68);
    private readonly ToolTip _tip = new() { AutoPopDelay = 20000, InitialDelay = 300, ReshowDelay = 100, ShowAlways = true };

    private readonly string _appDir = AppContext.BaseDirectory;
    private readonly string _cfgPath;
    private Catalog _catalog = new();
    private List<int> _allIds = new();
    private readonly List<int> _left = new();     // available (in library, not promoted)
    private readonly List<int> _right = new();    // active (present in live 몬스터\)
    private List<int> _leftShown = new();
    private List<int> _rightShown = new();

    private readonly TextBox _src = new();
    private readonly TextBox _leftSearch = new();
    private readonly TextBox _rightSearch = new();
    private readonly ListBox _leftList = new();
    private readonly ListBox _rightList = new();
    private readonly Button _apply = new() { Text = "APPLY  →  build GRF", Width = 200, Height = 34 };
    private readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 24, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 0, 0) };

    public MainForm()
    {
        _cfgPath = Path.Combine(_appDir, "picker_config.json");
        Text = "4ViviTools — Vision Assist Monster Picker";
        Width = 960; Height = 640; StartPosition = FormStartPosition.CenterScreen;
        BackColor = Bg; ForeColor = Fg; Font = new Font("Segoe UI", 9f);
        BuildUi();
        LoadConfig();
        TryLoadCatalog();
    }

    // ---------- styling helpers ----------
    Label Lbl(string t, bool sub = false) => new() { Text = t, AutoSize = true, ForeColor = sub ? Sub : Fg, Padding = new Padding(2, 4, 2, 2) };
    TextBox Tb(TextBox b, string ph = "") { b.BackColor = ListBg; b.ForeColor = Fg; b.BorderStyle = BorderStyle.FixedSingle; b.PlaceholderText = ph; return b; }
    Button Btn(string t, EventHandler onClick, bool primary = false, string tip = "")
    {
        var b = new Button { Text = t, AutoSize = true, FlatStyle = FlatStyle.Flat, ForeColor = Fg, BackColor = primary ? Red : BtnDark, Padding = new Padding(10, 5, 10, 5), Cursor = Cursors.Hand };
        b.FlatAppearance.BorderColor = primary ? Red : BtnBorder; b.FlatAppearance.BorderSize = 1;
        b.FlatAppearance.MouseOverBackColor = primary ? RedHover : Color.FromArgb(48, 48, 56);
        b.Click += onClick;
        if (tip.Length > 0) _tip.SetToolTip(b, tip);
        return b;
    }
    void StyleList(ListBox lb, EventHandler dbl)
    {
        // plain dark list (no owner-draw) — reliable item rendering
        lb.BackColor = ListBg; lb.ForeColor = Fg; lb.BorderStyle = BorderStyle.FixedSingle;
        lb.SelectionMode = SelectionMode.MultiExtended; lb.IntegralHeight = false;
        lb.DoubleClick += dbl;
    }

    private void BuildUi()
    {
        var top = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, AutoSize = true, Padding = new Padding(10, 8, 10, 4), BackColor = Bg };
        top.Controls.Add(Lbl("GRF  (changes are saved back into this file):", true), 0, 0);
        top.SetColumnSpan(top.GetControlFromPosition(0, 0)!, 4);
        Tb(_src, "path to your VisionAssist library .grf"); _src.Width = 620;
        top.Controls.Add(_src, 0, 1);
        top.Controls.Add(Btn("Browse", (_, _) => Pick(_src, false), false, "Choose the Vision Assist GRF to edit."), 1, 1);
        top.Controls.Add(Btn("Load", (_, _) => LoadGrf(), false, "Read the GRF and sort monsters: Active (already boxed) on the right, Available on the left."), 2, 1);
        _tip.SetToolTip(_src, "Path to your Vision Assist library GRF. Apply saves changes back into this same file.");
        var hint = Lbl("Flow:  Build Library (once)  →  Load  →  move monsters to Active  →  Apply  →  restart client", true);
        top.Controls.Add(hint, 0, 2); top.SetColumnSpan(hint, 4);

        var mid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Padding = new Padding(10), BackColor = Bg };
        mid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        mid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        mid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        mid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        mid.Controls.Add(MakePane("Available  (in library)", _leftSearch, _leftList, RefreshLeft, MoveRight), 0, 0);

        var btns = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, BackColor = Bg };
        btns.Controls.Add(new Label { Height = 60 });
        var addB = Btn("Add  →", (_, _) => MoveRight(), true, "Make the selected monsters Active — they'll get the red box + real name in game."); addB.Width = 110; addB.Height = 34;
        var remB = Btn("←  Remove", (_, _) => MoveLeft(), false, "Move the selected monsters back to Available — removes their box in game."); remB.Width = 110; remB.Height = 34;
        var allB = Btn("Add all →", (_, _) => { foreach (var id in _leftShown.ToList()) Promote(id); RefreshBoth(); }, false, "Make every monster in the Available list Active.");
        var clrB = Btn("Clear ←", (_, _) => { foreach (var id in _right.ToList()) Demote(id); RefreshBoth(); }, false, "Remove the box from ALL monsters (Active becomes empty).");
        foreach (var b in new[] { addB, remB, allB, clrB }) { b.Margin = new Padding(6); btns.Controls.Add(b); }
        mid.Controls.Add(btns, 1, 0);

        mid.Controls.Add(MakePane("Active  (boxed in game)", _rightSearch, _rightList, RefreshRight, MoveLeft), 2, 0);
        _tip.SetToolTip(_leftSearch, "Filter the Available list by name or #id.");
        _tip.SetToolTip(_rightSearch, "Filter the Active list by name or #id.");
        _tip.SetToolTip(_leftList, "Available monsters (baked in the library, not shown boxed). Double-click to make one Active.");
        _tip.SetToolTip(_rightList, "Active monsters — these show the red box + name in game. Double-click to remove.");

        var bot = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(10), BackColor = Bg };
        bot.Controls.Add(Btn("Build Library (all → visionassistant)…", (_, _) => BuildLibrary(), false,
            "ONE-TIME setup: bake every monster into the visionassistant folder (몬스터 stays empty). Pick your clean client data.grf as the source. Produces the library GRF you then Load here."));
        _apply.FlatStyle = FlatStyle.Flat; _apply.BackColor = Red; _apply.ForeColor = Fg; _apply.Cursor = Cursors.Hand;
        _apply.FlatAppearance.BorderColor = Red; _apply.FlatAppearance.MouseOverBackColor = RedHover;
        _apply.Click += (_, _) => Apply();
        _tip.SetToolTip(_apply, "Save your Active picks into the GRF (in place). Then restart the RO client to see the boxes.");
        bot.Controls.Add(_apply);
        _status.Dock = DockStyle.Fill; _status.BackColor = Color.FromArgb(12, 12, 14); _status.ForeColor = Sub; _status.Text = "Load a GRF.";

        // one deterministic root grid: top(auto) / middle(fills) / buttons(auto) / status(fixed) — no docking overlap
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = Bg };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        root.Controls.Add(top, 0, 0);
        root.Controls.Add(mid, 0, 1);
        root.Controls.Add(bot, 0, 2);
        root.Controls.Add(_status, 0, 3);
        Controls.Add(root);
    }

    Control MakePane(string title, TextBox search, ListBox list, Action refresh, Action move)
    {
        StyleList(list, (_, _) => move());
        var hdr = new Label { Text = title, Dock = DockStyle.Fill, ForeColor = Red, Font = new Font("Segoe UI", 10f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(4, 0, 0, 0) };
        Tb(search, "search…"); search.Dock = DockStyle.Fill; search.TextChanged += (_, _) => refresh();
        list.Dock = DockStyle.Fill;
        // fixed pixel rows -> header + search always visible, list fills the rest (no collapse)
        var p = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Panel, Padding = new Padding(6) };
        p.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        p.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        p.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        p.Controls.Add(hdr, 0, 0);
        p.Controls.Add(search, 0, 1);
        p.Controls.Add(list, 0, 2);
        return p;
    }

    private void Pick(TextBox box, bool save)
    {
        if (save) { using var d = new SaveFileDialog { Filter = "GRF|*.grf", FileName = "VisionAssist.grf" }; if (d.ShowDialog() == DialogResult.OK) box.Text = d.FileName; }
        else { using var d = new OpenFileDialog { Filter = "GRF|*.grf|All|*.*" }; if (d.ShowDialog() == DialogResult.OK) box.Text = d.FileName; }
    }

    private void TryLoadCatalog()
    {
        try
        {
            _catalog = Catalog.Load(_appDir);
            _allIds = _catalog.Sprites.Keys.Where(id => _catalog.Names.ContainsKey(id)).ToList();
            _left.Clear(); _left.AddRange(_allIds.OrderBy(Label));
            RefreshBoth();
            _status.Text = $"{_allIds.Count} monsters. Load a GRF to see which are already active.";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Load failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private string Label(int id) => $"{(_catalog.Names.TryGetValue(id, out var n) ? n : "mob_" + id)}  (#{id})";

    // ---------- read the GRF: split into active (live 몬스터\) vs available (library) ----------
    private void LoadGrf()
    {
        if (string.IsNullOrWhiteSpace(_src.Text) || !File.Exists(_src.Text)) { MessageBox.Show(this, "Pick a valid source GRF."); return; }
        try
        {
            var grf = GrfArchive.Open(_src.Text);
            bool hasLib = _allIds.Any(id => grf.Has(MarkerPaths.ToLib(_catalog.Sprites[id])));
            _left.Clear(); _right.Clear();
            foreach (var id in _allIds.OrderBy(Label))
            {
                string spr = _catalog.Sprites[id];
                if (grf.Has(spr)) _right.Add(id);                             // already in live 몬스터\
                else if (!hasLib || grf.Has(MarkerPaths.ToLib(spr))) _left.Add(id);
            }
            RefreshBoth();
            _status.Text = $"Loaded: {_right.Count} active · {_left.Count} available" + (hasLib ? " (library GRF)" : "");
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Load failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void RefreshBoth() { RefreshLeft(); RefreshRight(); }
    private void RefreshLeft() => Fill(_leftList, _left, _leftSearch.Text, out _leftShown);
    private void RefreshRight() => Fill(_rightList, _right, _rightSearch.Text, out _rightShown);
    private void Fill(ListBox lb, List<int> ids, string q, out List<int> shown)
    {
        q = q.Trim().ToLowerInvariant(); shown = new();
        lb.BeginUpdate(); lb.Items.Clear();
        foreach (var id in ids)
        {
            string label = Label(id);
            if (q.Length > 0 && !label.ToLowerInvariant().Contains(q)) continue;
            lb.Items.Add(label); shown.Add(id);
        }
        lb.EndUpdate();
        lb.Invalidate();     // owner-draw lists don't always repaint on Items change
    }

    private void Promote(int id) { if (_left.Remove(id) && !_right.Contains(id)) _right.Add(id); }
    private void Demote(int id) { if (_right.Remove(id) && !_left.Contains(id)) _left.Add(id); }

    private void MoveRight()
    {
        foreach (int i in _leftList.SelectedIndices) if (i >= 0 && i < _leftShown.Count) Promote(_leftShown[i]);
        _right.Sort((a, b) => string.Compare(Label(a), Label(b), StringComparison.OrdinalIgnoreCase));
        RefreshBoth(); _status.Text = $"{_right.Count} active.";
    }
    private void MoveLeft()
    {
        foreach (int i in _rightList.SelectedIndices) if (i >= 0 && i < _rightShown.Count) Demote(_rightShown[i]);
        _left.Sort((a, b) => string.Compare(Label(a), Label(b), StringComparison.OrdinalIgnoreCase));
        RefreshBoth(); _status.Text = $"{_right.Count} active.";
    }

    // ---------- config ----------
    private void LoadConfig()
    {
        try
        {
            if (!File.Exists(_cfgPath)) return;
            using var doc = JsonDocument.Parse(File.ReadAllText(_cfgPath));
            if (doc.RootElement.TryGetProperty("source", out var s)) _src.Text = s.GetString() ?? "";
        }
        catch { }
    }
    private void SaveConfig()
    {
        try { File.WriteAllText(_cfgPath, JsonSerializer.Serialize(new { source = _src.Text }, new JsonSerializerOptions { WriteIndented = true })); }
        catch { }
    }

    // ---------- apply (promote the Active list) ----------
    private void Apply()
    {
        if (string.IsNullOrWhiteSpace(_src.Text) || !File.Exists(_src.Text)) { MessageBox.Show(this, "Load a GRF first."); return; }
        _apply.Enabled = false; SaveConfig();
        Task.Run(ApplyWorker);   // Active list may be empty -> that just clears all boxes (valid)
    }

    private void ApplyWorker()
    {
        try
        {
            var grf = GrfArchive.Open(_src.Text);
            var entries = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in grf.Names) if (MarkerPaths.IsLib(p)) entries[p] = grf.Read(p);   // carry library

            var mobs = new Dictionary<string, object>();
            int done = 0, total = _right.Count;
            foreach (int id in _right.ToList())
            {
                if (!_catalog.Sprites.TryGetValue(id, out var spr)) continue;
                string name = _catalog.Names.TryGetValue(id, out var nm) ? nm : $"mob_{id}";
                string libSpr = MarkerPaths.ToLib(spr);
                byte[]? baked = grf.Has(libSpr) ? grf.Read(libSpr) : grf.Has(spr) ? grf.Read(spr) : null;
                if (baked == null) { SetStatus($"skip {name}: not in library"); continue; }
                entries[spr] = baked;
                string act = spr.EndsWith(".spr", StringComparison.OrdinalIgnoreCase) ? spr[..^4] + ".act" : spr + ".act";
                string libAct = MarkerPaths.ToLib(act);
                if (grf.Has(libAct)) entries[act] = grf.Read(libAct); else if (grf.Has(act)) entries[act] = grf.Read(act);
                mobs[id.ToString()] = new { name, sprite = spr, code = Marker.ColorCode(id) };
                done++; SetStatus($"Promoting {done}/{total}: {name}");
            }
            if (entries.Count == 0) throw new Exception("empty GRF — build a library first.");
            GrfArchive.Write(entries, _src.Text);      // save back into the same file (in place)
            WriteManifest(mobs);
            Done(_src.Text, Path.Combine(Path.GetDirectoryName(_src.Text)!, "VisionAssist.manifest.json"), done);
        }
        catch (Exception ex) { Error(ex.Message); }
    }

    private void BuildLibrary()
    {
        string clean; using (var d = new OpenFileDialog { Title = "Clean source GRF (your client data.grf)", Filter = "GRF|*.grf|All|*.*" }) { if (d.ShowDialog() != DialogResult.OK) return; clean = d.FileName; }
        string libOut; using (var d = new SaveFileDialog { Title = "Save library GRF", Filter = "GRF|*.grf", FileName = "VisionAssistLibrary.grf" }) { if (d.ShowDialog() != DialogResult.OK) return; libOut = d.FileName; }
        _apply.Enabled = false;
        Task.Run(() => BuildLibraryWorker(clean, libOut));
    }

    private void BuildLibraryWorker(string cleanSrc, string libOut)
    {
        try
        {
            var src = GrfArchive.Open(cleanSrc);
            var entries = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            int done = 0, total = _allIds.Count;
            foreach (int id in _allIds)
            {
                if (!_catalog.Sprites.TryGetValue(id, out var spr) || !src.Has(spr)) continue;
                string name = _catalog.Names.TryGetValue(id, out var nm) ? nm : $"mob_{id}";
                try
                {
                    byte[] rawSpr = src.Read(spr);
                    var dec = Spr.DecodeIndexed(rawSpr);
                    byte[] baked;
                    if (dec != null)
                    {
                        var (bframes, npal) = Baker.BakeIndexed(dec.Value.frames, dec.Value.palette, id, name);
                        baked = Spr.EncodeIndexed(bframes, npal);          // indexed -> exact colors + small
                    }
                    else
                    {
                        var frames = Spr.Decode(rawSpr);                    // truecolor original (rare)
                        baked = Spr.Encode(frames.Select(f => Baker.Bake(f, id, name)).ToList());
                    }
                    entries[MarkerPaths.ToLib(spr)] = baked;
                    string act = spr.EndsWith(".spr", StringComparison.OrdinalIgnoreCase) ? spr[..^4] + ".act" : spr + ".act";
                    if (src.Has(act)) entries[MarkerPaths.ToLib(act)] = src.Read(act);
                    done++; if (done % 50 == 0) SetStatus($"Baking library {done}/{total}: {name}");
                }
                catch (Exception ex) { SetStatus($"skip {name}: {ex.Message}"); }
            }
            if (entries.Count == 0) throw new Exception("nothing baked (does the source GRF contain monster sprites?)");
            GrfArchive.Write(entries, libOut);
            if (IsHandleCreated) BeginInvoke(() =>
            {
                _apply.Enabled = true;
                _src.Text = libOut;
                MessageBox.Show(this, $"Library built: {libOut}\n\n{done} monsters baked into visionassistant\\ (몬스터\\ empty).\n\nClick Load to start picking.", "Library ready", MessageBoxButtons.OK, MessageBoxIcon.Information);
            });
        }
        catch (Exception ex) { Error(ex.Message); }
    }

    private void WriteManifest(Dictionary<string, object> mobs)
    {
        var manifest = new { version = 1, codeCells = Marker.CodeCells, codeCell = Marker.CodeCell, boxPx = Marker.BoxPx, boxColor = new[] { 255, 0, 0 }, mobs };
        string manPath = Path.Combine(Path.GetDirectoryName(_src.Text)!, "VisionAssist.manifest.json");
        File.WriteAllText(manPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    }

    private void SetStatus(string msg) { if (IsHandleCreated) BeginInvoke(() => _status.Text = msg); }
    private void Error(string msg) { if (IsHandleCreated) BeginInvoke(() => { _apply.Enabled = true; MessageBox.Show(this, msg, "Failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }); }
    private void Done(string grf, string man, int n)
    {
        if (!IsHandleCreated) return;
        BeginInvoke(() =>
        {
            _apply.Enabled = true;
            _status.Text = $"Saved: {n} active → {Path.GetFileName(grf)}";
            MessageBox.Show(this,
                $"Saved into:\n{grf}\n{man}\n\n{n} monsters active (verify OK).\n\n" +
                "If this GRF is already in your client (DATA.INI 0=…), just RESTART the client to see the change.\n" +
                "In 4ViviTools enable Vision Assist GRF. No manifest path is needed.",
                "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
        });
    }
}
