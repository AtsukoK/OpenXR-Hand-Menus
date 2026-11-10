using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Win32;

namespace OpenXRHandMenusEditor;

static class Native
{
    [DllImport("OpenXRHandMenus.dll", CallingConvention = CallingConvention.Cdecl)]
    static extern int vro_validate([MarshalAs(UnmanagedType.LPUTF8Str)] string text, byte[] err, int errLen);
    [DllImport("OpenXRHandMenus.dll", CallingConvention = CallingConvention.Cdecl)]
    static extern IntPtr vro_default_profile();

    public static string Validate(string json)
    {
        try
        {
            var buf = new byte[1024];
            if (vro_validate(json, buf, buf.Length) != 0) return null;
            return Encoding.UTF8.GetString(buf, 0, Array.IndexOf(buf, (byte)0) is int n && n >= 0 ? n : buf.Length);
        }
        catch (Exception e) { return "(could not load OpenXRHandMenus.dll to check: " + e.Message + ")"; }
    }
    [DllImport("OpenXRHandMenus.dll", CallingConvention = CallingConvention.Cdecl)]
    static extern int vro_check_keys([MarshalAs(UnmanagedType.LPUTF8Str)] string text, int seq, byte[] err, int errLen);

    /// <summary>null = fine (or cannot check), otherwise the layer's reason.</summary>
    public static string CheckKeys(string text, bool sequence)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            var buf = new byte[512];
            if (vro_check_keys(text, sequence ? 1 : 0, buf, buf.Length) != 0) return null;
            int n = Array.IndexOf(buf, (byte)0);
            return Encoding.UTF8.GetString(buf, 0, n >= 0 ? n : buf.Length);
        }
        catch { return null; }
    }
    public static string DefaultProfile()
    {
        try { return Marshal.PtrToStringUTF8(vro_default_profile()); } catch { return null; }
    }
}

public partial class MainForm : Form
{
    public static MainForm Current;
    public ProfileModel Profile;

    const string LayerJson = "XR_APILAYER_NOVENDOR_openxr_hand_menus.json";
    const string ImplicitKey = @"SOFTWARE\Khronos\OpenXR\1\ApiLayers\Implicit";
    static readonly string DataDir = MigrateDataDir();
    // First run after the rename from VR Radial Overlay: copy the old settings folder across.
    static string MigrateDataDir()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = Path.Combine(local, "OpenXRHandMenus");
        var old = Path.Combine(local, "VRRadialOverlay");
        try
        {
            if (!Directory.Exists(dir) && Directory.Exists(old))
                foreach (var f in Directory.GetFiles(old, "*", SearchOption.AllDirectories))
                {
                    var t = Path.Combine(dir, Path.GetRelativePath(old, f));
                    Directory.CreateDirectory(Path.GetDirectoryName(t));
                    File.Copy(f, t, false);
                }
        }
        catch { }
        return dir;
    }
    static readonly string ProfilesDir = Path.Combine(DataDir, "profiles");
    internal static string ProfilesPath => ProfilesDir;

    readonly ToolStripComboBox profileBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    readonly ToolStripLabel layerStatus = new();
    readonly TreeView tree = new() { Dock = DockStyle.Fill, HideSelection = false, Font = new Font("Segoe UI", 10f) };
    readonly PropertyGrid grid = new() { Dock = DockStyle.Fill, PropertySort = PropertySort.Categorized, ToolbarVisible = false };
    readonly StatusStrip status = new();
    readonly ToolStripStatusLabel statusText = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    string currentFile;
    bool dirty, loading;

    readonly Panel editorHost = new() { Dock = DockStyle.Fill };
    Stack<object> history = new();
    object lastTag;
    bool navigatingBack;
    Button backBtn, homeBtn, dupBtn, delBtn, upBtn, downBtn;

    // Big, easy to spot navigation above every page.
    Control BuildNavBar()
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 46, Padding = new Padding(8, 6, 8, 4), BackColor = Color.FromArgb(232, 238, 248), WrapContents = false };
        Button Nav(string text, Color back)
        {
            var b = new Button { Text = text, AutoSize = true, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = back, ForeColor = Color.White, Font = new Font("Segoe UI", 10f, FontStyle.Bold), Margin = new Padding(0, 0, 8, 0), Padding = new Padding(8, 0, 8, 0), Cursor = Cursors.Hand };
            b.FlatAppearance.BorderSize = 0;
            return b;
        }
        backBtn = Nav("\u25C0  Back", Color.FromArgb(70, 90, 120));
        homeBtn = Nav("\u2302  Overview", Color.FromArgb(40, 90, 170));
        backBtn.Click += (s, e) => GoBack();
        homeBtn.Click += (s, e) => GoTo("overview");
        bar.Controls.Add(backBtn); bar.Controls.Add(homeBtn);
        Button Act(string text, Action a)
        {
            var b = new Button { Text = text, AutoSize = true, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = Color.White, ForeColor = Color.FromArgb(40, 50, 70), Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), Margin = new Padding(0, 0, 6, 0), Cursor = Cursors.Hand };
            b.FlatAppearance.BorderColor = Color.FromArgb(190, 200, 215);
            b.Click += (s, e) => a();
            return b;
        }
        bar.Controls.Add(new Label { Width = 14 });
        dupBtn = Act("Duplicate", Duplicate);
        delBtn = Act("Delete", Remove);
        upBtn = Act("\u25B2 Up", () => MoveItem(-1));
        downBtn = Act("\u25BC Down", () => MoveItem(1));
        delBtn.ForeColor = Color.FromArgb(170, 40, 40);
        bar.Controls.AddRange(new Control[] { dupBtn, delBtn, upBtn, downBtn });
        UpdateNav();
        return bar;
    }
    void UpdateNav()
    {
        if (dupBtn != null)
        {
            var tag = tree.SelectedNode?.Tag;
            string what = tag switch
            {
                BindingModel => "binding",
                WheelItemModel => InWrist(tree.SelectedNode) ? "button" : "item",
                WheelModel => InWrist(tree.SelectedNode) ? "page" : "wheel",
                _ => null,
            };
            bool movable = tag is BindingModel or WheelItemModel;
            dupBtn.Enabled = delBtn.Enabled = what != null;
            upBtn.Enabled = downBtn.Enabled = movable;
            dupBtn.Text = what != null ? "Duplicate " + what : "Duplicate";
            delBtn.Text = what != null ? "Delete " + what : "Delete";
        }
        if (backBtn != null) backBtn.Enabled = history.Count > 0;
        if (homeBtn != null) homeBtn.Enabled = lastTag as string != "overview";
    }
    public void GoTo(object tag)
    {
        var n = Find(tree.Nodes, tag);
        if (n != null) { tree.SelectedNode = n; n.EnsureVisible(); }
    }
    void GoBack()
    {
        while (history.Count > 0)
        {
            var tag = history.Pop();
            var n = Find(tree.Nodes, tag);
            if (n == null) continue; // deleted since
            navigatingBack = true;
            try { tree.SelectedNode = n; n.EnsureVisible(); } finally { navigatingBack = false; }
            break;
        }
        UpdateNav();
    }
    readonly Button addBtn = new() { Text = "Add", AutoSize = true };
    Control currentEditor;

    static readonly JsonSerializerOptions JsonOpts = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, WriteIndented = true, PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public MainForm()
    {
        Current = this;
        Text = "OpenXR Hand Menus - Profile Editor";
        Size = new Size(1150, 760);
        Font = new Font("Segoe UI", 10.5f);
        Directory.CreateDirectory(ProfilesDir);

        var ts = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(4) };
        ts.Items.Add(new ToolStripLabel("Profile:"));
        ts.Items.Add(profileBox);
        ts.Items.Add(Btn("New game profile...", NewProfile));
        ts.Items.Add(Btn("Delete profile", DeleteProfile));
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(Btn("Controllers", () => ChooseControllers(false)));
        ts.Items.Add(Btn("Tutorial", StartTutorial));
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(Btn("Save (Ctrl+S)", () => Save()));
        ts.Items.Add(Btn("Revert", () => LoadFile(currentFile)));
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(Btn("Open folder", () => Process.Start("explorer.exe", ProfilesDir)));
        ts.Items.Add(Btn("Logs", () => { Directory.CreateDirectory(Path.Combine(DataDir, "logs")); Process.Start("explorer.exe", Path.Combine(DataDir, "logs")); }));
        ts.Items.Add(new ToolStripSeparator());
        ts.Items.Add(Btn("Install layer", () => RunScript("install.ps1")));
        ts.Items.Add(Btn("Uninstall layer", () => RunScript("uninstall.ps1")));
        ts.Items.Add(layerStatus);
        profileBox.SelectedIndexChanged += (s, e) => { if (!loading && profileBox.SelectedItem is string f) { if (ConfirmDiscard()) LoadFile(Path.Combine(ProfilesDir, f)); } };

        var left = new Panel { Dock = DockStyle.Fill };
        addBtn.Click += (s, e) => Add();
        left.Controls.Add(tree);
        var searchWrap = new Panel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(6, 6, 6, 4), BackColor = Color.FromArgb(248, 249, 251) };
        searchWrap.Controls.Add(search);
        left.Controls.Add(searchWrap);


        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 430 };
        split.Panel1.Controls.Add(left);
        split.Panel2.Controls.Add(editorHost);
        split.Panel2.Controls.Add(BuildNavBar());

        status.Items.Add(statusText);
        Controls.Add(split);
        Controls.Add(ts);
        Controls.Add(status);
        split.SplitterDistance = 430;

        tree.AfterSelect += (s, e) =>
        {
            // Remember where we came from so Back can return there.
            if (!navigatingBack && lastTag != null && !ReferenceEquals(lastTag, e.Node?.Tag)) { history.Push(lastTag); if (history.Count > 50) history = new Stack<object>(history.Take(50).Reverse()); }
            lastTag = e.Node?.Tag;
            ShowEditor(e.Node); tree.Invalidate();
            UpdateNav();
        };
        SetupTreeLook();
        tree.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Delete) { Remove(); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.D) { Duplicate(); e.Handled = true; }
            else if (e.Alt && e.KeyCode == Keys.Up) { MoveItem(-1); e.Handled = true; }
            else if (e.Alt && e.KeyCode == Keys.Down) { MoveItem(1); e.Handled = true; }
        };
        var menu = new ContextMenuStrip { Font = new Font("Segoe UI", 10f) };
        tree.NodeMouseClick += (s, e) =>
        {
            if (e.Button != MouseButtons.Right || e.Node.Tag is AddSlot) return;
            tree.SelectedNode = e.Node;
            menu.Items.Clear();
            string add = e.Node.Tag switch
            {
                "bindings_right" or "bindings_left" or BindingModel => "Add binding",
                "wheels" => "New wheel", "wrist0" or "wrist1" => "New page",
                WheelModel or WheelItemModel => InWrist(e.Node) ? "Add button" : "Add item", _ => null,
            };
            if (add != null) menu.Items.Add(add, null, (o, a) => Add());
            if (dupBtn.Enabled) { menu.Items.Add(dupBtn.Text + "   (Ctrl+D)", null, (o, a) => Duplicate()); menu.Items.Add(delBtn.Text + "   (Del)", null, (o, a) => Remove()); }
            if (upBtn.Enabled) { menu.Items.Add("Move up   (Alt+\u2191)", null, (o, a) => MoveItem(-1)); menu.Items.Add("Move down   (Alt+\u2193)", null, (o, a) => MoveItem(1)); }
            if (menu.Items.Count > 0) menu.Show(tree, e.Location);
        };
        SetupDragDrop();
        grid.PropertyValueChanged += (s, e) => { MarkDirty(); RefreshNodeTexts(); };
        KeyPreview = true;
        KeyDown += (s, e) => { if (e.Control && e.KeyCode == Keys.S && !KeyBox.Recording) { Save(); e.SuppressKeyPress = true; } };
        FormClosing += (s, e) => { if (!ConfirmDiscard()) e.Cancel = true; };

        UpdateLayerStatus();
        RefreshProfileList("default.json");
    }

    // Headless screenshots of each editor page (for checking layout).
    public static void Shots(string dir)
    {
        Directory.CreateDirectory(dir);
        var f = new MainForm { StartPosition = FormStartPosition.Manual, Location = new Point(0, 0) };
        f.Show();
        for (int i = 0; i < 10; i++) Application.DoEvents();
        // Screenshots show the app's built-in default profile, never the user's own (nothing is saved).
        var builtin = Path.Combine(Path.GetTempPath(), "vro_builtin_default.json");
        File.WriteAllText(builtin, Native.DefaultProfile() ?? "{}");
        f.LoadFile(builtin);
        void Snap(string name, TreeNode n)
        {
            if (n == null) return;
            f.tree.SelectedNode = n;
            for (int i = 0; i < 5; i++) Application.DoEvents();
            using var bmp = new Bitmap(f.Width, f.Height);
            f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
            bmp.Save(Path.Combine(dir, name + ".png"));
        }
        {   // every arm pose picture, both arms, side by side
            string[] modes = { "fingers", "between", "along_arm" };
            using var sheet = new Bitmap(380 * 3, 300 * 2);
            for (int arm = 0; arm < 2; arm++)
                for (int m = 0; m < 3; m++)
                {
                    using var pv = new ArmPosePreview { Mode = modes[m], RightArm = arm == 1 };
                    using var b = new Bitmap(pv.Width, pv.Height);
                    pv.DrawToBitmap(b, new Rectangle(0, 0, pv.Width, pv.Height));
                    using var gg = Graphics.FromImage(sheet);
                    gg.DrawImage(b, m * 380, arm * 300);
                }
            sheet.Save(Path.Combine(dir, "0_arm_poses.png"));
        }
        {   // every small picture used on the Overview, on and off
            var pics = new List<Bitmap>
            {
                MiniPics.Headset(96, 72, true), MiniPics.Wheel(96, 72, 1, null, true), MiniPics.Wheel(96, 72, 0, null, false),
                MiniPics.Wrist(96, 72, 0, null, "ON"), MiniPics.Wrist(96, 72, 1, null, "MIRRORED"), MiniPics.Wrist(96, 72, 1, null, "OFF"),
                MiniPics.Keyboard(96, 72, true), MiniPics.Controllers(96, 72, true),
            };
            using var sheet2 = new Bitmap(pics.Count * 104, 80);
            using (var gg = Graphics.FromImage(sheet2)) { gg.Clear(Color.White); for (int i = 0; i < pics.Count; i++) gg.DrawImage(pics[i], i * 104 + 4, 4); }
            sheet2.Save(Path.Combine(dir, "0_minipics.png"));
            foreach (var b in pics) b.Dispose();
        }
        Snap("00_overview", f.tree.Nodes[0]);
        Snap("1_settings", f.tree.Nodes[1]);
        Snap("2_binding", f.bindingRoots[1].Nodes.Count > 0 ? f.bindingRoots[1].Nodes[0] : null);
        Snap("3_binding_chord", f.bindingRoots[1].Nodes.Count > 1 ? f.bindingRoots[1].Nodes[1] : null);
        Snap("4_wheel", f.wheelsRoot.Nodes.Count > 0 ? f.wheelsRoot.Nodes[0] : null);
        Snap("4b_wheel_last", f.wheelsRoot.Nodes.Count > 1 ? f.wheelsRoot.Nodes[f.wheelsRoot.Nodes.Count - 2] : null);
        Snap("6_wrist", f.wristRoots[0]);
        Snap("7_wrist_page", f.wristRoots[0].Nodes.Count > 0 ? f.wristRoots[0].Nodes[0] : null);
        Snap("8_wrist_button", f.wristRoots[0].Nodes.Count > 0 && f.wristRoots[0].Nodes[0].Nodes.Count > 2 ? f.wristRoots[0].Nodes[0].Nodes[2] : null);
        Snap("9_wrist_right", f.wristRoots[1]);
        Snap("5_item", f.wheelsRoot.Nodes.Count > 0 && f.wheelsRoot.Nodes[0].Nodes.Count > 0 ? f.wheelsRoot.Nodes[0].Nodes[0] : null);
        f.Fold(f.bindingRoots[1]); f.Fold(f.wheelsRoot);
        Snap("10_folded", f.tree.Nodes[0]);
        // Drop logic check (in memory only, never saved).
        var log = new StringBuilder();
        var b0 = (BindingModel)f.bindingRoots[1].Nodes[0].Tag;
        f.DoDrop(f.bindingRoots[1].Nodes[0], f.bindingRoots[1].Nodes[1], true);
        log.AppendLine("binding 0 dropped after binding 1 -> now at index " + f.Profile.Bindings.IndexOf(b0) + " (expect 1)");
        var w0 = f.wheelsRoot.Nodes[0]; var w1 = f.wheelsRoot.Nodes[1];
        var it0 = (WheelItemModel)w0.Nodes[0].Tag;
        int before0 = ((WheelModel)w0.Tag).Items.Count, before1 = ((WheelModel)w1.Tag).Items.Count;
        f.DoDrop(Find(f.tree.Nodes, it0), f.wheelsRoot.Nodes[1], true);
        log.AppendLine($"item moved to other wheel -> source {before0}->{((WheelModel)w0.Tag).Items.Count}, target {before1}->{((WheelModel)w1.Tag).Items.Count}, last={ReferenceEquals(((WheelModel)w1.Tag).Items.Last(), it0)}");
        var tgtFirst = Find(f.tree.Nodes, ((WheelModel)f.wheelsRoot.Nodes[1].Tag).Items[0]);
        f.DoDrop(Find(f.tree.Nodes, it0), tgtFirst, false);
        log.AppendLine("item dropped before first -> index " + ((WheelModel)f.wheelsRoot.Nodes[1].Tag).Items.IndexOf(it0) + " (expect 0)");
        File.WriteAllText(Path.Combine(dir, "drop.txt"), log.ToString());
        f.dirty = false;
        f.Close();
    }

    // ------------------------------------------------------------ right-hand editor
    void ShowEditor(TreeNode n)
    {
        var old = currentEditor;
        Control ed;
        void Changed() { MarkDirty(); RefreshNodeTexts(); }
        switch (n?.Tag)
        {
            case "overview":
                {
                    var ov = new OverviewEditor(Profile, GoTo);
                    ov.Changed += Changed; ed = ov; break;
                }
            case ProfileModel p: { var se = new SettingsEditor(p); se.Changed += Changed; ed = se; break; }
            case BindingModel b: { var be = new BindingEditor(b); be.Changed += Changed; ed = be; break; }
            case "wrist0": case "wrist1": { var se = new SettingsEditor(Profile, (string)n.Tag == "wrist1" ? 1 : 0); (bool, bool) lastMirror = (Profile.WristLeft.Mirror, Profile.WristRight.Mirror); se.Changed += () => { Changed(); var m = (Profile.WristLeft.Mirror, Profile.WristRight.Mirror); if (m != lastMirror) { lastMirror = m; BuildTree(); } }; ed = se; break; }
            case WheelModel w:
                {
                    int wh = WristOf(n);
                    var we = wh >= 0 ? new WheelEditor(w, (a, b) => RenamePage(wh, a, b), Profile.WristPages(wh), Profile.WristFor(wh).Columns)
                                     : new WheelEditor(w, RenameWheel);
                    we.Changed += Changed;
                    we.Preview.ItemClicked += it => tree.SelectedNode = Find(tree.Nodes, it);
                    ed = we; break;
                }
            case WheelItemModel i:
                {
                    var w2 = (WheelModel)n.Parent.Tag;
                    int ih = WristOf(n);
                    var ie = ih >= 0 ? new ItemEditor(w2, i, Profile.WristPages(ih), Profile.WristFor(ih).Columns) : new ItemEditor(w2, i);
                    ie.Changed += Changed;
                    ie.Preview.ItemClicked += it => tree.SelectedNode = Find(tree.Nodes, it);
                    ed = ie; break;
                }
            case "bindings_right":
            case "bindings_left":
                ed = InfoWithPic(n, (string)n.Tag == "bindings_left" ? "Left controller" : "Right controller", "A binding makes a controller button (or a combo of buttons) do something: press a key, open a wheel, open the keyboard, switch a layer...\n\nClick '+ Add' on this controller's bar in the list to make one. Click the round dot on the right of a row to switch it on or off without deleting it.\n\nLayers: a binding that switches a layer on lets the same buttons do different things, as long as that layer is on.");
                break;
            case "wheels":
                ed = InfoWithPic(n, "Wheels", "A wheel is a radial menu. A controller binding opens a main wheel; an item on a wheel can open a sub-wheel.\n\nTo make a wheel, click '+ Add' on the WHEELS bar in the list. To add items or a sub-wheel, unfold WHEELS (the arrow on its left) and use the '+ Add item' / '+ Add sub-wheel' row under a wheel.");
                break;
            default: ed = new Panel(); break;
        }
        addBtn.Text = n?.Tag switch
        {
            "bindings_right" or "bindings_left" or BindingModel => "Add binding",
            "wheels" => "Add wheel",
            "wrist0" or "wrist1" => "Add page",
            WheelModel or WheelItemModel when InWrist(n) => "Add button",
            WheelModel or WheelItemModel => "Add item",
            _ => "Add",
        };
        addBtn.Enabled = n?.Tag is not ProfileModel && n?.Tag as string != "overview";
        editorHost.SuspendLayout();
        editorHost.Controls.Clear();
        ed.Dock = DockStyle.Fill;
        editorHost.Controls.Add(ed);
        string tip = TipFor(n);
        if (tip != null) editorHost.Controls.Add(TipBar(tip));
        int stripHand = n?.Tag is WheelModel or WheelItemModel || n?.Tag as string is "wrist0" or "wrist1" ? WristOf(n) : -1;
        if (stripHand >= 0) editorHost.Controls.Add(PanelModeStrip(stripHand, () => { MarkDirty(); BuildTree(); }));
        else if (n?.Tag as string is "bindings_left" or "bindings_right" or "wheels") editorHost.Controls.Add(SectionSwitch((string)n.Tag));
        editorHost.ResumeLayout();
        currentEditor = ed;
        if (old != null && !ReferenceEquals(old, ed)) { if (old.Contains(grid)) old.Controls.Remove(grid); old.Dispose(); }
    }

    // ------------------------------------------------------------ drag to reorder
    const int TVM_SETINSERTMARK = 0x111A;
    [DllImport("user32.dll", EntryPoint = "SendMessageW")] static extern IntPtr SendMsg(IntPtr h, int msg, IntPtr w, IntPtr l);
    TreeNode highlightedWheel;

    void SetupDragDrop()
    {
        tree.AllowDrop = true;
        tree.ItemDrag += (s, e) =>
        {
            if (e.Item is TreeNode n && n.Tag is BindingModel or WheelItemModel)
            {
                tree.SelectedNode = n;
                tree.DoDragDrop(n, DragDropEffects.Move);
            }
        };
        tree.DragOver += (s, e) =>
        {
            var pt = tree.PointToClient(new Point(e.X, e.Y));
            var (target, after) = DropTarget(e.Data.GetData(typeof(TreeNode)) as TreeNode, pt);
            e.Effect = target != null ? DragDropEffects.Move : DragDropEffects.None;
            ShowDropMark(target, after);
            // Scroll when dragging near the top or bottom edge.
            var under = tree.GetNodeAt(pt);
            if (under != null && pt.Y < 16) under.PrevVisibleNode?.EnsureVisible();
            else if (under != null && pt.Y > tree.ClientSize.Height - 16) under.NextVisibleNode?.EnsureVisible();
        };
        tree.DragLeave += (s, e) => ShowDropMark(null, false);
        tree.DragDrop += (s, e) =>
        {
            ShowDropMark(null, false);
            var src = e.Data.GetData(typeof(TreeNode)) as TreeNode;
            var (target, after) = DropTarget(src, tree.PointToClient(new Point(e.X, e.Y)));
            if (target != null) DoDrop(src, target, after);
        };
    }

    // Where a dragged node would land: (node to insert next to, after it?). A wheel node means "append to that wheel".
    (TreeNode, bool) DropTarget(TreeNode src, Point pt)
    {
        var n = tree.GetNodeAt(pt);
        if (src == null || n == null || n == src) return (null, false);
        if (n.Tag is AddSlot)
        {
            if (src.Tag is WheelItemModel && n.Parent?.Tag is WheelModel) return (n.Parent, true); // onto "+ Add item" = append to that wheel
            return (null, false);
        }
        bool lowerHalf = pt.Y > n.Bounds.Top + n.Bounds.Height / 2;
        if (src.Tag is BindingModel)
        {
            if (n.Tag is BindingModel) return (n, lowerHalf);
            if ((n == bindingRoots[0] || n == bindingRoots[1]) && n.Nodes.Count > 0) return (n.Nodes[0], false);
        }
        else if (src.Tag is WheelItemModel)
        {
            if (n.Tag is WheelItemModel) return (n, lowerHalf);
            if (n.Tag is WheelModel w)
                return ReferenceEquals(w, src.Parent.Tag) && n.Nodes.Count > 0 ? (n.Nodes[0], false) : (n, true);
        }
        return (null, false);
    }

    // Insertion line between nodes; dropping onto another wheel highlights that wheel.
    void ShowDropMark(TreeNode n, bool after)
    {
        var wheel = n?.Tag is WheelModel ? n : null;
        var line = wheel == null ? n : null;
        if (ReferenceEquals(wheel, dropWheel) && ReferenceEquals(line, dropNode) && after == dropAfter) return;
        dropWheel = wheel; dropNode = line; dropAfter = after;
        tree.Invalidate();
    }

    void DoDrop(TreeNode src, TreeNode target, bool after)
    {
        if (src.Tag is BindingModel b && target.Tag is BindingModel tb)
        {
            Profile.Bindings.Remove(b);
            Profile.Bindings.Insert(Profile.Bindings.IndexOf(tb) + (after ? 1 : 0), b);
            Rebuild(b);
        }
        else if (src.Tag is WheelItemModel it)
        {
            var fromWheel = (WheelModel)src.Parent.Tag;
            var toWheel = target.Tag as WheelModel ?? (WheelModel)target.Parent.Tag;
            if (!ReferenceEquals(fromWheel, toWheel) && toWheel.Items.Count >= 16) { MessageBox.Show(this, $"Wheel {toWheel.Name} already has 16 items."); return; }
            fromWheel.Items.Remove(it);
            int i = target.Tag is WheelItemModel ti ? toWheel.Items.IndexOf(ti) + (after ? 1 : 0) : toWheel.Items.Count;
            toWheel.Items.Insert(Math.Clamp(i, 0, toWheel.Items.Count), it);
            Rebuild(it);
        }
    }

    // Run after the current event; before the window exists, wait until it is shown.
    void Later(Action a)
    {
        if (IsHandleCreated) BeginInvoke(a);
        else { EventHandler h = null; h = (s, e) => { Shown -= h; a(); }; Shown += h; }
    }

    Control InfoWithPic(TreeNode n, string title, string text)
    {
        Image pic = n.Tag switch
        {
            "bindings_left" => MiniPics.Wheel(200, 140, 0, Profile.Bindings.Where(b => HandOf(b) == 0 && b.Action?.Type == "wheel").Select(b => Profile.Wheels.GetValueOrDefault(b.Action.Wheel ?? "")).FirstOrDefault(w => w != null), true),
            "bindings_right" => MiniPics.Wheel(200, 140, 1, Profile.Bindings.Where(b => HandOf(b) == 1 && b.Action?.Type == "wheel").Select(b => Profile.Wheels.GetValueOrDefault(b.Action.Wheel ?? "")).FirstOrDefault(w => w != null), true),
            "wheels" => MiniPics.Wheel(200, 140, 1, Profile.Wheels.Values.FirstOrDefault(), true),
            _ => null,
        };
        return Info(title, text, pic);
    }

    static Control Info(string title, string text, Image pic = null)
    {
        var p = new Panel { Padding = new Padding(14) };
        if (pic != null) p.Controls.Add(new PictureBox { Image = pic, Dock = DockStyle.Top, Height = pic.Height + 12, SizeMode = PictureBoxSizeMode.Normal, Padding = new Padding(0, 8, 0, 0) });
        p.Controls.Add(new Label { Text = text, AutoSize = true, MaximumSize = new Size(620, 0), Dock = DockStyle.Top, Font = new Font("Segoe UI", 10f) });
        p.Controls.Add(new Label { Text = title, AutoSize = true, Dock = DockStyle.Top, Font = new Font("Segoe UI", 14f, FontStyle.Bold), Padding = new Padding(0, 0, 0, 8) });
        return p;
    }

    // -1 = not under a wrist panel, 0 = left, 1 = right
    // Tips shown in a soft bar under the top strip.
    string TipFor(TreeNode n)
    {
        string t = TipText(n);
        if (t != null && n != null && IsSection(n) && !n.IsExpanded && RealCount(n) > 0)
            t = "This section is folded: click the arrow left of its name in the list to show what's in it. " + t;
        return t;
    }

    string TipText(TreeNode n)
    {
        bool wrist = n != null && WristOf(n) >= 0;
        return n?.Tag switch
        {
            "overview" => "Tip: the switches on each card turn things on or off right here. The blue buttons jump to where you edit them.",
            ProfileModel => "Tip: these settings apply to every wheel and panel. Changes take effect in the game when you save (Ctrl+S).",
            "bindings_left" or "bindings_right" => "Tip: click the round dot at the right of a binding to switch it on or off without deleting it. Right-click a row to duplicate, delete or move it.",
            "wheels" => "Tip: a wheel opened by a controller binding is a main wheel. Use '+ Add sub-wheel' under a wheel to make a submenu that is linked to it automatically.",
            "wrist0" or "wrist1" => "Tip: turn your wrist toward your face and look at it to show the panel. Poke a button with your other hand's finger, or point at it and pull the trigger.",
            BindingModel => "Tip: pick the controller button and gesture first, then what it does. 'Record key' then press a key on your keyboard to bind it.",
            WheelModel when wrist => "Tip: click a button in the panel picture to edit it. '+ Add sub-page' in the list makes a page that a button here opens.",
            WheelModel => "Tip: click a segment in the wheel picture to edit or rebind that item. Drag items in the list to reorder them.",
            WheelItemModel => "Tip: click any other " + (wrist ? "button" : "segment") + " in the picture to jump to it. 'Record key' then press a key to bind it.",
            _ => null,
        };
    }

    static Control TipBar(string text)
    {
        var p = new Panel { Dock = DockStyle.Top, Height = text.Length > 300 ? 62 : 40, BackColor = Color.FromArgb(255, 248, 225), Padding = new Padding(14, 0, 14, 0) };
        p.Controls.Add(new Label { Text = "💡  " + text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(110, 80, 10), Font = new Font("Segoe UI", 9.5f) });
        return p;
    }

    // Switch a whole section on/off from its page: every binding on that controller, or every binding that opens a wheel.
    Control SectionSwitch(string tag)
    {
        List<BindingModel> list = tag switch
        {
            "bindings_left" => Profile.Bindings.Where(b => HandOf(b) == 0).ToList(),
            "bindings_right" => Profile.Bindings.Where(b => HandOf(b) == 1).ToList(),
            _ => Profile.Bindings.Where(b => b.Action?.Type == "wheel").ToList(),
        };
        string what = tag == "wheels" ? "Bindings that open a wheel" : (tag == "bindings_left" ? "Left" : "Right") + " controller bindings";
        var strip = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48, Padding = new Padding(12, 8, 0, 0), BackColor = Color.FromArgb(236, 240, 247), WrapContents = false };
        int on = list.Count(b => b.Enabled);
        strip.Controls.Add(new Label { Text = $"{what}: {on} of {list.Count} on", AutoSize = true, Font = new Font("Segoe UI", 10f, FontStyle.Bold), Margin = new Padding(0, 7, 12, 0), ForeColor = Color.FromArgb(40, 60, 100) });
        void Opt(string text, bool v)
        {
            var b = new Button { Text = text, AutoSize = true, Height = 32, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, Margin = new Padding(0, 0, 6, 0), Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), BackColor = Color.White, ForeColor = Color.FromArgb(40, 50, 70), Enabled = list.Count > 0 };
            b.FlatAppearance.BorderColor = Color.FromArgb(170, 185, 210);
            b.Click += (s, e) => { foreach (var x in list) x.Enabled = v; MarkDirty(); BuildTree(); };
            strip.Controls.Add(b);
        }
        Opt("Switch all on", true);
        Opt("Switch all off", false);
        return strip;
    }

    // Quick switch above a wrist page / button: Off, own pages, or mirror the other panel.
    Control PanelModeStrip(int h, Action changed)
    {
        var wm = Profile.WristFor(h); var other = Profile.WristFor(1 - h);
        string side = h == 0 ? "Left" : "Right", otherSide = h == 0 ? "right" : "left";
        var strip = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48, Padding = new Padding(12, 8, 0, 0), BackColor = Color.FromArgb(232, 244, 241), WrapContents = false };
        strip.Controls.Add(new Label { Text = $"{side} wrist panel:", AutoSize = true, Font = new Font("Segoe UI", 10f, FontStyle.Bold), Margin = new Padding(0, 7, 10, 0), ForeColor = Color.FromArgb(30, 90, 80) });
        void Opt(string text, bool active, Action apply)
        {
            var b = new Button { Text = text, AutoSize = true, Height = 32, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, Margin = new Padding(0, 0, 6, 0),
                                 Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                                 BackColor = active ? Color.FromArgb(30, 130, 110) : Color.White, ForeColor = active ? Color.White : Color.FromArgb(40, 50, 70) };
            b.FlatAppearance.BorderColor = Color.FromArgb(150, 190, 180);
            b.Click += (s, e) => { if (active) return; apply(); changed(); };
            strip.Controls.Add(b);
        }
        Opt("Off", !wm.Enabled, () => wm.Enabled = false);
        Opt("On, own pages", wm.Enabled && !wm.Mirror, () => { wm.Enabled = true; wm.Mirror = false; });
        Opt($"On, mirror the {otherSide} panel", wm.Enabled && wm.Mirror, () => { wm.Enabled = true; wm.Mirror = true; other.Mirror = false; });
        return strip;
    }

    int WristOf(TreeNode n) { while (n?.Parent != null) n = n.Parent; return n == null ? -1 : n == wristRoots[0] ? 0 : n == wristRoots[1] ? 1 : -1; }
    bool InWrist(TreeNode n) => WristOf(n) >= 0;
    Dictionary<string, WheelModel> DictFor(TreeNode n) => WristOf(n) is int h && h >= 0 ? Profile.WristPages(h).Pages : Profile.Wheels;

    void RenamePage(int h, string oldName, string newName)
    {
        var owner = Profile.WristPages(h);
        var pages = owner.Pages;
        var w = pages[oldName];
        pages.Remove(oldName);
        pages[newName] = w;
        if (owner.Start == oldName) owner.Start = newName;
        foreach (var pg in pages.Values) foreach (var i in pg.Items) if (i.Action?.Type == "page" && i.Action.Page == oldName) i.Action.Page = newName;
        RefreshNodeTexts();
    }

    void RenameWheel(string oldName, string newName)
    {
        var w = Profile.Wheels[oldName];
        Profile.Wheels.Remove(oldName);
        Profile.Wheels[newName] = w;
        foreach (var b in Profile.Bindings) if (b.Action?.Type == "wheel" && b.Action.Wheel == oldName) b.Action.Wheel = newName;
        foreach (var ww in Profile.Wheels.Values.Concat(Profile.WristLeft.Pages.Values).Concat(Profile.WristRight.Pages.Values)) foreach (var i in ww.Items) if (i.Action?.Type == "wheel" && i.Action.Wheel == oldName) i.Action.Wheel = newName;
        RefreshNodeTexts();
    }

    static ToolStripButton Btn(string t, Action a) { var b = new ToolStripButton(t); b.Click += (s, e) => a(); return b; }
    static Button PBtn(string t, Action a) { var b = new Button { Text = t, AutoSize = true }; b.Click += (s, e) => a(); return b; }

    void SetStatus(string s, bool error = false) { statusText.Text = s; statusText.ForeColor = error ? Color.Firebrick : SystemColors.ControlText; }
    void MarkDirty() { dirty = true; Text = "OpenXR Hand Menus - Profile Editor - " + Path.GetFileName(currentFile) + " *"; }

    bool ConfirmDiscard()
    {
        if (!dirty) return true;
        var r = MessageBox.Show(this, "Save changes to " + Path.GetFileName(currentFile) + "?", "Unsaved changes", MessageBoxButtons.YesNoCancel);
        if (r == DialogResult.Cancel) return false;
        if (r == DialogResult.Yes) return Save();
        dirty = false;
        return true;
    }

    // ------------------------------------------------------------ files
    void RefreshProfileList(string select)
    {
        string def = Path.Combine(ProfilesDir, "default.json");
        if (!File.Exists(def)) File.WriteAllText(def, Native.DefaultProfile() ?? "{}");
        loading = true;
        profileBox.Items.Clear();
        foreach (var f in Directory.GetFiles(ProfilesDir, "*.json").Select(Path.GetFileName).OrderBy(f => f != "default.json").ThenBy(f => f))
            profileBox.Items.Add(f);
        profileBox.SelectedItem = select;
        loading = false;
        LoadFile(Path.Combine(ProfilesDir, select));
    }

    void LoadFile(string path)
    {
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            // Accept a chord given as an array as well.
            foreach (var b in root?["bindings"]?.AsArray() ?? new JsonArray())
                if (b?["input"] is JsonArray arr) b["input"] = string.Join("+", arr.Select(x => x.ToString()));
            foreach (var b in root?["bindings"]?.AsArray() ?? new JsonArray())
                foreach (var k in new[] { "layers", "not_layers" })
                    if (b?[k] is JsonArray la) b[k] = string.Join(",", la.Select(x => x.ToString()));
            Profile = root.Deserialize<ProfileModel>(JsonOpts) ?? new ProfileModel();
            foreach (var (k, w) in Profile.Wheels) w.Name = k;
            Profile.WristLeft ??= new(); Profile.WristRight ??= new() { Mirror = true };
            MigrateWrist();
            foreach (var wm in new[] { Profile.WristLeft, Profile.WristRight })
            {
                wm.Pages ??= new();
                foreach (var (k, w) in wm.Pages) w.Name = k;
            }
            EnsureWristPages();
            currentFile = path;
            dirty = false;
            NoteFileTime();
            Text = "OpenXR Hand Menus - Profile Editor - " + Path.GetFileName(path);
            BuildTree();
            var err = Native.Validate(File.ReadAllText(path));
            SetStatus(err == null ? "Loaded " + path : "Loaded, but the layer reports: " + err, err != null);
        }
        catch (Exception e)
        {
            MessageBox.Show(this, "Could not read " + path + ":\n" + e.Message, "Load error");
        }
    }

    // Headless check: round-trip every profile through the model and validate with the layer.
    public static int SelfTest(string outFile)
    {
        var sb = new StringBuilder(); int bad = 0;
        Directory.CreateDirectory(ProfilesDir);
        string def = Path.Combine(ProfilesDir, "default.json");
        if (!File.Exists(def)) File.WriteAllText(def, Native.DefaultProfile() ?? "{}");
        var f = new MainForm();
        foreach (var file in Directory.GetFiles(ProfilesDir, "*.json"))
        {
            f.LoadFile(file);
            string json = f.Serialize();
            var err = Native.Validate(json);
            sb.AppendLine($"{Path.GetFileName(file)}: {(err == null ? "OK" : "ERROR " + err)} ({f.Profile.Bindings.Count} bindings, {f.Profile.Wheels.Count} wheels)");
            if (err != null) bad++;
        }
        // Controller choices applied to the built-in defaults must still pass the layer's parser.
        var builtin = Path.Combine(Path.GetTempPath(), "vro_builtin_default.json");
        File.WriteAllText(builtin, Native.DefaultProfile() ?? "{}");
        foreach (var (id, _, _) in ControllerKinds)
        {
            f.LoadFile(builtin);
            int n = ApplyControllerOpeners(f.Profile, id);
            var err = Native.Validate(f.Serialize());
            var openers = string.Join(", ", f.Profile.Bindings.Where(b => b.Action?.Type == "wheel").Select(b => b.Input + " " + b.Gesture));
            sb.AppendLine($"controllers {id}: {(err == null && n == 2 ? "OK" : "ERROR " + err)} ({n} openers: {openers})");
            if (err != null || n != 2) bad++;
        }
        sb.AppendLine("--- round-trip of default ---");
        f.LoadFile(def); sb.AppendLine(f.Serialize());
        File.WriteAllText(outFile, sb.ToString());
        return bad;
    }

    // Older profiles used a wheel as the wrist panel. Move it (a copy if something else opens it) into the panel's own pages.
    // A panel's first page: named after the hand, with example buttons that start switched off.
    static WheelModel DefaultPage(int h)
    {
        string side = h == 0 ? "Left" : "Right";
        WheelItemModel Ex(string label, ActionModel a) => new() { Label = label, Enabled = false, Action = a };
        return new WheelModel
        {
            Name = side + " hand main",
            Title = side + " hand",
            Items = new()
            {
                Ex("Keyboard", new ActionModel { Type = "keyboard" }),
                Ex("Esc", new ActionModel { Type = "key", Keys = "Esc" }),
                Ex("Map", new ActionModel { Type = "key", Keys = "M" }),
                Ex("Inventory", new ActionModel { Type = "key", Keys = "I" }),
                Ex("Walk toggle", new ActionModel { Type = "key", Keys = "LCtrl", Mode = "toggle" }),
                Ex("Quick save", new ActionModel { Type = "key", Keys = "F5" }),
            },
        };
    }

    void EnsureWristPages()
    {
        for (int h = 0; h < 2; h++)
        {
            var wm = Profile.WristFor(h);
            if (wm.Pages.Count > 0) continue;
            var page = DefaultPage(h);
            wm.Pages[page.Name] = page;
            wm.Start = page.Name;
        }
    }

    // Older profiles: one "wrist_panel" (with a hand), possibly pointing at a wheel. Move it to wrist_left / wrist_right.
    void MigrateWrist()
    {
        var old = Profile.LegacyWrist;
        Profile.LegacyWrist = null;
        if (old == null) return;
        int h = (old.LegacyHand ?? "left").ToLowerInvariant() == "right" ? 1 : 0;
        old.LegacyHand = null;
        old.Mirror = false;
        old.Pages ??= new();
        if (old.Pages.Count == 0 && old.LegacyWheel != null && Profile.Wheels.TryGetValue(old.LegacyWheel, out var w))
        {
            string legacy = old.LegacyWheel;
            bool usedElsewhere = Profile.Bindings.Any(b => b.Action?.Type == "wheel" && b.Action.Wheel == legacy)
                || Profile.Wheels.Values.Any(o => o.Items.Any(i => i.Action?.Type == "wheel" && i.Action.Wheel == legacy));
            var page = usedElsewhere ? Clone(w) : w;
            page.Name = legacy;
            old.Pages[legacy] = page;
            old.Start = legacy;
            if (!usedElsewhere) Profile.Wheels.Remove(legacy);
        }
        old.LegacyWheel = null;
        if (h == 0) Profile.WristLeft = old; else { Profile.WristRight = old; }
    }

    string Serialize()
    {
        var wheels = new Dictionary<string, WheelModel>();
        foreach (var w in Profile.Wheels.Values) wheels[w.Name] = w;
        Profile.Wheels = wheels;
        foreach (var wm in new[] { Profile.WristLeft, Profile.WristRight })
        {
            var pages = new Dictionary<string, WheelModel>();
            foreach (var w in wm.Pages.Values) pages[w.Name] = w;
            wm.Pages = pages;
        }
        return JsonSerializer.Serialize(Profile, JsonOpts);
    }

    bool Save()
    {
        if (Profile == null) return false;
        var names = Profile.Wheels.Values.GroupBy(w => w.Name).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (names.Count > 0) { MessageBox.Show(this, "Two wheels share the name " + names[0] + "."); return false; }
        var pnames = Profile.WristLeft.Pages.Values.Concat(Profile.WristRight.Pages.Values).GroupBy(w => w.Name).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (pnames.Count > 0) { MessageBox.Show(this, "Two wrist pages share the name " + pnames[0] + "."); return false; }
        string json = Serialize();
        var err = Native.Validate(json);
        if (err != null && MessageBox.Show(this, "The layer would reject this profile:\n\n" + err + "\n\nSave anyway? (the game keeps the last good version until fixed)", "Problem", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return false;
        File.WriteAllText(currentFile, json);
        NoteFileTime();
        dirty = false;
        Text = "OpenXR Hand Menus - Profile Editor - " + Path.GetFileName(currentFile);
        SetStatus(err == null ? "Saved. A running game reloads it within a second." : "Saved with errors: " + err, err != null);
        BuildTree();
        return true;
    }

    void NewProfile()
    {
        if (!ConfirmDiscard()) return;
        var seen = Directory.Exists(Path.Combine(DataDir, "logs"))
            ? Directory.GetFiles(Path.Combine(DataDir, "logs"), "*.log").Select(Path.GetFileNameWithoutExtension).ToArray() : Array.Empty<string>();
        using var dlg = new NewProfileDialog(seen);
        if (dlg.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(dlg.ExeName)) return;
        string name = dlg.ExeName.Trim().ToLowerInvariant();
        if (name.EndsWith(".exe")) name = name[..^4];
        string path = Path.Combine(ProfilesDir, name + ".json");
        if (File.Exists(path)) { MessageBox.Show(this, "A profile for " + name + " already exists."); RefreshProfileList(name + ".json"); return; }
        File.Copy(dlg.CopyFrom != null ? Path.Combine(ProfilesDir, dlg.CopyFrom) : Path.Combine(ProfilesDir, "default.json"), path);
        RefreshProfileList(name + ".json");
    }

    void DeleteProfile()
    {
        if (Path.GetFileName(currentFile) == "default.json") { MessageBox.Show(this, "default.json is used for every game without its own profile and can't be deleted."); return; }
        if (MessageBox.Show(this, "Delete " + Path.GetFileName(currentFile) + "? The game will fall back to default.json.", "Delete", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        File.Delete(currentFile);
        dirty = false;
        RefreshProfileList("default.json");
    }

    // ------------------------------------------------------------ tree
    readonly TreeNode[] bindingRoots = new TreeNode[2]; // [0] left controller, [1] right controller
    TreeNode wheelsRoot;
    readonly TreeNode[] wristRoots = new TreeNode[2];

    void BuildTree()
    {
        subDepth.Clear();
        if (Profile == null) return;
        object sel = tree.SelectedNode?.Tag;
        string q = Q;
        tree.BeginUpdate();
        tree.Nodes.Clear();
        var overview = tree.Nodes.Add("Overview");
        overview.Tag = "overview";
        var settings = tree.Nodes.Add("Settings");
        settings.Tag = Profile;
        // Bindings grouped by the controller whose button fires them.
        foreach (int h in new[] { 1, 0 })
        {
            bindingRoots[h] = tree.Nodes.Add(h == 1 ? "Right controller" : "Left controller");
            bindingRoots[h].Tag = h == 1 ? "bindings_right" : "bindings_left";
            foreach (var bd in Profile.Bindings.Where(x => HandOf(x) == h))
                if (Matches(q, bd.Name, bd.Input, InputText.Friendly(bd.Input), Plain(bd.Action), bd.Action?.Keys, bd.Action?.Wheel))
                    bindingRoots[h].Nodes.Add(new TreeNode(bd.Name) { Tag = bd });
            Slot(bindingRoots[h], "Add binding");
        }
        wheelsRoot = tree.Nodes.Add("Wheels");
        wheelsRoot.Tag = "wheels";
        AddPages(wheelsRoot, Profile.Wheels.Values, q);
        Slot(wheelsRoot, "New wheel or sub-wheel");
        for (int h = 0; h < 2; h++)
        {
            wristRoots[h] = tree.Nodes.Add(h == 0 ? "Left wrist panel" : "Right wrist panel");
            wristRoots[h].Tag = h == 0 ? "wrist0" : "wrist1"; // literals, so the selection survives a rebuild
            AddPages(wristRoots[h], Profile.WristFor(h).Pages.Values, q);
            Slot(wristRoots[h], "New sub-page");
        }
        ApplyFolds();
        RefreshNodeTexts();
        tree.EndUpdate();
        TreeNode found = Find(tree.Nodes, sel);
        tree.SelectedNode = found ?? overview;
    }

    void AddPages(TreeNode root, IEnumerable<WheelModel> pages, string q)
    {
        // order: each main wheel/page followed by its sub-wheels (depth-first), then anything left over
        var all = pages.ToList();
        bool wrist = root != wheelsRoot;
        WheelModel Find(string name) => all.FirstOrDefault(x => x.Name == name);
        IEnumerable<WheelModel> Children(WheelModel w) => w.Items
            .Select(i => wrist ? (i.Action?.Type == "page" ? i.Action.Page : null) : (i.Action?.Type == "wheel" ? i.Action.Wheel : null))
            .Where(nm => nm != null).Distinct().Select(Find).Where(x => x != null && x != w);
        var parentOf = new Dictionary<WheelModel, WheelModel>();
        foreach (var w in all) foreach (var c in Children(w)) parentOf.TryAdd(c, w);
        int startH = root == wristRoots[0] ? 0 : 1;
        bool IsMain(WheelModel w) => wrist ? w.Name == Profile.WristFor(startH).Start || !parentOf.ContainsKey(w)
                                           : Profile.Bindings.Any(b => b.Action?.Type == "wheel" && b.Action.Wheel == w.Name) || !parentOf.ContainsKey(w);
        var ordered = new List<WheelModel>(); var seen = new HashSet<WheelModel>();
        void Visit(WheelModel w, int d)
        {
            if (!seen.Add(w)) return;
            ordered.Add(w); subDepth[w] = Math.Min(d, 3);
            foreach (var c in Children(w)) if (!IsMain(c)) Visit(c, d + 1);
        }
        foreach (var w in all) if (IsMain(w)) Visit(w, 0);
        foreach (var w in all) Visit(w, 0); // cycles with no main wheel
        foreach (var w in ordered)
        {
            bool self = Matches(q, w.Name, w.Title);
            var items = w.Items.Where(i => self || Matches(q, i.Label, Plain(i.Action), i.Action?.Keys)).ToList();
            if (!self && items.Count == 0) continue;
            var wn = root.Nodes.Add(w.Name);
            wn.Tag = w;
            foreach (var i in items) wn.Nodes.Add(new TreeNode(i.Label) { Tag = i });
            Slot(wn, root == wheelsRoot ? "Add item" : "Add button", root == wheelsRoot ? "Add sub-wheel" : "Add sub-page");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct TVITEM { public int mask; public IntPtr hItem; public int state; public int stateMask; public IntPtr pszText; public int cchTextMax; public int iImage; public int iSelectedImage; public int cChildren; public IntPtr lParam; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, ref TVITEM l);

    void HideChecks(TreeNodeCollection nodes) { } // the list is drawn by hand now (no checkboxes)
    void HideChecksOld(TreeNodeCollection nodes)
    {
        foreach (TreeNode n in nodes)
        {
            if (n.Tag is not (BindingModel or WheelItemModel))
            {
                var it = new TVITEM { mask = 0x8 /*TVIF_STATE*/, hItem = n.Handle, stateMask = 0xF000 /*TVIS_STATEIMAGEMASK*/, state = 0 };
                SendMessage(tree.Handle, 0x113F /*TVM_SETITEMW*/, IntPtr.Zero, ref it);
            }
            HideChecks(n.Nodes);
        }
    }

    static TreeNode Find(TreeNodeCollection nodes, object tag)
    {
        if (tag == null) return null;
        foreach (TreeNode n in nodes) { if (ReferenceEquals(n.Tag, tag)) return n; var f = Find(n.Nodes, tag); if (f != null) return f; }
        return null;
    }

    void RefreshNodeTexts()
    {
        if (wheelsRoot == null) return;
        tree.Invalidate(); // rows are drawn from the live model
    }

    WheelModel SelectedWheel(out TreeNode wheelNode)
    {
        wheelNode = tree.SelectedNode;
        if (wheelNode?.Tag is WheelItemModel) wheelNode = wheelNode.Parent;
        return wheelNode?.Tag as WheelModel;
    }

    void Add()
    {
        var n = tree.SelectedNode;
        if (n == null) return;
        if (n == bindingRoots[0] || n == bindingRoots[1] || n.Tag is BindingModel)
        {
            int hand = n == bindingRoots[0] ? 0 : n == bindingRoots[1] ? 1 : HandOf((BindingModel)n.Tag);
            var b = new BindingModel { Name = "New binding", Input = hand == 0 ? "left.x" : "right.a", Gesture = "press", Action = new ActionModel { Type = "key", Keys = "F1" } };
            int at = n.Tag is BindingModel cur ? Profile.Bindings.IndexOf(cur) + 1 : Profile.Bindings.Count;
            Profile.Bindings.Insert(at, b);
            Rebuild(b);
        }
        else if (n == wristRoots[0] || n == wristRoots[1])
        {
            int h = n == wristRoots[0] ? 0 : 1;
            var owner = Profile.WristFor(h);
            var pages = owner.Pages;
            WheelModel w;
            if (pages.Count == 0) { w = DefaultPage(h); owner.Start = w.Name; }
            else
            {
                string name = "page" + (pages.Count + 1);
                while (pages.ContainsKey(name)) name += "_";
                w = new WheelModel { Name = name, Title = name };
            }
            string name0 = w.Name;
            pages[name0] = w;
            Rebuild(w);
        }
        else if (n == wheelsRoot)
        {
            string name = "wheel" + (Profile.Wheels.Count + 1);
            while (Profile.Wheels.ContainsKey(name)) name += "_";
            var w = new WheelModel { Name = name, Title = name };
            Profile.Wheels[name] = w;
            Rebuild(w);
        }
        else if (SelectedWheel(out _) is WheelModel w)
        {
            bool wristPage = InWrist(n);
            if (w.Items.Count >= 16) { MessageBox.Show(this, wristPage ? "A page holds at most 16 buttons. Add another page." : "A wheel holds at most 16 items. Use a submenu wheel."); return; }
            var it = new WheelItemModel { Label = wristPage ? "New button" : "New item", Action = new ActionModel { Type = "key", Keys = "F1" } };
            int at = n.Tag is WheelItemModel ci ? w.Items.IndexOf(ci) + 1 : w.Items.Count;
            w.Items.Insert(at, it);
            Rebuild(it);
        }
    }

    // A new sub-wheel (or wrist sub-page) plus the item on this wheel/page that opens it; the new one starts with a Back item.
    void AddSub(TreeNode wheelNode)
    {
        if (wheelNode.Tag is not WheelModel w) return;
        bool wrist = InWrist(wheelNode);
        if (w.Items.Count >= 16) { MessageBox.Show(this, wrist ? "This page is full (16 buttons)." : "This wheel is full (16 items)."); return; }
        var dict = DictFor(wheelNode);
        string name = w.Name + (wrist ? " sub-page" : " sub-wheel");
        for (int k = 2; dict.ContainsKey(name); k++) name = w.Name + (wrist ? " sub-page " : " sub-wheel ") + k;
        var sub = new WheelModel { Name = name, Title = "More" };
        sub.Items.Add(new WheelItemModel { Label = "Back", Action = new ActionModel { Type = "back" } });
        dict[name] = sub; unfolded.Add(sub); unfolded.Add(w);
        w.Items.Add(new WheelItemModel { Label = "More", Action = wrist ? new ActionModel { Type = "page", Page = name } : new ActionModel { Type = "wheel", Wheel = name } });
        Rebuild(sub);
    }

    static T Clone<T>(T o) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(o, JsonOpts), JsonOpts);

    void Duplicate()
    {
        var n = tree.SelectedNode;
        switch (n?.Tag)
        {
            case BindingModel b: { var c = Clone(b); c.Name += " copy"; Profile.Bindings.Insert(Profile.Bindings.IndexOf(b) + 1, c); Rebuild(c); break; }
            case WheelItemModel i: { var w = (WheelModel)n.Parent.Tag; var c = Clone(i); w.Items.Insert(w.Items.IndexOf(i) + 1, c); Rebuild(c); break; }
            case WheelModel w:
                {
                    var dict = DictFor(n);
                    var c = Clone(w); c.Name = w.Name + "_copy";
                    while (dict.ContainsKey(c.Name)) c.Name += "_";
                    dict[c.Name] = c; Rebuild(c); break;
                }
        }
    }

    void Remove()
    {
        var n = tree.SelectedNode;
        switch (n?.Tag)
        {
            case BindingModel b: Profile.Bindings.Remove(b); Rebuild(null); break;
            case WheelItemModel i: ((WheelModel)n.Parent.Tag).Items.Remove(i); Rebuild(n.Parent.Tag); break;
            case WheelModel w:
                {
                    var dict = DictFor(n);
                    if (MessageBox.Show(this, InWrist(n) ? $"Delete wrist page {w.Name} and its {w.Items.Count} buttons?" : $"Delete wheel {w.Name} and its {w.Items.Count} items?", "Delete", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
                    foreach (var k in dict.Where(kv => ReferenceEquals(kv.Value, w)).Select(kv => kv.Key).ToList()) dict.Remove(k);
                }
                Rebuild(null); break;
        }
    }

    void MoveItem(int d)
    {
        var n = tree.SelectedNode;
        void Swap<T>(List<T> l, T x) { int i = l.IndexOf(x), j = i + d; if (j < 0 || j >= l.Count) return; (l[i], l[j]) = (l[j], l[i]); Rebuild(x); }
        if (n?.Tag is BindingModel b)
        {
            var same = Profile.Bindings.Where(x => HandOf(x) == HandOf(b)).ToList();
            int i = same.IndexOf(b), j = i + d;
            if (j < 0 || j >= same.Count) return;
            int a1 = Profile.Bindings.IndexOf(b), a2 = Profile.Bindings.IndexOf(same[j]);
            (Profile.Bindings[a1], Profile.Bindings[a2]) = (Profile.Bindings[a2], Profile.Bindings[a1]);
            Rebuild(b);
        }
        else if (n?.Tag is WheelItemModel i) Swap(((WheelModel)n.Parent.Tag).Items, i);
    }

    void Rebuild(object select)
    {
        MarkDirty();
        BuildTree();
        var f = Find(tree.Nodes, select);
        if (f != null) tree.SelectedNode = f;
    }

    // ------------------------------------------------------------ install
    static string ScriptDir => AppContext.BaseDirectory;

    void UpdateLayerStatus()
    {
        string manifest = Path.Combine(ScriptDir, LayerJson);
        bool installed = false, other = false;
        using (var k = Registry.LocalMachine.OpenSubKey(ImplicitKey))
            if (k != null)
                foreach (var v in k.GetValueNames())
                    if (v.EndsWith(LayerJson, StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.Equals(v, manifest, StringComparison.OrdinalIgnoreCase) && k.GetValue(v) is int i && i == 0) installed = true;
                        else other = true;
                    }
        layerStatus.Text = installed ? "  Layer: installed" : other ? "  Layer: installed from another folder" : "  Layer: NOT installed";
        layerStatus.ForeColor = installed ? Color.ForestGreen : Color.Firebrick;
    }

    void RunScript(string name)
    {
        string script = Path.Combine(ScriptDir, name);
        if (!File.Exists(script) || !File.Exists(Path.Combine(ScriptDir, LayerJson)) || !File.Exists(Path.Combine(ScriptDir, "OpenXRHandMenus.dll")))
        {
            MessageBox.Show(this, "The layer files aren't next to the editor, so it can't install the layer.\n\n" +
                "Looked in: " + ScriptDir + "\n\n" +
                "Extract the WHOLE zip first (right-click it > Extract All), then run OpenXRHandMenusEditor.exe from the extracted folder. " +
                "It needs install.ps1, OpenXRHandMenus.dll and " + LayerJson + " in the same folder. " +
                "Running the exe straight from inside the zip doesn't work.", "Layer files not found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        try
        {
            var p = Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\"") { Verb = "runas", UseShellExecute = true });
            p?.WaitForExit();
        }
        catch (Exception e) { MessageBox.Show(this, e.Message); }
        UpdateLayerStatus();
    }
}

class NewProfileDialog : Form
{
    readonly ComboBox exe = new() { Width = 300 };
    readonly ComboBox copy = new() { Width = 300, DropDownStyle = ComboBoxStyle.DropDownList };
    public string ExeName => exe.Text;
    public string CopyFrom => copy.SelectedItem as string;

    public NewProfileDialog(string[] seenExes)
    {
        Text = "New game profile";
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; Padding = new Padding(10);
        var t = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, Dock = DockStyle.Fill };
        t.Controls.Add(new Label { Text = "Game exe name:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        exe.Items.AddRange(seenExes);
        t.Controls.Add(exe, 1, 0);
        var browse = new Button { Text = "Browse...", AutoSize = true };
        browse.Click += (s, e) => { using var d = new OpenFileDialog { Filter = "Programs|*.exe" }; if (d.ShowDialog(this) == DialogResult.OK) exe.Text = Path.GetFileNameWithoutExtension(d.FileName); };
        t.Controls.Add(browse, 2, 0);
        t.Controls.Add(new Label { Text = "Start from:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        foreach (var f in Directory.GetFiles(MainForm.ProfilesPath, "*.json"))
            copy.Items.Add(Path.GetFileName(f));
        copy.SelectedItem = "default.json";
        t.Controls.Add(copy, 1, 1);
        t.Controls.Add(new Label { Text = "Games the layer has already seen are in the list (from its logs).", AutoSize = true, ForeColor = Color.DimGray }, 1, 2);
        var ok = new Button { Text = "Create", DialogResult = DialogResult.OK, AutoSize = true };
        t.Controls.Add(ok, 1, 3);
        AcceptButton = ok;
        Controls.Add(t);
    }
}
