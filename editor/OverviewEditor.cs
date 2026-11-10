namespace VRHandMenusEditor;

// Start page: one card per feature with a clear ON / MIRRORED / OFF status and the switches for it.
public class OverviewEditor : EditorBase
{
    static readonly Color On = Color.FromArgb(46, 158, 91), Off = Color.FromArgb(150, 150, 150), Mirrored = Color.FromArgb(60, 120, 220);
    readonly ProfileModel p;
    readonly Action<object> go;
    readonly FlowLayoutPanel cards = new() { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Dock = DockStyle.Top };

    public OverviewEditor(ProfileModel model, Action<object> goTo)
    {
        p = model; go = goTo;
        Stack(Title("Overview"),
              FormPanel.Header("What is switched on right now. Click a switch to change it, or a blue button to jump to its details (Back returns here). Remember to Save (Ctrl+S)."),
              cards);
        Build();
    }

    static string Hand(BindingModel b) => (b.Input ?? "").Split('+').Last().Trim().StartsWith("left") ? "left" : "right";

    void Build()
    {
        cards.SuspendLayout();
        cards.Controls.Clear();

        // Overlay master switch
        {
            var c = Card("Overlay for this game", p.Enabled ? "ON" : "OFF", p.Enabled ? On : Off,
                "Everything below only works while this is on.", null, MiniPics.Headset(96, 72, p.Enabled));
            c.Body.Controls.Add(Switch("Overlay is on", p.Enabled, v => p.Enabled = v));
            c.Go("Settings (look, picking items)", () => go(p));
        }

        // Wheels per hand
        foreach (var hand in new[] { "right", "left" })
        {
            var openers = p.Bindings.Where(b => b.Action?.Type == "wheel" && Hand(b) == hand).ToList();
            bool any = openers.Any(b => b.Enabled);
            string title = hand == "right" ? "Right controller wheel" : "Left controller wheel";
            var c = Card(title, any ? "ON" : "OFF", any ? On : Off,
                openers.Count == 0 ? $"No binding opens a wheel from the {hand} controller. Add one under Bindings (action: Open a wheel)."
                                   : "Radial menus opened by these buttons:",
                openers.FirstOrDefault() ?? (object)"bindings",
                MiniPics.Wheel(96, 72, hand == "left" ? 0 : 1, openers.Select(b => p.Wheels.GetValueOrDefault(b.Action.Wheel ?? "")).FirstOrDefault(w => w != null), any));
            foreach (var b in openers)
                c.Body.Controls.Add(Switch($"{InputText.Friendly(b.Input)}  {GestureWord(b)}  ->  wheel '{b.Action.Wheel}'", b.Enabled, v => b.Enabled = v));
            foreach (var wn in openers.Select(b => b.Action.Wheel).Distinct())
                if (wn != null && p.Wheels.TryGetValue(wn, out var wheel)) c.Go($"Edit wheel '{wn}'", () => go(wheel));
            foreach (var b in openers) c.Go(openers.Count > 1 ? $"Edit binding '{b.Name}'" : "Edit the button that opens it", () => go(b));
            if (openers.Count == 0) c.Go("Go to Bindings", () => go("bindings"));
        }

        // Wrist panels
        for (int h = 0; h < 2; h++)
        {
            var wm = p.WristFor(h);
            string side = h == 0 ? "Left" : "Right", other = h == 0 ? "right" : "left";
            string state = !wm.Enabled ? "OFF" : wm.Mirror ? "MIRRORED" : "ON";
            Color col = !wm.Enabled ? Off : wm.Mirror ? Mirrored : On;
            var src = p.WristPages(h);
            int buttons = src.Pages.TryGetValue(src.Start ?? "", out var first) ? first.Items.Count(i => i.Enabled) : 0;
            string desc = !wm.Enabled ? $"Hidden. Turn it on to get buttons on your {side.ToLower()} forearm."
                : wm.Mirror ? $"Shows the {other} panel's pages ({buttons} buttons on its first page). Pressed with your {other} hand."
                : $"Own pages: {src.Pages.Count} page(s), {buttons} buttons on the first. Pressed with your {other} hand.";
            var c = Card($"{side} wrist panel", state, col, desc, h == 0 ? "wrist0" : "wrist1", MiniPics.Wrist(96, 72, h, first, state));
            var mode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
            mode.Items.AddRange(new object[] { "Off", "On, own pages", $"On, mirror the {other} panel" });
            mode.SelectedIndex = !wm.Enabled ? 0 : wm.Mirror ? 2 : 1;
            mode.SelectedIndexChanged += (s, e) =>
            {
                wm.Enabled = mode.SelectedIndex != 0;
                wm.Mirror = mode.SelectedIndex == 2;
                if (wm.Mirror) p.WristFor(1 - h).Mirror = false; // they can't both mirror
                Fire(); BeginInvoke(Build);
            };
            c.Body.Controls.Add(mode);
            string tag = h == 0 ? "wrist0" : "wrist1";
            c.Go("Panel settings (size, side, text)", () => go(tag));
            if (src.Pages.TryGetValue(src.Start ?? "", out var fp)) c.Go(wm.Mirror ? $"Edit the {other} panel's buttons" : "Edit its buttons", () => go(fp));
        }

        // Keyboard
        {
            var openers = p.Bindings.Where(b => b.Action?.Type == "keyboard").ToList();
            int fromMenus = p.Wheels.Values.Concat(p.WristLeft.Pages.Values).Concat(p.WristRight.Pages.Values)
                .SelectMany(w => w.Items).Count(i => i.Enabled && i.Action?.Type == "keyboard");
            bool any = openers.Any(b => b.Enabled) || fromMenus > 0;
            var c = Card("VR keyboard", any ? "ON" : "OFF", any ? On : Off,
                fromMenus > 0 ? $"Also opened by {fromMenus} wheel or wrist button(s)." : "Point and pull the trigger to type.",
                openers.FirstOrDefault() ?? (object)"bindings", MiniPics.Keyboard(96, 72, any));
            foreach (var b in openers)
                c.Body.Controls.Add(Switch($"{InputText.Friendly(b.Input)}  {GestureWord(b)}", b.Enabled, v => b.Enabled = v));
            foreach (var b in openers) c.Go("Edit the button that opens it", () => go(b));
            c.Go("Keyboard size and position", () => go(p));
        }

        // Everything else
        {
            var rest = p.Bindings.Where(b => b.Action?.Type is not ("wheel" or "keyboard")).ToList();
            int on = rest.Count(b => b.Enabled);
            var c = Card("Other bindings", $"{on} of {rest.Count} on", on > 0 ? On : Off,
                "Keys, macros, layers and game inputs on controller buttons.", rest.FirstOrDefault() ?? (object)"bindings", MiniPics.Controllers(96, 72, on > 0));
            foreach (var b in rest)
                c.Body.Controls.Add(Switch(b.Name, b.Enabled, v => b.Enabled = v));
            c.Go("Go to Bindings", () => go("bindings"));
        }
        cards.ResumeLayout();
    }

    static string GestureWord(BindingModel b) =>
        Choices.Gestures.FirstOrDefault(c => c.Code == b.Gesture)?.Text.Split(' ')[0].ToLowerInvariant() ?? b.Gesture;

    CheckBox Switch(string text, bool value, Action<bool> set)
    {
        var c = new CheckBox { Text = text, AutoSize = true, Checked = value, Margin = new Padding(2, 2, 2, 2) };
        c.CheckedChanged += (s, e) => { set(c.Checked); Fire(); BeginInvoke(Build); };
        return c;
    }

    class CardPanel : Panel
    {
        public FlowLayoutPanel Body, Buttons;
        public void Go(string text, Action click)
        {
            var b = new Button { Text = text + "  \u25B6", AutoSize = true, Height = 30, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(40, 90, 170), ForeColor = Color.White, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), Margin = new Padding(0, 0, 8, 4), Padding = new Padding(6, 0, 6, 0), Cursor = Cursors.Hand };
            b.FlatAppearance.BorderSize = 0;
            b.Click += (s, e) => click();
            Buttons.Controls.Add(b);
        }
    }

    CardPanel Card(string title, string state, Color color, string desc, object editTarget, Image pic = null)
    {
        var card = new CardPanel { Width = 640, MinimumSize = new Size(640, 0), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.White, Margin = new Padding(0, 0, 0, 10), Padding = new Padding(14, 8, 10, 10) };
        var bar = new Panel { Dock = DockStyle.Left, Width = 6, BackColor = color };
        var inner = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(10, 0, 0, 0) };
        var head = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Margin = Padding.Empty };
        head.Controls.Add(new Label { Text = title, AutoSize = true, Font = new Font("Segoe UI", 12f, FontStyle.Bold), Margin = new Padding(0, 2, 10, 0) });
        head.Controls.Add(new Label { Text = " " + state + " ", AutoSize = true, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), ForeColor = Color.White, BackColor = color, Margin = new Padding(0, 5, 10, 0), Padding = new Padding(4, 2, 4, 2) });
        _ = editTarget; // buttons are added per card below
        inner.Controls.Add(head);
        inner.Controls.Add(new Label { Text = desc, AutoSize = true, MaximumSize = new Size(580, 0), ForeColor = Color.FromArgb(80, 80, 80), Margin = new Padding(0, 4, 0, 4) });
        var body = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = Padding.Empty };
        inner.Controls.Add(body);
        card.Controls.Add(inner);
        if (pic != null)
            card.Controls.Add(new PictureBox { Image = pic, Dock = DockStyle.Left, Width = pic.Width + 14, SizeMode = PictureBoxSizeMode.Normal, Padding = new Padding(10, 4, 0, 0) });
        card.Controls.Add(bar);
        card.Body = body;
        card.Buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = true, AutoSize = true, MaximumSize = new Size(600, 0), Margin = new Padding(0, 6, 0, 0) };
        inner.Controls.Add(card.Buttons);
        cards.Controls.Add(card);
        return card;
    }
}
