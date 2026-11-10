using System.Drawing.Drawing2D;

namespace VRRadialOverlayEditor;

// The list on the left: drawn by hand so each row has an icon, a bold name, a plain-English
// description and a big on/off dot; sections are coloured bands with a status and "+ Add".
// An "+ Add ..." row at the end of a group.
public sealed class AddSlot { public string Text, Sub; } // Sub: optional right half ("Add sub-wheel")

public partial class MainForm
{
    // Adds an empty "+ Add ..." row under a group (not while searching).
    void Slot(TreeNode parent, string text, string sub = null)
    {
        if (!string.IsNullOrWhiteSpace(Q)) return;
        parent.Nodes.Add(new TreeNode(text) { Tag = new AddSlot { Text = text, Sub = sub } });
    }
    Rectangle SlotRect(TreeNode n)
    {
        int indent = Indent(n);
        var row = n.Bounds; int W = tree.ClientSize.Width;
        return new Rectangle(indent + 2, row.Y + 7, W - indent - 14, row.Height - 14);
    }

    // Sub-wheels / sub-pages (and their items) are shifted right under their parent.
    const int SubShift = 26;
    readonly Dictionary<WheelModel, int> subDepth = new();
    int SubDepthOf(TreeNode n)
    {
        var w = n.Tag as WheelModel ?? n.Parent?.Tag as WheelModel;
        return w != null && subDepth.TryGetValue(w, out int d) ? d : 0;
    }
    int Indent(TreeNode n) => 10 + n.Level * 22 + SubShift * SubDepthOf(n);

    static int RealCount(TreeNode n) => n.Nodes.Cast<TreeNode>().Count(c => c.Tag is not AddSlot);

    readonly TextBox search = new() { Dock = DockStyle.Top, PlaceholderText = "Search: name, key or button (e.g. F5, thumbrest)", Font = new Font("Segoe UI", 10.5f), Margin = new Padding(4) };
    TreeNode dropNode; bool dropAfter; TreeNode dropWheel;
    readonly HashSet<object> folded = new();   // sections the user folded (sections start open)
    readonly HashSet<object> unfolded = new(); // wheels / pages the user opened (they start folded)
    bool rebuilding;

    static readonly Color SecRight = Color.FromArgb(40, 110, 200), SecLeft = Color.FromArgb(150, 80, 190), SecWheels = Color.FromArgb(230, 130, 30),
                          SecWrist = Color.FromArgb(30, 150, 130), SecNav = Color.FromArgb(90, 100, 120);
    static readonly Color OnC = Color.FromArgb(46, 158, 91), OffC = Color.FromArgb(175, 175, 175), MirC = Color.FromArgb(60, 120, 220);
    static readonly Font fTitle = new("Segoe UI", 10.5f, FontStyle.Bold), fSub = new("Segoe UI", 9f), fHead = new("Segoe UI", 10f, FontStyle.Bold),
                         fIcon = new("Segoe MDL2 Assets", 13f), fPill = new("Segoe UI", 8.5f, FontStyle.Bold), fTitleOff = new("Segoe UI", 10.5f, FontStyle.Bold | FontStyle.Italic);

    void SetupTreeLook()
    {
        tree.DrawMode = TreeViewDrawMode.OwnerDrawAll;
        tree.ItemHeight = 46;
        tree.ShowLines = false; tree.ShowPlusMinus = false; tree.ShowRootLines = false;
        tree.FullRowSelect = true; tree.Indent = 22;
        tree.BorderStyle = BorderStyle.None;
        tree.BackColor = Color.FromArgb(248, 249, 251);
        tree.DrawNode += DrawRow;
        tree.BeforeSelect += (s, e) => { if (e.Node?.Tag is AddSlot) e.Cancel = true; };
        tree.NodeMouseClick += (s, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            if (e.Node.Tag is AddSlot)
            {
                // add next to the slot: select its group, then the usual Add for that group
                var owner = e.Node.Parent;
                if (owner == null) return;
                var sr = SlotRect(e.Node);
                if (((AddSlot)e.Node.Tag).Sub != null && e.X >= sr.X + sr.Width / 2) { AddSub(owner); return; }
                tree.SelectedNode = owner; Add();
                return;
            }
            int w = tree.ClientSize.Width;
            if (Toggleable(e.Node.Tag) && e.X >= w - 46) { ToggleNode(e.Node); return; }
            if (IsSection(e.Node))
            {
                if (e.X >= w - 70) { tree.SelectedNode = e.Node; Add(); return; }
                if (e.X < 34) Fold(e.Node); // only the arrow folds; clicking the rest of the header just selects it
                return;
            }
            if (RealCount(e.Node) > 0 && e.X < Indent(e.Node) + 2) Fold(e.Node); // the arrow left of a wheel or page
        };
        // no double-click / keyboard folding: only the arrows fold (code-driven changes have Action Unknown)
        tree.BeforeCollapse += (s, e) => { if (UserToggle()) e.Cancel = true; };
        tree.BeforeExpand += (s, e) => { if (UserToggle()) e.Cancel = true; };
        tree.KeyDown += (s, e) => arrowKey = e.KeyCode is Keys.Left or Keys.Right or Keys.Add or Keys.Subtract or Keys.Multiply;
        tree.KeyUp += (s, e) => arrowKey = false;
        Shown += (s, e) => { ApplyFolds(); if (tree.Nodes.Count > 0) tree.TopNode = tree.Nodes[0]; tree.Invalidate(); BeginInvoke(new Action(OfferTutorialOnFirstRun)); }; // the first build can happen before the list exists on screen
        tree.AfterCollapse += (s, e) => { if (folding && !rebuilding && e.Node.Tag != null) { folded.Add(e.Node.Tag); unfolded.Remove(e.Node.Tag); } tree.Invalidate(); }; // only the user's own folds count
        tree.AfterExpand += (s, e) => { if (!rebuilding && e.Node.Tag != null) { folded.Remove(e.Node.Tag); unfolded.Add(e.Node.Tag); } tree.Invalidate(); };
        var t = new System.Windows.Forms.Timer { Interval = 250 };
        t.Tick += (s, e) => { t.Stop(); BuildTree(); };
        search.TextChanged += (s, e) => { t.Stop(); t.Start(); };
        tree.SizeChanged += (s, e) => tree.Invalidate();
    }

    bool folding, arrowKey;
    // a double-click or arrow key folding a row (we only fold through the arrows); code-driven expands (selecting a hidden item) are fine
    bool UserToggle() => !folding && !rebuilding && (Control.MouseButtons != MouseButtons.None || arrowKey);
    void Fold(TreeNode n)
    {
        if (RealCount(n) == 0) return;
        folding = true;
        try { if (n.IsExpanded) n.Collapse(); else n.Expand(); } finally { folding = false; }
    }

    // After a rebuild: everything open except what the user folded (searching opens everything).
    void ApplyFolds()
    {
        rebuilding = true;
        try
        {
            tree.ExpandAll();
            if (string.IsNullOrWhiteSpace(Q))
                foreach (TreeNode root in tree.Nodes) ApplyFolds(root);
        }
        finally { rebuilding = false; }
    }
    void ApplyFolds(TreeNode n)
    {
        foreach (TreeNode c in n.Nodes) ApplyFolds(c);
        if (n.Tag is WheelModel ? !unfolded.Contains(n.Tag) : n.Tag != null && folded.Contains(n.Tag)) n.Collapse(false);
        else if (n.Nodes.Count > 0) n.Expand();
    }

    void Chevron(Graphics g, int x, int midY, bool open, Color c)
    {
        using var pen = new Pen(c, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        if (open) g.DrawLines(pen, new[] { new Point(x, midY - 3), new Point(x + 5, midY + 2), new Point(x + 10, midY - 3) });
        else g.DrawLines(pen, new[] { new Point(x + 3, midY - 5), new Point(x + 8, midY), new Point(x + 3, midY + 5) });
    }

    static bool Toggleable(object tag) => tag is BindingModel or WheelItemModel;
    bool IsSection(TreeNode n) => n != null && n.Level == 0 && n.Tag is string s && s is "bindings_right" or "bindings_left" or "wheels" or "wrist0" or "wrist1";

    void ToggleNode(TreeNode n)
    {
        if (n.Tag is BindingModel b) b.Enabled = !b.Enabled;
        else if (n.Tag is WheelItemModel wi) wi.Enabled = !wi.Enabled;
        else return;
        MarkDirty();
        RefreshNodeTexts();
        if (ReferenceEquals(tree.SelectedNode, n)) ShowEditor(n);
    }

    bool Matches(string q, params string[] fields) => string.IsNullOrWhiteSpace(q) || fields.Any(f => f != null && f.Contains(q, StringComparison.OrdinalIgnoreCase));
    string Q => search.Text.Trim();

    // ---- plain-English descriptions
    static string Plain(ActionModel a)
    {
        if (a == null) return "Does nothing";
        string mode = a.Mode switch { "toggle" => " (toggle on/off)", "hold" => " (held while the button is)", _ => "" };
        return a.Type switch
        {
            "key" => $"Presses {a.Keys}{mode}",
            "sequence" => $"Runs a macro: {a.Steps}",
            "wheel" => $"Opens wheel '{a.Wheel}'",
            "page" => $"Opens page '{a.Page}'",
            "keyboard" => "Opens the VR keyboard",
            "layer" => a.Mode switch { "on" => $"Turns layer '{a.Layer}' on", "off" => $"Turns layer '{a.Layer}' off", "toggle" => $"Switches layer '{a.Layer}' on/off", _ => $"Layer '{a.Layer}' while held" },
            "xr_input" => $"Presses game button {a.Target}{mode}",
            "back" => "Goes back",
            "close" => "Closes / hides",
            "reload" => "Reloads the profile",
            _ => "Does nothing",
        };
    }
    static string Gesture(BindingModel b) => b.Gesture switch
    {
        "press" => "press", "tap" => "tap", "hold" => "hold", "double_tap" => "double-tap", "while_held" => "hold down", _ => b.Gesture,
    };
    static int HandOf(BindingModel b) => (b.Input ?? "").Split('+').Last().Trim().StartsWith("left") ? 0 : 1;

    (string title, string sub, string icon, Color accent, string pill, Color pillColor) Describe(TreeNode n)
    {
        switch (n.Tag)
        {
            case "overview": return ("Overview", "What is on and off", "", SecNav, null, default);
            case ProfileModel: return ("Settings", "Look, picking items, keyboard, colours", "", SecNav, null, default);
            case "bindings_right":
            case "bindings_left":
                {
                    int h = (string)n.Tag == "bindings_left" ? 0 : 1;
                    var bs = Profile.Bindings.Where(b => HandOf(b) == h).ToList();
                    return (h == 1 ? "RIGHT CONTROLLER" : "LEFT CONTROLLER", null, null, h == 1 ? SecRight : SecLeft, $"{bs.Count(b => b.Enabled)} of {bs.Count} on", OnC);
                }
            case "wheels": return ("WHEELS (radial menus)", null, null, SecWheels, $"{Profile.Wheels.Count}", SecWheels);
            case "wrist0":
            case "wrist1":
                {
                    int h = (string)n.Tag == "wrist0" ? 0 : 1;
                    var wm = Profile.WristFor(h);
                    string st = !wm.Enabled ? "OFF" : wm.Mirror ? "MIRRORED" : "ON";
                    return (h == 0 ? "LEFT WRIST PANEL" : "RIGHT WRIST PANEL", null, null, SecWrist, st, !wm.Enabled ? OffC : wm.Mirror ? MirC : OnC);
                }
            case BindingModel b:
                {
                    string layer = string.IsNullOrWhiteSpace(b.Layers) ? "" : $"  [only with layer {b.Layers}]";
                    return (b.Name, $"{InputText.Friendly(b.Input)} {Gesture(b)}  →  {Plain(b.Action)}{layer}", "", HandOf(b) == 1 ? SecRight : SecLeft, null, default);
                }
            case WheelModel w:
                {
                    int wh = WristOf(n);
                    if (wh >= 0)
                    {
                        var wm = Profile.WristFor(wh);
                        bool first = w.Name == wm.Start;
                        var from = Profile.WristPages(wh).Pages.Values.SelectMany(pg => pg.Items.Where(i => i.Action?.Type == "page" && i.Action.Page == w.Name).Select(i => $"'{i.Label}' on {pg.Name}")).FirstOrDefault();
                        string note = wm.Mirror ? "not used while this panel mirrors" : first ? "main page, shown first"
                                    : from != null ? "sub-page, opened by " + from : "sub-page, no button opens it yet";
                        return ((first || wm.Mirror ? "" : "↳ ") + (w.Title is { Length: > 0 } t && t != w.Name ? $"{w.Name}  ·  \"{t}\"" : w.Name), $"{w.Items.Count(i => i.Enabled)} of {w.Items.Count} buttons on  ·  {note}", "", SecWrist, null, default);
                    }
                    var openers = Profile.Bindings.Where(b => b.Action?.Type == "wheel" && b.Action.Wheel == w.Name).Select(b => InputText.Friendly(b.Input) + " " + Gesture(b)).ToList();
                    var subFrom = Profile.Wheels.Values.SelectMany(o => o.Items.Where(i => i.Action?.Type == "wheel" && i.Action.Wheel == w.Name).Select(i => $"'{i.Label}' in {o.Name}")).FirstOrDefault();
                    string how = openers.Count > 0 ? "main wheel, opened by " + string.Join(", ", openers) : subFrom != null ? "sub-wheel, opened by " + subFrom : "not opened by anything yet";
                    return ((openers.Count == 0 && subFrom != null ? "↳ " : "") + (w.Title is { Length: > 0 } t2 && t2 != w.Name ? $"{w.Name}  ·  \"{t2}\"" : w.Name), $"{w.Items.Count(i => i.Enabled)} of {w.Items.Count} items on  ·  {how}", "", SecWheels, null, default);
                }
            case AddSlot:
                {
                    var top = n; while (top.Parent != null) top = top.Parent;
                    var acc = top.Tag switch { "bindings_left" => SecLeft, "bindings_right" => SecRight, "wheels" => SecWheels, _ => SecWrist };
                    return (null, null, null, acc, null, default);
                }
            case WheelItemModel i:
                return (i.Label, Plain(i.Action), i.Action?.Type == "keyboard" ? "" : null, InWrist(n) ? SecWrist : SecWheels, null, default);
        }
        return (n.Text, null, null, SecNav, null, default);
    }

    void DrawRow(object sender, DrawTreeNodeEventArgs e)
    {
        var n = e.Node;
        if (n == null || e.Bounds.Height == 0) return;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int W = tree.ClientSize.Width;
        var row = new Rectangle(0, e.Bounds.Y, W, e.Bounds.Height);
        bool sel = ReferenceEquals(tree.SelectedNode, n);
        var d = Describe(n);
        bool section = IsSection(n);
        using (var bg = new SolidBrush(tree.BackColor)) g.FillRectangle(bg, row);

        if (section)
        {
            var band = new Rectangle(4, row.Y + 8, W - 8, row.Height - 10);
            using (var b = new SolidBrush(Color.FromArgb(sel ? 60 : 32, d.accent))) using (var p = RoundRect(band, 8)) g.FillPath(b, p);
            using (var b = new SolidBrush(d.accent)) g.FillRectangle(b, band.X, band.Y + 6, 4, band.Height - 12);
            int real = RealCount(n);
            if (real > 0) Chevron(g, band.X + 12, band.Y + band.Height / 2, n.IsExpanded, Darker(d.accent)); // nothing to fold when empty
            TextRenderer.DrawText(g, d.title, fHead, new Point(band.X + 30, band.Y + (band.Height - fHead.Height) / 2), Darker(d.accent));
            int x = band.X + 34 + TextRenderer.MeasureText(d.title, fHead).Width;
            if (real == 0) x = Pill(g, string.IsNullOrWhiteSpace(Q) ? "empty" : "no matches", OffC, x, band.Y + band.Height / 2) + 6;
            else if (!n.IsExpanded) x = Pill(g, $"{real} hidden", OffC, x, band.Y + band.Height / 2) + 6;
            if (d.pill != null) x = Pill(g, d.pill, d.pillColor, x, band.Y + band.Height / 2) + 6;
            // + Add
            var add = new Rectangle(W - 70, band.Y + 6, 60, band.Height - 12);
            using (var b = new SolidBrush(d.accent)) using (var p = RoundRect(add, 6)) g.FillPath(b, p);
            TextRenderer.DrawText(g, "+ Add", fPill, add, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        int indent = Indent(n);
        int depth = SubDepthOf(n);
        if (depth > 0)
        {
            // sub-wheel block: tinted band + guide line per nesting level, so it reads as part of its parent
            int wx = Indent(n.Tag is WheelModel ? n : n.Parent);
            using (var tb = new SolidBrush(Color.FromArgb(14 * depth, d.accent))) g.FillRectangle(tb, wx - 26, row.Y, W - wx + 22, row.Height);
            for (int k = 1; k <= depth; k++)
            {
                int gx = wx - 22 - (depth - k) * SubShift;
                using var gp = new Pen(Color.FromArgb(200, d.accent), 3f);
                g.DrawLine(gp, gx, row.Y, gx, row.Bottom);
            }
        }
        if (n.Tag is AddSlot slot)
        {
            var full = SlotRect(n);
            var halves = slot.Sub == null ? new[] { (full, slot.Text) }
                : new[] { (new Rectangle(full.X, full.Y, full.Width / 2 - 4, full.Height), slot.Text), (new Rectangle(full.X + full.Width / 2 + 4, full.Y, full.Width - full.Width / 2 - 4, full.Height), slot.Sub) };
            foreach (var (sr, text) in halves)
            {
                using (var b = new SolidBrush(Color.FromArgb(18, d.accent))) using (var p = RoundRect(sr, 7)) g.FillPath(b, p);
                using (var pen = new Pen(Color.FromArgb(150, d.accent), 1.4f) { DashStyle = DashStyle.Dash }) using (var p = RoundRect(sr, 7)) g.DrawPath(pen, p);
                TextRenderer.DrawText(g, "+  " + text, fTitle, sr, Darker(d.accent), TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.LeftAndRightPadding | TextFormatFlags.EndEllipsis);
            }
            return;
        }
        bool enabled = n.Tag switch { BindingModel b => b.Enabled, WheelItemModel i => i.Enabled, _ => true };
        if (sel)
        {
            using var b = new SolidBrush(Color.FromArgb(222, 234, 252));
            using var p = RoundRect(new Rectangle(indent - 6, row.Y + 2, W - indent, row.Height - 4), 8);
            g.FillPath(b, p);
            using var a = new SolidBrush(d.accent);
            g.FillRectangle(a, indent - 6, row.Y + 8, 4, row.Height - 16);
        }
        if (RealCount(n) > 0) Chevron(g, indent - 13, row.Y + row.Height / 2, n.IsExpanded, Color.FromArgb(120, 125, 135));
        // icon (or colour swatch for items with a colour)
        var ic = new Rectangle(indent + 2, row.Y + 9, 28, 28);
        Color iconCol = enabled ? d.accent : OffC;
        if (n.Tag is WheelItemModel wi && WheelPreview.TryColor(wi.Color, out var sw))
        {
            using var b = new SolidBrush(Color.FromArgb(255, sw)); using var p = RoundRect(ic, 7); g.FillPath(b, p);
        }
        else if (d.icon != null)
        {
            using (var b = new SolidBrush(Color.FromArgb(28, iconCol))) using (var p = RoundRect(ic, 7)) g.FillPath(b, p);
            TextRenderer.DrawText(g, d.icon, fIcon, ic, iconCol, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
        else
        {
            using var b = new SolidBrush(Color.FromArgb(enabled ? 60 : 30, iconCol));
            using var p = RoundRect(new Rectangle(ic.X + 7, ic.Y + 7, 14, 14), 4); g.FillPath(b, p);
        }
        int tx = ic.Right + 10, tw = W - tx - (Toggleable(n.Tag) ? 54 : 10);
        var titleCol = enabled ? Color.FromArgb(30, 32, 36) : Color.FromArgb(150, 150, 150);
        var flags = TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        TextRenderer.DrawText(g, d.title, enabled ? fTitle : fTitleOff, new Rectangle(tx, row.Y + 4, tw, 20), titleCol, flags);
        if (d.sub != null)
            TextRenderer.DrawText(g, (enabled ? "" : "Off  ·  ") + d.sub, fSub, new Rectangle(tx, row.Y + 24, tw, 18), Color.FromArgb(110, 115, 125), flags);
        // on/off dot
        if (Toggleable(n.Tag))
        {
            var dot = new Rectangle(W - 38, row.Y + 13, 20, 20);
            if (enabled) { using var b = new SolidBrush(OnC); g.FillEllipse(b, dot); using var pen = new Pen(Color.White, 2.2f); g.DrawLines(pen, new[] { new Point(dot.X + 5, dot.Y + 10), new Point(dot.X + 9, dot.Y + 14), new Point(dot.X + 15, dot.Y + 6) }); }
            else { using var pen = new Pen(OffC, 2f); g.DrawEllipse(pen, dot); }
        }
        // drag and drop marks
        if (ReferenceEquals(dropWheel, n)) { using var pen = new Pen(SecWheels, 2f); using var p = RoundRect(new Rectangle(indent - 6, row.Y + 2, W - indent, row.Height - 5), 8); g.DrawPath(pen, p); }
        if (ReferenceEquals(dropNode, n)) { using var b = new SolidBrush(Color.FromArgb(40, 90, 170)); g.FillRectangle(b, indent, dropAfter ? row.Bottom - 3 : row.Y, W - indent - 8, 3); }
    }

    static int Pill(Graphics g, string text, Color c, int x, int midY)
    {
        var sz = TextRenderer.MeasureText(text, fPill);
        var r = new Rectangle(x, midY - 10, sz.Width + 8, 20);
        using (var b = new SolidBrush(c)) using (var p = RoundRect(r, 10)) g.FillPath(b, p);
        TextRenderer.DrawText(g, text, fPill, r, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        return r.Right;
    }
    static Color Darker(Color c) => Color.FromArgb(c.R * 7 / 10, c.G * 7 / 10, c.B * 7 / 10);
    static GraphicsPath RoundRect(Rectangle r, int d)
    {
        var p = new GraphicsPath();
        d = Math.Max(1, Math.Min(d * 2, Math.Min(r.Width, r.Height)));
        p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure(); return p;
    }
}
