namespace OpenXRHandMenusEditor;

public class SettingsEditor : EditorBase
{
    static readonly Choice[] BlockOpen =
    {
        new("hand", "Everything on the hand(s) using the wheel"), new("minimal", "Only the stick and the confirm / back buttons"),
        new("all", "Both controllers completely"), new("none", "Nothing, the game still sees every button"),
    };
    static readonly Choice[] Anchors =
    {
        new("hand_locked", "Above my hand, stays where it opened"), new("hand", "Above my hand, follows my hand"),
        new("head_locked", "In front of me, stays where it opened"), new("head", "In front of me, follows my view"),
    };
    static readonly Choice[] Methods =
    {
        new("thumbstick", "Push the thumbstick toward an item"), new("point", "Point the controller at an item"),
        new("both", "Either: thumbstick or pointing"),
    };
    static readonly Choice[] SelHands = { new("opener", "The hand that opened the wheel"), new("left", "Always the left hand"), new("right", "Always the right hand") };
    static readonly Choice[] Comps =
    {
        new("trigger", "Trigger"), new("grip", "Grip"), new("primary", "A / X"), new("secondary", "B / Y"),
        new("thumbstick", "Thumbstick click"), new("menu", "Menu button"),
    };

    readonly ProfileModel p;

    public SettingsEditor(ProfileModel model, int wristHand = -1)
    {
        p = model;
        p.Thresholds ??= new(); p.Style ??= new(); p.Selection ??= new(); p.Keyboard ??= new(); p.WristLeft ??= new(); p.WristRight ??= new(); p.Style.Colors ??= new();
        var s = p.Style; var sel = p.Selection; var kb = p.Keyboard; var th = p.Thresholds; var col = s.Colors;

        // ---------------- general
        var general = new FormPanel();
        general.AddRow("", Check("Overlay is on for this game", p.Enabled, v => p.Enabled = v));
        general.AddRow("While a wheel is open, hide from the game", Pick(BlockOpen, p.BlockWhileOpen, v => p.BlockWhileOpen = v),
            "So pressing buttons to use the wheel doesn't also do things in the game.");
        general.AddRow("", Check("Vibrate when pointing at and picking items", p.Haptics, v => p.Haptics = v));
        general.AddRow("Vibration strength", Num(0, 1, 0.05m, 2, p.HapticStrength, v => p.HapticStrength = v));
        general.AddRow("", Check("Show messages in the headset (layer on/off, profile reloaded, errors)", p.Notifications, v => p.Notifications = v));

        // ---------------- wheel position & look
        var look = new FormPanel();
        Label lHandUp = null, lHandToward = null, lHeadDist = null, lHeadDown = null;
        void SyncAnchor()
        {
            bool hand = s.Anchor is "hand" or "hand_locked";
            FormPanel.Show(lHandUp, hand); FormPanel.Show(lHandToward, hand);
            FormPanel.Show(lHeadDist, !hand); FormPanel.Show(lHeadDown, !hand);
        }
        look.AddRow("Where the wheel appears", Pick(Anchors, s.Anchor, v => { s.Anchor = v; SyncAnchor(); }));
        lHandUp = look.AddRow("Height above hand (m)", Num(0, 0.5m, 0.01m, 2, s.HandUp, v => s.HandUp = v));
        lHandToward = look.AddRow("Closer to my face by (m)", Num(-0.3m, 0.3m, 0.01m, 2, s.HandToward, v => s.HandToward = v));
        lHeadDist = look.AddRow("Distance in front (m)", Num(0.2m, 2, 0.05m, 2, s.HeadDistance, v => s.HeadDistance = v));
        lHeadDown = look.AddRow("Below eye level (m)", Num(-0.5m, 0.8m, 0.01m, 2, s.HeadDown, v => s.HeadDown = v));
        look.AddRow("Wheel size (radius, m)", Num(0.05m, 0.5m, 0.01m, 2, s.Radius, v => s.Radius = v), "0.12 m makes a wheel about 24 cm across.");
        look.AddRow("Text size", Num(0.5m, 2.5m, 0.1m, 1, s.FontScale, v => s.FontScale = v));
        look.AddRow("Font", Text(s.Font, v => s.Font = v));
        var colors = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(480, 0), Margin = Padding.Empty };
        void AddColor(string label, Func<string> get, Action<string> set) => colors.Controls.Add(ColorPick(label, get, set));
        AddColor("Background", () => col.Background, v => col.Background = v);
        AddColor("Segments", () => col.Segment, v => col.Segment = v);
        AddColor("Pointed at", () => col.Hover, v => col.Hover = v);
        AddColor("Text", () => col.Text, v => col.Text = v);
        AddColor("Accent", () => col.Accent, v => col.Accent = v);
        AddColor("Outline", () => col.Border, v => col.Border = v);
        AddColor("Toggled on", () => col.ToggledOn, v => col.ToggledOn = v);
        look.AddRow("Colours", colors, "Click a colour to change it. Transparency is kept.");
        SyncAnchor();

        // ---------------- selecting
        var pick = new FormPanel();
        Label lDead = null;
        pick.AddRow("Pick items by", Pick(Methods, sel.Method, v => { sel.Method = v; FormPanel.Show(lDead, v != "point"); }));
        pick.AddRow("Selecting hand", Pick(SelHands, sel.Hand, v => sel.Hand = v));
        lDead = pick.AddRow("Stick deadzone", Num(0.1m, 0.95m, 0.05m, 2, sel.Deadzone, v => sel.Deadzone = v), "How far the stick must be pushed before an item is chosen.");
        FormPanel.Show(lDead, sel.Method != "point");
        pick.AddRow("Confirm button", Pick(Comps, sel.ConfirmButton, v => sel.ConfirmButton = v), "Always picks the item you point at, whatever the wheel's pick mode.");
        pick.AddRow("Back / close button", Pick(Comps, sel.CancelButton, v => sel.CancelButton = v));
        pick.AddRow("", Check("Close the wheel after picking an item", sel.CloseAfterSelect, v => sel.CloseAfterSelect = v));
        pick.AddRow("Open submenus by resting (ms)", Int(0, 3000, 50, sel.SubmenuDwellMs, v => sel.SubmenuDwellMs = v), "For 'let go of the button' wheels. 0 = off.");
        pick.AddRow("Close by itself after (ms)", Int(0, 60000, 500, sel.AutoCloseMs, v => sel.AutoCloseMs = v), "0 = never.");

        // ---------------- keyboard
        var keys = new FormPanel();
        keys.AddRow("Width (m)", Num(0.2m, 1.5m, 0.02m, 2, kb.Width, v => kb.Width = v));
        keys.AddRow("Distance in front (m)", Num(0.2m, 1.5m, 0.02m, 2, kb.Distance, v => kb.Distance = v));
        keys.AddRow("Below eye level (m)", Num(-0.3m, 0.8m, 0.02m, 2, kb.Below, v => kb.Below = v));
        keys.AddRow("", Check("Close the keyboard after Enter (handy for chat boxes)", kb.CloseOnEnter, v => kb.CloseOnEnter = v));

        // ---------------- wrist panel
        var wp = p.WristFor(Math.Max(0, wristHand)); var wrist = new FormPanel();
        string armName = wristHand == 1 ? "right" : "left", otherHand = wristHand == 1 ? "left" : "right";
        var pageChoices = wp.Pages.Keys.Select(k => new Choice(k, k)).ToArray();
        if (!wp.Pages.ContainsKey(wp.Start ?? "")) pageChoices = pageChoices.Append(new Choice(wp.Start ?? "", (wp.Start ?? "") + "  (missing: add a page with this name)")).ToArray();
        wrist.AddRow("", Check($"Show buttons on my {armName} forearm", wp.Enabled, v => wp.Enabled = v),
            $"Appears only when you turn the {armName} forearm toward your eyes AND look at it. Your {otherHand} hand presses the buttons.");
        Label lFirst = null;
        var other = p.WristFor(wristHand == 1 ? 0 : 1);
        if (wristHand >= 0)
            wrist.AddRow("", Check($"Mirror the {otherHand} panel (same pages and buttons)", wp.Mirror, v => { wp.Mirror = v; if (v) other.Mirror = false; FormPanel.Show(lFirst, !v); }),
                $"Untick to give the {armName} forearm its own pages. Ticking this unticks it on the {otherHand} panel (they can't both mirror). Size, side and the settings below stay separate either way.");
        lFirst = wrist.AddRow("First page", Pick(pageChoices, wp.Start, v => wp.Start = v),
            "The page shown when the panel appears. Its pages are listed under it on the left.");
        FormPanel.Show(lFirst, !(wristHand >= 0 && wp.Mirror));
        wrist.AddRow("Side of the arm", Pick(new Choice[] { new("inner", "Inner side / underside (palm side)"), new("back", "Back of the wrist (like a watch)") }, wp.Face, v => wp.Face = v));
        var pose = new ArmPosePreview { Mode = wp.TextUp ?? "fingers", RightArm = wristHand == 1, Profile = p };
        Changed += () => pose.Invalidate(); // size, offsets, columns, side and mirror all show in the picture
        wrist.AddRow("Text direction", Pick(new Choice[] { new("fingers", "Top toward my fingers (hand held up, palm to face)"), new("between", "In between (arm half raised)"), new("along_arm", "Along the forearm (arm held across my body)") }, wp.TextUp ?? "fingers", v => { wp.TextUp = v; pose.Mode = v; }));
        wrist.AddRow("", pose);
        wrist.AddRow("Press buttons by", Pick(new Choice[] { new("both", "Poking or pointing + trigger"), new("poke", "Poking with the other controller"), new("point", "Pointing + trigger") }, wp.Press, v => wp.Press = v));
        wrist.AddRow("Columns", Int(1, 4, 1, wp.Columns, v => wp.Columns = v));
        wrist.AddRow("Panel size (m)", Num(0.05m, 0.4m, 0.01m, 2, wp.Size, v => wp.Size = v), "0.11 m is about the width of a large phone.");
        wrist.AddRow("Toward the elbow (m)", Num(-0.1m, 0.3m, 0.01m, 2, wp.TowardElbow, v => wp.TowardElbow = v), "Distance back along the forearm from the controller.");
        wrist.AddRow("Off the arm (m)", Num(0, 0.15m, 0.005m, 3, wp.Lift, v => wp.Lift = v));
        wrist.AddRow("Arm must face me within (deg)", Num(5, 80, 1, 0, wp.FacingDeg, v => wp.FacingDeg = v), "Smaller = you must turn the arm more deliberately.");
        wrist.AddRow("Must look within (deg)", Num(5, 60, 1, 0, wp.GazeDeg, v => wp.GazeDeg = v), "How close to the centre of your view the panel must be.");
        wrist.AddRow("Show after looking (ms)", Int(0, 2000, 50, wp.ShowMs, v => wp.ShowMs = v));
        wrist.AddRow("Hide after looking away (ms)", Int(0, 3000, 50, wp.HideMs, v => wp.HideMs = v));

        // ---------------- advanced
        var adv = new FormPanel();
        adv.AddRow("", Check("Bring the game window to the front before sending keys", p.FocusGameWindow, v => p.FocusGameWindow = v),
            "Turn on if keys end up in the wrong window.");
        adv.AddRow("Trigger / grip counts as pressed at", Num(0.05m, 1, 0.05m, 2, th.PressOn, v => th.PressOn = v));
        adv.AddRow("... and released at", Num(0, 1, 0.05m, 2, th.PressOff, v => th.PressOff = v));
        adv.AddRow("Stick direction counts at", Num(0.1m, 1, 0.05m, 2, th.StickOn, v => th.StickOn = v));
        adv.AddRow("... and released at", Num(0, 1, 0.05m, 2, th.StickOff, v => th.StickOff = v));
        adv.AddRow("Centre circle size", Num(0.15m, 0.7m, 0.02m, 2, s.InnerRatio, v => s.InnerRatio = v));
        adv.AddRow("Gap between segments (deg)", Num(0, 10, 0.5m, 1, s.GapDeg, v => s.GapDeg = v));
        adv.AddRow("Sharpness (pixels)", Int(256, 2048, 128, s.TexturePx, v => s.TexturePx = v), "Higher is sharper but uses more memory.");
        var advToggle = new CheckBox { Text = "Show advanced settings", AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(0, 10, 0, 4) };
        adv.Visible = false;
        advToggle.CheckedChanged += (o, e) => adv.Visible = advToggle.Checked;

        if (wristHand >= 0)
        {
            Stack(Title(wristHand == 1 ? "Right wrist panel" : "Left wrist panel"),
                  FormPanel.Header("Buttons on your forearm. They appear only when you turn the forearm toward your eyes and look at it. Its pages are listed under this panel's bar in the list on the left (unfold it with the arrow); select a page to add buttons."),
                  wrist);
            return;
        }
        Stack(Title("Settings"),
              FormPanel.Header("How the overlay behaves in this game. Changes apply when you save."),
              Section("General"), general,
              Section("Wheel position and look"), look,
              Section("Picking items"), pick,
              Section("VR keyboard"), keys,
              advToggle, adv);
    }

    // ---------------- small builders
    static Label Section(string t) => new()
    {
        Text = t, AutoSize = true, Dock = DockStyle.Top, Font = new Font("Segoe UI", 11f, FontStyle.Bold),
        ForeColor = Color.FromArgb(40, 90, 170), Padding = new Padding(0, 10, 0, 4)
    };

    CheckBox Check(string text, bool value, Action<bool> set)
    {
        var c = new CheckBox { Text = text, AutoSize = true, Checked = value };
        c.CheckedChanged += (o, e) => { set(c.Checked); Fire(); };
        return c;
    }
    ChoiceBox Pick(Choice[] items, string value, Action<string> set)
    {
        var c = new ChoiceBox(items, 360) { Code = value };
        c.SelectedIndexChanged += (o, e) => { set(c.Code); Fire(); };
        return c;
    }
    NumericUpDown Num(decimal min, decimal max, decimal step, int dec, float value, Action<float> set)
    {
        var n = new NumericUpDown { Minimum = min, Maximum = max, Increment = step, DecimalPlaces = dec, Width = 90 };
        n.Value = Math.Clamp((decimal)value, min, max);
        n.ValueChanged += (o, e) => { set((float)n.Value); Fire(); };
        return n;
    }
    NumericUpDown Int(int min, int max, int step, int value, Action<int> set)
    {
        var n = new NumericUpDown { Minimum = min, Maximum = max, Increment = step, Width = 90 };
        n.Value = Math.Clamp(value, min, max);
        n.ValueChanged += (o, e) => { set((int)n.Value); Fire(); };
        return n;
    }
    TextBox Text(string value, Action<string> set)
    {
        var t = new TextBox { Width = 200, Text = value ?? "" };
        t.TextChanged += (o, e) => { set(t.Text); Fire(); };
        return t;
    }
    Control ColorPick(string label, Func<string> get, Action<string> set)
    {
        var b = new Button { Width = 150, Height = 28, TextAlign = ContentAlignment.MiddleCenter, FlatStyle = FlatStyle.Flat };
        void Show()
        {
            WheelPreview.TryColor(get(), out var c);
            b.Text = label; b.BackColor = Color.FromArgb(255, c);
            b.ForeColor = c.GetBrightness() > 0.55f ? Color.Black : Color.White;
        }
        Show();
        b.Click += (o, e) =>
        {
            WheelPreview.TryColor(get(), out var c);
            using var d = new ColorDialog { FullOpen = true, Color = Color.FromArgb(255, c) };
            if (d.ShowDialog() != DialogResult.OK) return;
            int alpha = c.IsEmpty ? 255 : c.A;
            set($"#{d.Color.R:X2}{d.Color.G:X2}{d.Color.B:X2}{alpha:X2}");
            Show(); Fire();
        };
        return b;
    }
}
