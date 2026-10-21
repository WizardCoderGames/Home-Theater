// TV-style screens. Everything is drawn on a virtual 1920x1080 canvas and scaled to the window.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Theater
{
    enum Act { None, Up, Down, Left, Right, Ok, Back, Home, PlayPause, Stop, VolUp, VolDown, Mute, Audio, Subs, Forward, Rewind, Info, Menu, NextChapter, PrevChapter, Search }

    static class Ui
    {
        public static readonly Color Bg = Color.FromArgb(11, 12, 16);
        public static readonly Color Panel = Color.FromArgb(24, 26, 33);
        public static readonly Color Panel2 = Color.FromArgb(38, 41, 51);
        public static readonly Color Ink = Color.FromArgb(242, 242, 246);
        public static readonly Color Dim = Color.FromArgb(150, 154, 168);
        public static readonly Color Accent = Color.FromArgb(255, 176, 32);
        public const string IconFont = "Segoe MDL2 Assets";

        static readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();

        // weight: 0 regular, 1 semibold, 2 bold, 3 light, 9 icon
        public static Font F(float px, int weight = 0)
        {
            string key = px + ":" + weight;
            Font f;
            if (fonts.TryGetValue(key, out f)) return f;
            string family = weight == 1 ? "Segoe UI Semibold" : weight == 3 ? "Segoe UI Light" : weight == 9 ? IconFont : "Segoe UI";
            f = new Font(family, px, weight == 2 ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
            fonts[key] = f;
            return f;
        }

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            if (d <= 0.5f) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, Color c, RectangleF r, float rad)
        {
            using (var p = Round(r, rad))
            using (var b = new SolidBrush(c))
                g.FillPath(b, p);
        }

        public static void Text(Graphics g, string s, Font f, Color c, RectangleF r, StringAlignment h = StringAlignment.Near, StringAlignment v = StringAlignment.Near, bool wrap = false)
        {
            using (var sf = new StringFormat { Alignment = h, LineAlignment = v, Trimming = StringTrimming.EllipsisCharacter })
            using (var b = new SolidBrush(c))
            {
                if (!wrap) sf.FormatFlags = StringFormatFlags.NoWrap;
                g.DrawString(s ?? "", f, b, r, sf);
            }
        }

        public static float Width(Graphics g, string s, Font f)
        {
            using (var sf = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.MeasureTrailingSpaces })
                return g.MeasureString(s, f, 10000, sf).Width;
        }

        public static void DrawCover(Graphics g, Image img, RectangleF dest)
        {
            float sr = (float)img.Width / img.Height, dr = dest.Width / dest.Height;
            RectangleF src;
            if (sr > dr) { float w = img.Height * dr; src = new RectangleF((img.Width - w) / 2, 0, w, img.Height); }
            else { float h = img.Width / dr; src = new RectangleF(0, (img.Height - h) / 2, img.Width, h); }
            g.DrawImage(img, dest, src, GraphicsUnit.Pixel);
        }

        public static string Clock() { return DateTime.Now.ToString("h:mm tt"); }

        public static string Time(double sec)
        {
            if (sec < 0) sec = 0;
            var t = TimeSpan.FromSeconds(Math.Floor(sec));
            return t.TotalHours >= 1 ? string.Format("{0}:{1:00}:{2:00}", (int)t.TotalHours, t.Minutes, t.Seconds) : string.Format("{0}:{1:00}", t.Minutes, t.Seconds);
        }

        public static string Duration(double sec)
        {
            var t = TimeSpan.FromSeconds(sec);
            if (t.TotalHours >= 1) return (int)t.TotalHours + "h " + t.Minutes + "m";
            return Math.Max(1, (int)Math.Round(t.TotalMinutes)) + "m";
        }

        public static string Size(long bytes)
        {
            if (bytes <= 0) return "";
            return bytes >= 1L << 30 ? (bytes / (double)(1L << 30)).ToString("0.0") + " GB" : (bytes >> 20) + " MB";
        }

        public static Color[] Hue(string id)
        {
            int h = 0;
            foreach (char c in id ?? "") h = h * 31 + c;
            var palette = new[] {
                new[] { Color.FromArgb(58, 36, 92), Color.FromArgb(22, 18, 40) },
                new[] { Color.FromArgb(20, 70, 90), Color.FromArgb(12, 24, 36) },
                new[] { Color.FromArgb(92, 40, 36), Color.FromArgb(34, 16, 18) },
                new[] { Color.FromArgb(30, 76, 54), Color.FromArgb(12, 28, 22) },
                new[] { Color.FromArgb(88, 64, 24), Color.FromArgb(32, 24, 12) },
                new[] { Color.FromArgb(40, 52, 100), Color.FromArgb(16, 18, 40) },
            };
            return palette[Math.Abs(h) % palette.Length];
        }

        public static float Badge(Graphics g, string text, float x, float y, Color bg, Color fg, float size = 15)
        {
            var f = F(size, 2);
            float w = Width(g, text, f) + size * 0.9f, h = size + 10;
            FillRound(g, bg, new RectangleF(x, y, w, h), 5);
            Text(g, text, f, fg, new RectangleF(x, y, w, h), StringAlignment.Center, StringAlignment.Center);
            return w;
        }

        public static void Disc(Graphics g, PointF c, float r, Color col)
        {
            using (var pen = new Pen(col, r * 0.08f))
            {
                g.DrawEllipse(pen, c.X - r, c.Y - r, r * 2, r * 2);
                g.DrawEllipse(pen, c.X - r * 0.28f, c.Y - r * 0.28f, r * 0.56f, r * 0.56f);
                g.DrawArc(pen, c.X - r * 0.7f, c.Y - r * 0.7f, r * 1.4f, r * 1.4f, 200, 50);
            }
        }

        // A movie card. The art comes pre-rendered from the cache; only the caption is drawn live.
        public static void Card(Graphics g, TheaterForm f, MediaItem it, RectangleF r, float focus, bool caption = true, bool longTitle = false)
        {
            var art = f.Art.Get(it, r.Width, r.Height);
            if (focus > 0.01f)
            {
                float grow = 1 + 0.07f * focus;
                float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
                r = new RectangleF(cx - r.Width * grow / 2, cy - r.Height * grow / 2, r.Width * grow, r.Height * grow);
                using (var sh = Round(new RectangleF(r.X - 6, r.Y + 4, r.Width + 12, r.Height + 14), 16))
                using (var b = new SolidBrush(Color.FromArgb((int)(120 * focus), 0, 0, 0)))
                    g.FillPath(b, sh);
                g.DrawImage(art, r);
                using (var path = Round(new RectangleF(r.X - 3, r.Y - 3, r.Width + 6, r.Height + 6), 13))
                using (var pen = new Pen(Color.FromArgb((int)(255 * focus), 255, 255, 255), 4))
                    g.DrawPath(pen, path);
            }
            else Gfx.Blit(g, art, r.X, r.Y);

            if (!caption) return;
            string title = longTitle ? it.FullTitle : it.Title;
            Text(g, title, F(21, focus > 0.5f ? 1 : 0), focus > 0.5f ? Ink : Color.FromArgb(205, 206, 214), new RectangleF(r.X, r.Bottom + 10, r.Width, 30));
            string sub = it.Kind == Kind.Disc ? it.DiscType : it.Kind == Kind.NoDisc ? "Optical drive " + it.Drive : it.Year;
            if (sub != "") Text(g, sub, F(17), Dim, new RectangleF(r.X, r.Bottom + 38, r.Width, 24));
        }

        // Card artwork in virtual units: thumbnail (or colored placeholder), badges, watched/progress.
        public static void DrawArt(Graphics g, TheaterForm f, MediaItem it, Bitmap bmp, RectangleF r)
        {
            if (bmp != null)
            {
                g.DrawImage(bmp, r);
            }
            else
            {
                var hue = Hue(it.Id);
                using (var lg = new LinearGradientBrush(r, hue[0], hue[1], 35f)) g.FillRectangle(lg, r);
                bool disc = it.Kind == Kind.Disc || it.Kind == Kind.NoDisc || it.Kind == Kind.DiscFolder;
                if (disc)
                    Disc(g, new PointF(r.X + r.Width / 2, r.Y + r.Height * 0.42f), r.Height * 0.24f, Color.FromArgb(it.Kind == Kind.NoDisc ? 70 : 150, 255, 255, 255));
                var tr = new RectangleF(r.X + 16, r.Y + 12, r.Width - 32, r.Height - 24);
                Text(g, it.Kind == Kind.NoDisc ? "Insert a disc" : it.Title, F(r.Height * (disc ? 0.11f : 0.15f), 1), Color.FromArgb(225, 255, 255, 255),
                    disc ? new RectangleF(tr.X, r.Bottom - r.Height * 0.3f, tr.Width, r.Height * 0.25f) : tr,
                    StringAlignment.Center, StringAlignment.Center, !disc);
            }
            float bx = r.Right - 10;
            var badges = new List<string>();
            if (it.Hdr) badges.Add("HDR");
            if (it.Quality == "4K") badges.Add("4K");
            foreach (var b in badges)
            {
                float w = Width(g, b, F(14, 2)) + 13;
                bx -= w;
                Badge(g, b, bx, r.Y + 10, b == "4K" ? Accent : Color.FromArgb(220, 255, 255, 255), Color.FromArgb(14, 14, 18), 14);
                bx -= 6;
            }
            if (it.Kind == Kind.Collection)
                Badge(g, it.Children.Count + (it.IsGenre ? (it.Children.Count == 1 ? " title" : " titles") : " videos"), r.X + 10, r.Y + 10, Color.FromArgb(200, 12, 12, 16), Ink, 14);

            var w2 = f.Hist.Get(it.Path);
            if (w2 != null && w2.Watched)
            {
                FillRound(g, Color.FromArgb(220, 12, 12, 16), new RectangleF(r.X + 10, r.Bottom - 42, 32, 32), 16);
                Text(g, "\uE73E", F(16, 9), Accent, new RectangleF(r.X + 10, r.Bottom - 42, 32, 32), StringAlignment.Center, StringAlignment.Center);
            }
            else if (w2 != null && w2.Fraction > 0.01)
            {
                using (var bb = new SolidBrush(Color.FromArgb(150, 0, 0, 0))) g.FillRectangle(bb, r.X, r.Bottom - 6, r.Width, 6);
                using (var ab = new SolidBrush(Accent)) g.FillRectangle(ab, r.X, r.Bottom - 6, r.Width * (float)w2.Fraction, 6);
            }
        }

        public static string Meta(TheaterForm f, MediaItem it)
        {
            var parts = new List<string>();
            if (it.Kind == Kind.Disc || it.Kind == Kind.DiscFolder) parts.Add(it.DiscType);
            if (it.Year != "") parts.Add(it.Year);
            if (it.Duration > 0) parts.Add(Duration(it.Duration));
            if (it.Kind == Kind.Collection) parts.Add(it.Children.Count + " videos");
            if (it.Quality != "" && it.Quality != "4K") parts.Add(it.Quality);
            if (it.Size > 0 && it.Kind != Kind.Disc) parts.Add(Size(it.Size));
            return string.Join("  ·  ", parts);
        }

        public static bool Smooth = true;

        public static float Lerp(float v, float target, float k, float snap)
        {
            if (!Smooth) return target;
            float n = v + (target - v) * k;
            return Math.Abs(target - n) < snap ? target : n;
        }
    }

    abstract class Screen
    {
        protected readonly TheaterForm F;
        protected Screen(TheaterForm f) { F = f; }
        public abstract string Name { get; }
        public abstract void Draw(Graphics g);
        public abstract void Act(Act a);
        public virtual bool WantsText { get { return false; } }
        public virtual void Char(char c) { }
        public virtual bool Backspace() { return false; }
        public virtual void SetText(string t) { }
        public virtual void Move(PointF p) { }
        public virtual void Click(PointF p) { }
        public virtual void Wheel(int delta) { }
        public virtual bool Animate() { return false; }
        public virtual void Refresh() { }
        public virtual void Leave() { }
        public virtual string FocusTitle { get { return ""; } }

        protected void DrawClock(Graphics g)
        {
            Ui.Text(g, Ui.Clock(), Ui.F(32, 3), Ui.Ink, new RectangleF(1340, 34, 500, 44), StringAlignment.Far);
            Ui.Text(g, "Phone remote  " + F.RemoteUrl + "   ·   Code " + F.Cfg.Code, Ui.F(17), Ui.Dim, new RectangleF(940, 80, 900, 26), StringAlignment.Far);
        }
    }

    class Row
    {
        public string Title;
        public List<MediaItem> Items;
        public int Col;
        public float Scroll, ScrollTarget;
        public bool LongTitles;
    }

    class HomeScreen : Screen
    {
        const float CW = 300, CH = 169, GAP = 26, MX = 80, ROWS_TOP = 628, ROW_H = 300;
        static readonly string[] Pills = { "Search", "Genres", "All Movies", "Settings" };
        static readonly string[] PillIcons = { "\uE721", "\uE8FD", "\uE8B2", "\uE713" };

        readonly List<Row> rows = new List<Row>();
        int row, pill;
        bool inMenu;
        float scrollY, scrollYTarget;
        MediaItem hero;
        readonly Dictionary<MediaItem, float> focusAnim = new Dictionary<MediaItem, float>();
        RectangleF playButton;

        public HomeScreen(TheaterForm f) : base(f) { Refresh(); }

        // Typing on the home screen jumps straight into search.
        public override void Char(char c) { if (char.IsLetterOrDigit(c)) F.Push(GridScreen.Search(F, c.ToString())); }
        public override string Name { get { return "home"; } }

        public override string FocusTitle { get { var it = Focused(); return it != null ? it.Title : inMenu ? Pills[pill] : ""; } }

        MediaItem Focused()
        {
            if (inMenu || row < 0 || row >= rows.Count) return null;
            var r = rows[row];
            return r.Col < r.Items.Count ? r.Items[r.Col] : null;
        }

        public override void Refresh()
        {
            string keepRow = row < rows.Count ? rows[row].Title : null;
            var keepItem = Focused();
            rows.Clear();
            if (F.DiscItems.Count > 0) rows.Add(new Row { Title = "Disc", Items = F.DiscItems });

            var cont = new List<MediaItem>();
            foreach (var kv in F.Hist.All().OrderByDescending(k => k.Value.When))
            {
                MediaItem it;
                if (!kv.Value.Watched && kv.Value.Pos > 30 && F.Lib.ByPath.TryGetValue(kv.Key, out it)) cont.Add(it);
                if (cont.Count >= 15) break;
            }
            if (cont.Count > 0) rows.Add(new Row { Title = "Continue Watching", Items = cont, LongTitles = true });
            foreach (var d in F.Usb)
            {
                var onDrive = F.Lib.Items.Where(i => i.Path != null && i.Path.StartsWith(d.Root, StringComparison.OrdinalIgnoreCase)).ToList();
                if (onDrive.Count > 0) rows.Add(new Row { Title = "USB \u00B7 " + d.RowName, Items = onDrive });
            }

            if (F.Lib.Items.Count > 0)
                rows.Add(new Row { Title = "Recently Added", Items = F.Lib.Items.OrderByDescending(i => i.Added).Take(20).ToList() });
            var uhd = F.Lib.Flatten().Where(i => i.Quality == "4K" || i.Hdr).ToList();
            if (uhd.Count > 0) rows.Add(new Row { Title = "4K & HDR", Items = uhd, LongTitles = true });
            foreach (var genre in Genres.Order)
            {
                var list = F.Lib.Items.Where(i => i.Genres.Contains(genre)).ToList();
                if (list.Count >= 3) rows.Add(new Row { Title = genre, Items = list });
            }
            var cols = F.Lib.Items.Where(i => i.Kind == Kind.Collection).ToList();
            if (cols.Count > 0) rows.Add(new Row { Title = "Collections & Series", Items = cols });
            if (F.Lib.Items.Count > 0) rows.Add(new Row { Title = "All Movies  A–Z", Items = F.Lib.Items });

            row = 0;
            for (int i = 0; i < rows.Count; i++)
                if (rows[i].Title == keepRow)
                {
                    row = i;
                    int c = keepItem != null ? rows[i].Items.IndexOf(keepItem) : -1;
                    rows[i].Col = Math.Max(0, c);
                }
            if (rows.Count == 0) inMenu = true;
            Retarget(false);
            scrollY = scrollYTarget;
            foreach (var r in rows) r.Scroll = r.ScrollTarget;
        }

        void Retarget(bool mouse)
        {
            if (row >= rows.Count) row = Math.Max(0, rows.Count - 1);
            if (!mouse) scrollYTarget = row;
            if (row < rows.Count)
            {
                var r = rows[row];
                r.Col = Math.Max(0, Math.Min(r.Col, r.Items.Count - 1));
                float max = Math.Max(0, r.Items.Count * (CW + GAP) - GAP - (1920 - 2 * MX));
                if (!mouse) r.ScrollTarget = Math.Min(max, Math.Max(0, (r.Col - 1) * (CW + GAP)));
                else
                {
                    float x = r.Col * (CW + GAP);
                    if (x < r.ScrollTarget) r.ScrollTarget = x;
                    if (x + CW > r.ScrollTarget + 1920 - 2 * MX) r.ScrollTarget = x + CW - (1920 - 2 * MX);
                }
            }
        }

        public override bool Animate()
        {
            bool moving = false;
            float y = Ui.Lerp(scrollY, scrollYTarget, 0.22f, 0.002f);
            if (y != scrollY) { scrollY = y; moving = true; }
            foreach (var r in rows)
            {
                float s = Ui.Lerp(r.Scroll, r.ScrollTarget, 0.22f, 0.5f);
                if (s != r.Scroll) { r.Scroll = s; moving = true; }
            }
            var foc = Focused();
            foreach (var k in focusAnim.Keys.ToList())
            {
                float t = k == foc ? 1 : 0, v = Ui.Lerp(focusAnim[k], t, 0.3f, 0.01f);
                if (v != focusAnim[k]) moving = true;
                if (v <= 0 && k != foc) focusAnim.Remove(k); else focusAnim[k] = v;
            }
            if (foc != null && !focusAnim.ContainsKey(foc)) { focusAnim[foc] = 0; moving = true; }
            return moving || HeroPending;
        }

        float FocusOf(MediaItem it)
        {
            float v;
            return focusAnim.TryGetValue(it, out v) ? v : 0;
        }

        RectangleF CardRect(int r, int i)
        {
            float y = ROWS_TOP + (r - scrollY) * ROW_H;
            return new RectangleF(MX + i * (CW + GAP) - rows[r].Scroll, y + 46, CW, CH);
        }

        RectangleF[] pillRects;

        RectangleF[] PillRects(Graphics g)
        {
            if (pillRects != null) return pillRects;
            var rects = new RectangleF[Pills.Length];
            float x = 136 + Ui.Width(g, AppInfo.Name, Ui.F(32, 2)) + 44;
            for (int i = 0; i < Pills.Length; i++)
            {
                float w = Ui.Width(g, Pills[i], Ui.F(20, 1)) + 78;
                rects[i] = new RectangleF(x, 38, w, 48);
                x += w + 14;
            }
            pillRects = rects;
            return rects;
        }

        public override void Draw(Graphics g)
        {
            var foc = Focused();
            if (foc != null) hero = foc;
            if (hero == null && rows.Count > 0 && rows[0].Items.Count > 0) hero = rows[0].Items[0];
            DrawHero(g);

            // top bar
            Ui.FillRound(g, Ui.Accent, new RectangleF(80, 40, 44, 44), 11);
            using (var b = new SolidBrush(Ui.Bg)) g.FillPolygon(b, new[] { new PointF(96, 51), new PointF(114, 62), new PointF(96, 73) });
            Ui.Text(g, AppInfo.Name, Ui.F(32, 2), Ui.Ink, new RectangleF(136, 38, 420, 50), StringAlignment.Near, StringAlignment.Center);
            var pr = PillRects(g);
            for (int i = 0; i < Pills.Length; i++)
            {
                bool on = inMenu && pill == i;
                Ui.FillRound(g, on ? Ui.Ink : Color.FromArgb(150, 38, 41, 51), pr[i], 24);
                var fg = on ? Ui.Bg : Ui.Ink;
                Ui.Text(g, PillIcons[i], Ui.F(18, 9), fg, new RectangleF(pr[i].X + 20, pr[i].Y, 26, pr[i].Height), StringAlignment.Near, StringAlignment.Center);
                Ui.Text(g, Pills[i], Ui.F(20, 1), fg, new RectangleF(pr[i].X + 52, pr[i].Y, pr[i].Width - 52, pr[i].Height), StringAlignment.Near, StringAlignment.Center);
            }
            DrawClock(g);

            if (rows.Count == 0)
            {
                Ui.Text(g, "No movies found yet", Ui.F(40, 1), Ui.Ink, new RectangleF(80, 640, 1700, 60));
                Ui.Text(g, "Open Settings to add the folders where your movies are.", Ui.F(24), Ui.Dim, new RectangleF(80, 700, 1700, 40));
                return;
            }

            var state = g.Save();
            g.SetClip(new RectangleF(-2000, ROWS_TOP - 30, 6000, 2000));
            for (int r = 0; r < rows.Count; r++)
            {
                float y = ROWS_TOP + (r - scrollY) * ROW_H;
                if (y < ROWS_TOP - ROW_H || y > 1100) continue;
                bool rowFocused = !inMenu && r == row;
                Ui.Text(g, rows[r].Title, Ui.F(26, 1), rowFocused ? Ui.Ink : Color.FromArgb(185, 188, 198), new RectangleF(MX, y, 900, 36));
                for (int i = 0; i < rows[r].Items.Count; i++)
                {
                    var rc = CardRect(r, i);
                    if (rc.Right < -50) continue;
                    if (rc.X > 1980) break;
                    var it = rows[r].Items[i];
                    Ui.Card(g, F, it, rc, rowFocused ? FocusOf(it) : 0, true, rows[r].LongTitles);
                }
            }
            g.Restore(state);
        }

        // The backdrop behind the title is pre-rendered at screen resolution and copied 1:1 each frame.
        // It's enlarged with GDI's StretchBlt, which is many times faster than GDI+ for this.
        static readonly RectangleF HeroArea = new RectangleF(596, 0, 1324, 662);
        Bitmap heroBmp, heroOverlay;
        float heroOverlayScale;
        string heroKey, wantKey;
        DateTime wantSince;
        readonly Dictionary<string, Bitmap> heroCache = new Dictionary<string, Bitmap>();
        readonly List<string> heroOrder = new List<string>();

        public bool HeroPending { get { return wantKey != heroKey; } }

        static Size HeroSize(float s) { return new Size(Math.Max(1, (int)Math.Round(HeroArea.Width * s)), Math.Max(1, (int)Math.Round(HeroArea.Height * s))); }

        // Dimming and fades from the backdrop into the page background; the same for every title.
        static Bitmap BuildHeroOverlay(float s)
        {
            var size = HeroSize(s);
            var bmp = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.ScaleTransform(size.Width / HeroArea.Width, size.Height / HeroArea.Height);
                g.TranslateTransform(-HeroArea.X, 0);
                using (var b = new SolidBrush(Ui.Bg)) g.FillRectangle(b, HeroArea.X, 0, 600 - HeroArea.X, HeroArea.Height);
                using (var dim = new SolidBrush(Color.FromArgb(70, Ui.Bg))) g.FillRectangle(dim, 600, 0, 1320, HeroArea.Height);
                using (var lg = new LinearGradientBrush(new RectangleF(597, 0, 764, 660), Ui.Bg, Color.FromArgb(0, Ui.Bg), 0f)) g.FillRectangle(lg, 598, 0, 760, 662);
                using (var lg = new LinearGradientBrush(new RectangleF(0, 380, 1920, 282), Color.FromArgb(0, Ui.Bg), Ui.Bg, 90f)) g.FillRectangle(lg, HeroArea.X, 381, 1920, 281);
                using (var lg = new LinearGradientBrush(new RectangleF(0, 0, 1920, 150), Color.FromArgb(170, Ui.Bg), Color.FromArgb(0, Ui.Bg), 90f)) g.FillRectangle(lg, HeroArea.X, 0, 1920, 149);
            }
            return bmp;
        }

        static Bitmap BuildHero(MediaItem hero, Bitmap thumb, float s, Bitmap overlay)
        {
            var size = HeroSize(s);
            var bmp = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppRgb);   // opaque: copies to screen without blending
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Ui.Bg);
                float k = size.Width / HeroArea.Width;
                var dest = Rectangle.Round(new RectangleF((600 - HeroArea.X) * k, 0, 1320 * k, 660 * k));
                if (thumb != null) Gdi.StretchCover(g, thumb, dest);
                else
                {
                    var hue = Ui.Hue(hero.Id);
                    using (var lg = new LinearGradientBrush(dest, hue[0], Ui.Bg, 20f)) g.FillRectangle(lg, dest);
                }
                g.DrawImageUnscaled(overlay, 0, 0);
            }
            return bmp;
        }

        void DrawHero(Graphics g)
        {
            playButton = RectangleF.Empty;
            if (hero == null) return;
            var thumb = F.Thumbs.Acquire(hero);
            float s = F.ViewScale;
            string key = hero.Id + "|" + (thumb != null) + "|" + s;
            if (key != wantKey) { wantKey = key; wantSince = DateTime.Now; }
            // While scrolling fast, keep the previous backdrop and swap once focus rests for a moment.
            bool settled = heroBmp == null || (DateTime.Now - wantSince).TotalMilliseconds >= 140 || !Ui.Smooth;
            if (key != heroKey && settled)
            {
                Bitmap cached;
                if (!heroCache.TryGetValue(key, out cached))
                {
                    if (heroOverlay == null || heroOverlayScale != s)
                    {
                        if (heroOverlay != null) heroOverlay.Dispose();
                        heroOverlay = BuildHeroOverlay(s);
                        heroOverlayScale = s;
                    }
                    cached = BuildHero(hero, thumb, s, heroOverlay);
                    heroCache[key] = cached;
                    heroOrder.Add(key);
                    while (heroOrder.Count > 3)
                    {
                        int i = heroCache[heroOrder[0]] == heroBmp ? 1 : 0;   // never drop the one on screen
                        heroCache[heroOrder[i]].Dispose();
                        heroCache.Remove(heroOrder[i]);
                        heroOrder.RemoveAt(i);
                    }
                }
                heroBmp = cached;
                heroKey = key;
            }
            if (heroBmp != null) Gfx.Blit(g, heroBmp, HeroArea.X, HeroArea.Y);
            using (var lg = new LinearGradientBrush(new RectangleF(0, 0, 600, 150), Color.FromArgb(170, Ui.Bg), Color.FromArgb(0, Ui.Bg), 90f)) g.FillRectangle(lg, 0, 0, 599, 149);
            string title = hero.Kind == Kind.NoDisc ? "No disc inserted" : hero.FullTitle;
            var tf = Ui.Width(g, title, Ui.F(64, 2)) > 1150 ? Ui.F(46, 2) : Ui.F(64, 2);
            Ui.Text(g, title, tf, Ui.Ink, new RectangleF(76, 168, 1200, 84), StringAlignment.Near, StringAlignment.Far);

            float x = 80, y = 266;
            if (hero.Quality == "4K") x += Ui.Badge(g, "4K ULTRA HD", x, y, Ui.Accent, Ui.Bg, 15) + 10;
            if (hero.Hdr) x += Ui.Badge(g, "HDR", x, y, Ui.Ink, Ui.Bg, 15) + 10;
            string meta = hero.Kind == Kind.NoDisc ? "Put a DVD or Blu-ray in drive " + hero.Drive : Ui.Meta(F, hero);
            Ui.Text(g, meta, Ui.F(24), Color.FromArgb(205, 208, 218), new RectangleF(x + 4, y - 4, 1100, 34));

            string line = "";
            if (hero.Kind == Kind.Disc && hero.Encrypted && F.Cfg.DiscPlayer == "")
                line = "Copy-protected disc — needs a licensed disc player (see Settings)";
            else if (hero.Kind == Kind.Disc && hero.DiscType == "Data disc") line = "Press OK to browse the videos on this disc";
            else if (hero.Kind == Kind.Collection) line = "Press OK to open";
            else if (hero.Kind == Kind.Movie && hero.Path != null) line = Path.GetFileName(hero.Path);
            Ui.Text(g, line, Ui.F(19), Ui.Dim, new RectangleF(80, 312, 1100, 28));

            var w = F.Hist.Get(hero.Path);
            float by = 360;
            if (w != null && !w.Watched && w.Fraction > 0.01)
            {
                Ui.FillRound(g, Color.FromArgb(80, 255, 255, 255), new RectangleF(80, by + 12, 300, 6), 3);
                Ui.FillRound(g, Ui.Accent, new RectangleF(80, by + 12, 300 * (float)w.Fraction, 6), 3);
                Ui.Text(g, Ui.Duration(w.Len - w.Pos) + " left", Ui.F(19), Ui.Dim, new RectangleF(396, by, 300, 30), StringAlignment.Near, StringAlignment.Center);
                by += 44;
            }
            if (hero.Kind != Kind.NoDisc)
            {
                string label = hero.Kind == Kind.Collection || (hero.Kind == Kind.Disc && hero.DiscType == "Data disc") ? "Open" : w != null && !w.Watched && w.Pos > 30 ? "Resume" : "Play";
                playButton = new RectangleF(80, by, 190, 56);
                Ui.FillRound(g, Ui.Ink, playButton, 28);
                Ui.Text(g, hero.Kind == Kind.Collection ? "" : "", Ui.F(20, 9), Ui.Bg, new RectangleF(104, by, 30, 56), StringAlignment.Near, StringAlignment.Center);
                Ui.Text(g, label, Ui.F(22, 1), Ui.Bg, new RectangleF(140, by, 120, 56), StringAlignment.Near, StringAlignment.Center);
            }
        }

        public override void Act(Act a)
        {
            switch (a)
            {
                case Theater.Act.Up:
                    if (inMenu) return;
                    if (row > 0) row--; else inMenu = true;
                    break;
                case Theater.Act.Down:
                    if (inMenu) { if (rows.Count > 0) inMenu = false; }
                    else if (row < rows.Count - 1) row++;
                    break;
                case Theater.Act.Left:
                    if (inMenu) pill = Math.Max(0, pill - 1);
                    else if (row < rows.Count) rows[row].Col = Math.Max(0, rows[row].Col - 1);
                    break;
                case Theater.Act.Right:
                    if (inMenu) pill = Math.Min(Pills.Length - 1, pill + 1);
                    else if (row < rows.Count) rows[row].Col = Math.Min(rows[row].Items.Count - 1, rows[row].Col + 1);
                    break;
                case Theater.Act.Ok:
                    if (inMenu) OpenPill(pill);
                    else F.Open(Focused());
                    return;
                case Theater.Act.PlayPause:
                    { var it = Focused(); if (it != null) F.Open(it); }
                    return;
                case Theater.Act.Back:
                    F.AskExit();
                    return;
                case Theater.Act.Home:
                    if (rows.Count > 0) { inMenu = false; row = 0; rows[0].Col = 0; }
                    break;
                case Theater.Act.Menu:
                    { var it = Focused(); if (it != null) F.ItemMenu(it); else F.Push(new SettingsScreen(F)); }
                    return;
                case Theater.Act.Search: F.Push(GridScreen.Search(F, "")); return;
                default: return;
            }
            Retarget(false);
            F.Kick();
        }

        void OpenPill(int i)
        {
            if (i == 0) F.Push(GridScreen.Search(F, ""));
            else if (i == 1) F.Push(new GridScreen(F, "Genres", F.GenreItems()));
            else if (i == 2) F.Push(new GridScreen(F, "All Movies", F.Lib.Items));
            else F.Push(new SettingsScreen(F));
        }

        bool HitCard(PointF p, out int r, out int i)
        {
            r = i = -1;
            for (int rr = 0; rr < rows.Count; rr++)
            {
                float y = ROWS_TOP + (rr - scrollY) * ROW_H;
                if (y < ROWS_TOP - 40 || p.Y < y + 46 || p.Y > y + 46 + CH) continue;
                for (int ii = 0; ii < rows[rr].Items.Count; ii++)
                    if (CardRect(rr, ii).Contains(p)) { r = rr; i = ii; return true; }
            }
            return false;
        }

        int HitPill(PointF p)
        {
            if (pillRects == null) return -1;
            for (int i = 0; i < pillRects.Length; i++) if (pillRects[i].Contains(p)) return i;
            return -1;
        }

        public override void Move(PointF p)
        {
            int r, i;
            if (HitCard(p, out r, out i))
            {
                if (inMenu || r != row || rows[r].Col != i)
                {
                    inMenu = false; row = r; rows[r].Col = i;
                    Retarget(true);
                    F.Kick();
                }
                return;
            }
            int pi = HitPill(p);
            if (pi >= 0 && (!inMenu || pill != pi)) { inMenu = true; pill = pi; F.Kick(); }
        }

        public override void Click(PointF p)
        {
            int r, i;
            if (HitCard(p, out r, out i)) { inMenu = false; row = r; rows[r].Col = i; F.Open(rows[r].Items[i]); return; }
            int pi = HitPill(p);
            if (pi >= 0) { OpenPill(pi); return; }
            if (playButton.Contains(p) && hero != null) F.Open(hero);
        }

        public override void Wheel(int delta)
        {
            if (rows.Count == 0) return;
            inMenu = false;
            row = Math.Max(0, Math.Min(rows.Count - 1, row + (delta > 0 ? -1 : 1)));
            Retarget(false);
            F.Kick();
        }
    }

    class GridScreen : Screen
    {
        const int COLS = 5;
        const float CW = 320, CH = 180, GX = 28, RH = 278, MX = 104, TOP = 200;
        readonly string title;
        readonly List<MediaItem> source;
        List<MediaItem> items;
        readonly bool search;
        string query = "";
        int sel;
        float scroll, scrollTarget;
        readonly Dictionary<MediaItem, float> focusAnim = new Dictionary<MediaItem, float>();

        public GridScreen(TheaterForm f, string title, List<MediaItem> items) : base(f)
        {
            this.title = title;
            source = items;
            this.items = items;
        }

        GridScreen(TheaterForm f, List<MediaItem> all, string q) : base(f)
        {
            title = "Search";
            search = true;
            source = all;
            query = q;
            Filter();
        }

        public static GridScreen Search(TheaterForm f, string q)
        {
            var all = new List<MediaItem>(f.Lib.Items);
            foreach (var it in f.Lib.Items) if (it.Children != null) all.AddRange(it.Children);
            return new GridScreen(f, all, q);
        }

        public override string Name { get { return search ? "search" : "grid"; } }
        public override bool WantsText { get { return search; } }
        public override string FocusTitle { get { return sel < items.Count ? items[sel].Title : ""; } }

        void Filter()
        {
            sel = 0;
            scrollTarget = 0;
            if (query.Trim() == "") { items = source.Where(i => i.Parent == null).ToList(); return; }
            var words = query.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            items = source.Where(i =>
            {
                string hay = (i.FullTitle + " " + i.Year + " " + string.Join(" ", (i.Parent ?? i).Genres)).ToLowerInvariant();
                return words.All(w => hay.Contains(w));
            }).ToList();
        }

        public override void Char(char c)
        {
            if (search)
            {
                if (c >= ' ' && query.Length < 60) { query += c; Filter(); F.Kick(); }
                return;
            }
            // Jump to the first title starting with that letter.
            char up = char.ToUpperInvariant(c);
            for (int i = 0; i < items.Count; i++)
                if (items[i].SortKey.Length > 0 && char.ToUpperInvariant(items[i].SortKey[0]) == up) { sel = i; Retarget(); F.Kick(); return; }
        }

        public override bool Backspace()
        {
            if (!search || query.Length == 0) return false;
            query = query.Substring(0, query.Length - 1);
            Filter();
            F.Kick();
            return true;
        }

        public override void SetText(string t)
        {
            query = t;
            Filter();
            F.Kick();
        }

        void Retarget()
        {
            float rowY = (sel / COLS) * RH;
            float view = 1080 - TOP - RH + 20;
            if (rowY - scrollTarget > view) scrollTarget = rowY - view;
            if (rowY < scrollTarget) scrollTarget = rowY;
        }

        RectangleF CardRect(int i)
        {
            return new RectangleF(MX + (i % COLS) * (CW + GX), TOP + (i / COLS) * RH - scroll, CW, CH);
        }

        public override bool Animate()
        {
            bool moving = false;
            float s = Ui.Lerp(scroll, scrollTarget, 0.22f, 0.5f);
            if (s != scroll) { scroll = s; moving = true; }
            MediaItem foc = sel < items.Count ? items[sel] : null;
            foreach (var k in focusAnim.Keys.ToList())
            {
                float v = Ui.Lerp(focusAnim[k], k == foc ? 1 : 0, 0.3f, 0.01f);
                if (v != focusAnim[k]) moving = true;
                if (v <= 0 && k != foc) focusAnim.Remove(k); else focusAnim[k] = v;
            }
            if (foc != null && !focusAnim.ContainsKey(foc)) { focusAnim[foc] = 0; moving = true; }
            return moving;
        }

        public override void Draw(Graphics g)
        {
            if (search)
            {
                var box = new RectangleF(MX, 52, 1100, 76);
                Ui.FillRound(g, Ui.Panel2, box, 38);
                Ui.Text(g, "", Ui.F(26, 9), Ui.Dim, new RectangleF(box.X + 30, box.Y, 40, box.Height), StringAlignment.Near, StringAlignment.Center);
                bool caret = DateTime.Now.Millisecond < 500;
                string shown = query.Length == 0 ? "Type a movie name…" : query + (caret ? "|" : " ");
                Ui.Text(g, shown, Ui.F(32), query.Length == 0 ? Ui.Dim : Ui.Ink, new RectangleF(box.X + 84, box.Y, box.Width - 110, box.Height), StringAlignment.Near, StringAlignment.Center);
                Ui.Text(g, query.Length == 0 ? "All titles" : items.Count + (items.Count == 1 ? " result" : " results"), Ui.F(22), Ui.Dim, new RectangleF(1230, 52, 600, 76), StringAlignment.Near, StringAlignment.Center);
            }
            else
            {
                Ui.Text(g, title, Ui.F(52, 2), Ui.Ink, new RectangleF(MX, 52, 1200, 76), StringAlignment.Near, StringAlignment.Center);
                Ui.Text(g, items.Count + (items.Count == 1 ? " title" : " titles") + "   ·   type a letter to jump", Ui.F(20), Ui.Dim, new RectangleF(MX, 128, 1200, 30));
                DrawClock(g);
            }

            var state = g.Save();
            g.SetClip(new RectangleF(0, TOP - 24, 1920, 1100));
            MediaItem foc = sel < items.Count ? items[sel] : null;
            for (int i = 0; i < items.Count; i++)
            {
                var rc = CardRect(i);
                if (rc.Bottom + 70 < TOP - 30) continue;
                if (rc.Y > 1090) break;
                float v;
                Ui.Card(g, F, items[i], rc, focusAnim.TryGetValue(items[i], out v) ? v : 0, true, search);
            }
            g.Restore(state);
            if (items.Count == 0)
                Ui.Text(g, search ? "Nothing matches “" + query + "”" : "Nothing here", Ui.F(30), Ui.Dim, new RectangleF(MX, TOP + 40, 1700, 50));
            using (var lg = new LinearGradientBrush(new RectangleF(0, TOP - 26, 1920, 30), Ui.Bg, Color.FromArgb(0, Ui.Bg), 90f)) g.FillRectangle(lg, 0, TOP - 25, 1920, 28);
        }

        public override void Act(Act a)
        {
            int n = items.Count;
            switch (a)
            {
                case Theater.Act.Left: if (sel % COLS > 0) sel--; break;
                case Theater.Act.Right: if (sel % COLS < COLS - 1 && sel + 1 < n) sel++; break;
                case Theater.Act.Up: if (sel - COLS >= 0) sel -= COLS; break;
                case Theater.Act.Down:
                    if (sel + COLS < n) sel += COLS;
                    else if ((sel / COLS) < (n - 1) / COLS) sel = n - 1;
                    break;
                case Theater.Act.Ok:
                case Theater.Act.PlayPause:
                    if (sel < n) F.Open(items[sel]);
                    return;
                case Theater.Act.Back: F.Pop(); return;
                case Theater.Act.Home: F.GoHome(); return;
                case Theater.Act.Search: if (!search) F.Push(GridScreen.Search(F, "")); return;
                case Theater.Act.Menu: if (sel < n) F.ItemMenu(items[sel]); else F.Push(new SettingsScreen(F)); return;
                default: return;
            }
            Retarget();
            F.Kick();
        }

        int Hit(PointF p)
        {
            if (p.Y < TOP - 20) return -1;
            for (int i = 0; i < items.Count; i++) if (CardRect(i).Contains(p)) return i;
            return -1;
        }

        public override void Move(PointF p)
        {
            int i = Hit(p);
            if (i >= 0 && i != sel) { sel = i; F.Kick(); }
        }

        public override void Click(PointF p)
        {
            int i = Hit(p);
            if (i >= 0) { sel = i; F.Open(items[i]); }
        }

        public override void Wheel(int delta)
        {
            float max = Math.Max(0, ((items.Count + COLS - 1) / COLS) * RH - (1080 - TOP) + 40);
            scrollTarget = Math.Max(0, Math.Min(max, scrollTarget - delta * 1.2f));
            F.Kick();
        }
    }

    class SettingsScreen : Screen
    {
        class Item { public string Section, Label, Value; public Action Run; }
        readonly List<Item> items = new List<Item>();
        int sel;
        float scroll;
        Wifi.Status wifi;
        bool startup;
        Bitmap qrBmp;
        string qrKey;
        readonly Dictionary<int, RectangleF> rects = new Dictionary<int, RectangleF>();

        const float ListX = 104, ListW = 1160, ListTop = 150, ListBottom = 1000, ItemH = 70, ItemGap = 10, HeaderH = 52;

        public SettingsScreen(TheaterForm f) : base(f)
        {
            try { startup = StartWithWindows.Enabled; } catch { }
            Refresh();
            Task.Run(() => Wifi.Get()).ContinueWith(t => F.BeginInvoke((Action)(() => { if (!t.IsFaulted) { wifi = t.Result; Refresh(); } })));
        }

        public override string Name { get { return "settings"; } }
        public override string FocusTitle { get { return sel < items.Count ? items[sel].Label : ""; } }

        void Add(string section, string label, string value, Action run) { items.Add(new Item { Section = section, Label = label, Value = value, Run = run }); }

        public override void Refresh()
        {
            items.Clear();
            var c = F.Cfg;
            foreach (var root in c.Roots)
            {
                string r = root;
                Add("Library", "Movie folder", r, () => F.ShowDialog("Remove this folder?", r + "\n\nThe files stay on your PC; " + AppInfo.Name + " just stops showing them.", new[] { "Remove", "Keep" }, i => { if (i == 0) { c.Roots.Remove(r); c.Save(); F.Rescan(); Refresh(); } }));
            }
            Add("Library", "Add a movie folder…", "", AddFolder);
            Add("Library", "Rescan library", F.Lib.Items.Count + " titles", () => F.Rescan());
            Add("Library", "USB drives", (c.UsbAuto ? "Add movies automatically" : "Off") + (F.Usb.Count > 0 ? "  \u00B7  " + F.Usb.Count + " connected" : ""), () => { F.ToggleUsbAuto(); Refresh(); });

            Add("Playback", "Disc player", c.DiscPlayer == "" ? "VLC (built in)" : Path.GetFileNameWithoutExtension(c.DiscPlayer), ChooseDiscPlayer);
            Add("Playback", "Hardware acceleration", HwLabel(c.HwDecode), ChooseHw);
            Add("Playback", "Video renderer", RendererLabel(c.Renderer), ChooseRenderer);
            Add("Playback", "VLC location", c.VlcPath, ChooseVlc);

            Add("Display", "Screen resolution", F.ResolutionLabel, ChooseResolution);
            Add("Display", "Display mode", c.Windowed ? "Window" : "Full screen", () => { F.ToggleWindowed(); Refresh(); });
            Add("Display", "Animations", F.AnimationLabel, () => F.ShowDialog("Animations",
                "Automatic keeps smooth sliding animations unless this PC can't draw them quickly, then switches to instant moves.",
                new[] { "Automatic", "Always on", "Off (fastest)" }, i => { if (i >= 0) { F.SetAnimations(i == 0 ? "auto" : i == 1 ? "on" : "off"); Refresh(); } }));

            Add("Phone & network", "Phone remote", F.RemoteUrl + "   ·   code " + c.Code, () => F.ShowDialog("Phone remote",
                "Scan the QR code with your phone's camera (same Wi-Fi), or open " + F.RemoteUrl + " in its browser. A code pops up here to type on the phone.\n\nTip: in Chrome, use ⋮ → Add to Home screen so it opens like an app.",
                new[] { "Done", "Make a new code" }, i => { if (i == 1) { c.Code = Settings.NewCode(); c.Save(); Refresh(); } }));
            Add("Phone & network", "Phone QR popup", c.PhonePrompt ? "At start, and when the phone disconnects" : "Off", () => { c.PhonePrompt = !c.PhonePrompt; c.Save(); Refresh(); });
            Add("Phone & network", "Wi-Fi", wifi == null ? "Checking…" : wifi.Connected ? wifi.Ssid + (wifi.Signal != "" ? "  ·  " + wifi.Signal + " signal" : "") + (c.WifiAuto != "" ? "  ·  auto-connect on" : "") : "Not connected", ChooseWifi);
            Add("Phone & network", "Start with Windows", startup ? "On" : "Off", ToggleStartup);
            Add("Phone & network", "Wake this PC from your phone", "Wake-on-LAN details", ShowWakeInfo);

            Add("System", "System info", "CPU, graphics, memory and usage", () => F.Push(new InfoScreen(F)));
            Add("System", "Check for updates", "Version " + Updater.Current + "  \u00B7  " + F.UpdateStatus, () => F.CheckForUpdates(true));
            Add("System", "Automatic updates", F.Cfg.AutoUpdate ? "On" : "Off", () => { F.ToggleAutoUpdate(); Refresh(); });
            Add("System", "Exit " + AppInfo.Name, "Exit, restart or shut down the PC", () => F.AskExit());
            sel = Math.Max(0, Math.Min(sel, items.Count - 1));
            F.Kick();
        }

        public static string HwLabel(string v)
        {
            switch (v) { case "d3d11": return "Direct3D 11 (D3D11VA)"; case "dxva2": return "DXVA2"; case "off": return "Off (processor only)"; default: return "Automatic"; }
        }

        public static string RendererLabel(string v)
        {
            switch (v) { case "d3d11": return "Direct3D 11"; case "d3d9": return "Direct3D 9"; case "opengl": return "OpenGL"; case "gdi": return "Windows GDI"; default: return "Automatic (Direct3D 11)"; }
        }

        void ChooseHw()
        {
            var keys = new[] { "auto", "d3d11", "dxva2", "off" };
            F.ShowDialog("Hardware acceleration",
                "Uses the graphics card to decode video, which keeps 4K smooth and the PC cool. If a particular PC shows green or black frames, stutters, or crashes during playback, try another option.",
                new[] { "Automatic (recommended)", "Direct3D 11 (D3D11VA) — Windows 10/11 graphics", "DXVA2 — older graphics cards", "Off — use the processor only", "Cancel" },
                i => { if (i >= 0 && i < 4) { F.Cfg.HwDecode = keys[i]; F.Cfg.Save(); F.RestartEngine(); Refresh(); } });
        }

        void ChooseRenderer()
        {
            var keys = new[] { "auto", "d3d11", "d3d9", "opengl", "gdi" };
            F.ShowDialog("Video renderer",
                "How the picture is drawn on screen. Try Direct3D 9 or OpenGL if a PC shows a black picture or glitches. (DirectX 12 and Vulkan aren't offered because VLC 3, the playback engine, doesn't support them.)",
                new[] { "Automatic (Direct3D 11)", "Direct3D 11", "Direct3D 9 — older PCs", "OpenGL", "Windows GDI — slowest, most compatible", "Cancel" },
                i => { if (i >= 0 && i < 5) { F.Cfg.Renderer = keys[i]; F.Cfg.Save(); F.RestartEngine(); Refresh(); } });
        }

        void ChooseResolution()
        {
            var choices = F.ResolutionChoices();
            if (choices.Count == 0) { F.ShowToast("This screen doesn't report any other resolutions", 3); return; }
            var labels = new List<string>();
            foreach (var s in choices) labels.Add(s.IsEmpty ? "Native (" + F.NativeResolution.Width + " × " + F.NativeResolution.Height + ")" : s.Width + " × " + s.Height + (s.Height == 2160 ? "  (4K)" : s.Height == 1080 ? "  (1080p)" : s.Height == 720 ? "  (720p)" : ""));
            labels.Add("Cancel");
            F.ShowDialog("Screen resolution",
                "Lower resolutions make the menus faster on slow PCs. This only changes the screen while " + AppInfo.Name + " is open in full screen; Windows goes back to normal when you exit." + (F.Cfg.Windowed ? "\n\n(Switch Display mode to Full screen for this to take effect.)" : ""),
                labels.ToArray(), i => { if (i >= 0 && i < choices.Count) F.SetResolution(choices[i]); Refresh(); });
        }

        void ChooseWifi()
        {
            F.ShowToast("Looking for saved Wi-Fi networks…", 3);
            Task.Run(() => Wifi.SavedNetworks()).ContinueWith(t => F.BeginInvoke((Action)(() =>
            {
                var nets = t.IsFaulted ? new List<string>() : t.Result.Take(6).ToList();
                bool auto = F.Cfg.WifiAuto != "";
                var opts = new List<string>();
                foreach (var n in nets) opts.Add("Connect to " + n + (wifi != null && wifi.Connected && wifi.Ssid == n ? "  (connected)" : ""));
                opts.Add("Auto-connect when the app starts: " + (auto ? "On" : "Off"));
                opts.Add("Cancel");
                string status = wifi == null ? "" : wifi.Connected ? "Connected to " + wifi.Ssid + "." : "Not connected.";
                F.ShowDialog("Wi-Fi", status + "\n\nWith auto-connect on, if the app starts with Windows before Wi-Fi is up, it connects to your network so the phone remote works right away.",
                    opts.ToArray(), i =>
                    {
                        if (i < 0) return;
                        if (i < nets.Count)
                        {
                            string net = nets[i];
                            F.ShowToast("Connecting to " + net + "…", 10);
                            Task.Run(() => Wifi.Connect(net)).ContinueWith(c => F.BeginInvoke((Action)(() =>
                            {
                                F.ShowToast(c.Result ? "Connecting to " + net : "Couldn't connect to " + net, 4);
                                Task.Delay(4000).ContinueWith(_ => { var w = Wifi.Get(); F.BeginInvoke((Action)(() => { wifi = w; F.UpdateRemoteUrl(); Refresh(); })); });
                            })));
                        }
                        else if (i == nets.Count)
                        {
                            F.Cfg.WifiAuto = auto ? "" : (wifi != null && wifi.Connected ? wifi.Ssid : (nets.Count > 0 ? nets[0] : ""));
                            F.Cfg.Save();
                            Refresh();
                        }
                    });
            })));
        }

        void ShowWakeInfo()
        {
            string mac, broadcast, adapter;
            if (!WakeOnLan.Find(out mac, out broadcast, out adapter)) { F.ShowToast("No network connection right now", 3); return; }
            F.ShowDialog("Wake this PC from your phone",
                "Install a free \u201CWake On LAN\u201D app on your phone and add this PC:\n\n" +
                "MAC address:  " + mac + "\nBroadcast address:  " + broadcast + "\nPort:  9\n\n" +
                "It wakes the PC from sleep (choose \u201CPut the PC to sleep\u201D when you exit), with the phone on the same Wi-Fi. Network card: " + adapter + ".",
                new[] { "Done" }, i => { });
        }

        void ToggleStartup()
        {
            try
            {
                StartWithWindows.Set(!startup);
                startup = StartWithWindows.Enabled;
                F.ShowToast(startup ? AppInfo.Name + " will start when you sign in to Windows" : "Won't start with Windows", 3);
            }
            catch (Exception ex) { F.ShowToast("Couldn't change that: " + ex.Message, 4); }
            Refresh();
        }

        void AddFolder()
        {
            using (var dlg = new FolderBrowserDialog { Description = "Choose a folder with movies", ShowNewFolderButton = false })
            {
                if (dlg.ShowDialog(F) != DialogResult.OK) return;
                if (!F.Cfg.Roots.Contains(dlg.SelectedPath, StringComparer.OrdinalIgnoreCase)) F.Cfg.Roots.Add(dlg.SelectedPath);
                F.Cfg.Save();
                F.Rescan();
                Refresh();
            }
        }

        void ChooseDiscPlayer()
        {
            F.ShowDialog("Disc player", AppInfo.Name + " plays DVDs and unprotected Blu-rays with VLC.\n\nMost store-bought Blu-ray and 4K discs are copy-protected and need a licensed player program. If you have one, choose it here and it will open when you play a disc.",
                new[] { "Choose a program…", "Use VLC", "Cancel" }, i =>
                {
                    if (i == 0)
                        using (var dlg = new OpenFileDialog { Filter = "Programs (*.exe)|*.exe", Title = "Choose your disc player program" })
                            if (dlg.ShowDialog(F) == DialogResult.OK) { F.Cfg.DiscPlayer = dlg.FileName; F.Cfg.Save(); }
                    if (i == 1) { F.Cfg.DiscPlayer = ""; F.Cfg.Save(); }
                    Refresh();
                    F.RefreshHome();
                });
        }

        void ChooseVlc()
        {
            using (var dlg = new OpenFileDialog { Filter = "vlc.exe|vlc.exe", Title = "Find vlc.exe" })
            {
                try { dlg.InitialDirectory = Path.GetDirectoryName(F.Cfg.VlcPath); } catch { }
                if (dlg.ShowDialog(F) != DialogResult.OK) return;
                F.Cfg.VlcPath = dlg.FileName;
                F.Cfg.Save();
                F.RestartEngine();
                Refresh();
            }
        }

        // Lays out section headers and rows; returns the content height.
        float Layout()
        {
            rects.Clear();
            float y = 0;
            string section = null;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].Section != section) { section = items[i].Section; y += (i == 0 ? 0 : 14) + HeaderH; }
                rects[i] = new RectangleF(ListX, ListTop + y, ListW, ItemH);
                y += ItemH + ItemGap;
            }
            return y;
        }

        void KeepVisible()
        {
            Layout();
            if (!rects.ContainsKey(sel)) return;
            var r = rects[sel];
            float top = r.Y - ListTop - (sel == 0 || items[sel].Section != items[sel - 1].Section ? HeaderH : 0);
            float bottom = r.Bottom - ListTop;
            float view = ListBottom - ListTop;
            if (top < scroll) scroll = top;
            if (bottom > scroll + view) scroll = bottom - view;
            scroll = Math.Max(0, scroll);
        }

        public override void Draw(Graphics g)
        {
            Ui.Text(g, "Settings", Ui.F(52, 2), Ui.Ink, new RectangleF(104, 44, 1000, 76), StringAlignment.Near, StringAlignment.Center);
            DrawClock(g);
            Layout();
            var state = g.Save();
            g.SetClip(new RectangleF(0, ListTop - 6, 1300, ListBottom - ListTop + 12));
            string section = null;
            for (int i = 0; i < items.Count; i++)
            {
                var r = rects[i];
                r.Y -= scroll;
                if (items[i].Section != section)
                {
                    section = items[i].Section;
                    Ui.Text(g, section.ToUpperInvariant(), Ui.F(17, 2), Ui.Accent, new RectangleF(r.X + 4, r.Y - 40, 600, 30));
                }
                if (r.Bottom < ListTop - 10 || r.Y > ListBottom + 10) continue;
                bool on = i == sel;
                Ui.FillRound(g, on ? Ui.Panel2 : Ui.Panel, r, 14);
                if (on) Ui.FillRound(g, Ui.Accent, new RectangleF(r.X, r.Y + 14, 5, r.Height - 28), 2);
                Ui.Text(g, items[i].Label, Ui.F(23, on ? 1 : 0), Ui.Ink, new RectangleF(r.X + 30, r.Y, 440, r.Height), StringAlignment.Near, StringAlignment.Center);
                Ui.Text(g, items[i].Value, Ui.F(20), on ? Ui.Ink : Ui.Dim, new RectangleF(r.X + 470, r.Y, r.Width - 500, r.Height), StringAlignment.Far, StringAlignment.Center);
            }
            g.Restore(state);
            DrawPhoneCard(g);
            Ui.Text(g, "Arrows + OK to choose  ·  Esc = back  ·  F11 = window  ·  Info button = system info", Ui.F(18), Ui.Dim, new RectangleF(104, 1022, 1700, 30));
        }

        void DrawPhoneCard(Graphics g)
        {
            var card = new RectangleF(1310, 150, 520, 850);
            Ui.FillRound(g, Ui.Panel, card, 18);
            Ui.Text(g, "Phone remote", Ui.F(30, 2), Ui.Ink, new RectangleF(card.X + 40, card.Y + 34, 440, 44));
            Ui.Text(g, "Point your phone's camera at the code. It opens the remote, and a pairing code pops up on this screen.", Ui.F(19), Ui.Dim,
                new RectangleF(card.X + 40, card.Y + 86, 440, 80), StringAlignment.Near, StringAlignment.Near, true);

            // QR code, rendered so every module is a whole number of screen pixels.
            float target = 380;
            string url = "http://" + F.RemoteUrl + "/";
            var modules = F.RemoteUrl.Contains(":") ? Qr.Encode(url) : null;
            if (modules != null)
            {
                int n = modules.GetLength(0) + 8;
                int px = Math.Max(2, (int)(target * F.ViewScale / n));
                string key = url + "@" + px;
                if (key != qrKey)
                {
                    if (qrBmp != null) qrBmp.Dispose();
                    qrBmp = Qr.Render(modules, px, Color.Black, Color.White);
                    qrKey = key;
                }
                float vsize = qrBmp.Width / F.ViewScale;
                float qx = card.X + (card.Width - vsize) / 2, qy = card.Y + 180;
                Ui.FillRound(g, Color.White, new RectangleF(qx - 6, qy - 6, vsize + 12, vsize + 12), 16);
                Gfx.Blit(g, qrBmp, qx, qy);
                float y = qy + vsize + 30;
                Ui.Text(g, "or open", Ui.F(18), Ui.Dim, new RectangleF(card.X + 40, y, 440, 26), StringAlignment.Center);
                Ui.Text(g, F.RemoteUrl, Ui.F(30, 2), Ui.Accent, new RectangleF(card.X + 20, y + 26, 480, 44), StringAlignment.Center);
                Ui.Text(g, "Pairing code", Ui.F(18), Ui.Dim, new RectangleF(card.X + 40, y + 88, 440, 26), StringAlignment.Center);
                Ui.Text(g, F.Cfg.Code, Ui.F(56, 2), Ui.Ink, new RectangleF(card.X + 40, y + 110, 440, 72), StringAlignment.Center);
            }
            else
            {
                Ui.Text(g, "Waiting for a network connection…", Ui.F(24), Ui.Ink, new RectangleF(card.X + 40, card.Y + 300, 440, 80), StringAlignment.Center, StringAlignment.Near, true);
            }
        }

        public override void Act(Act a)
        {
            switch (a)
            {
                case Theater.Act.Up: sel = Math.Max(0, sel - 1); break;
                case Theater.Act.Down: sel = Math.Min(items.Count - 1, sel + 1); break;
                case Theater.Act.Ok: KeepVisible(); items[sel].Run(); return;
                case Theater.Act.Back: F.Pop(); return;
                case Theater.Act.Home: F.GoHome(); return;
                default: return;
            }
            KeepVisible();
            F.Kick();
        }

        int Hit(PointF p)
        {
            if (p.Y < ListTop || p.Y > ListBottom) return -1;
            Layout();
            foreach (var kv in rects)
            {
                var r = kv.Value;
                r.Y -= scroll;
                if (r.Contains(p)) return kv.Key;
            }
            return -1;
        }

        public override void Move(PointF p)
        {
            int i = Hit(p);
            if (i >= 0 && i != sel) { sel = i; F.Kick(); }
        }

        public override void Click(PointF p)
        {
            int i = Hit(p);
            if (i >= 0) { sel = i; items[i].Run(); }
        }

        public override void Wheel(int delta)
        {
            float max = Math.Max(0, Layout() - (ListBottom - ListTop));
            scroll = Math.Max(0, Math.Min(max, scroll - delta * 0.8f));
            F.Kick();
        }
    }

    // Live view of the hardware: processor, graphics, memory, display, playback and network.
    class InfoScreen : Screen
    {
        Wifi.Status wifi;

        public InfoScreen(TheaterForm f) : base(f)
        {
            F.Stats.Start();
            Task.Run(() => Wifi.Get()).ContinueWith(t => F.BeginInvoke((Action)(() => { if (!t.IsFaulted) wifi = t.Result; F.Kick(); })));
        }

        public override string Name { get { return "info"; } }
        public override string FocusTitle { get { return "System info"; } }
        public override void Leave() { F.Stats.Stop(); }
        public bool Live { get { return true; } }

        static string Gb(long bytes) { return (bytes / 1073741824.0).ToString(bytes >= 10L << 30 ? "0" : "0.0") + " GB"; }

        void Card(Graphics g, RectangleF r, string icon, string title, List<Tuple<string, string, float>> lines)
        {
            Ui.FillRound(g, Ui.Panel, r, 18);
            Ui.Text(g, icon, Ui.F(26, 9), Ui.Accent, new RectangleF(r.X + 34, r.Y + 30, 40, 40));
            Ui.Text(g, title, Ui.F(26, 2), Ui.Ink, new RectangleF(r.X + 80, r.Y + 28, r.Width - 110, 40));
            float y = r.Y + 88;
            foreach (var l in lines)
            {
                Ui.Text(g, l.Item1, Ui.F(19), Ui.Dim, new RectangleF(r.X + 34, y, 260, 30));
                Ui.Text(g, l.Item2, Ui.F(21, 1), Ui.Ink, new RectangleF(r.X + 280, y - 1, r.Width - 314, 30), StringAlignment.Far);
                y += 36;
                if (l.Item3 >= 0)
                {
                    Ui.FillRound(g, Ui.Panel2, new RectangleF(r.X + 34, y - 4, r.Width - 68, 10), 5);
                    var col = l.Item3 > 85 ? Color.FromArgb(240, 90, 80) : Ui.Accent;
                    Ui.FillRound(g, col, new RectangleF(r.X + 34, y - 4, (r.Width - 68) * Math.Min(100, l.Item3) / 100f, 10), 5);
                    y += 22;
                }
            }
        }

        static Tuple<string, string, float> L(string a, string b, float bar = -1) { return Tuple.Create(a, b, bar); }

        public override void Draw(Graphics g)
        {
            var s = F.Stats;
            Ui.Text(g, "System info", Ui.F(52, 2), Ui.Ink, new RectangleF(104, 44, 1000, 76), StringAlignment.Near, StringAlignment.Center);
            DrawClock(g);

            var cpu = new List<Tuple<string, string, float>> {
                L("Processor", s.CpuName == "" ? "…" : s.CpuName),
                L("Cores", s.Cores > 0 ? s.Cores + " cores · " + s.Threads + " threads" : "…"),
                L("CPU usage (whole PC)", s.CpuPercent >= 0 ? s.CpuPercent.ToString("0") + "%" : "…", s.CpuPercent >= 0 ? s.CpuPercent : 0),
                L(AppInfo.Name, s.AppCpuPercent >= 0 ? s.AppCpuPercent.ToString("0.0") + "% CPU" : "…"),
            };
            var mem = new List<Tuple<string, string, float>> {
                L("Installed", s.RamTotal > 0 ? Gb(s.RamTotal) : "…"),
                L("In use", s.RamTotal > 0 ? Gb(s.RamUsed) + " (" + (100.0 * s.RamUsed / s.RamTotal).ToString("0") + "%)" : "…", s.RamTotal > 0 ? (float)(100.0 * s.RamUsed / s.RamTotal) : 0),
                L(AppInfo.Name, (s.AppMemory >> 20) + " MB"),
                L("VLC (video engine)", s.VlcMemory > 0 ? (s.VlcMemory >> 20) + " MB" : "not running"),
            };
            var gpu = new List<Tuple<string, string, float>>();
            if (s.Gpus.Count == 0) gpu.Add(L("Graphics", "…"));
            foreach (var gp in s.Gpus) gpu.Add(L("Graphics", gp.Item1 + (gp.Item2 > 0 ? "  ·  " + Gb(gp.Item2) : "")));
            gpu.Add(L("3D / drawing load", s.Gpu3dPercent >= 0 ? s.Gpu3dPercent.ToString("0") + "%" : "n/a", Math.Max(0, s.Gpu3dPercent)));
            gpu.Add(L("Video decoder load", s.GpuVideoPercent >= 0 ? s.GpuVideoPercent.ToString("0") + "%" : "n/a", Math.Max(0, s.GpuVideoPercent)));

            int hz;
            var cur = F.CurrentResolution(out hz);
            var disp = new List<Tuple<string, string, float>> {
                L("Screen", cur.Width + " × " + cur.Height + (hz > 1 ? " at " + hz + " Hz" : "")),
                L("Resolution setting", F.ResolutionLabel),
                L("Animations", F.AnimationLabel),
                L("Hardware acceleration", SettingsScreen.HwLabel(F.Cfg.HwDecode)),
                L("Video renderer", SettingsScreen.RendererLabel(F.Cfg.Renderer)),
            };
            var net = new List<Tuple<string, string, float>> {
                L("Wi-Fi", wifi == null ? "…" : wifi.Connected ? wifi.Ssid + (wifi.Signal != "" ? " · " + wifi.Signal : "") : "Not connected"),
                L("Phone remote", F.RemoteUrl),
                L("Start with Windows", StartWithWindowsSafe() ? "On" : "Off"),
            };

            Card(g, new RectangleF(104, 150, 840, 330), "", "Processor", cpu);
            Card(g, new RectangleF(976, 150, 840, 330), "", "Graphics", gpu);
            Card(g, new RectangleF(104, 506, 840, 300), "", "Memory", mem);
            Card(g, new RectangleF(976, 506, 840, 300), "", "Display & playback", disp);
            Card(g, new RectangleF(104, 832, 1712, 168), "", "Network", new List<Tuple<string, string, float>> { net[0] });
            Ui.Text(g, "Phone remote  " + net[1].Item2 + "      Start with Windows  " + net[2].Item2, Ui.F(19), Ui.Dim, new RectangleF(1000, 864, 790, 30), StringAlignment.Far);
            Ui.Text(g, "Updates every second  ·  Esc = back", Ui.F(18), Ui.Dim, new RectangleF(104, 1022, 1700, 30));
        }

        static bool StartWithWindowsSafe() { try { return StartWithWindows.Enabled; } catch { return false; } }

        public override void Act(Act a)
        {
            if (a == Theater.Act.Back || a == Theater.Act.Info) F.Pop();
            else if (a == Theater.Act.Home) F.GoHome();
        }
    }

    // A modal choice box drawn over the current screen.
    class Dialog
    {
        public string Title, Message, Big;
        public string[] Options;
        public int Sel;
        public Action<int> Done;
        public DateTime? Expires;      // when set, the dialog cancels itself at this time
        public bool Countdown = true;  // show the countdown line
        public string CountdownFormat = "Going back automatically in {0} seconds.";
        public Bitmap Image;           // drawn 1:1 in screen pixels (used for the QR code)
        public float ImageScale = 1;   // screen pixels per canvas unit when Image was made
        RectangleF[] optRects = new RectangleF[0];

        public void Draw(Graphics g)
        {
            using (var dim = new SolidBrush(Color.FromArgb(185, 0, 0, 0))) g.FillRectangle(dim, -2000, -2000, 6000, 6000);
            float w = 960;
            string msg = Message ?? "";
            if (Expires.HasValue && Countdown) msg += (msg.Length > 0 ? "\n\n" : "") + string.Format(CountdownFormat, Math.Max(0, (int)Math.Ceiling((Expires.Value - DateTime.Now).TotalSeconds)));
            int cols = Cols;
            if (cols == 2) w = 1200;
            var msgSize = g.MeasureString(msg, Ui.F(24), (int)(w - 100));
            float bigH = Big != null ? 130 : 0;
            float imgW = Image != null ? Image.Width / ImageScale : 0, imgH = Image != null ? Image.Height / ImageScale + 40 : 0;
            bigH += imgH;
            float optH = cols == 2 ? 54 : 62, gap = 10;
            int rowsN = (Options.Length + cols - 1) / cols;
            float h = 60 + 56 + msgSize.Height + bigH + 36 + rowsN * (optH + gap) + 30;
            var r = new RectangleF((1920 - w) / 2, Math.Max(20, (1080 - h) / 2), w, h);
            Ui.FillRound(g, Ui.Panel, r, 22);
            Ui.Text(g, Title, Ui.F(38, 2), Ui.Ink, new RectangleF(r.X + 50, r.Y + 44, w - 100, 56));
            Ui.Text(g, msg, Ui.F(24), Color.FromArgb(200, 204, 214), new RectangleF(r.X + 50, r.Y + 110, w - 100, msgSize.Height + 10), StringAlignment.Near, StringAlignment.Near, true);
            float y = r.Y + 110 + msgSize.Height + 20;
            if (Image != null)
            {
                float ix = r.X + (w - imgW) / 2;
                Ui.FillRound(g, Color.White, new RectangleF(ix - 8, y + 4, imgW + 16, imgW + 16), 18);
                Gfx.Blit(g, Image, ix, y + 12);
                y += imgH;
            }
            if (Big != null)
            {
                Ui.Text(g, Big, Ui.F(104, 2), Ui.Accent, new RectangleF(r.X, y, w, 130), StringAlignment.Center, StringAlignment.Center);
                y += 130;
            }
            y += 10;
            optRects = new RectangleF[Options.Length];
            float colW = (w - 100 - (cols - 1) * 16) / cols;
            for (int i = 0; i < Options.Length; i++)
            {
                int col = i % cols, rowI = i / cols;
                var o = new RectangleF(r.X + 50 + col * (colW + 16), y + rowI * (optH + gap), colW, optH);
                optRects[i] = o;
                bool on = i == Sel;
                Ui.FillRound(g, on ? Ui.Ink : Ui.Panel2, o, optH / 2);
                Ui.Text(g, Options[i], Ui.F(cols == 2 ? 21 : 23, 1), on ? Ui.Bg : Ui.Ink, new RectangleF(o.X + 24, o.Y, o.Width - 48, o.Height),
                    cols == 2 ? StringAlignment.Near : StringAlignment.Center, StringAlignment.Center);
            }
        }

        int Cols { get { return Options.Length > 8 ? 2 : 1; } }

        // Returns true when the dialog is finished.
        public bool Act(Act a, out int result)
        {
            result = -1;
            int n = Options.Length, cols = Cols;
            switch (a)
            {
                case Theater.Act.Up: Sel = Sel - cols >= 0 ? Sel - cols : (cols == 1 ? 0 : Sel); return false;
                case Theater.Act.Down: Sel = Sel + cols < n ? Sel + cols : n - 1; return false;
                case Theater.Act.Left: Sel = cols == 1 ? Math.Max(0, Sel - 1) : (Sel % 2 == 1 ? Sel - 1 : Sel); return false;
                case Theater.Act.Right: Sel = cols == 1 ? Math.Min(n - 1, Sel + 1) : (Sel % 2 == 0 && Sel + 1 < n ? Sel + 1 : Sel); return false;
                case Theater.Act.Ok: case Theater.Act.PlayPause: result = Sel; return true;
                case Theater.Act.Back: case Theater.Act.Home: return true;
            }
            return false;
        }

        public int Hit(PointF p)
        {
            for (int i = 0; i < optRects.Length; i++) if (optRects[i].Contains(p)) return i;
            return -1;
        }
    }
}
