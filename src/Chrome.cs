// Always-on parts of the menus: a bar of navigation buttons along the bottom (arrows, Select and Back, each labeled
// with its keyboard key or controller button, and clickable) and notification cards in the bottom-right corner.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Theater
{
    partial class TheaterForm
    {
        // ---------- control bar ----------

        public const float BarTop = 1016;   // screens keep their content above this line

        class BarButton { public string Icon, Label, Key, Pad; public Act Act; public RectangleF Rect; }

        List<BarButton> bar = new List<BarButton>();
        int barHover = -1;
        Bitmap barBmp;
        string barKey;

        // The bar's buttons: the four directions, Select and Back. A screen can rename Select or Back (BarLabel).
        List<BarButton> BarButtons()
        {
            var list = new List<BarButton>();
            Action<Act, string, string, string, string> add = (a, icon, label, key, pad) =>
            {
                string l = dialog != null ? DialogLabel(a, label) : Current.BarLabel(a, label);
                if (l != null) list.Add(new BarButton { Act = a, Icon = icon, Label = l, Key = key, Pad = pad });
            };
            add(Act.Left, "", "Left", "←", "D-pad");
            add(Act.Up, "", "Up", "↑", "D-pad");
            add(Act.Down, "", "Down", "↓", "D-pad");
            add(Act.Right, "", "Right", "→", "D-pad");
            add(Act.Ok, "", "Select", "Enter", "A");
            add(Act.Back, "", "Back", "Esc", "B");
            return list;
        }

        static string DialogLabel(Act a, string label) { return a == Act.Ok ? "Choose" : a == Act.Back ? "Close" : label; }

        void RunBar(BarButton b) { Dispatch(b.Act); }

        void DrawBar(Graphics g)
        {
            var fresh = BarButtons();
            var sig = new System.Text.StringBuilder();
            sig.Append(scale).Append('|').Append(UsingController).Append('|').Append(barHover).Append('|').Append(Ui.Accent.ToArgb()).Append('|').Append(Ui.Bg.ToArgb());
            foreach (var b in fresh) sig.Append('|').Append(b.Label).Append('/').Append(b.Key);
            string key = sig.ToString();
            // The bar is drawn once into a bitmap and copied each frame; it's only redrawn when something on it changes.
            if (key != barKey || barBmp == null)
            {
                bar = fresh;
                if (barBmp != null) barBmp.Dispose();
                barBmp = RenderBar(bar, barHover);
                barKey = key;
            }
            Gfx.Blit(g, barBmp, 0, BarTop);
        }

        // Lays out the buttons (and, with a Graphics, draws them) on a 1920 x 64 strip.
        void LayoutBar(Graphics g, List<BarButton> buttons)
        {
            Graphics m = g;
            Bitmap tmp = null;
            if (m == null) { tmp = new Bitmap(1, 1); m = Graphics.FromImage(tmp); }
            try
            {
                var lf = Ui.F(19, 1);
                float total = 0;
                var widths = new float[buttons.Count];
                for (int i = 0; i < buttons.Count; i++)
                {
                    var b = buttons[i];
                    string cap = UsingController ? b.Pad : b.Key;
                    widths[i] = 18 + 26 + 8 + LabelWidth(m, b.Label) + (cap != "" ? 8 + CapsWidth(m, cap) : 0) + 18;
                    total += widths[i] + (i > 0 ? 10 : 0);
                }
                float x = (1920 - total) / 2;
                for (int i = 0; i < buttons.Count; i++)
                {
                    buttons[i].Rect = new RectangleF(x, BarTop + 11, widths[i], 44);
                    x += widths[i] + 10;
                }
            }
            finally { if (tmp != null) { m.Dispose(); tmp.Dispose(); } }
        }

        // DrawString pads text on both sides, so allow for that beyond the measured width.
        static float LabelWidth(Graphics g, string s) { return Ui.Width(g, s, Ui.F(19, 1)) * 1.05f + 10; }

        float CapsWidth(Graphics g, string cap)
        {
            float w = 0;
            foreach (var c in cap.Split('~')) w += CapWidth(g, c) + 4;
            return w - 4;
        }

        float CapWidth(Graphics g, string c) { return UsingController && c.Length == 1 && "ABXY".IndexOf(c) >= 0 ? 26 : Math.Max(26, Ui.Width(g, c, Ui.F(14, 2)) + 14); }

        Bitmap RenderBar(List<BarButton> buttons, int hover)
        {
            int w = Math.Max(1, (int)Math.Round(1920 * scale)), h = Math.Max(1, (int)Math.Round(64 * scale));
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                g.ScaleTransform(w / 1920f, h / 64f);
                g.TranslateTransform(0, -BarTop);
                // A soft fade from the page into the bar, so content scrolling underneath stays readable.
                using (var lg = new LinearGradientBrush(new RectangleF(0, BarTop - 1, 1920, 66), Color.FromArgb(150, Ui.Bg), Color.FromArgb(245, Ui.Bg), 90f))
                    g.FillRectangle(lg, 0, BarTop, 1920, 64);
                using (var pen = new Pen(Color.FromArgb(40, Ui.Ink), 1)) g.DrawLine(pen, 0, BarTop + 0.5f, 1920, BarTop + 0.5f);
                LayoutBar(g, buttons);
                var lf = Ui.F(19, 1);
                for (int i = 0; i < buttons.Count; i++)
                {
                    var b = buttons[i];
                    var r = b.Rect;
                    Ui.FillRound(g, i == hover ? Ui.Panel2 : Ui.Panel, r, 22);
                    if (i == hover) using (var pen = new Pen(Ui.Accent, 2)) using (var p = Ui.Round(r, 22)) g.DrawPath(pen, p);
                    float x = r.X + 18;
                    Ui.Text(g, b.Icon, Ui.F(20, 9), Ui.Accent, new RectangleF(x, r.Y, 26, r.Height), StringAlignment.Center, StringAlignment.Center);
                    x += 34;
                    float lw = LabelWidth(g, b.Label);
                    Ui.Text(g, b.Label, lf, Ui.Ink, new RectangleF(x, r.Y, lw + 40, r.Height), StringAlignment.Near, StringAlignment.Center);
                    x += lw + 8;
                    string cap = UsingController ? b.Pad : b.Key;
                    if (cap == "") continue;
                    foreach (var c in cap.Split('~'))
                    {
                        float cw = CapWidth(g, c);
                        var cr = new RectangleF(x, r.Y + 9, cw, 26);
                        Color face;
                        if (UsingController && c.Length == 1 && Face(c[0], out face))
                        {
                            using (var br = new SolidBrush(face)) g.FillEllipse(br, cr);
                            Ui.Text(g, c, Ui.F(14, 2), Color.FromArgb(20, 20, 24), cr, StringAlignment.Center, StringAlignment.Center);
                        }
                        else
                        {
                            Ui.FillRound(g, Color.FromArgb(60, Ui.Ink), cr, 6);
                            Ui.Text(g, c, Ui.F(14, 2), Ui.Ink, cr, StringAlignment.Center, StringAlignment.Center);
                        }
                        x += cw + 4;
                    }
                }
            }
            return bmp;
        }

        static bool Face(char k, out Color c)
        {
            switch (k)
            {
                case 'A': c = Color.FromArgb(110, 196, 72); return true;
                case 'B': c = Color.FromArgb(236, 84, 70); return true;
                case 'X': c = Color.FromArgb(64, 148, 240); return true;
                case 'Y': c = Color.FromArgb(244, 196, 48); return true;
            }
            c = Color.Empty;
            return false;
        }

        int HitBar(PointF p)
        {
            if (p.Y < BarTop) return -1;
            for (int i = 0; i < bar.Count; i++) if (bar[i].Rect.Contains(p)) return i;
            return -1;
        }

        // Mouse over the bar: highlight; returns true when the pointer is on the bar.
        bool BarMove(PointF p)
        {
            int i = HitBar(p);
            if (i != barHover) { barHover = i; InvalidateVirtual(new RectangleF(0, BarTop, 1920, 64)); }
            return p.Y >= BarTop;
        }

        bool BarClick(PointF p)
        {
            int i = HitBar(p);
            if (i < 0) return p.Y >= BarTop;
            RunBar(bar[i]);
            Kick();
            return true;
        }

        // ---------- notifications ----------

        class Note { public string Icon, Title, Text, Tag; public double Seconds; public DateTime? Since; }
        readonly List<Note> notes = new List<Note>();
        const float NoteW = 500, NoteH = 92;

        // A card in the bottom-right corner. Ones that arrive during a movie appear when it ends.
        // A tag updates the card already showing with that tag (e.g. "Looking for movies..." -> "12 videos added").
        public void Notify(string icon, string title, string text, double seconds = 7, string tag = null)
        {
            var same = tag != null ? notes.Find(n => n.Tag == tag) : null;
            if (same != null)
            {
                same.Icon = icon; same.Title = title; same.Text = text ?? "";
                same.Seconds = (same.Since.HasValue ? (DateTime.Now - same.Since.Value).TotalSeconds : 0) + seconds;
                if (!Player.Active) Kick();
                return;
            }
            notes.Add(new Note { Icon = icon, Title = title, Text = text ?? "", Seconds = seconds, Tag = tag });
            while (notes.Count > 3) notes.RemoveAt(0);
            if (!Player.Active) Kick();
        }

        public bool NotesMoving
        {
            get { foreach (var n in notes) if (!n.Since.HasValue || (DateTime.Now - n.Since.Value).TotalSeconds < 0.35) return true; return false; }
        }

        // Called by the slow timer: drops cards whose time is up.
        void ExpireNotes()
        {
            if (Player.Active) return;
            int before = notes.Count;
            notes.RemoveAll(n => n.Since.HasValue && (DateTime.Now - n.Since.Value).TotalSeconds > n.Seconds);
            if (notes.Count != before) InvalidateVirtual(new RectangleF(1920 - NoteW - 80, BarTop - 3 * (NoteH + 12) - 20, NoteW + 80, 3 * (NoteH + 12) + 20));
        }

        void DrawNotes(Graphics g)
        {
            var now = DateTime.Now;
            float bottom = BarTop - 16;
            for (int i = notes.Count - 1; i >= 0; i--)
            {
                var n = notes[i];
                if (!n.Since.HasValue) n.Since = now;
                double age = (now - n.Since.Value).TotalSeconds;
                float slide = age < 0.3 ? (float)Math.Pow(1 - age / 0.3, 2) : 0;   // slides in from the right edge
                var r = new RectangleF(1920 - 40 - NoteW + slide * (NoteW + 60), bottom - NoteH, NoteW, NoteH);
                using (var sh = Ui.Round(new RectangleF(r.X - 4, r.Y + 4, r.Width + 8, r.Height + 8), 20))
                using (var b = new SolidBrush(Color.FromArgb(110, 0, 0, 0)))
                    g.FillPath(b, sh);
                Ui.FillRound(g, Ui.Panel2, r, 16);
                Ui.FillRound(g, Ui.Accent, new RectangleF(r.X, r.Y + 16, 5, r.Height - 32), 2);
                Ui.Text(g, n.Icon, Ui.F(30, 9), Ui.Accent, new RectangleF(r.X + 22, r.Y, 44, r.Height), StringAlignment.Center, StringAlignment.Center);
                Ui.Text(g, n.Title, Ui.F(22, 2), Ui.Ink, new RectangleF(r.X + 82, r.Y + 16, r.Width - 100, 30));
                Ui.Text(g, n.Text, Ui.F(18), Ui.Dim, new RectangleF(r.X + 82, r.Y + 48, r.Width - 100, 28));
                bottom -= NoteH + 12;
            }
        }
    }
}
