using System.Drawing.Drawing2D;

namespace VRRadialOverlayEditor;

// ------------------------------------------------------------ plain-English choices
public record Choice(string Code, string Text) { public override string ToString() => Text; }

public static class Choices
{
    public static readonly Choice[] Hands = { new("left", "Left hand"), new("right", "Right hand") };
    public static readonly Choice[] Buttons =
    {
        new("primary", "A / X button"), new("secondary", "B / Y button"),
        new("trigger", "Trigger"), new("grip", "Grip"),
        new("thumbstick", "Thumbstick click"),
        new("thumbstick.up", "Thumbstick pushed up"), new("thumbstick.down", "Thumbstick pushed down"),
        new("thumbstick.left", "Thumbstick pushed left"), new("thumbstick.right", "Thumbstick pushed right"),
        new("thumbrest", "Thumbrest  (touch: thumb on the pad)"), new("menu", "Menu button"),
        new("primary.touch", "A / X  (just touching)"), new("secondary.touch", "B / Y  (just touching)"),
        new("trigger.touch", "Trigger  (just touching)"), new("thumbstick.touch", "Thumbstick  (just touching)"),
    };
    public static readonly Choice[] Gestures =
    {
        new("press", "Pressed"), new("tap", "Tapped quickly (so a Hold on the same button can still work)"),
        new("hold", "Held down for a moment"), new("double_tap", "Double-tapped"),
        new("while_held", "Held  (starts on press, stops on release)"),
    };
    public static readonly Choice[] Blocks =
    {
        new("none", "No, the game still gets the button"), new("trigger", "Yes, hide the last button from the game"),
        new("all", "Yes, hide all buttons of the combo from the game"),
    };
    public static readonly Choice[] ActionTypes =
    {
        new("key", "Press a keyboard key or mouse button"), new("sequence", "Run a macro (several keys with waits)"),
        new("wheel", "Open a wheel"), new("keyboard", "Open the VR keyboard"),
        new("layer", "Switch a layer (changes what other buttons do)"),
        new("xr_input", "Press a controller button inside the game"),
        new("back", "Wheel: go back one level"), new("close", "Wheel: close"),
        new("reload", "Reload this profile"), new("none", "Do nothing"),
    };
    public static readonly Choice[] KeyModes = { new("tap", "Tap it once"), new("hold", "Keep it held while my button is held"), new("toggle", "Toggle: first time down, next time up") };
    public static readonly Choice[] LayerModes = { new("hold", "On while held"), new("toggle", "Toggle on / off"), new("on", "Turn on"), new("off", "Turn off") };
    public static readonly Choice[] Confirms =
    {
        new("auto", "Automatic (best fit for the opening button)"), new("stick_return", "Push the stick to an item, let it spring back"),
        new("release", "Let go of the opening button"), new("button", "Press the confirm button (trigger)"),
    };
}

public class ChoiceBox : ComboBox
{
    public ChoiceBox(IEnumerable<Choice> items, int width = 340)
    {
        DropDownStyle = ComboBoxStyle.DropDownList; Width = width;
        foreach (var c in items) Items.Add(c);
        if (Items.Count > 0) SelectedIndex = 0;
    }
    public string Code
    {
        get => (SelectedItem as Choice)?.Code;
        set
        {
            foreach (Choice c in Items) if (c.Code == value) { SelectedItem = c; return; }
            if (!string.IsNullOrEmpty(value)) { var extra = new Choice(value, value); Items.Add(extra); SelectedItem = extra; }
        }
    }
}

// ------------------------------------------------------------ input parsing
public static class InputText
{
    public static (string hand, string button) Split(string part)
    {
        var bits = part.Trim().ToLowerInvariant().Split('.');
        string hand = bits[0] is "left" or "l" ? "left" : "right";
        string comp = bits.Length > 1 ? bits[1] : "trigger";
        comp = comp switch { "a" or "x" or "btn1" => "primary", "b" or "y" or "btn2" => "secondary", "stick" or "trackpad" => "thumbstick", "squeeze" => "grip", _ => comp };
        string feat = bits.Length > 2 && bits[2] is not ("click" or "press") ? "." + bits[2] : "";
        if (comp == "thumbrest") feat = "";
        return (hand, comp + feat);
    }
    public static string Join(string hand, string button)
    {
        var bits = button.Split('.');
        string comp = bits[0] switch { "primary" => hand == "left" ? "x" : "a", "secondary" => hand == "left" ? "y" : "b", _ => bits[0] };
        return hand + "." + comp + (bits.Length > 1 ? "." + bits[1] : "");
    }
    public static string Friendly(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "(nothing)";
        return string.Join(" + ", input.Split('+').Select(p =>
        {
            var (h, b) = Split(p);
            string name = Choices.Buttons.FirstOrDefault(c => c.Code == b)?.Text ?? b;
            if (b.StartsWith("primary")) name = name.Replace("A / X", h == "left" ? "X" : "A");
            if (b.StartsWith("secondary")) name = name.Replace("B / Y", h == "left" ? "Y" : "B");
            return (h == "left" ? "Left " : "Right ") + name.Split("  (")[0];
        }));
    }
}

// ------------------------------------------------------------ form helpers
public class FormPanel : TableLayoutPanel
{
    public FormPanel()
    {
        ColumnCount = 2; AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; Dock = DockStyle.Top;
        ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        Padding = new Padding(0, 0, 0, 6);
    }
    public Label AddRow(string label, Control c, string hint = null)
    {
        var l = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Top, Padding = new Padding(0, 6, 0, 0) };
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        int row = RowCount++;
        Controls.Add(l, 0, row);
        if (hint != null)
        {
            var f = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = Padding.Empty };
            f.Controls.Add(c);
            f.Controls.Add(new Label { Text = hint, AutoSize = true, ForeColor = Color.DimGray, MaximumSize = new Size(460, 0), Margin = new Padding(3, 0, 3, 4) });
            Controls.Add(f, 1, row);
            c.VisibleChanged += (s, e) => { f.Visible = c.Visible; };
            l.Tag = f;
        }
        else { Controls.Add(c, 1, row); l.Tag = c; }
        return l;
    }
    public static void Show(Label rowLabel, bool on) { rowLabel.Visible = on; if (rowLabel.Tag is Control c) c.Visible = on; }

    public static Label Header(string text, string help = null)
    {
        return new Label
        {
            Text = text + (help != null ? "\n" + help : ""), AutoSize = true, MaximumSize = new Size(640, 0),
            Font = new Font("Segoe UI", 9.5f), Padding = new Padding(0, 4, 0, 10), Dock = DockStyle.Top
        };
    }
}

public abstract class EditorBase : UserControl
{
    public event Action Changed;
    protected bool loading;
    protected void Fire() { if (!loading) Changed?.Invoke(); }
    protected EditorBase() { AutoScroll = true; Dock = DockStyle.Fill; Padding = new Padding(12, 8, 12, 8); }
    protected void Stack(params Control[] cs) { for (int i = cs.Length - 1; i >= 0; i--) Controls.Add(cs[i]); }
    protected static Label Title(string t) => new() { Text = t, Font = new Font("Segoe UI", 14f, FontStyle.Bold), AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(0, 0, 0, 4) };
}

// ------------------------------------------------------------ chord (button combo) editor
public class ChordEditor : FlowLayoutPanel
{
    public event Action Changed;
    readonly LinkLabel addLink = new() { Text = "+ add another button (makes a combo)", AutoSize = true, Margin = new Padding(3, 4, 3, 2) };
    bool loading;

    public ChordEditor()
    {
        FlowDirection = FlowDirection.TopDown; AutoSize = true; WrapContents = false; Margin = Padding.Empty;
        addLink.LinkClicked += (s, e) => { AddRow("left", "grip", true); Fire(); };
        Controls.Add(addLink);
    }
    void Fire() { if (!loading) Changed?.Invoke(); Relabel(); }

    void AddRow(string hand, string button, bool atFront)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 2) };
        var lead = new Label { AutoSize = true, Padding = new Padding(0, 6, 0, 0), Width = 60 };
        var h = new ChoiceBox(Choices.Hands, 110) { Code = hand };
        var b = new ChoiceBox(Choices.Buttons, 250) { Code = button };
        var x = new Button { Text = "✕", Width = 28, Height = 26 };
        h.SelectedIndexChanged += (s, e) => Fire();
        b.SelectedIndexChanged += (s, e) => Fire();
        x.Click += (s, e) => { if (Rows().Count() > 1) { Controls.Remove(row); row.Dispose(); Fire(); } };
        row.Controls.AddRange(new Control[] { lead, h, b, x });
        Controls.Add(row);
        // Combo modifiers go first, the button that fires stays last.
        Controls.SetChildIndex(row, atFront ? 0 : Controls.Count - 2);
        Controls.SetChildIndex(addLink, Controls.Count - 1);
    }
    IEnumerable<FlowLayoutPanel> Rows() => Controls.OfType<FlowLayoutPanel>();

    void Relabel()
    {
        var rows = Rows().ToList();
        for (int i = 0; i < rows.Count; i++)
        {
            ((Label)rows[i].Controls[0]).Text = rows.Count == 1 ? "" : i == rows.Count - 1 ? "then" : (i == 0 ? "Hold" : "and");
            rows[i].Controls[3].Visible = rows.Count > 1;
        }
    }

    public string Value
    {
        get => string.Join("+", Rows().Select(r => InputText.Join(((ChoiceBox)r.Controls[1]).Code, ((ChoiceBox)r.Controls[2]).Code)));
        set
        {
            loading = true;
            foreach (var r in Rows().ToList()) { Controls.Remove(r); r.Dispose(); }
            var parts = (value ?? "").Split('+', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) parts = new[] { "right.trigger" };
            foreach (var p in parts) { var (h, b) = InputText.Split(p); AddRow(h, b, false); }
            loading = false;
            Relabel();
        }
    }
}

// ------------------------------------------------------------ key combo box with recorder
public class KeyBox : FlowLayoutPanel
{
    public static bool Recording;
    public event Action Changed;
    readonly TextBox text = new() { Width = 200 };
    readonly Button rec = new() { Text = "Record key", AutoSize = true };
    readonly Button mouse = new() { Text = "Mouse ▾", AutoSize = true };

    public KeyBox()
    {
        AutoSize = true; WrapContents = false; Margin = Padding.Empty;
        Controls.AddRange(new Control[] { text, rec, mouse });
        text.TextChanged += (s, e) => { Check(); if (!Recording) Changed?.Invoke(); };
        rec.Click += (s, e) => StartRecording();
        text.KeyDown += OnKey;
        text.KeyUp += OnKeyUp;
        text.PreviewKeyDown += (s, e) => { if (Recording) e.IsInputKey = true; };
        text.LostFocus += (s, e) => StopRecording();
        var menu = new ContextMenuStrip();
        foreach (var (code, name) in new[] { ("LMB", "Left click"), ("RMB", "Right click"), ("MMB", "Middle click"), ("Mouse4", "Mouse button 4"), ("Mouse5", "Mouse button 5"), ("WheelUp", "Scroll up"), ("WheelDown", "Scroll down") })
            menu.Items.Add(name, null, (s, e) => { text.Text = code; Changed?.Invoke(); });
        mouse.Click += (s, e) => menu.Show(mouse, 0, mouse.Height);
    }
    public string Value { get => text.Text.Trim(); set => text.Text = value ?? ""; }
    static readonly ToolTip tip = new();
    void Check()
    {
        if (Recording) return;
        var err = Native.CheckKeys(text.Text, false);
        text.BackColor = err == null ? SystemColors.Window : Color.MistyRose;
        tip.SetToolTip(text, err == null ? null : "Unknown bind: " + err);
    }

    void StartRecording() { Recording = true; lastMod = null; rec.Text = "Press a key..."; text.BackColor = Color.LightYellow; text.Focus(); }
    void StopRecording() { if (!Recording) return; Recording = false; rec.Text = "Record key"; text.BackColor = SystemColors.Window; Check(); }

    void OnKey(object s, KeyEventArgs e)
    {
        if (!Recording) return;
        e.SuppressKeyPress = true; e.Handled = true;
        if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin)
        {   // remember which modifier; if it is released with no other key, it is recorded alone
            lastMod = ModName(e.KeyCode);
            return;
        }
        lastMod = null;
        string name = KeyName(e.KeyCode);
        if (name == null) return;
        var parts = new List<string>();
        if (e.Control) parts.Add("Ctrl");
        if (e.Shift) parts.Add("Shift");
        if (e.Alt) parts.Add("Alt");
        parts.Add(name);
        text.Text = string.Join("+", parts);
        StopRecording();
        Changed?.Invoke();
    }

    string lastMod;
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern short GetKeyState(int vk);
    static bool Down(int vk) => (GetKeyState(vk) & 0x8000) != 0;
    static string ModName(Keys k) => k switch
    {
        Keys.ControlKey => Down(0xA3) ? "RCtrl" : "LCtrl",
        Keys.ShiftKey => Down(0xA1) ? "RShift" : "LShift",
        Keys.Menu => Down(0xA5) ? "RAlt" : "LAlt",
        _ => "Win",
    };
    void OnKeyUp(object s, KeyEventArgs e)
    {
        if (!Recording) return;
        e.SuppressKeyPress = true; e.Handled = true;
        if (lastMod == null || e.KeyCode is not (Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin)) return;
        text.Text = lastMod; lastMod = null;
        StopRecording();
        Changed?.Invoke();
    }

    static string KeyName(Keys k)
    {
        if (k >= Keys.A && k <= Keys.Z) return k.ToString();
        if (k >= Keys.D0 && k <= Keys.D9) return ((int)(k - Keys.D0)).ToString();
        if (k >= Keys.F1 && k <= Keys.F24) return k.ToString();
        if (k >= Keys.NumPad0 && k <= Keys.NumPad9) return "Num" + (int)(k - Keys.NumPad0);
        return k switch
        {
            Keys.Escape => "Esc", Keys.Return => "Enter", Keys.Space => "Space", Keys.Tab => "Tab", Keys.Back => "Backspace",
            Keys.Delete => "Delete", Keys.Insert => "Insert", Keys.Home => "Home", Keys.End => "End",
            Keys.PageUp => "PageUp", Keys.PageDown => "PageDown", Keys.Up => "Up", Keys.Down => "Down", Keys.Left => "Left", Keys.Right => "Right",
            Keys.OemMinus => "minus", Keys.Oemplus => "equals", Keys.Oemcomma => "comma", Keys.OemPeriod => "period",
            Keys.OemQuestion => "slash", Keys.OemSemicolon => "semicolon", Keys.OemQuotes => "quote",
            Keys.OemOpenBrackets => "lbracket", Keys.OemCloseBrackets => "rbracket", Keys.OemPipe => "backslash", Keys.Oemtilde => "grave",
            Keys.Multiply => "NumMul", Keys.Add => "NumAdd", Keys.Subtract => "NumSub", Keys.Divide => "NumDiv", Keys.Decimal => "NumDec",
            Keys.CapsLock => "CapsLock", Keys.PrintScreen => "PrintScreen", Keys.Pause => "Pause", Keys.Scroll => "ScrollLock",
            _ => null,
        };
    }
}

// ------------------------------------------------------------ action editor ("then do")
public class ActionEditor : FormPanel
{
    public event Action Changed;
    ActionModel a;
    bool loading;
    readonly bool inWheel;
    readonly ChoiceBox type;
    readonly ComboBox page = new() { Width = 220, DropDownStyle = ComboBoxStyle.DropDownList };
    readonly Label lPage;
    readonly bool inWrist;
    readonly WristModel wristOwner;
    readonly KeyBox keys = new();
    readonly ChoiceBox keyMode = new(Choices.KeyModes, 300);
    static readonly ToolTip stepsTip = new();
    readonly TextBox steps = new() { Width = 420, Multiline = true, Height = 70, ScrollBars = ScrollBars.Vertical };
    readonly ComboBox wheel = new() { Width = 220, DropDownStyle = ComboBoxStyle.DropDownList };
    readonly ChoiceBox confirm = new(Choices.Confirms, 360);
    readonly ComboBox layer = new() { Width = 220 };
    readonly ChoiceBox layerMode = new(Choices.LayerModes, 220);
    readonly ChordEditor target = new();
    readonly Label lKeys, lKeyMode, lSteps, lWheel, lConfirm, lLayer, lLayerMode, lTarget;

    public ActionEditor(bool inWheel, WristModel wrist = null)
    {
        this.inWheel = inWheel; this.inWrist = wrist != null; wristOwner = wrist;
        var types = Choices.ActionTypes.Where(c => c.Code != "page").ToList();
        if (wrist != null)
        {
            types = types.Select(c => c.Code switch
            {
                "back" => new Choice("back", "Wrist panel: go back a page (hides it on the first page)"),
                "close" => new Choice("close", "Wrist panel: hide it until I look away"),
                "wheel" => new Choice("wheel", "Open a radial wheel"),
                _ => c,
            }).ToList();
            types.Insert(3, new Choice("page", "Wrist panel: open another page"));
        }
        type = new ChoiceBox(types.ToArray(), 360);
        AddRow("What happens", type);
        lKeys = AddRow("Key", keys, "Click Record key, then press the key or combo on your keyboard. Or pick a mouse button.");
        lKeyMode = AddRow("How", keyMode);
        lSteps = AddRow("Macro steps", steps, "One step per line or split with ;  e.g.\nCtrl+C\nwait 100\nCtrl+V\ndown W / up W to hold a key");
        lWheel = AddRow("Wheel", wheel);
        lPage = AddRow("Page", page, "Its buttons replace these; a Back button is added automatically.");
        lConfirm = AddRow("Pick items by", confirm);
        lLayer = AddRow("Layer name", layer, "Type a new name or pick one you already use.");
        lLayerMode = AddRow("Layer", layerMode);
        lTarget = AddRow("Game button", target, "The game will think this controller button is pressed.");
        lTarget.Text = "Game button";

        type.SelectedIndexChanged += (s, e) => { if (loading) return; a.Type = type.Code; Defaults(); Sync(); Fire(); };
        keys.Changed += () => { if (!loading) { a.Keys = keys.Value; Fire(); } };
        keyMode.SelectedIndexChanged += (s, e) => { if (!loading) { a.Mode = keyMode.Code; Fire(); } };
        steps.TextChanged += (s, e) =>
        {
            var err = Native.CheckKeys(string.Join("; ", steps.Text.Split((char)10).Select(x => x.Trim((char)13))), true);
            steps.BackColor = err == null ? SystemColors.Window : Color.MistyRose;
            stepsTip.SetToolTip(steps, err == null ? null : "Unknown bind: " + err);
        };
        steps.TextChanged += (s, e) => { if (!loading) { a.Steps = steps.Text.Replace("\r\n", "; ").Replace("\n", "; "); Fire(); } };
        wheel.SelectedIndexChanged += (s, e) => { if (!loading) { a.Wheel = wheel.SelectedItem as string; Fire(); } };
        page.SelectedIndexChanged += (s, e) => { if (!loading) { a.Page = page.SelectedItem as string; Fire(); } };
        confirm.SelectedIndexChanged += (s, e) => { if (!loading) { a.Confirm = confirm.Code; Fire(); } };
        layer.TextChanged += (s, e) => { if (!loading) { a.Layer = layer.Text.Trim(); Fire(); } };
        layerMode.SelectedIndexChanged += (s, e) => { if (!loading) { a.Mode = layerMode.Code; Fire(); } };
        target.Changed += () => { if (!loading) { a.Target = target.Value.Split('+').Last(); Fire(); } };
    }
    void Fire() { if (!loading) Changed?.Invoke(); }

    void Defaults()
    {
        switch (a.Type)
        {
            case "key": a.Keys ??= "F1"; if (a.Mode is not ("tap" or "hold" or "toggle")) a.Mode = null; break;
            case "wheel": a.Wheel ??= MainForm.Current.Profile.Wheels.Keys.FirstOrDefault(); break;
            case "page": a.Page ??= wristOwner?.Pages.Keys.FirstOrDefault(); break;
            case "layer": a.Layer ??= "shift"; if (a.Mode is not ("hold" or "toggle" or "on" or "off")) a.Mode = inWheel ? "toggle" : "hold"; break;
            case "xr_input": a.Target ??= "left.menu"; break;
        }
    }

    public void Bind(ActionModel model)
    {
        loading = true;
        a = model;
        type.Code = a.Type ?? "none";
        keys.Value = a.Keys;
        keyMode.Code = string.IsNullOrEmpty(a.Mode) || a.Type != "key" ? "tap" : a.Mode;
        steps.Text = (a.Steps ?? "").Replace("; ", "\r\n").Replace(";", "\r\n");
        wheel.Items.Clear();
        foreach (var w in MainForm.Current.Profile.Wheels.Keys) wheel.Items.Add(w);
        wheel.SelectedItem = a.Wheel;
        page.Items.Clear();
        if (wristOwner != null) foreach (var pg in wristOwner.Pages.Keys) page.Items.Add(pg);
        page.SelectedItem = a.Page;
        confirm.Code = string.IsNullOrEmpty(a.Confirm) ? "auto" : a.Confirm;
        layer.Items.Clear();
        foreach (var l in new LayerConv().GetStandardValues(null)) layer.Items.Add(l);
        layer.Text = a.Layer ?? "";
        layerMode.Code = a.Type == "layer" && !string.IsNullOrEmpty(a.Mode) ? a.Mode : (inWheel ? "toggle" : "hold");
        target.Value = a.Target ?? "left.menu";
        loading = false;
        Sync();
    }

    void Sync()
    {
        string t = a.Type;
        Show(lKeys, t == "key"); Show(lKeyMode, t is "key" or "xr_input");
        Show(lSteps, t == "sequence");
        Show(lWheel, t == "wheel"); Show(lConfirm, t == "wheel" && (!inWheel || inWrist));
        Show(lPage, t == "page");
        Show(lLayer, t == "layer"); Show(lLayerMode, t == "layer");
        Show(lTarget, t == "xr_input");
        if (t == "xr_input") { keyMode.Code = string.IsNullOrEmpty(a.Mode) ? "tap" : a.Mode; }
    }
}

// ------------------------------------------------------------ binding editor
public class BindingEditor : EditorBase
{
    readonly BindingModel b;
    readonly TextBox name = new() { Width = 360 };
    readonly CheckBox enabled = new() { Text = "On", AutoSize = true };
    readonly ChordEditor chord = new();
    readonly ChoiceBox gesture = new(Choices.Gestures, 420);
    readonly NumericUpDown hold = Ms(), tap = Ms(), dbl = Ms();
    readonly ComboBox layers = new() { Width = 220 }, notLayers = new() { Width = 220 };
    readonly ChoiceBox block = new(Choices.Blocks, 360);
    readonly CheckBox passthrough = new() { Text = "If I let go early, the game still gets a normal tap", AutoSize = true };
    readonly ActionEditor action = new(false);
    readonly Label lHold, lTap, lDbl, lPass;

    static NumericUpDown Ms() => new() { Minimum = 50, Maximum = 5000, Increment = 50, Width = 90 };

    public BindingEditor(BindingModel model)
    {
        b = model;
        var when = new FormPanel();
        when.AddRow("Name", name);
        when.AddRow("", enabled);
        when.AddRow("Button", chord, "For a combo, the button that fires goes last; the others must already be held.");
        when.AddRow("When it is", gesture);
        lHold = when.AddRow("Hold for (ms)", hold);
        lTap = when.AddRow("Tap shorter than (ms)", tap);
        lDbl = when.AddRow("Second tap within (ms)", dbl);
        when.AddRow("Only in layer", layers, "Leave empty to work always. With a layer name, this binding only works while that layer is on.");
        when.AddRow("Not in layer", notLayers);
        when.AddRow("Hide from game", block);
        lPass = when.AddRow("", passthrough);

        Stack(Title("Binding"),
              FormPanel.Header("Do something when a controller button (or combo) is used."),
              when,
              FormPanel.Header("Then:"),
              action);

        loading = true;
        name.Text = b.Name; enabled.Checked = b.Enabled; chord.Value = b.Input; gesture.Code = b.Gesture;
        hold.Value = Math.Clamp(b.HoldMs, 50, 5000); tap.Value = Math.Clamp(b.TapMs, 50, 5000); dbl.Value = Math.Clamp(b.DoubleTapMs, 50, 5000);
        foreach (var l in new LayerConv().GetStandardValues(null)) { layers.Items.Add(l); notLayers.Items.Add(l); }
        layers.Text = b.Layers ?? ""; notLayers.Text = b.NotLayers ?? "";
        block.Code = b.Block; passthrough.Checked = b.TapPassthrough;
        action.Bind(b.Action ??= new ActionModel());
        loading = false;
        Sync();

        name.TextChanged += (s, e) => { b.Name = name.Text; Fire(); };
        enabled.CheckedChanged += (s, e) => { b.Enabled = enabled.Checked; Fire(); };
        chord.Changed += () => { b.Input = chord.Value; Fire(); };
        gesture.SelectedIndexChanged += (s, e) => { b.Gesture = gesture.Code; Sync(); Fire(); };
        hold.ValueChanged += (s, e) => { b.HoldMs = (int)hold.Value; Fire(); };
        tap.ValueChanged += (s, e) => { b.TapMs = (int)tap.Value; Fire(); };
        dbl.ValueChanged += (s, e) => { b.DoubleTapMs = (int)dbl.Value; Fire(); };
        layers.TextChanged += (s, e) => { b.Layers = layers.Text.Trim(); Fire(); };
        notLayers.TextChanged += (s, e) => { b.NotLayers = notLayers.Text.Trim(); Fire(); };
        block.SelectedIndexChanged += (s, e) => { b.Block = block.Code; Fire(); };
        passthrough.CheckedChanged += (s, e) => { b.TapPassthrough = passthrough.Checked; Fire(); };
        action.Changed += Fire;
    }

    void Sync()
    {
        FormPanel.Show(lHold, b.Gesture == "hold");
        FormPanel.Show(lPass, b.Gesture == "hold");
        FormPanel.Show(lTap, b.Gesture == "tap");
        FormPanel.Show(lDbl, b.Gesture == "double_tap");
    }
}

// ------------------------------------------------------------ wheel + item editors
public class WheelPreview : Control
{
    public WheelModel Wheel;
    public WheelItemModel Highlight;
    public event Action<WheelItemModel> ItemClicked;
    List<WheelItemModel> On => Wheel?.Items.Where(i => i.Enabled).ToList() ?? new();

    public WheelPreview() { DoubleBuffered = true; Size = new Size(320, 320); Cursor = Cursors.Hand; }

    // The wheel the wrist panel uses is previewed as its button grid (same layout maths as the layer's WristLayout).
    public bool IsWristPage;
    bool Grid => Wheel != null && IsWristPage;
    public int GridColumns = 3;
    int Columns => Math.Clamp(GridColumns, 1, 4);
    internal static List<RectangleF> GridLayout(int n, int cols, out RectangleF header)
    {
        const float pad = 0.03f, gap = 0.02f, headH = 0.14f;
        header = new RectangleF(pad, pad, 1 - 2 * pad, headH);
        var outp = new List<RectangleF>();
        if (n <= 0) return outp;
        cols = Math.Clamp(Math.Min(cols, n), 1, 4);
        int rows = (n + cols - 1) / cols;
        float top = pad + headH + gap, availW = 1 - 2 * pad, availH = 1 - top - pad;
        float cw = (availW - gap * (cols - 1)) / cols, ch = (availH - gap * (rows - 1)) / rows;
        float c = Math.Min(cw, ch * 1.25f); ch = Math.Min(ch, c);
        float left = 0.5f - (c * cols + gap * (cols - 1)) / 2;
        for (int i = 0; i < n; i++) outp.Add(new RectangleF(left + (i % cols) * (c + gap), top + (i / cols) * (ch + gap), c, ch));
        return outp;
    }

    int HitIndex(Point p)
    {
        var items = On; int n = items.Count;
        if (n == 0) return -1;
        if (Grid)
        {
            float S0 = Math.Min(Width, Height); float u = p.X / S0, v = p.Y / S0;
            var cells = GridLayout(n, Columns, out _);
            return cells.FindIndex(r => r.Contains(u, v));
        }
        float cx = Width / 2f, cy = Height / 2f, dx = p.X - cx, dy = p.Y - cy;
        float r = MathF.Sqrt(dx * dx + dy * dy), R = Math.Min(Width, Height) / 2f - 4;
        if (r < R * 0.34f || r > R) return -1;
        float ang = MathF.Atan2(dx, -dy) * 180f / MathF.PI; // 0 = up, clockwise
        float seg = 360f / n;
        return (int)(((ang + seg / 2 + 720) % 360) / seg) % n;
    }
    protected override void OnMouseClick(MouseEventArgs e)
    {
        int i = HitIndex(e.Location);
        if (i >= 0) ItemClicked?.Invoke(On[i]);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.Clear(BackColor);
        if (Grid) { PaintGrid(g); return; }
        float S = Math.Min(Width, Height), c = S / 2f, R = S / 2f - 4, r = R * 0.34f;
        using var bg = new SolidBrush(Color.FromArgb(255, 24, 28, 34));
        g.FillEllipse(bg, c - R, c - R, 2 * R, 2 * R);
        var items = On; int n = items.Count;
        float seg = n > 0 ? 360f / n : 360f;
        using var segB = new SolidBrush(Color.FromArgb(255, 44, 52, 70));
        using var hovB = new SolidBrush(Color.FromArgb(255, 60, 140, 255));
        using var font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        for (int i = 0; i < n; i++)
        {
            var it = items[i];
            float mid = -90 + i * seg, a0 = mid - seg / 2 + 1, sw = seg - 2;
            using var path = new GraphicsPath();
            path.AddArc(c - R + 4, c - R + 4, 2 * (R - 4), 2 * (R - 4), a0, sw);
            path.AddArc(c - r - 3, c - r - 3, 2 * (r + 3), 2 * (r + 3), a0 + sw, -sw);
            path.CloseFigure();
            Brush br = ReferenceEquals(it, Highlight) ? hovB : segB;
            SolidBrush custom = null;
            if (!ReferenceEquals(it, Highlight) && TryColor(it.Color, out var col)) br = custom = new SolidBrush(col);
            g.FillPath(br, path);
            custom?.Dispose();
            float rad = mid * MathF.PI / 180, tr = (R + r) / 2;
            float boxW = Math.Min(R - r, 2 * tr * MathF.Sin(Math.Min(seg, 179) * MathF.PI / 360) * 0.9f);
            g.DrawString(it.Label, font, Brushes.White, new RectangleF(c + MathF.Cos(rad) * tr - boxW / 2, c + MathF.Sin(rad) * tr - 20, boxW, 40), fmt);
        }
        using var cb = new SolidBrush(Color.FromArgb(255, 16, 16, 20));
        g.FillEllipse(cb, c - r, c - r, 2 * r, 2 * r);
        using var tf = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        using var accent = new SolidBrush(Color.FromArgb(255, 127, 196, 255));
        g.DrawString(n == 0 ? "(empty)" : Wheel.Title, tf, accent, new RectangleF(c - r, c - r, 2 * r, 2 * r), fmt);
    }

    void PaintGrid(Graphics g)
    {
        float S = Math.Min(Width, Height);
        var items = On; int n = items.Count;
        var cells = GridLayout(n, Columns, out var hd);
        float bottom = hd.Bottom; foreach (var r in cells) bottom = Math.Max(bottom, r.Bottom);
        RectangleF Px(RectangleF r) => new(r.X * S, r.Y * S, r.Width * S, r.Height * S);
        static GraphicsPath Round(RectangleF r, float rad)
        {
            var gp = new GraphicsPath();
            gp.AddArc(r.X, r.Y, rad, rad, 180, 90); gp.AddArc(r.Right - rad, r.Y, rad, rad, 270, 90);
            gp.AddArc(r.Right - rad, r.Bottom - rad, rad, rad, 0, 90); gp.AddArc(r.X, r.Bottom - rad, rad, rad, 90, 90);
            gp.CloseFigure(); return gp;
        }
        using (var bgp = Round(new RectangleF(1, 1, S - 2, (bottom + 0.03f) * S - 2), S * 0.06f))
        using (var bg = new SolidBrush(Color.FromArgb(255, 24, 28, 34))) g.FillPath(bg, bgp);
        var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        using var tf = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        using var accent = new SolidBrush(Color.FromArgb(255, 127, 196, 255));
        g.DrawString(n == 0 ? "(empty)" : Wheel.Title, tf, accent, Px(hd), fmt);
        using var font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        using var segB = new SolidBrush(Color.FromArgb(255, 44, 52, 70));
        using var hovB = new SolidBrush(Color.FromArgb(255, 60, 140, 255));
        for (int i = 0; i < n; i++)
        {
            var it = items[i]; var r = Px(cells[i]);
            using var gp = Round(r, r.Height * 0.22f);
            Brush br = ReferenceEquals(it, Highlight) ? hovB : segB;
            SolidBrush custom = null;
            if (!ReferenceEquals(it, Highlight) && TryColor(it.Color, out var col)) br = custom = new SolidBrush(col);
            g.FillPath(br, gp);
            custom?.Dispose();
            g.DrawString(it.Label, font, Brushes.White, r, fmt);
        }
    }

    public static bool TryColor(string s, out Color c)
    {
        c = Color.Empty;
        if (string.IsNullOrWhiteSpace(s)) return false;
        s = s.TrimStart('#');
        if (s.Length != 6 && s.Length != 8) return false;
        try
        {
            uint v = Convert.ToUInt32(s, 16);
            c = s.Length == 6 ? Color.FromArgb(255, (int)(v >> 16) & 255, (int)(v >> 8) & 255, (int)v & 255)
                              : Color.FromArgb((int)v & 255, (int)(v >> 24) & 255, (int)(v >> 16) & 255, (int)(v >> 8) & 255);
            return true;
        }
        catch { return false; }
    }
}

public class WheelEditor : EditorBase
{
    public readonly WheelPreview Preview = new();
    public WheelEditor(WheelModel w, Action<string, string> renamed, WristModel wristPanel = null, int columns = 3)
    {
        bool wrist = wristPanel != null;
        var names = wrist ? wristPanel.Pages : MainForm.Current.Profile.Wheels;
        var name = new TextBox { Width = 220, Text = w.Name };
        var title = new TextBox { Width = 220, Text = w.Title };
        var f = new FormPanel();
        f.AddRow("Name", name, wrist ? "Used by 'open another page' buttons. Renaming updates them." : "Used by 'Open a wheel' actions. Renaming updates them.");
        f.AddRow("Title", title, wrist ? "Shown at the top of the page." : "Shown in the middle of the wheel.");
        Preview.Wheel = w; Preview.IsWristPage = wrist; Preview.GridColumns = columns;
        Preview.Margin = new Padding(170, 8, 0, 0);
        var previewHost = new Panel { Height = 340, Dock = DockStyle.Top };
        previewHost.Controls.Add(Preview); Preview.Location = new Point(170, 8);
        Stack(Title(wrist ? "Wrist page" : "Wheel"),
              FormPanel.Header(wrist ? "A page of buttons on the wrist panel (max 16). To add one, click '+ Add button' under this page in the list on the left. Click a button in the picture below to edit it."
                                     : "A radial menu (max 16 items). To add one, click '+ Add item' under this wheel in the list on the left. Click a segment in the picture below to edit it."),
              f, previewHost);
        name.Leave += (s, e) =>
        {
            string n = name.Text.Trim();
            if (n.Length == 0 || n == w.Name) { name.Text = w.Name; return; }
            if (names.ContainsKey(n)) { MessageBox.Show((wrist ? "A page" : "A wheel") + " called " + n + " already exists."); name.Text = w.Name; return; }
            string old = w.Name; w.Name = n; renamed(old, n); Fire();
        };
        title.TextChanged += (s, e) => { w.Title = title.Text; Preview.Invalidate(); Fire(); };
    }
}

public class ItemEditor : EditorBase
{
    public readonly WheelPreview Preview = new();
    public ItemEditor(WheelModel w, WheelItemModel it, WristModel wristPanel = null, int columns = 3)
    {
        bool wrist = wristPanel != null;
        var label = new TextBox { Width = 260, Text = it.Label };
        var desc = new TextBox { Width = 360, Text = it.Description ?? "" };
        var icon = new TextBox { Width = 260, Text = it.Icon ?? "" };
        var browse = new Button { Text = "Browse...", AutoSize = true };
        var iconRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        iconRow.Controls.AddRange(new Control[] { icon, browse });
        var colorBtn = new Button { Width = 120, Height = 26 };
        var colorClear = new Button { Text = "Default", AutoSize = true };
        var colorRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        colorRow.Controls.AddRange(new Control[] { colorBtn, colorClear });
        void ShowColor()
        {
            bool has = WheelPreview.TryColor(it.Color, out var c);
            colorBtn.BackColor = has ? Color.FromArgb(255, c) : SystemColors.Control;
            colorBtn.Text = has ? "" : "(default)";
        }
        ShowColor();
        var action = new ActionEditor(true, wristPanel);
        action.Bind(it.Action ??= new ActionModel());

        var on = new CheckBox { Text = wrist ? "On (untick to hide it from the page)" : "On (untick to hide it from the wheel)", AutoSize = true, Checked = it.Enabled };
        on.CheckedChanged += (s, e) => { it.Enabled = on.Checked; Preview.Invalidate(); Fire(); };
        var f = new FormPanel();
        f.AddRow("", on);
        f.AddRow("Label", label);
        if (!wrist) f.AddRow("Description", desc, "Optional, shown in the middle while this item is pointed at.");
        f.AddRow("Icon", iconRow, "Optional PNG.");
        f.AddRow("Colour", colorRow);
        Preview.Wheel = w; Preview.Highlight = it; Preview.IsWristPage = wrist; Preview.GridColumns = columns; Preview.Size = new Size(220, 220);
        var previewHost = new Panel { Height = 230, Dock = DockStyle.Top };
        previewHost.Controls.Add(Preview); Preview.Location = new Point(170, 4);

        Stack(Title(wrist ? "Wrist button" : "Wheel item"), FormPanel.Header(wrist ? $"A button on the '{w.Name}' wrist page." : $"An entry on the '{w.Name}' wheel."), f, previewHost, FormPanel.Header(wrist ? "When pressed:" : "When picked:"), action);

        label.TextChanged += (s, e) => { it.Label = label.Text; Preview.Invalidate(); Fire(); };
        desc.TextChanged += (s, e) => { it.Description = desc.Text.Length > 0 ? desc.Text : null; Fire(); };
        icon.TextChanged += (s, e) => { it.Icon = icon.Text.Length > 0 ? icon.Text : null; Fire(); };
        browse.Click += (s, e) => { using var d = new OpenFileDialog { Filter = "PNG images|*.png" }; if (d.ShowDialog() == DialogResult.OK) icon.Text = d.FileName; };
        colorBtn.Click += (s, e) =>
        {
            using var d = new ColorDialog { FullOpen = true };
            if (WheelPreview.TryColor(it.Color, out var c)) d.Color = c;
            if (d.ShowDialog() == DialogResult.OK) { it.Color = $"#{d.Color.R:X2}{d.Color.G:X2}{d.Color.B:X2}E0"; ShowColor(); Preview.Invalidate(); Fire(); }
        };
        colorClear.Click += (s, e) => { it.Color = null; ShowColor(); Preview.Invalidate(); Fire(); };
        action.Changed += Fire;
    }
}
