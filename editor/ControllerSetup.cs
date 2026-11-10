using System.Drawing;

namespace OpenXRHandMenusEditor;

// "Which controllers do you use?" Sets the buttons that open the two main wheels to ones that
// exist on that controller. Asked once on first run, and from the "Controllers" toolbar button.
public partial class MainForm
{
    static string ControllerFlag => Path.Combine(DataDir, "controller_chosen");

    static readonly (string id, string label, string how)[] ControllerKinds =
    {
        ("touch", "Meta Quest / Rift, Pico (Touch-style controllers)",
                  "Double-tap the thumbrest (just touch it, no press) to open that hand's wheel."),
        ("index", "Valve Index, Pimax Crystal, Bigscreen (Index-style)",
                  "Double-click the thumbstick (press it in twice quickly) to open that hand's wheel."),
        ("wmr",   "Windows Mixed Reality / HP Reverb",
                  "Hold the menu button to open that hand's wheel, point the stick, let go to pick. A quick tap still reaches the game."),
        ("vive",  "HTC Vive / Vive Pro wands",
                  "Hold the menu button to open that hand's wheel, point at an item on the trackpad, let go to pick. A quick tap still reaches the game."),
    };

    void AskControllerOnFirstRun()
    {
        if (File.Exists(ControllerFlag) || Environment.GetCommandLineArgs().Length > 1) { OfferTutorialOnFirstRun(); return; }
        ChooseControllers(firstRun: true);
        OfferTutorialOnFirstRun();
    }

    void ChooseControllers(bool firstRun)
    {
        if (Profile == null) return;
        using var dlg = new Form { Text = "Your controllers", FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false, ClientSize = new Size(620, 400), Font = new Font("Segoe UI", 10.5f) };
        dlg.Controls.Add(new Label { Text = (firstRun ? "Welcome to OpenXR Hand Menus!\n\n" : "") + "Which controllers do you use? This sets the buttons that open the right and left hand wheels in '" + Path.GetFileName(currentFile) + "'. Nothing else changes, and you can change it later with the 'Controllers' button at the top.", Location = new Point(18, 14), Size = new Size(584, firstRun ? 86 : 60) });
        var how = new Label { Location = new Point(18, 300), Size = new Size(584, 44), ForeColor = Color.FromArgb(60, 70, 90) };
        var radios = new List<RadioButton>();
        int y = firstRun ? 108 : 82;
        foreach (var k in ControllerKinds)
        {
            var r = new RadioButton { Text = k.label, Tag = k, Location = new Point(24, y), Size = new Size(570, 30) };
            r.CheckedChanged += (s, e) => { if (r.Checked) how.Text = k.how; };
            dlg.Controls.Add(r); radios.Add(r); y += 36;
        }
        radios[0].Checked = true;
        dlg.Controls.Add(how);
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 54, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10) };
        Button B(string t, DialogResult r, bool main = false)
        {
            var b = new Button { Text = t, AutoSize = true, Height = 34, DialogResult = r, FlatStyle = FlatStyle.Flat, Margin = new Padding(8, 0, 0, 0), Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                                 BackColor = main ? Color.FromArgb(240, 140, 30) : Color.White, ForeColor = main ? Color.White : Color.FromArgb(40, 50, 70) };
            bar.Controls.Add(b); return b;
        }
        dlg.AcceptButton = B("Use these buttons", DialogResult.OK, true);
        B("Keep my current buttons", DialogResult.Cancel);
        dlg.Controls.Add(bar);
        var res = dlg.ShowDialog(this);
        try { Directory.CreateDirectory(DataDir); File.WriteAllText(ControllerFlag, DateTime.Now.ToString("s")); } catch { }
        if (res != DialogResult.OK) return;
        var kind = ((ValueTuple<string, string, string>)radios.First(r => r.Checked).Tag).Item1;
        int n = ApplyControllerOpeners(Profile, kind);
        if (n == 0) { MessageBox.Show(this, "This profile has no bindings that open the 'right' or 'left' wheel, so nothing was changed."); return; }
        MarkDirty(); BuildTree();
        if (Save()) MessageBox.Show(this, "Done: " + ControllerKinds.First(k => k.id == kind).how, "Controllers");
    }

    // Rewrites the bindings that open the main 'right' / 'left' wheels for this controller kind.
    // Returns how many bindings were changed. Also used by the --selftest check.
    public static int ApplyControllerOpeners(ProfileModel p, string kind)
    {
        int n = 0;
        foreach (var b in p.Bindings)
        {
            if (b.Action?.Type != "wheel" || !string.IsNullOrEmpty(b.Layers)) continue;
            string hand = b.Action.Wheel == "right" ? "right" : b.Action.Wheel == "left" ? "left" : null;
            if (hand == null) continue;
            string side = hand == "right" ? "Right" : "Left";
            switch (kind)
            {
                case "touch":
                    b.Input = hand + ".thumbrest"; b.Gesture = "double_tap"; b.DoubleTapMs = 400; b.Block = "none"; b.TapPassthrough = false; b.Action.Confirm = "stick_return";
                    b.Name = side + " hand wheel"; break;
                case "index":
                    b.Input = hand + ".thumbstick"; b.Gesture = "double_tap"; b.DoubleTapMs = 400; b.Block = "none"; b.TapPassthrough = false; b.Action.Confirm = "stick_return";
                    b.Name = side + " hand wheel (double-click stick)"; break;
                default: // wmr, vive
                    b.Input = hand + ".menu"; b.Gesture = "hold"; b.HoldMs = 500; b.Block = "trigger"; b.TapPassthrough = true; b.Action.Confirm = "release";
                    b.Name = side + " hand wheel (hold menu)"; break;
            }
            n++;
        }
        // WMR and Vive have no B/Y: the grip goes back / closes instead.
        if (n > 0) p.Selection.CancelButton = kind == "wmr" || kind == "vive" ? "grip" : "secondary";
        return n;
    }
}
