using System.Text.Json;

namespace VRRadialOverlayEditor;

// A guided first-run tour. A coach card explains each step and an orange frame marks what to look at
// or click. "Do it" steps wait until the user has done the thing (added a binding, a wheel, ...).
// Everything made during the tour can be thrown away at the end (the profile is snapshotted first).
public partial class MainForm
{
    sealed class TourStep
    {
        public string Title, Text;
        public Func<Rectangle?> Target;   // form coordinates; null = no frame
        public Func<bool> Done;           // null = plain "Next" step
        public Action Enter;
    }

    List<TourStep> tour;
    int tourAt;
    Panel coach; Label coachTitle, coachText, coachCount; Button coachNext, coachBack;
    readonly Panel[] frame = new Panel[4];
    System.Windows.Forms.Timer tourTimer;
    string tourSnapshot, tourFile; bool tourWasDirty;
    object tBinding, tWheel, tItem, tSub; int tCount;

    TreeNode OverviewNode() => tree.Nodes.Cast<TreeNode>().FirstOrDefault(n => "overview".Equals(n.Tag));

    static string TourFlag => Path.Combine(DataDir, "tutorial_seen");

    void OfferTutorialOnFirstRun()
    {
        if (File.Exists(TourFlag) || Environment.GetCommandLineArgs().Length > 1) return;
        void Seen() { try { Directory.CreateDirectory(DataDir); File.WriteAllText(TourFlag, DateTime.Now.ToString("s")); } catch { } }
        using var dlg = new Form { Text = "First time here?", FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false, ClientSize = new Size(520, 230), Font = new Font("Segoe UI", 10.5f) };
        dlg.Controls.Add(new Label { Text = "Welcome to VR Radial Overlay!\n\nTake a short guided tour? It shows how to add a binding, a wheel and a sub-wheel, and where the settings are. Anything you make during the tour can be thrown away at the end.\n\nYou can always start it later with the 'Tutorial' button at the top.", Location = new Point(18, 14), Size = new Size(484, 150) });
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 54, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10) };
        Button B(string t, DialogResult r, bool main = false)
        {
            var b = new Button { Text = t, AutoSize = true, Height = 34, DialogResult = r, FlatStyle = FlatStyle.Flat, Margin = new Padding(8, 0, 0, 0), Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                                 BackColor = main ? Color.FromArgb(240, 140, 30) : Color.White, ForeColor = main ? Color.White : Color.FromArgb(40, 50, 70) };
            bar.Controls.Add(b); return b;
        }
        dlg.AcceptButton = B("Start the tour", DialogResult.Yes, true);
        B("Not now", DialogResult.Cancel);
        B("Don't ask again", DialogResult.No);
        dlg.Controls.Add(bar);
        var res = dlg.ShowDialog(this);
        if (res == DialogResult.Yes) { Seen(); StartTutorial(); }
        else if (res == DialogResult.No) Seen();   // "Not now" asks again next launch
    }

    void StartTutorial()
    {
        if (Profile == null || coach != null) return;
        tourSnapshot = Serialize(); tourFile = currentFile; tourWasDirty = dirty;
        tBinding = tWheel = tItem = tSub = null;
        BuildTour();
        BuildCoach();
        tourAt = -1;
        tourTimer = new System.Windows.Forms.Timer { Interval = 200 };
        tourTimer.Tick += (s, e) => TourTick();
        tourTimer.Start();
        TourGo(0);
    }

    // ---------- steps ----------
    void BuildTour()
    {
        TreeNode Node(object tag) => tag == null ? null : Find(tree.Nodes, tag);
        TreeNode SlotUnder(TreeNode p) => p?.Nodes.Cast<TreeNode>().LastOrDefault(c => c.Tag is AddSlot);
        Rectangle? Row(TreeNode n, int part = 0) // 0 whole row, 1 left half of a slot, 2 right half, 3 on/off dot
        {
            if (n == null || n.Bounds.Height == 0) return null;
            Rectangle r = n.Tag is AddSlot ? SlotRect(n) : new Rectangle(4, n.Bounds.Y, tree.ClientSize.Width - 8, n.Bounds.Height);
            if (part == 1) r = new Rectangle(r.X, r.Y, r.Width / 2 - 4, r.Height);
            if (part == 2) r = new Rectangle(r.X + r.Width / 2 + 4, r.Y, r.Width - r.Width / 2 - 4, r.Height);
            if (part == 3) r = new Rectangle(tree.ClientSize.Width - 46, n.Bounds.Y + 4, 40, n.Bounds.Height - 8);
            if (r.Bottom < 0 || r.Y > tree.ClientSize.Height) return null;
            return RectangleToClient(tree.RectangleToScreen(r));
        }
        Rectangle? Ctl(Control c) => c == null ? null : RectangleToClient(c.Parent.RectangleToScreen(c.Bounds));
        void Show(TreeNode n) { if (n == null) return; n.Parent?.Expand(); n.EnsureVisible(); }
        void Select(TreeNode n) { if (n != null) { Show(n); tree.SelectedNode = n; } }
        TreeNode settingsNode() => tree.Nodes.Cast<TreeNode>().FirstOrDefault(x => x.Tag is ProfileModel);

        tour = new()
        {
            new() { Title = "Welcome!", Text = "This tour takes about two minutes. You'll add a controller binding, make a wheel with a sub-wheel, and see where the settings are.\n\nThe orange frame shows where to look. Steps marked 'Your turn' wait until you've done them.",
                    Enter = () => Select(OverviewNode()) },
            new() { Title = "The Overview", Text = "This page shows what is on and off right now: the wheels on each controller, the wrist panels and the VR keyboard. The switches work right here, and the blue buttons jump to the place where you edit each thing.",
                    Target = () => Ctl(editorHost), Enter = () => Select(OverviewNode()) },
            new() { Title = "The list", Text = "Everything in your profile is listed here, grouped by colour: the right and left controller, the wheels, and the two wrist panels.\n\nClick the arrow left of a name to fold or unfold it. The search box at the top finds anything by name, key or button.",
                    Target = () => Ctl(tree) },

            new() { Title = "Your turn: add a binding", Text = "A binding makes a controller button do something.\n\nClick '+ Add binding' under RIGHT CONTROLLER.",
                    Enter = () => { tCount = Profile.Bindings.Count; Show(SlotUnder(bindingRoots[1])); },
                    Target = () => Row(SlotUnder(bindingRoots[1])),
                    Done = () => { if (Profile.Bindings.Count <= tCount) return false; tBinding = tree.SelectedNode?.Tag as BindingModel ?? (object)Profile.Bindings.Last(); return true; } },
            new() { Title = "Setting up a binding", Text = "Pick the controller button and how you use it (press, hold, double-tap...), then 'What happens'.\n\nTo bind a keyboard key, click 'Record key' and press the key. You can try it now if you like.",
                    Target = () => Ctl(editorHost) },
            new() { Title = "On and off", Text = "The round dot at the right of a row switches it on or off without deleting it. Off rows are greyed out.\n\nRight-click any row to duplicate, delete or move it.",
                    Target = () => Row(Node(tBinding), 3) },

            new() { Title = "Your turn: make a wheel", Text = "A wheel is a radial menu you open in VR.\n\nClick '+ Add' on the orange WHEELS bar.",
                    Enter = () => { tCount = Profile.Wheels.Count; Show(wheelsRoot); },
                    Target = () => Row(wheelsRoot),
                    Done = () => { if (Profile.Wheels.Count <= tCount) return false; tWheel = tree.SelectedNode?.Tag as WheelModel ?? (object)Profile.Wheels.Values.Last(); return true; } },
            new() { Title = "Your wheel", Text = "Give it a name and a title here (the title is shown in the middle of the wheel). It's empty for now, so let's add something to it.",
                    Target = () => Ctl(editorHost) },
            new() { Title = "Your turn: add an item", Text = "Click '+ Add item' under your new wheel in the list.",
                    Enter = () => { if (tWheel is WheelModel w) { unfolded.Add(w); tCount = w.Items.Count; var n = Node(w); if (n != null) { folding = true; n.Expand(); folding = false; } Show(SlotUnder(n)); } },
                    Target = () => Row(SlotUnder(Node(tWheel)), 1),
                    Done = () => { if (tWheel is not WheelModel w || w.Items.Count <= tCount) return false; tItem = w.Items.Last(); return true; } },
            new() { Title = "Setting up an item", Text = "Each item has a label and 'What happens' when you pick it: press a key, run a macro, open the keyboard, open another wheel...\n\nTip: on a wheel's page, click a segment in the wheel picture to jump straight to that item and rebind it.",
                    Target = () => Ctl(editorHost) },

            new() { Title = "Your turn: add a sub-wheel", Text = "A sub-wheel is a second wheel that opens from an item on the first one, so you can fit more in.\n\nClick '+ Add sub-wheel' under your wheel.",
                    Enter = () => { if (tWheel is WheelModel w) { tCount = Profile.Wheels.Count; var n = Node(w); if (n != null) { folding = true; n.Expand(); folding = false; } Show(SlotUnder(n)); } },
                    Target = () => Row(SlotUnder(Node(tWheel)), 2),
                    Done = () => { if (Profile.Wheels.Count <= tCount) return false; tSub = tree.SelectedNode?.Tag as WheelModel; return true; } },
            new() { Title = "Linked automatically", Text = "Your wheel got a 'More' item that opens the new sub-wheel, and the sub-wheel starts with a 'Back' item.\n\nIn the list, a sub-wheel sits right under its parent, shifted in and tinted, with a line down its left side.",
                    Enter = () => Show(Node(tSub)), Target = () => Row(Node(tSub)) },
            new() { Title = "Opening a wheel in VR", Text = "A main wheel opens from a controller binding. Select a binding and set 'What happens' to 'Open a wheel', then pick your wheel.\n\nThe ready-made 'Right hand wheel' binding does this already: double-tap the right thumbrest.",
                    Enter = () => Show(Node(tBinding)), Target = () => Row(Node(tBinding)) },

            new() { Title = "Wrist panels", Text = "Buttons on your forearm. They show when you turn your wrist toward your face and look at it, and you press them by poking with your other hand.\n\nEach panel can be off, have its own pages, or mirror the other arm's panel. Switch that in the strip at the top.",
                    Enter = () => Select(wristRoots[0]), Target = () => Ctl(editorHost) },
            new() { Title = "Settings", Text = "How the wheels look and how you pick items, the VR keyboard's size and place, and the colours. These apply to everything in this profile.",
                    Enter = () => Select(settingsNode()), Target = () => Ctl(editorHost) },
            new() { Title = "Save", Text = "Changes reach the game when you save (Ctrl+S). A running game picks them up within a second.\n\nThat's it! Click Finish. You can replay this tour any time with the 'Tutorial' button.",
                    Target = () => ToolItemRect("Save") },
        };
    }

    Rectangle? ToolItemRect(string startsWith)
    {
        var ts = Controls.OfType<ToolStrip>().FirstOrDefault();
        var it = ts?.Items.Cast<ToolStripItem>().FirstOrDefault(i => i.Text?.StartsWith(startsWith) == true);
        return it == null ? null : RectangleToClient(ts.RectangleToScreen(it.Bounds));
    }

    // ---------- coach card ----------
    void BuildCoach()
    {
        coach = new Panel { Size = new Size(440, 250), BackColor = Color.FromArgb(28, 36, 52), Padding = new Padding(16) };
        coachTitle = new Label { Dock = DockStyle.Top, Height = 32, Font = new Font("Segoe UI", 13f, FontStyle.Bold), ForeColor = Color.White };
        coachText = new Label { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 10.5f), ForeColor = Color.FromArgb(220, 228, 240) };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = FlowDirection.RightToLeft, BackColor = Color.Transparent };
        Button B(string t, Color bg, Action a)
        {
            var b = new Button { Text = t, AutoSize = true, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = bg, ForeColor = Color.White, Font = new Font("Segoe UI", 10f, FontStyle.Bold), Cursor = Cursors.Hand, Margin = new Padding(6, 0, 0, 0) };
            b.FlatAppearance.BorderSize = 0; b.Click += (s, e) => a(); return b;
        }
        coachNext = B("Next  ▶", Color.FromArgb(240, 140, 30), () => { if (tourAt >= tour.Count - 1) EndTutorial(true); else TourGo(tourAt + 1); });
        coachBack = B("◀  Back", Color.FromArgb(70, 82, 104), () => TourGo(Math.Max(0, tourAt - 1)));
        var skip = B("Skip tour", Color.FromArgb(70, 82, 104), () => EndTutorial(false));
        coachCount = new Label { AutoSize = true, ForeColor = Color.FromArgb(150, 160, 180), Margin = new Padding(0, 8, 10, 0) };
        bar.Controls.AddRange(new Control[] { coachNext, coachBack, skip, coachCount });
        coach.Controls.Add(coachText); coach.Controls.Add(bar); coach.Controls.Add(coachTitle);
        Controls.Add(coach);
        for (int i = 0; i < 4; i++) { frame[i] = new Panel { BackColor = Color.FromArgb(255, 140, 20), Visible = false }; Controls.Add(frame[i]); frame[i].BringToFront(); }
        coach.BringToFront();
        Resize += TourLayout;
    }

    void TourGo(int i)
    {
        tourAt = i;
        var st = tour[i];
        st.Enter?.Invoke();
        coachTitle.Text = st.Title;
        coachText.Text = st.Text;
        coachCount.Text = $"{i + 1} / {tour.Count}";
        coachBack.Enabled = i > 0;
        TourTick();
    }

    void TourTick()
    {
        if (coach == null || tourAt < 0) return;
        var st = tour[tourAt];
        bool waiting = st.Done != null && !st.Done();
        if (st.Done != null && !waiting && coachNext.Tag as string != "done") { coachNext.Tag = "done"; TourGo(tourAt + 1 < tour.Count ? tourAt + 1 : tourAt); coachNext.Tag = null; return; }
        coachNext.Enabled = !waiting;
        coachNext.Text = waiting ? "Waiting for you..." : tourAt >= tour.Count - 1 ? "Finish  ✓" : "Next  ▶";
        TourLayout(null, null);
    }

    void TourLayout(object s, EventArgs e)
    {
        if (coach == null) return;
        Rectangle? t = null;
        try { t = tour[tourAt].Target?.Invoke(); } catch { }
        // coach in the corner away from the target
        bool targetRight = t is Rectangle tr && tr.X + tr.Width / 2 > ClientSize.Width / 2 && tr.Width < ClientSize.Width * 0.6;
        bool bigRight = t is Rectangle tb && tb.Width >= ClientSize.Width * 0.4 && tb.X > ClientSize.Width / 3;
        int x = (targetRight && !bigRight) ? 16 : ClientSize.Width - coach.Width - 24;
        if (bigRight) x = ClientSize.Width - coach.Width - 24; // editor page: sit in its lower right corner
        coach.Location = new Point(x, ClientSize.Height - coach.Height - 34);
        const int w = 4;
        if (t is Rectangle r)
        {
            r.Inflate(3, 3);
            frame[0].Bounds = new Rectangle(r.X - w, r.Y - w, r.Width + 2 * w, w);
            frame[1].Bounds = new Rectangle(r.X - w, r.Bottom, r.Width + 2 * w, w);
            frame[2].Bounds = new Rectangle(r.X - w, r.Y, w, r.Height);
            frame[3].Bounds = new Rectangle(r.Right, r.Y, w, r.Height);
            foreach (var f in frame) { f.Visible = true; f.BringToFront(); }
        }
        else foreach (var f in frame) f.Visible = false;
        coach.BringToFront();
    }

    void EndTutorial(bool finished)
    {
        if (coach == null) return;
        tourTimer?.Stop(); tourTimer?.Dispose(); tourTimer = null;
        Resize -= TourLayout;
        Controls.Remove(coach); coach.Dispose(); coach = null;
        foreach (var f in frame) { Controls.Remove(f); f.Dispose(); }
        bool changed = Serialize() != tourSnapshot;
        if (changed && MessageBox.Show(this, (finished ? "Nice work! " : "") + "Keep what you made during the tour (the binding, wheel and sub-wheel)?\n\nChoose No to put your profile back the way it was before the tour.",
                "Tutorial", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.No)
        {
            var tmp = Path.Combine(Path.GetTempPath(), "vro_tour_restore.json");
            File.WriteAllText(tmp, tourSnapshot);
            LoadFile(tmp);
            currentFile = tourFile;
            Text = "VR Radial Overlay - Profile Editor - " + Path.GetFileName(currentFile);
            if (tourWasDirty) MarkDirty(); else dirty = false;
            SetStatus("Tour changes thrown away.", false);
        }
        tree.SelectedNode = OverviewNode();
    }

    // Test: run the whole tour on the built-in defaults, doing each "your turn" step, and screenshot every step.
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
    public static void TourShots(string dir)
    {
        Directory.CreateDirectory(dir);
        var f = new MainForm { StartPosition = FormStartPosition.Manual, Location = new Point(0, 0) };
        f.Show();
        var builtin = Path.Combine(Path.GetTempPath(), "vro_builtin_default.json");
        File.WriteAllText(builtin, Native.DefaultProfile() ?? "{}");
        f.LoadFile(builtin);
        void Pump() { for (int i = 0; i < 8; i++) Application.DoEvents(); }
        Pump();
        f.StartTutorial();
        var log = new List<string>();
        for (int guard = 0; guard < 40 && f.coach != null; guard++)
        {
            Pump(); f.TourTick(); Pump();
            int at = f.tourAt; var st = f.tour[at];
            Rectangle? t = null; try { t = st.Target?.Invoke(); } catch { }
            log.Add($"{at + 1} {st.Title}  target={(t?.ToString() ?? "none")}  waiting={(st.Done != null && !st.Done())}");
            using (var bmp = new Bitmap(f.Width, f.Height)) { using (var g = Graphics.FromImage(bmp)) { var dc = g.GetHdc(); PrintWindow(f.Handle, dc, 2); g.ReleaseHdc(dc); } bmp.Save(Path.Combine(dir, $"tour_{at + 1:00}.png")); }
            if (st.Done != null && !st.Done())
            {
                // do what the user would click
                TreeNode slot(TreeNode p) => p?.Nodes.Cast<TreeNode>().LastOrDefault(c => c.Tag is AddSlot);
                if (st.Title.Contains("binding")) { f.tree.SelectedNode = f.bindingRoots[1]; f.Add(); }
                else if (st.Title.Contains("make a wheel")) { f.tree.SelectedNode = f.wheelsRoot; f.Add(); }
                else if (st.Title.Contains("add an item")) { f.tree.SelectedNode = Find(f.tree.Nodes, f.tWheel); f.Add(); }
                else if (st.Title.Contains("sub-wheel")) f.AddSub(Find(f.tree.Nodes, f.tWheel));
                Pump(); f.TourTick(); Pump();
                if (f.tourAt == at) { log.Add("  STUCK"); break; }
                continue;
            }
            if (at >= f.tour.Count - 1) { log.Add("finished"); break; }
            f.TourGo(at + 1);
        }
        File.WriteAllLines(Path.Combine(dir, "tour.txt"), log);
        f.dirty = false; f.Close();
    }
}
