using System.Drawing.Drawing2D;

namespace VRRadialOverlayEditor;

// Small pictures that say at a glance what a card or page is about: a wheel, a wrist panel,
// the keyboard, a controller, the headset. Drawn from the real profile where that helps.
public static class MiniPics
{
    static readonly Color Bg = Color.FromArgb(38, 44, 52), Seg = Color.FromArgb(70, 82, 104), Hi = Color.FromArgb(60, 140, 255),
                          Skin = Color.FromArgb(205, 170, 140), Sleeve = Color.FromArgb(96, 104, 120), Line = Color.FromArgb(30, 30, 34),
                          Body = Color.FromArgb(225, 228, 234), Off = Color.FromArgb(120, 120, 120);

    public static Bitmap Make(int w, int h, Action<Graphics, float, float> draw)
    {
        var bmp = new Bitmap(w, h);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        using (var b = new SolidBrush(Bg)) using (var p = Round(new RectangleF(0, 0, w - 1, h - 1), 10)) g.FillPath(b, p);
        draw(g, w, h);
        return bmp;
    }

    // A radial menu next to the controller that opens it.
    public static Bitmap Wheel(int w, int h, int hand, WheelModel wheel, bool on)
        => Make(w, h, (g, W, H) =>
        {
            var items = wheel?.Items.Where(i => i.Enabled).ToList() ?? new();
            int n = wheel == null ? 6 : Math.Max(items.Count, 1); // sample wheel when there is none
            float R = Math.Min(W, H) * 0.40f, cx = hand == 0 ? W * 0.60f : W * 0.40f, cy = H / 2f, r = R * 0.36f;
            float seg = 360f / n;
            for (int i = 0; i < n; i++)
            {
                Color c = !on ? Off : i == 0 ? Hi : (i < items.Count && WheelPreview.TryColor(items[i].Color, out var ic) ? Color.FromArgb(255, ic) : Seg);
                using var path = new GraphicsPath();
                float a0 = -90 + i * seg - seg / 2 + 2, sw = seg - 4;
                path.AddArc(cx - R, cy - R, 2 * R, 2 * R, a0, sw);
                path.AddArc(cx - r, cy - r, 2 * r, 2 * r, a0 + sw, -sw);
                path.CloseFigure();
                using var b = new SolidBrush(c); g.FillPath(b, path);
            }
            using (var b = new SolidBrush(Color.FromArgb(16, 18, 22))) g.FillEllipse(b, cx - r + 2, cy - r + 2, 2 * r - 4, 2 * r - 4);
            Controller(g, hand == 0 ? W * 0.16f : W * 0.84f, H * 0.62f, H * 0.42f, hand, on);
        });

    // A forearm with the wrist panel on it.
    public static Bitmap Wrist(int w, int h, int hand, WheelModel page, string state)
        => Make(w, h, (g, W, H) =>
        {
            bool on = state != "OFF";
            var st = g.Save();
            float s = H / 120f;
            g.TranslateTransform(hand == 0 ? W * 0.30f : W * 0.70f, H * 1.02f);
            g.ScaleTransform(s * (hand == 0 ? 1 : -1), s);
            g.RotateTransform(-60);
            using (var b = new SolidBrush(Sleeve)) using (var p = Round(new RectangleF(-10, -13, 100, 26), 13)) g.FillPath(b, p);
            using (var b = new SolidBrush(Skin))
            {
                using (var p = Round(new RectangleF(86, -15, 34, 30), 13)) g.FillPath(b, p);
                for (int i = 0; i < 4; i++) using (var f = Round(new RectangleF(114, -13 + i * 7, 22, 6), 3)) g.FillPath(b, f);
            }
            // the panel: upright on screen
            g.TranslateTransform(48, 0);
            g.RotateTransform(60);
            if (hand == 1) g.ScaleTransform(-1, 1);
            var pr = new RectangleF(-26, -26, 52, 52);
            using (var b = new SolidBrush(Color.FromArgb(235, 16, 20, 24))) using (var p = Round(pr, 7)) g.FillPath(b, p);
            using (var pen = new Pen(Color.FromArgb(200, 255, 255, 255), 1.2f)) using (var p = Round(pr, 7)) g.DrawPath(pen, p);
            int n = Math.Clamp(page?.Items.Count(i => i.Enabled) ?? 6, 1, 9);
            int cols = Math.Min(3, n), rows = (n + cols - 1) / cols;
            float cw = 44f / cols, ch = Math.Min(13f, 38f / rows);
            for (int i = 0; i < n; i++)
            {
                var c = new RectangleF(-22 + (i % cols) * cw + 1, -16 + (i / cols) * (ch + 1), cw - 2, ch);
                using var b = new SolidBrush(!on ? Off : i == 0 ? Hi : Seg);
                using var p = Round(c, 3); g.FillPath(b, p);
            }
            g.Restore(st);
            if (state == "MIRRORED")
                using (var f = new Font("Segoe UI Symbol", Math.Max(9f, H * 0.16f), FontStyle.Bold))
                    TextRenderer.DrawText(g, "⇄", f, new Point(hand == 0 ? (int)(W - H * 0.32f) : 4, 2), Color.FromArgb(120, 170, 255));
        });

    public static Bitmap Keyboard(int w, int h, bool on)
        => Make(w, h, (g, W, H) =>
        {
            float kw = W * 0.8f / 10, kh = H * 0.55f / 4, x0 = W * 0.1f, y0 = H * 0.22f;
            using var b = new SolidBrush(on ? Seg : Off);
            using var hb = new SolidBrush(on ? Hi : Off);
            for (int r = 0; r < 4; r++)
                for (int c = 0; c < 10; c++)
                {
                    if (r == 3 && c != 2) continue; // bottom row: one wide space bar
                    var rect = r == 3 ? new RectangleF(x0 + 2 * kw + 1, y0 + r * kh + 1, kw * 6 - 2, kh - 2) : new RectangleF(x0 + c * kw + 1, y0 + r * kh + 1, kw - 2, kh - 2);
                    using var p = Round(rect, 2); g.FillPath(r == 1 && c == 4 ? hb : b, p);
                }
        });

    public static Bitmap Headset(int w, int h, bool on)
        => Make(w, h, (g, W, H) =>
        {
            var rect = new RectangleF(W * 0.18f, H * 0.28f, W * 0.64f, H * 0.42f);
            using (var b = new SolidBrush(on ? Body : Off)) using (var p = Round(rect, H * 0.12f)) g.FillPath(b, p);
            using (var b = new SolidBrush(Color.FromArgb(40, 46, 56)))
            {
                g.FillEllipse(b, rect.X + rect.Width * 0.12f, rect.Y + rect.Height * 0.2f, rect.Width * 0.3f, rect.Height * 0.6f);
                g.FillEllipse(b, rect.X + rect.Width * 0.58f, rect.Y + rect.Height * 0.2f, rect.Width * 0.3f, rect.Height * 0.6f);
            }
            using var pen = new Pen(on ? Color.FromArgb(46, 158, 91) : Off, 3f);
            g.DrawArc(pen, W * 0.36f, H * 0.06f, W * 0.28f, H * 0.3f, 200, 140); // signal arc
        });

    public static Bitmap Controllers(int w, int h, bool on)
        => Make(w, h, (g, W, H) =>
        {
            Controller(g, W * 0.32f, H * 0.55f, H * 0.62f, 0, on);
            Controller(g, W * 0.68f, H * 0.55f, H * 0.62f, 1, on);
        });

    // A simple touch-style controller: ring on top, handle below, a stick and two buttons.
    static void Controller(Graphics g, float cx, float cy, float size, int hand, bool on)
    {
        float s = size;
        using var body = new SolidBrush(on ? Body : Off);
        using var dark = new SolidBrush(Color.FromArgb(40, 46, 56));
        using var pen = new Pen(on ? Body : Off, s * 0.09f);
        g.DrawEllipse(pen, cx - s * 0.36f, cy - s * 0.62f, s * 0.72f, s * 0.5f);              // tracking ring
        using (var p = Round(new RectangleF(cx - s * 0.17f, cy - s * 0.2f, s * 0.34f, s * 0.7f), s * 0.15f)) g.FillPath(body, p); // handle
        g.FillEllipse(body, cx - s * 0.26f, cy - s * 0.42f, s * 0.52f, s * 0.36f);              // face
        float sx = cx + (hand == 0 ? s * 0.06f : -s * 0.06f);
        g.FillEllipse(dark, sx - s * 0.07f, cy - s * 0.34f, s * 0.14f, s * 0.14f);              // stick
        float bx = cx + (hand == 0 ? -s * 0.13f : s * 0.13f);
        g.FillEllipse(dark, bx - s * 0.04f, cy - s * 0.18f, s * 0.08f, s * 0.08f);              // buttons
        g.FillEllipse(dark, bx - s * 0.04f + (hand == 0 ? s * 0.08f : -s * 0.08f), cy - s * 0.12f, s * 0.08f, s * 0.08f);
    }

    static GraphicsPath Round(RectangleF r, float d)
    {
        var p = new GraphicsPath();
        d = Math.Max(0.5f, Math.Min(d * 2, Math.Min(r.Width, r.Height)));
        p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure(); return p;
    }
}
