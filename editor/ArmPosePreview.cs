using System.Drawing.Drawing2D;

namespace VRRadialOverlayEditor;

// A drawing of a forearm and hand in the pose the wrist panel's text direction is meant for, with
// the panel drawn to scale on the arm: its size, its distance back from the controller, and the
// real buttons of its first page. With no profile attached it draws sample buttons (used for the
// screenshot sheet).
public class ArmPosePreview : Control
{
    string mode = "fingers";
    public bool RightArm;
    public ProfileModel Profile;   // optional: read the live settings from here
    public string Mode { get => mode; set { mode = value ?? "fingers"; Invalidate(); } }

    // Drawing units per metre. The arm is drawn about 25 cm long and the palm about 9 cm.
    const float K = 500f;
    const float GripX = 131f;      // controller grip origin (centre of the palm) along the arm

    public ArmPosePreview() { DoubleBuffered = true; Size = new Size(380, 300); }

    WristModel Panel => Profile?.WristFor(RightArm ? 1 : 0);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.Clear(Color.FromArgb(255, 38, 44, 52));

        var wp = Panel;
        string m = wp?.TextUp ?? mode;
        float sizeM = wp?.Size ?? 0.11f, elbowM = wp?.TowardElbow ?? 0.13f;
        int columns = wp?.Columns ?? 2;

        float armAngle; string caption;
        switch (m)
        {
            case "between": armAngle = -45; caption = "Arm half raised: text turned halfway"; break;
            case "along_arm": armAngle = 0; caption = "Arm across the body: text along the forearm"; break;
            default: armAngle = -90; caption = "Hand held up, palm to face: top toward the fingers"; break;
        }
        if (RightArm) armAngle = -180 - armAngle;
        float textTurn = -armAngle; // every pose is meant to read upright from where you look

        float W = Width, H = Height - 40;
        float scale = m == "fingers" ? 1.15f : m == "along_arm" ? 1.45f : 1.3f;
        var state = g.Save();
        float elbowX = m == "fingers" ? W / 2 : m == "along_arm" ? (RightArm ? W - 30 : 30) : (RightArm ? W - 60 : 60);
        float elbowY = m == "along_arm" ? H * 0.5f : H + 4;
        g.TranslateTransform(elbowX, elbowY);
        g.ScaleTransform(scale, scale);
        g.RotateTransform(armAngle);

        using var skin = new SolidBrush(Color.FromArgb(255, 205, 170, 140));
        using var sleeve = new SolidBrush(Color.FromArgb(255, 90, 96, 110));
        using var outline = new Pen(Color.FromArgb(255, 30, 30, 34), 1.5f);
        using (var arm = Capsule(-30, -15, 145, 30)) { g.FillPath(sleeve, arm); g.DrawPath(outline, arm); }
        using (var palm = Capsule(108, -17, 46, 34)) { g.FillPath(skin, palm); g.DrawPath(outline, palm); }
        for (int i = 0; i < 4; i++)
        {
            float y = -15 + i * 8.5f;
            using var f = Capsule(146, y, i == 0 || i == 3 ? 26 : 32, 7.5f);
            g.FillPath(skin, f); g.DrawPath(outline, f);
        }
        float thumbSide = RightArm ? 1 : -1;
        var ts = g.Save();
        g.TranslateTransform(122, thumbSide * 15); g.RotateTransform(thumbSide * 35);
        using (var th = Capsule(0, -4, 24, 8)) { g.FillPath(skin, th); g.DrawPath(outline, th); }
        g.Restore(ts);

        // The panel, to scale, centred 'toward the elbow' back from the grip.
        g.TranslateTransform(GripX - elbowM * K, 0);
        g.RotateTransform(textTurn);
        DrawPanel(g, sizeM * K, columns);
        g.Restore(state);

        using var cap = new Font("Segoe UI", 8.5f);
        var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(caption, cap, Brushes.Gainsboro, new RectangleF(4, Height - 38, Width - 8, 18), fmt);
        if (wp != null)
        {
            string side = wp.Face == "back" ? "back of the wrist" : "inner side";
            string info = $"{side}, {wp.Size * 100:0.#} cm panel, {wp.TowardElbow * 100:0.#} cm back from the controller, {wp.Lift * 100:0.#} cm off the arm";
            using var small = new Font("Segoe UI", 7.5f);
            g.DrawString(info, small, Brushes.Silver, new RectangleF(4, Height - 20, Width - 8, 18), fmt);
        }
    }

    void DrawPanel(Graphics g, float S, int columns)
    {
        // The real first page when a profile is attached, sample buttons otherwise.
        string title = "TOP";
        var labels = new List<string> { "Keys", "Esc", "Map", "More" };
        var colors = new List<string> { null, null, null, null };
        if (Profile != null)
        {
            var owner = Profile.WristPages(RightArm ? 1 : 0);
            if (owner.Pages.TryGetValue(owner.Start ?? "", out var page))
            {
                title = string.IsNullOrEmpty(page.Title) ? page.Name : page.Title;
                var on = page.Items.Where(i => i.Enabled).ToList();
                labels = on.Select(i => i.Label).ToList();
                colors = on.Select(i => i.Color).ToList();
            }
        }
        var pr = new RectangleF(-S / 2, -S / 2, S, S);
        var cells = WheelPreview.GridLayout(labels.Count, columns, out var hd);
        float bottom = hd.Bottom; foreach (var c in cells) bottom = Math.Max(bottom, c.Bottom);
        using (var bg = new SolidBrush(Color.FromArgb(235, 16, 20, 24)))
        using (var p = Round(new RectangleF(pr.X, pr.Y, S, (bottom + 0.03f) * S), S * 0.08f))
        using (var edge = new Pen(Color.FromArgb(200, 255, 255, 255), 1.2f))
        { g.FillPath(bg, p); g.DrawPath(edge, p); }
        var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
        RectangleF Px(RectangleF r) => new(pr.X + r.X * S, pr.Y + r.Y * S, r.Width * S, r.Height * S);
        using var accent = new SolidBrush(Color.FromArgb(255, 127, 196, 255));
        using var tf = new Font("Segoe UI", Math.Max(4f, S * 0.075f), FontStyle.Bold, GraphicsUnit.Pixel);
        g.DrawString(title, tf, accent, Px(hd), fmt);
        using var cell = new SolidBrush(Color.FromArgb(255, 44, 52, 70));
        float cellPx = cells.Count > 0 ? cells[0].Height * S : 10;
        using var cf = new Font("Segoe UI", Math.Max(3.5f, cellPx * 0.2f), FontStyle.Bold, GraphicsUnit.Pixel);
        for (int i = 0; i < cells.Count; i++)
        {
            var r = Px(cells[i]);
            using var cp = Round(r, r.Height * 0.22f);
            SolidBrush custom = WheelPreview.TryColor(colors[i], out var col) ? new SolidBrush(col) : null;
            g.FillPath(custom ?? cell, cp);
            custom?.Dispose();
            g.DrawString(labels[i], cf, Brushes.White, r, fmt);
        }
    }

    static GraphicsPath Capsule(float x, float y, float w, float h) => Round(new RectangleF(x, y, w, h), Math.Min(w, h));
    static GraphicsPath Round(RectangleF r, float d)
    {
        var p = new GraphicsPath();
        d = Math.Max(0.5f, Math.Min(d, Math.Min(r.Width, r.Height)));
        p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure(); return p;
    }
}
