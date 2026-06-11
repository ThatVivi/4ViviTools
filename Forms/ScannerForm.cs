// ScannerForm.cs — ArtMoney-style 3-step scan wizard for 4rVivi.
// WinForms, .NET Framework 4.x, x86. Wires the UI to Utils/MemoryEngine.cs.
// Drop into 4rVivi/Forms/. Pure code (no .Designer) so it drops in cleanly.
//
// Flow:
//   1) Pick the RO process.
//   2) Type current HP -> First Scan.
//   3) Take damage, type new HP -> Next Scan. Repeat until 1 candidate.
//   4) Use Filters (Decreased / Increased / Changed) when you don't know exact value.
//   5) "Use as HP" / "Use as SP" saves the dynamic address (and tries to derive a
//      static pointer chain) back to the active profile.
//
// MIT. Private-server use only.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using _4rVivi.Utils;   // <-- after rename; was _4RTools.Utils

namespace _4rVivi.Forms
{
    public class ScannerForm : Form
    {
        private readonly MemoryEngine _engine = new MemoryEngine();
        private ScanSession _session;

        private ComboBox cmbProcess;
        private ComboBox cmbType;
        private TextBox txtValue;
        private Button btnRefresh, btnFirst, btnNext, btnReset, btnUseHp, btnUseSp;
        private Button btnFltDec, btnFltInc, btnFltChg, btnFltUnchg;
        private ListView lvResults;
        private Label lblStatus;

        public ScannerForm()
        {
            BuildUi();
            RefreshProcesses();
        }

        // expose the discovered address so the main form can persist it
        public IntPtr DiscoveredAddress { get; private set; } = IntPtr.Zero;
        public string DiscoveredRole { get; private set; }   // "HP" or "SP"
        public event EventHandler<MemoryTargetFound> TargetFound;

        private void BuildUi()
        {
            Text = "4rVivi — Memory Scanner";
            Width = 560; Height = 540;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;

            cmbProcess = new ComboBox { Left = 12, Top = 12, Width = 360, DropDownStyle = ComboBoxStyle.DropDownList };
            btnRefresh = new Button { Left = 380, Top = 11, Width = 150, Text = "Refresh processes" };
            btnRefresh.Click += (s, e) => RefreshProcesses();

            var lblVal = new Label { Left = 12, Top = 48, Width = 90, Text = "Value:", TextAlign = ContentAlignment.MiddleLeft };
            txtValue = new TextBox { Left = 100, Top = 45, Width = 150 };
            cmbType = new ComboBox { Left = 260, Top = 45, Width = 110, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbType.Items.AddRange(new object[] { "Int32", "Int16", "Float" });
            cmbType.SelectedIndex = 0;

            btnFirst = new Button { Left = 380, Top = 44, Width = 70, Text = "First" };
            btnNext = new Button { Left = 455, Top = 44, Width = 75, Text = "Next", Enabled = false };
            btnFirst.Click += FirstScan;
            btnNext.Click += (s, e) => NextScan(ScanFilter.Exact);

            // change-based filters (when exact value unknown)
            btnFltDec = new Button { Left = 12, Top = 78, Width = 90, Text = "Decreased", Enabled = false };
            btnFltInc = new Button { Left = 106, Top = 78, Width = 90, Text = "Increased", Enabled = false };
            btnFltChg = new Button { Left = 200, Top = 78, Width = 90, Text = "Changed", Enabled = false };
            btnFltUnchg = new Button { Left = 294, Top = 78, Width = 90, Text = "Unchanged", Enabled = false };
            btnFltDec.Click += (s, e) => NextScan(ScanFilter.Decreased);
            btnFltInc.Click += (s, e) => NextScan(ScanFilter.Increased);
            btnFltChg.Click += (s, e) => NextScan(ScanFilter.Changed);
            btnFltUnchg.Click += (s, e) => NextScan(ScanFilter.Unchanged);

            btnReset = new Button { Left = 440, Top = 78, Width = 90, Text = "New scan" };
            btnReset.Click += (s, e) => ResetScan();

            lvResults = new ListView
            {
                Left = 12, Top = 110, Width = 518, Height = 330,
                View = View.Details, FullRowSelect = true, GridLines = true
            };
            lvResults.Columns.Add("Address", 200);
            lvResults.Columns.Add("Value", 290);

            btnUseHp = new Button { Left = 12, Top = 450, Width = 180, Height = 34, Text = "Use selected as HP" };
            btnUseSp = new Button { Left = 200, Top = 450, Width = 180, Height = 34, Text = "Use selected as SP" };
            btnUseHp.Click += (s, e) => UseSelected("HP");
            btnUseSp.Click += (s, e) => UseSelected("SP");

            lblStatus = new Label { Left = 12, Top = 492, Width = 518, Text = "Pick a process and start a scan." };

            Controls.AddRange(new Control[]
            {
                cmbProcess, btnRefresh, lblVal, txtValue, cmbType, btnFirst, btnNext,
                btnFltDec, btnFltInc, btnFltChg, btnFltUnchg, btnReset,
                lvResults, btnUseHp, btnUseSp, lblStatus
            });
        }

        private void RefreshProcesses()
        {
            cmbProcess.Items.Clear();
            // RO clients vary in name; show everything with a visible window + reasonable size.
            foreach (var p in Process.GetProcesses()
                         .Where(p => p.MainWindowHandle != IntPtr.Zero && !string.IsNullOrEmpty(p.MainWindowTitle))
                         .OrderBy(p => p.ProcessName))
            {
                cmbProcess.Items.Add(new ProcItem(p));
            }
            if (cmbProcess.Items.Count > 0) cmbProcess.SelectedIndex = 0;
        }

        private ScanType SelectedType()
        {
            switch (cmbType.SelectedItem.ToString())
            {
                case "Int16": return ScanType.Int16;
                case "Float": return ScanType.Float;
                default: return ScanType.Int32;
            }
        }

        private bool EnsureAttached()
        {
            if (!(cmbProcess.SelectedItem is ProcItem pi)) { Status("Select a process first."); return false; }
            if (_engine.Target == null || _engine.Target.Id != pi.Process.Id)
            {
                if (!_engine.Attach(pi.Process)) { Status("Attach failed — run 4rVivi as Administrator."); return false; }
            }
            return true;
        }

        private object ParseValue()
        {
            var t = SelectedType();
            var txt = txtValue.Text.Trim();
            if (t == ScanType.Float) return float.Parse(txt, CultureInfo.InvariantCulture);
            if (t == ScanType.Int16) return short.Parse(txt);
            return int.Parse(txt);
        }

        private void FirstScan(object sender, EventArgs e)
        {
            if (!EnsureAttached()) return;
            try
            {
                Status("Scanning…");
                Application.DoEvents();
                _session = _engine.FirstScan(SelectedType(), ParseValue());
                ShowResults();
                EnableRefineButtons(true);
                Status($"First scan: {_session.Count} candidates. Change HP in-game, type new value, click Next (or use a filter).");
            }
            catch (FormatException) { Status("Enter a valid number for the selected type."); }
        }

        private void NextScan(ScanFilter filter)
        {
            if (_session == null) { Status("Do a First scan first."); return; }
            try
            {
                object exact = filter == ScanFilter.Exact ? ParseValue() : null;
                _session.NextScan(filter, exact);
                ShowResults();
                Status($"{filter}: {_session.Count} candidates left.");
            }
            catch (FormatException) { Status("Enter a valid number for Next (exact)."); }
        }

        private void ResetScan()
        {
            _session = null;
            lvResults.Items.Clear();
            EnableRefineButtons(false);
            Status("Scan reset. Start a new First scan.");
        }

        private void EnableRefineButtons(bool on)
        {
            btnNext.Enabled = on; btnFltDec.Enabled = on; btnFltInc.Enabled = on;
            btnFltChg.Enabled = on; btnFltUnchg.Enabled = on;
        }

        private void ShowResults()
        {
            lvResults.BeginUpdate();
            lvResults.Items.Clear();
            // cap display to keep the UI snappy; full set stays in the session
            foreach (var r in _session.Results.Take(2000))
            {
                var it = new ListViewItem("0x" + ((long)r.Address).ToString("X8"));
                it.SubItems.Add(Convert.ToString(r.Value, CultureInfo.InvariantCulture));
                it.Tag = r.Address;
                lvResults.Items.Add(it);
            }
            lvResults.EndUpdate();
        }

        private void UseSelected(string role)
        {
            if (lvResults.SelectedItems.Count == 0) { Status("Select an address row first."); return; }
            var addr = (IntPtr)lvResults.SelectedItems[0].Tag;
            DiscoveredAddress = addr;
            DiscoveredRole = role;

            // Best-effort: express the address as module+offset if it lives in the main module,
            // which is far more stable than a raw runtime address across restarts.
            int? staticOffset = null;
            if (_engine.ModuleBase != IntPtr.Zero && _engine.ModuleSize > 0)
            {
                long delta = (long)addr - (long)_engine.ModuleBase;
                if (delta >= 0 && delta < _engine.ModuleSize) staticOffset = (int)delta;
            }

            TargetFound?.Invoke(this, new MemoryTargetFound
            {
                Role = role,
                Address = addr,
                ModuleBase = _engine.ModuleBase,
                StaticModuleOffset = staticOffset,
                ProcessName = _engine.Target?.ProcessName
            });

            Status(staticOffset.HasValue
                ? $"{role} saved as module+0x{staticOffset.Value:X} (stable). Closing."
                : $"{role} saved as runtime 0x{((long)addr):X8}. Note: derive a pointer chain for stability.");
            DialogResult = DialogResult.OK;
            Close();
        }

        private void Status(string s) => lblStatus.Text = s;

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _engine.Dispose();
            base.OnFormClosed(e);
        }

        private sealed class ProcItem
        {
            public readonly Process Process;
            public ProcItem(Process p) { Process = p; }
            public override string ToString() => $"{Process.ProcessName}.exe  (PID {Process.Id})  — {Truncate(Process.MainWindowTitle, 28)}";
            private static string Truncate(string s, int n) => string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s.Substring(0, n) + "…");
        }
    }

    public class MemoryTargetFound : EventArgs
    {
        public string Role;                 // "HP" / "SP"
        public IntPtr Address;              // runtime address
        public IntPtr ModuleBase;
        public int? StaticModuleOffset;     // module-relative offset if inside main module
        public string ProcessName;
    }
}
